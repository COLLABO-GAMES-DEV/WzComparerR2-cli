using System;
using System.IO;
using System.Linq;

namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
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
