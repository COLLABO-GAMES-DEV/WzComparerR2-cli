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
        public string BaseDirectory { get; set; }
        public AgentJob Job { get; set; }
    }

    public sealed class AgentJob
    {
        public string DataDir { get; set; }
        public string OutputDir { get; set; }
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
        public AgentSessionCacheStatsDto CacheStats { get; set; }
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
        public string ResultPath { get; set; }
        public List<string> Command { get; set; }
        public string CacheStatus { get; set; }
        public int? ExitCode { get; set; }
        public int? Count { get; set; }
        public List<ImageSearchMatchDto> Results { get; set; }
        public List<RelatedImageExportFileDto> Files { get; set; }
    }

    public sealed class AgentSessionCacheStatsDto
    {
        public int SkillSessionCount { get; set; }
        public int MaxSkillSessions { get; set; }
        public long SkillSessionHits { get; set; }
        public long SkillSessionMisses { get; set; }
        public long SkillSessionEvictions { get; set; }
        public List<AgentSkillSessionCacheEntryDto> SkillSessions { get; set; }
        public int DomainRepositoryCount { get; set; }
        public int MaxDomainRepositories { get; set; }
        public long DomainRepositoryHits { get; set; }
        public long DomainRepositoryMisses { get; set; }
        public long DomainRepositoryEvictions { get; set; }
        public List<AgentDomainRepositoryCacheEntryDto> DomainRepositories { get; set; }
        public int WzContextCount { get; set; }
        public int MaxWzContexts { get; set; }
        public long WzContextHits { get; set; }
        public long WzContextMisses { get; set; }
        public long WzContextEvictions { get; set; }
        public List<AgentWzContextCacheEntryDto> WzContexts { get; set; }
    }

    public sealed class AgentSkillSessionCacheEntryDto
    {
        public string Id { get; set; }
        public string SkillInputPath { get; set; }
        public string DataDirectory { get; set; }
        public int UseCount { get; set; }
        public string CreatedAt { get; set; }
        public string LastUsedAt { get; set; }
    }

    public sealed class AgentDomainRepositoryCacheEntryDto
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string InputPath { get; set; }
        public string DataDirectory { get; set; }
        public int UseCount { get; set; }
        public string CreatedAt { get; set; }
        public string LastUsedAt { get; set; }
    }

    public sealed class AgentWzContextCacheEntryDto
    {
        public string Id { get; set; }
        public string InputPath { get; set; }
        public int UseCount { get; set; }
        public string CreatedAt { get; set; }
        public string LastUsedAt { get; set; }
    }
}
