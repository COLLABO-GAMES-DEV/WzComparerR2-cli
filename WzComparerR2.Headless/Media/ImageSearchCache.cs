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
