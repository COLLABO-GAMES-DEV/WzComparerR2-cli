using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WzComparerR2.Cli
{
    internal sealed class CliConfigStore
    {
        private CliConfigStore(string path, Dictionary<string, string> values)
        {
            Path = path;
            Values = values;
        }

        public string Path { get; private set; }
        public Dictionary<string, string> Values { get; private set; }

        public static CliConfigStore Open(string explicitPath)
        {
            string path = ResolvePath(explicitPath);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (loaded != null)
                    {
                        foreach (var item in loaded)
                        {
                            values[item.Key] = item.Value;
                        }
                    }
                }
                catch (JsonException ex)
                {
                    throw new UsageException("Config file is not valid JSON: " + ex.Message);
                }
            }

            return new CliConfigStore(path, values);
        }

        public void Set(string key, string value)
        {
            ValidateKey(key);
            Values[key] = value ?? string.Empty;
        }

        public bool Unset(string key)
        {
            ValidateKey(key);
            return Values.Remove(key);
        }

        public void Save()
        {
            string directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var sorted = Values
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
            File.WriteAllText(Path, JsonSerializer.Serialize(sorted, ProgramJsonOptions));
        }

        private static string ResolvePath(string explicitPath)
        {
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                return System.IO.Path.GetFullPath(explicitPath);
            }

            string envPath = Environment.GetEnvironmentVariable("WCR2_CLI_CONFIG");
            if (!string.IsNullOrWhiteSpace(envPath))
            {
                return System.IO.Path.GetFullPath(envPath);
            }

            string baseDirectory;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrEmpty(baseDirectory))
                {
                    baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }
                return System.IO.Path.Combine(baseDirectory, "WzComparerR2", "wcr2.config.json");
            }

            baseDirectory = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }
            return System.IO.Path.Combine(baseDirectory, "wzcomparerr2", "wcr2.config.json");
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !Regex.IsMatch(key, @"^[A-Za-z0-9_.:-]+$"))
            {
                throw new UsageException("Config key may contain only letters, numbers, dot, colon, underscore, and dash.");
            }
        }

        private static JsonSerializerOptions ProgramJsonOptions
        {
            get
            {
                return new JsonSerializerOptions
                {
                    WriteIndented = true
                };
            }
        }
    }

    internal sealed class ConfigPathDto
    {
        public string Path { get; set; }
        public bool Exists { get; set; }
        public int Count { get; set; }

        public static ConfigPathDto FromStore(CliConfigStore store)
        {
            return new ConfigPathDto
            {
                Path = store.Path,
                Exists = File.Exists(store.Path),
                Count = store.Values.Count
            };
        }
    }

    internal sealed class ConfigListDto
    {
        public string Path { get; set; }
        public string Profile { get; set; }
        public List<ConfigItemDto> Values { get; set; }

        public static ConfigListDto FromStore(CliConfigStore store, string profile)
        {
            string prefix = string.IsNullOrEmpty(profile) ? null : "profiles." + profile + ".";
            return new ConfigListDto
            {
                Path = store.Path,
                Profile = profile,
                Values = store.Values
                    .Where(item => prefix == null || item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new ConfigItemDto { Key = prefix == null ? item.Key : item.Key.Substring(prefix.Length), Value = item.Value })
                    .ToList()
            };
        }
    }

    internal sealed class ConfigItemDto
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

    internal sealed class ConfigValueDto
    {
        public string Path { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
        public bool Found { get; set; }

        public static ConfigValueDto FromStore(CliConfigStore store, string key)
        {
            string value;
            bool found = store.Values.TryGetValue(key, out value);
            return new ConfigValueDto
            {
                Path = store.Path,
                Key = key,
                Value = value,
                Found = found
            };
        }
    }

    internal sealed class ConfigUnsetDto
    {
        public string Path { get; set; }
        public string Key { get; set; }
        public bool Removed { get; set; }
        public int Count { get; set; }
    }
}
