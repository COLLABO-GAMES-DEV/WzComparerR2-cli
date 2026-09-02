using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal sealed class WzLoadOptions
    {
        public bool UseBaseWz { get; private set; }
        public string FallbackPath { get; private set; }

        public static WzLoadOptions FromArgs(ParsedArgs args)
        {
            return new WzLoadOptions
            {
                UseBaseWz = args.HasFlag("use-base-wz"),
                FallbackPath = args.GetValue("fallback")
            };
        }
    }

    internal sealed class WzLoadContext : IDisposable
    {
        private WzLoadContext(string inputPath, Wz_Structure structure)
        {
            this.InputPath = inputPath;
            this.Structure = structure;
        }

        public string InputPath { get; private set; }
        public Wz_Structure Structure { get; private set; }
        public Wz_Node Root { get { return this.Structure.WzNode; } }

        public static WzLoadContext Load(string inputPath, WzLoadOptions options)
        {
            if (string.IsNullOrEmpty(inputPath))
            {
                throw new UsageException("Input path is required.");
            }

            string fullPath = Path.GetFullPath(inputPath);
            var structure = new Wz_Structure();

            try
            {
                if (Directory.Exists(fullPath))
                {
                    Wz_Node node = null;
                    structure.LoadWzFolder(fullPath, ref node, options.UseBaseWz, options.FallbackPath);
                    structure.WzNode = node;
                    structure.calculate_img_count();
                }
                else if (File.Exists(fullPath))
                {
                    string ext = Path.GetExtension(fullPath);
                    if (string.Equals(ext, ".img", StringComparison.OrdinalIgnoreCase))
                    {
                        structure.LoadImg(fullPath);
                    }
                    else if (string.Equals(ext, ".ms", StringComparison.OrdinalIgnoreCase))
                    {
                        structure.LoadMsFile(fullPath);
                    }
                    else if (!string.IsNullOrEmpty(options.FallbackPath)
                        && !string.Equals(Path.GetFileName(fullPath), "list.wz", StringComparison.OrdinalIgnoreCase))
                    {
                        structure.WzNode = new Wz_Node(Path.GetFileName(fullPath));
                        structure.LoadFile(fullPath, structure.WzNode, options.UseBaseWz, false, options.FallbackPath);
                        structure.calculate_img_count();
                    }
                    else
                    {
                        structure.Load(fullPath, options.UseBaseWz);
                    }
                }
                else
                {
                    throw new FileNotFoundException("Input path not found: " + inputPath);
                }

                if (structure.WzNode == null)
                {
                    throw new WzLoadException("No root node was loaded from: " + inputPath);
                }

                return new WzLoadContext(fullPath, structure);
            }
            catch (FileNotFoundException)
            {
                structure.Clear();
                throw;
            }
            catch (DirectoryNotFoundException)
            {
                structure.Clear();
                throw;
            }
            catch (Exception ex)
            {
                structure.Clear();
                var diagnostic = WzLoadDiagnostic.FromPath(fullPath, ex);
                string message = diagnostic != null && diagnostic.IsUnsupportedPackage
                    ? "Failed to load input: " + inputPath + " (unsupported WZ package format)"
                    : "Failed to load input: " + inputPath;
                throw new WzLoadException(message, ex, diagnostic);
            }
        }

        public void Dispose()
        {
            this.Structure.Clear();
        }
    }

    internal sealed class CliWzRepository : IDisposable
    {
        private readonly List<WzLoadContext> dataContexts = new List<WzLoadContext>();
        private readonly List<WzLoadContext> stringContexts = new List<WzLoadContext>();
        private readonly List<CliWzRepositoryCandidate> lazyDataCandidates = new List<CliWzRepositoryCandidate>();
        private WzLoadOptions loadOptions;
        private string lazyDataLabel;

        private CliWzRepository()
        {
        }

        public IReadOnlyList<string> DataInputPaths
        {
            get { return dataContexts.Select(item => item.InputPath).ToList(); }
        }

        public IReadOnlyList<string> StringInputPaths
        {
            get { return stringContexts.Select(item => item.InputPath).ToList(); }
        }

        public static CliWzRepository ForSkill(string skillInput, ParsedArgs args)
        {
            return ForDomain("skill", skillInput, args);
        }

        public static CliWzRepository ForDomain(string kind, string input, ParsedArgs args)
        {
            var repository = new CliWzRepository();
            var options = WzLoadOptions.FromArgs(args);
            repository.loadOptions = options;
            repository.lazyDataLabel = kind + " data";
            var dataCandidates = new List<CliWzRepositoryCandidate>();
            var stringCandidates = new List<CliWzRepositoryCandidate>();

            AddCandidate(dataCandidates, input, true);
            AddCandidate(dataCandidates, args.GetValue(kind + "-wz"), true);
            AddAliasCandidates(dataCandidates, kind, args);

            string dataDir = args.GetValue("data-dir");
            if (!string.IsNullOrEmpty(dataDir))
            {
                AddCandidate(dataCandidates, Path.Combine(dataDir, GetDefaultDataFolderName(kind)), false);
                AddCandidate(stringCandidates, Path.Combine(dataDir, "String"), false);
                if (string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase))
                {
                    AddSkillPackCandidates(repository.lazyDataCandidates, dataDir);
                }
            }

            AddCandidate(stringCandidates, args.GetValue("string-wz"), true);
            AddCandidate(stringCandidates, InferSiblingDataFolder(input, "String"), false);

            repository.LoadContexts(repository.dataContexts, dataCandidates, options, kind + " data");
            repository.LoadContexts(repository.stringContexts, stringCandidates, options, kind + " string");
            if (repository.dataContexts.Count == 0)
            {
                repository.Dispose();
                throw new UsageException("No " + kind + " WZ input candidates were available. Provide <wz-file-or-dir>, --" + kind + "-wz, or --data-dir.");
            }

            return repository;
        }

        public static string GetDefaultDataFolderName(string kind)
        {
            if (string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase))
            {
                return "Character";
            }
            if (string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase))
            {
                return "Skill";
            }
            if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            {
                return "Item";
            }
            if (string.Equals(kind, "map", StringComparison.OrdinalIgnoreCase))
            {
                return "Map";
            }
            if (string.Equals(kind, "mob", StringComparison.OrdinalIgnoreCase))
            {
                return "Mob";
            }
            if (string.Equals(kind, "npc", StringComparison.OrdinalIgnoreCase))
            {
                return "Npc";
            }
            if (string.Equals(kind, "quest", StringComparison.OrdinalIgnoreCase))
            {
                return "Quest";
            }

            return kind;
        }

        private static void AddAliasCandidates(List<CliWzRepositoryCandidate> dataCandidates, string kind, ParsedArgs args)
        {
            if (string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase))
            {
                AddCandidate(dataCandidates, args.GetValue("character-wz"), true);
                AddCandidate(dataCandidates, args.GetValue("item-wz"), true);
            }
            if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            {
                AddCandidate(dataCandidates, args.GetValue("item-wz"), true);
            }
        }

        public CliWzDataResult FindDataNode(string kind, string id)
        {
            CliWzDataResult firstResult = null;
            foreach (WzLoadContext context in dataContexts)
            {
                Wz_Node node = DomainInfoFinder.FindDataNode(context.Root, kind, id);
                if (node != null)
                {
                    var result = new CliWzDataResult
                    {
                        Node = node,
                        InputPath = context.InputPath
                    };
                    if (!ShouldSearchLazyDataForBetterMatch(kind, node))
                    {
                        return result;
                    }

                    firstResult = result;
                    break;
                }
            }

            CliWzDataResult lazyResult = FindDataNodeInLazyContexts(kind, id);
            if (IsBetterDomainNode(kind, lazyResult == null ? null : lazyResult.Node, firstResult == null ? null : firstResult.Node))
            {
                return lazyResult;
            }
            return firstResult ?? lazyResult;
        }

        public CliWzStringResult FindStringInfo(string kind, string id)
        {
            foreach (WzLoadContext context in stringContexts)
            {
                DomainStringInfo info = DomainInfoFinder.FindStringInfo(context.Root, kind, id);
                if (info != null)
                {
                    return new CliWzStringResult
                    {
                        StringInfo = info,
                        InputPath = context.InputPath
                    };
                }
            }

            return null;
        }

        public List<CliWzStringEntryResult> EnumerateStringInfos(string kind)
        {
            var results = new List<CliWzStringEntryResult>();
            foreach (WzLoadContext context in stringContexts)
            {
                foreach (DomainStringEntry entry in DomainInfoFinder.EnumerateStringInfos(context.Root, kind))
                {
                    results.Add(new CliWzStringEntryResult
                    {
                        Id = entry.Id,
                        Path = entry.Path,
                        StringInfo = entry.StringInfo,
                        InputPath = context.InputPath
                    });
                }
            }
            return results;
        }

        private void LoadContexts(List<WzLoadContext> contexts, List<CliWzRepositoryCandidate> candidates, WzLoadOptions options, string label)
        {
            foreach (CliWzRepositoryCandidate candidate in candidates)
            {
                if (!candidate.Explicit && !PathExists(candidate.Path))
                {
                    continue;
                }

                try
                {
                    contexts.Add(WzLoadContext.Load(candidate.Path, options));
                }
                catch (FileNotFoundException)
                {
                    if (candidate.Explicit)
                    {
                        throw;
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    if (candidate.Explicit)
                    {
                        throw;
                    }
                }
                catch (WzLoadException ex)
                {
                    if (candidate.Explicit)
                    {
                        throw new WzLoadException("Failed to load " + label + " candidate: " + candidate.Path, ex, ex.Diagnostic);
                    }
                }
            }
        }

        private CliWzDataResult FindDataNodeInLazyContexts(string kind, string id)
        {
            for (int i = 0; i < lazyDataCandidates.Count; i++)
            {
                CliWzRepositoryCandidate candidate = lazyDataCandidates[i];
                if (!candidate.Explicit && !PathExists(candidate.Path))
                {
                    continue;
                }

                WzLoadContext context = null;
                try
                {
                    context = WzLoadContext.Load(candidate.Path, loadOptions);
                    Wz_Node node = DomainInfoFinder.FindDataNode(context.Root, kind, id);
                    if (node == null)
                    {
                        context.Dispose();
                        context = null;
                        continue;
                    }

                    dataContexts.Add(context);
                    return new CliWzDataResult
                    {
                        Node = node,
                        InputPath = context.InputPath
                    };
                }
                catch (FileNotFoundException)
                {
                    if (candidate.Explicit)
                    {
                        throw;
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    if (candidate.Explicit)
                    {
                        throw;
                    }
                }
                catch (WzLoadException ex)
                {
                    if (candidate.Explicit)
                    {
                        throw new WzLoadException("Failed to load " + lazyDataLabel + " candidate: " + candidate.Path, ex, ex.Diagnostic);
                    }
                }
                finally
                {
                    if (context != null && !dataContexts.Contains(context))
                    {
                        context.Dispose();
                    }
                }
            }

            return null;
        }

        private static bool ShouldSearchLazyDataForBetterMatch(string kind, Wz_Node node)
        {
            return string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase)
                && ScoreSkillDataNode(node) < 100;
        }

        private static bool IsBetterDomainNode(string kind, Wz_Node candidate, Wz_Node current)
        {
            if (candidate == null)
            {
                return false;
            }
            if (current == null)
            {
                return true;
            }
            if (!string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return ScoreSkillDataNode(candidate) > ScoreSkillDataNode(current);
        }

        private static int ScoreSkillDataNode(Wz_Node node)
        {
            if (node == null)
            {
                return 0;
            }

            int score = 0;
            if (node.Nodes["common"] != null)
            {
                score += 100;
            }
            if (node.Nodes["level"] != null)
            {
                score += 100;
            }
            if (node.Nodes["PVPcommon"] != null)
            {
                score += 50;
            }
            foreach (string name in new[] { "masterLevel", "reqLev", "req", "hyper", "vSkill", "relationSkill", "addAttack", "assistSkillLink" })
            {
                if (node.Nodes[name] != null)
                {
                    score += 20;
                }
            }

            foreach (Wz_Node child in node.Nodes)
            {
                if (child.Value != null && !(child.Value is Wz_Png) && !(child.Value is Wz_Image) && !(child.Value is Wz_File))
                {
                    score += 2;
                }
            }
            if (ContainsSkillVisuals(node))
            {
                score += 1;
            }
            return score;
        }

        private static bool ContainsSkillVisuals(Wz_Node root)
        {
            var stack = new Stack<Wz_Node>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Wz_Node node = NodePath.ExtractImageNode(stack.Pop(), true);
                if (node == null)
                {
                    continue;
                }
                if (node.Value is Wz_Png || node.Value is Wz_Video)
                {
                    return true;
                }
                Wz_Node outlink = node.Nodes["_outlink"];
                if (outlink != null && !string.IsNullOrEmpty(outlink.GetValueEx<string>(null)))
                {
                    return true;
                }

                var children = node.Nodes.ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }
            return false;
        }

        private static void AddCandidate(List<CliWzRepositoryCandidate> candidates, string path, bool explicitCandidate)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path);
            if (candidates.Any(item => string.Equals(item.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            candidates.Add(new CliWzRepositoryCandidate
            {
                Path = fullPath,
                Explicit = explicitCandidate
            });
        }

        private static void AddSkillPackCandidates(List<CliWzRepositoryCandidate> candidates, string dataDir)
        {
            if (string.IsNullOrWhiteSpace(dataDir))
            {
                return;
            }

            string packsDir = Path.Combine(dataDir, "Packs");
            if (!Directory.Exists(packsDir))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(packsDir, "Skill_*.ms").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                AddCandidate(candidates, file, false);
            }
        }

        private static string InferSiblingDataFolder(string inputPath, string siblingName)
        {
            if (string.IsNullOrWhiteSpace(inputPath))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(inputPath);
            string fileName = Path.GetFileName(fullPath);
            if (IsKnownDataFolderOrWz(fileName))
            {
                string parent = Directory.Exists(fullPath)
                    ? Path.GetDirectoryName(fullPath)
                    : Path.GetDirectoryName(Path.GetDirectoryName(fullPath));
                if (!string.IsNullOrEmpty(parent))
                {
                    return Path.Combine(parent, siblingName);
                }
            }

            return null;
        }

        private static bool IsKnownDataFolderOrWz(string fileName)
        {
            foreach (string name in new[] { "Skill", "Item", "Character", "Map", "Mob", "Npc", "Quest", "Etc" })
            {
                if (string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fileName, name + ".wz", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool PathExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        public void Dispose()
        {
            foreach (WzLoadContext context in dataContexts)
            {
                context.Dispose();
            }
            foreach (WzLoadContext context in stringContexts)
            {
                context.Dispose();
            }
        }
    }

    internal sealed class CliWzRepositoryCandidate
    {
        public string Path { get; set; }
        public bool Explicit { get; set; }
    }

    internal sealed class WzLoadDiagnostic
    {
        private static readonly int[] Pkg2RandomHeaderDataSizeOffsets = { 0x15, 0x19, 0x39, 0x41 };
        private static readonly int[] Pkg2RandomHeader64DataSizeOffsets = { 0x12, 0x09, 0x02, 0x95 };

        public string Path { get; set; }
        public string FileName { get; set; }
        public long FileSize { get; set; }
        public string Extension { get; set; }
        public string First4Ascii { get; set; }
        public string First4Hex { get; set; }
        public string HeaderHex { get; set; }
        public string DetectedFormat { get; set; }
        public bool IsUnsupportedPackage { get; set; }
        public long ExpectedPkg2RandomDataSize { get; set; }
        public long CurrentPkg2RandomDataSizeProbe { get; set; }
        public bool CurrentPkg2RandomDataSizeMatches { get; set; }
        public long ExpectedPkg2RandomHeader64DataSize { get; set; }
        public long CurrentPkg2RandomHeader64DataSizeProbe { get; set; }
        public bool CurrentPkg2RandomHeader64DataSizeMatches { get; set; }
        public string Note { get; set; }

        public static WzLoadDiagnostic FromPath(string path, Exception loadException)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                var info = new FileInfo(path);
                int bytesToRead = (int)Math.Min(150, info.Length);
                byte[] header = new byte[bytesToRead];
                using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int read = stream.Read(header, 0, header.Length);
                    if (read != header.Length)
                    {
                        Array.Resize(ref header, read);
                    }
                }

                string first4Ascii = header.Length >= 4 ? ToPrintableAscii(header, 0, 4) : null;
                string first4Hex = header.Length >= 4 ? ToHex(header, 0, 4) : ToHex(header, 0, header.Length);
                bool isWzExtension = string.Equals(info.Extension, ".wz", StringComparison.OrdinalIgnoreCase);
                bool isKnownSignature = string.Equals(first4Ascii, "PKG1", StringComparison.Ordinal)
                    || string.Equals(first4Ascii, "PKG2", StringComparison.Ordinal);

                long expectedPkg2DataSize = info.Length >= 68 ? info.Length - 68 : -1;
                long probedPkg2DataSize = header.Length >= 68
                    ? GatherAsUInt32(header, Pkg2RandomHeaderDataSizeOffsets)
                    : -1;
                bool currentPkg2RandomMatch = expectedPkg2DataSize >= 0
                    && probedPkg2DataSize == expectedPkg2DataSize;
                long expectedPkg2Header64DataSize = info.Length >= 150 ? info.Length - 150 : -1;
                long probedPkg2Header64DataSize = header.Length >= 150
                    ? GatherAsUInt32(header, Pkg2RandomHeader64DataSizeOffsets)
                    : -1;
                bool currentPkg2Header64RandomMatch = expectedPkg2Header64DataSize >= 0
                    && probedPkg2Header64DataSize == expectedPkg2Header64DataSize;

                var diagnostic = new WzLoadDiagnostic
                {
                    Path = path,
                    FileName = info.Name,
                    FileSize = info.Length,
                    Extension = info.Extension,
                    First4Ascii = first4Ascii,
                    First4Hex = first4Hex,
                    HeaderHex = ToHex(header, 0, header.Length),
                    ExpectedPkg2RandomDataSize = expectedPkg2DataSize,
                    CurrentPkg2RandomDataSizeProbe = probedPkg2DataSize,
                    CurrentPkg2RandomDataSizeMatches = currentPkg2RandomMatch,
                    ExpectedPkg2RandomHeader64DataSize = expectedPkg2Header64DataSize,
                    CurrentPkg2RandomHeader64DataSizeProbe = probedPkg2Header64DataSize,
                    CurrentPkg2RandomHeader64DataSizeMatches = currentPkg2Header64RandomMatch
                };

                if (isKnownSignature)
                {
                    diagnostic.DetectedFormat = first4Ascii;
                    diagnostic.Note = "The file has a known WZ signature, but loading still failed.";
                }
                else if (isWzExtension && info.Length >= 68)
                {
                    diagnostic.DetectedFormat = currentPkg2Header64RandomMatch
                        ? "pkg2-random-header-64"
                        : currentPkg2RandomMatch
                        ? "pkg2-random-header"
                        : "unsupported-randomized-or-encrypted-wz";
                    diagnostic.IsUnsupportedPackage = !currentPkg2Header64RandomMatch
                        && !currentPkg2RandomMatch
                        && IsInvalidWzMessage(loadException);
                    diagnostic.Note = currentPkg2Header64RandomMatch
                        ? "Header matches the KMST1202 64-bit PKG2 random-header layout."
                        : diagnostic.IsUnsupportedPackage
                        ? "Header does not match PKG1, PKG2, or the currently supported KMST1201/KMST1202 random-header layouts. This client may use encrypted or newer package shards."
                        : "The file does not start with PKG1 or PKG2.";
                }
                else
                {
                    diagnostic.DetectedFormat = "unknown";
                    diagnostic.Note = "The file does not look like a supported WZ package.";
                }

                return diagnostic;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsInvalidWzMessage(Exception ex)
        {
            while (ex != null)
            {
                if (ex.Message != null
                    && ex.Message.IndexOf("not a valid wz file", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
                ex = ex.InnerException;
            }
            return false;
        }

        private static uint GatherAsUInt32(byte[] bytes, int[] offsets)
        {
            uint value = 0;
            for (int i = 0; i < offsets.Length; i++)
            {
                int offset = offsets[i];
                if (offset < 0 || offset >= bytes.Length)
                {
                    return 0;
                }
                value |= (uint)bytes[offset] << (8 * i);
            }
            return value;
        }

        private static string ToPrintableAscii(byte[] bytes, int offset, int count)
        {
            char[] chars = new char[count];
            for (int i = 0; i < count; i++)
            {
                byte value = bytes[offset + i];
                chars[i] = value >= 0x20 && value <= 0x7e ? (char)value : '.';
            }
            return new string(chars);
        }

        private static string ToHex(byte[] bytes, int offset, int count)
        {
            if (bytes == null || count <= 0)
            {
                return string.Empty;
            }

            char[] chars = new char[count * 3 - 1];
            int pos = 0;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    chars[pos++] = ' ';
                }

                byte value = bytes[offset + i];
                chars[pos++] = GetHexNibble(value >> 4);
                chars[pos++] = GetHexNibble(value & 0x0f);
            }
            return new string(chars);
        }

        private static char GetHexNibble(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + value - 10);
        }
    }

    internal sealed class CliWzDataResult
    {
        public Wz_Node Node { get; set; }
        public string InputPath { get; set; }
    }

    internal sealed class CliWzStringResult
    {
        public DomainStringInfo StringInfo { get; set; }
        public string InputPath { get; set; }
    }

    internal sealed class CliWzStringEntryResult
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public DomainStringInfo StringInfo { get; set; }
        public string InputPath { get; set; }
    }
}
