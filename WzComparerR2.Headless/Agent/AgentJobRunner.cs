using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

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

            string outputDir = !string.IsNullOrWhiteSpace(request.OutputDirectoryOverride)
                ? request.OutputDirectoryOverride
                : job.OutputDir;
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                outputDir = Path.GetFullPath(outputDir);
                Directory.CreateDirectory(outputDir);
            }

            var result = new AgentRunResult
            {
                Status = "ok",
                JobPath = fullJobPath,
                DataDir = string.IsNullOrWhiteSpace(job.DataDir) ? null : Path.GetFullPath(job.DataDir),
                OutputDir = outputDir,
                Steps = new List<AgentStepResult>()
            };

            IReadOnlyList<AgentJobStep> steps = job.Steps != null
                ? job.Steps
                : Array.Empty<AgentJobStep>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < steps.Count; i++)
            {
                AgentStepResult stepResult = RunStep(steps[i], i, seenIds);
                result.Steps.Add(stepResult);
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

        private static AgentStepResult RunStep(AgentJobStep step, int index, HashSet<string> seenIds)
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

            return FailedStep(step.Id, step.Type, "unknown-step-type", "Unknown agent step type: " + step.Type);
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
    }
}
