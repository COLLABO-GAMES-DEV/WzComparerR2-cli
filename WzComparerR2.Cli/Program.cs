using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml;
using System.Threading;
using WzComparerR2.Patcher;
using WzComparerR2.Patcher.Builder;
using WzComparerR2.WzLib;
using WzComparerR2.Common;
using WzComparerR2.Encoders;

namespace WzComparerR2.Cli
{
    internal static class Program
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

        private static int RunDomainInfo(ParsedArgs args, string kind)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintDomainHelp(kind);
                return ExitSuccess;
            }
            if (!string.Equals(args.Positionals[0], "info", StringComparison.OrdinalIgnoreCase))
            {
                throw new UsageException("Unknown " + kind + " command: " + args.Positionals[0]);
            }
            string input = RequireInputAt(args, 1, kind + " info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]");
            string id = args.GetValue("id");
            bool json = args.HasFlag("json");
            string stringWz = args.GetValue("string-wz");
            if (string.IsNullOrEmpty(id))
            {
                throw new UsageException(kind + " info requires --id <id>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node dataNode = DomainInfoFinder.FindDataNode(context.Root, kind, id);
                if (dataNode == null)
                {
                    throw new UsageException(kind + " id not found: " + id);
                }

                DomainStringInfo stringInfo = null;
                if (!string.IsNullOrEmpty(stringWz))
                {
                    using (var stringContext = WzLoadContext.Load(stringWz, WzLoadOptions.FromArgs(args)))
                    {
                        stringInfo = DomainInfoFinder.FindStringInfo(stringContext.Root, kind, id);
                    }
                }

                var dto = DomainInfoDto.FromNode(kind, id, dataNode, stringInfo);
                WriteOutput(dto, json, writer =>
                {
                    writer.WriteLine(kind + " " + id);
                    writer.WriteLine("Path: " + dto.Path);
                    if (!string.IsNullOrEmpty(dto.Name))
                    {
                        writer.WriteLine("Name: " + dto.Name);
                    }
                    if (!string.IsNullOrEmpty(dto.Description))
                    {
                        writer.WriteLine("Description: " + dto.Description);
                    }
                    writer.WriteLine("Children: " + dto.ChildrenCount + " Properties: " + dto.Properties.Count);
                });
            }

            return ExitSuccess;
        }

        private static int RunSkill(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintSkillHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            if (subCommand == "info")
            {
                return RunDomainInfo(args, "skill");
            }
            if (subCommand == "full" || subCommand == "detail")
            {
                return RunSkillFull(args);
            }

            throw new UsageException("Unknown skill command: " + args.Positionals[0]);
        }

        private static int RunSkillFull(ParsedArgs args)
        {
            string input = RequireInputAt(args, 1, "skill full <skill-wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--level <n>] [--format json|xml|text] [--out <path>]");
            string id = args.GetValue("id");
            if (string.IsNullOrEmpty(id))
            {
                throw new UsageException("skill full requires --id <id>.");
            }

            string format = args.GetValue("format") ?? (args.HasFlag("json") ? "json" : "text");
            string output = args.GetValue("out") ?? args.GetValue("output");
            string stringWz = args.GetValue("string-wz");
            int? level = null;
            string levelText = args.GetValue("level");
            if (!string.IsNullOrEmpty(levelText))
            {
                if (!string.Equals(levelText, "max", StringComparison.OrdinalIgnoreCase))
                {
                    int parsedLevel;
                    if (!int.TryParse(levelText, out parsedLevel) || parsedLevel < 0)
                    {
                        throw new UsageException("skill full --level must be a non-negative integer or max.");
                    }
                    level = parsedLevel;
                }
            }

            DomainStringInfo stringInfo = null;
            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                WzLoadContext stringContext = null;
                try
                {
                    if (!string.IsNullOrEmpty(stringWz))
                    {
                        stringContext = WzLoadContext.Load(stringWz, WzLoadOptions.FromArgs(args));
                        stringInfo = DomainInfoFinder.FindStringInfo(stringContext.Root, "skill", id);
                    }

                    Wz_Node dataNode = DomainInfoFinder.FindDataNode(context.Root, "skill", id);
                    if (dataNode == null)
                    {
                        if (args.HasFlag("allow-string-only") && stringInfo != null)
                        {
                            var stringOnly = SkillFullDto.FromStringOnly(id, stringInfo);
                            WriteSkillFullOutput(stringOnly, format, output);
                            return ExitSuccess;
                        }

                        throw new UsageException("skill id not found: " + id);
                    }

                    var dto = SkillFullDto.FromNode(id, dataNode, stringInfo, level);
                    WriteSkillFullOutput(dto, format, output);
                }
                finally
                {
                    if (stringContext != null)
                    {
                        stringContext.Dispose();
                    }
                }
            }

            return ExitSuccess;
        }

        private static int RunAnimate(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintAnimateHelp();
                return ExitSuccess;
            }
            string subCommand = args.Positionals[0].ToLowerInvariant();
            if (subCommand == "gif")
            {
                return RunAnimateGif(args);
            }
            if (subCommand == "apng")
            {
                return RunAnimateApng(args);
            }
            if (subCommand == "ffmpeg")
            {
                return RunAnimateFfmpeg(args);
            }
            if (subCommand != "frames")
            {
                throw new UsageException("Unknown animate command: " + args.Positionals[0]);
            }
            string input = RequireInputAt(args, 1, "animate frames <wz-file-or-dir> --path <wz-path> --out <dir> [--json]");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("animate frames requires --path <wz-path>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("animate frames requires --out <dir>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                var result = AnimationFrameExporter.ExportFrames(node, output);
                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Frames: " + result.FrameCount);
                    writer.WriteLine("Output: " + result.OutputDirectory);
                    writer.WriteLine("Manifest: " + result.ManifestPath);
                    foreach (var frame in result.Frames)
                    {
                        writer.WriteLine(frame.Index + "\tdelay=" + frame.Delay + "\tfiles=" + frame.Files.Count);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunAnimateFfmpeg(ParsedArgs args)
        {
            string input = RequireInputAt(args, 1, "animate ffmpeg <wz-file-or-dir> --path <wz-path> --out <file> [--ffmpeg <path>] [--ffmpeg-args <format>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("animate ffmpeg requires --path <wz-path>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("animate ffmpeg requires --out <file>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                var result = AnimationGifExporter.ExportFfmpeg(node, output, AnimationGifOptions.FromArgs(args), args.GetValue("ffmpeg"), args.GetValue("ffmpeg-args"));
                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Frames: " + result.FrameCount);
                    writer.WriteLine("Output: " + result.OutputPath);
                    writer.WriteLine("Bytes: " + result.Bytes);
                    writer.WriteLine("Canvas: " + result.Width + "x" + result.Height);
                    foreach (var frame in result.Frames)
                    {
                        writer.WriteLine(frame.Index + "\tdelay=" + frame.Delay + "\tpath=" + frame.SourcePath);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunAnimateApng(ParsedArgs args)
        {
            string input = RequireInputAt(args, 1, "animate apng <wz-file-or-dir> --path <wz-path> --out <file.png> [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--optimize] [--json]");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("animate apng requires --path <wz-path>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("animate apng requires --out <file.png>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                var result = AnimationGifExporter.ExportApng(node, output, AnimationGifOptions.FromArgs(args), args.HasFlag("optimize"));
                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Frames: " + result.FrameCount);
                    writer.WriteLine("Output: " + result.OutputPath);
                    writer.WriteLine("Bytes: " + result.Bytes);
                    writer.WriteLine("Canvas: " + result.Width + "x" + result.Height);
                    foreach (var frame in result.Frames)
                    {
                        writer.WriteLine(frame.Index + "\tdelay=" + frame.Delay + "\tpath=" + frame.SourcePath);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunAnimateGif(ParsedArgs args)
        {
            string input = RequireInputAt(args, 1, "animate gif <wz-file-or-dir> --path <wz-path> --out <file.gif> [--background #RRGGBB|transparent] [--min-alpha <0-255>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("animate gif requires --path <wz-path>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("animate gif requires --out <file.gif>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                var result = AnimationGifExporter.ExportGif(node, output, AnimationGifOptions.FromArgs(args));
                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Frames: " + result.FrameCount);
                    writer.WriteLine("Output: " + result.OutputPath);
                    writer.WriteLine("Bytes: " + result.Bytes);
                    writer.WriteLine("Canvas: " + result.Width + "x" + result.Height);
                    foreach (var frame in result.Frames)
                    {
                        writer.WriteLine(frame.Index + "\tdelay=" + frame.Delay + "\tpath=" + frame.SourcePath);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunAvatar(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintAvatarHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            if (subCommand == "render")
            {
                return RunAvatarRender(args);
            }

            if (subCommand != "inspect" && subCommand != "unpack")
            {
                throw new UsageException("Unknown avatar command: " + args.Positionals[0]);
            }

            string code = args.GetValue("code");
            bool json = args.HasFlag("json") || subCommand == "unpack";
            if (string.IsNullOrEmpty(code))
            {
                throw new UsageException("avatar " + subCommand + " requires --code <code>.");
            }

            var result = AvatarCodeDto.Parse(code);
            WriteOutput(result, json, writer =>
            {
                writer.WriteLine("Avatar code items: " + result.Items.Count);
                foreach (var item in result.Items)
                {
                    writer.WriteLine(item.Id + "\t" + item.Category);
                }
            });
            return ExitSuccess;
        }

        private static int RunAvatarRender(ParsedArgs args)
        {
            if (!args.HasFlag("dry-run"))
            {
                throw new UsageException("avatar render currently supports --dry-run only. Real PNG rendering needs AvatarCommon dependency isolation first.");
            }

            string output = args.GetValue("out");
            if (string.IsNullOrEmpty(output))
            {
                output = args.GetValue("output");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("avatar render requires --out <avatar.png>.");
            }

            var avatarCode = ParseAvatarRenderInput(args);
            if (!avatarCode.IsValid)
            {
                throw new UsageException("avatar render requires --code <code> or --items <ids>.");
            }

            string action = args.GetValue("action");
            if (string.IsNullOrEmpty(action))
            {
                action = "stand1";
            }

            string emotion = args.GetValue("emotion");
            if (string.IsNullOrEmpty(emotion))
            {
                emotion = "default";
            }

            var result = AvatarRenderPlanDto.Create(
                avatarCode,
                output,
                action,
                emotion,
                args.HasFlag("offline"),
                !string.IsNullOrEmpty(args.GetValue("api-key")));

            bool json = args.HasFlag("json") || args.HasFlag("dry-run");
            WriteOutput(result, json, writer =>
            {
                writer.WriteLine("Avatar render dry-run");
                writer.WriteLine("Output: " + result.OutputPath);
                writer.WriteLine("Action: " + result.Action + " Emotion: " + result.Emotion);
                writer.WriteLine("Items: " + result.Items.Count);
                foreach (var candidate in result.Candidates)
                {
                    writer.WriteLine(candidate.Id + "\t" + candidate.Category + "\t" + string.Join(", ", candidate.CandidatePaths));
                }
                foreach (string blocker in result.Blockers)
                {
                    writer.WriteLine("Blocker: " + blocker);
                }
            });

            return ExitSuccess;
        }

        private static AvatarCodeDto ParseAvatarRenderInput(ParsedArgs args)
        {
            var parts = new List<string>();
            string code = args.GetValue("code");
            if (!string.IsNullOrEmpty(code))
            {
                parts.Add(code);
            }

            string items = args.GetValue("items");
            if (!string.IsNullOrEmpty(items))
            {
                parts.Add(items);
            }

            for (int i = 1; i < args.Positionals.Count; i++)
            {
                parts.Add(args.Positionals[i]);
            }

            return AvatarCodeDto.Parse(string.Join(",", parts));
        }

        private static int RunMap(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintMapHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            if (subCommand == "info")
            {
                return RunDomainInfo(args, "map");
            }
            if (subCommand == "render")
            {
                return RunMapRender(args);
            }

            if (subCommand != "objects" && subCommand != "portals" && subCommand != "life" && subCommand != "reactors")
            {
                throw new UsageException("Unknown map command: " + args.Positionals[0]);
            }
            string input = RequireInputAt(args, 1, "map " + subCommand + " <map-wz-file-or-dir> --id <map-id> [--json]");
            string id = args.GetValue("id");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(id))
            {
                throw new UsageException("map " + subCommand + " requires --id <map-id>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node mapNode = DomainInfoFinder.FindDataNode(context.Root, "map", id);
                if (mapNode == null)
                {
                    throw new UsageException("map id not found: " + id);
                }

                var result = MapMetadataDto.FromMapNode(id, mapNode, subCommand);
                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Map " + id);
                    writer.WriteLine("Path: " + result.Path);
                    writer.WriteLine("Portals: " + result.Portals.Count + " Life: " + result.Life.Count + " Reactors: " + result.Reactors.Count + " Objects: " + result.Objects.Count);
                    foreach (var item in result.SelectedItems)
                    {
                        writer.WriteLine(item.Kind + "\t" + item.Index + "\t" + item.Path + FormatOptionalValue(item.Summary));
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunMapRender(ParsedArgs args)
        {
            if (!args.HasFlag("dry-run"))
            {
                throw new UsageException("map render currently supports --dry-run only. Real screenshot rendering needs a headless MonoGame render target first.");
            }

            string id = args.GetValue("id");
            if (string.IsNullOrEmpty(id))
            {
                throw new UsageException("map render requires --id <map-id>.");
            }

            string output = args.GetValue("out");
            if (string.IsNullOrEmpty(output))
            {
                output = args.GetValue("output");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("map render requires --out <map.png>.");
            }

            string input = args.Positionals.Count > 1 ? args.Positionals[1] : null;
            string layer = args.GetValue("layer");
            if (string.IsNullOrEmpty(layer))
            {
                layer = "all";
            }

            var result = MapRenderPlanDto.Create(
                id,
                input,
                output,
                layer,
                args.HasFlag("include-life"),
                args.HasFlag("include-reactor"),
                args.HasFlag("include-tooltip"));

            bool json = args.HasFlag("json") || args.HasFlag("dry-run");
            WriteOutput(result, json, writer =>
            {
                writer.WriteLine("Map render dry-run");
                writer.WriteLine("Map: " + result.Id);
                writer.WriteLine("Output: " + result.OutputPath);
                writer.WriteLine("Layer: " + result.Layer);
                foreach (string path in result.CandidatePaths)
                {
                    writer.WriteLine("Candidate: " + path);
                }
                foreach (string blocker in result.Blockers)
                {
                    writer.WriteLine("Blocker: " + blocker);
                }
            });

            return ExitSuccess;
        }

        private static int RunLua(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintLuaHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            if (subCommand != "run" && subCommand != "eval")
            {
                throw new UsageException("Unknown lua command: " + args.Positionals[0]);
            }
            if (subCommand == "run" && args.Positionals.Count < 2)
            {
                throw new UsageException("Usage: wcr2 lua run <script.lua> [--wz <file-or-dir>] [--dry-run] [--json]");
            }

            string wzInput = args.GetValue("wz");
            bool json = args.HasFlag("json");
            bool dryRun = args.HasFlag("dry-run");
            int timeoutSeconds = args.GetInt("timeout", 30);

            LuaRunResultDto result;
            if (subCommand == "eval")
            {
                string code = args.GetValue("code");
                if (string.IsNullOrEmpty(code) && args.Positionals.Count > 1)
                {
                    code = string.Join(" ", args.Positionals.Skip(1));
                }
                if (string.IsNullOrEmpty(code))
                {
                    throw new UsageException("lua eval requires <code> or --code <code>.");
                }
                result = LuaRunResultDto.CreateEval(code, wzInput);
            }
            else
            {
                string scriptPath = args.Positionals[1];
                result = LuaRunResultDto.Create(scriptPath, wzInput);
                if (!File.Exists(result.ScriptPath))
                {
                    throw new FileNotFoundException("Lua script not found: " + scriptPath);
                }
            }

            if (!string.IsNullOrEmpty(wzInput))
            {
                using (var context = WzLoadContext.Load(wzInput, WzLoadOptions.FromArgs(args)))
                {
                    result.WzRootName = context.Root.Text;
                    result.WzRootChildren = context.Root.Nodes.Count;
                }
            }

            if (dryRun)
            {
                result.Mode = "dry-run";
                result.ExitCode = 0;
            }
            else
            {
                LuaExternalRunner.Run(result, timeoutSeconds);
            }

            WriteOutput(result, json, writer =>
            {
                writer.WriteLine("Lua: " + (result.IsEval ? "eval" : result.ScriptPath));
                writer.WriteLine("Mode: " + result.Mode + " ExitCode: " + result.ExitCode);
                if (result.IsEval)
                {
                    writer.WriteLine("Code: " + result.Code);
                }
                if (!string.IsNullOrEmpty(result.LuaExecutable))
                {
                    writer.WriteLine("Executable: " + result.LuaExecutable);
                }
                if (!string.IsNullOrEmpty(result.WzInputPath))
                {
                    writer.WriteLine("WZ: " + result.WzInputPath + " root=" + result.WzRootName);
                }
                if (!string.IsNullOrEmpty(result.Stdout))
                {
                    writer.WriteLine(result.Stdout);
                }
                if (!string.IsNullOrEmpty(result.Stderr))
                {
                    writer.WriteLine(result.Stderr);
                }
            });

            return result.ExitCode == 0 ? ExitSuccess : ExitInternalError;
        }

        private static int RunNetwork(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintNetworkHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            bool json = args.HasFlag("json");
            var result = NetworkCommandDto.FromArgs(subCommand, args);

            if (subCommand == "server-info")
            {
                if (args.HasFlag("interactive"))
                {
                    throw new UsageException("network server-info does not support --interactive.");
                }
                if (args.HasFlag("connect"))
                {
                    NetworkProbe.TryConnect(result, args.GetInt("timeout", 5));
                }
            }
            else if (subCommand == "chat")
            {
                if (args.HasFlag("interactive"))
                {
                    throw new UsageException("network chat --interactive is not implemented. Use the GUI network plugin for live chat.");
                }
                result.Message = "Non-interactive chat dry-run prepared. Interactive chat is not enabled in CLI yet.";
            }
            else if (subCommand == "send")
            {
                if (args.HasFlag("interactive"))
                {
                    throw new UsageException("network send does not support --interactive.");
                }
                if (string.IsNullOrEmpty(args.GetValue("message")))
                {
                    throw new UsageException("network send requires --message <text>.");
                }
                result.Message = "Non-interactive send dry-run prepared. Use the GUI network plugin for live chat until protocol handshakes are wired into CLI.";
            }
            else
            {
                throw new UsageException("Unknown network command: " + args.Positionals[0]);
            }

            WriteOutput(result, json, writer =>
            {
                writer.WriteLine("Network " + result.Command);
                writer.WriteLine("Host: " + result.Host + " Port: " + result.Port);
                writer.WriteLine("Mode: " + result.Mode);
                if (!string.IsNullOrEmpty(result.Message))
                {
                    writer.WriteLine(result.Message);
                }
                if (!string.IsNullOrEmpty(result.Error))
                {
                    writer.WriteLine("Error: " + result.Error);
                }
            });

            return result.Success ? ExitSuccess : ExitInternalError;
        }

        private static async System.Threading.Tasks.Task<int> RunUpdate(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintUpdateHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            if (subCommand != "check" && subCommand != "download" && subCommand != "apply")
            {
                throw new UsageException("Unknown update command: " + args.Positionals[0]);
            }

            if (subCommand == "download" && string.IsNullOrEmpty(args.GetValue("out")))
            {
                throw new UsageException("update download requires --out <dir>.");
            }

            string assetKind = args.GetValue("asset") ?? "net8";
            if (!IsUpdateAssetKind(assetKind))
            {
                throw new UsageException("--asset must be net8, net10, net6, net462, or zip.");
            }
            var result = await UpdateClient.QueryLatestAsync(args).ConfigureAwait(false);
            result.SelectedAsset = result.SelectAsset(assetKind);

            if (subCommand == "download")
            {
                if (result.SelectedAsset == null)
                {
                    throw new UsageException("No update asset matched --asset " + assetKind + ".");
                }

                string outputPath = await UpdateClient.DownloadAssetAsync(result.SelectedAsset, args.GetValue("out"), args.HasFlag("force"), args.GetInt("timeout", 30)).ConfigureAwait(false);
                var download = UpdateDownloadResultDto.FromRelease(result, outputPath);
                WriteOutput(download, args.HasFlag("json"),
                    writer =>
                    {
                        writer.WriteLine("Downloaded: " + download.OutputPath);
                        writer.WriteLine("Asset: " + download.Asset.Name + " (" + download.Asset.Size + " bytes)");
                    });
                return ExitSuccess;
            }

            if (subCommand == "apply")
            {
                var plan = UpdateApplyPlanDto.FromRelease(result, args, assetKind);
                if (args.HasFlag("execute"))
                {
                    await UpdateClient.ExecuteApplyAsync(plan, args.HasFlag("force"), args.GetInt("timeout", 30)).ConfigureAwait(false);
                }

                WriteOutput(plan, args.HasFlag("json"),
                    writer =>
                    {
                        writer.WriteLine("Update apply mode: " + plan.Mode);
                        writer.WriteLine("Release: " + plan.Release.TagName + " updateAvailable=" + FormatNullableBool(plan.Release.UpdateAvailable));
                        writer.WriteLine("Asset: " + (plan.Asset == null ? "(none)" : plan.Asset.Name));
                        writer.WriteLine("Updater: " + (plan.UpdaterPath ?? "(required for --execute)"));
                        writer.WriteLine(plan.Message);
                    });
                return plan.Success ? ExitSuccess : ExitUsage;
            }

            WriteOutput(result, args.HasFlag("json"),
                writer =>
                {
                    writer.WriteLine("Repository: " + result.Repository);
                    writer.WriteLine("Current: " + (result.CurrentVersion ?? "(unknown)"));
                    writer.WriteLine("Latest: " + (result.TagName ?? result.Name));
                    writer.WriteLine("Update available: " + FormatNullableBool(result.UpdateAvailable));
                    if (result.SelectedAsset != null)
                    {
                        writer.WriteLine("Selected asset: " + result.SelectedAsset.Name);
                        writer.WriteLine("Download: " + result.SelectedAsset.BrowserDownloadUrl);
                    }
            });
            return ExitSuccess;
        }

        private static int RunConfig(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintConfigHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            var store = CliConfigStore.Open(args.GetValue("config"));
            string profile = GetConfigProfile(args);

            switch (subCommand)
            {
                case "path":
                {
                    var result = ConfigPathDto.FromStore(store);
                    WriteOutput(result, args.HasFlag("json"),
                        writer =>
                        {
                            writer.WriteLine(result.Path);
                            writer.WriteLine("exists=" + result.Exists.ToString().ToLowerInvariant());
                        });
                    return ExitSuccess;
                }
                case "list":
                {
                    var result = ConfigListDto.FromStore(store, profile);
                    WriteOutput(result, args.HasFlag("json"),
                        writer =>
                        {
                            if (result.Values.Count == 0)
                            {
                                writer.WriteLine("(empty)");
                                return;
                            }

                            foreach (var item in result.Values)
                            {
                                writer.WriteLine(item.Key + "=" + item.Value);
                            }
                        });
                    return ExitSuccess;
                }
                case "get":
                {
                    if (args.Positionals.Count < 2)
                    {
                        throw new UsageException("Usage: wcr2 config get <key> [--json]");
                    }

                    string key = ResolveConfigKey(args.Positionals[1], profile);
                    var result = ConfigValueDto.FromStore(store, key);
                    WriteOutput(result, args.HasFlag("json"),
                        writer =>
                        {
                            if (result.Found)
                            {
                                writer.WriteLine(result.Value);
                            }
                            else
                            {
                                writer.WriteLine("Config key not found: " + key);
                            }
                        });
                    return result.Found ? ExitSuccess : ExitNotFound;
                }
                case "set":
                {
                    if (args.Positionals.Count < 3)
                    {
                        throw new UsageException("Usage: wcr2 config set <key> <value> [--json]");
                    }

                    string key = ResolveConfigKey(args.Positionals[1], profile);
                    string value = args.Positionals[2];
                    store.Set(key, value);
                    store.Save();
                    var result = ConfigValueDto.FromStore(store, key);
                    WriteOutput(result, args.HasFlag("json"),
                        writer => writer.WriteLine(result.Key + "=" + result.Value));
                    return ExitSuccess;
                }
                case "unset":
                {
                    if (args.Positionals.Count < 2)
                    {
                        throw new UsageException("Usage: wcr2 config unset <key> [--json]");
                    }

                    string key = ResolveConfigKey(args.Positionals[1], profile);
                    bool removed = store.Unset(key);
                    store.Save();
                    var result = new ConfigUnsetDto
                    {
                        Path = store.Path,
                        Key = key,
                        Removed = removed,
                        Count = store.Values.Count
                    };
                    WriteOutput(result, args.HasFlag("json"),
                        writer => writer.WriteLine((removed ? "removed " : "not found ") + key));
                    return ExitSuccess;
                }
                default:
                    throw new UsageException("Unknown config command: " + args.Positionals[0]);
            }
        }

        private static int RunPlugin(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintPluginHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            bool json = args.HasFlag("json");

            switch (subCommand)
            {
                case "list":
                {
                    var result = CliPluginRegistry.Discover(args);
                    WriteOutput(result, json,
                        writer =>
                        {
                            writer.WriteLine("Plugin directories:");
                            foreach (string directory in result.Directories)
                            {
                                writer.WriteLine("- " + directory);
                            }
                            writer.WriteLine("Plugins: " + result.Plugins.Count + " loadFailed=" + result.LoadFailedCount);
                            foreach (var plugin in result.Plugins)
                            {
                                writer.WriteLine(plugin.Status + "\tcommands=" + plugin.Commands.Count + "\t" + plugin.Path);
                                if (!string.IsNullOrEmpty(plugin.Error))
                                {
                                    writer.WriteLine("  error: " + plugin.Error);
                                }
                            }
                        });
                    return ExitSuccess;
                }
                case "inspect":
                {
                    if (args.Positionals.Count < 2)
                    {
                        throw new UsageException("Usage: wcr2 plugin inspect <assembly.dll> [--json]");
                    }

                    var result = CliPluginRegistry.InspectFile(args.Positionals[1]);
                    WriteOutput(result, json,
                        writer =>
                        {
                            writer.WriteLine(result.Status + "\t" + result.Path);
                            writer.WriteLine("Assembly: " + (result.AssemblyName ?? "(unknown)"));
                            writer.WriteLine("Version: " + (result.AssemblyVersion ?? "(unknown)"));
                            writer.WriteLine("CLI providers: " + result.ProviderTypes.Count + " GUI entries: " + result.GuiPluginEntryTypes.Count);
                            foreach (var command in result.Commands)
                            {
                                writer.WriteLine(command.Name + "\t" + command.ProviderType + FormatOptionalValue(command.Summary));
                            }
                            if (!string.IsNullOrEmpty(result.Error))
                            {
                                writer.WriteLine("Error: " + result.Error);
                            }
                        });
                    return result.Success ? ExitSuccess : ExitInternalError;
                }
                case "commands":
                {
                    var result = CliPluginRegistry.Discover(args);
                    var commands = result.Plugins
                        .SelectMany(plugin => plugin.Commands.Select(command => new CliPluginCommandListItemDto
                        {
                            PluginPath = plugin.Path,
                            ProviderType = command.ProviderType,
                            Name = command.Name,
                            Summary = command.Summary,
                            Usage = command.Usage
                        }))
                        .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(command => command.PluginPath, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    WriteOutput(commands, json,
                        writer =>
                        {
                            if (commands.Count == 0)
                            {
                                writer.WriteLine("(no cli plugin commands)");
                                return;
                            }

                            foreach (var command in commands)
                            {
                                writer.WriteLine(command.Name + "\t" + command.PluginPath + FormatOptionalValue(command.Summary));
                            }
                        });
                    return ExitSuccess;
                }
                case "run":
                {
                    if (args.Positionals.Count < 2)
                    {
                        throw new UsageException("Usage: wcr2 plugin run <command> [args...] [--plugin-dir <dir>]");
                    }

                    string commandName = args.Positionals[1];
                    var result = CliPluginRegistry.Execute(args, commandName, GetPluginCommandArguments(args));
                    WriteOutput(result, json,
                        writer =>
                        {
                            writer.WriteLine("Plugin command: " + result.Command);
                            writer.WriteLine("Provider: " + (result.ProviderType ?? "(not found)"));
                            writer.WriteLine("ExitCode: " + result.ExitCode);
                            if (!string.IsNullOrEmpty(result.Message))
                            {
                                writer.WriteLine(result.Message);
                            }
                            if (!string.IsNullOrEmpty(result.Error))
                            {
                                writer.WriteLine("Error: " + result.Error);
                            }
                        });
                    return result.ExitCode;
                }
                default:
                    throw new UsageException("Unknown plugin command: " + args.Positionals[0]);
            }
        }

        private static int RunPatch(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintPatchHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            switch (subCommand)
            {
                case "inspect":
                    return RunPatchInspect(args);
                case "dry-run":
                    return RunPatchDryRun(args);
                case "apply":
                    return RunPatchApply(args);
                default:
                    throw new UsageException("Unknown patch command: " + subCommand);
            }
        }

        private static int RunPatchInspect(ParsedArgs args)
        {
            if (args.Positionals.Count < 2)
            {
                throw new UsageException("Usage: wcr2 patch inspect <patch-file> [--json]");
            }

            string patchFile = args.Positionals[1];
            bool json = args.HasFlag("json");
            string output = args.GetValue("out") ?? args.GetValue("output");

            if (!File.Exists(patchFile))
            {
                throw new FileNotFoundException("Input path not found: " + patchFile);
            }

            using (var patcher = new WzPatcher(patchFile))
            {
                long dataPosition = patcher.PrePatch(CancellationToken.None);
                var result = PatchInspectResultDto.FromPatcher(Path.GetFullPath(patchFile), dataPosition, patcher);
                if (!string.IsNullOrEmpty(output))
                {
                    result.OutputPath = WriteJsonFile(result, output);
                }

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Patch: " + result.PatchFilePath);
                    writer.WriteLine("Parts: " + result.PartCount + " Create: " + result.CreateCount + " Rebuild: " + result.RebuildCount + " Delete: " + result.DeleteCount);
                    writer.WriteLine("KMST1125: " + FormatNullableBool(result.IsKmst1125Format));
                    writer.WriteLine("Notice length: " + result.NoticeLength);
                    if (!string.IsNullOrEmpty(result.OutputPath))
                    {
                        writer.WriteLine("Output: " + result.OutputPath);
                    }
                    foreach (var part in result.Parts)
                    {
                        writer.WriteLine(part.Type + "\t" + part.FileName + "\tnewLength=" + part.NewFileLength + "\toldChecksum=" + part.OldChecksum + "\tnewChecksum=" + part.NewChecksum);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunPatchDryRun(ParsedArgs args)
        {
            if (args.Positionals.Count < 2)
            {
                throw new UsageException("Usage: wcr2 patch dry-run <patch-file> --target <dir> [--json]");
            }

            string patchFile = args.Positionals[1];
            string target = args.GetValue("target");
            bool json = args.HasFlag("json");
            string output = args.GetValue("out") ?? args.GetValue("output");

            if (string.IsNullOrEmpty(target))
            {
                throw new UsageException("patch dry-run requires --target <dir>.");
            }
            if (!File.Exists(patchFile))
            {
                throw new FileNotFoundException("Input path not found: " + patchFile);
            }
            if (!Directory.Exists(target))
            {
                throw new DirectoryNotFoundException("Target directory not found: " + target);
            }

            using (var patcher = new WzPatcher(patchFile))
            {
                long dataPosition = patcher.PrePatch(CancellationToken.None);
                var inspect = PatchInspectResultDto.FromPatcher(Path.GetFullPath(patchFile), dataPosition, patcher);
                var result = PatchDryRunResultDto.FromInspect(inspect, Path.GetFullPath(target), patcher.PatchParts);

                if (!string.IsNullOrEmpty(output))
                {
                    result.OutputPath = WriteJsonFile(result, output);
                }

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Patch: " + result.PatchFilePath);
                    writer.WriteLine("Target: " + result.TargetDirectory);
                    writer.WriteLine("Actions: create=" + result.CreateCount + " rebuild=" + result.RebuildCount + " delete=" + result.DeleteCount);
                    writer.WriteLine("Validation: ok=" + result.ValidCount + " missing=" + result.MissingCount + " mismatch=" + result.ChecksumMismatchCount + " unchecked=" + result.UncheckedCount);
                    if (!string.IsNullOrEmpty(result.OutputPath))
                    {
                        writer.WriteLine("Output: " + result.OutputPath);
                    }
                    foreach (var action in result.Actions)
                    {
                        writer.WriteLine(action.Action + "\t" + action.Status + "\t" + action.FileName);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunPatchApply(ParsedArgs args)
        {
            if (args.Positionals.Count < 2)
            {
                throw new UsageException("Usage: wcr2 patch apply <patch-file> --target <dir> --out <dir> [--log <file>] [--json]");
            }

            string patchFile = args.Positionals[1];
            string target = args.GetValue("target");
            string output = args.GetValue("out") ?? args.GetValue("output");
            string logPath = args.GetValue("log");
            bool json = args.HasFlag("json");

            if (string.IsNullOrEmpty(target))
            {
                throw new UsageException("patch apply requires --target <dir>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("patch apply requires --out <dir>.");
            }
            if (!File.Exists(patchFile))
            {
                throw new FileNotFoundException("Input path not found: " + patchFile);
            }
            if (!Directory.Exists(target))
            {
                throw new DirectoryNotFoundException("Target directory not found: " + target);
            }

            string targetFullPath = Path.GetFullPath(target);
            string outputFullPath = Path.GetFullPath(output);
            ValidatePatchOutputDirectory(targetFullPath, outputFullPath);

            using (var patcher = new WzPatcher(patchFile))
            {
                long dataPosition = patcher.PrePatch(CancellationToken.None);
                var inspect = PatchInspectResultDto.FromPatcher(Path.GetFullPath(patchFile), dataPosition, patcher);
                var dryRun = PatchDryRunResultDto.FromInspect(inspect, targetFullPath, patcher.PatchParts);

                Directory.CreateDirectory(outputFullPath);
                var copyStats = CopyDirectory(targetFullPath, outputFullPath);

                var result = PatchApplyResultDto.FromDryRun(dryRun, outputFullPath, copyStats);
                var events = new List<PatchApplyEventDto>();
                patcher.PatchingStateChanged += (sender, eventArgs) =>
                {
                    if (eventArgs != null)
                    {
                        events.Add(PatchApplyEventDto.FromEvent(eventArgs));
                    }
                };

                patcher.Patch(outputFullPath, outputFullPath, CancellationToken.None);
                result.Events = events;
                result.EventCount = events.Count;

                if (!string.IsNullOrEmpty(logPath))
                {
                    result.LogPath = WritePatchApplyLog(result, logPath);
                }

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Patch: " + result.PatchFilePath);
                    writer.WriteLine("Target: " + result.TargetDirectory);
                    writer.WriteLine("Output: " + result.OutputDirectory);
                    writer.WriteLine("Copied files: " + result.CopiedFileCount + " bytes=" + result.CopiedBytes);
                    writer.WriteLine("Patch parts: " + result.PartCount + " events=" + result.EventCount);
                    if (!string.IsNullOrEmpty(result.LogPath))
                    {
                        writer.WriteLine("Log: " + result.LogPath);
                    }
                });
            }

            return ExitSuccess;
        }

        private static void ValidatePatchOutputDirectory(string targetFullPath, string outputFullPath)
        {
            if (PathsEqual(targetFullPath, outputFullPath)
                || IsSubPathOf(outputFullPath, targetFullPath)
                || IsSubPathOf(targetFullPath, outputFullPath))
            {
                throw new UsageException("--out must be separate from --target.");
            }

            if (Directory.Exists(outputFullPath) && Directory.EnumerateFileSystemEntries(outputFullPath).Any())
            {
                throw new UsageException("--out directory must not exist or must be empty.");
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(TrimDirectorySeparator(left), TrimDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSubPathOf(string path, string possibleParent)
        {
            string normalizedPath = TrimDirectorySeparator(path) + Path.DirectorySeparatorChar;
            string normalizedParent = TrimDirectorySeparator(possibleParent) + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase)
                && !PathsEqual(path, possibleParent);
        }

        private static string TrimDirectorySeparator(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static DirectoryCopyStats CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            var stats = new DirectoryCopyStats();
            foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
            }

            foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDirectory, file);
                string destination = Path.Combine(destinationDirectory, relative);
                string destinationParent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationParent))
                {
                    Directory.CreateDirectory(destinationParent);
                }
                File.Copy(file, destination, false);
                stats.FileCount++;
                stats.Bytes += new FileInfo(file).Length;
            }

            return stats;
        }

        private static string WritePatchApplyLog(PatchApplyResultDto result, string logPath)
        {
            string fullPath = Path.GetFullPath(logPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var writer = new StreamWriter(fullPath))
            {
                writer.WriteLine("Patch: " + result.PatchFilePath);
                writer.WriteLine("Target: " + result.TargetDirectory);
                writer.WriteLine("Output: " + result.OutputDirectory);
                writer.WriteLine("CopiedFiles: " + result.CopiedFileCount);
                writer.WriteLine("CopiedBytes: " + result.CopiedBytes);
                writer.WriteLine();
                foreach (var evt in result.Events)
                {
                    writer.WriteLine(evt.State + "\t" + evt.FileName + "\t" + evt.CurrentFileLength);
                }
            }

            return fullPath;
        }

        private static int RunCompare(ParsedArgs args)
        {
            if (args.Positionals.Count < 2)
            {
                throw new UsageException("Usage: wcr2 compare <old-file-or-dir> <new-file-or-dir> [--path <wz-path>] [--type added|removed|changed] [--out <json>] [--json]");
            }

            string oldInput = args.Positionals[0];
            string newInput = args.Positionals[1];
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            string format = args.GetValue("format");
            bool json = args.HasFlag("json");
            bool extractImages = args.HasFlag("extract-images");
            var options = CompareOptions.FromArgs(args);
            string outputFormat = ResolveCompareOutputFormat(format, output);

            using (var oldContext = WzLoadContext.Load(oldInput, WzLoadOptions.FromArgs(args)))
            using (var newContext = WzLoadContext.Load(newInput, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node oldNode = NodePath.Resolve(oldContext.Root, nodePath, extractImages);
                Wz_Node newNode = NodePath.Resolve(newContext.Root, nodePath, extractImages);

                if (oldNode == null && newNode == null)
                {
                    throw new UsageException("WZ path not found in either input: " + nodePath);
                }

                var result = CompareResultDto.Create(oldContext.InputPath, newContext.InputPath, nodePath);
                result.MaxResults = options.MaxResults;
                result.IgnoreImageBinary = options.IgnoreImageBinary;
                CompareNodes(oldNode, newNode, options, result, extractImages);

                if (!string.IsNullOrEmpty(output))
                {
                    result.OutputPath = WriteCompareFile(result, output, outputFormat);
                }

                if (string.IsNullOrEmpty(output) && IsMarkdownFormat(outputFormat))
                {
                    Console.WriteLine(FormatCompareMarkdown(result));
                    return ExitSuccess;
                }

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Compared: " + result.OldInputPath);
                    writer.WriteLine("With: " + result.NewInputPath);
                    writer.WriteLine("Added: " + result.Added + " Removed: " + result.Removed + " Changed: " + result.Changed);
                    if (!string.IsNullOrEmpty(result.OutputPath))
                    {
                        writer.WriteLine("Output: " + result.OutputPath);
                    }
                    if (result.Truncated)
                    {
                        writer.WriteLine("Results truncated at " + result.MaxResults + " item(s).");
                    }
                    foreach (var diff in result.Differences)
                    {
                        writer.WriteLine(diff.ChangeType + "\t" + diff.Path + "\t" + diff.OldType + " -> " + diff.NewType);
                    }
                });
            }

            return ExitSuccess;
        }

        private static string WriteJsonFile<T>(T value, string output)
        {
            string fullPath = Path.GetFullPath(output);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, JsonSerializer.Serialize(value, JsonOptions));
            return fullPath;
        }

        private static string WriteCompareFile(CompareResultDto result, string output, string format)
        {
            string fullPath = Path.GetFullPath(output);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string content = IsMarkdownFormat(format)
                ? FormatCompareMarkdown(result)
                : JsonSerializer.Serialize(result, JsonOptions);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        private static string ResolveCompareOutputFormat(string format, string output)
        {
            if (!string.IsNullOrEmpty(format))
            {
                if (IsMarkdownFormat(format) || string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                {
                    return format;
                }
                throw new UsageException("Unsupported compare format: " + format);
            }

            string ext = string.IsNullOrEmpty(output) ? null : Path.GetExtension(output);
            return string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".markdown", StringComparison.OrdinalIgnoreCase)
                ? "markdown"
                : "json";
        }

        private static bool IsMarkdownFormat(string format)
        {
            return string.Equals(format, "md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatCompareMarkdown(CompareResultDto result)
        {
            var writer = new System.Text.StringBuilder();
            writer.AppendLine("# WzComparerR2 Compare Report");
            writer.AppendLine();
            writer.AppendLine("- Old: `" + EscapeMarkdownInline(result.OldInputPath) + "`");
            writer.AppendLine("- New: `" + EscapeMarkdownInline(result.NewInputPath) + "`");
            if (!string.IsNullOrEmpty(result.Path))
            {
                writer.AppendLine("- Path: `" + EscapeMarkdownInline(result.Path) + "`");
            }
            writer.AppendLine("- Added: " + result.Added);
            writer.AppendLine("- Removed: " + result.Removed);
            writer.AppendLine("- Changed: " + result.Changed);
            writer.AppendLine("- Truncated: " + result.Truncated.ToString().ToLowerInvariant());
            writer.AppendLine();
            writer.AppendLine("| Change | Path | Old | New |");
            writer.AppendLine("| --- | --- | --- | --- |");

            foreach (var diff in result.Differences)
            {
                writer.Append("| ");
                writer.Append(EscapeMarkdownCell(diff.ChangeType));
                writer.Append(" | `");
                writer.Append(EscapeMarkdownInline(diff.Path));
                writer.Append("` | ");
                writer.Append(EscapeMarkdownCell(FormatCompareSide(diff.OldType, diff.OldValue)));
                writer.Append(" | ");
                writer.Append(EscapeMarkdownCell(FormatCompareSide(diff.NewType, diff.NewValue)));
                writer.AppendLine(" |");
            }

            return writer.ToString();
        }

        private static string FormatCompareSide(string type, string value)
        {
            if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return string.IsNullOrEmpty(value) ? type : type + " " + value;
        }

        private static string EscapeMarkdownInline(string value)
        {
            return (value ?? string.Empty).Replace("`", "\\`");
        }

        private static string EscapeMarkdownCell(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private static int RunDump(ParsedArgs args)
        {
            string input = RequireInput(args, "dump <file-or-dir> --path <wz-path> [--format json|xml|raw]");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            string format = args.GetValue("format") ?? "json";
            int depth = args.GetInt("depth", 10);
            int limit = args.GetInt("limit", 5000);

            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("dump requires --path <wz-path>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                {
                    string json = JsonSerializer.Serialize(NodeDto.FromNode(node, depth, limit, true), JsonOptions);
                    WriteDumpOutput(json, output);
                }
                else if (string.Equals(format, "xml", StringComparison.OrdinalIgnoreCase))
                {
                    string xml = DumpXmlToString(node);
                    WriteDumpOutput(xml, output);
                }
                else if (string.Equals(format, "raw", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(output))
                    {
                        throw new UsageException("dump --format raw requires --out <output-dir>.");
                    }

                    var result = ExtractResultDto.Create(input, node);
                    result.Files.AddRange(ExtractExporter.ExportAuto(node, output, false));
                    if (result.Files.Count == 0)
                    {
                        throw new UsageException("Selected node has no raw exportable value.");
                    }

                    WriteOutput(result, args.HasFlag("json"), writer =>
                    {
                        foreach (var file in result.Files)
                        {
                            writer.WriteLine(file.OutputPath);
                        }
                    });
                }
                else
                {
                    throw new UsageException("Unsupported dump format: " + format);
                }
            }

            return ExitSuccess;
        }

        private static int RunExtract(ParsedArgs args)
        {
            string input = RequireInput(args, "extract <file-or-dir> --path <wz-path> --out <output-dir>");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            bool recursive = args.HasFlag("recursive");
            bool json = args.HasFlag("json");
            string format = args.GetValue("format");
            string manifest = args.GetValue("manifest");

            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("extract requires --path <wz-path>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("extract requires --out <output-dir>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                var result = ExtractResultDto.Create(input, node);

                if (string.Equals(format, "xml", StringComparison.OrdinalIgnoreCase) || args.HasFlag("xml"))
                {
                    result.Files.Add(ExtractExporter.ExportXml(node, output));
                }
                else
                {
                    result.Files.AddRange(ExtractExporter.ExportAuto(node, output, recursive));
                    if (result.Files.Count == 0)
                    {
                        throw new UsageException("Selected node has no exportable value. Use --recursive for containers.");
                    }
                }

                if (!string.IsNullOrEmpty(manifest))
                {
                    result.ManifestPath = ExtractExporter.WriteManifest(result, manifest);
                }

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Extracted " + result.Files.Count + " file(s) from " + result.SourcePath);
                    if (!string.IsNullOrEmpty(result.ManifestPath))
                    {
                        writer.WriteLine("Manifest: " + result.ManifestPath);
                    }
                    foreach (var file in result.Files)
                    {
                        writer.WriteLine("- " + file.Type + "\t" + file.OutputPath);
                    }
                });
            }

            return ExitSuccess;
        }

        private static void WriteDumpOutput(string content, string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                Console.WriteLine(content);
                return;
            }

            string fullPath = Path.GetFullPath(output);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(fullPath, content);
            Console.WriteLine(fullPath);
        }

        private static string DumpXmlToString(Wz_Node node)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true
            };
            using (var writer = new StringWriter())
            {
                using (var xmlWriter = XmlWriter.Create(writer, settings))
                {
                    node.DumpAsXml(xmlWriter);
                }
                return writer.ToString();
            }
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

        private static void CompareNodes(Wz_Node oldNode, Wz_Node newNode, CompareOptions options, CompareResultDto result, bool extractImages)
        {
            var stack = new Stack<NodePair>();
            stack.Push(new NodePair(oldNode, newNode));

            while (stack.Count > 0)
            {
                NodePair pair = stack.Pop();
                Wz_Node left = NodePath.ExtractImageNode(pair.OldNode, extractImages);
                Wz_Node right = NodePath.ExtractImageNode(pair.NewNode, extractImages);

                CompareChangeDto diff = CompareChangeDto.FromNodes(left, right);
                if (diff != null)
                {
                    result.Count(diff.ChangeType);
                    if (options.Includes(diff.ChangeType))
                    {
                        if (result.Differences.Count >= options.MaxResults)
                        {
                            result.Truncated = true;
                            continue;
                        }
                        result.Differences.Add(diff);
                    }
                }

                if (result.Truncated)
                {
                    continue;
                }

                foreach (var childPair in EnumerateChildPairs(left, right).Reverse())
                {
                    stack.Push(childPair);
                }
            }
        }

        private static IEnumerable<NodePair> EnumerateChildPairs(Wz_Node oldNode, Wz_Node newNode)
        {
            var oldChildren = BuildNodeMap(oldNode);
            var newChildren = BuildNodeMap(newNode);
            var names = new SortedSet<string>(oldChildren.Keys, StringComparer.OrdinalIgnoreCase);
            names.UnionWith(newChildren.Keys);

            foreach (string name in names)
            {
                Wz_Node oldChild;
                Wz_Node newChild;
                oldChildren.TryGetValue(name, out oldChild);
                newChildren.TryGetValue(name, out newChild);
                yield return new NodePair(oldChild, newChild);
            }
        }

        private static Dictionary<string, Wz_Node> BuildNodeMap(Wz_Node node)
        {
            var map = new Dictionary<string, Wz_Node>(StringComparer.OrdinalIgnoreCase);
            if (node == null)
            {
                return map;
            }

            foreach (Wz_Node child in node.Nodes)
            {
                if (!map.ContainsKey(child.Text))
                {
                    map.Add(child.Text, child);
                }
            }
            return map;
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

        private static bool IsHelp(string arg)
        {
            return string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUpdateAssetKind(string value)
        {
            switch ((value ?? string.Empty).ToLowerInvariant())
            {
                case "net8":
                case "net10":
                case "net6":
                case "net462":
                case "zip":
                    return true;
                default:
                    return false;
            }
        }

        private static IReadOnlyList<string> GetPluginCommandArguments(ParsedArgs args)
        {
            var result = new List<string>();
            var tail = args.GetTailArguments(2);
            for (int i = 0; i < tail.Count; i++)
            {
                string arg = tail[i];
                if (string.Equals(arg, "--", StringComparison.Ordinal))
                {
                    result.AddRange(tail.Skip(i + 1));
                    break;
                }

                if (IsPluginHostOptionWithValue(arg))
                {
                    i++;
                    continue;
                }

                if (IsPluginHostFlag(arg))
                {
                    continue;
                }

                result.Add(arg);
            }
            return result;
        }

        private static bool IsPluginHostOptionWithValue(string arg)
        {
            return string.Equals(arg, "--plugin-dir", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPluginHostFlag(string arg)
        {
            return string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--include-gui-plugin-dir", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("wcr2 - WzComparerR2 command line tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 info <file-or-dir> [--json]");
            Console.WriteLine("  wcr2 tree <file-or-dir> [--path <wz-path>] [--depth <n>] [--limit <n>] [--json]");
            Console.WriteLine("  wcr2 list <file-or-dir> [--path <wz-path>] [--json]");
            Console.WriteLine("  wcr2 search <file-or-dir> --name <text> [--path <wz-path>] [--json]");
            Console.WriteLine("  wcr2 search <file-or-dir> --value <text> [--path <wz-path>] [--json]");
            Console.WriteLine("  wcr2 search <file-or-dir> --match-path <glob-or-regex> [--type <type>] [--json]");
            Console.WriteLine("  wcr2 compare <old-file-or-dir> <new-file-or-dir> [--path <wz-path>] [--type added|removed|changed] [--format json|markdown] [--out <path>] [--json]");
            Console.WriteLine("  wcr2 dump <file-or-dir> --path <wz-path> [--format json|xml|raw] [--out <path>]");
            Console.WriteLine("  wcr2 extract <file-or-dir> --path <wz-path> --out <output-dir> [--recursive] [--manifest <json>] [--json]");
            Console.WriteLine("  wcr2 skill info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]");
            Console.WriteLine("  wcr2 skill full <skill-wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--level <n>] [--format json|xml|text] [--out <path>]");
            Console.WriteLine("  wcr2 item info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]");
            Console.WriteLine("  wcr2 gear info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]");
            Console.WriteLine("  wcr2 mob info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]");
            Console.WriteLine("  wcr2 npc info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]");
            Console.WriteLine("  wcr2 quest info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]");
            Console.WriteLine("  wcr2 map info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]");
            Console.WriteLine("  wcr2 map objects <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 map portals <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 animate frames <wz-file-or-dir> --path <wz-path> --out <dir> [--json]");
            Console.WriteLine("  wcr2 animate gif <wz-file-or-dir> --path <wz-path> --out <file.gif> [--background transparent|#RRGGBB] [--min-alpha <0-255>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
            Console.WriteLine("  wcr2 animate apng <wz-file-or-dir> --path <wz-path> --out <file.png> [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--optimize] [--json]");
            Console.WriteLine("  wcr2 animate ffmpeg <wz-file-or-dir> --path <wz-path> --out <file> [--ffmpeg <path>] [--ffmpeg-args <format>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
            Console.WriteLine("  wcr2 avatar inspect --code <code> [--json]");
            Console.WriteLine("  wcr2 avatar unpack --code <code>");
            Console.WriteLine("  wcr2 lua run <script.lua> [--wz <file-or-dir>] [--dry-run] [--json]");
            Console.WriteLine("  wcr2 lua eval <code> [--wz <file-or-dir>] [--dry-run] [--json]");
            Console.WriteLine("  wcr2 network server-info [--host <host>] [--port <port>] [--connect] [--json]");
            Console.WriteLine("  wcr2 network send --message <text> [--host <host>] [--port <port>] [--json]");
            Console.WriteLine("  wcr2 update check [--asset net8|net10|net6|net462|zip] [--json]");
            Console.WriteLine("  wcr2 update download --out <dir> [--asset net8|net10|net6|net462|zip] [--json]");
            Console.WriteLine("  wcr2 update apply [--asset net8|net10|net6|net462|zip] [--updater <path>] [--execute] [--json]");
            Console.WriteLine("  wcr2 config list|get|set|unset|path [--config <path>] [--profile <name>] [--json]");
            Console.WriteLine("  wcr2 plugin list|commands [--plugin-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 plugin inspect <assembly.dll> [--json]");
            Console.WriteLine("  wcr2 plugin run <command> [args...] [--plugin-dir <dir>]");
            Console.WriteLine("  wcr2 patch inspect <patch-file> [--json]");
            Console.WriteLine("  wcr2 patch dry-run <patch-file> --target <dir> [--json]");
            Console.WriteLine("  wcr2 patch apply <patch-file> --target <dir> --out <dir> [--log <file>] [--json]");
            Console.WriteLine();
            Console.WriteLine("Common options:");
            Console.WriteLine("  --use-base-wz       Load with Base.wz link behavior where supported.");
            Console.WriteLine("  --fallback <path>   Fallback WZ file or folder.");
            Console.WriteLine("  --extract-images    Extract image nodes while traversing.");
            Console.WriteLine("  --json              Emit JSON output.");
            Console.WriteLine("  --quiet             Suppress stdout for successful commands.");
            Console.WriteLine("  --verbose           Include exception details on stderr when a command fails.");
            Console.WriteLine("  --no-color          Disable colored output; accepted for script compatibility.");
            Console.WriteLine("  --format xml        Export selected node as XML instead of loose files.");
            Console.WriteLine("  --regex             Treat --match-path as a regular expression.");
            Console.WriteLine("  --ignore-image-binary  Skip pixel-level image comparison where supported.");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  wcr2 info Base.wz");
            Console.WriteLine("  wcr2 tree Base.wz --depth 2");
            Console.WriteLine("  wcr2 list Base.wz --path Character");
            Console.WriteLine("  wcr2 search String.wz --name Maple --json");
            Console.WriteLine("  wcr2 search Base.wz --match-path \"*/Canvas\" --type png");
            Console.WriteLine("  wcr2 compare old/Base.wz new/Base.wz --json");
            Console.WriteLine("  wcr2 extract Base.wz --path String --out out/string --recursive");
            Console.WriteLine("  wcr2 skill info Skill.wz --id 1001004 --string-wz String.wz --json");
            Console.WriteLine("  wcr2 skill full Skill.wz --id 3001004 --string-wz String.wz --format json");
            Console.WriteLine("  wcr2 map portals Map.wz --id 100000000 --json");
            Console.WriteLine("  wcr2 animate frames Mob.wz --path 0100100.img/stand --out out/stand");
            Console.WriteLine("  wcr2 animate gif Mob.wz --path 0100100.img/stand --out out/stand.gif");
            Console.WriteLine("  wcr2 animate apng Mob.wz --path 0100100.img/stand --out out/stand.png");
            Console.WriteLine("  wcr2 animate ffmpeg Mob.wz --path 0100100.img/stand --out out/stand.mp4");
            Console.WriteLine("  wcr2 avatar inspect --code \"1002140,1040036,1060026\"");
            Console.WriteLine("  wcr2 lua run WzComparerR2.LuaConsole/Examples/DumpXml.lua --dry-run --json");
            Console.WriteLine("  wcr2 lua eval --code \"print('ok')\" --dry-run --json");
            Console.WriteLine("  wcr2 network server-info --json");
            Console.WriteLine("  wcr2 update check --asset net8 --json");
            Console.WriteLine("  wcr2 update download --asset net8 --out downloads");
            Console.WriteLine("  wcr2 config set default-wz /path/to/Base.wz");
            Console.WriteLine("  wcr2 config list");
            Console.WriteLine("  wcr2 plugin list --plugin-dir CliPlugin --json");
            Console.WriteLine("  wcr2 plugin commands");
            Console.WriteLine("  wcr2 patch inspect MaplePatch.patch --json");
            Console.WriteLine("  wcr2 patch dry-run MaplePatch.patch --target MapleStory --json");
            Console.WriteLine("  wcr2 patch apply MaplePatch.patch --target MapleStory --out MapleStory.patched --log patch.log");
        }

        private static void PrintMapHelp()
        {
            Console.WriteLine("wcr2 map - map metadata tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 map info <map-wz-file-or-dir> --id <map-id> [--string-wz <file-or-dir>] [--json]");
            Console.WriteLine("  wcr2 map objects <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 map portals <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 map life <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 map reactors <map-wz-file-or-dir> --id <map-id> [--json]");
            Console.WriteLine("  wcr2 map render [<map-wz-file-or-dir>] --id <map-id> --out <map.png> --dry-run [--layer <n|all>] [--include-life] [--include-reactor] [--include-tooltip] [--json]");
        }

        private static void PrintSkillHelp()
        {
            Console.WriteLine("wcr2 skill - skill lookup tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 skill info <skill-wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]");
            Console.WriteLine("  wcr2 skill full <skill-wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--level <n>] [--format json|xml|text] [--out <path>]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --allow-string-only  Emit string metadata when the skill id exists only in String.wz.");
        }

        private static void PrintDomainHelp(string kind)
        {
            Console.WriteLine("wcr2 " + kind + " - " + kind + " lookup tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 " + kind + " info <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--json]");
            if (string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  wcr2 skill full <wz-file-or-dir> --id <id> [--string-wz <file-or-dir>] [--level <n>] [--format json|xml|text]");
            }
        }

        private static void PrintAnimateHelp()
        {
            Console.WriteLine("wcr2 animate - animation export tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 animate frames <wz-file-or-dir> --path <wz-path> --out <dir> [--json]");
            Console.WriteLine("  wcr2 animate gif <wz-file-or-dir> --path <wz-path> --out <file.gif> [--background transparent|#RRGGBB] [--min-alpha <0-255>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
            Console.WriteLine("  wcr2 animate apng <wz-file-or-dir> --path <wz-path> --out <file.png> [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--optimize] [--json]");
            Console.WriteLine("  wcr2 animate ffmpeg <wz-file-or-dir> --path <wz-path> --out <file> [--ffmpeg <path>] [--ffmpeg-args <format>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
        }

        private static void PrintAvatarHelp()
        {
            Console.WriteLine("wcr2 avatar - avatar code tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 avatar inspect --code <code> [--json]");
            Console.WriteLine("  wcr2 avatar unpack --code <code>");
            Console.WriteLine("  wcr2 avatar render --code <code> --out <avatar.png> --dry-run [--action <action>] [--emotion <emotion>] [--offline] [--api-key <key>] [--json]");
            Console.WriteLine("  wcr2 avatar render --items <ids> --out <avatar.png> --dry-run [--action <action>] [--emotion <emotion>] [--json]");
        }

        private static void PrintLuaHelp()
        {
            Console.WriteLine("wcr2 lua - Lua script tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 lua run <script.lua> [--wz <file-or-dir>] [--dry-run] [--timeout <seconds>] [--json]");
            Console.WriteLine("  wcr2 lua eval <code> [--wz <file-or-dir>] [--dry-run] [--timeout <seconds>] [--json]");
            Console.WriteLine("  wcr2 lua eval --code <code> [--wz <file-or-dir>] [--dry-run] [--timeout <seconds>] [--json]");
        }

        private static void PrintNetworkHelp()
        {
            Console.WriteLine("wcr2 network - network command tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 network server-info [--host <host>] [--port <port>] [--connect] [--json]");
            Console.WriteLine("  wcr2 network chat [--host <host>] [--port <port>] [--json]");
            Console.WriteLine("  wcr2 network send --message <text> [--host <host>] [--port <port>] [--json]");
            Console.WriteLine();
            Console.WriteLine("Notes:");
            Console.WriteLine("  network commands are non-interactive dry-runs unless server-info --connect is used for a TCP probe.");
            Console.WriteLine("  --interactive is reserved and currently rejected.");
        }

        private static void PrintUpdateHelp()
        {
            Console.WriteLine("wcr2 update - release update tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 update check [--repo <owner/name>] [--current-version <version>] [--asset net8|net10|net6|net462|zip] [--json]");
            Console.WriteLine("  wcr2 update download --out <dir> [--repo <owner/name>] [--asset net8|net10|net6|net462|zip] [--force] [--json]");
            Console.WriteLine("  wcr2 update apply [--repo <owner/name>] [--asset net8|net10|net6|net462|zip] [--updater <path>] [--download <zip>] [--execute] [--json]");
            Console.WriteLine();
            Console.WriteLine("Notes:");
            Console.WriteLine("  update apply is dry-run unless --execute is provided.");
            Console.WriteLine("  --execute requires an external WzComparerR2.Updater executable.");
        }

        private static void PrintConfigHelp()
        {
            Console.WriteLine("wcr2 config - CLI configuration tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 config path [--config <path>] [--json]");
            Console.WriteLine("  wcr2 config list [--config <path>] [--profile <name>] [--json]");
            Console.WriteLine("  wcr2 config get <key> [--config <path>] [--profile <name>] [--json]");
            Console.WriteLine("  wcr2 config set <key> <value> [--config <path>] [--profile <name>] [--json]");
            Console.WriteLine("  wcr2 config unset <key> [--config <path>] [--profile <name>] [--json]");
            Console.WriteLine();
            Console.WriteLine("Default path:");
            Console.WriteLine("  Windows: %APPDATA%/WzComparerR2/wcr2.config.json");
            Console.WriteLine("  Unix:    $XDG_CONFIG_HOME/wzcomparerr2/wcr2.config.json or ~/.config/wzcomparerr2/wcr2.config.json");
            Console.WriteLine();
            Console.WriteLine("Profiles:");
            Console.WriteLine("  --profile <name> stores values under profiles.<name>.<key> and profile values override global fallback keys.");
        }

        private static void PrintPluginHelp()
        {
            Console.WriteLine("wcr2 plugin - CLI plugin tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 plugin list [--plugin-dir <dir>] [--include-gui-plugin-dir] [--json]");
            Console.WriteLine("  wcr2 plugin inspect <assembly.dll> [--json]");
            Console.WriteLine("  wcr2 plugin commands [--plugin-dir <dir>] [--json]");
            Console.WriteLine("  wcr2 plugin run <command> [args...] [--plugin-dir <dir>]");
            Console.WriteLine();
            Console.WriteLine("Discovery:");
            Console.WriteLine("  Explicit --plugin-dir, config key plugin-dir, WCR2_CLI_PLUGIN_DIR, then CliPlugin beside the executable/current directory.");
            Console.WriteLine("  GUI Plugin directories are scanned only with --include-gui-plugin-dir.");
        }

        private static void PrintPatchHelp()
        {
            Console.WriteLine("wcr2 patch - patch inspection tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 patch inspect <patch-file> [--json]");
            Console.WriteLine("  wcr2 patch dry-run <patch-file> --target <dir> [--json]");
            Console.WriteLine("  wcr2 patch apply <patch-file> --target <dir> --out <dir> [--log <file>] [--json]");
        }

        private static string FormatNullableBool(bool? value)
        {
            return value.HasValue ? value.Value.ToString().ToLowerInvariant() : "unknown";
        }
    }

    public interface ICliCommandProvider
    {
        IEnumerable<CliCommandDescriptor> GetCommands();

        int Execute(string commandName, IReadOnlyList<string> args, ICliCommandContext context);
    }

    public interface ICliCommandContext
    {
        TextWriter Output { get; }
        TextWriter Error { get; }
        string WorkingDirectory { get; }
        string CliVersion { get; }
        string GetConfigValue(string key);
    }

    public sealed class CliCommandDescriptor
    {
        public string Name { get; set; }
        public string Summary { get; set; }
        public string Usage { get; set; }
    }

    internal sealed class CliCommandContext : ICliCommandContext
    {
        private readonly CliConfigStore store;
        private readonly TextWriter output;
        private readonly TextWriter error;

        public CliCommandContext(CliConfigStore store, TextWriter output, TextWriter error)
        {
            this.store = store;
            this.output = output;
            this.error = error;
        }

        public TextWriter Output => this.output;

        public TextWriter Error => this.error;

        public string WorkingDirectory => Directory.GetCurrentDirectory();

        public string CliVersion => "0.1.0";

        public string GetConfigValue(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            string value;
            return this.store.Values.TryGetValue(key, out value) ? value : null;
        }
    }

    internal sealed class CliPluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;

        public CliPluginLoadContext(string pluginPath)
            : base(isCollectible: true)
        {
            this.resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            Assembly current = typeof(ICliCommandProvider).Assembly;
            if (string.Equals(assemblyName.Name, current.GetName().Name, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            string assemblyPath = this.resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath == null ? null : LoadFromAssemblyPath(assemblyPath);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string libraryPath = this.resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return libraryPath == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(libraryPath);
        }
    }

    internal static class CliPluginRegistry
    {
        public static CliPluginDiscoveryDto Discover(ParsedArgs args)
        {
            var directories = ResolveDirectories(args);
            var result = new CliPluginDiscoveryDto
            {
                Directories = directories,
                Plugins = new List<CliPluginDto>()
            };

            foreach (string directory in directories)
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    result.Plugins.Add(InspectFile(file));
                }
            }

            result.PluginCount = result.Plugins.Count;
            result.LoadFailedCount = result.Plugins.Count(plugin => !plugin.Success);
            return result;
        }

        public static CliPluginDto InspectFile(string file)
        {
            string fullPath = Path.GetFullPath(file);
            var dto = new CliPluginDto
            {
                Path = fullPath,
                Status = "failed",
                ProviderTypes = new List<string>(),
                GuiPluginEntryTypes = new List<string>(),
                Commands = new List<CliPluginCommandDto>()
            };

            if (!File.Exists(fullPath))
            {
                dto.Error = "Plugin assembly not found.";
                return dto;
            }

            try
            {
                AssemblyName name = AssemblyName.GetAssemblyName(fullPath);
                dto.AssemblyName = name.Name;
                dto.AssemblyVersion = name.Version?.ToString();
            }
            catch (Exception ex)
            {
                dto.Error = "Invalid assembly: " + ex.Message;
                return dto;
            }

            var context = new CliPluginLoadContext(fullPath);
            try
            {
                Assembly assembly = context.LoadFromAssemblyPath(fullPath);
                var types = GetLoadableTypes(assembly, dto);
                foreach (Type type in types)
                {
                    if (typeof(ICliCommandProvider).IsAssignableFrom(type) && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                    {
                        dto.ProviderTypes.Add(type.FullName);
                        AddCommands(dto, type);
                    }
                    else if (IsGuiPluginEntry(type))
                    {
                        dto.GuiPluginEntryTypes.Add(type.FullName);
                    }
                }

                dto.Status = dto.ProviderTypes.Count > 0 ? "cli-plugin" : dto.GuiPluginEntryTypes.Count > 0 ? "gui-plugin" : "assembly";
                dto.Success = true;
                return dto;
            }
            catch (Exception ex)
            {
                dto.Error = ex.Message;
                return dto;
            }
            finally
            {
                context.Unload();
            }
        }

        public static CliPluginRunResultDto Execute(ParsedArgs args, string commandName, IReadOnlyList<string> commandArgs)
        {
            var discovery = Discover(args);
            var result = new CliPluginRunResultDto
            {
                Command = commandName,
                ExitCode = 1,
                SearchedPluginCount = discovery.Plugins.Count
            };

            if (discovery.LoadFailedCount > 0)
            {
                result.Message = "Some plugin assemblies failed to load; continuing with available CLI providers.";
            }

            foreach (var plugin in discovery.Plugins.Where(plugin => plugin.Success && plugin.Commands.Any(command => CommandNameMatches(command.Name, commandName))))
            {
                string fullPath = plugin.Path;
                var context = new CliPluginLoadContext(fullPath);
                try
                {
                    Assembly assembly = context.LoadFromAssemblyPath(fullPath);
                    foreach (Type type in GetLoadableTypes(assembly, null))
                    {
                        if (!typeof(ICliCommandProvider).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
                        {
                            continue;
                        }

                        var provider = (ICliCommandProvider)Activator.CreateInstance(type);
                        var commands = SafeGetCommands(provider);
                        if (!commands.Any(command => CommandNameMatches(command.Name, commandName)))
                        {
                            continue;
                        }

                        bool captureOutput = args.HasFlag("json");
                        var outputWriter = captureOutput ? new StringWriter() : Console.Out;
                        var errorWriter = captureOutput ? new StringWriter() : Console.Error;
                        result.PluginPath = fullPath;
                        result.ProviderType = type.FullName;
                        result.ExitCode = provider.Execute(commandName, commandArgs, new CliCommandContext(CliConfigStore.Open(args.GetValue("config")), outputWriter, errorWriter));
                        if (captureOutput)
                        {
                            result.Stdout = outputWriter.ToString();
                            result.Stderr = errorWriter.ToString();
                        }
                        result.Success = result.ExitCode == 0;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    result.PluginPath = fullPath;
                    result.Error = ex.Message;
                    result.ExitCode = 5;
                    return result;
                }
                finally
                {
                    context.Unload();
                }
            }

            result.Error = "CLI plugin command not found: " + commandName;
            return result;
        }

        private static List<string> ResolveDirectories(ParsedArgs args)
        {
            var directories = new List<string>();
            AddDirectory(directories, args.GetValue("plugin-dir"));

            var store = CliConfigStore.Open(args.GetValue("config"));
            string configured;
            if (store.Values.TryGetValue("plugin-dir", out configured))
            {
                AddDirectory(directories, configured);
            }

            string env = Environment.GetEnvironmentVariable("WCR2_CLI_PLUGIN_DIR");
            if (!string.IsNullOrWhiteSpace(env))
            {
                foreach (string item in env.Split(Path.PathSeparator))
                {
                    AddDirectory(directories, item);
                }
            }

            AddDirectory(directories, Path.Combine(AppContext.BaseDirectory, "CliPlugin"));
            AddDirectory(directories, Path.Combine(Directory.GetCurrentDirectory(), "CliPlugin"));

            if (args.HasFlag("include-gui-plugin-dir"))
            {
                AddDirectory(directories, Path.Combine(AppContext.BaseDirectory, "Plugin"));
                AddDirectory(directories, Path.Combine(Directory.GetCurrentDirectory(), "Plugin"));
            }

            return directories;
        }

        private static void AddDirectory(List<string> directories, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path);
            if (!directories.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                directories.Add(fullPath);
            }
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly, CliPluginDto dto)
        {
            try
            {
                return assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                if (dto != null)
                {
                    dto.Error = string.Join(Environment.NewLine, ex.LoaderExceptions.Where(item => item != null).Select(item => item.Message));
                }
                return ex.Types.Where(type => type != null && type.IsPublic);
            }
        }

        private static void AddCommands(CliPluginDto dto, Type providerType)
        {
            try
            {
                var provider = (ICliCommandProvider)Activator.CreateInstance(providerType);
                foreach (var command in SafeGetCommands(provider))
                {
                    if (string.IsNullOrWhiteSpace(command.Name))
                    {
                        continue;
                    }

                    dto.Commands.Add(new CliPluginCommandDto
                    {
                        ProviderType = providerType.FullName,
                        Name = command.Name,
                        Summary = command.Summary,
                        Usage = command.Usage
                    });
                }
            }
            catch (Exception ex)
            {
                dto.Error = "Provider discovery failed for " + providerType.FullName + ": " + ex.Message;
            }
        }

        private static List<CliCommandDescriptor> SafeGetCommands(ICliCommandProvider provider)
        {
            return (provider.GetCommands() ?? Enumerable.Empty<CliCommandDescriptor>())
                .Where(command => command != null)
                .ToList();
        }

        private static bool CommandNameMatches(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGuiPluginEntry(Type type)
        {
            for (Type current = type.BaseType; current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, "WzComparerR2.PluginBase.PluginEntry", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal sealed class CliPluginDiscoveryDto
    {
        public List<string> Directories { get; set; }
        public int PluginCount { get; set; }
        public int LoadFailedCount { get; set; }
        public List<CliPluginDto> Plugins { get; set; }
    }

    internal sealed class CliPluginDto
    {
        public string Path { get; set; }
        public string AssemblyName { get; set; }
        public string AssemblyVersion { get; set; }
        public string Status { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public List<string> ProviderTypes { get; set; }
        public List<string> GuiPluginEntryTypes { get; set; }
        public List<CliPluginCommandDto> Commands { get; set; }
    }

    internal sealed class CliPluginCommandDto
    {
        public string ProviderType { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        public string Usage { get; set; }
    }

    internal sealed class CliPluginCommandListItemDto
    {
        public string PluginPath { get; set; }
        public string ProviderType { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        public string Usage { get; set; }
    }

    internal sealed class CliPluginRunResultDto
    {
        public string Command { get; set; }
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public int SearchedPluginCount { get; set; }
        public string PluginPath { get; set; }
        public string ProviderType { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
        public string Stdout { get; set; }
        public string Stderr { get; set; }
    }

    internal sealed class ParsedArgs
    {
        private readonly Dictionary<string, string> values;
        private readonly HashSet<string> flags;

        private ParsedArgs()
        {
            this.Positionals = new List<string>();
            this.RawArguments = new List<string>();
            this.values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            this.flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public List<string> Positionals { get; private set; }
        public List<string> RawArguments { get; private set; }

        public static ParsedArgs Parse(IEnumerable<string> args)
        {
            var parsed = new ParsedArgs();
            var list = args.ToList();
            parsed.RawArguments.AddRange(list);

            for (int i = 0; i < list.Count; i++)
            {
                string arg = list[i];
                if (string.Equals(arg, "--", StringComparison.Ordinal))
                {
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        parsed.Positionals.Add(list[j]);
                    }
                    break;
                }

                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    string key = arg.Substring(2);
                    if (key.Length == 0)
                    {
                        throw new UsageException("Empty option name.");
                    }

                    if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        parsed.values[key] = list[++i];
                    }
                    else
                    {
                        parsed.flags.Add(key);
                    }
                }
                else
                {
                    parsed.Positionals.Add(arg);
                }
            }

            return parsed;
        }

        public IReadOnlyList<string> GetTailArguments(int positionalCount)
        {
            if (positionalCount <= 0)
            {
                return this.RawArguments.ToList();
            }

            int seen = 0;
            for (int i = 0; i < this.RawArguments.Count; i++)
            {
                string arg = this.RawArguments[i];
                if (string.Equals(arg, "--", StringComparison.Ordinal))
                {
                    return this.RawArguments.Skip(i + 1).ToList();
                }

                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    seen++;
                    if (seen == positionalCount)
                    {
                        return this.RawArguments.Skip(i + 1).ToList();
                    }
                }
                else if (i + 1 < this.RawArguments.Count && !this.RawArguments[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    i++;
                }
            }

            return Array.Empty<string>();
        }

        public string GetValue(string key)
        {
            string value;
            return this.values.TryGetValue(key, out value) ? value : null;
        }

        public bool HasFlag(string key)
        {
            return this.flags.Contains(key) || this.values.ContainsKey(key);
        }

        public int GetInt(string key, int defaultValue)
        {
            string value = GetValue(key);
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            int result;
            if (!int.TryParse(value, out result))
            {
                throw new UsageException("--" + key + " must be an integer.");
            }
            return result;
        }
    }

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

    internal sealed class SearchOptions
    {
        private Regex pathRegex;

        public string NameQuery { get; set; }
        public string ValueQuery { get; set; }
        public string PathQuery { get; private set; }
        public string Type { get; private set; }
        public int MaxResults { get; private set; }
        public bool ExtractImages { get; set; }

        public static SearchOptions FromArgs(ParsedArgs args)
        {
            var options = new SearchOptions
            {
                PathQuery = args.GetValue("match-path"),
                Type = args.GetValue("type"),
                MaxResults = args.GetInt("max-results", 100)
            };

            if (!string.IsNullOrEmpty(options.PathQuery))
            {
                string pattern = args.HasFlag("regex")
                    ? options.PathQuery
                    : GlobToRegexPattern(options.PathQuery);
                try
                {
                    options.pathRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException ex)
                {
                    throw new UsageException("Invalid --match-path pattern: " + ex.Message);
                }
            }

            return options;
        }

        public bool Matches(Wz_Node node)
        {
            if (!string.IsNullOrEmpty(this.Type) && !TypeMatches(node, this.Type))
            {
                return false;
            }

            string value = NodeDto.FormatValue(node.Value);
            bool nameMatched = !string.IsNullOrEmpty(this.NameQuery)
                && node.Text != null
                && node.Text.IndexOf(this.NameQuery, StringComparison.OrdinalIgnoreCase) >= 0;
            bool valueMatched = !string.IsNullOrEmpty(this.ValueQuery)
                && value != null
                && value.IndexOf(this.ValueQuery, StringComparison.OrdinalIgnoreCase) >= 0;
            bool pathMatched = this.pathRegex != null
                && this.pathRegex.IsMatch(NormalizePath(node.FullPath));

            return nameMatched || valueMatched || pathMatched;
        }

        private static bool TypeMatches(Wz_Node node, string type)
        {
            string nodeType = NodeDto.GetTypeName(node.Value);
            if (string.Equals(nodeType, type, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(nodeType, "wz-file", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(type, "dir", StringComparison.OrdinalIgnoreCase))
            {
                return node.Value == null;
            }

            if (string.Equals(type, "string", StringComparison.OrdinalIgnoreCase))
            {
                return node.Value is string;
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        private static string GlobToRegexPattern(string glob)
        {
            bool hasWildcard = glob.IndexOf('*') >= 0 || glob.IndexOf('?') >= 0;
            if (!hasWildcard)
            {
                return Regex.Escape(NormalizePath(glob));
            }

            string normalized = NormalizePath(glob);
            var builder = new System.Text.StringBuilder();
            builder.Append('^');
            foreach (char ch in normalized)
            {
                switch (ch)
                {
                    case '*':
                        builder.Append(".*");
                        break;
                    case '?':
                        builder.Append('.');
                        break;
                    default:
                        builder.Append(Regex.Escape(ch.ToString()));
                        break;
                }
            }
            builder.Append('$');
            return builder.ToString();
        }
    }

    internal static class NodePath
    {
        public static Wz_Node Resolve(Wz_Node root, string path, bool extractImages)
        {
            if (root == null)
            {
                return null;
            }

            Wz_Node current = ExtractImageNode(root, extractImages);
            if (string.IsNullOrWhiteSpace(path))
            {
                return current;
            }

            string[] parts = path
                .Replace('/', '\\')
                .Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

            int start = 0;
            if (parts.Length > 0 && string.Equals(parts[0], current.Text, StringComparison.OrdinalIgnoreCase))
            {
                start = 1;
            }

            for (int i = start; i < parts.Length; i++)
            {
                current = ExtractImageNode(current, extractImages);
                if (current == null)
                {
                    return null;
                }

                Wz_Node child = current.Nodes[parts[i]];
                if (child == null)
                {
                    foreach (Wz_Node candidate in current.Nodes)
                    {
                        if (string.Equals(candidate.Text, parts[i], StringComparison.OrdinalIgnoreCase))
                        {
                            child = candidate;
                            break;
                        }
                    }
                }

                if (child == null)
                {
                    return null;
                }

                current = child;
            }

            return ExtractImageNode(current, extractImages);
        }

        public static Wz_Node ExtractImageNode(Wz_Node node, bool extractImages)
        {
            if (!extractImages || node == null)
            {
                return node;
            }

            Wz_Image image = node.GetValue<Wz_Image>();
            if (image != null && image.TryExtract())
            {
                return image.Node;
            }

            return node;
        }
    }

    internal sealed class InfoDto
    {
        public string InputPath { get; set; }
        public string RootName { get; set; }
        public int RootChildren { get; set; }
        public int WzFileCount { get; set; }
        public int MsFileCount { get; set; }
        public int ImageCount { get; set; }
        public List<FileDto> Files { get; set; }

        public static InfoDto FromContext(WzLoadContext context)
        {
            return new InfoDto
            {
                InputPath = context.InputPath,
                RootName = context.Root.Text,
                RootChildren = context.Root.Nodes.Count,
                WzFileCount = context.Structure.wz_files.Count,
                MsFileCount = context.Structure.ms_files.Count,
                ImageCount = context.Structure.img_number,
                Files = context.Structure.wz_files.Select(FileDto.FromWzFile).ToList()
            };
        }
    }

    internal sealed class FileDto
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int ImageCount { get; set; }
        public int WzVersion { get; set; }
        public long FileSize { get; set; }

        public static FileDto FromWzFile(Wz_File file)
        {
            return new FileDto
            {
                Name = Path.GetFileName(file.Header.FileName),
                Type = file.Type.ToString(),
                ImageCount = file.ImageCount,
                WzVersion = file.Header.WzVersion,
                FileSize = file.Header.FileSize
            };
        }
    }

    internal sealed class NodeDto
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public int ChildrenCount { get; set; }
        public List<NodeDto> Children { get; set; }

        public static NodeDto FromNodeShallow(Wz_Node node)
        {
            return new NodeDto
            {
                Name = node.Text,
                Path = node.FullPath,
                Type = GetTypeName(node.Value),
                Value = FormatValue(node.Value),
                ChildrenCount = node.Nodes.Count
            };
        }

        public static NodeDto FromNode(Wz_Node node, int depth, int limit, bool extractImages)
        {
            int remaining = limit <= 0 ? int.MaxValue : limit;
            return FromNodeCore(node, depth, extractImages, ref remaining);
        }

        private static NodeDto FromNodeCore(Wz_Node node, int depth, bool extractImages, ref int remaining)
        {
            node = NodePath.ExtractImageNode(node, extractImages);
            remaining--;

            var dto = FromNodeShallow(node);
            if (depth > 0 && remaining > 0)
            {
                dto.Children = new List<NodeDto>();
                foreach (Wz_Node child in node.Nodes)
                {
                    if (remaining <= 0)
                    {
                        break;
                    }
                    dto.Children.Add(FromNodeCore(child, depth - 1, extractImages, ref remaining));
                }
            }

            return dto;
        }

        public static string GetTypeName(object value)
        {
            if (value == null)
            {
                return "dir";
            }
            if (value is Wz_File)
            {
                return "wz-file";
            }
            if (value is Wz_Image)
            {
                return "image";
            }
            if (value is Wz_Png)
            {
                return "png";
            }
            if (value is Wz_Sound)
            {
                return "sound";
            }
            if (value is Wz_Uol)
            {
                return "uol";
            }
            if (value is Wz_Vector)
            {
                return "vector";
            }
            if (value is Wz_RawData)
            {
                return "raw";
            }
            if (value is Wz_Video)
            {
                return "video";
            }
            return value.GetType().Name;
        }

        public static string FormatValue(object value)
        {
            if (value == null || value is Wz_File)
            {
                return null;
            }

            var image = value as Wz_Image;
            if (image != null)
            {
                return "size=" + image.Size;
            }

            var png = value as Wz_Png;
            if (png != null)
            {
                return png.Width + "x" + png.Height + " format=" + png.Format + " pages=" + png.ActualPages;
            }

            var sound = value as Wz_Sound;
            if (sound != null)
            {
                return "length=" + sound.DataLength + " ms=" + sound.Ms + " channels=" + sound.Channels + " frequency=" + sound.Frequency;
            }

            var uol = value as Wz_Uol;
            if (uol != null)
            {
                return uol.Uol;
            }

            var vector = value as Wz_Vector;
            if (vector != null)
            {
                return vector.X + "," + vector.Y;
            }

            var raw = value as Wz_RawData;
            if (raw != null)
            {
                return "length=" + raw.Length;
            }

            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    internal sealed class SearchResultDto
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }

        public static SearchResultDto FromNode(Wz_Node node)
        {
            return new SearchResultDto
            {
                Name = node.Text,
                Path = node.FullPath,
                Type = NodeDto.GetTypeName(node.Value),
                Value = NodeDto.FormatValue(node.Value)
            };
        }
    }

    internal sealed class CompareOptions
    {
        public string ChangeType { get; private set; }
        public int MaxResults { get; private set; }
        public bool IgnoreImageBinary { get; private set; }

        public static CompareOptions FromArgs(ParsedArgs args)
        {
            string changeType = args.GetValue("type");
            if (!string.IsNullOrEmpty(changeType)
                && !string.Equals(changeType, "added", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(changeType, "removed", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(changeType, "changed", StringComparison.OrdinalIgnoreCase))
            {
                throw new UsageException("--type must be added, removed, or changed.");
            }

            return new CompareOptions
            {
                ChangeType = changeType,
                MaxResults = args.GetInt("max-results", 1000),
                IgnoreImageBinary = args.HasFlag("ignore-image-binary")
            };
        }

        public bool Includes(string changeType)
        {
            return string.IsNullOrEmpty(this.ChangeType)
                || string.Equals(this.ChangeType, changeType, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class CompareResultDto
    {
        public string OldInputPath { get; set; }
        public string NewInputPath { get; set; }
        public string OutputPath { get; set; }
        public string Path { get; set; }
        public int Added { get; set; }
        public int Removed { get; set; }
        public int Changed { get; set; }
        public int MaxResults { get; set; }
        public bool IgnoreImageBinary { get; set; }
        public bool Truncated { get; set; }
        public List<CompareChangeDto> Differences { get; set; }

        public static CompareResultDto Create(string oldInputPath, string newInputPath, string path)
        {
            return new CompareResultDto
            {
                OldInputPath = oldInputPath,
                NewInputPath = newInputPath,
                Path = path,
                MaxResults = 1000,
                Differences = new List<CompareChangeDto>()
            };
        }

        public void Count(string changeType)
        {
            if (string.Equals(changeType, "added", StringComparison.OrdinalIgnoreCase))
            {
                this.Added++;
            }
            else if (string.Equals(changeType, "removed", StringComparison.OrdinalIgnoreCase))
            {
                this.Removed++;
            }
            else if (string.Equals(changeType, "changed", StringComparison.OrdinalIgnoreCase))
            {
                this.Changed++;
            }
        }
    }

    internal sealed class CompareChangeDto
    {
        public string Path { get; set; }
        public string ChangeType { get; set; }
        public string OldType { get; set; }
        public string NewType { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }

        public static CompareChangeDto FromNodes(Wz_Node oldNode, Wz_Node newNode)
        {
            if (oldNode == null && newNode == null)
            {
                return null;
            }

            if (oldNode == null)
            {
                return Create(newNode.FullPath, "added", null, newNode);
            }

            if (newNode == null)
            {
                return Create(oldNode.FullPath, "removed", oldNode, null);
            }

            string oldType = NodeDto.GetTypeName(oldNode.Value);
            string newType = NodeDto.GetTypeName(newNode.Value);
            string oldValue = NodeDto.FormatValue(oldNode.Value);
            string newValue = NodeDto.FormatValue(newNode.Value);
            if (string.Equals(oldType, newType, StringComparison.Ordinal)
                && string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                return null;
            }

            return new CompareChangeDto
            {
                Path = newNode.FullPath ?? oldNode.FullPath,
                ChangeType = "changed",
                OldType = oldType,
                NewType = newType,
                OldValue = oldValue,
                NewValue = newValue
            };
        }

        private static CompareChangeDto Create(string path, string changeType, Wz_Node oldNode, Wz_Node newNode)
        {
            return new CompareChangeDto
            {
                Path = path,
                ChangeType = changeType,
                OldType = oldNode == null ? null : NodeDto.GetTypeName(oldNode.Value),
                NewType = newNode == null ? null : NodeDto.GetTypeName(newNode.Value),
                OldValue = oldNode == null ? null : NodeDto.FormatValue(oldNode.Value),
                NewValue = newNode == null ? null : NodeDto.FormatValue(newNode.Value)
            };
        }
    }

    internal struct NodePair
    {
        public NodePair(Wz_Node oldNode, Wz_Node newNode)
        {
            this.OldNode = oldNode;
            this.NewNode = newNode;
        }

        public Wz_Node OldNode { get; private set; }
        public Wz_Node NewNode { get; private set; }
    }

    internal sealed class PatchInspectResultDto
    {
        public string PatchFilePath { get; set; }
        public string OutputPath { get; set; }
        public long DataPosition { get; set; }
        public bool? IsKmst1125Format { get; set; }
        public int PartCount { get; set; }
        public int CreateCount { get; set; }
        public int RebuildCount { get; set; }
        public int DeleteCount { get; set; }
        public int OldFileHashCount { get; set; }
        public int NoticeLength { get; set; }
        public string NoticeText { get; set; }
        public List<PatchPartDto> Parts { get; set; }

        public static PatchInspectResultDto FromPatcher(string patchFilePath, long dataPosition, WzPatcher patcher)
        {
            var parts = patcher.PatchParts == null
                ? new List<PatchPartDto>()
                : patcher.PatchParts.Select(PatchPartDto.FromPart).ToList();

            return new PatchInspectResultDto
            {
                PatchFilePath = patchFilePath,
                DataPosition = dataPosition,
                IsKmst1125Format = patcher.IsKMST1125Format,
                PartCount = parts.Count,
                CreateCount = parts.Count(part => part.Type == "create"),
                RebuildCount = parts.Count(part => part.Type == "rebuild"),
                DeleteCount = parts.Count(part => part.Type == "delete"),
                OldFileHashCount = patcher.OldFileHash == null ? 0 : patcher.OldFileHash.Count,
                NoticeLength = patcher.NoticeText == null ? 0 : patcher.NoticeText.Length,
                NoticeText = patcher.NoticeText,
                Parts = parts
            };
        }
    }

    internal sealed class PatchPartDto
    {
        public string FileName { get; set; }
        public string Type { get; set; }
        public string WzType { get; set; }
        public long Offset { get; set; }
        public int? OldFileLength { get; set; }
        public int NewFileLength { get; set; }
        public string OldChecksum { get; set; }
        public string NewChecksum { get; set; }

        public static PatchPartDto FromPart(PatchPartContext part)
        {
            return new PatchPartDto
            {
                FileName = part.FileName,
                Type = GetPatchTypeName(part.Type),
                WzType = part.WzType.ToString(),
                Offset = part.Offset,
                OldFileLength = part.OldFileLength,
                NewFileLength = part.NewFileLength,
                OldChecksum = part.OldChecksum.HasValue ? "0x" + part.OldChecksum.Value.ToString("x8") : null,
                NewChecksum = "0x" + part.NewChecksum.ToString("x8")
            };
        }

        private static string GetPatchTypeName(int type)
        {
            switch (type)
            {
                case 0:
                    return "create";
                case 1:
                    return "rebuild";
                case 2:
                    return "delete";
                default:
                    return "unknown";
            }
        }
    }

    internal sealed class PatchDryRunResultDto
    {
        public string PatchFilePath { get; set; }
        public string TargetDirectory { get; set; }
        public string OutputPath { get; set; }
        public bool? IsKmst1125Format { get; set; }
        public int PartCount { get; set; }
        public int CreateCount { get; set; }
        public int RebuildCount { get; set; }
        public int DeleteCount { get; set; }
        public int ValidCount { get; set; }
        public int MissingCount { get; set; }
        public int ChecksumMismatchCount { get; set; }
        public int UncheckedCount { get; set; }
        public List<PatchDryRunActionDto> Actions { get; set; }

        public static PatchDryRunResultDto FromInspect(PatchInspectResultDto inspect, string targetDirectory, List<PatchPartContext> parts)
        {
            var actions = parts == null
                ? new List<PatchDryRunActionDto>()
                : parts.Select(part => PatchDryRunActionDto.FromPart(part, targetDirectory)).ToList();

            return new PatchDryRunResultDto
            {
                PatchFilePath = inspect.PatchFilePath,
                TargetDirectory = targetDirectory,
                IsKmst1125Format = inspect.IsKmst1125Format,
                PartCount = actions.Count,
                CreateCount = actions.Count(action => action.Action == "create"),
                RebuildCount = actions.Count(action => action.Action == "rebuild"),
                DeleteCount = actions.Count(action => action.Action == "delete"),
                ValidCount = actions.Count(action => action.Status == "valid"),
                MissingCount = actions.Count(action => action.Status == "missing"),
                ChecksumMismatchCount = actions.Count(action => action.Status == "checksum-mismatch"),
                UncheckedCount = actions.Count(action => action.Status == "unchecked" || action.Status == "exists" || action.Status == "absent"),
                Actions = actions
            };
        }
    }

    internal sealed class PatchDryRunActionDto
    {
        public string FileName { get; set; }
        public string TargetPath { get; set; }
        public string Action { get; set; }
        public string Status { get; set; }
        public bool Exists { get; set; }
        public long? ExistingLength { get; set; }
        public int? NewFileLength { get; set; }
        public string ExpectedOldChecksum { get; set; }
        public string ActualOldChecksum { get; set; }
        public string NewChecksum { get; set; }
        public string Message { get; set; }

        public static PatchDryRunActionDto FromPart(PatchPartContext part, string targetDirectory)
        {
            string targetPath = Path.Combine(targetDirectory, NormalizePatchPath(part.FileName));
            bool isDirectory = part.FileName.EndsWith("\\", StringComparison.Ordinal) || part.FileName.EndsWith("/", StringComparison.Ordinal);
            bool exists = isDirectory ? Directory.Exists(targetPath) : File.Exists(targetPath);
            var dto = new PatchDryRunActionDto
            {
                FileName = part.FileName,
                TargetPath = targetPath,
                Action = GetActionName(part.Type),
                Exists = exists,
                NewFileLength = part.NewFileLength,
                ExpectedOldChecksum = part.OldChecksum.HasValue ? FormatChecksum(part.OldChecksum.Value) : null,
                NewChecksum = FormatChecksum(part.NewChecksum)
            };

            if (!isDirectory && exists)
            {
                dto.ExistingLength = new FileInfo(targetPath).Length;
            }

            switch (part.Type)
            {
                case 0:
                    dto.Status = exists ? "exists" : "absent";
                    dto.Message = exists ? "Target already exists; create would overwrite if applied." : "Target does not exist; create can add it.";
                    break;
                case 1:
                    ValidateExistingFile(dto, part, targetPath, exists);
                    break;
                case 2:
                    dto.Status = exists ? "exists" : "missing";
                    dto.Message = exists ? "Target exists and would be deleted." : "Target is already missing.";
                    break;
                default:
                    dto.Status = "unknown";
                    dto.Message = "Unknown patch action type.";
                    break;
            }

            return dto;
        }

        private static void ValidateExistingFile(PatchDryRunActionDto dto, PatchPartContext part, string targetPath, bool exists)
        {
            if (!exists)
            {
                dto.Status = "missing";
                dto.Message = "Required old file is missing.";
                return;
            }

            if (!part.OldChecksum.HasValue)
            {
                dto.Status = "unchecked";
                dto.Message = "No old checksum is available in this patch part.";
                return;
            }

            try
            {
                using (var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    uint actual = CheckSum.ComputeHash(stream, stream.Length, CancellationToken.None);
                    dto.ActualOldChecksum = FormatChecksum(actual);
                    if (actual == part.OldChecksum.Value)
                    {
                        dto.Status = "valid";
                        dto.Message = "Old checksum matches.";
                    }
                    else
                    {
                        dto.Status = "checksum-mismatch";
                        dto.Message = "Old checksum does not match.";
                    }
                }
            }
            catch (Exception ex)
            {
                dto.Status = "read-error";
                dto.Message = ex.Message;
            }
        }

        private static string NormalizePatchPath(string fileName)
        {
            string path = fileName ?? string.Empty;
            return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string GetActionName(int type)
        {
            switch (type)
            {
                case 0:
                    return "create";
                case 1:
                    return "rebuild";
                case 2:
                    return "delete";
                default:
                    return "unknown";
            }
        }

        private static string FormatChecksum(uint checksum)
        {
            return "0x" + checksum.ToString("x8");
        }
    }

    internal sealed class DirectoryCopyStats
    {
        public int FileCount { get; set; }
        public long Bytes { get; set; }
    }

    internal sealed class PatchApplyResultDto
    {
        public string PatchFilePath { get; set; }
        public string TargetDirectory { get; set; }
        public string OutputDirectory { get; set; }
        public string LogPath { get; set; }
        public int PartCount { get; set; }
        public int CreateCount { get; set; }
        public int RebuildCount { get; set; }
        public int DeleteCount { get; set; }
        public int CopiedFileCount { get; set; }
        public long CopiedBytes { get; set; }
        public int EventCount { get; set; }
        public List<PatchApplyEventDto> Events { get; set; }

        public static PatchApplyResultDto FromDryRun(PatchDryRunResultDto dryRun, string outputDirectory, DirectoryCopyStats copyStats)
        {
            return new PatchApplyResultDto
            {
                PatchFilePath = dryRun.PatchFilePath,
                TargetDirectory = dryRun.TargetDirectory,
                OutputDirectory = outputDirectory,
                PartCount = dryRun.PartCount,
                CreateCount = dryRun.CreateCount,
                RebuildCount = dryRun.RebuildCount,
                DeleteCount = dryRun.DeleteCount,
                CopiedFileCount = copyStats.FileCount,
                CopiedBytes = copyStats.Bytes,
                Events = new List<PatchApplyEventDto>()
            };
        }
    }

    internal sealed class PatchApplyEventDto
    {
        public string State { get; set; }
        public string FileName { get; set; }
        public long CurrentFileLength { get; set; }

        public static PatchApplyEventDto FromEvent(PatchingEventArgs args)
        {
            return new PatchApplyEventDto
            {
                State = args.State.ToString(),
                FileName = args.Part == null ? null : args.Part.FileName,
                CurrentFileLength = args.CurrentFileLength
            };
        }
    }

    internal static class ExtractExporter
    {
        public static List<ExtractedFileDto> ExportAuto(Wz_Node node, string outputDirectory, bool recursive)
        {
            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);

            var files = new List<ExtractedFileDto>();
            if (recursive)
            {
                foreach (var current in Traverse(node))
                {
                    ExportSingle(current, fullOutputDirectory, node, files);
                }
            }
            else
            {
                ExportSingle(node, fullOutputDirectory, node, files);
            }

            return files;
        }

        public static ExtractedFileDto ExportXml(Wz_Node node, string output)
        {
            string fullOutput = Path.GetFullPath(output);
            if (Directory.Exists(fullOutput) || string.IsNullOrEmpty(Path.GetExtension(fullOutput)))
            {
                Directory.CreateDirectory(fullOutput);
                fullOutput = Path.Combine(fullOutput, SanitizeFileName(node.Text) + ".xml");
            }
            else
            {
                string directory = Path.GetDirectoryName(fullOutput);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            var settings = new XmlWriterSettings
            {
                Indent = true
            };
            using (var writer = XmlWriter.Create(fullOutput, settings))
            {
                node.DumpAsXml(writer);
            }

            return new ExtractedFileDto
            {
                SourcePath = node.FullPath,
                OutputPath = fullOutput,
                Type = "xml",
                Bytes = new FileInfo(fullOutput).Length
            };
        }

        public static string WriteManifest(ExtractResultDto result, string manifestPath)
        {
            string fullPath = Path.GetFullPath(manifestPath);
            EnsureParentDirectory(fullPath);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            return fullPath;
        }

        private static IEnumerable<Wz_Node> Traverse(Wz_Node root)
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

                yield return node;

                var children = node.Nodes.ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }
        }

        private static void ExportSingle(Wz_Node node, string outputDirectory, Wz_Node root, List<ExtractedFileDto> files)
        {
            node = NodePath.ExtractImageNode(node, true);
            if (node == null || node.Value == null || node.Value is Wz_File || node.Value is Wz_Image)
            {
                return;
            }

            if (node.Value is Wz_Png)
            {
                ExportPng(node, outputDirectory, root, files);
            }
            else if (node.Value is Wz_Sound)
            {
                ExportSound(node, outputDirectory, root, files);
            }
            else if (node.Value is Wz_RawData)
            {
                ExportBlob(node, outputDirectory, root, "raw", ".bin", files);
            }
            else if (node.Value is Wz_Video)
            {
                ExportBlob(node, outputDirectory, root, "video", ".mcv", files);
            }
            else
            {
                ExportText(node, outputDirectory, root, files);
            }
        }

        private static void ExportPng(Wz_Node node, string outputDirectory, Wz_Node root, List<ExtractedFileDto> files)
        {
            var png = (Wz_Png)node.Value;
            for (int page = 0; page < png.ActualPages; page++)
            {
                string suffix = png.ActualPages > 1 ? ".p" + (page + 1) : string.Empty;
                string path = GetOutputPath(outputDirectory, root, node, suffix + ".png");
                EnsureParentDirectory(path);

                using (var bitmap = png.ExtractPng(page))
                {
                    bitmap.Save(path, ImageFormat.Png);
                }

                files.Add(CreateFileDto(node, path, "png"));
            }
        }

        private static void ExportSound(Wz_Node node, string outputDirectory, Wz_Node root, List<ExtractedFileDto> files)
        {
            var sound = (Wz_Sound)node.Value;
            byte[] data = sound.ExtractSound();
            string type = sound.SoundType.ToString().ToLowerInvariant();
            string extension = GetSoundExtension(sound.SoundType);

            if (data == null)
            {
                data = new byte[sound.DataLength];
                sound.CopyTo(data, 0);
                type = "sound";
            }

            string path = GetOutputPath(outputDirectory, root, node, extension);
            EnsureParentDirectory(path);
            File.WriteAllBytes(path, data);
            files.Add(CreateFileDto(node, path, type));
        }

        private static void ExportBlob(Wz_Node node, string outputDirectory, Wz_Node root, string type, string extension, List<ExtractedFileDto> files)
        {
            byte[] data;
            if (node.Value is Wz_RawData)
            {
                var rawData = (Wz_RawData)node.Value;
                data = new byte[rawData.Length];
                rawData.CopyTo(data, 0);
            }
            else
            {
                var video = (Wz_Video)node.Value;
                data = new byte[video.Length];
                video.CopyTo(data, 0);
            }

            string path = GetOutputPath(outputDirectory, root, node, extension);
            EnsureParentDirectory(path);
            File.WriteAllBytes(path, data);
            files.Add(CreateFileDto(node, path, type));
        }

        private static void ExportText(Wz_Node node, string outputDirectory, Wz_Node root, List<ExtractedFileDto> files)
        {
            string value = NodeDto.FormatValue(node.Value) ?? Convert.ToString(node.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            string path = GetOutputPath(outputDirectory, root, node, ".txt");
            EnsureParentDirectory(path);
            File.WriteAllText(path, value);
            files.Add(CreateFileDto(node, path, NodeDto.GetTypeName(node.Value)));
        }

        private static string GetSoundExtension(Wz_SoundType type)
        {
            switch (type)
            {
                case Wz_SoundType.Mp3:
                    return ".mp3";
                case Wz_SoundType.Pcm:
                    return ".wav";
                default:
                    return ".bin";
            }
        }

        private static string GetOutputPath(string outputDirectory, Wz_Node root, Wz_Node node, string extension)
        {
            var segments = GetRelativeSegments(root, node);
            if (segments.Count == 0)
            {
                segments.Add(SanitizeFileName(node.Text));
            }

            segments[segments.Count - 1] = segments[segments.Count - 1] + extension;
            return Path.Combine(new[] { outputDirectory }.Concat(segments).ToArray());
        }

        private static List<string> GetRelativeSegments(Wz_Node root, Wz_Node node)
        {
            var segments = new List<string>();
            Wz_Node current = node;
            while (current != null && current != root)
            {
                segments.Add(SanitizeFileName(current.Text));
                current = current.ParentNode;
            }

            segments.Reverse();
            return segments;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "_";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            return new string(chars);
        }

        private static void EnsureParentDirectory(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static ExtractedFileDto CreateFileDto(Wz_Node node, string path, string type)
        {
            return new ExtractedFileDto
            {
                SourcePath = node.FullPath,
                OutputPath = path,
                Type = type,
                Bytes = new FileInfo(path).Length
            };
        }
    }

    internal sealed class ExtractResultDto
    {
        public string InputPath { get; set; }
        public string SourcePath { get; set; }
        public string ManifestPath { get; set; }
        public List<ExtractedFileDto> Files { get; set; }

        public static ExtractResultDto Create(string inputPath, Wz_Node node)
        {
            return new ExtractResultDto
            {
                InputPath = Path.GetFullPath(inputPath),
                SourcePath = node.FullPath,
                Files = new List<ExtractedFileDto>()
            };
        }
    }

    internal sealed class ExtractedFileDto
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public string Type { get; set; }
        public long Bytes { get; set; }
    }

    internal static class DomainInfoFinder
    {
        public static Wz_Node FindDataNode(Wz_Node root, string kind, string id)
        {
            var candidates = BuildIdCandidates(kind, id);
            foreach (Wz_Node node in Traverse(root, true))
            {
                if (MatchesAny(node.Text, candidates))
                {
                    if (IsPreferredDomainPath(node, kind))
                    {
                        return node;
                    }
                }
            }

            foreach (Wz_Node node in Traverse(root, true))
            {
                if (MatchesAny(node.Text, candidates))
                {
                    return node;
                }
            }

            return null;
        }

        public static DomainStringInfo FindStringInfo(Wz_Node root, string kind, string id)
        {
            var candidates = BuildIdCandidates(kind, id);
            foreach (Wz_Node node in Traverse(root, true))
            {
                if (!MatchesAny(node.Text, candidates))
                {
                    continue;
                }

                var info = DomainStringInfo.FromNode(node);
                if (info.HasValues && IsPreferredStringPath(node, kind))
                {
                    return info;
                }
            }

            foreach (Wz_Node node in Traverse(root, true))
            {
                if (!MatchesAny(node.Text, candidates))
                {
                    continue;
                }

                var info = DomainStringInfo.FromNode(node);
                if (info.HasValues)
                {
                    return info;
                }
            }

            return null;
        }

        private static List<string> BuildIdCandidates(string kind, string id)
        {
            var candidates = new List<string>();
            AddUnique(candidates, id);
            AddUnique(candidates, id + ".img");

            int numericId;
            if (int.TryParse(id, out numericId))
            {
                if (string.Equals(kind, "map", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(candidates, numericId.ToString("d9"));
                    AddUnique(candidates, numericId.ToString("d9") + ".img");
                }
                else if (string.Equals(kind, "mob", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "npc", StringComparison.OrdinalIgnoreCase))
                {
                    AddUnique(candidates, numericId.ToString("d7"));
                    AddUnique(candidates, numericId.ToString("d7") + ".img");
                    AddUnique(candidates, numericId.ToString("d8"));
                    AddUnique(candidates, numericId.ToString("d8") + ".img");
                }
                else
                {
                    AddUnique(candidates, numericId.ToString("d8"));
                    AddUnique(candidates, numericId.ToString("d8") + ".img");
                }
            }

            return candidates;
        }

        private static void AddUnique(List<string> list, string value)
        {
            if (!string.IsNullOrEmpty(value) && !list.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(value);
            }
        }

        private static bool MatchesAny(string text, List<string> candidates)
        {
            return candidates.Any(candidate => string.Equals(text, candidate, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPreferredDomainPath(Wz_Node node, string kind)
        {
            string path = NormalizePath(node.FullPath);
            if (string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase))
            {
                kind = "item";
            }

            if (string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/skill", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/item", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("/character", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "map", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/map", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "mob", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/mob", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "npc", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/npc", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "quest", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("/quest", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return true;
        }

        private static bool IsPreferredStringPath(Wz_Node node, string kind)
        {
            string path = NormalizePath(node.FullPath);
            if (string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase))
            {
                kind = "item";
            }

            if (string.Equals(kind, "skill", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("skill.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.EndsWith("/skill.img", StringComparison.OrdinalIgnoreCase);
            }
            if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("cash.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("consume.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("eqp.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("etc.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("ins.img/", StringComparison.OrdinalIgnoreCase) >= 0
                    || path.IndexOf("pet.img/", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "map", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("map.img/", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "mob", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("mob.img/", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "npc", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("npc.img/", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (string.Equals(kind, "quest", StringComparison.OrdinalIgnoreCase))
            {
                return path.IndexOf("quest.img/", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            return true;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        private static IEnumerable<Wz_Node> Traverse(Wz_Node root, bool extractImages)
        {
            if (root == null)
            {
                yield break;
            }

            var stack = new Stack<Wz_Node>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Wz_Node node = NodePath.ExtractImageNode(stack.Pop(), extractImages);
                if (node == null)
                {
                    continue;
                }

                yield return node;

                var children = node.Nodes.ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }
        }
    }

    internal sealed class DomainStringInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public Dictionary<string, string> Values { get; set; }

        public bool HasValues
        {
            get
            {
                return !string.IsNullOrEmpty(this.Name)
                    || !string.IsNullOrEmpty(this.Description)
                    || (this.Values != null && this.Values.Count > 0);
            }
        }

        public static DomainStringInfo FromNode(Wz_Node node)
        {
            var info = new DomainStringInfo
            {
                Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            };
            info.Values["__path"] = node.FullPath;

            foreach (Wz_Node child in node.Nodes)
            {
                string value = NodeDto.FormatValue(child.Value);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                info.Values[child.Text] = value;
                if (string.Equals(child.Text, "name", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(child.Text, "mapName", StringComparison.OrdinalIgnoreCase))
                {
                    info.Name = value;
                }
                else if (string.Equals(child.Text, "desc", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(child.Text, "h", StringComparison.OrdinalIgnoreCase))
                {
                    info.Description = value;
                }
            }

            if (string.IsNullOrEmpty(info.Name))
            {
                string streetName;
                string mapName;
                if (info.Values.TryGetValue("streetName", out streetName)
                    && info.Values.TryGetValue("mapName", out mapName))
                {
                    info.Name = streetName + " - " + mapName;
                }
            }

            return info;
        }
    }

    internal sealed class DomainInfoDto
    {
        public string Kind { get; set; }
        public string Id { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int ChildrenCount { get; set; }
        public int? LevelCount { get; set; }
        public int? MaxLevel { get; set; }
        public Dictionary<string, string> Properties { get; set; }
        public Dictionary<string, string> StringProperties { get; set; }
        public List<string> IconPaths { get; set; }

        public static DomainInfoDto FromNode(string kind, string id, Wz_Node node, DomainStringInfo stringInfo)
        {
            var dto = new DomainInfoDto
            {
                Kind = kind,
                Id = id,
                Path = node.FullPath,
                Name = stringInfo == null ? null : stringInfo.Name,
                Description = stringInfo == null ? null : stringInfo.Description,
                ChildrenCount = node.Nodes.Count,
                Properties = CollectImmediateProperties(node),
                StringProperties = stringInfo == null ? new Dictionary<string, string>() : stringInfo.Values,
                IconPaths = CollectIconPaths(node)
            };

            ApplyLevelInfo(dto, node);
            return dto;
        }

        private static Dictionary<string, string> CollectImmediateProperties(Wz_Node node)
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Wz_Node child in node.Nodes)
            {
                string value = NodeDto.FormatValue(child.Value);
                if (!string.IsNullOrEmpty(value))
                {
                    properties[child.Text] = value;
                }
            }
            return properties;
        }

        private static List<string> CollectIconPaths(Wz_Node node)
        {
            var paths = new List<string>();
            var stack = new Stack<Wz_Node>();
            stack.Push(node);
            while (stack.Count > 0)
            {
                Wz_Node current = NodePath.ExtractImageNode(stack.Pop(), true);
                if (current == null)
                {
                    continue;
                }

                if (current.Value is Wz_Png
                    && current.Text != null
                    && current.Text.IndexOf("icon", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    paths.Add(current.FullPath);
                }

                var children = current.Nodes.ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }
            return paths;
        }

        private static void ApplyLevelInfo(DomainInfoDto dto, Wz_Node node)
        {
            Wz_Node level = FindChild(node, "level");
            if (level == null)
            {
                return;
            }

            dto.LevelCount = level.Nodes.Count;
            int maxLevel = 0;
            foreach (Wz_Node child in level.Nodes)
            {
                int parsed;
                if (int.TryParse(child.Text, out parsed) && parsed > maxLevel)
                {
                    maxLevel = parsed;
                }
            }
            if (maxLevel > 0)
            {
                dto.MaxLevel = maxLevel;
            }
        }

        private static Wz_Node FindChild(Wz_Node node, string name)
        {
            foreach (Wz_Node child in node.Nodes)
            {
                if (string.Equals(child.Text, name, StringComparison.OrdinalIgnoreCase))
                {
                    return NodePath.ExtractImageNode(child, true);
                }
            }
            return null;
        }
    }

    internal sealed class SkillFullDto
    {
        public string Kind { get; set; }
        public string Id { get; set; }
        public string Mode { get; set; }
        public bool FoundData { get; set; }
        public string DataPath { get; set; }
        public string StringPath { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string PassiveDescription { get; set; }
        public int? Level { get; set; }
        public int? MaxLevel { get; set; }
        public int? MasterLevel { get; set; }
        public int? LevelCount { get; set; }
        public bool PreBigBangSkill { get; set; }
        public string RawSummary { get; set; }
        public string ResolvedSummary { get; set; }
        public int? NextLevel { get; set; }
        public string NextRawSummary { get; set; }
        public string NextResolvedSummary { get; set; }
        public Dictionary<string, string> Common { get; set; }
        public Dictionary<string, string> EffectiveProperties { get; set; }
        public List<SkillLevelPropertiesDto> LevelProperties { get; set; }
        public Dictionary<string, string> PvpCommon { get; set; }
        public Dictionary<string, string> StringProperties { get; set; }
        public Dictionary<string, bool> Flags { get; set; }
        public Dictionary<string, string> SpecialProperties { get; set; }
        public Dictionary<string, int> RequiredSkills { get; set; }
        public int? RequiredLevel { get; set; }
        public int? RequiredAmount { get; set; }
        public List<string> Actions { get; set; }
        public List<SkillIconDto> Icons { get; set; }
        public List<SkillVectorDto> Vectors { get; set; }
        public Dictionary<string, List<SkillExtraPropertyDto>> AttackInfo { get; set; }
        public List<SkillSummaryVariantDto> SummaryVariants { get; set; }
        public List<string> Diagnostics { get; set; }

        public static SkillFullDto FromStringOnly(string id, DomainStringInfo stringInfo)
        {
            var skillString = SkillStringInfo.FromDomainStringInfo(stringInfo);
            return new SkillFullDto
            {
                Kind = "skill",
                Id = id,
                Mode = "string-only",
                FoundData = false,
                StringPath = stringInfo == null ? null : stringInfo.Values.GetValueOrDefault("__path"),
                Name = skillString.Name,
                Description = skillString.Description,
                PassiveDescription = skillString.PassiveDescription,
                StringProperties = skillString.Values,
                Common = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                EffectiveProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                LevelProperties = new List<SkillLevelPropertiesDto>(),
                PvpCommon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                Flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
                SpecialProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                RequiredSkills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                Actions = new List<string>(),
                Icons = new List<SkillIconDto>(),
                Vectors = new List<SkillVectorDto>(),
                AttackInfo = new Dictionary<string, List<SkillExtraPropertyDto>>(StringComparer.OrdinalIgnoreCase),
                SummaryVariants = skillString.BuildVariants(),
                Diagnostics = new List<string> { "No matching skill data node was found; output contains String.wz metadata only." }
            };
        }

        public static SkillFullDto FromNode(string id, Wz_Node node, DomainStringInfo stringInfo, int? requestedLevel)
        {
            var model = HeadlessSkillModel.FromNode(node);
            var skillString = SkillStringInfo.FromDomainStringInfo(stringInfo);
            int selectedLevel = ResolveLevel(model, requestedLevel);
            var effective = model.GetEffectiveProperties(selectedLevel);
            var diagnostics = new List<string>();
            string rawSummary = skillString.SelectSummary(model.PreBigBangSkill, selectedLevel);
            string resolvedSummary = CliSkillSummaryResolver.Resolve(rawSummary, selectedLevel, effective, diagnostics);
            int? nextLevel = ResolveNextLevel(model, selectedLevel);
            string nextRawSummary = null;
            string nextResolvedSummary = null;
            if (nextLevel.HasValue)
            {
                nextRawSummary = skillString.SelectSummary(model.PreBigBangSkill, nextLevel.Value);
                nextResolvedSummary = CliSkillSummaryResolver.Resolve(nextRawSummary, nextLevel.Value, model.GetEffectiveProperties(nextLevel.Value), diagnostics);
            }

            if (stringInfo == null)
            {
                diagnostics.Add("No --string-wz was provided or no String.wz skill entry was found.");
            }
            if (string.IsNullOrEmpty(rawSummary))
            {
                diagnostics.Add("No skill summary template was found in string metadata.");
            }

            return new SkillFullDto
            {
                Kind = "skill",
                Id = id,
                Mode = "charasim-headless",
                FoundData = true,
                DataPath = node.FullPath,
                StringPath = stringInfo == null ? null : stringInfo.Values.GetValueOrDefault("__path"),
                Name = skillString.Name,
                Description = skillString.Description,
                PassiveDescription = skillString.PassiveDescription,
                Level = selectedLevel,
                MaxLevel = model.MaxLevel > 0 ? (int?)model.MaxLevel : null,
                MasterLevel = model.MasterLevel > 0 ? (int?)model.MasterLevel : null,
                LevelCount = model.LevelProperties.Count > 0 ? (int?)model.LevelProperties.Count : null,
                PreBigBangSkill = model.PreBigBangSkill,
                RawSummary = rawSummary,
                ResolvedSummary = resolvedSummary,
                NextLevel = nextLevel,
                NextRawSummary = nextRawSummary,
                NextResolvedSummary = nextResolvedSummary,
                Common = model.Common,
                EffectiveProperties = effective,
                LevelProperties = model.LevelProperties
                    .Select(item => new SkillLevelPropertiesDto { Level = item.Key, Properties = item.Value })
                    .ToList(),
                PvpCommon = model.PvpCommon,
                StringProperties = skillString.Values,
                Flags = model.Flags,
                SpecialProperties = model.SpecialProperties,
                RequiredSkills = model.RequiredSkills,
                RequiredLevel = model.RequiredLevel,
                RequiredAmount = model.RequiredAmount,
                Actions = model.Actions,
                Icons = model.Icons,
                Vectors = model.Vectors,
                AttackInfo = model.AttackInfo,
                SummaryVariants = skillString.BuildVariants(),
                Diagnostics = diagnostics
            };
        }

        private static int ResolveLevel(HeadlessSkillModel model, int? requestedLevel)
        {
            if (requestedLevel.HasValue)
            {
                return requestedLevel.Value;
            }
            if (model.MaxLevel > 0)
            {
                return model.MaxLevel;
            }
            if (model.LevelProperties.Count > 0)
            {
                return model.LevelProperties.Keys.Max();
            }
            return 1;
        }

        private static int? ResolveNextLevel(HeadlessSkillModel model, int selectedLevel)
        {
            bool disableNextLevelInfo;
            if (model.Flags.TryGetValue("disableNextLevelInfo", out disableNextLevelInfo) && disableNextLevelInfo)
            {
                return null;
            }

            if (model.MaxLevel > selectedLevel)
            {
                return selectedLevel + 1;
            }

            if (model.LevelProperties.Count > 0)
            {
                foreach (int level in model.LevelProperties.Keys)
                {
                    if (level > selectedLevel)
                    {
                        return level;
                    }
                }
            }

            return null;
        }
    }

    internal sealed class HeadlessSkillModel
    {
        public Dictionary<string, string> Common { get; private set; }
        public SortedDictionary<int, Dictionary<string, string>> LevelProperties { get; private set; }
        public Dictionary<string, string> PvpCommon { get; private set; }
        public Dictionary<string, bool> Flags { get; private set; }
        public Dictionary<string, string> SpecialProperties { get; private set; }
        public Dictionary<string, int> RequiredSkills { get; private set; }
        public int? RequiredLevel { get; private set; }
        public int? RequiredAmount { get; private set; }
        public int MasterLevel { get; private set; }
        public int MaxLevel { get; private set; }
        public bool PreBigBangSkill { get; private set; }
        public List<string> Actions { get; private set; }
        public List<SkillIconDto> Icons { get; private set; }
        public List<SkillVectorDto> Vectors { get; private set; }
        public Dictionary<string, List<SkillExtraPropertyDto>> AttackInfo { get; private set; }

        private HeadlessSkillModel()
        {
            Common = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            LevelProperties = new SortedDictionary<int, Dictionary<string, string>>();
            PvpCommon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Flags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            SpecialProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            RequiredSkills = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Actions = new List<string>();
            Icons = new List<SkillIconDto>();
            Vectors = new List<SkillVectorDto>();
            AttackInfo = new Dictionary<string, List<SkillExtraPropertyDto>>(StringComparer.OrdinalIgnoreCase);
        }

        public static HeadlessSkillModel FromNode(Wz_Node node)
        {
            var model = new HeadlessSkillModel();
            foreach (Wz_Node child in node.Nodes)
            {
                string name = child.Text;
                switch (name)
                {
                    case "icon":
                    case "iconMouseOver":
                    case "iconDisabled":
                        model.Icons.Add(SkillIconDto.FromNode(name, child));
                        break;
                    case "common":
                        model.ReadCommon(child);
                        break;
                    case "PVPcommon":
                        model.PvpCommon = CollectScalarProperties(child);
                        break;
                    case "level":
                        model.ReadLevels(child);
                        break;
                    case "req":
                        model.ReadRequirements(child);
                        break;
                    case "action":
                        model.ReadActions(child);
                        break;
                    case "masterLevel":
                        model.MasterLevel = child.GetValue<int>();
                        model.SpecialProperties[name] = model.MasterLevel.ToString();
                        break;
                    case "reqLev":
                        model.RequiredLevel = child.GetValue<int>();
                        model.SpecialProperties[name] = model.RequiredLevel.ToString();
                        break;
                    case "hyper":
                    case "vSkill":
                    case "vehicleID":
                        model.SpecialProperties[name] = NodeDto.FormatValue(child.Value);
                        break;
                    case "hyperStat":
                    case "invisible":
                    case "combatOrders":
                    case "notRemoved":
                    case "origin":
                    case "ascent":
                    case "timeLimited":
                    case "isPetAutoBuff":
                    case "isSequenceOn":
                    case "disableNextLevelInfo":
                        model.Flags[name] = child.GetValue<int>() != 0;
                        break;
                    case "relationSkill":
                    case "addAttack":
                    case "assistSkillLink":
                        model.SpecialProperties[name] = SummarizeChildValues(child);
                        break;
                    default:
                        string value = NodeDto.FormatValue(child.Value);
                        if (!string.IsNullOrEmpty(value))
                        {
                            model.SpecialProperties[name] = value;
                        }
                        break;
                }
            }

            model.MaxLevel = model.ResolveMaxLevel();
            model.PreBigBangSkill = model.LevelProperties.Count > 0
                && (model.Common.Count == 0 || model.Common.ContainsKey("maxLevel"));
            return model;
        }

        public Dictionary<string, string> GetEffectiveProperties(int level)
        {
            if (PreBigBangSkill && level > 0)
            {
                Dictionary<string, string> props;
                if (LevelProperties.TryGetValue(level, out props))
                {
                    return new Dictionary<string, string>(props, StringComparer.OrdinalIgnoreCase);
                }
            }
            return new Dictionary<string, string>(Common, StringComparer.OrdinalIgnoreCase);
        }

        private void ReadCommon(Wz_Node commonNode)
        {
            foreach (Wz_Node prop in commonNode.Nodes)
            {
                if (string.Equals(prop.Text, "attackInfo", StringComparison.OrdinalIgnoreCase))
                {
                    ReadAttackInfo(prop);
                    continue;
                }

                var vector = prop.Value as Wz_Vector;
                if (vector != null)
                {
                    Vectors.Add(new SkillVectorDto { Name = prop.Text, X = vector.X, Y = vector.Y, Path = prop.FullPath });
                    continue;
                }

                string value = NodeDto.FormatValue(prop.Value);
                if (!string.IsNullOrEmpty(value))
                {
                    Common[prop.Text] = value;
                }
            }
        }

        private void ReadLevels(Wz_Node levelNode)
        {
            foreach (Wz_Node child in levelNode.Nodes)
            {
                int level;
                if (!int.TryParse(child.Text, out level))
                {
                    continue;
                }
                LevelProperties[level] = CollectScalarProperties(child);
            }
        }

        private void ReadRequirements(Wz_Node reqNode)
        {
            foreach (Wz_Node child in reqNode.Nodes)
            {
                if (string.Equals(child.Text, "level", StringComparison.OrdinalIgnoreCase))
                {
                    RequiredLevel = child.GetValue<int>();
                }
                else if (string.Equals(child.Text, "reqAmount", StringComparison.OrdinalIgnoreCase))
                {
                    RequiredAmount = child.GetValue<int>();
                }
                else
                {
                    int skillId;
                    if (int.TryParse(child.Text, out skillId))
                    {
                        RequiredSkills[child.Text] = child.GetValue<int>();
                    }
                }
            }
        }

        private void ReadActions(Wz_Node actionNode)
        {
            foreach (Wz_Node child in actionNode.Nodes.OrderBy(item => ParseIntOrMax(item.Text)))
            {
                string value = NodeDto.FormatValue(child.Value);
                if (!string.IsNullOrEmpty(value))
                {
                    Actions.Add(value);
                }
            }
        }

        private void ReadAttackInfo(Wz_Node attackInfoNode)
        {
            foreach (Wz_Node jobNode in attackInfoNode.Nodes)
            {
                var props = new List<SkillExtraPropertyDto>();
                foreach (Wz_Node prop in jobNode.Nodes)
                {
                    props.Add(new SkillExtraPropertyDto
                    {
                        Name = prop.Text,
                        Value = NodeDto.FormatValue(prop.Value)
                    });
                }
                AttackInfo[jobNode.Text] = props;
            }
        }

        private int ResolveMaxLevel()
        {
            string maxLevel;
            if (Common.TryGetValue("maxLevel", out maxLevel))
            {
                int parsed;
                if (int.TryParse(maxLevel, out parsed))
                {
                    return parsed;
                }
            }
            return LevelProperties.Count > 0 ? LevelProperties.Keys.Max() : 0;
        }

        private static Dictionary<string, string> CollectScalarProperties(Wz_Node node)
        {
            var props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Wz_Node child in node.Nodes)
            {
                if (child.Value is Wz_Vector)
                {
                    continue;
                }
                string value = NodeDto.FormatValue(child.Value);
                if (!string.IsNullOrEmpty(value))
                {
                    props[child.Text] = value;
                }
            }
            return props;
        }

        private static string SummarizeChildValues(Wz_Node node)
        {
            var parts = new List<string>();
            foreach (Wz_Node child in node.Nodes)
            {
                string value = NodeDto.FormatValue(child.Value);
                if (!string.IsNullOrEmpty(value))
                {
                    parts.Add(child.Text + "=" + value);
                }
            }
            return string.Join(" ", parts);
        }

        private static int ParseIntOrMax(string value)
        {
            int parsed;
            return int.TryParse(value, out parsed) ? parsed : int.MaxValue;
        }
    }

    internal sealed class SkillStringInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string PassiveDescription { get; set; }
        public Dictionary<string, string> Values { get; set; }
        public List<string> SkillH { get; private set; }
        public List<string> PassiveH { get; private set; }
        public List<string> HyperChangedH { get; private set; }
        public SortedDictionary<int, string> ExtraH { get; private set; }

        private SkillStringInfo()
        {
            Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            SkillH = new List<string>();
            PassiveH = new List<string>();
            HyperChangedH = new List<string>();
            ExtraH = new SortedDictionary<int, string>();
        }

        public static SkillStringInfo FromDomainStringInfo(DomainStringInfo info)
        {
            var result = new SkillStringInfo();
            if (info == null)
            {
                return result;
            }

            result.Values = new Dictionary<string, string>(info.Values ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            result.Name = Get(result.Values, "name") ?? info.Name;
            result.Description = Get(result.Values, "desc") ?? info.Description;
            result.PassiveDescription = Get(result.Values, "pdesc");

            string h = Get(result.Values, "h");
            if (!string.IsNullOrEmpty(h))
            {
                result.SkillH.Add(h);
            }
            else
            {
                for (int i = 1; ; i++)
                {
                    h = Get(result.Values, "h" + i);
                    if (string.IsNullOrEmpty(h))
                    {
                        break;
                    }
                    result.SkillH.Add(h);
                }
            }

            AddIfNotNull(result.PassiveH, Get(result.Values, "ph"));
            AddIfNotNull(result.HyperChangedH, Get(result.Values, "hch"));
            foreach (var item in result.Values)
            {
                if (item.Key.StartsWith("h_", StringComparison.OrdinalIgnoreCase))
                {
                    int level;
                    if (int.TryParse(item.Key.Substring(2), out level) && level > 0)
                    {
                        result.ExtraH[level] = item.Value;
                    }
                }
            }

            return result;
        }

        public string SelectSummary(bool preBigBangSkill, int level)
        {
            if (preBigBangSkill)
            {
                if (SkillH.Count >= level && level > 0)
                {
                    return SkillH[level - 1];
                }
                if (SkillH.Count == 1)
                {
                    return SkillH[0];
                }
                return null;
            }

            string h = SkillH.Count > 0 ? SkillH[0] : null;
            foreach (var item in ExtraH)
            {
                if (level < item.Key)
                {
                    break;
                }
                h = item.Value;
            }
            return h;
        }

        public List<SkillSummaryVariantDto> BuildVariants()
        {
            var variants = new List<SkillSummaryVariantDto>();
            for (int i = 0; i < SkillH.Count; i++)
            {
                variants.Add(new SkillSummaryVariantDto { Kind = "h", Level = SkillH.Count == 1 ? (int?)null : i + 1, Text = SkillH[i] });
            }
            foreach (string text in PassiveH)
            {
                variants.Add(new SkillSummaryVariantDto { Kind = "ph", Text = text });
            }
            foreach (string text in HyperChangedH)
            {
                variants.Add(new SkillSummaryVariantDto { Kind = "hch", Text = text });
            }
            foreach (var item in ExtraH)
            {
                variants.Add(new SkillSummaryVariantDto { Kind = "h_", Level = item.Key, Text = item.Value });
            }
            return variants;
        }

        private static string Get(Dictionary<string, string> values, string key)
        {
            string value;
            return values != null && values.TryGetValue(key, out value) ? value : null;
        }

        private static void AddIfNotNull(List<string> values, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }
    }

    internal static class CliSkillSummaryResolver
    {
        private static readonly Dictionary<string, string> GlobalVariableMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "comboConAran", "aranComboCon" }
        };

        public static string Resolve(string template, int level, Dictionary<string, string> properties, List<string> diagnostics)
        {
            if (template == null)
            {
                return null;
            }

            var output = new StringBuilder();
            int index = 0;
            while (index < template.Length)
            {
                if (template[index] == '#')
                {
                    int length = ReadPlaceholderLength(template, index + 1);
                    if (index + 1 < template.Length && template[index + 1] == 'c')
                    {
                        index += 2;
                        continue;
                    }
                    if (length > 0)
                    {
                        string key = template.Substring(index + 1, length);
                        string value;
                        if (TryGetValue(properties, key, out value)
                            || TryGetMappedValue(properties, key, out value))
                        {
                            output.Append(EvaluateValue(key, value, level, diagnostics));
                            index += length + 1;
                            continue;
                        }

                        diagnostics.Add("Unresolved summary placeholder: #" + key);
                        output.Append("#").Append(key);
                        index += length + 1;
                        continue;
                    }

                    index++;
                    continue;
                }

                if (template[index] == '\\' && index + 1 < template.Length)
                {
                    switch (template[index + 1])
                    {
                        case 'r':
                            output.Append('\r');
                            break;
                        case 'n':
                            output.Append('\n');
                            break;
                        case '\\':
                            output.Append('\\');
                            break;
                        default:
                            output.Append(template[index + 1]);
                            break;
                    }
                    index += 2;
                    continue;
                }

                output.Append(template[index]);
                index++;
            }

            return output.ToString().Replace("\t", string.Empty).TrimEnd('\r', '\n');
        }

        private static string EvaluateValue(string key, string value, int level, List<string> diagnostics)
        {
            try
            {
                decimal parsed = WzComparerR2.Calculator.Parse(value.ToLowerInvariant(), level);
                if (string.Equals(key, "cooltimeMS", StringComparison.Ordinal))
                {
                    return (parsed / 1000).ToString("0.##");
                }
                if (key.EndsWith("PerM", StringComparison.Ordinal))
                {
                    return (parsed / 100).ToString("0.#");
                }
                return parsed.ToString();
            }
            catch (Exception ex)
            {
                diagnostics.Add("Failed to evaluate #" + key + "='" + value + "': " + ex.Message);
                return value;
            }
        }

        private static bool TryGetMappedValue(Dictionary<string, string> properties, string key, out string value)
        {
            string mapped;
            if (GlobalVariableMapping.TryGetValue(key, out mapped) && !string.IsNullOrEmpty(mapped))
            {
                return TryGetValue(properties, mapped, out value);
            }
            value = null;
            return false;
        }

        private static bool TryGetValue(Dictionary<string, string> properties, string key, out string value)
        {
            if (properties != null)
            {
                foreach (var item in properties)
                {
                    if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        value = item.Value;
                        return true;
                    }
                }
            }
            value = null;
            return false;
        }

        private static int ReadPlaceholderLength(string text, int start)
        {
            int length = 0;
            while (start + length < text.Length)
            {
                char ch = text[start + length];
                if (ch == '_'
                    || (ch >= 'a' && ch <= 'z')
                    || (ch >= 'A' && ch <= 'Z')
                    || (length > 0 && ch >= '0' && ch <= '9'))
                {
                    length++;
                    continue;
                }
                break;
            }
            return length;
        }
    }

    internal sealed class SkillFullXmlWriter
    {
        public static string ToXml(SkillFullDto dto)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true
            };
            using (var stringWriter = new StringWriter())
            {
                using (var writer = XmlWriter.Create(stringWriter, settings))
                {
                    writer.WriteStartElement("skill");
                    writer.WriteAttributeString("id", dto.Id);
                    writer.WriteAttributeString("mode", dto.Mode);
                    writer.WriteAttributeString("foundData", dto.FoundData.ToString().ToLowerInvariant());
                    WriteElement(writer, "name", dto.Name);
                    WriteElement(writer, "description", dto.Description);
                    WriteElement(writer, "passiveDescription", dto.PassiveDescription);
                    writer.WriteStartElement("summary");
                    if (dto.Level.HasValue)
                    {
                        writer.WriteAttributeString("level", dto.Level.Value.ToString());
                    }
                    WriteElement(writer, "raw", dto.RawSummary);
                    WriteElement(writer, "resolved", dto.ResolvedSummary);
                    writer.WriteEndElement();
                    if (dto.NextLevel.HasValue)
                    {
                        writer.WriteStartElement("nextSummary");
                        writer.WriteAttributeString("level", dto.NextLevel.Value.ToString());
                        WriteElement(writer, "raw", dto.NextRawSummary);
                        WriteElement(writer, "resolved", dto.NextResolvedSummary);
                        writer.WriteEndElement();
                    }
                    WriteDictionary(writer, "common", "property", dto.Common);
                    WriteDictionary(writer, "effectiveProperties", "property", dto.EffectiveProperties);
                    WriteDictionary(writer, "pvpCommon", "property", dto.PvpCommon);
                    WriteDictionary(writer, "strings", "property", dto.StringProperties);
                    WriteDictionary(writer, "flags", "flag", dto.Flags.ToDictionary(item => item.Key, item => item.Value.ToString().ToLowerInvariant(), StringComparer.OrdinalIgnoreCase));
                    WriteDictionary(writer, "specialProperties", "property", dto.SpecialProperties);
                    WriteDictionary(writer, "requiredSkills", "skill", dto.RequiredSkills.ToDictionary(item => item.Key, item => item.Value.ToString(), StringComparer.OrdinalIgnoreCase));
                    writer.WriteStartElement("levels");
                    foreach (var level in dto.LevelProperties)
                    {
                        writer.WriteStartElement("level");
                        writer.WriteAttributeString("value", level.Level.ToString());
                        WriteDictionaryItems(writer, "property", level.Properties);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                    writer.WriteStartElement("actions");
                    foreach (string action in dto.Actions)
                    {
                        WriteElement(writer, "action", action);
                    }
                    writer.WriteEndElement();
                    writer.WriteStartElement("icons");
                    foreach (var icon in dto.Icons)
                    {
                        writer.WriteStartElement("icon");
                        writer.WriteAttributeString("name", icon.Name);
                        writer.WriteAttributeString("path", icon.Path);
                        writer.WriteAttributeString("type", icon.Type);
                        if (icon.Width.HasValue) writer.WriteAttributeString("width", icon.Width.Value.ToString());
                        if (icon.Height.HasValue) writer.WriteAttributeString("height", icon.Height.Value.ToString());
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                    writer.WriteStartElement("diagnostics");
                    foreach (string diagnostic in dto.Diagnostics)
                    {
                        WriteElement(writer, "diagnostic", diagnostic);
                    }
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }
                return stringWriter.ToString();
            }
        }

        private static void WriteDictionary(XmlWriter writer, string rootName, string itemName, Dictionary<string, string> values)
        {
            writer.WriteStartElement(rootName);
            WriteDictionaryItems(writer, itemName, values);
            writer.WriteEndElement();
        }

        private static void WriteDictionaryItems(XmlWriter writer, string itemName, Dictionary<string, string> values)
        {
            foreach (var item in values ?? new Dictionary<string, string>())
            {
                writer.WriteStartElement(itemName);
                writer.WriteAttributeString("name", item.Key);
                writer.WriteAttributeString("value", item.Value);
                writer.WriteEndElement();
            }
        }

        private static void WriteElement(XmlWriter writer, string name, string value)
        {
            if (value == null)
            {
                return;
            }
            writer.WriteStartElement(name);
            writer.WriteString(value);
            writer.WriteEndElement();
        }
    }

    internal sealed class SkillLevelPropertiesDto
    {
        public int Level { get; set; }
        public Dictionary<string, string> Properties { get; set; }
    }

    internal sealed class SkillIconDto
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string Format { get; set; }
        public int? Pages { get; set; }

        public static SkillIconDto FromNode(string name, Wz_Node node)
        {
            Wz_Node extracted = NodePath.ExtractImageNode(node, true) ?? node;
            var png = extracted.Value as Wz_Png;
            return new SkillIconDto
            {
                Name = name,
                Path = extracted.FullPath,
                Type = NodeDto.GetTypeName(extracted.Value),
                Width = png == null ? null : (int?)png.Width,
                Height = png == null ? null : (int?)png.Height,
                Format = png == null ? null : png.Format.ToString(),
                Pages = png == null ? null : (int?)png.ActualPages
            };
        }
    }

    internal sealed class SkillVectorDto
    {
        public string Name { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public string Path { get; set; }
    }

    internal sealed class SkillExtraPropertyDto
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    internal sealed class SkillSummaryVariantDto
    {
        public string Kind { get; set; }
        public int? Level { get; set; }
        public string Text { get; set; }
    }

    internal static class AnimationFrameExporter
    {
        public static AnimationFramesResultDto ExportFrames(Wz_Node node, string outputDirectory)
        {
            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);

            var result = new AnimationFramesResultDto
            {
                SourcePath = node.FullPath,
                OutputDirectory = fullOutputDirectory,
                Frames = new List<AnimationFrameDto>()
            };

            var frameNodes = GetFrameNodes(node);
            foreach (var frameNode in frameNodes)
            {
                string frameDirectory = Path.Combine(fullOutputDirectory, frameNode.Index.ToString("d4"));
                var files = ExtractExporter.ExportAuto(frameNode.Node, frameDirectory, true);
                result.Frames.Add(new AnimationFrameDto
                {
                    Index = frameNode.Index,
                    SourcePath = frameNode.Node.FullPath,
                    Delay = ReadDelay(frameNode.Node),
                    Files = files
                });
            }

            result.FrameCount = result.Frames.Count;
            result.ManifestPath = Path.Combine(fullOutputDirectory, "frames.json");
            File.WriteAllText(result.ManifestPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            return result;
        }

        private static List<AnimationFrameNode> GetFrameNodes(Wz_Node node)
        {
            var frames = new List<AnimationFrameNode>();
            foreach (Wz_Node child in node.Nodes)
            {
                int index;
                if (int.TryParse(child.Text, out index))
                {
                    frames.Add(new AnimationFrameNode(index, NodePath.ExtractImageNode(child, true)));
                }
            }

            if (frames.Count == 0)
            {
                frames.Add(new AnimationFrameNode(0, node));
            }

            return frames.OrderBy(frame => frame.Index).ToList();
        }

        private static int? ReadDelay(Wz_Node frameNode)
        {
            foreach (Wz_Node child in frameNode.Nodes)
            {
                if (string.Equals(child.Text, "delay", StringComparison.OrdinalIgnoreCase))
                {
                    int delay;
                    string value = NodeDto.FormatValue(child.Value);
                    if (int.TryParse(value, out delay))
                    {
                        return delay;
                    }
                }
            }
            return null;
        }

        private struct AnimationFrameNode
        {
            public AnimationFrameNode(int index, Wz_Node node)
            {
                this.Index = index;
                this.Node = node;
            }

            public int Index { get; private set; }
            public Wz_Node Node { get; private set; }
        }
    }

    internal sealed class AnimationFramesResultDto
    {
        public string SourcePath { get; set; }
        public string OutputDirectory { get; set; }
        public string ManifestPath { get; set; }
        public int FrameCount { get; set; }
        public List<AnimationFrameDto> Frames { get; set; }
    }

    internal sealed class AnimationFrameDto
    {
        public int Index { get; set; }
        public string SourcePath { get; set; }
        public int? Delay { get; set; }
        public List<ExtractedFileDto> Files { get; set; }
    }

    internal static class AnimationGifExporter
    {
        public static AnimationGifResultDto ExportGif(Wz_Node node, string outputPath, AnimationGifOptions options)
        {
            return Export(node, outputPath, new BuildInGifEncoder(), options);
        }

        public static AnimationGifResultDto ExportApng(Wz_Node node, string outputPath, AnimationGifOptions options, bool optimize)
        {
            return Export(node, outputPath, new BuildInApngEncoder { OptimizeEnabled = optimize }, options);
        }

        public static AnimationGifResultDto ExportFfmpeg(Wz_Node node, string outputPath, AnimationGifOptions options, string ffmpegPath, string ffmpegArgs)
        {
            return Export(node, outputPath, new FFmpegEncoder
            {
                FFmpegBinPath = ffmpegPath,
                FFmpegArgumentFormat = ffmpegArgs
            }, options);
        }

        private static AnimationGifResultDto Export(Wz_Node node, string outputPath, GifEncoder encoder, AnimationGifOptions options)
        {
            string fullOutputPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Gif gif = Gif.CreateFromNode(node, FindLinkedNode);
            if (gif == null || gif.Frames.Count == 0)
            {
                throw new UsageException("No GIF-compatible bitmap frames were found at path: " + node.FullPath);
            }
            gif = ApplyOptions(gif, options);
            if (gif.Frames.Count == 0)
            {
                throw new UsageException("No frames remain after applying frame range options.");
            }

            Rectangle rect = gif.GetRect();
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                throw new UsageException("Animation frame bounds are empty: " + node.FullPath);
            }

            using (encoder)
            {
                encoder.Init(fullOutputPath, rect.Width, rect.Height);
                gif.SaveGif(encoder, fullOutputPath, options.Background, options.MinAlpha);
            }

            var result = new AnimationGifResultDto
            {
                SourcePath = node.FullPath,
                OutputPath = fullOutputPath,
                Width = rect.Width,
                Height = rect.Height,
                FrameCount = gif.Frames.Count,
                Bytes = new FileInfo(fullOutputPath).Length,
                StartFrame = options.StartFrame,
                EndFrame = options.EndFrame,
                Scale = options.Scale,
                DelayOverride = options.DelayOverride,
                OriginOverride = options.OriginOverrideText,
                Frames = CollectFrameMetadata(node, options)
            };
            return result;
        }

        private static Gif ApplyOptions(Gif source, AnimationGifOptions options)
        {
            var result = new Gif();
            for (int index = 0; index < source.Frames.Count; index++)
            {
                if (index < options.StartFrame)
                {
                    continue;
                }
                if (options.EndFrame.HasValue && index > options.EndFrame.Value)
                {
                    continue;
                }

                var frame = source.Frames[index] as GifFrame;
                if (frame == null)
                {
                    continue;
                }

                Bitmap bitmap = options.Scale == 1d ? frame.Bitmap : ScaleBitmap(frame.Bitmap, options.Scale);
                Point origin = options.OriginOverride ?? ScalePoint(frame.Origin, options.Scale);
                int delay = options.DelayOverride ?? frame.Delay;
                if (delay <= 0)
                {
                    delay = 120;
                }

                result.Frames.Add(new GifFrame(bitmap, origin, delay)
                {
                    A0 = frame.A0,
                    A1 = frame.A1
                });
            }
            return result;
        }

        private static Bitmap ScaleBitmap(Bitmap bitmap, double scale)
        {
            if (bitmap == null)
            {
                return null;
            }

            int width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            int height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(bitmap, new Rectangle(0, 0, width, height));
            }
            return scaled;
        }

        private static Point ScalePoint(Point point, double scale)
        {
            if (scale == 1d)
            {
                return point;
            }
            return new Point(
                (int)Math.Round(point.X * scale),
                (int)Math.Round(point.Y * scale));
        }

        private static Wz_Node FindLinkedNode(string fullPath, Wz_File sourceWzFile)
        {
            return null;
        }

        private static List<AnimationGifFrameDto> CollectFrameMetadata(Wz_Node node, AnimationGifOptions options)
        {
            var frames = new List<AnimationGifFrameDto>();
            foreach (Wz_Node child in node.Nodes)
            {
                int index;
                if (!int.TryParse(child.Text, out index))
                {
                    continue;
                }
                if (index < options.StartFrame)
                {
                    continue;
                }
                if (options.EndFrame.HasValue && index > options.EndFrame.Value)
                {
                    continue;
                }

                Wz_Node frameNode = NodePath.ExtractImageNode(child, true) ?? child;
                if (!(frameNode.Value is Wz_Png))
                {
                    continue;
                }

                frames.Add(new AnimationGifFrameDto
                {
                    Index = index,
                    SourcePath = frameNode.FullPath,
                    Delay = options.DelayOverride ?? ReadDelay(frameNode)
                });
            }

            return frames.OrderBy(frame => frame.Index).ToList();
        }

        private static int ReadDelay(Wz_Node frameNode)
        {
            Wz_Node delayNode = frameNode.FindNodeByPath("delay");
            int delay = delayNode.GetValueEx<int>(0);
            return delay <= 0 ? 120 : delay;
        }
    }

    internal sealed class AnimationGifOptions
    {
        public Color Background { get; private set; }
        public int MinAlpha { get; private set; }
        public int StartFrame { get; private set; }
        public int? EndFrame { get; private set; }
        public int? DelayOverride { get; private set; }
        public double Scale { get; private set; }
        public Point? OriginOverride { get; private set; }
        public string OriginOverrideText { get; private set; }

        public static AnimationGifOptions FromArgs(ParsedArgs args)
        {
            var options = new AnimationGifOptions
            {
                Background = ParseColor(args.GetValue("background") ?? "transparent"),
                MinAlpha = Math.Max(0, Math.Min(255, args.GetInt("min-alpha", 0))),
                StartFrame = args.GetInt("start-frame", 0),
                EndFrame = ParseOptionalNonNegativeInt(args.GetValue("end-frame"), "end-frame"),
                DelayOverride = ParseOptionalPositiveInt(args.GetValue("delay"), "delay"),
                Scale = ParseScale(args.GetValue("scale"))
            };
            options.OriginOverride = ParseOrigin(args.GetValue("origin"), out string originText);
            options.OriginOverrideText = originText;
            if (options.StartFrame < 0)
            {
                throw new UsageException("animate --start-frame must be a non-negative integer.");
            }
            if (options.EndFrame.HasValue && options.EndFrame.Value < options.StartFrame)
            {
                throw new UsageException("animate --end-frame must be greater than or equal to --start-frame.");
            }
            return options;
        }

        private static Color ParseColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase))
            {
                return Color.Transparent;
            }
            if (value.Length == 7 && value[0] == '#')
            {
                int r;
                int g;
                int b;
                if (int.TryParse(value.Substring(1, 2), System.Globalization.NumberStyles.HexNumber, null, out r)
                    && int.TryParse(value.Substring(3, 2), System.Globalization.NumberStyles.HexNumber, null, out g)
                    && int.TryParse(value.Substring(5, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
                {
                    return Color.FromArgb(255, r, g, b);
                }
            }

            throw new UsageException("animate gif --background must be transparent or #RRGGBB.");
        }

        private static int? ParseOptionalNonNegativeInt(string value, string optionName)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            int parsed;
            if (!int.TryParse(value, out parsed) || parsed < 0)
            {
                throw new UsageException("animate --" + optionName + " must be a non-negative integer.");
            }
            return parsed;
        }

        private static int? ParseOptionalPositiveInt(string value, string optionName)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            int parsed;
            if (!int.TryParse(value, out parsed) || parsed <= 0)
            {
                throw new UsageException("animate --" + optionName + " must be a positive integer.");
            }
            return parsed;
        }

        private static double ParseScale(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 1d;
            }
            double parsed;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) || parsed <= 0d)
            {
                throw new UsageException("animate --scale must be a positive number.");
            }
            return parsed;
        }

        private static Point? ParseOrigin(string value, out string originText)
        {
            originText = null;
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var parts = value.Split(',');
            if (parts.Length != 2)
            {
                throw new UsageException("animate --origin must use x,y format.");
            }

            int x;
            int y;
            if (!int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y))
            {
                throw new UsageException("animate --origin must use integer x,y values.");
            }

            originText = x + "," + y;
            return new Point(x, y);
        }
    }

    internal sealed class AnimationGifResultDto
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public int FrameCount { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long Bytes { get; set; }
        public int StartFrame { get; set; }
        public int? EndFrame { get; set; }
        public double Scale { get; set; }
        public int? DelayOverride { get; set; }
        public string OriginOverride { get; set; }
        public List<AnimationGifFrameDto> Frames { get; set; }
    }

    internal sealed class AnimationGifFrameDto
    {
        public int Index { get; set; }
        public string SourcePath { get; set; }
        public int Delay { get; set; }
    }

    internal sealed class AvatarCodeDto
    {
        public string RawCode { get; set; }
        public bool IsValid { get; set; }
        public List<AvatarItemDto> Items { get; set; }
        public List<string> Warnings { get; set; }

        public static AvatarCodeDto Parse(string code)
        {
            var dto = new AvatarCodeDto
            {
                RawCode = code,
                Items = new List<AvatarItemDto>(),
                Warnings = new List<string>()
            };

            foreach (Match match in Regex.Matches(code ?? string.Empty, @"\d{4,}"))
            {
                if (dto.Items.Any(item => item.Id == match.Value))
                {
                    continue;
                }

                dto.Items.Add(AvatarItemDto.FromId(match.Value));
            }

            dto.IsValid = dto.Items.Count > 0;
            if (!dto.IsValid)
            {
                dto.Warnings.Add("No item ids were found in the avatar code.");
            }
            if (dto.Items.Any(item => item.Category == "unknown"))
            {
                dto.Warnings.Add("Some ids could not be classified by MapleStory item id prefix.");
            }

            return dto;
        }
    }

    internal sealed class AvatarRenderPlanDto
    {
        public string Mode { get; set; }
        public string OutputPath { get; set; }
        public string Action { get; set; }
        public string Emotion { get; set; }
        public bool Offline { get; set; }
        public bool ApiKeyProvided { get; set; }
        public bool CanRender { get; set; }
        public List<AvatarItemDto> Items { get; set; }
        public List<AvatarRenderCandidateDto> Candidates { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> Blockers { get; set; }

        public static AvatarRenderPlanDto Create(AvatarCodeDto code, string outputPath, string action, string emotion, bool offline, bool apiKeyProvided)
        {
            var warnings = new List<string>(code.Warnings ?? new List<string>());
            if (!offline && !apiKeyProvided)
            {
                warnings.Add("No MapleStory OpenAPI key was provided; dry-run will not attempt remote code expansion.");
            }

            return new AvatarRenderPlanDto
            {
                Mode = "dry-run",
                OutputPath = outputPath,
                Action = action,
                Emotion = emotion,
                Offline = offline,
                ApiKeyProvided = apiKeyProvided,
                CanRender = false,
                Items = code.Items,
                Candidates = code.Items.Select(AvatarRenderCandidateDto.FromItem).ToList(),
                Warnings = warnings,
                Blockers = new List<string>
                {
                    "AvatarCommon.AvatarCanvas and AvatarCanvasManager resolve WZ data through PluginManager.FindWz.",
                    "CLI must inject a headless WZ repository before AvatarCommon can render without the WinForms plugin host.",
                    "PNG rendering uses System.Drawing/GDI+ paths and still needs Windows verification with a real Maple client."
                }
            };
        }
    }

    internal sealed class AvatarRenderCandidateDto
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string SlotGuess { get; set; }
        public List<string> CandidatePaths { get; set; }

        public static AvatarRenderCandidateDto FromItem(AvatarItemDto item)
        {
            return new AvatarRenderCandidateDto
            {
                Id = item.Id,
                Category = item.Category,
                SlotGuess = item.SlotGuess,
                CandidatePaths = GuessCandidatePaths(item)
            };
        }

        private static List<string> GuessCandidatePaths(AvatarItemDto item)
        {
            string id = NormalizeItemId(item.Id);
            var paths = new List<string>();
            switch (item.Category)
            {
                case "body":
                case "head":
                    paths.Add("Character/" + id + ".img");
                    break;
                case "face":
                    paths.Add("Character/Face/" + id + ".img");
                    break;
                case "hair":
                    paths.Add("Character/Hair/" + id + ".img");
                    break;
                case "equipment":
                    string folder = EquipmentFolder(item.SlotGuess);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        paths.Add("Character/" + folder + "/" + id + ".img");
                    }
                    paths.Add("Character/" + id + ".img");
                    break;
                default:
                    paths.Add("Character/" + id + ".img");
                    break;
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string NormalizeItemId(string id)
        {
            int numericId;
            if (int.TryParse(id, out numericId) && numericId >= 0)
            {
                return numericId.ToString("D8", CultureInfo.InvariantCulture);
            }

            return id;
        }

        private static string EquipmentFolder(string slot)
        {
            switch (slot)
            {
                case "cap":
                    return "Cap";
                case "face-accessory":
                case "eye-accessory":
                case "earrings":
                case "pendant":
                case "belt":
                case "shoulder":
                case "pocket":
                case "badge":
                case "emblem":
                case "totem":
                    return "Accessory";
                case "coat":
                    return "Coat";
                case "longcoat":
                    return "Longcoat";
                case "pants":
                    return "Pants";
                case "shoes":
                    return "Shoes";
                case "glove":
                    return "Glove";
                case "shield":
                    return "Shield";
                case "cape":
                    return "Cape";
                case "ring":
                    return "Ring";
                case "medal":
                    return "Medal";
                case "weapon":
                    return "Weapon";
                default:
                    return null;
            }
        }
    }

    internal sealed class AvatarItemDto
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string SlotGuess { get; set; }

        public static AvatarItemDto FromId(string id)
        {
            int numericId;
            int.TryParse(id, out numericId);
            int prefix = numericId / 10000;

            string slot = GuessEquipmentSlot(prefix);
            return new AvatarItemDto
            {
                Id = id,
                Category = slot == "unknown" ? GuessGeneralCategory(prefix, numericId) : "equipment",
                SlotGuess = slot
            };
        }

        private static string GuessEquipmentSlot(int prefix)
        {
            switch (prefix)
            {
                case 100:
                    return "cap";
                case 101:
                    return "face-accessory";
                case 102:
                    return "eye-accessory";
                case 103:
                    return "earrings";
                case 104:
                    return "coat";
                case 105:
                    return "longcoat";
                case 106:
                    return "pants";
                case 107:
                    return "shoes";
                case 108:
                    return "glove";
                case 109:
                    return "shield";
                case 110:
                    return "cape";
                case 111:
                    return "ring";
                case 112:
                    return "pendant";
                case 113:
                    return "belt";
                case 114:
                    return "medal";
                case 115:
                    return "shoulder";
                case 116:
                    return "pocket";
                case 118:
                    return "badge";
                case 119:
                    return "emblem";
                case 120:
                    return "totem";
            }

            if (prefix >= 121 && prefix <= 170)
            {
                return "weapon";
            }

            return "unknown";
        }

        private static string GuessGeneralCategory(int prefix, int numericId)
        {
            if (numericId >= 2000 && numericId < 3000)
            {
                return "body";
            }
            if (numericId >= 12000 && numericId < 13000)
            {
                return "head";
            }
            if (numericId >= 20000 && numericId < 30000)
            {
                return "face";
            }
            if (numericId >= 30000 && numericId < 50000)
            {
                return "hair";
            }
            if (prefix >= 200 && prefix <= 245)
            {
                return "use";
            }
            if (prefix >= 300 && prefix <= 399)
            {
                return "install";
            }
            if (prefix >= 400 && prefix <= 499)
            {
                return "etc";
            }
            if (prefix >= 500 && prefix <= 599)
            {
                return "cash";
            }
            return "unknown";
        }
    }

    internal sealed class MapRenderPlanDto
    {
        public string Mode { get; set; }
        public string Id { get; set; }
        public string WzInputPath { get; set; }
        public string OutputPath { get; set; }
        public string Layer { get; set; }
        public bool IncludeLife { get; set; }
        public bool IncludeReactor { get; set; }
        public bool IncludeTooltip { get; set; }
        public bool CanRender { get; set; }
        public List<string> CandidatePaths { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> Blockers { get; set; }

        public static MapRenderPlanDto Create(string id, string wzInputPath, string outputPath, string layer, bool includeLife, bool includeReactor, bool includeTooltip)
        {
            return new MapRenderPlanDto
            {
                Mode = "dry-run",
                Id = id,
                WzInputPath = wzInputPath,
                OutputPath = outputPath,
                Layer = layer,
                IncludeLife = includeLife,
                IncludeReactor = includeReactor,
                IncludeTooltip = includeTooltip,
                CanRender = false,
                CandidatePaths = BuildCandidatePaths(id),
                Warnings = BuildWarnings(wzInputPath),
                Blockers = new List<string>
                {
                    "WzComparerR2.MapRender is built around MonoGame Game and GraphicsDevice lifecycle.",
                    "CLI needs an offscreen render target and deterministic viewport before screenshot export can run headless.",
                    "MapRender also depends on EmptyKeys UI, Bass/native runtime files, and real-client asset layout verification."
                }
            };
        }

        private static List<string> BuildCandidatePaths(string id)
        {
            var paths = new List<string>();
            int numericId;
            if (int.TryParse(id, out numericId) && numericId >= 0)
            {
                int group = numericId / 100000000;
                int shard = (numericId / 100000) % 1000;
                paths.Add("Map/Map/Map" + group + "/" + numericId.ToString("D9", CultureInfo.InvariantCulture) + ".img");
                paths.Add("Data/Map/Map/Map" + group + "/Map" + group + "_" + shard.ToString("D3", CultureInfo.InvariantCulture) + ".wz");
            }
            else
            {
                paths.Add("Map/Map/<group>/" + id + ".img");
            }

            return paths;
        }

        private static List<string> BuildWarnings(string wzInputPath)
        {
            var warnings = new List<string>();
            if (string.IsNullOrEmpty(wzInputPath))
            {
                warnings.Add("No map WZ input path was provided; dry-run only reports candidate paths.");
            }
            return warnings;
        }
    }

    internal sealed class MapMetadataDto
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public List<MapSectionItemDto> Portals { get; set; }
        public List<MapSectionItemDto> Life { get; set; }
        public List<MapSectionItemDto> Reactors { get; set; }
        public List<MapSectionItemDto> Objects { get; set; }
        public List<MapSectionItemDto> SelectedItems { get; set; }

        public static MapMetadataDto FromMapNode(string id, Wz_Node mapNode, string selectedSection)
        {
            var dto = new MapMetadataDto
            {
                Id = id,
                Path = mapNode.FullPath,
                Portals = CollectSection(mapNode, "portal", "portal"),
                Life = CollectSection(mapNode, "life", "life"),
                Reactors = CollectSection(mapNode, "reactor", "reactor"),
                Objects = CollectObjects(mapNode)
            };

            switch (selectedSection)
            {
                case "portals":
                    dto.SelectedItems = dto.Portals;
                    break;
                case "life":
                    dto.SelectedItems = dto.Life;
                    break;
                case "reactors":
                    dto.SelectedItems = dto.Reactors;
                    break;
                default:
                    dto.SelectedItems = dto.Objects;
                    break;
            }

            return dto;
        }

        private static List<MapSectionItemDto> CollectSection(Wz_Node mapNode, string sectionName, string kind)
        {
            var result = new List<MapSectionItemDto>();
            Wz_Node section = FindChild(mapNode, sectionName);
            if (section == null)
            {
                return result;
            }

            foreach (Wz_Node item in section.Nodes)
            {
                result.Add(MapSectionItemDto.FromNode(kind, null, item));
            }
            return result;
        }

        private static List<MapSectionItemDto> CollectObjects(Wz_Node mapNode)
        {
            var result = new List<MapSectionItemDto>();
            foreach (Wz_Node layer in mapNode.Nodes)
            {
                int layerNo;
                if (!int.TryParse(layer.Text, out layerNo))
                {
                    continue;
                }

                AddLayerItems(result, layer, layerNo, "obj", "object");
                AddLayerItems(result, layer, layerNo, "tile", "tile");
            }
            return result;
        }

        private static void AddLayerItems(List<MapSectionItemDto> result, Wz_Node layer, int layerNo, string sectionName, string kind)
        {
            Wz_Node section = FindChild(layer, sectionName);
            if (section == null)
            {
                return;
            }

            foreach (Wz_Node item in section.Nodes)
            {
                result.Add(MapSectionItemDto.FromNode(kind, layerNo, item));
            }
        }

        private static Wz_Node FindChild(Wz_Node node, string name)
        {
            foreach (Wz_Node child in node.Nodes)
            {
                if (string.Equals(child.Text, name, StringComparison.OrdinalIgnoreCase))
                {
                    return NodePath.ExtractImageNode(child, true);
                }
            }
            return null;
        }
    }

    internal sealed class MapSectionItemDto
    {
        public string Kind { get; set; }
        public string Index { get; set; }
        public int? Layer { get; set; }
        public string Path { get; set; }
        public string Summary { get; set; }
        public Dictionary<string, string> Properties { get; set; }

        public static MapSectionItemDto FromNode(string kind, int? layer, Wz_Node node)
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Wz_Node child in node.Nodes)
            {
                string value = NodeDto.FormatValue(child.Value);
                if (!string.IsNullOrEmpty(value))
                {
                    properties[child.Text] = value;
                }
            }

            return new MapSectionItemDto
            {
                Kind = kind,
                Index = node.Text,
                Layer = layer,
                Path = node.FullPath,
                Properties = properties,
                Summary = BuildSummary(properties)
            };
        }

        private static string BuildSummary(Dictionary<string, string> properties)
        {
            string[] keys = { "id", "type", "x", "y", "tm", "tn", "pn", "rx0", "rx1", "mobTime", "reactorTime" };
            var parts = new List<string>();
            foreach (string key in keys)
            {
                string value;
                if (properties.TryGetValue(key, out value))
                {
                    parts.Add(key + "=" + value);
                }
            }
            return string.Join(" ", parts);
        }
    }

    internal sealed class LuaRunResultDto
    {
        public string ScriptPath { get; set; }
        public string Code { get; set; }
        public bool IsEval { get; set; }
        public string WzInputPath { get; set; }
        public string WzRootName { get; set; }
        public int WzRootChildren { get; set; }
        public string Mode { get; set; }
        public string LuaExecutable { get; set; }
        public int ExitCode { get; set; }
        public string Stdout { get; set; }
        public string Stderr { get; set; }
        public int LineCount { get; set; }

        public static LuaRunResultDto Create(string scriptPath, string wzInput)
        {
            string fullScriptPath = Path.GetFullPath(scriptPath);
            return new LuaRunResultDto
            {
                ScriptPath = fullScriptPath,
                WzInputPath = string.IsNullOrEmpty(wzInput) ? null : Path.GetFullPath(wzInput),
                Mode = "external-lua",
                LineCount = File.Exists(fullScriptPath) ? File.ReadLines(fullScriptPath).Count() : 0
            };
        }

        public static LuaRunResultDto CreateEval(string code, string wzInput)
        {
            return new LuaRunResultDto
            {
                Code = code,
                IsEval = true,
                WzInputPath = string.IsNullOrEmpty(wzInput) ? null : Path.GetFullPath(wzInput),
                Mode = "external-lua",
                LineCount = string.IsNullOrEmpty(code) ? 0 : code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length
            };
        }
    }

    internal static class LuaExternalRunner
    {
        public static void Run(LuaRunResultDto result, int timeoutSeconds)
        {
            string executable = FindExecutable("lua")
                ?? FindExecutable("lua5.4")
                ?? FindExecutable("lua5.3")
                ?? FindExecutable("luajit");

            if (string.IsNullOrEmpty(executable))
            {
                result.Mode = "missing-executable";
                result.ExitCode = 1;
                result.Stderr = "No lua executable was found on PATH. Re-run with --dry-run to validate only.";
                return;
            }

            result.LuaExecutable = executable;
            var psi = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = result.IsEval ? Environment.CurrentDirectory : Path.GetDirectoryName(result.ScriptPath)
            };
            if (result.IsEval)
            {
                psi.ArgumentList.Add("-e");
                psi.ArgumentList.Add(result.Code);
            }
            else
            {
                psi.ArgumentList.Add(result.ScriptPath);
            }
            if (!string.IsNullOrEmpty(result.WzInputPath))
            {
                psi.Environment["WCR2_WZ_INPUT"] = result.WzInputPath;
                psi.Environment["WCR2_WZ_ROOT"] = result.WzRootName ?? string.Empty;
            }

            using (var process = new Process())
            {
                process.StartInfo = psi;
                process.Start();
                if (!process.WaitForExit(Math.Max(1, timeoutSeconds) * 1000))
                {
                    try
                    {
                        process.Kill();
                    }
                    catch
                    {
                    }
                    result.Mode = "timeout";
                    result.ExitCode = 1;
                    result.Stderr = "Lua process timed out.";
                    return;
                }

                result.Stdout = process.StandardOutput.ReadToEnd();
                result.Stderr = process.StandardError.ReadToEnd();
                result.ExitCode = process.ExitCode;
            }
        }

        private static string FindExecutable(string name)
        {
            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string directory in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                string path = Path.Combine(directory, name);
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }
    }

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

    internal sealed class UpdateReleaseDto
    {
        public string Repository { get; set; }
        public string ApiUrl { get; set; }
        public string CurrentVersion { get; set; }
        public string Name { get; set; }
        public string TagName { get; set; }
        public string Body { get; set; }
        public string CreatedAt { get; set; }
        public string HtmlUrl { get; set; }
        public bool? UpdateAvailable { get; set; }
        public List<UpdateAssetDto> Assets { get; set; }
        public UpdateAssetDto SelectedAsset { get; set; }

        public UpdateAssetDto SelectAsset(string assetKind)
        {
            string kind = string.IsNullOrWhiteSpace(assetKind) ? "net8" : assetKind.ToLowerInvariant();
            if (Assets == null || Assets.Count == 0)
            {
                return null;
            }

            return Assets.FirstOrDefault(asset => string.Equals(asset.Kind, kind, StringComparison.OrdinalIgnoreCase))
                ?? Assets.FirstOrDefault(asset => asset.Name != null && asset.Name.IndexOf(kind, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? Assets.FirstOrDefault(asset => string.Equals(asset.Kind, "zip", StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class UpdateAssetDto
    {
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Label { get; set; }
        public string ContentType { get; set; }
        public string BrowserDownloadUrl { get; set; }
        public long Size { get; set; }

        public static UpdateAssetDto FromJson(JsonElement asset)
        {
            string name = GetString(asset, "name");
            return new UpdateAssetDto
            {
                Kind = DetectKind(name),
                Name = name,
                Label = GetString(asset, "label"),
                ContentType = GetString(asset, "content_type"),
                BrowserDownloadUrl = GetString(asset, "browser_download_url"),
                Size = GetLong(asset, "size")
            };
        }

        private static string DetectKind(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "zip";
            }

            string lower = name.ToLowerInvariant();
            if (lower.Contains("net10"))
            {
                return "net10";
            }
            if (lower.Contains("net8"))
            {
                return "net8";
            }
            if (lower.Contains("net6"))
            {
                return "net6";
            }
            if (lower.Contains("net462"))
            {
                return "net462";
            }
            return lower.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "zip" : "asset";
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            JsonElement value;
            return element.TryGetProperty(propertyName, out value) && value.ValueKind != JsonValueKind.Null
                ? value.GetString()
                : null;
        }

        private static long GetLong(JsonElement element, string propertyName)
        {
            JsonElement value;
            return element.TryGetProperty(propertyName, out value) && value.TryGetInt64(out long result)
                ? result
                : 0;
        }
    }

    internal sealed class UpdateDownloadResultDto
    {
        public UpdateReleaseDto Release { get; set; }
        public UpdateAssetDto Asset { get; set; }
        public string OutputPath { get; set; }

        public static UpdateDownloadResultDto FromRelease(UpdateReleaseDto release, string outputPath)
        {
            return new UpdateDownloadResultDto
            {
                Release = release,
                Asset = release.SelectedAsset,
                OutputPath = outputPath
            };
        }
    }

    internal sealed class UpdateApplyPlanDto
    {
        public string Mode { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public UpdateReleaseDto Release { get; set; }
        public UpdateAssetDto Asset { get; set; }
        public string AssetKind { get; set; }
        public string DownloadPath { get; set; }
        public string UpdaterPath { get; set; }
        public string UpdaterVersionArg { get; set; }
        public bool ProcessStarted { get; set; }
        public int? ProcessId { get; set; }

        public static UpdateApplyPlanDto FromRelease(UpdateReleaseDto release, ParsedArgs args, string assetKind)
        {
            bool execute = args.HasFlag("execute");
            var plan = new UpdateApplyPlanDto
            {
                Mode = execute ? "execute" : "dry-run",
                Success = true,
                Release = release,
                Asset = release.SelectedAsset,
                AssetKind = assetKind,
                DownloadPath = args.GetValue("download"),
                UpdaterPath = args.GetValue("updater"),
                UpdaterVersionArg = GetUpdaterVersionArg(assetKind)
            };

            if (plan.Asset == null)
            {
                plan.Success = false;
                plan.Message = "No update asset matched --asset " + assetKind + ".";
            }
            else if (execute && string.IsNullOrEmpty(plan.UpdaterPath))
            {
                plan.Success = false;
                plan.Message = "update apply --execute requires --updater <path>.";
            }
            else if (execute && string.IsNullOrEmpty(plan.UpdaterVersionArg))
            {
                plan.Success = false;
                plan.Message = "The existing external updater only supports net462, net6, and net8 assets.";
            }
            else if (execute)
            {
                plan.Message = "External updater will be launched after the asset is downloaded.";
            }
            else
            {
                plan.Message = "Dry-run only. Re-run with --execute --updater <path> to launch the external updater.";
            }

            return plan;
        }

        private static string GetUpdaterVersionArg(string kind)
        {
            switch ((kind ?? string.Empty).ToLowerInvariant())
            {
                case "net462":
                    return "4";
                case "net6":
                    return "6";
                case "net8":
                    return "8";
                default:
                    return null;
            }
        }
    }

    internal static class UpdateClient
    {
        private const string DefaultRepository = "seotbeo/WzComparerR2";

        public static async System.Threading.Tasks.Task<UpdateReleaseDto> QueryLatestAsync(ParsedArgs args)
        {
            string repository = args.GetValue("repo") ?? DefaultRepository;
            if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
            {
                throw new UsageException("--repo must use owner/name format.");
            }

            string apiUrl = "https://api.github.com/repos/" + repository + "/releases/latest";
            int timeoutSeconds = args.GetInt("timeout", 15);
            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
            {
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.UserAgent.ParseAdd("wcr2-cli/" + ProgramVersion);

                using (var response = await client.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using (var document = JsonDocument.Parse(json))
                    {
                        JsonElement root = document.RootElement;
                        string currentVersion = args.GetValue("current-version") ?? GetDefaultCurrentVersion();
                        var release = new UpdateReleaseDto
                        {
                            Repository = repository,
                            ApiUrl = apiUrl,
                            CurrentVersion = currentVersion,
                            Name = GetString(root, "name"),
                            TagName = GetString(root, "tag_name"),
                            Body = GetString(root, "body"),
                            CreatedAt = GetString(root, "created_at"),
                            HtmlUrl = GetString(root, "html_url"),
                            Assets = new List<UpdateAssetDto>()
                        };
                        release.UpdateAvailable = IsUpdateAvailable(currentVersion, release.TagName ?? release.Name);

                        JsonElement assets;
                        if (root.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement asset in assets.EnumerateArray())
                            {
                                release.Assets.Add(UpdateAssetDto.FromJson(asset));
                            }
                        }

                        return release;
                    }
                }
            }
        }

        public static async System.Threading.Tasks.Task<string> DownloadAssetAsync(UpdateAssetDto asset, string outputDirectory, bool force, int timeoutSeconds)
        {
            if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
            {
                throw new UsageException("Selected update asset has no download URL.");
            }

            string directory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(directory);
            string fileName = string.IsNullOrEmpty(asset.Name) ? "update.zip" : asset.Name;
            string outputPath = Path.Combine(directory, fileName);
            await DownloadAssetToFileAsync(asset, outputPath, force, timeoutSeconds).ConfigureAwait(false);
            return outputPath;
        }

        public static async System.Threading.Tasks.Task ExecuteApplyAsync(UpdateApplyPlanDto plan, bool force, int timeoutSeconds)
        {
            if (!plan.Success)
            {
                throw new UsageException(plan.Message);
            }

            string updaterPath = Path.GetFullPath(plan.UpdaterPath);
            if (!File.Exists(updaterPath))
            {
                throw new FileNotFoundException("Updater executable not found: " + updaterPath);
            }

            string downloadPath = plan.DownloadPath;
            if (string.IsNullOrEmpty(downloadPath))
            {
                downloadPath = Path.Combine(Path.GetTempPath(), "wcr2-update-" + Guid.NewGuid().ToString("N") + ".zip");
            }
            downloadPath = Path.GetFullPath(downloadPath);

            if (!File.Exists(downloadPath))
            {
                string directory = Path.GetDirectoryName(downloadPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                await DownloadAssetToFileAsync(plan.Asset, downloadPath, force, timeoutSeconds).ConfigureAwait(false);
            }

            var psi = new ProcessStartInfo(updaterPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(updaterPath)
            };
            psi.ArgumentList.Add(downloadPath);
            psi.ArgumentList.Add(plan.UpdaterVersionArg);

            var process = Process.Start(psi);
            plan.DownloadPath = downloadPath;
            plan.ProcessStarted = process != null;
            plan.ProcessId = process == null ? (int?)null : process.Id;
            plan.Message = process == null ? "Failed to start updater process." : "Updater process started.";
            plan.Success = process != null;
        }

        private static async System.Threading.Tasks.Task DownloadAssetToFileAsync(UpdateAssetDto asset, string outputPath, bool force, int timeoutSeconds)
        {
            if (File.Exists(outputPath))
            {
                if (!force)
                {
                    throw new UsageException("Output file already exists. Use --force to overwrite: " + outputPath);
                }
                File.Delete(outputPath);
            }

            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage(HttpMethod.Get, asset.BrowserDownloadUrl))
            {
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
                if (!string.IsNullOrEmpty(asset.ContentType))
                {
                    request.Headers.Accept.ParseAdd(asset.ContentType);
                }
                request.Headers.UserAgent.ParseAdd("wcr2-cli/" + ProgramVersion);

                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    bool fileCreated = false;
                    try
                    {
                        using (var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var fileStream = File.Create(outputPath))
                        {
                            fileCreated = true;
                            await responseStream.CopyToAsync(fileStream).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        if (fileCreated && File.Exists(outputPath))
                        {
                            File.Delete(outputPath);
                        }
                        throw;
                    }
                }
            }
        }

        private static string ProgramVersion
        {
            get { return "0.1.0"; }
        }

        private static string GetDefaultCurrentVersion()
        {
            var assembly = Assembly.GetEntryAssembly();
            var informational = assembly == null
                ? null
                : assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrEmpty(informational) ? ProgramVersion : informational;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            JsonElement value;
            return element.TryGetProperty(propertyName, out value) && value.ValueKind != JsonValueKind.Null
                ? value.GetString()
                : null;
        }

        private static bool? IsUpdateAvailable(string currentVersion, string latestVersion)
        {
            Version currentBuild;
            Version latestBuild;
            if (TryParseBuildVersion(currentVersion, out currentBuild) && TryParseBuildVersion(latestVersion, out latestBuild))
            {
                return latestBuild.Build > currentBuild.Build
                    || (latestBuild.Build == currentBuild.Build && latestBuild.Revision > currentBuild.Revision);
            }

            Version current;
            Version latest;
            if (TryParseVersion(currentVersion, out current) && TryParseVersion(latestVersion, out latest))
            {
                return latest > current;
            }

            return null;
        }

        private static bool TryParseBuildVersion(string value, out Version result)
        {
            var match = Regex.Match(value ?? string.Empty, @"(\d{6})(\d{2})$");
            if (match.Success
                && int.TryParse(match.Groups[1].Value, out int build)
                && int.TryParse(match.Groups[2].Value, out int revision))
            {
                result = new Version(0, 0, build, revision);
                return true;
            }

            result = null;
            return false;
        }

        private static bool TryParseVersion(string value, out Version result)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(1);
            }

            return Version.TryParse(normalized, out result);
        }
    }

    internal sealed class NetworkCommandDto
    {
        public string Command { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Mode { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
        public string ChatMessage { get; set; }

        public static NetworkCommandDto FromArgs(string command, ParsedArgs args)
        {
            return new NetworkCommandDto
            {
                Command = command,
                Host = args.GetValue("host") ?? "wc.kagamia.com",
                Port = args.GetInt("port", 2100),
                Mode = args.HasFlag("connect") ? "tcp-probe" : "dry-run",
                Success = true,
                ChatMessage = args.GetValue("message")
            };
        }
    }

    internal static class NetworkProbe
    {
        public static void TryConnect(NetworkCommandDto result, int timeoutSeconds)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var task = client.ConnectAsync(result.Host, result.Port);
                    if (!task.Wait(Math.Max(1, timeoutSeconds) * 1000))
                    {
                        result.Success = false;
                        result.Error = "TCP probe timed out.";
                        return;
                    }
                    result.Success = client.Connected;
                    result.Message = client.Connected ? "TCP connection succeeded." : "TCP connection failed.";
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }
        }
    }

    internal sealed class UsageException : Exception
    {
        public UsageException(string message)
            : base(message)
        {
        }
    }

    internal sealed class WzLoadException : Exception
    {
        public WzLoadException(string message)
            : base(message)
        {
        }

        public WzLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
