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
    public sealed class ImageSearchCacheFileDto
    {
        public int Version { get; set; }
        public string Method { get; set; }
        public int MinAlpha { get; set; }
        public string InputPath { get; set; }
        public string RootPath { get; set; }
        public string Scope { get; set; }
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
        public double AverageR { get; set; }
        public double AverageG { get; set; }
        public double AverageB { get; set; }
        public double AspectRatio { get; set; }
        public double AlphaCoverage { get; set; }
    }

    public static class ImageSearchCacheStore
    {
        public const int CurrentVersion = 2;
        public const string Method = "phash-alpha-crop-region-color-cache-v2";

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
            return GetCachePath(cacheDirectory, inputPath, options.MinAlpha);
        }

        private static string GetCachePath(string cacheDirectory, string inputPath, int minAlpha)
        {
            string fullInputPath = Path.GetFullPath(inputPath);
            string key = fullInputPath.ToLowerInvariant()
                + "\n" + Method
                + "\nmin-alpha=" + minAlpha.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string hash = ComputeSha256Hex(Encoding.UTF8.GetBytes(key));
            return Path.Combine(Path.GetFullPath(cacheDirectory), hash + ".json.gz");
        }

        public static bool TryReadValid(string cacheDirectory, string inputPath, ImageSearchOptions options, out ImageSearchCacheFileDto cache, out string cachePath)
        {
            cachePath = GetCachePath(cacheDirectory, inputPath, options);
            cache = null;
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

            if (loaded == null
                || loaded.Version != CurrentVersion
                || !string.Equals(loaded.Method, Method, StringComparison.Ordinal)
                || loaded.MinAlpha != options.MinAlpha
                || loaded.InputStamp == null
                || (!options.TrustCache && !StampsEqual(loaded.InputStamp, CreateStamp(inputPath))))
            {
                return false;
            }

            cache = loaded;
            return true;
        }

        public static string Write(string cacheDirectory, ImageSearchCacheFileDto cache)
        {
            string cachePath = GetCachePath(cacheDirectory, cache.InputPath, cache.MinAlpha);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
            using (var stream = File.Create(cachePath))
            using (var gzip = new GZipStream(stream, CompressionLevel.Optimal))
            {
                JsonSerializer.Serialize(gzip, cache, JsonOptions);
            }
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
