using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WzComparerR2.Headless.Media
{
    public interface IImageSearchCacheProvider
    {
        bool TryReadValid(
            string cacheDirectory,
            string inputPath,
            ImageSearchOptions options,
            out ImageSearchCacheFileDto cache,
            out string cachePath);

        string Write(string cacheDirectory, ImageSearchCacheFileDto cache);
    }

    public sealed class FileImageSearchCacheProvider : IImageSearchCacheProvider
    {
        public static readonly FileImageSearchCacheProvider Instance = new FileImageSearchCacheProvider();

        private FileImageSearchCacheProvider()
        {
        }

        public bool TryReadValid(
            string cacheDirectory,
            string inputPath,
            ImageSearchOptions options,
            out ImageSearchCacheFileDto cache,
            out string cachePath)
        {
            return ImageSearchCacheStore.TryReadValid(cacheDirectory, inputPath, options, out cache, out cachePath);
        }

        public string Write(string cacheDirectory, ImageSearchCacheFileDto cache)
        {
            return ImageSearchCacheStore.Write(cacheDirectory, cache);
        }
    }

    public sealed class ImageSearchCacheFileDto
    {
        public int Version { get; set; }
        public string Method { get; set; }
        public int MinAlpha { get; set; }
        public string InputPath { get; set; }
        public string RootPath { get; set; }
        public string Scope { get; set; }
        public bool IncludeVideo { get; set; }
        public int MaxVideoFrames { get; set; }
        public DateTime CreatedUtc { get; set; }
        public ImageSearchInputStampDto InputStamp { get; set; }
        public List<ImageSearchIndexItemDto> Items { get; set; }

        internal ImageSearchCacheRuntimeIndex RuntimeIndex { get; set; }
    }

    public sealed class ImageSearchInputStampDto
    {
        public string Kind { get; set; }
        public int FileCount { get; set; }
        public long TotalBytes { get; set; }
        public long LastWriteUtcTicks { get; set; }
    }

    public sealed class ImageSearchIndexItemDto
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public int? Page { get; set; }
        public string VideoPath { get; set; }
        public int? FrameIndex { get; set; }
        public int? FrameCount { get; set; }
        public double? FrameDelayMs { get; set; }
        public double? FrameStartMs { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; }
        public int Pages { get; set; }
        public List<ImageSearchFingerprintDto> Fingerprints { get; set; }
    }

    public sealed class ImageSearchFingerprintDto
    {
        public string Region { get; set; }
        public ulong Hash { get; set; }
        public ulong DHash { get; set; }
        public ulong EdgeHash { get; set; }
        public double AverageR { get; set; }
        public double AverageG { get; set; }
        public double AverageB { get; set; }
        public double AspectRatio { get; set; }
        public double AlphaCoverage { get; set; }
    }

    internal sealed class ImageSearchCacheRuntimeIndex
    {
        private const int SizeBucketQuantum = 16;
        private const double AspectBucketStep = 0.25;
        private const double AlphaBucketStep = 0.125;

        private ImageSearchCacheRuntimeIndex(List<ImageSearchIndexItemDto> items)
        {
            this.ItemCount = items == null ? 0 : items.Count;
            this.Buckets = BuildBuckets(items);
        }

        public int ItemCount { get; private set; }
        public IReadOnlyList<ImageSearchCacheRuntimeBucket> Buckets { get; private set; }

        public static ImageSearchCacheRuntimeIndex GetOrCreate(ImageSearchCacheFileDto cache)
        {
            if (cache == null)
            {
                return new ImageSearchCacheRuntimeIndex(null);
            }

            lock (cache)
            {
                if (cache.RuntimeIndex == null)
                {
                    cache.RuntimeIndex = new ImageSearchCacheRuntimeIndex(cache.Items);
                }

                return cache.RuntimeIndex;
            }
        }

        public IEnumerable<ImageSearchCacheRuntimeBucket> EnumerateScoringBuckets(
            int queryWidth,
            int queryHeight,
            ImageSearchOptions options,
            ImageSearchCacheRuntimeIndexStats stats)
        {
            bool useSizePrefilter = ShouldUseSizePrefilter(queryWidth, queryHeight, options);
            double minWidth = useSizePrefilter ? queryWidth * options.MinSizeRatio : 0;
            double minHeight = useSizePrefilter ? queryHeight * options.MinSizeRatio : 0;
            foreach (ImageSearchCacheRuntimeBucket bucket in this.Buckets)
            {
                if (useSizePrefilter && (bucket.MaxWidth < minWidth || bucket.MaxHeight < minHeight))
                {
                    stats.SizeBucketPrefilteredImageCount += bucket.ItemCount;
                    continue;
                }

                stats.ScoringBucketCount++;
                yield return bucket;
            }
        }

        private static IReadOnlyList<ImageSearchCacheRuntimeBucket> BuildBuckets(List<ImageSearchIndexItemDto> items)
        {
            if (items == null || items.Count == 0)
            {
                return Array.Empty<ImageSearchCacheRuntimeBucket>();
            }

            var builders = new Dictionary<ImageSearchCacheRuntimeBucketKey, ImageSearchCacheRuntimeBucketBuilder>();
            foreach (ImageSearchIndexItemDto item in items)
            {
                if (item == null)
                {
                    continue;
                }

                ImageSearchFingerprintDto primary = GetPrimaryFingerprint(item);
                var key = new ImageSearchCacheRuntimeBucketKey(
                    QuantizeSize(item.Width),
                    QuantizeSize(item.Height),
                    QuantizeAspect(primary == null ? GetItemAspectRatio(item) : primary.AspectRatio),
                    QuantizeAlpha(primary == null ? 1.0 : primary.AlphaCoverage));

                ImageSearchCacheRuntimeBucketBuilder builder;
                if (!builders.TryGetValue(key, out builder))
                {
                    builder = new ImageSearchCacheRuntimeBucketBuilder(key);
                    builders.Add(key, builder);
                }

                builder.Add(item);
            }

            return builders.Values
                .Select(builder => builder.Build())
                .OrderBy(bucket => bucket.Key.WidthBucket)
                .ThenBy(bucket => bucket.Key.HeightBucket)
                .ThenBy(bucket => bucket.Key.AspectBucket)
                .ThenBy(bucket => bucket.Key.AlphaBucket)
                .ToList();
        }

        private static bool ShouldUseSizePrefilter(int queryWidth, int queryHeight, ImageSearchOptions options)
        {
            if (options == null || options.NoSizePrefilter)
            {
                return false;
            }
            return queryWidth >= 128 || queryHeight >= 128;
        }

        private static ImageSearchFingerprintDto GetPrimaryFingerprint(ImageSearchIndexItemDto item)
        {
            if (item == null || item.Fingerprints == null || item.Fingerprints.Count == 0)
            {
                return null;
            }

            ImageSearchFingerprintDto full = item.Fingerprints.FirstOrDefault(fingerprint => string.Equals(fingerprint.Region, "full", StringComparison.OrdinalIgnoreCase));
            return full ?? item.Fingerprints[0];
        }

        private static int QuantizeSize(int value)
        {
            return Math.Max(0, value) / SizeBucketQuantum;
        }

        private static int QuantizeAspect(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                return 0;
            }

            return (int)Math.Floor(Math.Min(value, 16.0) / AspectBucketStep);
        }

        private static int QuantizeAlpha(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            double clamped = Math.Max(0, Math.Min(1.0, value));
            return (int)Math.Floor(clamped / AlphaBucketStep);
        }

        private static double GetItemAspectRatio(ImageSearchIndexItemDto item)
        {
            if (item == null || item.Height <= 0)
            {
                return 1.0;
            }

            return (double)Math.Max(1, item.Width) / item.Height;
        }
    }

    internal sealed class ImageSearchCacheRuntimeBucket
    {
        public ImageSearchCacheRuntimeBucketKey Key { get; set; }
        public IReadOnlyList<ImageSearchIndexItemDto> Items { get; set; }
        public int ItemCount { get; set; }
        public int MaxWidth { get; set; }
        public int MaxHeight { get; set; }
        public double MinAspectRatio { get; set; }
        public double MaxAspectRatio { get; set; }
        public double MinAlphaCoverage { get; set; }
        public double MaxAlphaCoverage { get; set; }
    }

    internal sealed class ImageSearchCacheRuntimeIndexStats
    {
        public int SizeBucketPrefilteredImageCount { get; set; }
        public int ShapeBucketPrefilteredImageCount { get; set; }
        public int ScoringBucketCount { get; set; }
    }

    internal readonly struct ImageSearchCacheRuntimeBucketKey : IEquatable<ImageSearchCacheRuntimeBucketKey>
    {
        public ImageSearchCacheRuntimeBucketKey(int widthBucket, int heightBucket, int aspectBucket, int alphaBucket)
        {
            this.WidthBucket = widthBucket;
            this.HeightBucket = heightBucket;
            this.AspectBucket = aspectBucket;
            this.AlphaBucket = alphaBucket;
        }

        public int WidthBucket { get; }
        public int HeightBucket { get; }
        public int AspectBucket { get; }
        public int AlphaBucket { get; }

        public bool Equals(ImageSearchCacheRuntimeBucketKey other)
        {
            return this.WidthBucket == other.WidthBucket
                && this.HeightBucket == other.HeightBucket
                && this.AspectBucket == other.AspectBucket
                && this.AlphaBucket == other.AlphaBucket;
        }

        public override bool Equals(object obj)
        {
            return obj is ImageSearchCacheRuntimeBucketKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + this.WidthBucket;
                hash = hash * 31 + this.HeightBucket;
                hash = hash * 31 + this.AspectBucket;
                hash = hash * 31 + this.AlphaBucket;
                return hash;
            }
        }
    }

    internal sealed class ImageSearchCacheRuntimeBucketBuilder
    {
        private readonly List<ImageSearchIndexItemDto> items = new List<ImageSearchIndexItemDto>();
        private bool hasShape;
        private double minAspectRatio = double.PositiveInfinity;
        private double maxAspectRatio = double.NegativeInfinity;
        private double minAlphaCoverage = double.PositiveInfinity;
        private double maxAlphaCoverage = double.NegativeInfinity;

        public ImageSearchCacheRuntimeBucketBuilder(ImageSearchCacheRuntimeBucketKey key)
        {
            this.Key = key;
        }

        public ImageSearchCacheRuntimeBucketKey Key { get; }
        public int MaxWidth { get; private set; }
        public int MaxHeight { get; private set; }

        public void Add(ImageSearchIndexItemDto item)
        {
            if (item == null)
            {
                return;
            }

            this.items.Add(item);
            this.MaxWidth = Math.Max(this.MaxWidth, item.Width);
            this.MaxHeight = Math.Max(this.MaxHeight, item.Height);
            if (item.Fingerprints != null)
            {
                foreach (ImageSearchFingerprintDto fingerprint in item.Fingerprints)
                {
                    if (fingerprint == null)
                    {
                        continue;
                    }

                    AddShape(fingerprint.AspectRatio, fingerprint.AlphaCoverage);
                }
            }

            if (!this.hasShape)
            {
                AddShape(item.Height <= 0 ? 1.0 : (double)Math.Max(1, item.Width) / item.Height, 1.0);
            }
        }

        public ImageSearchCacheRuntimeBucket Build()
        {
            return new ImageSearchCacheRuntimeBucket
            {
                Key = this.Key,
                Items = this.items,
                ItemCount = this.items.Count,
                MaxWidth = this.MaxWidth,
                MaxHeight = this.MaxHeight,
                MinAspectRatio = this.hasShape ? this.minAspectRatio : 0,
                MaxAspectRatio = this.hasShape ? this.maxAspectRatio : double.PositiveInfinity,
                MinAlphaCoverage = this.hasShape ? this.minAlphaCoverage : 0,
                MaxAlphaCoverage = this.hasShape ? this.maxAlphaCoverage : 1
            };
        }

        private void AddShape(double aspectRatio, double alphaCoverage)
        {
            if (double.IsNaN(aspectRatio) || double.IsInfinity(aspectRatio) || aspectRatio <= 0)
            {
                aspectRatio = 1.0;
            }
            if (double.IsNaN(alphaCoverage) || double.IsInfinity(alphaCoverage))
            {
                alphaCoverage = 1.0;
            }

            alphaCoverage = Math.Max(0, Math.Min(1.0, alphaCoverage));
            this.hasShape = true;
            this.minAspectRatio = Math.Min(this.minAspectRatio, aspectRatio);
            this.maxAspectRatio = Math.Max(this.maxAspectRatio, aspectRatio);
            this.minAlphaCoverage = Math.Min(this.minAlphaCoverage, alphaCoverage);
            this.maxAlphaCoverage = Math.Max(this.maxAlphaCoverage, alphaCoverage);
        }
    }

    public static class ImageSearchCacheStore
    {
        public const int CurrentVersion = 3;
        public const string Method = "phash-dhash-edge-alpha-crop-region-color-cache-v3";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        public static string GetDefaultCacheDirectory()
        {
            string root;
            if (OperatingSystem.IsWindows())
            {
                root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }
            else if (OperatingSystem.IsMacOS())
            {
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches");
            }
            else
            {
                root = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                if (string.IsNullOrWhiteSpace(root))
                {
                    root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
                }
            }

            return Path.Combine(root, "wcr2", "image-search");
        }

        public static string GetCachePath(string cacheDirectory, string inputPath, ImageSearchOptions options)
        {
            return GetCachePath(cacheDirectory, inputPath, options.MinAlpha, options.IncludeVideo, options.MaxVideoFrames);
        }

        private static string GetCachePath(string cacheDirectory, string inputPath, int minAlpha, bool includeVideo, int maxVideoFrames)
        {
            string fullInputPath = Path.GetFullPath(inputPath);
            string key = fullInputPath.ToLowerInvariant()
                + "\n" + Method
                + "\nmin-alpha=" + minAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (includeVideo)
            {
                key += "\ninclude-video=1"
                    + "\nmax-video-frames=" + maxVideoFrames.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            string hash = ComputeSha256Hex(Encoding.UTF8.GetBytes(key));
            return Path.Combine(Path.GetFullPath(cacheDirectory), hash + ".json.gz");
        }

        public static bool TryReadValid(string cacheDirectory, string inputPath, ImageSearchOptions options, out ImageSearchCacheFileDto cache, out string cachePath)
        {
            cachePath = GetCachePath(cacheDirectory, inputPath, options);
            cache = null;
            string binaryCachePath = ImageSearchBinaryCacheStore.GetCachePath(cachePath);
            if (ImageSearchBinaryCacheStore.TryReadValid(binaryCachePath, inputPath, options, out cache))
            {
                return true;
            }

            if (!File.Exists(cachePath))
            {
                return false;
            }

            ImageSearchCacheFileDto loaded;
            try
            {
                using (var stream = File.OpenRead(cachePath))
                using (var gzip = new GZipStream(stream, CompressionMode.Decompress))
                {
                    loaded = JsonSerializer.Deserialize<ImageSearchCacheFileDto>(gzip);
                }
            }
            catch
            {
                return false;
            }

            if (!IsValid(loaded, inputPath, options))
            {
                return false;
            }

            cache = loaded;
            ImageSearchBinaryCacheStore.TryWrite(binaryCachePath, loaded);
            return true;
        }

        public static bool IsValid(ImageSearchCacheFileDto cache, string inputPath, ImageSearchOptions options)
        {
            if (cache == null
                || options == null
                || cache.Version != CurrentVersion
                || !string.Equals(cache.Method, Method, StringComparison.Ordinal)
                || cache.MinAlpha != options.MinAlpha
                || cache.IncludeVideo != options.IncludeVideo
                || (options.IncludeVideo && cache.MaxVideoFrames != options.MaxVideoFrames)
                || cache.InputStamp == null)
            {
                return false;
            }

            return options.TrustCache || StampsEqual(cache.InputStamp, CreateStamp(inputPath));
        }

        public static string Write(string cacheDirectory, ImageSearchCacheFileDto cache)
        {
            string cachePath = GetCachePath(cacheDirectory, cache.InputPath, cache.MinAlpha, cache.IncludeVideo, cache.MaxVideoFrames);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            using (var stream = File.Create(cachePath))
            using (var gzip = new GZipStream(stream, CompressionLevel.Optimal))
            {
                JsonSerializer.Serialize(gzip, cache, JsonOptions);
            }
            ImageSearchBinaryCacheStore.TryWrite(ImageSearchBinaryCacheStore.GetCachePath(cachePath), cache);
            return cachePath;
        }

        public static ImageSearchInputStampDto CreateStamp(string inputPath)
        {
            string fullInputPath = Path.GetFullPath(inputPath);
            if (File.Exists(fullInputPath))
            {
                var info = new FileInfo(fullInputPath);
                return new ImageSearchInputStampDto
                {
                    Kind = "file",
                    FileCount = 1,
                    TotalBytes = info.Length,
                    LastWriteUtcTicks = info.LastWriteTimeUtc.Ticks
                };
            }

            if (!Directory.Exists(fullInputPath))
            {
                throw new FileNotFoundException("Input path not found: " + inputPath);
            }

            int fileCount = 0;
            long totalBytes = 0;
            long lastWriteTicks = 0;
            foreach (string file in Directory.EnumerateFiles(fullInputPath, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                fileCount++;
                totalBytes += info.Length;
                if (info.LastWriteTimeUtc.Ticks > lastWriteTicks)
                {
                    lastWriteTicks = info.LastWriteTimeUtc.Ticks;
                }
            }

            return new ImageSearchInputStampDto
            {
                Kind = "directory",
                FileCount = fileCount,
                TotalBytes = totalBytes,
                LastWriteUtcTicks = lastWriteTicks
            };
        }

        private static bool StampsEqual(ImageSearchInputStampDto left, ImageSearchInputStampDto right)
        {
            return string.Equals(left.Kind, right.Kind, StringComparison.Ordinal)
                && left.FileCount == right.FileCount
                && left.TotalBytes == right.TotalBytes
                && left.LastWriteUtcTicks == right.LastWriteUtcTicks;
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

    }
}
