using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace WzComparerR2.Cli
{
    internal static class ItemIconPaths
    {
        public static string ResolveItemInput(ParsedArgs args, string dataDir)
        {
            if (args.Positionals.Count > 1 && !string.IsNullOrEmpty(args.Positionals[1]) && !IsHelpLike(args.Positionals[1]))
            {
                string input = Path.GetFullPath(args.Positionals[1]);
                if (Directory.Exists(Path.Combine(input, "Item")))
                {
                    return Path.Combine(input, "Item");
                }
                return input;
            }

            string itemWz = args.GetValue("item-wz");
            if (!string.IsNullOrEmpty(itemWz))
            {
                return itemWz;
            }
            if (!string.IsNullOrEmpty(dataDir))
            {
                return Path.Combine(dataDir, "Item");
            }

            return null;
        }

        public static string ResolveStringInput(ParsedArgs args, string dataDir, string itemInput)
        {
            string stringWz = args.GetValue("string-wz");
            if (!string.IsNullOrEmpty(stringWz))
            {
                return stringWz;
            }
            if (!string.IsNullOrEmpty(dataDir))
            {
                return Path.Combine(dataDir, "String");
            }
            if (!string.IsNullOrEmpty(itemInput))
            {
                string current = Path.GetFullPath(itemInput);
                while (!string.IsNullOrEmpty(current))
                {
                    string candidate = Path.Combine(current, "String");
                    if (Directory.Exists(candidate) || File.Exists(candidate))
                    {
                        return candidate;
                    }

                    string sibling = Path.Combine(Path.GetDirectoryName(current) ?? string.Empty, "String");
                    if (Directory.Exists(sibling) || File.Exists(sibling))
                    {
                        return sibling;
                    }

                    string parent = Path.GetDirectoryName(current);
                    if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    current = parent;
                }
            }
            return null;
        }

        public static string ResolveCanvasInput(ParsedArgs args, string dataDir, string itemInput, string category)
        {
            string canvas = args.GetValue("canvas-wz");
            if (!string.IsNullOrEmpty(canvas))
            {
                return canvas;
            }

            string folder = ToFolderName(category);
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(dataDir))
            {
                candidates.Add(Path.Combine(dataDir, "Item", folder, "_Canvas"));
            }
            if (!string.IsNullOrEmpty(itemInput))
            {
                string full = Path.GetFullPath(itemInput);
                string name = Path.GetFileName(full);
                if (string.Equals(name, "_Canvas", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(full);
                }
                candidates.Add(Path.Combine(full, folder, "_Canvas"));
                candidates.Add(Path.Combine(full, "_Canvas"));
                string parent = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(parent))
                {
                    candidates.Add(Path.Combine(parent, folder, "_Canvas"));
                }
            }

            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate) && (Directory.Exists(candidate) || File.Exists(candidate)))
                {
                    return candidate;
                }
            }

            return candidates.FirstOrDefault();
        }

        public static string NormalizeCategory(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "cash":
                    return "Cash";
                case "consume":
                case "cons":
                    return "Consume";
                case "install":
                case "ins":
                    return "Install";
                case "etc":
                    return "Etc";
                case "pet":
                    return "Pet";
                case "eqp":
                case "equip":
                case "character":
                    return "Eqp";
                default:
                    return value.Trim();
            }
        }

        public static string ToFolderName(string category)
        {
            if (string.Equals(category, "Ins", StringComparison.OrdinalIgnoreCase))
            {
                return "Install";
            }
            return NormalizeCategory(category) ?? category;
        }

        public static string InferCategoryFromId(string id)
        {
            string padded = PadItemId(id);
            if (padded.StartsWith("02", StringComparison.Ordinal))
            {
                return "Consume";
            }
            if (padded.StartsWith("03", StringComparison.Ordinal))
            {
                return "Install";
            }
            if (padded.StartsWith("04", StringComparison.Ordinal))
            {
                return "Etc";
            }
            if (padded.StartsWith("0500", StringComparison.Ordinal))
            {
                return "Pet";
            }
            if (padded.StartsWith("05", StringComparison.Ordinal))
            {
                return "Cash";
            }
            return "Cash";
        }

        public static string CanonicalizeItemName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string text = value.Trim();
            Match duration = Regex.Match(text, @"\d+\s*일", RegexOptions.CultureInvariant);
            string durationKey = duration.Success
                ? Regex.Replace(duration.Value, @"\s+", string.Empty, RegexOptions.CultureInvariant)
                : string.Empty;
            string baseName = Regex.Replace(text, @"[\[\(]\s*\d+\s*일\s*[\]\)]", string.Empty, RegexOptions.CultureInvariant);
            baseName = Regex.Replace(baseName, @"\d+\s*일", string.Empty, RegexOptions.CultureInvariant);
            baseName = Regex.Replace(baseName, @"[\s\[\]\(\)]", string.Empty, RegexOptions.CultureInvariant);
            return baseName + "|" + durationKey;
        }

        public static string PadItemId(string id)
        {
            if (int.TryParse(id, out int numeric))
            {
                return numeric.ToString("d8");
            }
            return id;
        }

        public static List<string> BuildPaddedIdCandidates(string id)
        {
            var candidates = new List<string>();
            AddUnique(candidates, PadItemId(id));
            if (int.TryParse(id, out int numeric))
            {
                foreach (int divisor in new[] { 10, 100, 1000 })
                {
                    int rounded = numeric / divisor * divisor;
                    if (rounded > 0 && rounded != numeric)
                    {
                        AddUnique(candidates, rounded.ToString("d8"));
                    }
                }
            }
            return candidates;
        }

        public static List<string> BuildIconPathCandidates(IEnumerable<string> paddedIds)
        {
            var paths = new List<string>();
            foreach (string candidateId in paddedIds)
            {
                if (string.IsNullOrEmpty(candidateId) || candidateId.Length < 4)
                {
                    continue;
                }

                string group = candidateId.Substring(0, 4);
                AddUnique(paths, group + ".img/" + candidateId + "/info/icon");
                AddUnique(paths, group + ".img/" + candidateId + "/info/iconRaw");
            }
            return paths;
        }

        private static void AddUnique(List<string> values, string value)
        {
            if (!string.IsNullOrEmpty(value) && !values.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
            {
                values.Add(value);
            }
        }

        private static bool IsHelpLike(string arg)
        {
            return string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase);
        }
    }
}
