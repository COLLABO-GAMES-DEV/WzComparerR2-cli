using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WzComparerR2.WzLib;

namespace WzComparerR2.Headless
{
    internal sealed class SkillSpriteExportOptions
    {
        public List<string> Branches { get; private set; }
        public List<string> CanvasInputs { get; private set; }
        public List<string> SoundInputs { get; private set; }
        public List<string> RelatedInputs { get; private set; }
        public List<string> RelatedKeys { get; private set; }
        public int MaxCanvasInputs { get; private set; }
        public int MaxSoundInputs { get; private set; }
        public int MaxRelatedInputs { get; private set; }
        public int MaxRelatedMatches { get; private set; }
        public bool DirectOnly { get; private set; }
        public bool IncludeSounds { get; private set; }
        public bool IncludeVideos { get; private set; }
        public bool IncludeRelatedAssets { get; private set; }
        public bool AutoVisualBranches { get; private set; }

        public static SkillSpriteExportOptions FromArgs(ParsedArgs args)
        {
            return FromArgs(args, false);
        }

        public static SkillSpriteExportOptions FromArgs(ParsedArgs args, bool includeSoundsByDefault)
        {
            string defaultBranches = includeSoundsByDefault ? "auto" : "icon,effect,hit";
            var options = new SkillSpriteExportOptions
            {
                Branches = ParseBranches(args.GetValue("branch") ?? args.GetValue("branches") ?? defaultBranches, out bool autoVisualBranches),
                CanvasInputs = new List<string>(),
                SoundInputs = new List<string>(),
                RelatedInputs = new List<string>(),
                RelatedKeys = new List<string>(),
                MaxCanvasInputs = args.GetInt("max-canvas-inputs", 256),
                MaxSoundInputs = args.GetInt("max-sound-inputs", 64),
                MaxRelatedInputs = args.GetInt("max-related-inputs", 256),
                MaxRelatedMatches = args.GetInt("max-related-matches", 64),
                DirectOnly = args.HasFlag("direct-only"),
                IncludeSounds = includeSoundsByDefault || args.HasFlag("include-sound") || args.HasFlag("include-sounds"),
                IncludeVideos = (includeSoundsByDefault || args.HasFlag("include-video") || args.HasFlag("include-videos"))
                    && !args.HasFlag("skip-video"),
                IncludeRelatedAssets = (includeSoundsByDefault || args.HasFlag("include-related") || args.HasFlag("include-related-assets") || args.HasFlag("related-key"))
                    && !args.HasFlag("skip-related"),
                AutoVisualBranches = autoVisualBranches
            };

            AddCanvasInput(options.CanvasInputs, args.GetValue("canvas-wz"));
            AddCanvasInput(options.CanvasInputs, args.GetValue("canvas"));
            AddCanvasInput(options.SoundInputs, args.GetValue("sound-wz"));
            AddCanvasInput(options.SoundInputs, args.GetValue("sound"));
            AddCanvasInput(options.RelatedInputs, args.GetValue("related-wz"));
            AddCanvasInput(options.RelatedInputs, args.GetValue("related-input"));
            AddCanvasInput(options.RelatedInputs, args.GetValue("effect-wz"));
            AddCanvasInput(options.RelatedInputs, args.GetValue("character-wz"));
            AddRelatedKeys(options.RelatedKeys, args.GetValue("related-key"));
            AddRelatedKeys(options.RelatedKeys, args.GetValue("related-keys"));

            if (options.MaxCanvasInputs <= 0)
            {
                throw new UsageException("skill sprite --max-canvas-inputs must be a positive integer.");
            }
            if (options.MaxSoundInputs <= 0)
            {
                throw new UsageException("skill export --max-sound-inputs must be a positive integer.");
            }
            if (options.MaxRelatedInputs <= 0)
            {
                throw new UsageException("skill export --max-related-inputs must be a positive integer.");
            }
            if (options.MaxRelatedMatches <= 0)
            {
                throw new UsageException("skill export --max-related-matches must be a positive integer.");
            }

            return options;
        }

