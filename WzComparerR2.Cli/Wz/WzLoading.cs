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
                throw new WzLoadException("Failed to load input: " + inputPath, ex);
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
            foreach (WzLoadContext context in dataContexts)
            {
                Wz_Node node = DomainInfoFinder.FindDataNode(context.Root, kind, id);
                if (node != null)
                {
                    return new CliWzDataResult
                    {
                        Node = node,
                        InputPath = context.InputPath
                    };
                }
            }

            return null;
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
                        throw new WzLoadException("Failed to load " + label + " candidate: " + candidate.Path, ex);
                    }
                }
            }
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
}
