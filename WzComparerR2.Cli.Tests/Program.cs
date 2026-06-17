using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace WzComparerR2.Cli.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var options = TestOptions.Parse(args);
            var runner = new CliRunner(options.CliPath);
            var tests = new List<TestCase>
            {
                TestCase.Create("help lists core commands", () => HelpListsCoreCommands(runner)),
                TestCase.Create("version prints cli version", () => VersionPrintsCliVersion(runner)),
                TestCase.Create("unknown command returns usage error", () => UnknownCommandReturnsUsageError(runner)),
                TestCase.Create("missing input returns not found", () => MissingInputReturnsNotFound(runner)),
                TestCase.Create("invalid regex returns usage error before WZ load", () => InvalidRegexReturnsUsageError(runner)),
                TestCase.Create("avatar inspect emits stable json", () => AvatarInspectEmitsStableJson(runner)),
                TestCase.Create("avatar render dry-run emits blocked plan", () => AvatarRenderDryRunEmitsBlockedPlan(runner)),
                TestCase.Create("map render dry-run emits blocked plan", () => MapRenderDryRunEmitsBlockedPlan(runner)),
                TestCase.Create("config stores and removes values", () => ConfigStoresAndRemovesValues(runner)),
                TestCase.Create("config default-wz fallback is used", () => ConfigDefaultWzFallbackIsUsed(runner)),
                TestCase.Create("lua dry-run validates example script", () => LuaDryRunValidatesExampleScript(runner)),
                TestCase.Create("lua eval dry-run emits code contract", () => LuaEvalDryRunEmitsCodeContract(runner)),
                TestCase.Create("network server-info emits dry-run json", () => NetworkServerInfoEmitsDryRunJson(runner)),
                TestCase.Create("update invalid asset returns usage error", () => UpdateInvalidAssetReturnsUsageError(runner)),
                TestCase.Create("plugin list reports corrupt assembly without failing", () => PluginListReportsCorruptAssembly(runner)),
                TestCase.Create("plugin command provider can be discovered and run", () => PluginProviderCanBeDiscoveredAndRun(runner)),
            };

            int failed = 0;
            foreach (var test in tests)
            {
                try
                {
                    test.Run();
                    Console.WriteLine("[PASS] " + test.Name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine("[FAIL] " + test.Name);
                    Console.Error.WriteLine(ex.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine("Total: " + tests.Count + " Passed: " + (tests.Count - failed) + " Failed: " + failed);
            return failed == 0 ? 0 : 1;
        }

        private static void HelpListsCoreCommands(CliRunner runner)
        {
            CommandResult result = runner.Run("--help");
            AssertExitCode(result, 0);
            AssertContains(result.Stdout, "wcr2 info <file-or-dir>");
            AssertContains(result.Stdout, "wcr2 compare <old-file-or-dir> <new-file-or-dir>");
            AssertContains(result.Stdout, "wcr2 plugin list|commands");
        }

        private static void VersionPrintsCliVersion(CliRunner runner)
        {
            CommandResult result = runner.Run("version");
            AssertExitCode(result, 0);
            AssertContains(result.Stdout, "wcr2 cli ");
        }

        private static void UnknownCommandReturnsUsageError(CliRunner runner)
        {
            CommandResult result = runner.Run("no-such-command");
            AssertExitCode(result, 1);
            AssertContains(result.Stderr, "Unknown command");
        }

        private static void MissingInputReturnsNotFound(CliRunner runner)
        {
            string missing = Path.Combine(Path.GetTempPath(), "wcr2-tests-missing-" + Guid.NewGuid().ToString("N") + ".wz");
            CommandResult result = runner.Run("info", missing);
            AssertExitCode(result, 2);
            AssertContains(result.Stderr, "Input path not found");
        }

        private static void InvalidRegexReturnsUsageError(CliRunner runner)
        {
            CommandResult result = runner.Run("search", "/no/such.wz", "--match-path", "[", "--regex");
            AssertExitCode(result, 1);
            AssertContains(result.Stderr, "Invalid --match-path pattern");
        }

        private static void AvatarInspectEmitsStableJson(CliRunner runner)
        {
            CommandResult result = runner.Run("avatar", "inspect", "--code", "1002140,1040036,1060026", "--json");
            AssertExitCode(result, 0);
            using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
            {
                JsonElement root = doc.RootElement;
                AssertEqual(3, root.GetProperty("Items").GetArrayLength(), "avatar item count");
                AssertEqual(true, root.GetProperty("IsValid").GetBoolean(), "avatar validity");
            }
        }

        private static void AvatarRenderDryRunEmitsBlockedPlan(CliRunner runner)
        {
            CommandResult result = runner.Run("avatar", "render", "--items", "00002000", "00012000", "1002140", "--out", "avatar.png", "--dry-run");
            AssertExitCode(result, 0);
            using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
            {
                JsonElement root = doc.RootElement;
                AssertEqual("dry-run", root.GetProperty("Mode").GetString(), "avatar render mode");
                AssertEqual("avatar.png", root.GetProperty("OutputPath").GetString(), "avatar render output");
                AssertEqual(false, root.GetProperty("CanRender").GetBoolean(), "avatar render capability");
                AssertEqual(3, root.GetProperty("Items").GetArrayLength(), "avatar render item count");
                AssertEqual(3, root.GetProperty("Candidates").GetArrayLength(), "avatar render candidate count");
                AssertContains(root.GetProperty("Candidates")[0].GetProperty("CandidatePaths")[0].GetString(), "Character/00002000.img");
                AssertContains(root.GetProperty("Blockers")[0].GetString(), "PluginManager.FindWz");
            }
        }

        private static void MapRenderDryRunEmitsBlockedPlan(CliRunner runner)
        {
            CommandResult result = runner.Run("map", "render", "--id", "100000000", "--out", "map.png", "--dry-run", "--include-life", "--layer", "all");
            AssertExitCode(result, 0);
            using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
            {
                JsonElement root = doc.RootElement;
                AssertEqual("dry-run", root.GetProperty("Mode").GetString(), "map render mode");
                AssertEqual("100000000", root.GetProperty("Id").GetString(), "map render id");
                AssertEqual("map.png", root.GetProperty("OutputPath").GetString(), "map render output");
                AssertEqual(false, root.GetProperty("CanRender").GetBoolean(), "map render capability");
                AssertEqual(true, root.GetProperty("IncludeLife").GetBoolean(), "map render life option");
                AssertContains(root.GetProperty("CandidatePaths")[0].GetString(), "Map/Map/Map1/100000000.img");
                AssertContains(root.GetProperty("CandidatePaths")[1].GetString(), "Data/Map/Map/Map1/Map1_000.wz");
                AssertContains(root.GetProperty("Blockers")[0].GetString(), "MonoGame");
            }
        }

        private static void ConfigStoresAndRemovesValues(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string config = Path.Combine(temp.Path, "wcr2.config.json");
                AssertExitCode(runner.Run("config", "set", "default-wz", "/tmp/Base.wz", "--config", config, "--json"), 0);

                CommandResult get = runner.Run("config", "get", "default-wz", "--config", config, "--json");
                AssertExitCode(get, 0);
                using (JsonDocument doc = JsonDocument.Parse(get.Stdout))
                {
                    AssertEqual("/tmp/Base.wz", doc.RootElement.GetProperty("Value").GetString(), "config value");
                }

                AssertExitCode(runner.Run("config", "unset", "default-wz", "--config", config, "--json"), 0);
                AssertExitCode(runner.Run("config", "get", "default-wz", "--config", config, "--json"), 2);
            }
        }

        private static void ConfigDefaultWzFallbackIsUsed(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string config = Path.Combine(temp.Path, "wcr2.config.json");
                string missing = Path.Combine(temp.Path, "configured.wz");
                AssertExitCode(runner.Run("config", "set", "default-wz", missing, "--config", config, "--json"), 0);
                CommandResult result = runner.Run("info", "--config", config);
                AssertExitCode(result, 2);
                AssertContains(result.Stderr, missing);
            }
        }

        private static void LuaDryRunValidatesExampleScript(CliRunner runner)
        {
            string script = Path.Combine("WzComparerR2.LuaConsole", "Examples", "DumpXml.lua");
            CommandResult result = runner.Run("lua", "run", script, "--dry-run", "--json");
            AssertExitCode(result, 0);
            using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
            {
                AssertEqual("dry-run", doc.RootElement.GetProperty("Mode").GetString(), "lua mode");
                AssertEqual(0, doc.RootElement.GetProperty("ExitCode").GetInt32(), "lua exit code");
            }
        }

        private static void LuaEvalDryRunEmitsCodeContract(CliRunner runner)
        {
            CommandResult result = runner.Run("lua", "eval", "--code", "print('ok')", "--dry-run", "--json");
            AssertExitCode(result, 0);
            using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
            {
                JsonElement root = doc.RootElement;
                AssertEqual(true, root.GetProperty("IsEval").GetBoolean(), "lua eval marker");
                AssertEqual("print('ok')", root.GetProperty("Code").GetString(), "lua eval code");
                AssertEqual("dry-run", root.GetProperty("Mode").GetString(), "lua eval mode");
                AssertEqual(0, root.GetProperty("ExitCode").GetInt32(), "lua eval exit code");
            }
        }

        private static void NetworkServerInfoEmitsDryRunJson(CliRunner runner)
        {
            CommandResult result = runner.Run("network", "server-info", "--json");
            AssertExitCode(result, 0);
            using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
            {
                AssertEqual("server-info", doc.RootElement.GetProperty("Command").GetString(), "network command");
                AssertEqual("dry-run", doc.RootElement.GetProperty("Mode").GetString(), "network mode");
            }
        }

        private static void UpdateInvalidAssetReturnsUsageError(CliRunner runner)
        {
            CommandResult result = runner.Run("update", "download", "--asset", "bad", "--out", "/tmp/wcr2-update-test", "--json", "--timeout", "1");
            AssertExitCode(result, 1);
            AssertContains(result.Stderr, "--asset must be");
        }

        private static void PluginListReportsCorruptAssembly(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string pluginDir = Path.Combine(temp.Path, "plugins");
                Directory.CreateDirectory(pluginDir);
                File.WriteAllText(Path.Combine(pluginDir, "Broken.dll"), "not a dll");

                CommandResult result = runner.Run("plugin", "list", "--plugin-dir", pluginDir, "--json");
                AssertExitCode(result, 0);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    AssertEqual(1, doc.RootElement.GetProperty("LoadFailedCount").GetInt32(), "plugin load failures");
                    AssertEqual("failed", doc.RootElement.GetProperty("Plugins")[0].GetProperty("Status").GetString(), "plugin status");
                }
            }
        }

        private static void PluginProviderCanBeDiscoveredAndRun(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string projectDir = Path.Combine(temp.Path, "HelloPlugin");
                Directory.CreateDirectory(projectDir);
                File.WriteAllText(Path.Combine(projectDir, "HelloPlugin.csproj"), CreateHelloPluginProject(runner.CliPath));
                File.WriteAllText(Path.Combine(projectDir, "HelloProvider.cs"), HelloProviderSource);

                CommandResult build = RunProcess("dotnet", new[] { "build", Path.Combine(projectDir, "HelloPlugin.csproj"), "-c", "Debug", "-v:minimal" }, Directory.GetCurrentDirectory());
                AssertExitCode(build, 0);

                string pluginDir = Path.Combine(temp.Path, "plugins");
                Directory.CreateDirectory(pluginDir);
                File.Copy(Path.Combine(projectDir, "bin", "Debug", "net8.0", "HelloPlugin.dll"), Path.Combine(pluginDir, "HelloPlugin.dll"));

                CommandResult commands = runner.Run("plugin", "commands", "--plugin-dir", pluginDir, "--json");
                AssertExitCode(commands, 0);
                using (JsonDocument doc = JsonDocument.Parse(commands.Stdout))
                {
                    AssertEqual("hello", doc.RootElement[0].GetProperty("Name").GetString(), "plugin command name");
                }

                CommandResult run = runner.Run("plugin", "run", "hello", "Codex", "--plugin-dir", pluginDir, "--json");
                AssertExitCode(run, 0);
                using (JsonDocument doc = JsonDocument.Parse(run.Stdout))
                {
                    AssertEqual(true, doc.RootElement.GetProperty("Success").GetBoolean(), "plugin run success");
                    AssertEqual("hello Codex\n", doc.RootElement.GetProperty("Stdout").GetString(), "plugin stdout");
                }
            }
        }

        private static string CreateHelloPluginProject(string cliPath)
        {
            return @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include=""wcr2"">
      <HintPath>" + EscapeXml(cliPath) + @"</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>";
        }

        private const string HelloProviderSource = @"using WzComparerR2.Cli;

