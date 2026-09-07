using System;
using System.Collections.Generic;
using System.Text.Json;
using WzComparerR2.Headless.Agent;

namespace WzComparerR2.AgentHost
{
    internal static class Program
    {
        private const int ExitSuccess = 0;
        private const int ExitUsage = 1;
        private const string AgentVersion = "0.1.0";

        private static int Main(string[] args)
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintHelp();
                return ExitSuccess;
            }

            string command = args[0].ToLowerInvariant();
            var parsed = SimpleArgs.Parse(args, 1);
            switch (command)
            {
                case "run":
                    return RunJob(parsed);
                case "serve":
                    Console.Error.WriteLine("wcr2-agent serve is not implemented yet.");
                    return ExitUsage;
                case "version":
                case "--version":
                    Console.WriteLine("wcr2-agent " + AgentVersion);
                    return ExitSuccess;
                default:
                    Console.Error.WriteLine("Unknown command: " + command);
                    Console.Error.WriteLine();
                    PrintHelp();
                    return ExitUsage;
            }
        }

        private static int RunJob(SimpleArgs args)
        {
            if (args.HasFlag("help"))
            {
                PrintRunHelp();
                return ExitSuccess;
            }

            var runner = new AgentJobRunner();
            AgentRunResult result = runner.Run(new AgentRunRequest
            {
                JobPath = args.GetValue("job"),
                OutputDirectoryOverride = args.GetValue("out")
            });

            if (args.HasFlag("json"))
            {
                Console.WriteLine(JsonSerializer.Serialize(result, AgentJobRunner.JsonOptions));
            }
            else if (result.IsSuccess)
            {
                Console.WriteLine("status: " + result.Status);
                Console.WriteLine("job: " + result.JobPath);
                if (!string.IsNullOrWhiteSpace(result.OutputDir))
                {
                    Console.WriteLine("output: " + result.OutputDir);
                }
                Console.WriteLine("steps: " + result.Steps.Count);
            }
            else
            {
                Console.Error.WriteLine(result.Message);
            }

            return result.IsSuccess ? ExitSuccess : ExitUsage;
        }

        private static bool IsHelp(string value)
        {
            return string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "help", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("wcr2-agent " + AgentVersion);
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2-agent run --job <job.json> [--out <dir>] [--json]");
            Console.WriteLine("  wcr2-agent serve --stdio");
            Console.WriteLine("  wcr2-agent version");
            Console.WriteLine();
            Console.WriteLine("Supported steps: noop, image.search, image.export-related, skill.export, skill.export-batch, skill.export-xlsx.");
        }

        private static void PrintRunHelp()
        {
            Console.WriteLine("Usage: wcr2-agent run --job <job.json> [--out <dir>] [--json]");
            Console.WriteLine();
            Console.WriteLine("Supported steps: noop, image.search, image.export-related, skill.export, skill.export-batch, skill.export-xlsx.");
        }
    }

    internal sealed class SimpleArgs
    {
        private readonly Dictionary<string, string> values;
        private readonly HashSet<string> flags;

        private SimpleArgs(Dictionary<string, string> values, HashSet<string> flags)
        {
            this.values = values;
            this.flags = flags;
        }

        public static SimpleArgs Parse(IReadOnlyList<string> args, int startIndex)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = startIndex; i < args.Count; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                string key = arg.Substring(2);
                if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    values[key] = args[++i];
                }
                else
                {
                    flags.Add(key);
                }
            }

            return new SimpleArgs(values, flags);
        }

        public bool HasFlag(string name)
        {
            return this.flags.Contains(name);
        }

        public string GetValue(string name)
        {
            string value;
            return this.values.TryGetValue(name, out value) ? value : null;
        }
    }
}