        private static List<string> ParseBranches(string value, out bool autoVisualBranches)
        {
            autoVisualBranches = false;
            var branches = new List<string>();
            foreach (string raw in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string branch = NormalizePath(raw.Trim());
                if (branch.Length == 0)
                {
                    continue;
                }
                if (string.Equals(branch, "all", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(branch, "auto", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(branch, "visual", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(branch, "visuals", StringComparison.OrdinalIgnoreCase))
                {
                    autoVisualBranches = true;
                    continue;
                }
                if (!branches.Any(item => string.Equals(item, branch, StringComparison.OrdinalIgnoreCase)))
                {
                    branches.Add(branch);
                }
            }

            if (branches.Count == 0 && !autoVisualBranches)
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

        private static void AddRelatedKeys(List<string> keys, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (string part in value.Split(new[] { ',', ';', Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                string key = part.Trim();
                if (key.Length > 0 && !keys.Any(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase)))
                {
                    keys.Add(key);
                }
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
            using (var session = OpenSession(skillInput, args, options))
            {
                return session.Export(skillId, outputDirectory);
            }
        }

        internal static SkillSpriteExportSession OpenSession(string skillInput, ParsedArgs args, SkillSpriteExportOptions options)
        {
            return new SkillSpriteExportSession(skillInput, args, options);
        }

        internal sealed class SkillSpriteExportSession : IDisposable
        {
            private readonly string skillInput;
            private readonly ParsedArgs args;
            private readonly SkillSpriteExportOptions options;
            private readonly WzLoadOptions loadOptions;
            private readonly CliWzRepository repository;
            private readonly Dictionary<string, WzLoadContext> cachedContexts;
            private bool disposed;

            public SkillSpriteExportSession(string skillInput, ParsedArgs args, SkillSpriteExportOptions options)
            {
                this.skillInput = skillInput;
                this.args = args;
                this.options = options;
                this.loadOptions = WzLoadOptions.FromArgs(args);
                this.repository = CliWzRepository.ForSkill(skillInput, args);
                this.cachedContexts = new Dictionary<string, WzLoadContext>(StringComparer.OrdinalIgnoreCase);
            }

            internal CliWzRepository Repository
            {
                get
                {
                    ThrowIfDisposed();
                    return repository;
                }
            }

            public SkillSpriteExportResultDto Export(string skillId, string outputDirectory)
            {
                ThrowIfDisposed();

                string fullOutputDirectory = Path.GetFullPath(outputDirectory);
                Directory.CreateDirectory(fullOutputDirectory);

                var stringResult = repository.FindStringInfo("skill", skillId);
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
                    Branches = new List<SkillSpriteBranchResultDto>(),
                    RelatedAssets = new List<SkillRelatedAssetResultDto>(),
                    MetadataPaths = new List<string>()
                };
                result.SkillInfo = SkillExportMetadataWriter.BuildSkillInfo(skillId, dataResult, stringResult, repository, result.Diagnostics);

                List<string> canvasCandidates = options.DirectOnly
                    ? new List<string>()
                    : ResolveCanvasCandidates(args, skillInput, dataResult.InputPath, options, result.Diagnostics);
                result.CanvasInputPaths.AddRange(canvasCandidates);
                if (options.IncludeSounds)
                {
                    result.SoundInputPaths.AddRange(ResolveSoundCandidates(args, skillInput, dataResult.InputPath, options, result.Diagnostics));
                }

                var branches = ResolveRequestedBranches(dataResult.Node, options, result.Diagnostics);
                foreach (string branch in branches)
                {
                    result.Branches.Add(ExportBranch(dataResult.Node, dataResult.InputPath, branch, fullOutputDirectory, args, options, canvasCandidates, this));
                }

                if (options.IncludeSounds)
                {
                    result.Sound = ExportSounds(skillId, fullOutputDirectory, args, result.SoundInputPaths, this);
                }

                if (options.IncludeVideos)
                {
                    result.Videos = ExportVideos(skillId, dataResult.Node, skillInput, dataResult.InputPath, fullOutputDirectory, args, options, this);
                }

                if (options.IncludeRelatedAssets && !options.DirectOnly)
                {
                    result.RelatedAssets.AddRange(SkillRelatedAssetExporter.Export(
                        dataResult.Node,
                        skillId,
                        skillInput,
                        dataResult.InputPath,
                        fullOutputDirectory,
                        args,
                        options,
                        result.Diagnostics,
                        this));
                }

                result.SpriteFileCount = result.Branches.Sum(branch => branch.ExportedFileCount);
                result.RelatedFileCount = result.RelatedAssets.Sum(asset => asset.ExportedFileCount);
                result.SoundFileCount = result.Sound == null ? 0 : result.Sound.ExportedFileCount;
                result.VideoFileCount = result.Videos == null ? 0 : result.Videos.ExportedFileCount;
                result.ExportedFileCount = result.SpriteFileCount + result.SoundFileCount + result.VideoFileCount + result.RelatedFileCount;
                result.Resources = SkillExportMetadataWriter.BuildResourceSummaries(result);
                SkillExportMetadataWriter.WriteSidecars(result, fullOutputDirectory);
                return result;
            }

            internal WzLoadContext LoadCachedContext(string input, List<string> diagnostics)
            {
                ThrowIfDisposed();
                if (string.IsNullOrWhiteSpace(input))
                {
                    return null;
                }

                string fullPath = Path.GetFullPath(input);
                WzLoadContext cached;
                if (cachedContexts.TryGetValue(fullPath, out cached))
                {
                    return cached;
                }

                try
                {
                    cached = WzLoadContext.Load(fullPath, loadOptions);
                    cachedContexts.Add(fullPath, cached);
                    return cached;
                }
                catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is WzLoadException)
                {
                    if (diagnostics != null)
                    {
                        diagnostics.Add(Path.GetFileName(input) + ": " + ex.Message);
                    }
                    return null;
                }
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                foreach (var context in cachedContexts.Values)
                {
                    context.Dispose();
                }
                cachedContexts.Clear();
                repository.Dispose();
                disposed = true;
            }

            private void ThrowIfDisposed()
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(SkillSpriteExportSession));
                }
            }
        }

        private static List<string> ResolveRequestedBranches(Wz_Node skillNode, SkillSpriteExportOptions options, List<string> diagnostics)
        {
            var branches = new List<string>();
            if (options.AutoVisualBranches)
            {
                branches.AddRange(CollectVisualBranches(skillNode));
            }

            foreach (string branch in options.Branches)
            {
                AddBranch(branches, branch);
            }

            if (branches.Count == 0)
            {
                diagnostics.Add("No visual branches were detected for this skill id.");
            }
            else if (options.AutoVisualBranches)
            {
                diagnostics.Add("Auto-selected visual branches: " + string.Join(",", branches));
            }

            return branches;
        }

        private static List<string> CollectVisualBranches(Wz_Node skillNode)
        {
            var branches = new List<string>();
            foreach (Wz_Node child in skillNode.Nodes)
            {
                string branch = SkillSpriteExportOptions.NormalizePath(child.Text);
                if (branch.Length == 0 || IsNonVisualSkillBranch(branch))
                {
                    continue;
                }

                if (ContainsImageOrOutlink(child))
                {
                    AddBranch(branches, branch);
                }
            }
            return branches;
        }

        private static bool IsNonVisualSkillBranch(string branch)
        {
            switch (branch.ToLowerInvariant())
            {
                case "common":
                case "pvpcommon":
                case "level":
                case "req":
                case "action":
                case "masterlevel":
                case "reqlev":
                case "hyper":
                case "vskill":
                case "vehicleid":
                case "hyperstat":
                case "invisible":
                case "combatorders":
                case "notremoved":
                case "origin":
                case "ascent":
                case "timelimited":
                case "ispetautobuff":
                case "issequenceon":
                case "disablenextlevelinfo":
                case "relationskill":
                case "addattack":
                case "assistskilllink":
                    return true;
                default:
                    return false;
            }
        }

        private static bool ContainsImageOrOutlink(Wz_Node root)
        {
            foreach (var item in TraverseWithRelativePath(root))
            {
                if (item.Node.Value is Wz_Png || item.Node.Value is Wz_Video || !string.IsNullOrEmpty(ReadOutlink(item.Node)))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddBranch(List<string> branches, string branch)
        {
            branch = SkillSpriteExportOptions.NormalizePath(branch);
            if (branch.Length == 0 || branches.Any(item => string.Equals(item, branch, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            branches.Add(branch);
        }

        private static SkillSoundExportResultDto ExportSounds(
            string skillId,
            string outputDirectory,
            ParsedArgs args,
            List<string> soundCandidates,
            SkillSpriteExportSession session)
        {
            var result = new SkillSoundExportResultDto
            {
                RequestedPath = "Skill.img/" + skillId,
                Files = new List<ExtractedFileDto>(),
                TriedSoundInputs = new List<string>(),
                Diagnostics = new List<string>()
            };

            var triedInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string input in soundCandidates)
            {
                if (!triedInputs.Add(input))
                {
                    continue;
                }

                result.TriedSoundInputs.Add(input);
                WzLoadContext context = session.LoadCachedContext(input, result.Diagnostics);
                if (context == null)
                {
                    continue;
                }

                Wz_Node node = NodePath.Resolve(context.Root, result.RequestedPath, true);
                if (node == null)
                {
                    continue;
                }

                result.SourcePath = node.FullPath;
                result.InputPath = input;
                result.Files.AddRange(ExtractExporter.ExportMedia(node, Path.Combine(outputDirectory, "sound"), true, "sound"));
                result.ExportedFileCount = result.Files.Count;
                result.Status = result.ExportedFileCount > 0 ? "exported" : "no-sound-assets";
                if (result.ExportedFileCount == 0)
                {
                    result.Diagnostic = "Skill sound path exists but contains no sound assets.";
                }
                return result;
            }

            result.Status = "not-found";
            result.Diagnostic = "No readable sound input contained " + result.RequestedPath + ".";
            return result;
        }

        private static SkillVideoExportResultDto ExportVideos(
            string skillId,
            Wz_Node skillNode,
            string skillInput,
            string dataInputPath,
            string outputDirectory,
            ParsedArgs args,
            SkillSpriteExportOptions options,
            SkillSpriteExportSession session)
        {
            var result = new SkillVideoExportResultDto
            {
                RequestedPath = skillNode.FullPath,
                SourcePath = skillNode.FullPath,
                Files = new List<ExtractedFileDto>(),
                TriedVideoInputs = new List<string>(),
                Diagnostics = new List<string>()
            };

            try
            {
                result.TriedVideoInputs.Add(dataInputPath);
                result.Files.AddRange(VideoExporter.Export(skillNode, Path.Combine(outputDirectory, "video"), true, VideoExportOptions.FromArgs(args, "frames")));
                if (result.Files.Count == 0)
                {
                    ExportVideosFromMetadataInputs(skillId, skillInput, dataInputPath, outputDirectory, args, options, result, session);
                }

                result.ExportedFileCount = result.Files.Count;
                result.Status = result.ExportedFileCount > 0 ? "exported" : "no-video-assets";
                if (result.ExportedFileCount == 0)
                {
                    result.Diagnostic = "No selected Skill or Skill*.ms metadata input contained Wz_Video assets for this skill.";
                }
            }
            catch (UsageException ex)
            {
                result.Status = "decode-failed";
                result.Diagnostic = ex.Message;
                result.Diagnostics.Add(ex.Message);
            }

            return result;
        }

        private static void ExportVideosFromMetadataInputs(
            string skillId,
            string skillInput,
            string dataInputPath,
            string outputDirectory,
            ParsedArgs args,
            SkillSpriteExportOptions options,
            SkillVideoExportResultDto result,
            SkillSpriteExportSession session)
        {
            string dataDir = ResolveDataDirectory(args.GetValue("data-dir"), skillInput, dataInputPath);
            if (string.IsNullOrEmpty(dataDir))
            {
                return;
            }

            var inputs = new List<string>();
            AddExistingFiles(inputs, Path.Combine(dataDir, "Packs"), "Skill*.ms", options.MaxRelatedInputs);

            foreach (string input in inputs)
            {
                if (result.TriedVideoInputs.Any(item => string.Equals(item, input, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                result.TriedVideoInputs.Add(input);
                WzLoadContext context = session.LoadCachedContext(input, result.Diagnostics);
                if (context == null)
                {
                    continue;
                }

                Wz_Node node = DomainInfoFinder.FindDataNode(context.Root, "skill", skillId);
                if (node == null)
                {
                    continue;
                }

                int before = result.Files.Count;
                result.Files.AddRange(VideoExporter.Export(node, Path.Combine(outputDirectory, "video"), true, VideoExportOptions.FromArgs(args, "frames")));
                if (result.Files.Count > before)
                {
                    result.SourcePath = node.FullPath;
                    return;
                }
            }
        }

        private static SkillSpriteBranchResultDto ExportBranch(
            Wz_Node skillNode,
            string skillInputPath,
            string branch,
            string outputDirectory,
            ParsedArgs args,
            SkillSpriteExportOptions options,
            List<string> canvasCandidates,
            SkillSpriteExportSession session)
        {
            var branchResult = new SkillSpriteBranchResultDto
            {
                Branch = branch,
                RequestedPath = SkillSpriteExportOptions.NormalizePath(skillNode.FullPath) + "/" + branch,
                InputPath = skillInputPath,
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
            branchResult.MetadataSourcePath = branchNode.FullPath;
            var branchMetadata = ExtractedFileMetadata.CollectDirectNodeMetadata(branchNode);
            if (branchMetadata.Count > 0)
            {
                branchResult.Metadata = branchMetadata;
            }
            string outlink = FindBranchOutlink(branchNode, out string descendantRelativePath);
            if (!string.IsNullOrEmpty(outlink))
            {
                string resolvedOutlink = TrimDescendantPath(outlink, descendantRelativePath);
                branchResult.OutlinkPath = resolvedOutlink;
                branchResult.TriedOutlinkPaths.AddRange(BuildOutlinkPathCandidates(resolvedOutlink));

                if (TryExportOutlinkNode(canvasCandidates, branchResult, args, Path.Combine(outputDirectory, SanitizeSegment(branch)), branchNode, session))
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

        private static bool TryExportOutlinkNode(
            List<string> canvasCandidates,
            SkillSpriteBranchResultDto branchResult,
            ParsedArgs args,
            string outputDirectory,
            Wz_Node metadataRoot,
            SkillSpriteExportSession session)
        {
            var triedInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string input in canvasCandidates)
            {
                if (!triedInputs.Add(input))
                {
                    continue;
                }

                branchResult.TriedCanvasInputs.Add(input);
                WzLoadContext context = session.LoadCachedContext(input, branchResult.Diagnostics);
                if (context == null)
                {
                    continue;
                }

                foreach (string outlinkPath in branchResult.TriedOutlinkPaths)
                {
                    Wz_Node node = NodePath.Resolve(context.Root, outlinkPath, true);
                    if (node != null)
                    {
                        branchResult.ResolvedPath = node.FullPath;
                        branchResult.ResolvedInputPath = input;
                        var files = ExtractExporter.ExportMedia(node, outputDirectory, true, "image");
                        ExtractedFileMetadata.EnrichFromMetadataRoot(files, node, metadataRoot);
                        branchResult.Files.AddRange(files);
                        return true;
                    }
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

        private static List<string> ResolveSoundCandidates(ParsedArgs args, string skillInput, string dataInputPath, SkillSpriteExportOptions options, List<string> diagnostics)
        {
            var candidates = new List<string>();
            foreach (string input in options.SoundInputs)
            {
                AddExistingCandidate(candidates, input);
            }

            string dataDir = ResolveDataDirectory(args.GetValue("data-dir"), skillInput, dataInputPath);
            if (!string.IsNullOrEmpty(dataDir))
            {
                AddExistingCandidate(candidates, Path.Combine(dataDir, "Sound"));
                AddExistingFiles(candidates, Path.Combine(dataDir, "Sound"), "Sound*.wz", options.MaxSoundInputs);
            }

            if (candidates.Count > options.MaxSoundInputs)
            {
                diagnostics.Add("Sound candidate list was truncated to " + options.MaxSoundInputs + " entries.");
                candidates = candidates.Take(options.MaxSoundInputs).ToList();
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
        public int SpriteFileCount { get; set; }
        public int RelatedFileCount { get; set; }
        public int SoundFileCount { get; set; }
        public int VideoFileCount { get; set; }
        public int MetadataFileCount { get; set; }
        public List<string> CanvasInputPaths { get; set; }
        public List<string> SoundInputPaths { get; set; } = new List<string>();
        public List<string> MetadataPaths { get; set; }
        public List<string> Diagnostics { get; set; }
        public SkillExportInfoDto SkillInfo { get; set; }
        public List<SkillSpriteBranchResultDto> Branches { get; set; }
        public List<SkillRelatedAssetResultDto> RelatedAssets { get; set; }
        public List<SkillExportResourceDto> Resources { get; set; }
        public SkillSoundExportResultDto Sound { get; set; }
        public SkillVideoExportResultDto Videos { get; set; }
    }

    internal sealed class SkillSpriteBranchResultDto
    {
        public string Branch { get; set; }
        public string RequestedPath { get; set; }
        public string InputPath { get; set; }
        public string SourcePath { get; set; }
        public string MetadataSourcePath { get; set; }
        public string OutlinkPath { get; set; }
        public string ResolvedPath { get; set; }
        public string ResolvedInputPath { get; set; }
        public string Status { get; set; }
        public string Diagnostic { get; set; }
        public int ExportedFileCount { get; set; }
        public List<string> TriedOutlinkPaths { get; set; }
        public List<string> TriedCanvasInputs { get; set; }
        public List<string> Diagnostics { get; set; } = new List<string>();
        public Dictionary<string, ExtractedNodeValueDto> Metadata { get; set; }
        public List<ExtractedFileDto> Files { get; set; }
    }

    internal sealed class SkillRelatedAssetResultDto
    {
        public string Key { get; set; }
        public string InputPath { get; set; }
        public string MatchedPath { get; set; }
        public string ExportRootPath { get; set; }
        public string Status { get; set; }
        public string Diagnostic { get; set; }
        public int ExportedFileCount { get; set; }
        public List<ExtractedFileDto> Files { get; set; }
    }

    internal sealed class SkillSoundExportResultDto
    {
        public string RequestedPath { get; set; }
        public string InputPath { get; set; }
        public string SourcePath { get; set; }
        public string Status { get; set; }
        public string Diagnostic { get; set; }
        public int ExportedFileCount { get; set; }
        public List<string> TriedSoundInputs { get; set; }
        public List<string> Diagnostics { get; set; }
        public List<ExtractedFileDto> Files { get; set; }
    }

    internal sealed class SkillVideoExportResultDto
    {
        public string RequestedPath { get; set; }
        public string SourcePath { get; set; }
        public string Status { get; set; }
        public string Diagnostic { get; set; }
        public int ExportedFileCount { get; set; }
        public List<string> TriedVideoInputs { get; set; }
        public List<string> Diagnostics { get; set; }
        public List<ExtractedFileDto> Files { get; set; }
    }
}
