using System;
using System.IO;

namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
        private static int RunDomainInfo(ParsedArgs args, string kind)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintDomainHelp(kind);
                return ExitSuccess;
            }
            if (string.Equals(kind, "item", StringComparison.OrdinalIgnoreCase)
                && string.Equals(args.Positionals[0], "icon", StringComparison.OrdinalIgnoreCase))
            {
                return RunItemIcon(args);
            }
            if (!string.Equals(args.Positionals[0], "info", StringComparison.OrdinalIgnoreCase))
            {
                throw new UsageException("Unknown " + kind + " command: " + args.Positionals[0]);
            }
            string input = ResolveDomainInfoInput(args, kind);
            string id = args.GetValue("id");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(id))
            {
                throw new UsageException(kind + " info requires --id <id>.");
            }

            using (var repository = CliWzRepository.ForDomain(kind, input, args))
            {
                var dataResult = repository.FindDataNode(kind, id);
                if (dataResult == null)
                {
                    throw new UsageException(kind + " id not found: " + id);
                }

                DomainStringInfo stringInfo = null;
                var stringResult = repository.FindStringInfo(kind, id);
                if (stringResult != null)
                {
                    stringInfo = stringResult.StringInfo;
                }

                var dto = DomainInfoDto.FromNode(
                    kind,
                    id,
                    dataResult.Node,
                    stringInfo,
                    dataResult.InputPath,
                    stringResult == null ? null : stringResult.InputPath,
                    repository.DataInputPaths,
                    repository.StringInputPaths);
                WriteOutput(dto, json, writer =>
                {
                    writer.WriteLine(kind + " " + id);
                    if (!string.IsNullOrEmpty(dto.DataInputPath))
                    {
                        writer.WriteLine("DataInput: " + dto.DataInputPath);
                    }
                    if (!string.IsNullOrEmpty(dto.StringInputPath))
                    {
                        writer.WriteLine("StringInput: " + dto.StringInputPath);
                    }
                    writer.WriteLine("Path: " + dto.Path);
                    if (!string.IsNullOrEmpty(dto.Name))
                    {
                        writer.WriteLine("Name: " + dto.Name);
                    }
                    if (!string.IsNullOrEmpty(dto.Description))
                    {
                        writer.WriteLine("Description: " + dto.Description);
                    }
                    writer.WriteLine("Children: " + dto.ChildrenCount + " Properties: " + dto.Properties.Count);
                });
            }

            return ExitSuccess;
        }

        private static int RunItemIcon(ParsedArgs args)
        {
            string id = args.GetValue("id");
            string name = args.GetValue("name");
            string output = args.GetValue("out") ?? args.GetValue("output");
            string manifest = args.GetValue("manifest");
            bool json = args.HasFlag("json");

            if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(name))
            {
                throw new UsageException("item icon requires --id <id> or --name <exact-name>.");
            }
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
            {
                throw new UsageException("item icon accepts either --id <id> or --name <exact-name>, not both.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("item icon requires --out <output-dir>.");
            }

            ItemIconExportResultDto result = ItemIconExporter.Export(args, output);
            if (!string.IsNullOrEmpty(manifest))
            {
                result.ManifestPath = ExtractExporter.WriteManifest(result.ToExtractResult(), manifest);
            }

            WriteOutput(result, json, writer =>
            {
                writer.WriteLine("item " + result.Id + " icon");
                if (!string.IsNullOrEmpty(result.Name))
                {
                    writer.WriteLine("Name: " + result.Name);
                }
                writer.WriteLine("Category: " + result.Category);
                writer.WriteLine("IconPath: " + result.IconPath);
                foreach (var file in result.Files)
                {
                    writer.WriteLine("- " + file.OutputPath);
                }
            });

            return ExitSuccess;
        }

        private static string ResolveDomainInfoInput(ParsedArgs args, string kind)
        {
            if (args.Positionals.Count > 1 && !IsHelp(args.Positionals[1]))
            {
                return args.Positionals[1];
            }

            string domainInput = GetDomainInputOption(args, kind);
            if (!string.IsNullOrEmpty(domainInput))
            {
                return domainInput;
            }

            string dataDir = args.GetValue("data-dir");
            if (!string.IsNullOrEmpty(dataDir))
            {
                return Path.Combine(dataDir, CliWzRepository.GetDefaultDataFolderName(kind));
            }

            string optionName = string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase) ? "character-wz" : kind + "-wz";
            throw new UsageException(kind + " info requires <wz-file-or-dir>, --" + optionName + " <path>, or --data-dir <dir>.");
        }

        private static string GetDomainInputOption(ParsedArgs args, string kind)
        {
            string direct = args.GetValue(kind + "-wz");
            if (!string.IsNullOrEmpty(direct))
            {
                return direct;
            }
            if (string.Equals(kind, "gear", StringComparison.OrdinalIgnoreCase))
            {
                return args.GetValue("character-wz") ?? args.GetValue("item-wz");
            }
            return null;
        }
    }
}
