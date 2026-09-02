using System;
using System.IO;

namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
        private static int RunSkill(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintSkillHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            if (subCommand == "info")
            {
                return RunDomainInfo(args, "skill");
            }
            if (subCommand == "full" || subCommand == "detail")
            {
                return RunSkillFull(args);
            }
            if (subCommand == "search-name" || subCommand == "name-search")
            {
                return RunSkillNameLookup(args, false);
            }
            if (subCommand == "resolve-name" || subCommand == "name-resolve")
            {
                return RunSkillNameLookup(args, true);
            }
            if (subCommand == "sprite" || subCommand == "sprites")
            {
                return RunSkillSprite(args);
            }
            if (subCommand == "export")
            {
                return RunSkillExport(args);
            }
            if (subCommand == "export-batch" || subCommand == "batch-export")
            {
                return RunSkillExportBatch(args);
            }

            throw new UsageException("Unknown skill command: " + args.Positionals[0]);
        }

        private static int RunSkillFull(ParsedArgs args)
        {
            string input = ResolveSkillFullInput(args);
            string id = args.GetValue("id");
            if (string.IsNullOrEmpty(id))
            {
                throw new UsageException("skill full requires --id <id>.");
            }

            string format = args.GetValue("format") ?? (args.HasFlag("json") ? "json" : "text");
            string output = args.GetValue("out") ?? args.GetValue("output");
            int? level = null;
            string levelText = args.GetValue("level");
            if (!string.IsNullOrEmpty(levelText))
            {
                if (!string.Equals(levelText, "max", StringComparison.OrdinalIgnoreCase))
                {
                    int parsedLevel;
                    if (!int.TryParse(levelText, out parsedLevel) || parsedLevel < 0)
                    {
                        throw new UsageException("skill full --level must be a non-negative integer or max.");
                    }
                    level = parsedLevel;
                }
            }

            DomainStringInfo stringInfo = null;
            using (var repository = CliWzRepository.ForSkill(input, args))
            {
                var stringResult = repository.FindStringInfo("skill", id);
                if (stringResult != null)
                {
                    stringInfo = stringResult.StringInfo;
                }

                var dataResult = repository.FindDataNode("skill", id);
                if (dataResult == null)
                {
                    if (args.HasFlag("allow-string-only") && stringInfo != null)
                    {
                        var stringOnly = SkillFullDto.FromStringOnly(id, stringInfo, stringResult.InputPath, repository.DataInputPaths, repository.StringInputPaths);
                        WriteSkillFullOutput(stringOnly, format, output);
                        return ExitSuccess;
                    }

                    throw new UsageException("skill id not found: " + id);
                }

                var dto = SkillFullDto.FromNode(
                    id,
                    dataResult.Node,
                    stringInfo,
                    level,
                    dataResult.InputPath,
                    stringResult == null ? null : stringResult.InputPath,
                    repository.DataInputPaths,
                    repository.StringInputPaths);
                WriteSkillFullOutput(dto, format, output);
            }

            return ExitSuccess;
        }

        private static int RunSkillNameLookup(ParsedArgs args, bool requireResolved)
        {
            string input = ResolveSkillInput(args, requireResolved ? "skill resolve-name" : "skill search-name");
            var options = SkillNameSearchOptions.FromArgs(args, requireResolved || args.HasFlag("require-data"));
            SkillNameSearchResultDto result;
            using (var repository = CliWzRepository.ForSkill(input, args))
            {
                result = requireResolved
                    ? SkillNameResolver.Resolve(repository, options)
                    : SkillNameResolver.Search(repository, options);
            }

            WriteOutput(result, args.HasFlag("json"), writer =>
            {
                writer.WriteLine((requireResolved ? "Skill name resolve: " : "Skill name search: ") + result.Query);
                writer.WriteLine("Status: " + result.Status);
                if (!string.IsNullOrEmpty(result.ResolvedId))
                {
                    writer.WriteLine("Resolved: " + result.ResolvedId + "\t" + result.ResolvedName);
                }
                writer.WriteLine("Candidates: " + result.ReturnedCount);
                foreach (var candidate in result.Candidates)
                {
                    writer.WriteLine("- " + candidate.Id
                        + "\t" + candidate.Name
                        + "\tscore=" + candidate.Score
                        + "\tmatch=" + candidate.MatchType
                        + "\tjob=" + candidate.JobCode
                        + "\tdata=" + candidate.FoundData
                        + "\tprofile=" + candidate.SourceProfile);
                }
                foreach (string diagnostic in result.Diagnostics)
                {
                    writer.WriteLine("  " + diagnostic);
                }
            });

            return requireResolved && !string.Equals(result.Status, "resolved", StringComparison.OrdinalIgnoreCase)
                ? ExitUsage
                : ExitSuccess;
        }

        private static int RunSkillSprite(ParsedArgs args)
        {
            return RunSkillAssetExport(args, "skill sprite", false);
        }

        private static int RunSkillExport(ParsedArgs args)
        {
            return RunSkillAssetExport(args, "skill export", true);
        }

        private static int RunSkillAssetExport(ParsedArgs args, string commandName, bool includeSoundsByDefault)
        {
            string id = args.GetValue("id");
            if (string.IsNullOrEmpty(id))
            {
                throw new UsageException(commandName + " requires --id <id>.");
            }

            string output = args.GetValue("out") ?? args.GetValue("output");
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException(commandName + " requires --out <output-dir>.");
            }

            string input = ResolveSkillFullInput(args);
            var options = SkillSpriteExportOptions.FromArgs(args, includeSoundsByDefault);
            SkillSpriteExportResultDto result = SkillSpriteExporter.Export(input, id, output, args, options);

            WriteOutput(result, args.HasFlag("json"), writer =>
            {
                writer.WriteLine("Skill asset export: " + result.SkillId);
                writer.WriteLine("Exported files: " + result.ExportedFileCount);
                writer.WriteLine("Output: " + result.OutputDirectory);
                if (result.MetadataPaths != null && result.MetadataPaths.Count > 0)
                {
                    writer.WriteLine("Metadata files: " + result.MetadataFileCount);
                    foreach (string metadataPath in result.MetadataPaths)
                    {
                        writer.WriteLine("- metadata\t" + metadataPath);
                    }
                }
                foreach (var branch in result.Branches)
                {
                    writer.WriteLine("- " + branch.Branch + "\t" + branch.Status + "\t" + branch.ExportedFileCount);
                    if (!string.IsNullOrEmpty(branch.Diagnostic))
                    {
                        writer.WriteLine("  " + branch.Diagnostic);
                    }
                }
                if (result.Sound != null)
                {
                    writer.WriteLine("- sound\t" + result.Sound.Status + "\t" + result.Sound.ExportedFileCount);
                    if (!string.IsNullOrEmpty(result.Sound.Diagnostic))
                    {
                        writer.WriteLine("  " + result.Sound.Diagnostic);
                    }
                }
                if (result.Videos != null)
                {
                    writer.WriteLine("- video\t" + result.Videos.Status + "\t" + result.Videos.ExportedFileCount);
                    if (!string.IsNullOrEmpty(result.Videos.Diagnostic))
                    {
                        writer.WriteLine("  " + result.Videos.Diagnostic);
                    }
                }
                foreach (var related in result.RelatedAssets)
                {
                    writer.WriteLine("- related:" + related.Key + "\t" + related.Status + "\t" + related.ExportedFileCount);
                    writer.WriteLine("  " + related.ExportRootPath);
                    if (!string.IsNullOrEmpty(related.Diagnostic))
                    {
                        writer.WriteLine("  " + related.Diagnostic);
                    }
                }
            });
            return ExitSuccess;
        }

        private static int RunSkillExportBatch(ParsedArgs args)
        {
            string input = ResolveSkillFullInput(args);
            var exportOptions = SkillSpriteExportOptions.FromArgs(args, true);
            var batchOptions = SkillBatchExportOptions.FromArgs(args);
            SkillBatchExportManifestDto result = SkillBatchExporter.Export(input, args, exportOptions, batchOptions);

            WriteOutput(result, args.HasFlag("json"), writer =>
            {
                writer.WriteLine("Skill batch export");
                writer.WriteLine("Total: " + result.Total);
                writer.WriteLine("Succeeded: " + result.Succeeded);
                writer.WriteLine("Skipped: " + result.Skipped);
                writer.WriteLine("Failed: " + result.Failed);
                writer.WriteLine("Exported files: " + result.ExportedFileCount);
                writer.WriteLine("Output root: " + result.OutputRoot);
                if (!string.IsNullOrEmpty(result.ManifestPath))
                {
                    writer.WriteLine("Manifest: " + result.ManifestPath);
                }
                foreach (var item in result.Items)
                {
                    string label = !string.IsNullOrEmpty(item.RequestedName)
                        ? item.RequestedName + " -> " + item.Id
                        : item.Id;
                    writer.WriteLine("- " + label + "\t" + item.Status + "\t" + item.ExportedFileCount + "\t" + item.RelativeOutput);
                    if (!string.IsNullOrEmpty(item.ResolveStatus))
                    {
                        writer.WriteLine("  resolve=" + item.ResolveStatus + " candidates=" + item.ResolveCandidateCount);
                    }
                    if (!string.IsNullOrEmpty(item.Error))
                    {
                        writer.WriteLine("  " + item.Error);
                    }
                }
            });
            return result.Failed == 0 ? ExitSuccess : ExitUsage;
        }

        private static string ResolveSkillFullInput(ParsedArgs args)
        {
            return ResolveSkillInput(args, "skill full");
        }

        private static string ResolveSkillInput(ParsedArgs args, string commandName)
        {
            if (args.Positionals.Count > 1 && !IsHelp(args.Positionals[1]))
            {
                return args.Positionals[1];
            }

            string skillWz = args.GetValue("skill-wz");
            if (!string.IsNullOrEmpty(skillWz))
            {
                return skillWz;
            }

            string dataDir = args.GetValue("data-dir");
            if (!string.IsNullOrEmpty(dataDir))
            {
                return Path.Combine(dataDir, "Skill");
            }

            throw new UsageException(commandName + " requires <skill-wz-file-or-dir>, --skill-wz <path>, or --data-dir <dir>.");
        }
    }
}
