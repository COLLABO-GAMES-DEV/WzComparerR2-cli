using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using WzComparerR2.Headless.Media;
using WzComparerR2.Headless.Wz;
using WzComparerR2.WzLib;

namespace WzComparerR2.Headless.Agent
{
    public sealed class AgentJobRunner
    {
        public static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public AgentRunResult Run(AgentRunRequest request)
        {
            if (request == null)
            {
                return Failure(null, null, "missing-request", "Agent run request is required.");
            }

            if (string.IsNullOrWhiteSpace(request.JobPath))
            {
                return Failure(null, null, "missing-job", "agent run requires --job <job.json>.");
            }

            string fullJobPath = Path.GetFullPath(request.JobPath);
            if (!File.Exists(fullJobPath))
            {
                return Failure(fullJobPath, null, "job-not-found", "Job file not found: " + fullJobPath);
            }

            AgentJob job;
            try
            {
                job = JsonSerializer.Deserialize<AgentJob>(File.ReadAllText(fullJobPath), JsonOptions);
            }
            catch (JsonException ex)
            {
                return Failure(fullJobPath, null, "invalid-json", ex.Message);
            }

            if (job == null)
            {
                return Failure(fullJobPath, null, "invalid-job", "Job file did not contain a valid object.");
            }

            string jobDirectory = Directory.GetCurrentDirectory();
            string outputDir = !string.IsNullOrWhiteSpace(request.OutputDirectoryOverride)
                ? request.OutputDirectoryOverride
                : job.OutputDir;
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = ResolvePath(jobDirectory, outputDir);
                Directory.CreateDirectory(outputDir);
            }

            string dataDir = string.IsNullOrWhiteSpace(job.DataDir)
                ? null
                : ResolvePath(jobDirectory, job.DataDir);

            var result = new AgentRunResult
            {
                Status = "ok",
                JobPath = fullJobPath,
                DataDir = dataDir,
                OutputDir = outputDir,
                Steps = new List<AgentStepResult>()
            };

            var context = new AgentRunContext
            {
                JobDirectory = jobDirectory,
                DataDir = dataDir,
                OutputDir = outputDir,
                StepResults = new Dictionary<string, AgentStepResult>(StringComparer.OrdinalIgnoreCase)
            };
            IReadOnlyList<AgentJobStep> steps = job.Steps != null
                ? job.Steps
                : Array.Empty<AgentJobStep>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < steps.Count; i++)
            {
                AgentStepResult stepResult = RunStep(steps[i], i, seenIds, context);
                result.Steps.Add(stepResult);
                if (!string.IsNullOrWhiteSpace(stepResult.Id))
                {
                    context.StepResults[stepResult.Id] = stepResult;
                }
                if (!string.Equals(stepResult.Status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = "failed";
                    result.Error = stepResult.Error;
                    result.Message = stepResult.Message;
                    break;
                }
            }

            WriteManifest(result);
            return result;
        }

        private static AgentStepResult RunStep(AgentJobStep step, int index, HashSet<string> seenIds, AgentRunContext context)
        {
            if (step == null)
            {
                return FailedStep("step-" + (index + 1), null, "invalid-step", "Step must be an object.");
            }

            if (string.IsNullOrWhiteSpace(step.Id))
            {
                return FailedStep("step-" + (index + 1), step.Type, "missing-step-id", "Step id is required.");
            }

            if (!seenIds.Add(step.Id))
            {
                return FailedStep(step.Id, step.Type, "duplicate-step-id", "Duplicate step id: " + step.Id);
            }

            if (string.IsNullOrWhiteSpace(step.Type))
            {
                return FailedStep(step.Id, null, "missing-step-type", "Step type is required: " + step.Id);
            }

            if (string.Equals(step.Type, "noop", StringComparison.OrdinalIgnoreCase))
            {
                return new AgentStepResult
                {
                    Id = step.Id,
                    Type = step.Type,
                    Status = "ok"
                };
            }

            if (string.Equals(step.Type, "image.search", StringComparison.OrdinalIgnoreCase))
            {
                return RunImageSearchStep(step, context);
            }

            if (string.Equals(step.Type, "image.export-related", StringComparison.OrdinalIgnoreCase))
            {
                return RunImageExportRelatedStep(step, context);
            }

            return FailedStep(step.Id, step.Type, "unknown-step-type", "Unknown agent step type: " + step.Type);
        }

        private static AgentStepResult RunImageExportRelatedStep(AgentJobStep step, AgentRunContext context)
        {
            string fromStep = GetString(step, "fromStep");
            if (string.IsNullOrWhiteSpace(fromStep))
            {
                return FailedStep(step.Id, step.Type, "missing-from-step", "image.export-related requires fromStep.");
            }

            AgentStepResult sourceStep;
            if (!context.StepResults.TryGetValue(fromStep, out sourceStep))
            {
                return FailedStep(step.Id, step.Type, "from-step-not-found", "fromStep result not found: " + fromStep);
            }
            if (sourceStep.Results == null || sourceStep.Results.Count == 0)
            {
                return FailedStep(step.Id, step.Type, "from-step-empty", "fromStep has no image search results: " + fromStep);
            }

            string outputDir = GetString(step, "outputDir") ?? GetString(step, "out");
            if (string.IsNullOrWhiteSpace(outputDir) && !string.IsNullOrWhiteSpace(context.OutputDir))
            {
                outputDir = Path.Combine(context.OutputDir, SanitizeFileName(step.Id));
            }
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                return FailedStep(step.Id, step.Type, "missing-output-dir", "image.export-related requires outputDir, out, or job outputDir.");
            }
            outputDir = ResolvePath(context.JobDirectory, outputDir);

