using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WzComparerR2.Headless.Agent
{
    public sealed partial class AgentJobRunner
    {
        private static readonly JsonSerializerOptions SkillSidecarJsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

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
            string skillInput;
            if (!AddSkillInputArgs(args, step, context, out skillInput))
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
            try
            {
                ParsedArgs parsedArgs = ParsedArgs.Parse(args.Skip(1));
                SkillSpriteExportOptions options = SkillSpriteExportOptions.FromArgs(parsedArgs, true);
                AgentSkillSessionLease session = context.SessionCache.GetSkillSession(skillInput, parsedArgs, options);
                SkillSpriteExportResultDto result = session.Session.Export(skillId.Trim(), outputDir);
                result.Diagnostics.Add("Agent session cache: " + session.CacheStatus + " (" + session.SessionId + ")");
                WriteJsonSidecar(resultJsonPath, result);

                return new AgentStepResult
                {
                    Id = step.Id,
                    Type = step.Type,
                    Status = "ok",
                    OutputDir = result.OutputDirectory,
                    ManifestPath = resultJsonPath,
                    ResultPath = resultJsonPath,
                    Command = args,
                    CacheStatus = session.CacheStatus,
                    ExitCode = 0,
                    Count = result.ExportedFileCount
                };
            }
            catch (Exception ex) when (IsSkillExportException(ex))
            {
                return new AgentStepResult
                {
                    Id = step.Id,
                    Type = step.Type,
                    Status = "failed",
                    Error = "skill-export-failed",
                    Message = TrimForMessage(GetInnermostMessage(ex)),
                    OutputDir = outputDir,
                    Command = args,
                    ExitCode = 1
                };
            }
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
            string skillInput;
            if (!AddSkillInputArgs(args, step, context, out skillInput))
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

            AddOption(args, step, "--output-pattern", "outputPattern", "output-pattern");
            AddSkillCommonArgs(args, step, context);
            AddBoolFlag(args, step, "--continue-on-error", "continueOnError", "continue-on-error");
            AddBoolFlag(args, step, "--skip-existing", "skipExisting", "skip-existing");
            args.Add("--json");

            string resultJsonPath = Path.Combine(outputRoot, "agent-skill-batch-result.json");
            try
            {
                ParsedArgs parsedArgs = ParsedArgs.Parse(args.Skip(1));
                SkillSpriteExportOptions exportOptions = SkillSpriteExportOptions.FromArgs(parsedArgs, true);
                SkillBatchExportOptions batchOptions = SkillBatchExportOptions.FromArgs(parsedArgs);
                AgentSkillSessionLease session = context.SessionCache.GetSkillSession(skillInput, parsedArgs, exportOptions);
                SkillBatchExportManifestDto result = SkillBatchExporter.ExportWithSession(skillInput, session.Session, parsedArgs, batchOptions);
                result.Diagnostics.Add("Agent session cache: " + session.CacheStatus + " (" + session.SessionId + ")");
                WriteJsonSidecar(resultJsonPath, result);

                bool success = result.Failed == 0;
                return new AgentStepResult
                {
                    Id = step.Id,
                    Type = step.Type,
                    Status = success ? "ok" : "failed",
                    Error = success ? null : "skill-export-batch-failed",
                    Message = success ? null : "One or more skill export-batch items failed.",
                    OutputDir = result.OutputRoot,
                    ManifestPath = result.ManifestPath,
                    ResultPath = resultJsonPath,
                    Command = args,
                    CacheStatus = session.CacheStatus,
                    ExitCode = success ? 0 : 1,
                    Count = result.ExportedFileCount
                };
            }
            catch (Exception ex) when (IsSkillExportException(ex))
            {
                return new AgentStepResult
                {
                    Id = step.Id,
                    Type = step.Type,
                    Status = "failed",
                    Error = "skill-export-batch-failed",
                    Message = TrimForMessage(GetInnermostMessage(ex)),
                    OutputDir = outputRoot,
                    ManifestPath = manifestPath,
                    Command = args,
                    ExitCode = 1
                };
            }
        }

        private static AgentStepResult RunSkillExportXlsxStep(AgentJobStep step, AgentRunContext context)
        {
            string workbookPath = GetStringAny(step, "xlsx", "xlsxFile", "xlsx-file", "workbook", "file");
            if (string.IsNullOrWhiteSpace(workbookPath))
            {
                return FailedStep(step.Id, step.Type, "missing-xlsx", "skill.export-xlsx requires xlsx, xlsxFile, or workbook.");
            }
            workbookPath = ResolvePath(context.JobDirectory, workbookPath);

            string outputRoot = GetStringAny(step, "outRoot", "outputRoot", "outputDir", "out", "output");
            if (string.IsNullOrWhiteSpace(outputRoot) && !string.IsNullOrWhiteSpace(context.OutputDir))
            {
                outputRoot = Path.Combine(context.OutputDir, SanitizeFileName(step.Id));
            }
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                return FailedStep(step.Id, step.Type, "missing-output-root", "skill.export-xlsx requires outRoot, outputDir, out, or job outputDir.");
            }
            outputRoot = ResolvePath(context.JobDirectory, outputRoot);

            var args = new List<string> { "skill", "export-batch" };
            string skillInput;
            if (!AddSkillInputArgs(args, step, context, out skillInput))
            {
                return FailedStep(step.Id, step.Type, "missing-data-dir", "skill.export-xlsx requires job/step dataDir, skillWz, or input.");
            }
            args.Add("--out-root");
            args.Add(outputRoot);

            string agentDirectory = Path.Combine(outputRoot, "_agent");
            string namesFilePath = Path.Combine(agentDirectory, SanitizeFileName(step.Id) + "-names.tsv");
            string recipePath = Path.Combine(agentDirectory, SanitizeFileName(step.Id) + "-xlsx-recipe.json");
            string manifestPath = GetStringAny(step, "manifest", "manifestPath");
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                manifestPath = Path.Combine(outputRoot, "manifest.json");
            }
            manifestPath = ResolvePath(context.JobDirectory, manifestPath);

            args.Add("--names-file");
            args.Add(namesFilePath);
            args.Add("--manifest");
            args.Add(manifestPath);

            string outputPattern = GetStringAny(step, "outputPattern", "output-pattern");
            if (string.IsNullOrWhiteSpace(outputPattern))
            {
                outputPattern = "{jobCode}_{jobName}/{id}_{name}";
            }
            args.Add("--output-pattern");
            args.Add(outputPattern);

            AddSkillCommonArgs(args, step, context);
            AddBoolFlag(args, step, "--continue-on-error", "continueOnError", "continue-on-error");
            AddBoolFlag(args, step, "--skip-existing", "skipExisting", "skip-existing");
            args.Add("--json");

            string resultJsonPath = Path.Combine(outputRoot, "agent-skill-batch-result.json");
            try
            {
                var recipeOptions = new XlsxSkillRecipeOptions
                {
                    SheetName = GetStringAny(step, "sheet", "sheetName", "sheet-name"),
                    JobNameColumn = GetStringAny(step, "jobNameColumn", "job-name-column", "jobNameCol", "job-name-col"),
                    JobCodeColumn = GetStringAny(step, "jobCodeColumn", "job-code-column", "jobCodeCol", "job-code-col"),
                    SkillColumns = SplitOptionList(GetStringOrArrayAny(step, "skillColumns", "skill-columns", "skillColumn", "skill-column")),
                    HeaderRow = GetIntAny(step, 0, "headerRow", "header-row"),
                    FirstDataRow = GetIntAny(step, 0, "firstDataRow", "first-data-row"),
                    MaxRows = GetIntAny(step, 0, "maxRows", "max-rows")
                };
                XlsxSkillRecipeResultDto recipe = XlsxSkillRecipeReader.Read(workbookPath, recipeOptions);
                XlsxSkillRecipeReader.WriteNamesFile(recipe, namesFilePath);
                WriteJsonSidecar(recipePath, recipe);

                ParsedArgs parsedArgs = ParsedArgs.Parse(args.Skip(1));
                SkillSpriteExportOptions exportOptions = SkillSpriteExportOptions.FromArgs(parsedArgs, true);
                SkillBatchExportOptions batchOptions = SkillBatchExportOptions.FromArgs(parsedArgs);
                AgentSkillSessionLease session = context.SessionCache.GetSkillSession(skillInput, parsedArgs, exportOptions);
                SkillBatchExportManifestDto result = SkillBatchExporter.ExportWithSession(skillInput, session.Session, parsedArgs, batchOptions);
                result.Diagnostics.Add("XLSX recipe: " + recipePath);
                result.Diagnostics.Add("XLSX names file: " + namesFilePath);
                result.Diagnostics.Add("Agent session cache: " + session.CacheStatus + " (" + session.SessionId + ")");
                WriteJsonSidecar(resultJsonPath, result);

                bool success = result.Failed == 0;
                return new AgentStepResult
                {
                    Id = step.Id,
                    Type = step.Type,
                    Status = success ? "ok" : "failed",
                    Error = success ? null : "skill-export-xlsx-failed",
                    Message = success ? null : "One or more skill export-xlsx items failed.",
                    OutputDir = result.OutputRoot,
                    ManifestPath = result.ManifestPath,
                    ResultPath = resultJsonPath,
                    Command = args,
                    CacheStatus = session.CacheStatus,
                    ExitCode = success ? 0 : 1,
                    Count = result.ExportedFileCount
                };
            }
            catch (Exception ex) when (IsSkillExportException(ex) || ex is InvalidDataException)
            {
                return new AgentStepResult
                {
                    Id = step.Id,
                    Type = step.Type,
                    Status = "failed",
                    Error = "skill-export-xlsx-failed",
                    Message = TrimForMessage(GetInnermostMessage(ex)),
                    OutputDir = outputRoot,
                    ManifestPath = manifestPath,
                    Command = args,
                    ExitCode = 1
                };
            }
        }

        private static bool AddSkillInputArgs(List<string> args, AgentJobStep step, AgentRunContext context, out string skillInput)
        {
            string input = GetStringAny(step, "input", "skillInput");
            if (!string.IsNullOrWhiteSpace(input))
            {
                skillInput = ResolvePath(context.JobDirectory, input);
                args.Add(skillInput);
                return true;
            }

            string skillWz = GetStringAny(step, "skillWz", "skill-wz");
            if (!string.IsNullOrWhiteSpace(skillWz))
            {
                skillInput = ResolvePath(context.JobDirectory, skillWz);
                args.Add("--skill-wz");
                args.Add(skillInput);
                return true;
            }

            string dataDir = GetStringAny(step, "dataDir", "data-dir") ?? context.DataDir;
            if (!string.IsNullOrWhiteSpace(dataDir))
            {
                dataDir = ResolvePath(context.JobDirectory, dataDir);
                skillInput = Path.Combine(dataDir, "Skill");
                args.Add("--data-dir");
                args.Add(dataDir);
                return true;
            }

            skillInput = null;
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

        private static int GetIntAny(AgentJobStep step, int defaultValue, params string[] keys)
        {
            foreach (string key in keys)
            {
                int value = GetInt(step, key, int.MinValue);
                if (value != int.MinValue)
                {
                    return value;
                }
            }
            return defaultValue;
        }

        private static List<string> SplitOptionList(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            foreach (string part in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    result.Add(trimmed);
                }
            }
            return result;
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

        private static void WriteJsonSidecar(string path, object value)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(fullPath, JsonSerializer.Serialize(value, SkillSidecarJsonOptions));
        }

        private static bool IsSkillExportException(Exception ex)
        {
            return ex is UsageException
                || ex is FileNotFoundException
                || ex is DirectoryNotFoundException
                || ex is WzLoadException
                || ex is InvalidDataException
                || ex is JsonException;
        }
    }
}
