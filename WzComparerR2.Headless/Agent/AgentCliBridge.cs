using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace WzComparerR2.Headless.Agent
{
    internal sealed class AgentCliCommandResult
    {
        public string CliPath { get; set; }
        public List<string> Command { get; set; }
        public int ExitCode { get; set; }
        public string Stdout { get; set; }
        public string Stderr { get; set; }
    }

    internal static class AgentCliBridge
    {
        public static string ResolveCliPath(string requestedPath, string baseDirectory)
        {
            foreach (string candidate in EnumerateCliPathCandidates(requestedPath, baseDirectory))
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return null;
        }

        public static AgentCliCommandResult Run(string cliPath, IReadOnlyList<string> args, string workingDirectory, int timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(cliPath))
            {
                throw new ArgumentException("CLI path is required.", nameof(cliPath));
            }

            if (!File.Exists(cliPath))
            {
                throw new FileNotFoundException("CLI binary not found: " + cliPath, cliPath);
            }

            string executable = cliPath;
            var processArgs = new List<string>();
            string extension = Path.GetExtension(cliPath);
            if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
            {
                executable = "dotnet";
                processArgs.Add(cliPath);
            }
            processArgs.AddRange(args);

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string arg in processArgs)
            {
                startInfo.ArgumentList.Add(arg);
            }
            if (string.IsNullOrEmpty(startInfo.Environment["DOTNET_ROLL_FORWARD"]))
            {
                startInfo.Environment["DOTNET_ROLL_FORWARD"] = "Major";
            }
            startInfo.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "1";

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("Failed to start CLI process.");
                }

                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                int timeoutMs = Math.Max(1, timeoutSeconds) * 1000;
                if (!process.WaitForExit(timeoutMs))
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                    }

                    return new AgentCliCommandResult
                    {
                        CliPath = Path.GetFullPath(cliPath),
                        Command = new[] { executable }.Concat(processArgs).ToList(),
                        ExitCode = -1,
                        Stdout = stdoutTask.IsCompleted ? stdoutTask.GetAwaiter().GetResult() : string.Empty,
                        Stderr = "CLI process timed out after " + timeoutSeconds + " seconds."
                    };
                }

                string stdout = stdoutTask.GetAwaiter().GetResult();
                string stderr = stderrTask.GetAwaiter().GetResult();
                return new AgentCliCommandResult
                {
                    CliPath = Path.GetFullPath(cliPath),
                    Command = new[] { executable }.Concat(processArgs).ToList(),
                    ExitCode = process.ExitCode,
                    Stdout = stdout,
                    Stderr = stderr
                };
            }
        }

        private static IEnumerable<string> EnumerateCliPathCandidates(string requestedPath, string baseDirectory)
        {
            if (!string.IsNullOrWhiteSpace(requestedPath))
            {
                yield return ResolvePath(baseDirectory, requestedPath);
                yield break;
            }

            string fromEnvironment = Environment.GetEnvironmentVariable("WCR2_CLI_PATH");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                yield return ResolvePath(baseDirectory, fromEnvironment);
            }

            string appDirectory = AppContext.BaseDirectory;
            foreach (string fileName in new[] { "wcr2", "wcr2.exe", "wcr2.dll" })
            {
                yield return Path.Combine(appDirectory, fileName);
            }

            string configuration = TryGetConfigurationName(appDirectory);
            string repoRoot = TryGetRepoRoot(appDirectory);
            if (!string.IsNullOrWhiteSpace(configuration) && !string.IsNullOrWhiteSpace(repoRoot))
            {
                yield return Path.Combine(repoRoot, "WzComparerR2.Cli", "bin", configuration, "net8.0", "wcr2.dll");
            }

            yield return Path.Combine(Directory.GetCurrentDirectory(), "WzComparerR2.Cli", "bin", "Release", "net8.0", "wcr2.dll");
            yield return Path.Combine(Directory.GetCurrentDirectory(), "WzComparerR2.Cli", "bin", "Debug", "net8.0", "wcr2.dll");
        }

        private static string TryGetConfigurationName(string appDirectory)
        {
            try
            {
                DirectoryInfo frameworkDirectory = new DirectoryInfo(appDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                DirectoryInfo configurationDirectory = frameworkDirectory.Parent;
                return configurationDirectory == null ? null : configurationDirectory.Name;
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetRepoRoot(string appDirectory)
        {
            try
            {
                DirectoryInfo current = new DirectoryInfo(appDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                while (current != null)
                {
                    if (File.Exists(Path.Combine(current.FullName, "WzComparerR2.sln"))
                        && Directory.Exists(Path.Combine(current.FullName, "WzComparerR2.Cli")))
                    {
                        return current.FullName;
                    }
                    current = current.Parent;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string ResolvePath(string baseDirectory, string path)
        {
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            return Path.GetFullPath(Path.Combine(baseDirectory ?? Directory.GetCurrentDirectory(), path));
        }
    }
}
