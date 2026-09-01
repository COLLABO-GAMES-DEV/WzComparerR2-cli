using System;
using System.IO;
using System.Text.Json;
using System.Xml;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
        private static int RunDump(ParsedArgs args)
        {
            string input = RequireInput(args, "dump <file-or-dir> --path <wz-path> [--format json|xml|raw]");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            string format = args.GetValue("format") ?? "json";
            int depth = args.GetInt("depth", 10);
            int limit = args.GetInt("limit", 5000);

            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("dump requires --path <wz-path>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                {
                    string json = JsonSerializer.Serialize(NodeDto.FromNode(node, depth, limit, true), JsonOptions);
                    WriteDumpOutput(json, output);
                }
                else if (string.Equals(format, "xml", StringComparison.OrdinalIgnoreCase))
                {
                    string xml = DumpXmlToString(node);
                    WriteDumpOutput(xml, output);
                }
                else if (string.Equals(format, "raw", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(output))
                    {
                        throw new UsageException("dump --format raw requires --out <output-dir>.");
                    }

                    var result = ExtractResultDto.Create(input, node);
                    result.Files.AddRange(ExtractExporter.ExportAuto(node, output, false));
                    if (result.Files.Count == 0)
                    {
                        throw new UsageException("Selected node has no raw exportable value.");
                    }

                    WriteOutput(result, args.HasFlag("json"), writer =>
                    {
                        foreach (var file in result.Files)
                        {
                            writer.WriteLine(file.OutputPath);
                        }
                    });
                }
                else
                {
                    throw new UsageException("Unsupported dump format: " + format);
                }
            }

            return ExitSuccess;
        }

        private static int RunExtract(ParsedArgs args)
        {
            string input = RequireInput(args, "extract <file-or-dir> --path <wz-path> --out <output-dir>");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            bool recursive = args.HasFlag("recursive");
            bool json = args.HasFlag("json");
            string format = args.GetValue("format");
            string manifest = args.GetValue("manifest");

            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("extract requires --path <wz-path>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("extract requires --out <output-dir>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                var result = ExtractResultDto.Create(input, node);

                if (string.Equals(format, "xml", StringComparison.OrdinalIgnoreCase) || args.HasFlag("xml"))
                {
                    result.Files.Add(ExtractExporter.ExportXml(node, output));
                }
                else
                {
                    result.Files.AddRange(ExtractExporter.ExportAuto(node, output, recursive));
                    if (result.Files.Count == 0)
                    {
                        throw new UsageException("Selected node has no exportable value. Use --recursive for containers.");
                    }
                }

                if (!string.IsNullOrEmpty(manifest))
                {
                    result.ManifestPath = ExtractExporter.WriteManifest(result, manifest);
                }

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Extracted " + result.Files.Count + " file(s) from " + result.SourcePath);
                    if (!string.IsNullOrEmpty(result.ManifestPath))
                    {
                        writer.WriteLine("Manifest: " + result.ManifestPath);
                    }
                    foreach (var file in result.Files)
                    {
                        writer.WriteLine("- " + file.Type + "\t" + file.OutputPath);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunMedia(ParsedArgs args, string kind)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintMediaHelp(kind);
                return ExitSuccess;
            }

            string action = args.Positionals[0].ToLowerInvariant();
            if (string.Equals(action, "list", StringComparison.OrdinalIgnoreCase))
            {
                return RunMediaList(args, kind);
            }
            if (string.Equals(action, "export", StringComparison.OrdinalIgnoreCase))
            {
                return RunMediaExport(args, kind, false);
            }
            if (string.Equals(action, "export-all", StringComparison.OrdinalIgnoreCase))
            {
                return RunMediaExport(args, kind, true);
            }

            throw new UsageException("Unknown " + kind + " command: " + action);
        }

        private static int RunMediaList(ParsedArgs args, string kind)
        {
            string input = RequireInputAt(args, 1, kind + " list <file-or-dir> [--path <wz-path>] [--max-results <n>]");
            string nodePath = args.GetValue("path");
            int maxResults = args.GetInt("max-results", 100);
            bool json = args.HasFlag("json");

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node root = ResolveRequiredNode(context.Root, nodePath, true);
                var result = MediaListResultDto.Create(input, root, kind, maxResults);
                result.Assets.AddRange(MediaAssetFinder.Find(root, kind, maxResults, out bool truncated));
                result.Truncated = truncated;

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine(kind + " assets: " + result.Assets.Count + (result.Truncated ? " (truncated)" : string.Empty));
                    foreach (var asset in result.Assets)
                    {
                        writer.WriteLine(asset.Path + "\t" + asset.Type + FormatOptionalValue(asset.Value));
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunMediaExport(ParsedArgs args, string kind, bool recursive)
        {
            string usage = kind + " " + (recursive ? "export-all" : "export") + " <file-or-dir> --path <wz-path> --out <output-dir>";
            string input = RequireInputAt(args, 1, usage);
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            string manifest = args.GetValue("manifest");
            bool json = args.HasFlag("json");

            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException(kind + " " + (recursive ? "export-all" : "export") + " requires --path <wz-path>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException(kind + " " + (recursive ? "export-all" : "export") + " requires --out <output-dir>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                var result = ExtractResultDto.Create(input, node);

                try
                {
                    if (string.Equals(kind, "video", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Files.AddRange(VideoExporter.Export(node, output, recursive, VideoExportOptions.FromArgs(args)));
                    }
                    else
                    {
                        result.Files.AddRange(ExtractExporter.ExportMedia(node, output, recursive, kind));
                    }
                }
                catch (NotSupportedException ex) when (string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase))
                {
                    throw new UsageException(ex.Message);
                }
                catch (Exception ex) when (string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase) && IsLikelySystemDrawingFailure(ex))
                {
                    throw new UsageException("PNG export failed through System.Drawing/GDI+. On macOS/Linux the CLI cross-platform PNG writer is used for common WZ texture formats. Original error: " + GetInnermostMessage(ex));
                }

                if (result.Files.Count == 0)
                {
                    string hint = recursive
                        ? "Selected node contains no " + kind + " assets."
                        : "Selected node is not a " + kind + " asset. Use " + kind + " export-all for containers.";
                    throw new UsageException(hint);
                }

                if (!string.IsNullOrEmpty(manifest))
                {
                    result.ManifestPath = ExtractExporter.WriteManifest(result, manifest);
                }

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Exported " + result.Files.Count + " " + kind + " file(s) from " + result.SourcePath);
                    if (!string.IsNullOrEmpty(result.ManifestPath))
                    {
                        writer.WriteLine("Manifest: " + result.ManifestPath);
                    }
                    foreach (var file in result.Files)
                    {
                        writer.WriteLine("- " + file.Type + "\t" + file.OutputPath);
                    }
                });
            }

            return ExitSuccess;
        }

        private static bool IsLikelySystemDrawingFailure(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                string typeName = current.GetType().FullName ?? string.Empty;
                string message = current.Message ?? string.Empty;
                if (typeName.IndexOf("System.Drawing", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("System.Drawing", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("GDI+", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("libgdiplus", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
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

        private static void WriteDumpOutput(string content, string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                Console.WriteLine(content);
                return;
            }

            string fullPath = Path.GetFullPath(output);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(fullPath, content);
            Console.WriteLine(fullPath);
        }

        private static string DumpXmlToString(Wz_Node node)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true
            };
            using (var writer = new StringWriter())
            {
                using (var xmlWriter = XmlWriter.Create(writer, settings))
                {
                    node.DumpAsXml(xmlWriter);
                }
                return writer.ToString();
            }
        }

    }
}
