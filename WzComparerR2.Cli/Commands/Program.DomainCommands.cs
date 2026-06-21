using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
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
            string input = ResolveDomainInfoInput(args, kind);
            string id = args.GetValue("id");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(id))
            {
                throw new UsageException(kind + " info requires --id <id>.");
            }

            using (var repository = CliWzRepository.ForDomain(kind, input, args))
            {
                var dataResult = repository.FindDataNode(kind, id);
                if (dataResult == null)
                {
                    throw new UsageException(kind + " id not found: " + id);
                }

                DomainStringInfo stringInfo = null;
                var stringResult = repository.FindStringInfo(kind, id);
                if (stringResult != null)
                {
                    stringInfo = stringResult.StringInfo;
                }

                var dto = DomainInfoDto.FromNode(
                    kind,
                    id,
                    dataResult.Node,
                    stringInfo,
                    dataResult.InputPath,
                    stringResult == null ? null : stringResult.InputPath,
                    repository.DataInputPaths,
                    repository.StringInputPaths);
                WriteOutput(dto, json, writer =>
                {
                    writer.WriteLine(kind + " " + id);
                    if (!string.IsNullOrEmpty(dto.DataInputPath))
                    {
                        writer.WriteLine("DataInput: " + dto.DataInputPath);
                    }
                    if (!string.IsNullOrEmpty(dto.StringInputPath))
                    {
                        writer.WriteLine("StringInput: " + dto.StringInputPath);
                    }
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

        private static string ResolveDomainInfoInput(ParsedArgs args, string kind)
        {
            if (args.Positionals.Count > 1 && !IsHelp(args.Positionals[1]))
            {
                return args.Positionals[1];
            }

            string domainInput = GetDomainInputOption(args, kind);
            if (!string.IsNullOrEmpty(domainInput))
            {
                return domainInput;
            }

            string dataDir = args.GetValue("data-dir");
            if (!string.IsNullOrEmpty(dataDir))
            {
                return Path.Combine(dataDir, CliWzRepository.GetDefaultDataFolderName(kind));
            }

            string optionName = string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase) ? "character-wz" : kind + "-wz";
            throw new UsageException(kind + " info requires <wz-file-or-dir>, --" + optionName + " <path>, or --data-dir <dir>.");
        }

        private static string GetDomainInputOption(ParsedArgs args, string kind)
        {
            string direct = args.GetValue(kind + "-wz");
            if (!string.IsNullOrEmpty(direct))
            {
                return direct;
            }
            if (string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase))
            {
                return args.GetValue("character-wz") ?? args.GetValue("item-wz");
            }
            return null;
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
            string input = ResolveSkillFullInput(args);
            string id = args.GetValue("id");
            if (string.IsNullOrEmpty(id))
            {
                throw new UsageException("skill full requires --id <id>.");
            }

            string format = args.GetValue("format") ?? (args.HasFlag("json") ? "json" : "text");
            string output = args.GetValue("out") ?? args.GetValue("output");
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
            using (var repository = CliWzRepository.ForSkill(input, args))
            {
                var stringResult = repository.FindStringInfo("skill", id);
                if (stringResult != null)
                {
                    stringInfo = stringResult.StringInfo;
                }

                var dataResult = repository.FindDataNode("skill", id);
                if (dataResult == null)
                {
                    if (args.HasFlag("allow-string-only") && stringInfo != null)
                    {
                        var stringOnly = SkillFullDto.FromStringOnly(id, stringInfo, stringResult.InputPath, repository.DataInputPaths, repository.StringInputPaths);
                        WriteSkillFullOutput(stringOnly, format, output);
                        return ExitSuccess;
                    }

                    throw new UsageException("skill id not found: " + id);
                }

                var dto = SkillFullDto.FromNode(
                    id,
                    dataResult.Node,
                    stringInfo,
                    level,
                    dataResult.InputPath,
                    stringResult == null ? null : stringResult.InputPath,
                    repository.DataInputPaths,
                    repository.StringInputPaths);
                WriteSkillFullOutput(dto, format, output);
            }

            return ExitSuccess;
        }

        private static string ResolveSkillFullInput(ParsedArgs args)
        {
            if (args.Positionals.Count > 1 && !IsHelp(args.Positionals[1]))
            {
                return args.Positionals[1];
            }

            string skillWz = args.GetValue("skill-wz");
            if (!string.IsNullOrEmpty(skillWz))
            {
                return skillWz;
            }

            string dataDir = args.GetValue("data-dir");
            if (!string.IsNullOrEmpty(dataDir))
            {
                return Path.Combine(dataDir, "Skill");
            }

            throw new UsageException("skill full requires <skill-wz-file-or-dir>, --skill-wz <path>, or --data-dir <dir>.");
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

    }
}
