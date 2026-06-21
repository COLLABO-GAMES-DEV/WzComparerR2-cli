using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace WzComparerR2.Cli
{
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
}
