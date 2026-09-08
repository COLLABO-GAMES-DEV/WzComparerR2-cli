using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WzComparerR2.Headless.Agent
{
    public sealed partial class AgentJobRunner
    {
        private static AgentStepResult RunItemIconStep(AgentJobStep step, AgentRunContext context)
        {
            string id = GetStringAny(step, "itemId", "item", "id");
            string name = GetStringAny(step, "name", "itemName", "item-name");
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name) && LooksLikeNumericIdentifier(step.Id))
            {
                id = step.Id;
            }
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
            {
                return FailedStep(step.Id, step.Type, "missing-item-id-or-name", "item.icon requires itemId/id or name/itemName.");
            }
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
            {
                return FailedStep(step.Id, step.Type, "ambiguous-item-selector", "item.icon accepts either itemId/id or name/itemName, not both.");
            }

            string outputDir = ResolveStepOutputDirectory(step, context);
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                return FailedStep(step.Id, step.Type, "missing-output-dir", "item.icon requires outputDir, out, or job outputDir.");
            }

            var args = new List<string> { "item", "icon" };
            AddOptionalInput(args, step, context);
            AddDomainCommonArgs(args, step, context, "item");
            if (!string.IsNullOrWhiteSpace(id))
            {
                args.Add("--id");
                args.Add(id.Trim());
            }
            else
            {
                args.Add("--name");
                args.Add(name.Trim());
            }
            args.Add("--out");
            args.Add(outputDir);
            args.Add("--json");

            string resultJsonPath = Path.Combine(outputDir, "agent-item-icon-result.json");
            try
            {
                ParsedArgs parsedArgs = ParsedArgs.Parse(args.Skip(1));
                var cacheStatuses = new List<string>();
                ItemIconExportResultDto result = ItemIconExporter.Export(
                    parsedArgs,
                    outputDir,
                    (input, options) => LoadCachedItemIconContext(context, input, options, cacheStatuses));
                AddCacheDiagnostic(result.Diagnostics, cacheStatuses);
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
                    CacheStatus = CombineCacheStatus(cacheStatuses),
                    ExitCode = 0,
                    Count = result.Files == null ? 0 : result.Files.Count
                };
            }
            catch (Exception ex) when (IsDomainExportException(ex))
            {
                return new AgentStepResult
                {
                    Id = step.Id,
                    Type = step.Type,
                    Status = "failed",
                    Error = "item-icon-failed",
                    Message = TrimForMessage(GetInnermostMessage(ex)),
                    OutputDir = outputDir,
                    Command = args,
                    ExitCode = 1
                };
            }
        }

        private static AgentStepResult RunItemExportStep(AgentJobStep step, AgentRunContext context)
        {
            string id = GetStringAny(step, "itemId", "item", "id");
            string name = GetStringAny(step, "name", "itemName", "item-name");
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name) && LooksLikeNumericIdentifier(step.Id))
            {
                id = step.Id;
            }
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
            {
                return FailedStep(step.Id, step.Type, "missing-item-id-or-name", "item.export requires itemId/id or name/itemName.");
            }
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
            {
                return FailedStep(step.Id, step.Type, "ambiguous-item-selector", "item.export accepts either itemId/id or name/itemName, not both.");
            }

            string outputDir = ResolveStepOutputDirectory(step, context);
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                return FailedStep(step.Id, step.Type, "missing-output-dir", "item.export requires outputDir, out, or job outputDir.");
            }

            var args = new List<string> { "item", "export" };
            AddOptionalInput(args, step, context);
            AddDomainCommonArgs(args, step, context, "item");
            if (!string.IsNullOrWhiteSpace(id))
            {
                args.Add("--id");
                args.Add(id.Trim());
            }
            else
            {
                args.Add("--name");
                args.Add(name.Trim());
            }
            args.Add("--out");
            args.Add(outputDir);
            AddBoolFlag(args, step, "--skip-icon", "skipIcon", "skip-icon");
            AddBoolFlag(args, step, "--allow-missing-icon", "allowMissingIcon", "allow-missing-icon");
            args.Add("--json");

            string resultJsonPath = Path.Combine(outputDir, "agent-item-export-result.json");
            try
            {
                ParsedArgs parsedArgs = ParsedArgs.Parse(args.Skip(1));
                var cacheStatuses = new List<string>();
                string itemInput = ItemIconPaths.ResolveItemInput(parsedArgs, parsedArgs.GetValue("data-dir"));
                if (string.IsNullOrWhiteSpace(itemInput))
                {
                    throw new UsageException("item.export requires input, itemWz, or dataDir.");
                }

                if (string.IsNullOrWhiteSpace(id))
                {
                    string stringInput = ItemIconPaths.ResolveStringInput(parsedArgs, parsedArgs.GetValue("data-dir"), itemInput);
                    using (ItemIconExporter.ItemIconLoadLease stringContext = LoadCachedItemIconContext(context, stringInput, WzLoadOptions.FromArgs(parsedArgs), cacheStatuses))
                    {
                        ItemStringMatch match = ItemStringResolver.ResolveByName(stringContext.Context, name);
                        id = match.Id;
                    }
                }
                id = id.Trim();

                AgentDomainRepositoryLease repository = context.SessionCache.GetDomainRepository("item", itemInput, parsedArgs);
                cacheStatuses.Add(FormatCacheStatus(repository.CacheStatus, repository.SessionId));
                DomainInfoDto info = ExportDomainInfo("item", id, repository.Repository, Path.Combine(outputDir, "item-info.json"));

                ItemIconExportResultDto icon = null;
                string iconResultPath = null;
                bool skipIcon = parsedArgs.HasFlag("skip-icon");
                bool allowMissingIcon = parsedArgs.HasFlag("allow-missing-icon");
                if (!skipIcon)
                {
                    try
                    {
                        icon = ItemIconExporter.Export(
                            parsedArgs,
                            Path.Combine(outputDir, "icon"),
                            (input, options) => LoadCachedItemIconContext(context, input, options, cacheStatuses));
                        iconResultPath = Path.Combine(outputDir, "item-icon-result.json");
                        AddCacheDiagnostic(icon.Diagnostics, cacheStatuses);
                        WriteJsonSidecar(iconResultPath, icon);
                    }
                    catch (Exception ex) when (allowMissingIcon && IsDomainExportException(ex))
                    {
                        iconResultPath = Path.Combine(outputDir, "item-icon-result.json");
                        icon = new ItemIconExportResultDto
                        {
                            Id = id,
                            Name = info.Name,
                            OutputDirectory = Path.Combine(outputDir, "icon"),
                            Diagnostics = new List<string> { "Icon export skipped after failure: " + GetInnermostMessage(ex) },
                            Files = new List<ExtractedFileDto>()
                        };
                        AddCacheDiagnostic(icon.Diagnostics, cacheStatuses);
                        WriteJsonSidecar(iconResultPath, icon);
                    }
                }

                var result = new ItemExportResultDto
                {
                    Id = id,
                    Name = info.Name,
                    OutputDirectory = Path.GetFullPath(outputDir),
                    ItemInfoPath = Path.Combine(outputDir, "item-info.json"),
                    IconResultPath = iconResultPath,
                    ResultPath = resultJsonPath,
                    ItemInfo = info,
                    Icon = icon,
                    ExportedFileCount = icon == null || icon.Files == null ? 0 : icon.Files.Count,
                    Diagnostics = icon == null || icon.Diagnostics == null ? new List<string>() : icon.Diagnostics
                };
                AddCacheDiagnostic(result.Diagnostics, cacheStatuses);
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
                    CacheStatus = CombineCacheStatus(cacheStatuses),
                    ExitCode = 0,
                    Count = result.ExportedFileCount
                };
            }
            catch (Exception ex) when (IsDomainExportException(ex))
            {
                return new AgentStepResult
                {
                    Id = step.Id,
                    Type = step.Type,
                    Status = "failed",
                    Error = "item-export-failed",
                    Message = TrimForMessage(GetInnermostMessage(ex)),
                    OutputDir = outputDir,
                    Command = args,
                    ExitCode = 1
                };
            }
        }

        private static AgentStepResult RunMapExportStep(AgentJobStep step, AgentRunContext context)
        {
            string id = GetStringAny(step, "mapId", "map", "id");
            if (string.IsNullOrWhiteSpace(id) && LooksLikeNumericIdentifier(step.Id))
            {
                id = step.Id;
            }
            if (string.IsNullOrWhiteSpace(id))
            {
                return FailedStep(step.Id, step.Type, "missing-map-id", "map.export requires mapId/id.");
            }

            string outputDir = ResolveStepOutputDirectory(step, context);
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                return FailedStep(step.Id, step.Type, "missing-output-dir", "map.export requires outputDir, out, or job outputDir.");
            }

            var args = new List<string> { "map", "export" };
            AddOptionalInput(args, step, context);
            AddDomainCommonArgs(args, step, context, "map");
            args.Add("--id");
            args.Add(id.Trim());
            args.Add("--out");
            args.Add(outputDir);
            args.Add("--json");

            string resultJsonPath = Path.Combine(outputDir, "agent-map-export-result.json");
            try
            {
                ParsedArgs parsedArgs = ParsedArgs.Parse(args.Skip(1));
                var cacheStatuses = new List<string>();
                string mapInput = ResolveDomainInputFromParsed(parsedArgs, "map");
                if (string.IsNullOrWhiteSpace(mapInput))
                {
                    throw new UsageException("map.export requires input, mapWz, or dataDir.");
                }

                AgentDomainRepositoryLease repository = context.SessionCache.GetDomainRepository("map", mapInput, parsedArgs);
                cacheStatuses.Add(FormatCacheStatus(repository.CacheStatus, repository.SessionId));
                DomainInfoDto info = ExportDomainInfo("map", id.Trim(), repository.Repository, Path.Combine(outputDir, "map-info.json"));
                MapMetadataDto metadata;
                CliWzDataResult dataResult = repository.Repository.FindDataNode("map", id.Trim());
                if (dataResult == null)
                {
                    throw new UsageException("map id not found: " + id.Trim());
                }
                metadata = MapMetadataDto.FromMapNode(id.Trim(), dataResult.Node, "objects");

                string metadataPath = Path.Combine(outputDir, "map-metadata.json");
                WriteJsonSidecar(metadataPath, metadata);

                var result = new MapExportResultDto
                {
                    Id = id.Trim(),
                    Name = info.Name,
                    OutputDirectory = Path.GetFullPath(outputDir),
                    MapInfoPath = Path.Combine(outputDir, "map-info.json"),
                    MapMetadataPath = metadataPath,
                    ResultPath = resultJsonPath,
                    MapInfo = info,
                    Metadata = metadata,
                    PortalCount = metadata.Portals == null ? 0 : metadata.Portals.Count,
                    LifeCount = metadata.Life == null ? 0 : metadata.Life.Count,
                    ReactorCount = metadata.Reactors == null ? 0 : metadata.Reactors.Count,
                    ObjectCount = metadata.Objects == null ? 0 : metadata.Objects.Count
                };
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
                    CacheStatus = CombineCacheStatus(cacheStatuses),
                    ExitCode = 0,
                    Count = result.PortalCount + result.LifeCount + result.ReactorCount + result.ObjectCount
                };
            }
            catch (Exception ex) when (IsDomainExportException(ex))
            {
                return new AgentStepResult
                {
                    Id = step.Id,
                    Type = step.Type,
                    Status = "failed",
                    Error = "map-export-failed",
                    Message = TrimForMessage(GetInnermostMessage(ex)),
                    OutputDir = outputDir,
                    Command = args,
                    ExitCode = 1
                };
            }
        }

        private static DomainInfoDto ExportDomainInfo(string kind, string input, string id, ParsedArgs args, string outputPath)
        {
            using (var repository = CliWzRepository.ForDomain(kind, input, args))
            {
                return ExportDomainInfo(kind, id, repository, outputPath);
            }
        }

        private static DomainInfoDto ExportDomainInfo(string kind, string id, CliWzRepository repository, string outputPath)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }

            CliWzDataResult dataResult = repository.FindDataNode(kind, id);
            if (dataResult == null)
            {
                throw new UsageException(kind + " id not found: " + id);
            }

            DomainStringInfo stringInfo = null;
            CliWzStringResult stringResult = repository.FindStringInfo(kind, id);
            if (stringResult != null)
            {
                stringInfo = stringResult.StringInfo;
            }

            DomainInfoDto dto = DomainInfoDto.FromNode(
                kind,
                id,
                dataResult.Node,
                stringInfo,
                dataResult.InputPath,
                stringResult == null ? null : stringResult.InputPath,
                repository.DataInputPaths,
                repository.StringInputPaths);
            WriteJsonSidecar(outputPath, dto);
            return dto;
        }

        private static ItemIconExporter.ItemIconLoadLease LoadCachedItemIconContext(
            AgentRunContext context,
            string input,
            WzLoadOptions options,
            List<string> cacheStatuses)
        {
            AgentWzContextLease lease = context.SessionCache.GetWzContext(input, options);
            cacheStatuses.Add(FormatCacheStatus(lease.CacheStatus, lease.SessionId));
            return new ItemIconExporter.ItemIconLoadLease(lease.Context, false);
        }

        private static void AddCacheDiagnostic(List<string> diagnostics, List<string> cacheStatuses)
        {
            if (diagnostics == null || cacheStatuses == null || cacheStatuses.Count == 0)
            {
                return;
            }
            diagnostics.Add("Agent cache: " + string.Join(", ", cacheStatuses));
        }

        private static string FormatCacheStatus(string status, string id)
        {
            return string.IsNullOrWhiteSpace(id) ? status : status + "(" + id + ")";
        }

        private static string CombineCacheStatus(List<string> cacheStatuses)
        {
            if (cacheStatuses == null || cacheStatuses.Count == 0)
            {
                return null;
            }

            bool hasHit = cacheStatuses.Any(item => item.StartsWith("cache-hit", StringComparison.OrdinalIgnoreCase));
            bool hasMiss = cacheStatuses.Any(item => item.StartsWith("cache-miss", StringComparison.OrdinalIgnoreCase));
            if (hasHit && hasMiss)
            {
                return "cache-mixed";
            }
            return hasHit ? "cache-hit" : "cache-miss";
        }

        private static string ResolveStepOutputDirectory(AgentJobStep step, AgentRunContext context)
        {
            string outputDir = GetStringAny(step, "outputDir", "out", "output");
            if (string.IsNullOrWhiteSpace(outputDir) && !string.IsNullOrWhiteSpace(context.OutputDir))
            {
                outputDir = Path.Combine(context.OutputDir, SanitizeFileName(step.Id));
            }
            return string.IsNullOrWhiteSpace(outputDir)
                ? null
                : ResolvePath(context.JobDirectory, outputDir);
        }

        private static void AddOptionalInput(List<string> args, AgentJobStep step, AgentRunContext context)
        {
            string input = GetStringAny(step, "input", "inputPath");
            if (!string.IsNullOrWhiteSpace(input))
            {
                args.Add(ResolvePath(context.JobDirectory, input));
            }
        }

        private static void AddDomainCommonArgs(List<string> args, AgentJobStep step, AgentRunContext context, string kind)
        {
            string dataDir = GetStringAny(step, "dataDir", "data-dir");
            if (string.IsNullOrWhiteSpace(dataDir))
            {
                dataDir = context.DataDir;
            }
            if (!string.IsNullOrWhiteSpace(dataDir))
            {
                args.Add("--data-dir");
                args.Add(ResolvePath(context.JobDirectory, dataDir));
            }

            AddPathOption(args, step, context, "--" + kind + "-wz", kind + "Wz", kind + "-wz");
            if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase))
            {
                AddPathOption(args, step, context, "--string-wz", "stringWz", "string-wz");
                AddPathOption(args, step, context, "--canvas-wz", "canvasWz", "canvas-wz", "canvas");
                AddOption(args, step, "--category", "category");
            }
            else if (string.Equals(kind, "map", StringComparison.OrdinalIgnoreCase))
            {
                AddPathOption(args, step, context, "--string-wz", "stringWz", "string-wz");
            }

            AddBoolFlag(args, step, "--use-base-wz", "useBaseWz", "use-base-wz");
            AddPathOption(args, step, context, "--fallback", "fallback");
        }

        private static string ResolveDomainInputFromParsed(ParsedArgs args, string kind)
        {
            if (args.Positionals.Count > 1 && !string.IsNullOrWhiteSpace(args.Positionals[1]))
            {
                return Path.GetFullPath(args.Positionals[1]);
            }

            string explicitInput = args.GetValue(kind + "-wz");
            if (!string.IsNullOrWhiteSpace(explicitInput))
            {
                return explicitInput;
            }

            string dataDir = args.GetValue("data-dir");
            return string.IsNullOrWhiteSpace(dataDir)
                ? null
                : Path.Combine(dataDir, CliWzRepository.GetDefaultDataFolderName(kind));
        }

        private static bool IsDomainExportException(Exception ex)
        {
            return ex is UsageException
                || ex is FileNotFoundException
                || ex is DirectoryNotFoundException
                || ex is WzLoadException
                || ex is InvalidDataException;
        }

        private static bool LooksLikeNumericIdentifier(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Trim().All(char.IsDigit);
        }
    }

    internal sealed class ItemExportResultDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string OutputDirectory { get; set; }
        public string ItemInfoPath { get; set; }
        public string IconResultPath { get; set; }
        public string ResultPath { get; set; }
        public int ExportedFileCount { get; set; }
        public DomainInfoDto ItemInfo { get; set; }
        public ItemIconExportResultDto Icon { get; set; }
        public List<string> Diagnostics { get; set; }
    }

    internal sealed class MapExportResultDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string OutputDirectory { get; set; }
        public string MapInfoPath { get; set; }
        public string MapMetadataPath { get; set; }
        public string ResultPath { get; set; }
        public int PortalCount { get; set; }
        public int LifeCount { get; set; }
        public int ReactorCount { get; set; }
        public int ObjectCount { get; set; }
        public DomainInfoDto MapInfo { get; set; }
        public MapMetadataDto Metadata { get; set; }
    }
}
