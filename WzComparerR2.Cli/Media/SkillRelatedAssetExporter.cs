using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal static class SkillRelatedAssetExporter
    {
        private static readonly HashSet<string> ReferenceNodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "action",
            "delay",
            "animation",
            "ani",
            "effect",
            "screen",
            "screen0",
            "screen1",
            "screen2",
            "screen3"
        };

        private static readonly HashSet<string> GenericReferenceValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "icon",
            "effect",
            "effect0",
            "effect1",
            "effect2",
            "hit",
            "special",
            "special0",
            "special1",
            "special2",
            "special3",
            "screen",
            "screen0",
            "screen1",
            "screen2",
            "screen3",
            "tile"
        };

        public static List<SkillRelatedAssetResultDto> Export(
            Wz_Node skillNode,
            string skillId,
            string skillInput,
            string dataInputPath,
            string outputDirectory,
            ParsedArgs args,
            SkillSpriteExportOptions options,
            List<string> diagnostics,
            SkillSpriteExporter.SkillSpriteExportSession session)
        {
            var results = new List<SkillRelatedAssetResultDto>();
            var keys = CollectReferenceKeys(skillNode, options);
            AddReferenceKeysFromMetadataInputs(keys, skillId, skillInput, dataInputPath, args, options, diagnostics, session);
            if (keys.Count == 0)
            {
                diagnostics.Add("No related asset keys were found in action/delay nodes; use --related-key <name> to seed a lookup.");
                return results;
            }

            var inputs = ResolveRelatedInputs(args, skillInput, dataInputPath, options, diagnostics);
            if (inputs.Count == 0)
            {
                diagnostics.Add("No related asset inputs were found; use --data-dir, --related-wz, --effect-wz, or --character-wz.");
                return results;
            }

            diagnostics.Add("Related asset keys: " + string.Join(",", keys));
            var seenMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string input in inputs)
            {
                if (results.Count >= options.MaxRelatedMatches)
                {
                    diagnostics.Add("Related asset matches were truncated to " + options.MaxRelatedMatches + " entries.");
                    break;
                }

                WzLoadContext context = session.LoadCachedContext(input, diagnostics);
                if (context == null)
                {
                    continue;
                }

                foreach (var match in FindMatches(context.Root, keys))
                {
                    if (results.Count >= options.MaxRelatedMatches)
                    {
                        diagnostics.Add("Related asset matches were truncated to " + options.MaxRelatedMatches + " entries.");
                        break;
                    }

                    string matchKey = input + "|" + match.Key + "|" + match.Node.FullPath;
                    if (!seenMatches.Add(matchKey))
                    {
                        continue;
                    }

                    results.Add(ExportMatch(match, input, outputDirectory, skillId));
                }
            }

            if (results.Count == 0)
            {
                diagnostics.Add("No related image assets were resolved from the selected related inputs.");
            }
            return results;
        }

        private static List<string> CollectReferenceKeys(Wz_Node skillNode, SkillSpriteExportOptions options)
        {
            var keys = new List<string>();
            foreach (string key in options.RelatedKeys)
            {
                AddReferenceKey(keys, key);
            }

            foreach (var item in Traverse(skillNode))
            {
                if (!IsUnderReferenceNode(item.Node))
                {
                    continue;
                }

                string value = item.Node.GetValueEx<string>(null);
                if (LooksLikeReferenceKey(value))
                {
                    AddReferenceKey(keys, value);
                }

            }

            return keys;
        }

        private static void AddReferenceKeysFromMetadataInputs(
            List<string> keys,
            string skillId,
            string skillInput,
            string dataInputPath,
            ParsedArgs args,
            SkillSpriteExportOptions options,
            List<string> diagnostics,
            SkillSpriteExporter.SkillSpriteExportSession session)
        {
            string dataDir = ResolveDataDirectory(args.GetValue("data-dir"), skillInput, dataInputPath);
            if (string.IsNullOrEmpty(dataDir))
            {
                return;
            }

            var inputs = new List<string>();
            AddExistingFiles(inputs, Path.Combine(dataDir, "Packs"), "Skill*.ms", options.MaxRelatedInputs);
            if (inputs.Count == 0)
            {
                return;
            }

            var before = keys.Count;
            foreach (string input in inputs)
            {
                WzLoadContext context = session.LoadCachedContext(input, diagnostics);
                if (context == null)
                {
                    continue;
                }

                Wz_Node node = DomainInfoFinder.FindDataNode(context.Root, "skill", skillId);
                if (node != null)
                {
                    foreach (string key in CollectReferenceKeys(node, options))
                    {
                        AddReferenceKey(keys, key);
                    }
                }
            }

            if (keys.Count > before)
            {
                diagnostics.Add("Added related asset keys from Skill*.ms metadata: " + string.Join(",", keys.Skip(before)));
            }
        }

        private static bool IsUnderReferenceNode(Wz_Node node)
        {
            Wz_Node current = node;
            while (current != null)
            {
                if (ReferenceNodeNames.Contains(current.Text))
                {
                    return true;
                }
                current = current.ParentNode;
            }
            return false;
        }

        private static bool LooksLikeReferenceKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.Length < 3 || trimmed.Length > 80)
            {
                return false;
            }
            if (GenericReferenceValues.Contains(trimmed))
            {
                return false;
            }
            if (trimmed.IndexOf('#') >= 0 || trimmed.IndexOf('\\') >= 0 || trimmed.IndexOf('/') >= 0)
            {
                return false;
            }

            return trimmed.Any(char.IsLetter) && trimmed.All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.');
        }

        private static void AddReferenceKey(List<string> keys, string value)
        {
            string key = (value ?? string.Empty).Trim();
            if (key.Length == 0
                || GenericReferenceValues.Contains(key)
                || keys.Any(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            keys.Add(key);
        }

        private static List<string> ResolveRelatedInputs(ParsedArgs args, string skillInput, string dataInputPath, SkillSpriteExportOptions options, List<string> diagnostics)
        {
            var candidates = new List<string>();
            foreach (string input in options.RelatedInputs)
            {
                AddExistingCandidate(candidates, input);
            }

            string dataDir = ResolveDataDirectory(args.GetValue("data-dir"), skillInput, dataInputPath);
            if (!string.IsNullOrEmpty(dataDir))
            {
                AddExistingCandidate(candidates, Path.Combine(dataDir, "Skill", "_Canvas"));
                AddExistingCandidate(candidates, Path.Combine(dataDir, "Effect", "_Canvas"));
                AddExistingCandidate(candidates, Path.Combine(dataDir, "Character", "_Canvas"));
                AddExistingCandidate(candidates, Path.Combine(dataDir, "Character", "Afterimage"));
            }

            if (candidates.Count > options.MaxRelatedInputs)
            {
                diagnostics.Add("Related input list was truncated to " + options.MaxRelatedInputs + " entries.");
                candidates = candidates.Take(options.MaxRelatedInputs).ToList();
            }
            return candidates;
        }

        private static IEnumerable<RelatedNodeMatch> FindMatches(Wz_Node root, List<string> keys)
        {
            foreach (var item in Traverse(root))
            {
                string path = SkillSpriteExportOptions.NormalizePath(item.Node.FullPath);
                foreach (string key in keys)
                {
                    if (!MatchesKey(item.Node, path, key))
                    {
                        continue;
                    }

                    Wz_Node exportRoot = FindImageExportRoot(item.Node);
                    if (exportRoot != null)
                    {
                        yield return new RelatedNodeMatch(key, item.Node, exportRoot);
                    }
                }
            }
        }

        private static bool MatchesKey(Wz_Node node, string normalizedPath, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (string.Equals(node.Text, key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (normalizedPath.IndexOf("/" + key + "/", StringComparison.OrdinalIgnoreCase) >= 0
                || normalizedPath.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string value = node.GetValueEx<string>(null);
            return string.Equals(value, key, StringComparison.OrdinalIgnoreCase);
        }

        private static Wz_Node FindImageExportRoot(Wz_Node matchedNode)
        {
            Wz_Node current = NodePath.ExtractImageNode(matchedNode, true);
            if (ContainsImage(current))
            {
                return current;
            }

            current = matchedNode.ParentNode;
            while (current != null)
            {
                Wz_Node extracted = NodePath.ExtractImageNode(current, true);
                if (ContainsImage(extracted))
                {
                    return extracted;
                }
                current = current.ParentNode;
            }

            return null;
        }

        private static bool ContainsImage(Wz_Node root)
        {
            if (root == null)
            {
                return false;
            }

            foreach (var item in Traverse(root))
            {
                if (item.Node.Value is Wz_Png)
                {
                    return true;
                }
            }
            return false;
        }

        private static SkillRelatedAssetResultDto ExportMatch(RelatedNodeMatch match, string input, string outputDirectory, string skillId)
        {
            string rootPath = SkillSpriteExportOptions.NormalizePath(match.ExportRoot.FullPath);
            string output = Path.Combine(outputDirectory, "related", SanitizeSegment(match.Key), SanitizeSegment(rootPath));
            var files = ExtractExporter.ExportMedia(match.ExportRoot, output, true, "image");
            return new SkillRelatedAssetResultDto
            {
                Key = match.Key,
                InputPath = input,
                MatchedPath = match.Node.FullPath,
                ExportRootPath = match.ExportRoot.FullPath,
                Status = files.Count > 0 ? "exported" : "no-image-assets",
                Diagnostic = files.Count > 0 ? null : "Matched node did not export image assets.",
                ExportedFileCount = files.Count,
                Files = files
            };
        }

        private static IEnumerable<SkillSpriteNodePath> Traverse(Wz_Node root)
        {
            var stack = new Stack<SkillSpriteNodePath>();
            stack.Push(new SkillSpriteNodePath(root, string.Empty));

            while (stack.Count > 0)
            {
                Wz_Node node = NodePath.ExtractImageNode(stack.Pop().Node, true);
                if (node == null)
                {
                    continue;
                }

                yield return new SkillSpriteNodePath(node, string.Empty);

                var children = node.Nodes.Cast<Wz_Node>().ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(new SkillSpriteNodePath(children[i], string.Empty));
                }
            }
        }

        private static string ResolveDataDirectory(string explicitDataDir, string skillInput, string dataInputPath)
        {
            foreach (string path in new[] { explicitDataDir, skillInput, dataInputPath })
            {
                string dataDir = TryResolveDataDirectory(path);
                if (!string.IsNullOrEmpty(dataDir))
                {
                    return dataDir;
                }
            }
            return null;
        }

        private static string TryResolveDataDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(path);
            var info = Directory.Exists(fullPath)
                ? new DirectoryInfo(fullPath)
                : File.Exists(fullPath)
                ? new FileInfo(fullPath).Directory
                : new DirectoryInfo(Path.GetDirectoryName(fullPath) ?? fullPath);

            while (info != null)
            {
                if (string.Equals(info.Name, "Data", StringComparison.OrdinalIgnoreCase))
                {
                    return info.FullName;
                }
                info = info.Parent;
            }

            if (Directory.Exists(fullPath) && Directory.Exists(Path.Combine(fullPath, "Skill")))
            {
                return fullPath;
            }
            return null;
        }

        private static void AddExistingFiles(List<string> candidates, string directory, string pattern, int max)
        {
            if (!Directory.Exists(directory) || candidates.Count >= max)
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(directory, pattern).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                AddExistingCandidate(candidates, file);
                if (candidates.Count >= max)
                {
                    return;
                }
            }
        }

        private static void AddExistingCandidate(List<string> candidates, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path);
            if ((File.Exists(fullPath) || Directory.Exists(fullPath))
                && !candidates.Any(item => string.Equals(item, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(fullPath);
            }
        }

        private static string SanitizeSegment(string value)
        {
            string normalized = SkillSpriteExportOptions.NormalizePath(value);
            if (normalized.Length == 0)
            {
                return "_";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(normalized.Select(ch => ch == '/' || invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        private struct SkillSpriteNodePath
        {
            public SkillSpriteNodePath(Wz_Node node, string relativePath)
            {
                this.Node = node;
                this.RelativePath = relativePath;
            }

            public Wz_Node Node { get; private set; }
            public string RelativePath { get; private set; }
        }

        private sealed class RelatedNodeMatch
        {
            public RelatedNodeMatch(string key, Wz_Node node, Wz_Node exportRoot)
            {
                this.Key = key;
                this.Node = node;
                this.ExportRoot = exportRoot;
            }

            public string Key { get; private set; }
            public Wz_Node Node { get; private set; }
            public Wz_Node ExportRoot { get; private set; }
        }
    }
}
