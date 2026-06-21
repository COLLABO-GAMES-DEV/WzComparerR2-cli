using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Json;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
        private const int ExitSuccess = 0;
        private const int ExitUsage = 1;
        private const int ExitNotFound = 2;
        private const int ExitLoadFailed = 3;
        private const int ExitInternalError = 5;
        private const string CliVersion = "0.1.0";
        private static bool QuietOutput;
        private static bool VerboseErrors;
        private static bool NoColor;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        public static int Main(string[] args)
        {
            QuietOutput = HasRawFlag(args, "quiet");
            VerboseErrors = HasRawFlag(args, "verbose");
            NoColor = HasRawFlag(args, "no-color");

            try
            {
                var commandArgs = StripLeadingOutputFlags(args).ToList();
                if (commandArgs.Count == 0 || IsHelp(commandArgs[0]))
                {
                    if (!QuietOutput)
                    {
                        PrintHelp();
                    }
                    return ExitSuccess;
                }

                string command = commandArgs[0].ToLowerInvariant();
                var parsed = ParsedArgs.Parse(commandArgs.Skip(1));

                switch (command)
                {
                    case "info":
                        return RunInfo(parsed);
                    case "tree":
                        return RunTree(parsed);
                    case "list":
                        return RunList(parsed);
                    case "search":
                        return RunSearch(parsed);
                    case "compare":
                        return RunCompare(parsed);
                    case "dump":
                        return RunDump(parsed);
                    case "extract":
                        return RunExtract(parsed);
                    case "sound":
                        return RunMedia(parsed, "sound");
                    case "image":
                        return RunMedia(parsed, "image");
                    case "patch":
                        return RunPatch(parsed);
                    case "skill":
                        return RunSkill(parsed);
                    case "item":
                    case "gear":
                    case "mob":
                    case "npc":
                    case "quest":
                        return RunDomainInfo(parsed, command);
                    case "map":
                        return RunMap(parsed);
                    case "animate":
                        return RunAnimate(parsed);
                    case "avatar":
                        return RunAvatar(parsed);
                    case "lua":
                        return RunLua(parsed);
                    case "network":
                        return RunNetwork(parsed);
                    case "update":
                        return RunUpdate(parsed).GetAwaiter().GetResult();
                    case "config":
                        return RunConfig(parsed);
                    case "plugin":
                        return RunPlugin(parsed);
                    case "version":
                    case "--version":
                        WriteLine("wcr2 cli " + CliVersion);
                        return ExitSuccess;
                    default:
                        Console.Error.WriteLine("Unknown command: " + command);
                        Console.Error.WriteLine();
                        if (!QuietOutput)
                        {
                            PrintHelp();
                        }
                        return ExitUsage;
                }
            }
            catch (UsageException ex)
            {
                Console.Error.WriteLine(ex.Message);
                WriteVerboseError(ex);
                return ExitUsage;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
                WriteVerboseError(ex);
                return ExitNotFound;
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
                WriteVerboseError(ex);
                return ExitNotFound;
            }
            catch (WzLoadException ex)
            {
                Console.Error.WriteLine(ex.Message);
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine(ex.InnerException.Message);
                }
                WriteVerboseError(ex);
                return ExitLoadFailed;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Unexpected error: " + ex.Message);
                WriteVerboseError(ex);
                return ExitInternalError;
            }
        }

        private static bool HasRawFlag(IEnumerable<string> args, string name)
        {
            string flag = "--" + name;
            return args.Any(arg => string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> StripLeadingOutputFlags(IEnumerable<string> args)
        {
            bool inLeadingFlags = true;
            foreach (string arg in args)
            {
                if (inLeadingFlags && IsOutputFlag(arg))
                {
                    continue;
                }

                inLeadingFlags = false;
                yield return arg;
            }
        }

        private static bool IsOutputFlag(string arg)
        {
            return string.Equals(arg, "--quiet", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--verbose", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--no-color", StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteVerboseError(Exception ex)
        {
            if (!VerboseErrors)
            {
                return;
            }

            Console.Error.WriteLine("Exception: " + ex.GetType().FullName);
            if (!string.IsNullOrEmpty(ex.StackTrace))
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
        }

        private static void WriteLine(string text)
        {
            if (!QuietOutput)
            {
                Console.WriteLine(NoColor ? text : text);
            }
        }

        private static int RunInfo(ParsedArgs args)
        {
            string input = RequireInput(args, "info <file-or-dir>");
            bool json = args.HasFlag("json");

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                var dto = InfoDto.FromContext(context);
                WriteOutput(dto, json, writer =>
                {
                    writer.WriteLine("Input: " + dto.InputPath);
                    writer.WriteLine("Root: " + dto.RootName);
                    writer.WriteLine("Children: " + dto.RootChildren);
                    writer.WriteLine("WZ files: " + dto.WzFileCount);
                    writer.WriteLine("MS files: " + dto.MsFileCount);
                    writer.WriteLine("Images: " + dto.ImageCount);

                    foreach (var file in dto.Files)
                    {
                        writer.WriteLine("- " + file.Name + " type=" + file.Type + " version=" + file.WzVersion + " images=" + file.ImageCount);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunTree(ParsedArgs args)
        {
            string input = RequireInput(args, "tree <file-or-dir>");
            bool json = args.HasFlag("json");
            int depth = args.GetInt("depth", 3);
            int limit = args.GetInt("limit", 500);
            string nodePath = args.GetValue("path");
            bool extractImages = args.HasFlag("extract-images");

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, extractImages);
                var dto = NodeDto.FromNode(node, depth, limit, extractImages);
                WriteOutput(dto, json, writer => WriteTree(writer, dto, 0));
            }

            return ExitSuccess;
        }

        private static int RunList(ParsedArgs args)
        {
            string input = RequireInput(args, "list <file-or-dir>");
            bool json = args.HasFlag("json");
            string nodePath = args.GetValue("path");
            bool extractImages = args.HasFlag("extract-images");

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, extractImages);
                var items = node.Nodes.Select(NodeDto.FromNodeShallow).ToList();
                WriteOutput(items, json, writer =>
                {
                    foreach (var item in items)
                    {
                        writer.WriteLine(item.Name + "\t" + item.Type + "\tchildren=" + item.ChildrenCount + FormatOptionalValue(item.Value));
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunSearch(ParsedArgs args)
        {
            string input = RequireInput(args, "search <file-or-dir> --name <text>|--value <text>");
            bool json = args.HasFlag("json");
            string nameQuery = args.GetValue("name");
            string valueQuery = args.GetValue("value");
            string nodePath = args.GetValue("path");
            bool extractImages = args.HasFlag("extract-images");
            var searchOptions = SearchOptions.FromArgs(args);

            if (string.IsNullOrEmpty(nameQuery) && string.IsNullOrEmpty(valueQuery) && string.IsNullOrEmpty(searchOptions.PathQuery))
            {
                throw new UsageException("search requires --name <text>, --value <text>, or --match-path <pattern>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, extractImages);
                searchOptions.NameQuery = nameQuery;
                searchOptions.ValueQuery = valueQuery;
                searchOptions.ExtractImages = extractImages;
                var results = Search(node, searchOptions);
                WriteOutput(results, json, writer =>
                {
                    foreach (var result in results)
                    {
                        writer.WriteLine(result.Path + "\t" + result.Type + FormatOptionalValue(result.Value));
                    }
                });
            }

            return ExitSuccess;
        }

        private static string RequireInput(ParsedArgs args, string usage)
        {
            return RequireInputAt(args, 0, usage);
        }

        private static string RequireInputAt(ParsedArgs args, int positionalIndex, string usage)
        {
            if (args.Positionals.Count <= positionalIndex)
            {
                string configuredInput = GetConfiguredInput(args);
                if (!string.IsNullOrEmpty(configuredInput))
                {
                    return configuredInput;
                }

                throw new UsageException("Usage: wcr2 " + usage);
            }
            return args.Positionals[positionalIndex];
        }

        private static string GetConfiguredInput(ParsedArgs args)
        {
            var store = CliConfigStore.Open(args.GetValue("config"));
            string value;
            string profile = GetConfigProfile(args);
            if (!string.IsNullOrEmpty(profile))
            {
                if (store.Values.TryGetValue(BuildProfiledConfigKey(profile, "default-wz"), out value))
                {
                    return value;
                }
                if (store.Values.TryGetValue(BuildProfiledConfigKey(profile, "wz"), out value))
                {
                    return value;
                }
            }
            if (store.Values.TryGetValue("default-wz", out value))
            {
                return value;
            }
            if (store.Values.TryGetValue("wz", out value))
            {
                return value;
            }
            return null;
        }

        private static string GetConfigProfile(ParsedArgs args)
        {
            string profile = args.GetValue("profile");
            if (string.IsNullOrWhiteSpace(profile))
            {
                return null;
            }

            profile = profile.Trim();
            if (!Regex.IsMatch(profile, @"^[A-Za-z0-9_.-]+$"))
            {
                throw new UsageException("Config profile may contain only letters, numbers, dot, underscore, and dash.");
            }
            return profile;
        }

        private static string ResolveConfigKey(string key, string profile)
        {
            if (string.IsNullOrEmpty(profile))
            {
                return key;
            }
            return BuildProfiledConfigKey(profile, key);
        }

        private static string BuildProfiledConfigKey(string profile, string key)
        {
            return "profiles." + profile + "." + key;
        }

        private static Wz_Node ResolveRequiredNode(Wz_Node root, string path, bool extractImages)
        {
            Wz_Node node = NodePath.Resolve(root, path, extractImages);
            if (node == null)
            {
                throw new UsageException("WZ path not found: " + path);
            }
            return node;
        }

        private static List<SearchResultDto> Search(Wz_Node root, SearchOptions options)
        {
            var results = new List<SearchResultDto>();
            var stack = new Stack<Wz_Node>();
            stack.Push(root);

            while (stack.Count > 0 && results.Count < options.MaxResults)
            {
                Wz_Node node = stack.Pop();
                node = NodePath.ExtractImageNode(node, options.ExtractImages);
                if (node == null)
                {
                    continue;
                }

                if (options.Matches(node))
                {
                    results.Add(SearchResultDto.FromNode(node));
                }

                var children = node.Nodes.ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }

            return results;
        }

        private static void WriteTree(TextWriter writer, NodeDto node, int indent)
        {
            writer.Write(new string(' ', indent * 2));
            writer.Write(node.Name);
            writer.Write(" [");
            writer.Write(node.Type);
            writer.Write("] children=");
            writer.Write(node.ChildrenCount);
            if (!string.IsNullOrEmpty(node.Value))
            {
                writer.Write(" value=");
                writer.Write(node.Value);
            }
            writer.WriteLine();

            if (node.Children == null)
            {
                return;
            }

            foreach (var child in node.Children)
            {
                WriteTree(writer, child, indent + 1);
            }
        }

        private static void WriteOutput<T>(T value, bool json, Action<TextWriter> writeText)
        {
            if (QuietOutput)
            {
                return;
            }

            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
            }
            else
            {
                writeText(Console.Out);
            }
        }

        private static void WriteSkillFullOutput(SkillFullDto dto, string format, string output)
        {
            string normalizedFormat = string.IsNullOrEmpty(format) ? "text" : format.ToLowerInvariant();
            if (normalizedFormat == "json")
            {
                WriteDumpOutput(JsonSerializer.Serialize(dto, JsonOptions), output);
                return;
            }
            if (normalizedFormat == "xml")
            {
                WriteDumpOutput(SkillFullXmlWriter.ToXml(dto), output);
                return;
            }
            if (normalizedFormat != "text" && normalizedFormat != "txt")
            {
                throw new UsageException("Unsupported skill full format: " + format);
            }

            if (string.IsNullOrEmpty(output))
            {
                WriteSkillFullText(Console.Out, dto);
                return;
            }

            using (var writer = new StreamWriter(CreateOutputFile(output), false, Encoding.UTF8))
            {
                WriteSkillFullText(writer, dto);
            }
        }

        private static void WriteSkillFullText(TextWriter writer, SkillFullDto dto)
        {
            writer.WriteLine("skill " + dto.Id);
            writer.WriteLine("Mode: " + dto.Mode);
            if (!string.IsNullOrEmpty(dto.SourceProfile))
            {
                writer.WriteLine("SourceProfile: " + dto.SourceProfile);
            }
            if (!string.IsNullOrEmpty(dto.DataInputPath))
            {
                writer.WriteLine("DataInput: " + dto.DataInputPath);
            }
            if (!string.IsNullOrEmpty(dto.StringInputPath))
            {
                writer.WriteLine("StringInput: " + dto.StringInputPath);
            }
            if (!string.IsNullOrEmpty(dto.DataPath))
            {
                writer.WriteLine("Path: " + dto.DataPath);
            }
            if (!string.IsNullOrEmpty(dto.Name))
            {
                writer.WriteLine("Name: " + dto.Name);
            }
            if (!string.IsNullOrEmpty(dto.Description))
            {
                writer.WriteLine("Description: " + dto.Description);
            }
            if (dto.Level.HasValue)
            {
                writer.WriteLine("Level: " + dto.Level + " / " + (dto.MaxLevel.HasValue ? dto.MaxLevel.ToString() : "?"));
            }
            if (!string.IsNullOrEmpty(dto.ResolvedSummary))
            {
                writer.WriteLine("Summary: " + dto.ResolvedSummary);
            }
            if (dto.NextLevel.HasValue && !string.IsNullOrEmpty(dto.NextResolvedSummary))
            {
                writer.WriteLine("NextLevel: " + dto.NextLevel);
                writer.WriteLine("NextSummary: " + dto.NextResolvedSummary);
            }
            writer.WriteLine("Common: " + dto.Common.Count + " Effective: " + dto.EffectiveProperties.Count + " LevelSets: " + dto.LevelProperties.Count);
            writer.WriteLine("StatProperties: " + dto.StatPropertyCount + " VisualBranches: " + dto.VisualBranchCount);
            if (dto.LinkerStatus != null)
            {
                writer.WriteLine("LinkerStatus: " + dto.LinkerStatus.Status + " Resolver: " + dto.LinkerStatus.Resolver);
                writer.WriteLine("LinkerFound: data=" + dto.LinkerStatus.FoundData + " string=" + dto.LinkerStatus.FoundString + " stats=" + dto.LinkerStatus.FoundStats + " visuals=" + dto.LinkerStatus.FoundVisuals + " summary=" + dto.LinkerStatus.FoundSummaryTemplate);
            }
            if (dto.UnresolvedPlaceholders != null && dto.UnresolvedPlaceholders.Count > 0)
            {
                writer.WriteLine("UnresolvedPlaceholders: " + string.Join(", ", dto.UnresolvedPlaceholders));
            }
            writer.WriteLine("Actions: " + dto.Actions.Count + " Icons: " + dto.Icons.Count + " Requirements: " + dto.RequiredSkills.Count);
            foreach (string diagnostic in dto.Diagnostics)
            {
                writer.WriteLine("Diagnostic: " + diagnostic);
            }
        }

        private static string CreateOutputFile(string output)
        {
            string fullPath = Path.GetFullPath(output);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return fullPath;
        }

        private static string FormatOptionalValue(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : "\tvalue=" + value;
        }

    }
}
