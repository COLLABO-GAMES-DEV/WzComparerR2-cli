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
            if (subCommand == "sprite" || subCommand == "sprites")
            {
                return RunSkillSprite(args);
            }
            if (subCommand == "export")
            {
                return RunSkillExport(args);
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
            });
            return ExitSuccess;
        }

        private static string ResolveSkillFullInput(ParsedArgs args)
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

            throw new UsageException("skill full requires <skill-wz-file-or-dir>, --skill-wz <path>, or --data-dir <dir>.");
        }
    }
}