public sealed class HelloProvider : ICliCommandProvider
{
    public IEnumerable<CliCommandDescriptor> GetCommands()
    {
        yield return new CliCommandDescriptor { Name = ""hello"", Summary = ""test hello command"", Usage = ""wcr2 plugin run hello [name]"" };
    }

    public int Execute(string commandName, IReadOnlyList<string> args, ICliCommandContext context)
    {
        context.Output.WriteLine(""hello "" + (args.Count > 0 ? args[0] : ""plugin""));
        return 0;
    }
}";

        private static CommandResult RunProcess(string fileName, IReadOnlyList<string> args, string workingDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }
            startInfo.Environment["DOTNET_ROLL_FORWARD"] = "Major";
            startInfo.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "1";

            using (var process = Process.Start(startInfo))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return new CommandResult(process.ExitCode, stdout, stderr);
            }
        }

        private static string EscapeXml(string value)
        {
            return value
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }

        private static void AssertExitCode(CommandResult result, int expected)
        {
            if (result.ExitCode != expected)
            {
                throw new Exception("Expected exit code " + expected + " but got " + result.ExitCode + Environment.NewLine + result);
            }
        }

        private static void AssertContains(string value, string expected)
        {
            if (value == null || !value.Contains(expected, StringComparison.Ordinal))
            {
                throw new Exception("Expected text to contain '" + expected + "'." + Environment.NewLine + value);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new Exception("Expected " + name + " to be '" + expected + "' but got '" + actual + "'.");
            }
        }
    }

    internal sealed class TestOptions
    {
        public string CliPath { get; private set; }

        public static TestOptions Parse(IReadOnlyList<string> args)
        {
            string cliPath = Environment.GetEnvironmentVariable("WCR2_TEST_CLI");
            for (int i = 0; i < args.Count; i++)
            {
                if (string.Equals(args[i], "--cli", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                {
                    cliPath = args[++i];
                }
            }

            if (string.IsNullOrEmpty(cliPath))
            {
                cliPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "WzComparerR2.Cli", "bin", "Debug", "net8.0", "wcr2.dll"));
            }

            cliPath = Path.GetFullPath(cliPath);
            if (!File.Exists(cliPath))
            {
                throw new FileNotFoundException("CLI binary not found. Build WzComparerR2.Cli first or pass --cli <path>.", cliPath);
            }

            return new TestOptions { CliPath = cliPath };
        }
    }

    internal sealed class CliRunner
    {
        public CliRunner(string cliPath)
        {
            this.CliPath = Path.GetFullPath(cliPath);
        }

        public string CliPath { get; private set; }

        public CommandResult Run(params string[] args)
        {
            string extension = Path.GetExtension(this.CliPath);
            if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
            {
                return ProgramRun("dotnet", new[] { this.CliPath }.Concat(args).ToList());
            }

            return ProgramRun(this.CliPath, args);
        }

        private static CommandResult ProgramRun(string fileName, IReadOnlyList<string> args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }
            startInfo.Environment["DOTNET_ROLL_FORWARD"] = "Major";
            startInfo.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "1";

            using (var process = Process.Start(startInfo))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return new CommandResult(process.ExitCode, stdout, stderr);
            }
        }
    }

    internal sealed class TestCase
    {
        private TestCase(string name, Action run)
        {
            this.Name = name;
            this.Run = run;
        }

        public string Name { get; private set; }
        public Action Run { get; private set; }

        public static TestCase Create(string name, Action run)
        {
            return new TestCase(name, run);
        }
    }

    internal sealed class CommandResult
    {
        public CommandResult(int exitCode, string stdout, string stderr)
        {
            this.ExitCode = exitCode;
            this.Stdout = stdout;
            this.Stderr = stderr;
        }

        public int ExitCode { get; private set; }
        public string Stdout { get; private set; }
        public string Stderr { get; private set; }

        public override string ToString()
        {
            return "ExitCode: " + this.ExitCode + Environment.NewLine
                + "Stdout:" + Environment.NewLine + this.Stdout + Environment.NewLine
                + "Stderr:" + Environment.NewLine + this.Stderr;
        }
    }

    internal sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            this.Path = path;
        }

        public string Path { get; private set; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wcr2-cli-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(this.Path))
                {
                    Directory.Delete(this.Path, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
