using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal sealed class SkillSpriteExportOptions
    {
        public List<string> Branches { get; private set; }
        public List<string> CanvasInputs { get; private set; }
        public int MaxCanvasInputs { get; private set; }
        public bool DirectOnly { get; private set; }

        public static SkillSpriteExportOptions FromArgs(ParsedArgs args)
        {
            var options = new SkillSpriteExportOptions
            {
                Branches = ParseBranches(args.GetValue("branch") ?? args.GetValue("branches") ?? "icon,effect,hit"),
                CanvasInputs = new List<string>(),
                MaxCanvasInputs = args.GetInt("max-canvas-inputs", 256),
                DirectOnly = args.HasFlag("direct-only")
            };

            AddCanvasInput(options.CanvasInputs, args.GetValue("canvas-wz"));
            AddCanvasInput(options.CanvasInputs, args.GetValue("canvas"));

            if (options.MaxCanvasInputs <= 0)
            {
                throw new UsageException("skill sprite --max-canvas-inputs must be a positive integer.");
            }

            return options;
        }

        private static List<string> ParseBranches(string value)
        {
            var branches = new List<string>();
            foreach (string raw in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string branch = NormalizePath(raw.Trim());
                if (branch.Length == 0)
                {
                    continue;
                }
                if (string.Equals(branch, "all", StringComparison.OrdinalIgnoreCase))
                {
                    return new List<string> { "icon", "effect", "hit" };
                }
                branches.Add(branch);
            }

            if (branches.Count == 0)
            {
                throw new UsageException("skill sprite --branch must contain at least one branch name.");
            }
            return branches;
        }

        private static void AddCanvasInput(List<string> inputs, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (string part in value.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                inputs.Add(Path.GetFullPath(part));
            }
        }

        internal static string NormalizePath(string value)
        {
            return (value ?? string.Empty).Replace('\\', '/').Trim('/');
        }
    }

    internal static class SkillSpriteExporter
    {
        public static SkillSpriteExportResultDto Export(string skillInput, string skillId, string outputDirectory, ParsedArgs args, SkillSpriteExportOptions options)
        {
            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);

            using (var repository = CliWzRepository.ForSkill(skillInput, args))
            {
                var dataResult = repository.FindDataNode("skill", skillId);
                if (dataResult == null)
                {
                    throw new UsageException("skill id not found: " + skillId);
                }

                var result = new SkillSpriteExportResultDto
                {
                    SkillId = skillId,
                    SkillInputPath = dataResult.InputPath,
                    SkillNodePath = dataResult.Node.FullPath,
                    OutputDirectory = fullOutputDirectory,
                    CanvasInputPaths = new List<string>(),
                    Diagnostics = new List<string>(),
                    Branches = new List<SkillSpriteBranchResultDto>()
                };

                List<string> canvasCandidates = options.DirectOnly
                    ? new List<string>()
                    : ResolveCanvasCandidates(args, skillInput, dataResult.InputPath, options, result.Diagnostics);
                result.CanvasInputPaths.AddRange(canvasCandidates);

                foreach (string branch in options.Branches)
                {
                    result.Branches.Add(ExportBranch(dataResult.Node, branch, fullOutputDirectory, args, options, canvasCandidates));
                }

                result.ExportedFileCount = result.Branches.Sum(branch => branch.ExportedFileCount);
                return result;
            }
        }

        private static SkillSpriteBranchResultDto ExportBranch(Wz_Node skillNode, string branch, string outputDirectory, ParsedArgs args, SkillSpriteExportOptions options, List<string> canvasCandidates)
        {
            var branchResult = new SkillSpriteBranchResultDto
            {
                Branch = branch,
                RequestedPath = SkillSpriteExportOptions.NormalizePath(skillNode.FullPath) + "/" + branch,
                Files = new List<ExtractedFileDto>(),
                TriedOutlinkPaths = new List<string>(),
                TriedCanvasInputs = new List<string>()
            };

            Wz_Node branchNode = NodePath.Resolve(skillNode, branch, true);
            if (branchNode == null)
            {
                branchResult.Status = "not-found";
                branchResult.Diagnostic = "Skill branch was not found under the skill node.";
                return branchResult;
            }

            branchResult.SourcePath = branchNode.FullPath;
            string outlink = FindBranchOutlink(branchNode, out string descendantRelativePath);
            if (!string.IsNullOrEmpty(outlink))
            {
                string resolvedOutlink = TrimDescendantPath(outlink, descendantRelativePath);
                branchResult.OutlinkPath = resolvedOutlink;
                branchResult.TriedOutlinkPaths.AddRange(BuildOutlinkPathCandidates(resolvedOutlink));

                if (TryExportOutlinkNode(canvasCandidates, branchResult, args, Path.Combine(outputDirectory, SanitizeSegment(branch))))
                {
                    branchResult.ExportedFileCount = branchResult.Files.Count;
                    branchResult.Status = branchResult.ExportedFileCount > 0 ? "exported-outlink" : "no-image-assets";
                    if (branchResult.ExportedFileCount == 0)
                    {
                        branchResult.Diagnostic = "Resolved outlink node exists but contains no image assets.";
                    }
                    return branchResult;
                }

                branchResult.Status = "outlink-not-resolved";
                branchResult.Diagnostic = "Skill branch uses _outlink, but no readable canvas input contained the target path.";
                return branchResult;
            }

            branchResult.Files.AddRange(ExtractExporter.ExportMedia(branchNode, Path.Combine(outputDirectory, SanitizeSegment(branch)), true, "image"));
            branchResult.ExportedFileCount = branchResult.Files.Count;
            branchResult.Status = branchResult.ExportedFileCount > 0 ? "exported-direct" : "no-image-assets";
            if (branchResult.ExportedFileCount == 0)
            {
                branchResult.Diagnostic = "Selected skill branch contains no image assets.";
            }
            return branchResult;
        }

        private static bool TryExportOutlinkNode(List<string> canvasCandidates, SkillSpriteBranchResultDto branchResult, ParsedArgs args, string outputDirectory)
        {
            var triedInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string input in canvasCandidates)
            {
                if (!triedInputs.Add(input))
                {
                    continue;
                }

                branchResult.TriedCanvasInputs.Add(input);
                try
                {
                    using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
                    {
                        foreach (string outlinkPath in branchResult.TriedOutlinkPaths)
                        {
                            Wz_Node node = NodePath.Resolve(context.Root, outlinkPath, true);
                            if (node != null)
                            {
                                branchResult.ResolvedPath = node.FullPath;
                                branchResult.Files.AddRange(ExtractExporter.ExportMedia(node, outputDirectory, true, "image"));
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is WzLoadException)
                {
                    branchResult.Diagnostics.Add(Path.GetFileName(input) + ": " + ex.Message);
                }
            }

            return false;
        }

        private static string FindBranchOutlink(Wz_Node branchNode, out string descendantRelativePath)
        {
            foreach (var item in TraverseWithRelativePath(branchNode))
            {
                string outlink = ReadOutlink(item.Node);
                if (!string.IsNullOrEmpty(outlink))
                {
                    descendantRelativePath = item.RelativePath;
                    return SkillSpriteExportOptions.NormalizePath(outlink);
                }
            }

            descendantRelativePath = null;
            return null;
        }

        private static string ReadOutlink(Wz_Node node)
        {
            Wz_Node outlinkNode = node == null ? null : node.Nodes["_outlink"];
            return outlinkNode.GetValueEx<string>(null);
        }

        private static IEnumerable<SkillSpriteNodePath> TraverseWithRelativePath(Wz_Node root)
        {
            var stack = new Stack<SkillSpriteNodePath>();
            stack.Push(new SkillSpriteNodePath(root, string.Empty));

            while (stack.Count > 0)
            {
                SkillSpriteNodePath current = stack.Pop();
                Wz_Node node = NodePath.ExtractImageNode(current.Node, true);
                if (node == null)
                {
                    continue;
                }

                yield return new SkillSpriteNodePath(node, current.RelativePath);

                var children = node.Nodes.Cast<Wz_Node>().ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    string relative = string.IsNullOrEmpty(current.RelativePath)
                        ? children[i].Text
                        : current.RelativePath + "/" + children[i].Text;
                    stack.Push(new SkillSpriteNodePath(children[i], SkillSpriteExportOptions.NormalizePath(relative)));
                }
            }
        }

        private static string TrimDescendantPath(string outlink, string descendantRelativePath)
        {
            string normalized = SkillSpriteExportOptions.NormalizePath(outlink);
            string relative = SkillSpriteExportOptions.NormalizePath(descendantRelativePath);
            if (relative.Length == 0)
            {
                return normalized;
            }

            string suffix = "/" + relative;
            if (normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(0, normalized.Length - suffix.Length);
            }
            return normalized;
        }

        private static List<string> BuildOutlinkPathCandidates(string outlink)
        {
            var result = new List<string>();
            AddPathCandidate(result, outlink);

            string normalized = SkillSpriteExportOptions.NormalizePath(outlink);
            foreach (string prefix in new[] { "Skill/_Canvas/", "Skill/Canvas/", "_Canvas/", "Canvas/", "Skill/" })
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    AddPathCandidate(result, normalized.Substring(prefix.Length));
                }
            }

            int imgIndex = normalized.IndexOf(".img/", StringComparison.OrdinalIgnoreCase);
            if (imgIndex >= 0)
            {
                AddPathCandidate(result, normalized.Substring(Math.Max(0, normalized.LastIndexOf('/', imgIndex) + 1)));
            }

            return result;
        }

        private static void AddPathCandidate(List<string> paths, string path)
        {
            path = SkillSpriteExportOptions.NormalizePath(path);
            if (path.Length == 0 || paths.Any(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            paths.Add(path);
        }

        private static List<string> ResolveCanvasCandidates(ParsedArgs args, string skillInput, string dataInputPath, SkillSpriteExportOptions options, List<string> diagnostics)
        {
            var candidates = new List<string>();
            foreach (string input in options.CanvasInputs)
            {
                AddExistingCandidate(candidates, input);
            }

            string dataDir = ResolveDataDirectory(args.GetValue("data-dir"), skillInput, dataInputPath);
            if (!string.IsNullOrEmpty(dataDir))
            {
                AddExistingCandidate(candidates, Path.Combine(dataDir, "Skill", "_Canvas"));
                AddExistingFiles(candidates, Path.Combine(dataDir, "Skill", "_Canvas"), "*.wz", options.MaxCanvasInputs);
                AddExistingFiles(candidates, Path.Combine(dataDir, "Packs"), "Skill*.ms", options.MaxCanvasInputs);
            }

            if (candidates.Count > options.MaxCanvasInputs)
            {
                diagnostics.Add("Canvas candidate list was truncated to " + options.MaxCanvasInputs + " entries.");
                candidates = candidates.Take(options.MaxCanvasInputs).ToList();
            }

            return candidates;
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
    }

    internal sealed class SkillSpriteExportResultDto
    {
        public string SkillId { get; set; }
        public string SkillInputPath { get; set; }
        public string SkillNodePath { get; set; }
        public string OutputDirectory { get; set; }
        public int ExportedFileCount { get; set; }
        public List<string> CanvasInputPaths { get; set; }
        public List<string> Diagnostics { get; set; }
        public List<SkillSpriteBranchResultDto> Branches { get; set; }
    }

    internal sealed class SkillSpriteBranchResultDto
    {
        public string Branch { get; set; }
        public string RequestedPath { get; set; }
        public string SourcePath { get; set; }
        public string OutlinkPath { get; set; }
        public string ResolvedPath { get; set; }
        public string Status { get; set; }
        public string Diagnostic { get; set; }
        public int ExportedFileCount { get; set; }
        public List<string> TriedOutlinkPaths { get; set; }
        public List<string> TriedCanvasInputs { get; set; }
        public List<string> Diagnostics { get; set; } = new List<string>();
        public List<ExtractedFileDto> Files { get; set; }
    }
}