            string manifestPath = GetString(step, "manifest") ?? GetString(step, "manifestPath");
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                manifestPath = Path.Combine(outputDir, "related-images-result.json");
            }
            manifestPath = ResolvePath(context.JobDirectory, manifestPath);

            int take = Math.Max(1, GetInt(step, "sourceLimit", 1));
            var options = new RelatedImageExportOptions
            {
                OutputDirectory = outputDir,
                ManifestPath = manifestPath,
                ParentDepth = Math.Max(1, GetInt(step, "parentDepth", 1)),
                MaxFiles = Math.Max(1, GetInt(step, "maxFiles", 100)),
                LoadOptions = CreateWzLoadOptions(step)
            };

            RelatedImageExportResultDto result = RelatedImageExporter.Export(sourceStep.Results.Take(take).ToList(), options);
            return new AgentStepResult
            {
                Id = step.Id,
                Type = step.Type,
                Status = "ok",
                OutputDir = result.OutputDirectory,
                ManifestPath = result.ManifestPath,
                Count = result.Count,
                Files = result.Files,
                Message = result.Truncated ? "truncated" : null
            };
        }

        private static AgentStepResult RunImageSearchStep(AgentJobStep step, AgentRunContext context)
        {
            string query = GetString(step, "query") ?? GetString(step, "image");
            if (string.IsNullOrWhiteSpace(query))
            {
                return FailedStep(step.Id, step.Type, "missing-query", "image.search requires query.");
            }

            query = ResolvePath(context.JobDirectory, query);
            if (!File.Exists(query))
            {
                return FailedStep(step.Id, step.Type, "query-not-found", "Query image not found: " + query);
            }

            string explicitInput = GetString(step, "input") ?? GetString(step, "inputPath");
            if (!string.IsNullOrWhiteSpace(explicitInput))
            {
                explicitInput = ResolvePath(context.JobDirectory, explicitInput);
            }

            string dataDir = GetString(step, "dataDir") ?? context.DataDir;
            if (!string.IsNullOrWhiteSpace(dataDir))
            {
                dataDir = ResolvePath(context.JobDirectory, dataDir);
            }

            if (string.IsNullOrWhiteSpace(explicitInput) && string.IsNullOrWhiteSpace(dataDir))
            {
                return FailedStep(step.Id, step.Type, "missing-data-dir", "image.search requires job dataDir, step dataDir, or step input.");
            }

            string stepOutputDir = GetString(step, "outputDir") ?? GetString(step, "out");
            bool exportTopResults = GetBool(step, "exportTopResults", false);
            if (string.IsNullOrWhiteSpace(stepOutputDir) && exportTopResults && !string.IsNullOrWhiteSpace(context.OutputDir))
            {
                stepOutputDir = Path.Combine(context.OutputDir, SanitizeFileName(step.Id));
            }
            if (!string.IsNullOrWhiteSpace(stepOutputDir))
            {
                stepOutputDir = ResolvePath(context.JobDirectory, stepOutputDir);
            }

            string manifestPath = GetString(step, "manifest") ?? GetString(step, "manifestPath");
            if (string.IsNullOrWhiteSpace(manifestPath) && !string.IsNullOrWhiteSpace(stepOutputDir))
            {
                manifestPath = Path.Combine(stepOutputDir, "image-search-result.json");
            }
            if (!string.IsNullOrWhiteSpace(manifestPath))
            {
                manifestPath = ResolvePath(context.JobDirectory, manifestPath);
            }

            var options = new ImageSearchOptions
            {
                DataDirectory = dataDir,
                QueryPath = query,
                OutputDirectory = stepOutputDir,
                ManifestPath = manifestPath,
                Scope = GetScope(step, "scope", "all"),
                CacheDirectory = ResolveOptionalPath(context.JobDirectory, GetString(step, "cacheDir")),
                NoCache = GetBool(step, "noCache", false),
                RebuildCache = GetBool(step, "rebuildCache", false),
                TrustCache = GetBool(step, "trustCache", false),
                NoRefine = !GetBool(step, "refine", true) || GetBool(step, "noRefine", false),
                NoSizePrefilter = GetBool(step, "noSizePrefilter", false),
                MinSizeRatio = GetDouble(step, "minSizeRatio", 0.25),
                MaxResults = Math.Max(1, GetInt(step, "maxResults", 20)),
                RefineLimit = Math.Max(0, GetInt(step, "refineLimit", 0)),
                MinScore = GetDouble(step, "minScore", 0),
                MinAlpha = Math.Max(0, Math.Min(255, GetInt(step, "minAlpha", 16)))
            };

            if (string.IsNullOrWhiteSpace(options.CacheDirectory) && !options.NoCache)
            {
                options.CacheDirectory = ImageSearchCacheStore.GetDefaultCacheDirectory();
            }

            HeadlessWzLoadOptions loadOptions = CreateWzLoadOptions(step);
            try
            {
                ImageSearchResultDto result;
                string nodePath = GetString(step, "path") ?? GetString(step, "nodePath");
                if (!string.IsNullOrWhiteSpace(explicitInput))
                {
                    using (var loadContext = HeadlessWzLoadContext.Load(explicitInput, loadOptions))
                    {
                        Wz_Node root = HeadlessNodePath.Resolve(loadContext.Root, nodePath, true);
                        if (root == null)
                        {
                            return FailedStep(step.Id, step.Type, "path-not-found", "WZ path not found: " + nodePath);
                        }

                        result = ImageSimilaritySearcher.Search(explicitInput, root, options);
                    }
                }
                else
                {
                    IReadOnlyList<ImageSearchScanRoot> roots = ImageSearchDataSources.FromDataDirectory(dataDir, options.Scope);
                    if (roots.Count == 0)
                    {
                        return FailedStep(step.Id, step.Type, "no-roots", "No canvas roots were found under dataDir for scope: " + options.Scope);
                    }

                    result = ImageSimilaritySearcher.SearchInputs(roots, nodePath, options, loadOptions);
                }

                if (!string.IsNullOrWhiteSpace(options.ManifestPath))
                {
                    result.ManifestPath = ImageSimilaritySearcher.WriteManifest(result, options.ManifestPath);
                }

                return new AgentStepResult
                {
                    Id = step.Id,
                    Type = step.Type,
                    Status = "ok",
                    OutputDir = result.OutputDirectory,
                    ManifestPath = result.ManifestPath,
                    Count = result.Count,
                    Results = result.Results
                };
            }
            catch (Exception ex)
            {
                return FailedStep(step.Id, step.Type, "image-search-failed", GetInnermostMessage(ex));
            }
        }

        private static AgentStepResult FailedStep(string id, string type, string error, string message)
        {
            return new AgentStepResult
            {
                Id = id,
                Type = type,
                Status = "failed",
                Error = error,
                Message = message
            };
        }

        private static AgentRunResult Failure(string jobPath, string outputDir, string error, string message)
        {
            return new AgentRunResult
            {
                Status = "failed",
                JobPath = jobPath,
                OutputDir = outputDir,
                Error = error,
                Message = message,
                Steps = new List<AgentStepResult>()
            };
        }

        private static void WriteManifest(AgentRunResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.OutputDir))
            {
                return;
            }

            result.ManifestPath = Path.Combine(result.OutputDir, "agent-result.json");
            File.WriteAllText(result.ManifestPath, JsonSerializer.Serialize(result, JsonOptions));
        }

        private static HeadlessWzLoadOptions CreateWzLoadOptions(AgentJobStep step)
        {
            return new HeadlessWzLoadOptions
            {
                UseBaseWz = GetBool(step, "useBaseWz", false),
                FallbackPath = GetString(step, "fallback")
            };
        }

        private static string GetString(AgentJobStep step, string key)
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

            return null;
        }

        private static int GetInt(AgentJobStep step, string key, int defaultValue)
        {
            JsonElement value;
            if (step.ExtensionData == null || !step.ExtensionData.TryGetValue(key, out value))
            {
                return defaultValue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        private static double GetDouble(AgentJobStep step, string key, double defaultValue)
        {
            JsonElement value;
            if (step.ExtensionData == null || !step.ExtensionData.TryGetValue(key, out value))
            {
                return defaultValue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        private static bool GetBool(AgentJobStep step, string key, bool defaultValue)
        {
            JsonElement value;
            if (step.ExtensionData == null || !step.ExtensionData.TryGetValue(key, out value))
            {
                return defaultValue;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            return defaultValue;
        }

        private static string GetScope(AgentJobStep step, string key, string defaultValue)
        {
            JsonElement value;
            if (step.ExtensionData == null || !step.ExtensionData.TryGetValue(key, out value))
            {
                return defaultValue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return string.Join(",", value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item)));
            }

            return defaultValue;
        }

        private static string ResolveOptionalPath(string baseDirectory, string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : ResolvePath(baseDirectory, path);
        }

        private static string ResolvePath(string baseDirectory, string path)
        {
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }

            return Path.GetFullPath(Path.Combine(baseDirectory ?? Directory.GetCurrentDirectory(), path));
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = new char[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                chars[i] = invalid.Contains(ch) || ch == '\\' || ch == '/' || ch == ':' ? '_' : ch;
            }

            return new string(chars);
        }

        private static string GetInnermostMessage(Exception ex)
        {
            Exception current = ex;
            while (current.InnerException != null)
            {
                current = current.InnerException;
            }

            return current.Message;
        }

        private sealed class AgentRunContext
        {
            public string JobDirectory { get; set; }
            public string DataDir { get; set; }
            public string OutputDir { get; set; }
            public Dictionary<string, AgentStepResult> StepResults { get; set; }
        }
    }
}
