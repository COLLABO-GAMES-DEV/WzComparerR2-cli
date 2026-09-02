using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WzComparerR2.Cli
{
    internal static class SkillExportMetadataWriter
    {
        private static readonly JsonSerializerOptions SidecarJsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        public static SkillExportInfoDto BuildSkillInfo(
            string skillId,
            CliWzDataResult dataResult,
            CliWzStringResult stringResult,
            CliWzRepository repository,
            List<string> diagnostics)
        {
            try
            {
                var full = SkillFullDto.FromNode(
                    skillId,
                    dataResult.Node,
                    stringResult == null ? null : stringResult.StringInfo,
                    null,
                    dataResult.InputPath,
                    stringResult == null ? null : stringResult.InputPath,
                    repository.DataInputPaths,
                    repository.StringInputPaths);
                return SkillExportInfoDto.FromSkillFull(full);
            }
            catch (Exception ex)
            {
                if (diagnostics != null)
                {
                    diagnostics.Add("Skill metadata sidecar could not be fully built: " + ex.Message);
                }
                return SkillExportInfoDto.FromFallback(skillId, dataResult, stringResult);
            }
        }

        public static List<SkillExportResourceDto> BuildResourceSummaries(SkillSpriteExportResultDto result)
        {
            var resources = new List<SkillExportResourceDto>();
            foreach (var branch in result.Branches ?? Enumerable.Empty<SkillSpriteBranchResultDto>())
            {
                resources.Add(new SkillExportResourceDto
                {
                    Kind = "image",
                    Resource = branch.Branch,
                    RequestedPath = branch.RequestedPath,
                    SourcePath = branch.SourcePath,
                    OutlinkPath = branch.OutlinkPath,
                    ResolvedPath = branch.ResolvedPath,
                    Status = branch.Status,
                    Diagnostic = branch.Diagnostic,
                    ExportedFileCount = branch.ExportedFileCount,
                    Files = branch.Files ?? new List<ExtractedFileDto>()
                });
            }

            if (result.Sound != null)
            {
                resources.Add(new SkillExportResourceDto
                {
                    Kind = "sound",
                    Resource = "sound",
                    RequestedPath = result.Sound.RequestedPath,
                    SourcePath = result.Sound.SourcePath,
                    InputPath = result.Sound.InputPath,
                    Status = result.Sound.Status,
                    Diagnostic = result.Sound.Diagnostic,
                    ExportedFileCount = result.Sound.ExportedFileCount,
                    Files = result.Sound.Files ?? new List<ExtractedFileDto>()
                });
            }

            if (result.Videos != null)
            {
                resources.Add(new SkillExportResourceDto
                {
                    Kind = "video",
                    Resource = "video",
                    RequestedPath = result.Videos.RequestedPath,
                    SourcePath = result.Videos.SourcePath,
                    Status = result.Videos.Status,
                    Diagnostic = result.Videos.Diagnostic,
                    ExportedFileCount = result.Videos.ExportedFileCount,
                    Files = result.Videos.Files ?? new List<ExtractedFileDto>()
                });
            }

            foreach (var related in result.RelatedAssets ?? Enumerable.Empty<SkillRelatedAssetResultDto>())
            {
                resources.Add(new SkillExportResourceDto
                {
                    Kind = "related",
                    Resource = related.Key,
                    RequestedPath = related.Key,
                    SourcePath = related.MatchedPath,
                    InputPath = related.InputPath,
                    OutputPath = related.ExportRootPath,
                    Status = related.Status,
                    Diagnostic = related.Diagnostic,
                    ExportedFileCount = related.ExportedFileCount,
                    Files = related.Files ?? new List<ExtractedFileDto>()
                });
            }

            return resources;
        }

        public static void WriteSidecars(SkillSpriteExportResultDto result, string outputDirectory)
        {
            string skillInfoPath = Path.Combine(outputDirectory, "skill-info.json");
            string resourcePath = Path.Combine(outputDirectory, "resources.json");
            File.WriteAllText(skillInfoPath, JsonSerializer.Serialize(result.SkillInfo, SidecarJsonOptions));
            File.WriteAllText(resourcePath, JsonSerializer.Serialize(SkillExportResourceManifestDto.FromResult(result), SidecarJsonOptions));
            if (result.MetadataPaths == null)
            {
                result.MetadataPaths = new List<string>();
            }
            result.MetadataPaths.Add(skillInfoPath);
            result.MetadataPaths.Add(resourcePath);
            result.MetadataFileCount = result.MetadataPaths.Count;
        }
    }

    internal sealed class SkillExportInfoDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string PassiveDescription { get; set; }
        public int? Level { get; set; }
        public int? MaxLevel { get; set; }
        public int? MasterLevel { get; set; }
        public string RawSummary { get; set; }
        public string ResolvedSummary { get; set; }
        public string NextRawSummary { get; set; }
        public string NextResolvedSummary { get; set; }
        public string SourceProfile { get; set; }
        public string DataInputPath { get; set; }
        public string StringInputPath { get; set; }
        public string DataPath { get; set; }
        public string StringPath { get; set; }
        public List<string> VisualBranches { get; set; }
        public List<string> Actions { get; set; }
        public List<SkillSummaryVariantDto> SummaryVariants { get; set; }
        public List<string> UnresolvedPlaceholders { get; set; }
        public List<string> Diagnostics { get; set; }

        public static SkillExportInfoDto FromSkillFull(SkillFullDto full)
        {
            return new SkillExportInfoDto
            {
                Id = full.Id,
                Name = full.Name,
                Description = full.Description,
                PassiveDescription = full.PassiveDescription,
                Level = full.Level,
                MaxLevel = full.MaxLevel,
                MasterLevel = full.MasterLevel,
                RawSummary = full.RawSummary,
                ResolvedSummary = full.ResolvedSummary,
                NextRawSummary = full.NextRawSummary,
                NextResolvedSummary = full.NextResolvedSummary,
                SourceProfile = full.SourceProfile,
                DataInputPath = full.DataInputPath,
                StringInputPath = full.StringInputPath,
                DataPath = full.DataPath,
                StringPath = full.StringPath,
                VisualBranches = full.VisualBranches ?? new List<string>(),
                Actions = full.Actions ?? new List<string>(),
                SummaryVariants = full.SummaryVariants ?? new List<SkillSummaryVariantDto>(),
                UnresolvedPlaceholders = full.UnresolvedPlaceholders ?? new List<string>(),
                Diagnostics = full.Diagnostics ?? new List<string>()
            };
        }

        public static SkillExportInfoDto FromFallback(string skillId, CliWzDataResult dataResult, CliWzStringResult stringResult)
        {
            var stringInfo = stringResult == null ? null : stringResult.StringInfo;
            return new SkillExportInfoDto
            {
                Id = skillId,
                Name = stringInfo == null ? null : stringInfo.Name,
                Description = stringInfo == null ? null : stringInfo.Description,
                SourceProfile = "fallback",
                DataInputPath = dataResult == null ? null : dataResult.InputPath,
                StringInputPath = stringResult == null ? null : stringResult.InputPath,
                DataPath = dataResult == null || dataResult.Node == null ? null : dataResult.Node.FullPath,
                StringPath = stringInfo == null || stringInfo.Values == null ? null : stringInfo.Values.GetValueOrDefault("__path"),
                VisualBranches = new List<string>(),
                Actions = new List<string>(),
                SummaryVariants = new List<SkillSummaryVariantDto>(),
                UnresolvedPlaceholders = new List<string>(),
                Diagnostics = new List<string> { "Skill metadata was written with fallback string/data fields only." }
            };
        }
    }

    internal sealed class SkillExportResourceManifestDto
    {
        public string SkillId { get; set; }
        public string SkillName { get; set; }
        public string OutputDirectory { get; set; }
        public List<SkillExportResourceDto> Resources { get; set; }

        public static SkillExportResourceManifestDto FromResult(SkillSpriteExportResultDto result)
        {
            return new SkillExportResourceManifestDto
            {
                SkillId = result.SkillId,
                SkillName = result.SkillInfo == null ? null : result.SkillInfo.Name,
                OutputDirectory = result.OutputDirectory,
                Resources = result.Resources ?? new List<SkillExportResourceDto>()
            };
        }
    }

    internal sealed class SkillExportResourceDto
    {
        public string Kind { get; set; }
        public string Resource { get; set; }
        public string RequestedPath { get; set; }
        public string SourcePath { get; set; }
        public string OutlinkPath { get; set; }
        public string ResolvedPath { get; set; }
        public string InputPath { get; set; }
        public string OutputPath { get; set; }
        public string Status { get; set; }
        public string Diagnostic { get; set; }
        public int ExportedFileCount { get; set; }
        public List<ExtractedFileDto> Files { get; set; }
    }
}
