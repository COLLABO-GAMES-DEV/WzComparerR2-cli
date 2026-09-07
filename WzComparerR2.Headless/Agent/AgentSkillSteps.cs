using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WzComparerR2.Headless.Agent
{
    public sealed partial class AgentJobRunner
    {
        private static AgentStepResult RunSkillExportStep(AgentJobStep step, AgentRunContext context)
        {
            string skillId = GetStringAny(step, "skillId", "skill");
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return FailedStep(step.Id, step.Type, "missing-skill-id", "skill.export requires skillId.");
            }

            string outputDir = GetStringAny(step, "outputDir", "out", "output");
            if (string.IsNullOrWhiteSpace(outputDir) && !string.IsNullOrWhiteSpace(context.OutputDir))
            {
                outputDir = Path.Combine(context.OutputDir, SanitizeFileName(step.Id));
            }
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                return FailedStep(step.Id, step.Type, "missing-output-dir", "skill.export requires outputDir, out, or job outputDir.");
            }
            outputDir = ResolvePath(context.JobDirectory, outputDir);

            var args = new List<string> { "skill", "export" };
            if (!AddSkillInputArgs(args, step, context))
            {
                return FailedStep(step.Id, step.Type, "missing-data-dir", "skill.export requires job/step dataDir, skillWz, or input.");
            }
            args.Add("--id");
            args.Add(skillId.Trim());
            args.Add("--out");
            args.Add(outputDir);
            AddSkillCommonArgs(args, step, context);
            args.Add("--json");

            string resultJsonPath = Path.Combine(outputDir, "agent-skill-export-result.json");
            return RunCliJsonStep(step, context, outputDir, args, resultJsonPath, null, "skill-export-failed");
        }

        private static AgentStepResult RunSkillExportBatchStep(AgentJobStep step, AgentRunContext context)
        {
            string outputRoot = GetStringAny(step, "outRoot", "outputRoot", "outputDir", "out", "output");
            if (string.IsNullOrWhiteSpace(outputRoot) && !string.IsNullOrWhiteSpace(context.OutputDir))
            {
                outputRoot = Path.Combine(context.OutputDir, SanitizeFileName(step.Id));
            }
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                return FailedStep(step.Id, step.Type, "missing-output-root", "skill.export-batch requires outRoot, outputDir, out, or job outputDir.");
            }
            outputRoot = ResolvePath(context.JobDirectory, outputRoot);

            var args = new List<string> { "skill", "export-batch" };
            if (!AddSkillInputArgs(args, step, context))
            {
                return FailedStep(step.Id, step.Type, "missing-data-dir", "skill.export-batch requires job/step dataDir, skillWz, or input.");
            }
            args.Add("--out-root");
            args.Add(outputRoot);

            if (!AddSkillBatchRequestArgs(args, step, context))
            {
                return FailedStep(step.Id, step.Type, "missing-batch-requests", "skill.export-batch requires ids, idsFile, names, or namesFile.");
            }

            string manifestPath = GetStringAny(step, "manifest", "manifestPath");
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                manifestPath = Path.Combine(outputRoot, "manifest.json");
            }
            manifestPath = ResolvePath(context.JobDirectory, manifestPath);
            args.Add("--manifest");
            args.Add(manifestPath);

            AddSkillCommonArgs(args, step, context);
            AddBoolFlag(args, step, "--continue-on-error", "continueOnError", "continue-on-error");
            AddBoolFlag(args, step, "--skip-existing", "skipExisting", "skip-existing");
            args.Add("--json");

            string resultJsonPath = Path.Combine(outputRoot, "agent-skill-batch-result.json");
            return RunCliJsonStep(step, context, outputRoot, args, resultJsonPath, manifestPath, "skill-export-batch-failed");
        }

        private static AgentStepResult RunCliJsonStep(
            AgentJobStep step,
            AgentRunContext context,
            string outputDirectory,
            IReadOnlyList<string> args,
            string stdoutJsonPath,
            string manifestPath,
            string failureError)
        {
            string requestedCliPath = GetStringAny(step, "cliPath", "cli") ?? context.CliPath;
            string cliPath = AgentCliBridge.ResolveCliPath(requestedCliPath, context.JobDirectory);
            if (string.IsNullOrWhiteSpace(cliPath))
            {
                return FailedStep(step.Id, step.Type, "cli-not-found", "CLI binary not found. Pass --cli, job cliPath, step cliPath, or WCR2_CLI_PATH.");
            }

            Directory.CreateDirectory(outputDirectory);
            AgentCliCommandResult cliResult;
            try
            {
                cliResult = AgentCliBridge.Run(cliPath, args, context.JobDirectory, GetInt(step, "timeoutSeconds", 1800));
            }
            catch (Exception ex)
            {
                return FailedStep(step.Id, step.Type, failureError, GetInnermostMessage(ex));
            }

            string stdoutPath = null;
            if (!string.IsNullOrWhiteSpace(cliResult.Stdout))
            {
                stdoutPath = ResolvePath(context.JobDirectory, stdoutJsonPath);
                Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath) ?? outputDirectory);
                File.WriteAllText(stdoutPath, cliResult.Stdout);
            }

            string stderrPath = null;
            if (!string.IsNullOrWhiteSpace(cliResult.Stderr))
            {
                stderrPath = Path.Combine(outputDirectory, "agent-cli-stderr.txt");
                File.WriteAllText(stderrPath, cliResult.Stderr);
            }

            int? count = TryReadExportedFileCount(cliResult.Stdout);
            if (!count.HasValue && !string.IsNullOrWhiteSpace(manifestPath) && File.Exists(manifestPath))
            {
                count = TryReadExportedFileCount(File.ReadAllText(manifestPath));
            }

            bool success = cliResult.ExitCode == 0;
            return new AgentStepResult
            {
                Id = step.Id,
                Type = step.Type,
                Status = success ? "ok" : "failed",
                Error = success ? null : failureError,
                Message = success ? null : TrimForMessage(string.IsNullOrWhiteSpace(cliResult.Stderr) ? cliResult.Stdout : cliResult.Stderr),
                OutputDir = outputDirectory,
                ManifestPath = !string.IsNullOrWhiteSpace(manifestPath) ? manifestPath : stdoutPath,
                CliPath = cliResult.CliPath,
                Command = cliResult.Command,
                ExitCode = cliResult.ExitCode,
                StdoutPath = stdoutPath,
                StderrPath = stderrPath,
                Count = count
            };
        }

        private static bool AddSkillInputArgs(List<string> args, AgentJobStep step, AgentRunContext context)
        {
            string input = GetStringAny(step, "input", "skillInput");
            if (!string.IsNullOrWhiteSpace(input))
            {
                args.Add(ResolvePath(context.JobDirectory, input));
                return true;
            }

            string skillWz = GetStringAny(step, "skillWz", "skill-wz");
            if (!string.IsNullOrWhiteSpace(skillWz))
            {
                args.Add("--skill-wz");
                args.Add(ResolvePath(context.JobDirectory, skillWz));
                return true;
            }

            string dataDir = GetStringAny(step, "dataDir", "data-dir") ?? context.DataDir;
            if (!string.IsNullOrWhiteSpace(dataDir))
            {
                args.Add("--data-dir");
                args.Add(ResolvePath(context.JobDirectory, dataDir));
                return true;
            }

            return false;
        }

        private static void AddSkillCommonArgs(List<string> args, AgentJobStep step, AgentRunContext context)
        {
            AddOption(args, step, "--branch", "branch", "branches");
            AddPathOption(args, step, context, "--canvas-wz", "canvasWz", "canvas-wz", "canvas");
            AddPathOption(args, step, context, "--sound-wz", "soundWz", "sound-wz", "sound");
            AddPathOption(args, step, context, "--related-wz", "relatedWz", "related-wz", "relatedInput", "related-input");
            AddPathOption(args, step, context, "--effect-wz", "effectWz", "effect-wz");
            AddPathOption(args, step, context, "--character-wz", "characterWz", "character-wz");
            AddOption(args, step, "--related-key", "relatedKey", "related-key", "relatedKeys", "related-keys");
            AddOption(args, step, "--video-format", "videoFormat", "video-format");
            AddOption(args, step, "--max-canvas-inputs", "maxCanvasInputs", "max-canvas-inputs");
            AddOption(args, step, "--max-sound-inputs", "maxSoundInputs", "max-sound-inputs");
            AddOption(args, step, "--max-related-inputs", "maxRelatedInputs", "max-related-inputs");
            AddOption(args, step, "--max-related-matches", "maxRelatedMatches", "max-related-matches");

            AddBoolFlag(args, step, "--direct-only", "directOnly", "direct-only");
            AddBoolFlag(args, step, "--include-sound", "includeSound", "includeSounds", "include-sound", "include-sounds");
            AddBoolFlag(args, step, "--include-video", "includeVideo", "includeVideos", "include-video", "include-videos");
            AddBoolFlag(args, step, "--include-related", "includeRelated", "includeRelatedAssets", "include-related", "include-related-assets");
            AddBoolFlag(args, step, "--skip-related", "skipRelated", "skip-related");
            AddBoolFlag(args, step, "--skip-video", "skipVideo", "skip-video");
        }

        private static bool AddSkillBatchRequestArgs(List<string> args, AgentJobStep step, AgentRunContext context)
        {
            bool added = false;
            added |= AddOption(args, step, "--ids", "ids", "skillIds");
            added |= AddPathOption(args, step, context, "--ids-file", "idsFile", "ids-file");
            added |= AddOption(args, step, "--names", "names", "skillNames");
            added |= AddPathOption(args, step, context, "--names-file", "namesFile", "names-file");
            return added;
        }

        private static bool AddOption(List<string> args, AgentJobStep step, string cliName, params string[] keys)
        {
            string value = GetStringOrArrayAny(step, keys);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            args.Add(cliName);
            args.Add(value);
            return true;
        }

        private static bool AddPathOption(List<string> args, AgentJobStep step, AgentRunContext context, string cliName, params string[] keys)
        {
            string value = GetStringOrArrayAny(step, Path.PathSeparator.ToString(), keys);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            args.Add(cliName);
            args.Add(ResolvePathList(context.JobDirectory, value));
            return true;
        }

        private static void AddBoolFlag(List<string> args, AgentJobStep step, string cliName, params string[] keys)
        {
            foreach (string key in keys)
            {
                bool value;
                if (TryGetBool(step, key, out value))
                {
                    if (value)
                    {
                        args.Add(cliName);
                    }
                    return;
                }
            }
        }

        private static string GetStringAny(AgentJobStep step, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = GetString(step, key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string GetStringOrArrayAny(AgentJobStep step, params string[] keys)
        {
            return GetStringOrArrayAny(step, ",", keys);
        }

        private static string GetStringOrArrayAny(AgentJobStep step, string separator, params string[] keys)
        {
            foreach (string key in keys)
            {
                string value = GetStringOrArray(step, key, separator);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string GetStringOrArray(AgentJobStep step, string key, string separator)
        {
            JsonElement value;
            if (step.ExtensionData == null || !step.ExtensionData.TryGetValue(key, out value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return string.Join(separator, value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String || item.ValueKind == JsonValueKind.Number)
                    .Select(item => item.ToString())
                    .Where(item => !string.IsNullOrWhiteSpace(item)));
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.ToString();
            }

            return null;
        }

        private static bool TryGetBool(AgentJobStep step, string key, out bool result)
        {
            JsonElement value;
            if (step.ExtensionData != null && step.ExtensionData.TryGetValue(key, out value))
            {
                if (value.ValueKind == JsonValueKind.True)
                {
                    result = true;
                    return true;
                }
                if (value.ValueKind == JsonValueKind.False)
                {
                    result = false;
                    return true;
                }
            }

            result = false;
            return false;
        }

        private static string ResolvePathList(string baseDirectory, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var parts = value.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1)
            {
                return ResolvePath(baseDirectory, value);
            }

            return string.Join(Path.PathSeparator.ToString(), parts.Select(part => ResolvePath(baseDirectory, part.Trim())));
        }

        private static int? TryReadExportedFileCount(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    return TryReadIntProperty(document.RootElement, "ExportedFileCount")
                        ?? TryReadIntProperty(document.RootElement, "exportedFileCount")
                        ?? TryReadIntProperty(document.RootElement, "Count")
                        ?? TryReadIntProperty(document.RootElement, "count");
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static int? TryReadIntProperty(JsonElement element, string propertyName)
        {
            JsonElement value;
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out int parsed))
            {
                return parsed;
            }

            return null;
        }

        private static string TrimForMessage(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string trimmed = value.Trim();
            return trimmed.Length <= 2000 ? trimmed : trimmed.Substring(0, 2000);
        }
    }
}
