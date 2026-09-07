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
        private static readonly JsonSerializerOptions ServeJsonOptions = CreateServeJsonOptions();

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
                    return RunServe(parsed);
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

        private static int RunServe(SimpleArgs args)
        {
            if (args.HasFlag("help"))
            {
                PrintServeHelp();
                return ExitSuccess;
            }
            if (!args.HasFlag("stdio"))
            {
                Console.Error.WriteLine("wcr2-agent serve currently requires --stdio.");
                return ExitUsage;
            }

            var runner = new AgentJobRunner();
            string line;
            while ((line = Console.In.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                AgentServeResponse response = HandleServeRequest(runner, line);
                Console.WriteLine(JsonSerializer.Serialize(response, ServeJsonOptions));
                Console.Out.Flush();
                if (string.Equals(response.Message, "shutdown", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            return ExitSuccess;
        }

        private static AgentServeResponse HandleServeRequest(AgentJobRunner runner, string line)
        {
            AgentServeRequest request;
            try
            {
                request = JsonSerializer.Deserialize<AgentServeRequest>(line, AgentJobRunner.JsonOptions);
            }
            catch (JsonException ex)
            {
                return new AgentServeResponse
                {
                    Status = "failed",
                    Error = "invalid-json",
                    Message = ex.Message
                };
            }

            if (request == null)
            {
                return new AgentServeResponse
                {
                    Status = "failed",
                    Error = "invalid-request",
                    Message = "Request must be a JSON object."
                };
            }

            string requestId = FirstNonEmpty(request.RequestId, request.Id);
            string method = FirstNonEmpty(request.Method, request.Command);
            if (string.IsNullOrWhiteSpace(method) && (request.Job != null || !string.IsNullOrWhiteSpace(request.JobPath)))
            {
                method = "run";
            }

            if (string.Equals(method, "ping", StringComparison.OrdinalIgnoreCase))
            {
                return new AgentServeResponse
                {
                    Id = requestId,
                    Status = "ok",
                    Message = "pong"
                };
            }

            if (string.Equals(method, "shutdown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(method, "exit", StringComparison.OrdinalIgnoreCase))
            {
                return new AgentServeResponse
                {
                    Id = requestId,
                    Status = "ok",
                    Message = "shutdown"
                };
            }

            if (string.Equals(method, "run", StringComparison.OrdinalIgnoreCase))
            {
                AgentRunResult result = runner.Run(new AgentRunRequest
                {
                    JobPath = request.JobPath,
                    OutputDirectoryOverride = FirstNonEmpty(request.OutputDir, request.OutputDirectory, request.Out),
                    BaseDirectory = request.BaseDirectory,
                    Job = request.Job
                });
                return new AgentServeResponse
                {
                    Id = requestId,
                    Status = result.IsSuccess ? "ok" : "failed",
                    Error = result.Error,
                    Message = result.Message,
                    Result = result
                };
            }

            return new AgentServeResponse
            {
                Id = requestId,
                Status = "failed",
                Error = "unknown-method",
                Message = "Unknown serve method: " + method
            };
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
            Console.WriteLine("Supported steps: noop, image.search, image.export-related, skill.export, skill.export-batch, skill.export-xlsx, item.icon, item.export, map.export.");
        }

        private static void PrintRunHelp()
        {
            Console.WriteLine("Usage: wcr2-agent run --job <job.json> [--out <dir>] [--json]");
            Console.WriteLine();
            Console.WriteLine("Supported steps: noop, image.search, image.export-related, skill.export, skill.export-batch, skill.export-xlsx, item.icon, item.export, map.export.");
        }

        private static void PrintServeHelp()
        {
            Console.WriteLine("Usage: wcr2-agent serve --stdio");
            Console.WriteLine();
            Console.WriteLine("Protocol: newline-delimited JSON request/response over stdin/stdout.");
            Console.WriteLine("Methods: ping, run, shutdown.");
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            return null;
        }

        private static JsonSerializerOptions CreateServeJsonOptions()
        {
            var options = new JsonSerializerOptions(AgentJobRunner.JsonOptions);
            options.WriteIndented = false;
            return options;
        }
    }

    internal sealed class AgentServeRequest
    {
        public string Id { get; set; }
        public string RequestId { get; set; }
        public string Method { get; set; }
        public string Command { get; set; }
        public string JobPath { get; set; }
        public string OutputDir { get; set; }
        public string OutputDirectory { get; set; }
        public string Out { get; set; }
        public string BaseDirectory { get; set; }
        public AgentJob Job { get; set; }
    }

    internal sealed class AgentServeResponse
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
        public string Message { get; set; }
        public AgentRunResult Result { get; set; }
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
