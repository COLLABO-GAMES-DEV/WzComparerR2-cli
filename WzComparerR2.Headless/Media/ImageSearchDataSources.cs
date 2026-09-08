using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WzComparerR2.Headless.Media
{
    public sealed class ImageSearchScanRoot
    {
        public string InputPath { get; set; }
        public string Scope { get; set; }
        public bool IncludeVideo { get; set; }
    }

    public static class ImageSearchDataSources
    {
        private static readonly string[] DefaultScopeOrder = new[]
        {
            "ui",
            "item",
            "skill",
            "effect",
            "character",
            "mob",
            "npc",
            "map",
            "etc",
            "quest",
            "reactor",
            "morph"
        };

        public static IReadOnlyList<ImageSearchScanRoot> FromDataDirectory(string dataDirectory, string scope)
        {
            return FromDataDirectory(dataDirectory, scope, includeVideo: false);
        }

        public static IReadOnlyList<ImageSearchScanRoot> FromDataDirectory(string dataDirectory, string scope, bool includeVideo)
        {
            if (string.IsNullOrWhiteSpace(dataDirectory))
            {
                return Array.Empty<ImageSearchScanRoot>();
            }

            string fullDataDirectory = Path.GetFullPath(dataDirectory);
            if (!Directory.Exists(fullDataDirectory))
            {
                throw new DirectoryNotFoundException("Data directory not found: " + dataDirectory);
            }

            var requestedScopes = ParseScopes(scope);
            var roots = new List<ImageSearchScanRoot>();
            foreach (string item in requestedScopes)
            {
                AddScopeRoots(roots, fullDataDirectory, item);
                if (includeVideo)
                {
                    AddScopeVideoRoots(roots, fullDataDirectory, item);
                }
            }

            return roots
                .GroupBy(root => Path.GetFullPath(root.InputPath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(root => ScopeOrder(root.Scope))
                .ThenBy(root => root.InputPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static IReadOnlyList<string> ParseScopes(string scope)
        {
            if (string.IsNullOrWhiteSpace(scope) || string.Equals(scope, "auto", StringComparison.OrdinalIgnoreCase) || string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultScopeOrder;
            }

            return scope
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim().ToLowerInvariant())
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddScopeRoots(List<ImageSearchScanRoot> roots, string dataDirectory, string scope)
        {
            if (string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase) || string.Equals(scope, "auto", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string item in DefaultScopeOrder)
                {
                    AddScopeRoots(roots, dataDirectory, item);
                }
                return;
            }

            string domainDirectory = Path.Combine(dataDirectory, ScopeToDirectoryName(scope));
            if (Directory.Exists(domainDirectory))
            {
                foreach (string canvasDirectory in Directory.EnumerateDirectories(domainDirectory, "*", SearchOption.AllDirectories)
                    .Where(IsCanvasDirectory))
                {
                    roots.Add(new ImageSearchScanRoot
                    {
                        InputPath = canvasDirectory,
                        Scope = scope,
                        IncludeVideo = false
                    });
                }
            }

            string siblingCanvasDirectory = Path.Combine(dataDirectory, ScopeToDirectoryName(scope) + "_Canvas");
            if (Directory.Exists(siblingCanvasDirectory))
            {
                roots.Add(new ImageSearchScanRoot
                {
                    InputPath = siblingCanvasDirectory,
                    Scope = scope,
                    IncludeVideo = false
                });
            }
        }

        private static void AddScopeVideoRoots(List<ImageSearchScanRoot> roots, string dataDirectory, string scope)
        {
            if (string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase) || string.Equals(scope, "auto", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string item in DefaultScopeOrder)
                {
                    AddScopeVideoRoots(roots, dataDirectory, item);
                }
                return;
            }

            if (!string.Equals(scope, "skill", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(scope, "video", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string packsDirectory = Path.Combine(dataDirectory, "Packs");
            if (Directory.Exists(packsDirectory))
            {
                foreach (string file in Directory.EnumerateFiles(packsDirectory, "Skill*.ms", SearchOption.TopDirectoryOnly))
                {
                    roots.Add(new ImageSearchScanRoot
                    {
                        InputPath = file,
                        Scope = "skill-video",
                        IncludeVideo = true
                    });
                }
            }
        }

        private static bool IsCanvasDirectory(string path)
        {
            string name = Path.GetFileName(path);
            return string.Equals(name, "_Canvas", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_Canvas", StringComparison.OrdinalIgnoreCase);
        }

        private static string ScopeToDirectoryName(string scope)
        {
            switch ((scope ?? string.Empty).ToLowerInvariant())
            {
                case "ui":
                    return "UI";
                case "item":
                    return "Item";
                case "skill":
                    return "Skill";
                case "effect":
                    return "Effect";
                case "character":
                case "chara":
                    return "Character";
                case "mob":
                    return "Mob";
                case "npc":
                    return "Npc";
                case "map":
                    return "Map";
                case "etc":
                    return "Etc";
                case "quest":
                    return "Quest";
                case "reactor":
                    return "Reactor";
                case "morph":
                    return "Morph";
                default:
                    return scope;
            }
        }

        private static int ScopeOrder(string scope)
        {
            for (int i = 0; i < DefaultScopeOrder.Length; i++)
            {
                if (string.Equals(DefaultScopeOrder[i], scope, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return DefaultScopeOrder.Length;
        }
    }
}
