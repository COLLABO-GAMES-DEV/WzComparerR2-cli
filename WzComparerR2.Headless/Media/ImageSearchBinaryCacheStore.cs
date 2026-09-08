using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WzComparerR2.Headless.Media
{
    internal static class ImageSearchBinaryCacheStore
    {
        private const int BinaryCacheVersion = 3;
        private const int MaxBinaryItemCount = 10000000;
        private const int MaxBinaryFingerprintCount = 64;
        private const string BinaryCacheMagic = "WCR2ISB";

        public static string GetCachePath(string jsonCachePath)
        {
            const string JsonGzipExtension = ".json.gz";
            if (jsonCachePath != null && jsonCachePath.EndsWith(JsonGzipExtension, StringComparison.OrdinalIgnoreCase))
            {
                return jsonCachePath.Substring(0, jsonCachePath.Length - JsonGzipExtension.Length) + ".bin.gz";
            }

            return jsonCachePath + ".bin.gz";
        }

        public static bool TryReadValid(
            string binaryCachePath,
            string inputPath,
            ImageSearchOptions options,
            out ImageSearchCacheFileDto cache)
        {
            cache = null;
            if (!File.Exists(binaryCachePath))
            {
                return false;
            }

            try
            {
                using (var stream = File.OpenRead(binaryCachePath))
                using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
                using (var reader = new BinaryReader(gzip, Encoding.UTF8))
                {
                    ImageSearchCacheFileDto loaded = ReadBinary(reader);
                    if (!ImageSearchCacheStore.IsValid(loaded, inputPath, options))
                    {
                        return false;
                    }

                    cache = loaded;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void TryWrite(string binaryCachePath, ImageSearchCacheFileDto cache)
        {
            try
            {
                string directory = Path.GetDirectoryName(binaryCachePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var stream = File.Create(binaryCachePath))
                using (var gzip = new GZipStream(stream, CompressionLevel.Optimal))
                using (var writer = new BinaryWriter(gzip, Encoding.UTF8))
                {
                    WriteBinary(writer, cache);
                }
            }
            catch
            {
                // Binary cache is an optimization. JSON cache remains the compatibility source.
            }
        }

        private static void WriteBinary(BinaryWriter writer, ImageSearchCacheFileDto cache)
        {
            writer.Write(BinaryCacheMagic);
            writer.Write(BinaryCacheVersion);
            writer.Write(cache.Version);
            WriteNullableString(writer, cache.Method);
            writer.Write(cache.MinAlpha);
            writer.Write(cache.IncludeVideo);
            writer.Write(cache.MaxVideoFrames);
            WriteNullableString(writer, cache.InputPath);
            WriteNullableString(writer, cache.RootPath);
            WriteNullableString(writer, cache.Scope);
            writer.Write(cache.CreatedUtc.ToUniversalTime().Ticks);
            WriteStamp(writer, cache.InputStamp);
            WriteItems(writer, cache.Items);
        }

        private static ImageSearchCacheFileDto ReadBinary(BinaryReader reader)
        {
            if (!string.Equals(reader.ReadString(), BinaryCacheMagic, StringComparison.Ordinal)
                || reader.ReadInt32() != BinaryCacheVersion)
            {
                return null;
            }

            return new ImageSearchCacheFileDto
            {
                Version = reader.ReadInt32(),
                Method = ReadNullableString(reader),
                MinAlpha = reader.ReadInt32(),
                IncludeVideo = reader.ReadBoolean(),
                MaxVideoFrames = reader.ReadInt32(),
                InputPath = ReadNullableString(reader),
                RootPath = ReadNullableString(reader),
                Scope = ReadNullableString(reader),
                CreatedUtc = ReadDateTimeUtc(reader.ReadInt64()),
                InputStamp = ReadStamp(reader),
                Items = ReadItems(reader)
            };
        }

        private static void WriteStamp(BinaryWriter writer, ImageSearchInputStampDto stamp)
        {
            writer.Write(stamp != null);
            if (stamp == null)
            {
                return;
            }

            WriteNullableString(writer, stamp.Kind);
            writer.Write(stamp.FileCount);
            writer.Write(stamp.TotalBytes);
            writer.Write(stamp.LastWriteUtcTicks);
        }

        private static ImageSearchInputStampDto ReadStamp(BinaryReader reader)
        {
            if (!reader.ReadBoolean())
            {
                return null;
            }

            return new ImageSearchInputStampDto
            {
                Kind = ReadNullableString(reader),
                FileCount = reader.ReadInt32(),
                TotalBytes = reader.ReadInt64(),
                LastWriteUtcTicks = reader.ReadInt64()
            };
        }

        private static void WriteItems(BinaryWriter writer, List<ImageSearchIndexItemDto> items)
        {
            if (items == null)
            {
                writer.Write(-1);
                return;
            }

            writer.Write(items.Count);
            foreach (ImageSearchIndexItemDto item in items)
            {
                WriteNullableString(writer, item.Name);
                WriteNullableString(writer, item.Path);
                WriteNullableString(writer, item.Type);
                writer.Write(item.Page.HasValue);
                if (item.Page.HasValue)
                {
                    writer.Write(item.Page.Value);
                }

                WriteNullableString(writer, item.VideoPath);
                writer.Write(item.FrameIndex.HasValue);
                if (item.FrameIndex.HasValue)
                {
                    writer.Write(item.FrameIndex.Value);
                }

                writer.Write(item.FrameCount.HasValue);
                if (item.FrameCount.HasValue)
                {
                    writer.Write(item.FrameCount.Value);
                }

                writer.Write(item.FrameDelayMs.HasValue);
                if (item.FrameDelayMs.HasValue)
                {
                    writer.Write(item.FrameDelayMs.Value);
                }

                writer.Write(item.FrameStartMs.HasValue);
                if (item.FrameStartMs.HasValue)
                {
                    writer.Write(item.FrameStartMs.Value);
                }

                writer.Write(item.Width);
                writer.Write(item.Height);
                WriteNullableString(writer, item.Format);
                writer.Write(item.Pages);
                WriteFingerprints(writer, item.Fingerprints);
            }
        }

        private static List<ImageSearchIndexItemDto> ReadItems(BinaryReader reader)
        {
            int count = ReadBoundedCount(reader, MaxBinaryItemCount);
            if (count < 0)
            {
                return null;
            }

            var items = new List<ImageSearchIndexItemDto>(count);
            for (int i = 0; i < count; i++)
            {
                var item = new ImageSearchIndexItemDto
                {
                    Name = ReadNullableString(reader),
                    Path = ReadNullableString(reader),
                    Type = ReadNullableString(reader)
                };

                bool hasPage = reader.ReadBoolean();
                item.Page = hasPage ? reader.ReadInt32() : (int?)null;
                item.VideoPath = ReadNullableString(reader);
                bool hasFrameIndex = reader.ReadBoolean();
                item.FrameIndex = hasFrameIndex ? reader.ReadInt32() : (int?)null;
                bool hasFrameCount = reader.ReadBoolean();
                item.FrameCount = hasFrameCount ? reader.ReadInt32() : (int?)null;
                bool hasFrameDelay = reader.ReadBoolean();
                item.FrameDelayMs = hasFrameDelay ? reader.ReadDouble() : (double?)null;
                bool hasFrameStart = reader.ReadBoolean();
                item.FrameStartMs = hasFrameStart ? reader.ReadDouble() : (double?)null;
                item.Width = reader.ReadInt32();
                item.Height = reader.ReadInt32();
                item.Format = ReadNullableString(reader);
                item.Pages = reader.ReadInt32();
                item.Fingerprints = ReadFingerprints(reader);
                items.Add(item);
            }

            return items;
        }

        private static void WriteFingerprints(BinaryWriter writer, List<ImageSearchFingerprintDto> fingerprints)
        {
            if (fingerprints == null)
            {
                writer.Write(-1);
                return;
            }

            writer.Write(fingerprints.Count);
            foreach (ImageSearchFingerprintDto fingerprint in fingerprints)
            {
                WriteNullableString(writer, fingerprint.Region);
                writer.Write(fingerprint.Hash);
                writer.Write(fingerprint.DHash);
                writer.Write(fingerprint.EdgeHash);
                writer.Write(fingerprint.AverageR);
                writer.Write(fingerprint.AverageG);
                writer.Write(fingerprint.AverageB);
                writer.Write(fingerprint.AspectRatio);
                writer.Write(fingerprint.AlphaCoverage);
            }
        }

        private static List<ImageSearchFingerprintDto> ReadFingerprints(BinaryReader reader)
        {
            int count = ReadBoundedCount(reader, MaxBinaryFingerprintCount);
            if (count < 0)
            {
                return null;
            }

            var fingerprints = new List<ImageSearchFingerprintDto>(count);
            for (int i = 0; i < count; i++)
            {
                fingerprints.Add(new ImageSearchFingerprintDto
                {
                    Region = ReadNullableString(reader),
                    Hash = reader.ReadUInt64(),
                    DHash = reader.ReadUInt64(),
                    EdgeHash = reader.ReadUInt64(),
                    AverageR = reader.ReadDouble(),
                    AverageG = reader.ReadDouble(),
                    AverageB = reader.ReadDouble(),
                    AspectRatio = reader.ReadDouble(),
                    AlphaCoverage = reader.ReadDouble()
                });
            }

            return fingerprints;
        }

        private static void WriteNullableString(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null)
            {
                writer.Write(value);
            }
        }

        private static string ReadNullableString(BinaryReader reader)
        {
            return reader.ReadBoolean() ? reader.ReadString() : null;
        }

        private static int ReadBoundedCount(BinaryReader reader, int maxValue)
        {
            int count = reader.ReadInt32();
            if (count < -1 || count > maxValue)
            {
                throw new InvalidDataException("Invalid image search binary cache count: " + count);
            }

            return count;
        }

        private static DateTime ReadDateTimeUtc(long ticks)
        {
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            {
                return DateTime.MinValue;
            }

            return new DateTime(ticks, DateTimeKind.Utc);
        }
    }
}
