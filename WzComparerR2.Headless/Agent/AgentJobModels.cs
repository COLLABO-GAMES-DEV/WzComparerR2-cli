using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using WzComparerR2.Headless.Media;

namespace WzComparerR2.Headless.Agent
{
    public sealed class AgentRunRequest
    {
        public string JobPath { get; set; }
        public string OutputDirectoryOverride { get; set; }
        public string CliPath { get; set; }
    }

    public sealed class AgentJob
    {
        public string DataDir { get; set; }
        public string OutputDir { get; set; }
        public string CliPath { get; set; }
        public List<AgentJobStep> Steps { get; set; }
    }

    public sealed class AgentJobStep
    {
        public string Id { get; set; }
        public string Type { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement> ExtensionData { get; set; }
    }

    public sealed class AgentRunResult
    {
        public string Status { get; set; }
        public string JobPath { get; set; }
        public string DataDir { get; set; }
        public string OutputDir { get; set; }
        public string ManifestPath { get; set; }
        public string Error { get; set; }
        public string Message { get; set; }
        public List<AgentStepResult> Steps { get; set; }

        [JsonIgnore]
        public bool IsSuccess
        {
            get { return string.Equals(this.Status, "ok", StringComparison.OrdinalIgnoreCase); }
        }
    }

    public sealed class AgentStepResult
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
        public string Message { get; set; }
        public string OutputDir { get; set; }
        public string ManifestPath { get; set; }
        public string CliPath { get; set; }
        public List<string> Command { get; set; }
        public int? ExitCode { get; set; }
        public string StdoutPath { get; set; }
        public string StderrPath { get; set; }
        public int? Count { get; set; }
        public List<ImageSearchMatchDto> Results { get; set; }
        public List<RelatedImageExportFileDto> Files { get; set; }
    }
}
