using System;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
        private static int RunMap(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintMapHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            if (subCommand == "info")
            {
                return RunDomainInfo(args, "map");
            }
            if (subCommand == "render")
            {
                return RunMapRender(args);
            }

            if (subCommand != "objects" && subCommand != "portals" && subCommand != "life" && subCommand != "reactors")
            {
                throw new UsageException("Unknown map command: " + args.Positionals[0]);
            }
            string input = RequireInputAt(args, 1, "map " + subCommand + " <map-wz-file-or-dir> --id <map-id> [--json]");
            string id = args.GetValue("id");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(id))
            {
                throw new UsageException("map " + subCommand + " requires --id <map-id>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node mapNode = DomainInfoFinder.FindDataNode(context.Root, "map", id);
                if (mapNode == null)
                {
                    throw new UsageException("map id not found: " + id);
                }

                var result = MapMetadataDto.FromMapNode(id, mapNode, subCommand);
                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Map " + id);
                    writer.WriteLine("Path: " + result.Path);
                    writer.WriteLine("Portals: " + result.Portals.Count + " Life: " + result.Life.Count + " Reactors: " + result.Reactors.Count + " Objects: " + result.Objects.Count);
                    foreach (var item in result.SelectedItems)
                    {
                        writer.WriteLine(item.Kind + "\t" + item.Index + "\t" + item.Path + FormatOptionalValue(item.Summary));
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunMapRender(ParsedArgs args)
        {
            if (!args.HasFlag("dry-run"))
            {
                throw new UsageException("map render currently supports --dry-run only. Real screenshot rendering needs a headless MonoGame render target first.");
            }

            string id = args.GetValue("id");
            if (string.IsNullOrEmpty(id))
            {
                throw new UsageException("map render requires --id <map-id>.");
            }

            string output = args.GetValue("out");
            if (string.IsNullOrEmpty(output))
            {
                output = args.GetValue("output");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("map render requires --out <map.png>.");
            }

            string input = args.Positionals.Count > 1 ? args.Positionals[1] : null;
            string layer = args.GetValue("layer");
            if (string.IsNullOrEmpty(layer))
            {
                layer = "all";
            }

            var result = MapRenderPlanDto.Create(
                id,
                input,
                output,
                layer,
                args.HasFlag("include-life"),
                args.HasFlag("include-reactor"),
                args.HasFlag("include-tooltip"));

            bool json = args.HasFlag("json") || args.HasFlag("dry-run");
            WriteOutput(result, json, writer =>
            {
                writer.WriteLine("Map render dry-run");
                writer.WriteLine("Map: " + result.Id);
                writer.WriteLine("Output: " + result.OutputPath);
                writer.WriteLine("Layer: " + result.Layer);
                foreach (string path in result.CandidatePaths)
                {
                    writer.WriteLine("Candidate: " + path);
                }
                foreach (string blocker in result.Blockers)
                {
                    writer.WriteLine("Blocker: " + blocker);
                }
            });

            return ExitSuccess;
        }
    }
}
