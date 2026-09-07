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
            var agentRunner = new CliRunner(options.AgentPath);
            var tests = new List<TestCase>
            {
                TestCase.Create("help lists core commands", () => HelpListsCoreCommands(runner)),
                TestCase.Create("version prints cli version", () => VersionPrintsCliVersion(runner)),
                TestCase.Create("quiet suppresses success stdout", () => QuietSuppressesSuccessStdout(runner)),
                TestCase.Create("verbose adds exception details", () => VerboseAddsExceptionDetails(runner)),
                TestCase.Create("extended domain help lists info commands", () => ExtendedDomainHelpListsInfoCommands(runner)),
                TestCase.Create("item help lists icon export command", () => ItemHelpListsIconExportCommand(runner)),
                TestCase.Create("item icon validates required inputs", () => ItemIconValidatesRequiredInputs(runner)),
                TestCase.Create("skill help lists repository inputs", () => SkillHelpListsRepositoryInputs(runner)),
                TestCase.Create("skill sprite validates required inputs", () => SkillSpriteValidatesRequiredInputs(runner)),
                TestCase.Create("optional real data skill sprite merges pack metadata", () => OptionalRealDataSkillSpriteMergesPackMetadata(runner)),
                TestCase.Create("optional real data map detail prefers populated shard", () => OptionalRealDataMapDetailPrefersPopulatedShard(runner)),
                TestCase.Create("media help lists dedicated commands", () => MediaHelpListsDedicatedCommands(runner)),
                TestCase.Create("media commands validate required inputs", () => MediaCommandsValidateRequiredInputs(runner)),
                TestCase.Create("unknown command returns usage error", () => UnknownCommandReturnsUsageError(runner)),
                TestCase.Create("missing input returns not found", () => MissingInputReturnsNotFound(runner)),
                TestCase.Create("unsupported wz package emits json diagnostic", () => UnsupportedWzPackageEmitsJsonDiagnostic(runner)),
                TestCase.Create("kmst1202 random header emits json diagnostic", () => Kmst1202RandomHeaderEmitsJsonDiagnostic(runner)),
                TestCase.Create("invalid regex returns usage error before WZ load", () => InvalidRegexReturnsUsageError(runner)),
                TestCase.Create("avatar inspect emits stable json", () => AvatarInspectEmitsStableJson(runner)),
                TestCase.Create("avatar render dry-run emits blocked plan", () => AvatarRenderDryRunEmitsBlockedPlan(runner)),
                TestCase.Create("map render dry-run emits blocked plan", () => MapRenderDryRunEmitsBlockedPlan(runner)),
                TestCase.Create("config stores and removes values", () => ConfigStoresAndRemovesValues(runner)),
                TestCase.Create("config default-wz fallback is used", () => ConfigDefaultWzFallbackIsUsed(runner)),
                TestCase.Create("config profile overrides default fallback", () => ConfigProfileOverridesDefaultFallback(runner)),
                TestCase.Create("lua dry-run validates example script", () => LuaDryRunValidatesExampleScript(runner)),
                TestCase.Create("lua eval dry-run emits code contract", () => LuaEvalDryRunEmitsCodeContract(runner)),
                TestCase.Create("network server-info emits dry-run json", () => NetworkServerInfoEmitsDryRunJson(runner)),
                TestCase.Create("network interactive chat is explicitly rejected", () => NetworkInteractiveChatIsExplicitlyRejected(runner)),
                TestCase.Create("update invalid asset returns usage error", () => UpdateInvalidAssetReturnsUsageError(runner)),
                TestCase.Create("plugin list reports corrupt assembly without failing", () => PluginListReportsCorruptAssembly(runner)),
                TestCase.Create("plugin command provider can be discovered and run", () => PluginProviderCanBeDiscoveredAndRun(runner)),
                TestCase.Create("agent help lists run command", () => AgentHelpListsRunCommand(agentRunner)),
                TestCase.Create("agent run empty job emits manifest", () => AgentRunEmptyJobEmitsManifest(agentRunner)),
                TestCase.Create("agent run noop step succeeds", () => AgentRunNoopStepSucceeds(agentRunner)),
                TestCase.Create("agent run unknown step emits json failure", () => AgentRunUnknownStepEmitsJsonFailure(agentRunner)),
                TestCase.Create("agent run invalid json emits json failure", () => AgentRunInvalidJsonEmitsJsonFailure(agentRunner)),
                TestCase.Create("agent serve stdio handles ping run shutdown", () => AgentServeStdioHandlesPingRunShutdown(agentRunner)),
                TestCase.Create("agent image search validates required data", () => AgentImageSearchValidatesRequiredData(agentRunner)),
                TestCase.Create("agent image related export validates from step", () => AgentImageRelatedExportValidatesFromStep(agentRunner)),
                TestCase.Create("agent skill export validates required fields", () => AgentSkillExportValidatesRequiredFields(agentRunner)),
                TestCase.Create("agent skill batch validates requests", () => AgentSkillBatchValidatesRequests(agentRunner)),
                TestCase.Create("agent skill xlsx validates workbook", () => AgentSkillXlsxValidatesWorkbook(agentRunner)),
                TestCase.Create("agent item icon validates selector", () => AgentItemIconValidatesSelector(agentRunner)),
                TestCase.Create("agent item export validates selector", () => AgentItemExportValidatesSelector(agentRunner)),
                TestCase.Create("agent map export validates id", () => AgentMapExportValidatesId(agentRunner)),
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
            AssertContains(result.Stdout, "wcr2 sound list <file-or-dir>");
            AssertContains(result.Stdout, "wcr2 image export <file-or-dir>");
            AssertContains(result.Stdout, "wcr2 video export <file-or-dir>");
            AssertContains(result.Stdout, "wcr2 plugin list|commands");
        }

        private static void VersionPrintsCliVersion(CliRunner runner)
        {
            CommandResult result = runner.Run("version");
            AssertExitCode(result, 0);
            AssertContains(result.Stdout, "wcr2 cli ");
        }

        private static void QuietSuppressesSuccessStdout(CliRunner runner)
        {
            CommandResult afterCommand = runner.Run("version", "--quiet");
            AssertExitCode(afterCommand, 0);
            AssertEqual(string.Empty, afterCommand.Stdout, "quiet stdout after command");

            CommandResult beforeCommand = runner.Run("--quiet", "version");
            AssertExitCode(beforeCommand, 0);
            AssertEqual(string.Empty, beforeCommand.Stdout, "quiet stdout before command");
        }

        private static void VerboseAddsExceptionDetails(CliRunner runner)
        {
            CommandResult result = runner.Run("search", "/no/such.wz", "--match-path", "[", "--regex", "--verbose");
            AssertExitCode(result, 1);
            AssertContains(result.Stderr, "Invalid --match-path pattern");
            AssertContains(result.Stderr, "Exception: WzComparerR2.Headless.UsageException");
        }

        private static void ExtendedDomainHelpListsInfoCommands(CliRunner runner)
        {
            foreach (string command in new[] { "gear", "mob", "npc", "quest" })
            {
                CommandResult result = runner.Run(command, "--help");
                AssertExitCode(result, 0);
                AssertContains(result.Stdout, "wcr2 " + command + " info [<wz-file-or-dir>] --id <id>");
                AssertContains(result.Stdout, "--data-dir <dir>");
            }

            CommandResult gear = runner.Run("gear", "--help");
            AssertExitCode(gear, 0);
            AssertContains(gear.Stdout, "--character-wz <file-or-dir>");
        }

        private static void ItemHelpListsIconExportCommand(CliRunner runner)
        {
            CommandResult result = runner.Run("item", "--help");
            AssertExitCode(result, 0);
            AssertContains(result.Stdout, "wcr2 item icon");
            AssertContains(result.Stdout, "--canvas-wz <file-or-dir>");
            AssertContains(result.Stdout, "--category cash|consume|install|etc|pet");
        }

        private static void ItemIconValidatesRequiredInputs(CliRunner runner)
        {
            CommandResult missingIdOrName = runner.Run("item", "icon", "--out", "icons");
            AssertExitCode(missingIdOrName, 1);
            AssertContains(missingIdOrName.Stderr, "item icon requires --id <id> or --name <exact-name>.");

            CommandResult missingOut = runner.Run("item", "icon", "--id", "5062000");
            AssertExitCode(missingOut, 1);
            AssertContains(missingOut.Stderr, "item icon requires --out <output-dir>.");
        }

        private static void SkillHelpListsRepositoryInputs(CliRunner runner)
        {
            CommandResult result = runner.Run("skill", "--help");
            AssertExitCode(result, 0);
            AssertContains(result.Stdout, "--data-dir <dir>");
            AssertContains(result.Stdout, "--skill-wz <path>");
            AssertContains(result.Stdout, "wcr2 skill full [<skill-wz-file-or-dir>]");
            AssertContains(result.Stdout, "wcr2 skill search-name [<skill-wz-file-or-dir>]");
            AssertContains(result.Stdout, "wcr2 skill resolve-name [<skill-wz-file-or-dir>]");
            AssertContains(result.Stdout, "wcr2 skill sprite [<skill-wz-file-or-dir>]");
            AssertContains(result.Stdout, "wcr2 skill export [<skill-wz-file-or-dir>]");
            AssertContains(result.Stdout, "wcr2 skill export-batch [<skill-wz-file-or-dir>]");
            AssertContains(result.Stdout, "--name <text>");
            AssertContains(result.Stdout, "--job-code <code>");
            AssertContains(result.Stdout, "--canvas-wz <path>");
            AssertContains(result.Stdout, "--sound-wz <path>");
            AssertContains(result.Stdout, "Packs/Skill_*.ms fallback");
            AssertContains(result.Stdout, "--include-video");
            AssertContains(result.Stdout, "--video-format <fmt>");
            AssertContains(result.Stdout, "mcv|frames|png|gif|both");
            AssertContains(result.Stdout, "default PNG frames");
            AssertContains(result.Stdout, "--branch <list>");
            AssertContains(result.Stdout, "auto/all/visual detected branches");
            AssertContains(result.Stdout, "--include-related");
            AssertContains(result.Stdout, "--related-key <name>");
            AssertContains(result.Stdout, "skill-info.json");
            AssertContains(result.Stdout, "resources.json");
            AssertContains(result.Stdout, "UnresolvedPlaceholders");
            AssertContains(result.Stdout, "Origin, Z, Delay");
            AssertContains(result.Stdout, "--ids-file <path>");
            AssertContains(result.Stdout, "--names-file <path>");
            AssertContains(result.Stdout, "--out-root <dir>");
            AssertContains(result.Stdout, "--skip-existing");
        }

        private static void SkillSpriteValidatesRequiredInputs(CliRunner runner)
        {
            CommandResult missingId = runner.Run("skill", "sprite", "--out", "sprites");
            AssertExitCode(missingId, 1);
            AssertContains(missingId.Stderr, "skill sprite requires --id <id>.");

            CommandResult missingOut = runner.Run("skill", "sprite", "--id", "1121008");
            AssertExitCode(missingOut, 1);
            AssertContains(missingOut.Stderr, "skill sprite requires --out <output-dir>.");

            CommandResult missingInput = runner.Run("skill", "sprite", "--id", "1121008", "--out", "sprites");
            AssertExitCode(missingInput, 1);
            AssertContains(missingInput.Stderr, "skill full requires <skill-wz-file-or-dir>, --skill-wz <path>, or --data-dir <dir>.");

            CommandResult exportMissingOut = runner.Run("skill", "export", "--id", "1121008");
            AssertExitCode(exportMissingOut, 1);
            AssertContains(exportMissingOut.Stderr, "skill export requires --out <output-dir>.");

            CommandResult batchMissingInput = runner.Run("skill", "export-batch", "--ids", "1121008", "--out-root", "sprites");
            AssertExitCode(batchMissingInput, 1);
            AssertContains(batchMissingInput.Stderr, "skill full requires <skill-wz-file-or-dir>, --skill-wz <path>, or --data-dir <dir>.");

            CommandResult batchMissingOut = runner.Run("skill", "export-batch", "--data-dir", "Data", "--ids", "1121008");
            AssertExitCode(batchMissingOut, 1);
            AssertContains(batchMissingOut.Stderr, "skill export-batch requires --out-root <output-dir>.");

            CommandResult batchMissingIds = runner.Run("skill", "export-batch", "--data-dir", "Data", "--out-root", "sprites");
            AssertExitCode(batchMissingIds, 1);
            AssertContains(batchMissingIds.Stderr, "skill export-batch requires --ids <id,id>, --ids-file <path>, --names <name,name>, or --names-file <path>.");

            CommandResult nameSearchMissingInput = runner.Run("skill", "search-name", "--name", "파이어크래커");
            AssertExitCode(nameSearchMissingInput, 1);
            AssertContains(nameSearchMissingInput.Stderr, "skill search-name requires <skill-wz-file-or-dir>, --skill-wz <path>, or --data-dir <dir>.");

            CommandResult nameSearchMissingName = runner.Run("skill", "search-name", "--data-dir", "Data");
            AssertExitCode(nameSearchMissingName, 1);
            AssertContains(nameSearchMissingName.Stderr, "skill name lookup requires --name <text>.");
        }

        private static void OptionalRealDataSkillSpriteMergesPackMetadata(CliRunner runner)
        {
            string dataDir = Environment.GetEnvironmentVariable("WCR2_TEST_DATA_DIR");
            if (string.IsNullOrWhiteSpace(dataDir) || !Directory.Exists(dataDir))
            {
                return;
            }

            using (var temp = TempDirectory.Create())
            {
                CommandResult result = runner.Run(
                    "skill",
                    "sprite",
                    "--data-dir",
                    dataDir,
                    "--id",
                    "3141000",
                    "--branch",
                    "prepare,keydown,keydownend",
                    "--out",
                    temp.Path,
                    "--json");
                AssertExitCode(result, 0);

                string skillInfoPath = Path.Combine(temp.Path, "skill-info.json");
                string resourcesPath = Path.Combine(temp.Path, "resources.json");
                using (JsonDocument info = JsonDocument.Parse(File.ReadAllText(skillInfoPath)))
                {
                    JsonElement root = info.RootElement;
                    AssertEqual("mixed", root.GetProperty("SourceProfile").GetString(), "3141000 source profile");
                    AssertContains(root.GetProperty("DataInputPath").GetString(), "Packs");
                    AssertEqual("314.img\\skill\\3141000", root.GetProperty("DataPath").GetString(), "3141000 data path");
                }

                using (JsonDocument resources = JsonDocument.Parse(File.ReadAllText(resourcesPath)))
                {
                    JsonElement keydown = FindResource(resources.RootElement, "keydown");
                    AssertEqual("exported-outlink", keydown.GetProperty("Status").GetString(), "keydown status");
                    AssertContains(keydown.GetProperty("OutlinkPath").GetString(), "Skill/_Canvas/314.img/skill/3141000/keydown");
                    AssertContains(keydown.GetProperty("ResolvedInputPath").GetString(), "Skill");
                    AssertEqual("6thStormArrowLoop", keydown.GetProperty("Metadata").GetProperty("action").GetProperty("Value").GetString(), "keydown action");

                    JsonElement firstFrame = keydown.GetProperty("Files")[0];
                    AssertEqual(316, firstFrame.GetProperty("Origin").GetProperty("X").GetInt32(), "keydown frame 0 origin x");
                    AssertEqual(214, firstFrame.GetProperty("Origin").GetProperty("Y").GetInt32(), "keydown frame 0 origin y");
                    AssertEqual(60, firstFrame.GetProperty("Delay").GetInt32(), "keydown frame 0 delay");
                    AssertContains(firstFrame.GetProperty("MetadataPath").GetString(), "3141000\\keydown\\0");
                }
            }
        }

        private static void OptionalRealDataMapDetailPrefersPopulatedShard(CliRunner runner)
        {
            string dataDir = Environment.GetEnvironmentVariable("WCR2_TEST_DATA_DIR");
            if (string.IsNullOrWhiteSpace(dataDir) || !Directory.Exists(dataDir))
            {
                return;
            }

            string map1Dir = Path.Combine(dataDir, "Map", "Map", "Map1");
            if (!Directory.Exists(map1Dir))
            {
                return;
            }

            CommandResult portals = runner.Run("map", "portals", map1Dir, "--id", "100000000", "--json");
            AssertExitCode(portals, 0);
            using (JsonDocument doc = JsonDocument.Parse(portals.Stdout))
            {
                JsonElement root = doc.RootElement;
                int portalCount = root.GetProperty("Portals").GetArrayLength();
                int selectedCount = root.GetProperty("SelectedItems").GetArrayLength();
                if (portalCount <= 0 || selectedCount <= 0)
                {
                    throw new Exception("Expected map portals to resolve populated shard data." + Environment.NewLine + portals.Stdout);
                }
            }

            CommandResult info = runner.Run("map", "info", "--data-dir", dataDir, "--id", "100000000", "--json");
            AssertExitCode(info, 0);
            using (JsonDocument doc = JsonDocument.Parse(info.Stdout))
            {
                int childrenCount = doc.RootElement.GetProperty("ChildrenCount").GetInt32();
                if (childrenCount <= 1)
                {
                    throw new Exception("Expected map info to prefer populated shard data." + Environment.NewLine + info.Stdout);
                }
            }
        }

        private static JsonElement FindResource(JsonElement root, string resource)
        {
            foreach (JsonElement item in root.GetProperty("Resources").EnumerateArray())
            {
                if (string.Equals(item.GetProperty("Resource").GetString(), resource, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
            throw new Exception("Resource not found: " + resource);
        }

        private static void MediaHelpListsDedicatedCommands(CliRunner runner)
        {
            CommandResult sound = runner.Run("sound", "--help");
            AssertExitCode(sound, 0);
            AssertContains(sound.Stdout, "wcr2 sound list <file-or-dir>");
            AssertContains(sound.Stdout, "wcr2 sound export-all <file-or-dir>");

            CommandResult image = runner.Run("image", "--help");
            AssertExitCode(image, 0);
            AssertContains(image.Stdout, "wcr2 image list <file-or-dir>");
            AssertContains(image.Stdout, "wcr2 image search [<file-or-dir>]");
            AssertContains(image.Stdout, "--data-dir <Data>");
            AssertContains(image.Stdout, "--cache-dir <dir>");
            AssertContains(image.Stdout, "--trust-cache");
            AssertContains(image.Stdout, "--no-refine");
            AssertContains(image.Stdout, "System.Drawing PNG path");

            CommandResult video = runner.Run("video", "--help");
            AssertExitCode(video, 0);
            AssertContains(video.Stdout, "wcr2 video export <file-or-dir>");
            AssertContains(video.Stdout, "mcv|frames|png|gif|both");
            AssertContains(video.Stdout, "default mcv");
        }

        private static void MediaCommandsValidateRequiredInputs(CliRunner runner)
        {
            CommandResult list = runner.Run("sound", "list");
            AssertExitCode(list, 1);
            AssertContains(list.Stderr, "Usage: wcr2 sound list <file-or-dir>");

            CommandResult export = runner.Run("image", "export", "/no/such.wz", "--path", "x");
            AssertExitCode(export, 1);
            AssertContains(export.Stderr, "image export requires --out <output-dir>.");

            CommandResult search = runner.Run("image", "search", "/no/such.wz");
            AssertExitCode(search, 1);
            AssertContains(search.Stderr, "image search requires --query <png>.");
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

        private static void UnsupportedWzPackageEmitsJsonDiagnostic(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string input = Path.Combine(temp.Path, "Unsupported_000.wz");
                byte[] bytes = Enumerable.Range(0, 80).Select(i => (byte)i).ToArray();
                File.WriteAllBytes(input, bytes);

                CommandResult result = runner.Run("info", input, "--json");
                AssertExitCode(result, 3);
                AssertEqual(string.Empty, result.Stdout, "unsupported package stdout");
                using (JsonDocument doc = JsonDocument.Parse(result.Stderr))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("wz-load-failed", root.GetProperty("Error").GetString(), "load error code");
                    AssertContains(root.GetProperty("Message").GetString(), "unsupported WZ package format");

                    JsonElement diagnostic = root.GetProperty("Diagnostic");
                    AssertEqual("Unsupported_000.wz", diagnostic.GetProperty("FileName").GetString(), "diagnostic file name");
                    AssertEqual("unsupported-randomized-or-encrypted-wz", diagnostic.GetProperty("DetectedFormat").GetString(), "diagnostic format");
                    AssertEqual(true, diagnostic.GetProperty("IsUnsupportedPackage").GetBoolean(), "unsupported package marker");
                    AssertEqual("00 01 02 03", diagnostic.GetProperty("First4Hex").GetString(), "first bytes");
                    AssertEqual(false, diagnostic.GetProperty("CurrentPkg2RandomDataSizeMatches").GetBoolean(), "pkg2 random size probe");
                }
            }
        }

        private static void Kmst1202RandomHeaderEmitsJsonDiagnostic(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string input = Path.Combine(temp.Path, "Kmst1202_000.wz");
                byte[] bytes = Enumerable.Range(0, 150).Select(i => (byte)((i * 37) & 0xff)).ToArray();
                SetUInt32AtGatherOffsets(bytes, new[] { 0x12, 0x09, 0x02, 0x95 }, 0);
                File.WriteAllBytes(input, bytes);

                CommandResult result = runner.Run("info", input, "--json");
                AssertExitCode(result, 3);
                AssertEqual(string.Empty, result.Stdout, "kmst1202 diagnostic stdout");
                using (JsonDocument doc = JsonDocument.Parse(result.Stderr))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("wz-load-failed", root.GetProperty("Error").GetString(), "load error code");

                    JsonElement diagnostic = root.GetProperty("Diagnostic");
                    AssertEqual("Kmst1202_000.wz", diagnostic.GetProperty("FileName").GetString(), "diagnostic file name");
                    AssertEqual("pkg2-random-header-64", diagnostic.GetProperty("DetectedFormat").GetString(), "diagnostic format");
                    AssertEqual(true, diagnostic.GetProperty("CurrentPkg2RandomHeader64DataSizeMatches").GetBoolean(), "pkg2 random 64 size probe");
                    AssertEqual(false, diagnostic.GetProperty("IsUnsupportedPackage").GetBoolean(), "unsupported package marker");
                }
            }
        }

        private static void SetUInt32AtGatherOffsets(byte[] bytes, int[] offsets, uint value)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                bytes[offsets[i]] = (byte)(value >> (8 * i));
            }
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

        private static void ConfigProfileOverridesDefaultFallback(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string config = Path.Combine(temp.Path, "wcr2.config.json");
                string global = Path.Combine(temp.Path, "global.wz");
                string kms = Path.Combine(temp.Path, "kms.wz");

                AssertExitCode(runner.Run("config", "set", "default-wz", global, "--config", config, "--json"), 0);
                AssertExitCode(runner.Run("config", "set", "default-wz", kms, "--profile", "kms", "--config", config, "--json"), 0);

                CommandResult get = runner.Run("config", "get", "default-wz", "--profile", "kms", "--config", config, "--json");
                AssertExitCode(get, 0);
                using (JsonDocument doc = JsonDocument.Parse(get.Stdout))
                {
                    AssertEqual(kms, doc.RootElement.GetProperty("Value").GetString(), "profile default-wz value");
                    AssertEqual("profiles.kms.default-wz", doc.RootElement.GetProperty("Key").GetString(), "profile storage key");
                }

                CommandResult list = runner.Run("config", "list", "--profile", "kms", "--config", config, "--json");
                AssertExitCode(list, 0);
                using (JsonDocument doc = JsonDocument.Parse(list.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("kms", root.GetProperty("Profile").GetString(), "profile list name");
                    AssertEqual("default-wz", root.GetProperty("Values")[0].GetProperty("Key").GetString(), "profile list display key");
                }

                CommandResult result = runner.Run("info", "--profile", "kms", "--config", config);
                AssertExitCode(result, 2);
                AssertContains(result.Stderr, kms);
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

        private static void NetworkInteractiveChatIsExplicitlyRejected(CliRunner runner)
        {
            CommandResult result = runner.Run("network", "chat", "--interactive");
            AssertExitCode(result, 1);
            AssertContains(result.Stderr, "network chat --interactive is not implemented");
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

        private static void AgentHelpListsRunCommand(CliRunner runner)
        {
            CommandResult result = runner.Run("--help");
            AssertExitCode(result, 0);
            AssertContains(result.Stdout, "wcr2-agent run --job <job.json>");
            AssertContains(result.Stdout, "skill.export");
            AssertContains(result.Stdout, "skill.export-xlsx");
            AssertContains(result.Stdout, "item.icon");
            AssertContains(result.Stdout, "map.export");
            AssertContains(result.Stdout, "wcr2-agent serve --stdio");
        }

        private static void AgentRunEmptyJobEmitsManifest(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string outDir = Path.Combine(temp.Path, "out");
                string jobPath = Path.Combine(temp.Path, "empty-job.json");
                File.WriteAllText(jobPath, "{ \"outputDir\": " + JsonSerializer.Serialize(outDir) + ", \"steps\": [] }");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 0);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("ok", root.GetProperty("status").GetString(), "agent status");
                    AssertEqual(0, root.GetProperty("steps").GetArrayLength(), "agent step count");
                    string manifestPath = root.GetProperty("manifestPath").GetString();
                    AssertEqual(true, File.Exists(manifestPath), "agent manifest exists");
                }
            }
        }

        private static void AgentRunNoopStepSucceeds(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string jobPath = Path.Combine(temp.Path, "noop-job.json");
                File.WriteAllText(jobPath, "{ \"steps\": [{ \"id\": \"probe\", \"type\": \"noop\" }] }");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 0);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("ok", root.GetProperty("status").GetString(), "agent status");
                    JsonElement step = root.GetProperty("steps")[0];
                    AssertEqual("probe", step.GetProperty("id").GetString(), "agent step id");
                    AssertEqual("noop", step.GetProperty("type").GetString(), "agent step type");
                    AssertEqual("ok", step.GetProperty("status").GetString(), "agent step status");
                }
            }
        }

        private static void AgentRunUnknownStepEmitsJsonFailure(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string jobPath = Path.Combine(temp.Path, "unknown-step-job.json");
                File.WriteAllText(jobPath, "{ \"steps\": [{ \"id\": \"probe\", \"type\": \"agent.unknown\" }] }");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 1);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("failed", root.GetProperty("status").GetString(), "agent status");
                    AssertEqual("unknown-step-type", root.GetProperty("error").GetString(), "agent error");
                    AssertEqual("failed", root.GetProperty("steps")[0].GetProperty("status").GetString(), "agent step status");
                }
            }
        }

        private static void AgentRunInvalidJsonEmitsJsonFailure(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string jobPath = Path.Combine(temp.Path, "invalid-job.json");
                File.WriteAllText(jobPath, "{ invalid-json");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 1);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("failed", root.GetProperty("status").GetString(), "agent status");
                    AssertEqual("invalid-json", root.GetProperty("error").GetString(), "agent error");
                }
            }
        }

        private static void AgentServeStdioHandlesPingRunShutdown(CliRunner runner)
        {
            string input = string.Join(Environment.NewLine, new[]
            {
                "{ \"id\": \"p1\", \"method\": \"ping\" }",
                "{ \"id\": \"r1\", \"method\": \"run\", \"job\": { \"steps\": [{ \"id\": \"probe\", \"type\": \"noop\" }] } }",
                "{ \"id\": \"s1\", \"method\": \"shutdown\" }"
            }) + Environment.NewLine;

            CommandResult result = runner.RunWithInput(input, "serve", "--stdio");
            AssertExitCode(result, 0);
            string[] lines = result.Stdout.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            AssertEqual(3, lines.Length, "serve response count");

            using (JsonDocument ping = JsonDocument.Parse(lines[0]))
            {
                AssertEqual("p1", ping.RootElement.GetProperty("id").GetString(), "ping response id");
                AssertEqual("ok", ping.RootElement.GetProperty("status").GetString(), "ping status");
                AssertEqual("pong", ping.RootElement.GetProperty("message").GetString(), "ping message");
            }
            using (JsonDocument run = JsonDocument.Parse(lines[1]))
            {
                AssertEqual("r1", run.RootElement.GetProperty("id").GetString(), "run response id");
                AssertEqual("ok", run.RootElement.GetProperty("status").GetString(), "run status");
                JsonElement runResult = run.RootElement.GetProperty("result");
                AssertEqual("ok", runResult.GetProperty("status").GetString(), "inline run status");
                AssertEqual("noop", runResult.GetProperty("steps")[0].GetProperty("type").GetString(), "inline run step type");
            }
            using (JsonDocument shutdown = JsonDocument.Parse(lines[2]))
            {
                AssertEqual("s1", shutdown.RootElement.GetProperty("id").GetString(), "shutdown response id");
                AssertEqual("ok", shutdown.RootElement.GetProperty("status").GetString(), "shutdown status");
                AssertEqual("shutdown", shutdown.RootElement.GetProperty("message").GetString(), "shutdown message");
            }
        }

        private static void AgentImageSearchValidatesRequiredData(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string queryPath = Path.Combine(temp.Path, "query.png");
                File.WriteAllBytes(queryPath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII="));
                string jobPath = Path.Combine(temp.Path, "image-search-job.json");
                File.WriteAllText(jobPath, "{ \"steps\": [{ \"id\": \"find\", \"type\": \"image.search\", \"query\": " + JsonSerializer.Serialize(queryPath) + " }] }");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 1);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("failed", root.GetProperty("status").GetString(), "agent status");
                    AssertEqual("missing-data-dir", root.GetProperty("error").GetString(), "agent error");
                    JsonElement step = root.GetProperty("steps")[0];
                    AssertEqual("image.search", step.GetProperty("type").GetString(), "agent image search step type");
                    AssertEqual("missing-data-dir", step.GetProperty("error").GetString(), "agent image search step error");
                }
            }
        }

        private static void AgentImageRelatedExportValidatesFromStep(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string jobPath = Path.Combine(temp.Path, "related-export-job.json");
                File.WriteAllText(jobPath, "{ \"steps\": [{ \"id\": \"related\", \"type\": \"image.export-related\" }] }");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 1);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("failed", root.GetProperty("status").GetString(), "agent status");
                    AssertEqual("missing-from-step", root.GetProperty("error").GetString(), "agent error");
                    JsonElement step = root.GetProperty("steps")[0];
                    AssertEqual("image.export-related", step.GetProperty("type").GetString(), "agent related export step type");
                    AssertEqual("missing-from-step", step.GetProperty("error").GetString(), "agent related export step error");
                }
            }
        }

        private static void AgentSkillExportValidatesRequiredFields(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string outDir = Path.Combine(temp.Path, "out");
                string jobPath = Path.Combine(temp.Path, "skill-export-job.json");
                File.WriteAllText(jobPath, "{ \"outputDir\": " + JsonSerializer.Serialize(outDir) + ", \"dataDir\": " + JsonSerializer.Serialize(temp.Path) + ", \"steps\": [{ \"id\": \"skill\", \"type\": \"skill.export\" }] }");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 1);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("failed", root.GetProperty("status").GetString(), "agent status");
                    AssertEqual("missing-skill-id", root.GetProperty("error").GetString(), "agent error");
                    JsonElement step = root.GetProperty("steps")[0];
                    AssertEqual("skill.export", step.GetProperty("type").GetString(), "agent skill export step type");
                    AssertEqual("missing-skill-id", step.GetProperty("error").GetString(), "agent skill export step error");
                }
            }
        }

        private static void AgentSkillBatchValidatesRequests(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string outDir = Path.Combine(temp.Path, "out");
                string jobPath = Path.Combine(temp.Path, "skill-batch-job.json");
                File.WriteAllText(jobPath, "{ \"outputDir\": " + JsonSerializer.Serialize(outDir) + ", \"dataDir\": " + JsonSerializer.Serialize(temp.Path) + ", \"steps\": [{ \"id\": \"batch\", \"type\": \"skill.export-batch\" }] }");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 1);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("failed", root.GetProperty("status").GetString(), "agent status");
                    AssertEqual("missing-batch-requests", root.GetProperty("error").GetString(), "agent error");
                    JsonElement step = root.GetProperty("steps")[0];
                    AssertEqual("skill.export-batch", step.GetProperty("type").GetString(), "agent skill batch step type");
                    AssertEqual("missing-batch-requests", step.GetProperty("error").GetString(), "agent skill batch step error");
                }
            }
        }

        private static void AgentSkillXlsxValidatesWorkbook(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string outDir = Path.Combine(temp.Path, "out");
                string jobPath = Path.Combine(temp.Path, "skill-xlsx-job.json");
                File.WriteAllText(jobPath, "{ \"outputDir\": " + JsonSerializer.Serialize(outDir) + ", \"dataDir\": " + JsonSerializer.Serialize(temp.Path) + ", \"steps\": [{ \"id\": \"xlsx\", \"type\": \"skill.export-xlsx\" }] }");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 1);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("failed", root.GetProperty("status").GetString(), "agent status");
                    AssertEqual("missing-xlsx", root.GetProperty("error").GetString(), "agent error");
                    JsonElement step = root.GetProperty("steps")[0];
                    AssertEqual("skill.export-xlsx", step.GetProperty("type").GetString(), "agent skill xlsx step type");
                    AssertEqual("missing-xlsx", step.GetProperty("error").GetString(), "agent skill xlsx step error");
                }
            }
        }

        private static void AgentItemIconValidatesSelector(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string outDir = Path.Combine(temp.Path, "out");
                string jobPath = Path.Combine(temp.Path, "item-icon-job.json");
                File.WriteAllText(jobPath, "{ \"outputDir\": " + JsonSerializer.Serialize(outDir) + ", \"dataDir\": " + JsonSerializer.Serialize(temp.Path) + ", \"steps\": [{ \"id\": \"icon\", \"type\": \"item.icon\" }] }");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 1);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("failed", root.GetProperty("status").GetString(), "agent status");
                    AssertEqual("missing-item-id-or-name", root.GetProperty("error").GetString(), "agent error");
                    JsonElement step = root.GetProperty("steps")[0];
                    AssertEqual("item.icon", step.GetProperty("type").GetString(), "agent item icon step type");
                    AssertEqual("missing-item-id-or-name", step.GetProperty("error").GetString(), "agent item icon step error");
                }
            }
        }

        private static void AgentItemExportValidatesSelector(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string outDir = Path.Combine(temp.Path, "out");
                string jobPath = Path.Combine(temp.Path, "item-export-job.json");
                File.WriteAllText(jobPath, "{ \"outputDir\": " + JsonSerializer.Serialize(outDir) + ", \"dataDir\": " + JsonSerializer.Serialize(temp.Path) + ", \"steps\": [{ \"id\": \"item\", \"type\": \"item.export\" }] }");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 1);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("failed", root.GetProperty("status").GetString(), "agent status");
                    AssertEqual("missing-item-id-or-name", root.GetProperty("error").GetString(), "agent error");
                    JsonElement step = root.GetProperty("steps")[0];
                    AssertEqual("item.export", step.GetProperty("type").GetString(), "agent item export step type");
                    AssertEqual("missing-item-id-or-name", step.GetProperty("error").GetString(), "agent item export step error");
                }
            }
        }

        private static void AgentMapExportValidatesId(CliRunner runner)
        {
            using (var temp = TempDirectory.Create())
            {
                string outDir = Path.Combine(temp.Path, "out");
                string jobPath = Path.Combine(temp.Path, "map-export-job.json");
                File.WriteAllText(jobPath, "{ \"outputDir\": " + JsonSerializer.Serialize(outDir) + ", \"dataDir\": " + JsonSerializer.Serialize(temp.Path) + ", \"steps\": [{ \"id\": \"map\", \"type\": \"map.export\" }] }");

                CommandResult result = runner.Run("run", "--job", jobPath, "--json");
                AssertExitCode(result, 1);
                using (JsonDocument doc = JsonDocument.Parse(result.Stdout))
                {
                    JsonElement root = doc.RootElement;
                    AssertEqual("failed", root.GetProperty("status").GetString(), "agent status");
                    AssertEqual("missing-map-id", root.GetProperty("error").GetString(), "agent error");
                    JsonElement step = root.GetProperty("steps")[0];
                    AssertEqual("map.export", step.GetProperty("type").GetString(), "agent map export step type");
                    AssertEqual("missing-map-id", step.GetProperty("error").GetString(), "agent map export step error");
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
        public string AgentPath { get; private set; }

        public static TestOptions Parse(IReadOnlyList<string> args)
        {
            string cliPath = Environment.GetEnvironmentVariable("WCR2_TEST_CLI");
            string agentPath = Environment.GetEnvironmentVariable("WCR2_TEST_AGENT");
            for (int i = 0; i < args.Count; i++)
            {
                if (string.Equals(args[i], "--cli", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                {
                    cliPath = args[++i];
                }
                else if (string.Equals(args[i], "--agent", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
                {
                    agentPath = args[++i];
                }
            }

            if (string.IsNullOrEmpty(cliPath))
            {
                cliPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "WzComparerR2.Cli", "bin", "Debug", "net8.0", "wcr2.dll"));
            }

            cliPath = Path.GetFullPath(cliPath);
            if (string.IsNullOrEmpty(agentPath))
            {
                agentPath = InferAgentPath(cliPath);
            }

            agentPath = Path.GetFullPath(agentPath);
            if (!File.Exists(cliPath))
            {
                throw new FileNotFoundException("CLI binary not found. Build WzComparerR2.Cli first or pass --cli <path>.", cliPath);
            }
            if (!File.Exists(agentPath))
            {
                throw new FileNotFoundException("Agent binary not found. Build WzComparerR2.AgentHost first or pass --agent <path>.", agentPath);
            }

            return new TestOptions { CliPath = cliPath, AgentPath = agentPath };
        }

        private static string InferAgentPath(string cliPath)
        {
            string cliDirectory = Path.GetDirectoryName(cliPath);
            DirectoryInfo targetFrameworkDirectory = new DirectoryInfo(cliDirectory);
            DirectoryInfo configurationDirectory = targetFrameworkDirectory.Parent;
            DirectoryInfo binDirectory = configurationDirectory != null ? configurationDirectory.Parent : null;
            DirectoryInfo cliProjectDirectory = binDirectory != null ? binDirectory.Parent : null;
            DirectoryInfo repoDirectory = cliProjectDirectory != null ? cliProjectDirectory.Parent : null;

            if (configurationDirectory != null && repoDirectory != null)
            {
                string sibling = Path.Combine(repoDirectory.FullName, "WzComparerR2.AgentHost", "bin", configurationDirectory.Name, targetFrameworkDirectory.Name, "wcr2-agent.dll");
                if (File.Exists(sibling))
                {
                    return sibling;
                }
            }

            string release = Path.Combine(Directory.GetCurrentDirectory(), "WzComparerR2.AgentHost", "bin", "Release", "net8.0", "wcr2-agent.dll");
            if (File.Exists(release))
            {
                return release;
            }

            return Path.Combine(Directory.GetCurrentDirectory(), "WzComparerR2.AgentHost", "bin", "Debug", "net8.0", "wcr2-agent.dll");
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

        public CommandResult RunWithInput(string stdin, params string[] args)
        {
            string extension = Path.GetExtension(this.CliPath);
            if (string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase))
            {
                return ProgramRun("dotnet", new[] { this.CliPath }.Concat(args).ToList(), stdin);
            }

            return ProgramRun(this.CliPath, args, stdin);
        }

        private static CommandResult ProgramRun(string fileName, IReadOnlyList<string> args, string stdin = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
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
                if (stdin != null)
                {
                    process.StandardInput.Write(stdin);
                    process.StandardInput.Close();
                }
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
