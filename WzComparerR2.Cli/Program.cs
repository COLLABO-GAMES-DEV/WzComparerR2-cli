using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal static class Program
    {
        private const int ExitSuccess = 0;
        private const int ExitUsage = 1;
        private const int ExitNotFound = 2;
        private const int ExitLoadFailed = 3;
        private const int ExitInternalError = 5;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0 || IsHelp(args[0]))
                {
                    PrintHelp();
                    return ExitSuccess;
                }

                string command = args[0].ToLowerInvariant();
                var parsed = ParsedArgs.Parse(args.Skip(1));

                switch (command)
                {
                    case "info":
                        return RunInfo(parsed);
                    case "tree":
                        return RunTree(parsed);
                    case "list":
                        return RunList(parsed);
                    case "search":
                        return RunSearch(parsed);
                    case "extract":
                        return RunExtract(parsed);
                    case "version":
                    case "--version":
                        Console.WriteLine("wcr2 cli 0.1.0");
                        return ExitSuccess;
                    default:
                        Console.Error.WriteLine("Unknown command: " + command);
                        Console.Error.WriteLine();
                        PrintHelp();
                        return ExitUsage;
                }
            }
            catch (UsageException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitUsage;
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitNotFound;
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return ExitNotFound;
            }
            catch (WzLoadException ex)
            {
                Console.Error.WriteLine(ex.Message);
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine(ex.InnerException.Message);
                }
                return ExitLoadFailed;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Unexpected error: " + ex.Message);
                return ExitInternalError;
            }
        }

        private static int RunInfo(ParsedArgs args)
        {
            string input = RequireInput(args, "info <file-or-dir>");
            bool json = args.HasFlag("json");

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                var dto = InfoDto.FromContext(context);
                WriteOutput(dto, json, writer =>
                {
                    writer.WriteLine("Input: " + dto.InputPath);
                    writer.WriteLine("Root: " + dto.RootName);
                    writer.WriteLine("Children: " + dto.RootChildren);
                    writer.WriteLine("WZ files: " + dto.WzFileCount);
                    writer.WriteLine("MS files: " + dto.MsFileCount);
                    writer.WriteLine("Images: " + dto.ImageCount);

                    foreach (var file in dto.Files)
                    {
                        writer.WriteLine("- " + file.Name + " type=" + file.Type + " version=" + file.WzVersion + " images=" + file.ImageCount);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunTree(ParsedArgs args)
        {
            string input = RequireInput(args, "tree <file-or-dir>");
            bool json = args.HasFlag("json");
            int depth = args.GetInt("depth", 3);
            int limit = args.GetInt("limit", 500);
            string nodePath = args.GetValue("path");
            bool extractImages = args.HasFlag("extract-images");

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, extractImages);
                var dto = NodeDto.FromNode(node, depth, limit, extractImages);
                WriteOutput(dto, json, writer => WriteTree(writer, dto, 0));
            }

            return ExitSuccess;
        }

        private static int RunList(ParsedArgs args)
        {
            string input = RequireInput(args, "list <file-or-dir>");
            bool json = args.HasFlag("json");
            string nodePath = args.GetValue("path");
            bool extractImages = args.HasFlag("extract-images");

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, extractImages);
                var items = node.Nodes.Select(NodeDto.FromNodeShallow).ToList();
                WriteOutput(items, json, writer =>
                {
                    foreach (var item in items)
                    {
                        writer.WriteLine(item.Name + "\t" + item.Type + "\tchildren=" + item.ChildrenCount + FormatOptionalValue(item.Value));
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunSearch(ParsedArgs args)
        {
            string input = RequireInput(args, "search <file-or-dir> --name <text>|--value <text>");
            bool json = args.HasFlag("json");
            string nameQuery = args.GetValue("name");
            string valueQuery = args.GetValue("value");
            string nodePath = args.GetValue("path");
            bool extractImages = args.HasFlag("extract-images");
            int maxResults = args.GetInt("max-results", 100);

            if (string.IsNullOrEmpty(nameQuery) && string.IsNullOrEmpty(valueQuery))
            {
                throw new UsageException("search requires --name <text> or --value <text>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, extractImages);
                var results = Search(node, nameQuery, valueQuery, maxResults, extractImages);
                WriteOutput(results, json, writer =>
                {
                    foreach (var result in results)
                    {
                        writer.WriteLine(result.Path + "\t" + result.Type + FormatOptionalValue(result.Value));
                    }
                });
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

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Extracted " + result.Files.Count + " file(s) from " + result.SourcePath);
                    foreach (var file in result.Files)
                    {
                        writer.WriteLine("- " + file.Type + "\t" + file.OutputPath);
                    }
                });
            }

            return ExitSuccess;
        }

        private static string RequireInput(ParsedArgs args, string usage)
        {
            if (args.Positionals.Count == 0)
            {
                throw new UsageException("Usage: wcr2 " + usage);
            }
            return args.Positionals[0];
        }

        private static Wz_Node ResolveRequiredNode(Wz_Node root, string path, bool extractImages)
        {
            Wz_Node node = NodePath.Resolve(root, path, extractImages);
            if (node == null)
            {
                throw new UsageException("WZ path not found: " + path);
            }
            return node;
        }

        private static List<SearchResultDto> Search(Wz_Node root, string nameQuery, string valueQuery, int maxResults, bool extractImages)
        {
            var results = new List<SearchResultDto>();
            var stack = new Stack<Wz_Node>();
            stack.Push(root);

            while (stack.Count > 0 && results.Count < maxResults)
            {
                Wz_Node node = stack.Pop();
                node = NodePath.ExtractImageNode(node, extractImages);
                if (node == null)
                {
                    continue;
                }

                string value = NodeDto.FormatValue(node.Value);
                bool nameMatched = !string.IsNullOrEmpty(nameQuery)
                    && node.Text != null
                    && node.Text.IndexOf(nameQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                bool valueMatched = !string.IsNullOrEmpty(valueQuery)
                    && value != null
                    && value.IndexOf(valueQuery, StringComparison.OrdinalIgnoreCase) >= 0;

                if (nameMatched || valueMatched)
                {
                    results.Add(SearchResultDto.FromNode(node));
                }

                var children = node.Nodes.ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }

            return results;
        }

        private static void WriteTree(TextWriter writer, NodeDto node, int indent)
        {
            writer.Write(new string(' ', indent * 2));
            writer.Write(node.Name);
            writer.Write(" [");
            writer.Write(node.Type);
            writer.Write("] children=");
            writer.Write(node.ChildrenCount);
            if (!string.IsNullOrEmpty(node.Value))
            {
                writer.Write(" value=");
                writer.Write(node.Value);
            }
            writer.WriteLine();

            if (node.Children == null)
            {
                return;
            }

            foreach (var child in node.Children)
            {
                WriteTree(writer, child, indent + 1);
            }
        }

        private static void WriteOutput<T>(T value, bool json, Action<TextWriter> writeText)
        {
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
            }
            else
            {
                writeText(Console.Out);
            }
        }

        private static string FormatOptionalValue(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : "\tvalue=" + value;
        }

        private static bool IsHelp(string arg)
        {
            return string.Equals(arg, "help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("wcr2 - WzComparerR2 command line tools");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  wcr2 info <file-or-dir> [--json]");
            Console.WriteLine("  wcr2 tree <file-or-dir> [--path <wz-path>] [--depth <n>] [--limit <n>] [--json]");
            Console.WriteLine("  wcr2 list <file-or-dir> [--path <wz-path>] [--json]");
            Console.WriteLine("  wcr2 search <file-or-dir> --name <text> [--path <wz-path>] [--json]");
            Console.WriteLine("  wcr2 search <file-or-dir> --value <text> [--path <wz-path>] [--json]");
            Console.WriteLine("  wcr2 extract <file-or-dir> --path <wz-path> --out <output-dir> [--recursive] [--json]");
            Console.WriteLine();
            Console.WriteLine("Common options:");
            Console.WriteLine("  --use-base-wz       Load with Base.wz link behavior where supported.");
            Console.WriteLine("  --fallback <path>   Fallback WZ file or folder.");
            Console.WriteLine("  --extract-images    Extract image nodes while traversing.");
            Console.WriteLine("  --json              Emit JSON output.");
            Console.WriteLine("  --format xml        Export selected node as XML instead of loose files.");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  wcr2 info Base.wz");
            Console.WriteLine("  wcr2 tree Base.wz --depth 2");
            Console.WriteLine("  wcr2 list Base.wz --path Character");
            Console.WriteLine("  wcr2 search String.wz --name Maple --json");
            Console.WriteLine("  wcr2 extract Base.wz --path String --out out/string --recursive");
        }
    }

    internal sealed class ParsedArgs
    {
        private readonly Dictionary<string, string> values;
        private readonly HashSet<string> flags;

        private ParsedArgs()
        {
            this.Positionals = new List<string>();
            this.values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            this.flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public List<string> Positionals { get; private set; }

        public static ParsedArgs Parse(IEnumerable<string> args)
        {
            var parsed = new ParsedArgs();
            var list = args.ToList();

            for (int i = 0; i < list.Count; i++)
            {
                string arg = list[i];
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    string key = arg.Substring(2);
                    if (key.Length == 0)
                    {
                        throw new UsageException("Empty option name.");
                    }

                    if (i + 1 < list.Count && !list[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        parsed.values[key] = list[++i];
                    }
                    else
                    {
                        parsed.flags.Add(key);
                    }
                }
                else
                {
                    parsed.Positionals.Add(arg);
                }
            }

            return parsed;
        }

        public string GetValue(string key)
        {
            string value;
            return this.values.TryGetValue(key, out value) ? value : null;
        }

        public bool HasFlag(string key)
        {
            return this.flags.Contains(key) || this.values.ContainsKey(key);
        }

        public int GetInt(string key, int defaultValue)
        {
            string value = GetValue(key);
            if (string.IsNullOrEmpty(value))
            {
                return defaultValue;
            }

            int result;
            if (!int.TryParse(value, out result))
            {
                throw new UsageException("--" + key + " must be an integer.");
            }
            return result;
        }
    }

    internal sealed class WzLoadOptions
    {
        public bool UseBaseWz { get; private set; }
        public string FallbackPath { get; private set; }

        public static WzLoadOptions FromArgs(ParsedArgs args)
        {
            return new WzLoadOptions
            {
                UseBaseWz = args.HasFlag("use-base-wz"),
                FallbackPath = args.GetValue("fallback")
            };
        }
    }

    internal sealed class WzLoadContext : IDisposable
    {
        private WzLoadContext(string inputPath, Wz_Structure structure)
        {
            this.InputPath = inputPath;
            this.Structure = structure;
        }

        public string InputPath { get; private set; }
        public Wz_Structure Structure { get; private set; }
        public Wz_Node Root { get { return this.Structure.WzNode; } }

        public static WzLoadContext Load(string inputPath, WzLoadOptions options)
        {
            if (string.IsNullOrEmpty(inputPath))
            {
                throw new UsageException("Input path is required.");
            }

            string fullPath = Path.GetFullPath(inputPath);
            var structure = new Wz_Structure();

            try
            {
                if (Directory.Exists(fullPath))
                {
                    Wz_Node node = null;
                    structure.LoadWzFolder(fullPath, ref node, options.UseBaseWz, options.FallbackPath);
                    structure.WzNode = node;
                    structure.calculate_img_count();
                }
                else if (File.Exists(fullPath))
                {
                    string ext = Path.GetExtension(fullPath);
                    if (string.Equals(ext, ".img", StringComparison.OrdinalIgnoreCase))
                    {
                        structure.LoadImg(fullPath);
                    }
                    else if (string.Equals(ext, ".ms", StringComparison.OrdinalIgnoreCase))
                    {
                        structure.LoadMsFile(fullPath);
                    }
                    else if (!string.IsNullOrEmpty(options.FallbackPath)
                        && !string.Equals(Path.GetFileName(fullPath), "list.wz", StringComparison.OrdinalIgnoreCase))
                    {
                        structure.WzNode = new Wz_Node(Path.GetFileName(fullPath));
                        structure.LoadFile(fullPath, structure.WzNode, options.UseBaseWz, false, options.FallbackPath);
                        structure.calculate_img_count();
                    }
                    else
                    {
                        structure.Load(fullPath, options.UseBaseWz);
                    }
                }
                else
                {
                    throw new FileNotFoundException("Input path not found: " + inputPath);
                }

                if (structure.WzNode == null)
                {
                    throw new WzLoadException("No root node was loaded from: " + inputPath);
                }

                return new WzLoadContext(fullPath, structure);
            }
            catch (FileNotFoundException)
            {
                structure.Clear();
                throw;
            }
            catch (DirectoryNotFoundException)
            {
                structure.Clear();
                throw;
            }
            catch (Exception ex)
            {
                structure.Clear();
                throw new WzLoadException("Failed to load input: " + inputPath, ex);
            }
        }

        public void Dispose()
        {
            this.Structure.Clear();
        }
    }

    internal static class NodePath
    {
        public static Wz_Node Resolve(Wz_Node root, string path, bool extractImages)
        {
            if (root == null)
            {
                return null;
            }

            Wz_Node current = ExtractImageNode(root, extractImages);
            if (string.IsNullOrWhiteSpace(path))
            {
                return current;
            }

            string[] parts = path
                .Replace('/', '\\')
                .Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

            int start = 0;
            if (parts.Length > 0 && string.Equals(parts[0], current.Text, StringComparison.OrdinalIgnoreCase))
            {
                start = 1;
            }

            for (int i = start; i < parts.Length; i++)
            {
                current = ExtractImageNode(current, extractImages);
                if (current == null)
                {
                    return null;
                }

                Wz_Node child = current.Nodes[parts[i]];
                if (child == null)
                {
                    foreach (Wz_Node candidate in current.Nodes)
                    {
                        if (string.Equals(candidate.Text, parts[i], StringComparison.OrdinalIgnoreCase))
                        {
                            child = candidate;
                            break;
                        }
                    }
                }

                if (child == null)
                {
                    return null;
                }

                current = child;
            }

            return ExtractImageNode(current, extractImages);
        }

        public static Wz_Node ExtractImageNode(Wz_Node node, bool extractImages)
        {
            if (!extractImages || node == null)
            {
                return node;
            }

            Wz_Image image = node.GetValue<Wz_Image>();
            if (image != null && image.TryExtract())
            {
                return image.Node;
            }

            return node;
        }
    }

    internal sealed class InfoDto
    {
        public string InputPath { get; set; }
        public string RootName { get; set; }
        public int RootChildren { get; set; }
        public int WzFileCount { get; set; }
        public int MsFileCount { get; set; }
        public int ImageCount { get; set; }
        public List<FileDto> Files { get; set; }

        public static InfoDto FromContext(WzLoadContext context)
        {
            return new InfoDto
            {
                InputPath = context.InputPath,
                RootName = context.Root.Text,
                RootChildren = context.Root.Nodes.Count,
                WzFileCount = context.Structure.wz_files.Count,
                MsFileCount = context.Structure.ms_files.Count,
                ImageCount = context.Structure.img_number,
                Files = context.Structure.wz_files.Select(FileDto.FromWzFile).ToList()
            };
        }
    }

    internal sealed class FileDto
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public int ImageCount { get; set; }
        public int WzVersion { get; set; }
        public long FileSize { get; set; }

        public static FileDto FromWzFile(Wz_File file)
        {
            return new FileDto
            {
                Name = Path.GetFileName(file.Header.FileName),
                Type = file.Type.ToString(),
                ImageCount = file.ImageCount,
                WzVersion = file.Header.WzVersion,
                FileSize = file.Header.FileSize
            };
        }
    }

    internal sealed class NodeDto
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public int ChildrenCount { get; set; }
        public List<NodeDto> Children { get; set; }

        public static NodeDto FromNodeShallow(Wz_Node node)
        {
            return new NodeDto
            {
                Name = node.Text,
                Path = node.FullPath,
                Type = GetTypeName(node.Value),
                Value = FormatValue(node.Value),
                ChildrenCount = node.Nodes.Count
            };
        }

        public static NodeDto FromNode(Wz_Node node, int depth, int limit, bool extractImages)
        {
            int remaining = limit <= 0 ? int.MaxValue : limit;
            return FromNodeCore(node, depth, extractImages, ref remaining);
        }

        private static NodeDto FromNodeCore(Wz_Node node, int depth, bool extractImages, ref int remaining)
        {
            node = NodePath.ExtractImageNode(node, extractImages);
            remaining--;

            var dto = FromNodeShallow(node);
            if (depth > 0 && remaining > 0)
            {
                dto.Children = new List<NodeDto>();
                foreach (Wz_Node child in node.Nodes)
                {
                    if (remaining <= 0)
                    {
                        break;
                    }
                    dto.Children.Add(FromNodeCore(child, depth - 1, extractImages, ref remaining));
                }
            }

            return dto;
        }

        public static string GetTypeName(object value)
        {
            if (value == null)
            {
                return "dir";
            }
            if (value is Wz_File)
            {
                return "wz-file";
            }
            if (value is Wz_Image)
            {
                return "image";
            }
            if (value is Wz_Png)
            {
                return "png";
            }
            if (value is Wz_Sound)
            {
                return "sound";
            }
            if (value is Wz_Uol)
            {
                return "uol";
            }
            if (value is Wz_Vector)
            {
                return "vector";
            }
            if (value is Wz_RawData)
            {
                return "raw";
            }
            return value.GetType().Name;
        }

        public static string FormatValue(object value)
        {
            if (value == null || value is Wz_File)
            {
                return null;
            }

            var image = value as Wz_Image;
            if (image != null)
            {
                return "size=" + image.Size;
            }

            var png = value as Wz_Png;
            if (png != null)
            {
                return png.Width + "x" + png.Height + " format=" + png.Format + " pages=" + png.ActualPages;
            }

            var sound = value as Wz_Sound;
            if (sound != null)
            {
                return "length=" + sound.DataLength + " ms=" + sound.Ms + " channels=" + sound.Channels + " frequency=" + sound.Frequency;
            }

            var uol = value as Wz_Uol;
            if (uol != null)
            {
                return uol.Uol;
            }

            var vector = value as Wz_Vector;
            if (vector != null)
            {
                return vector.X + "," + vector.Y;
            }

            var raw = value as Wz_RawData;
            if (raw != null)
            {
                return "length=" + raw.Length;
            }

            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    internal sealed class SearchResultDto
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }

        public static SearchResultDto FromNode(Wz_Node node)
        {
            return new SearchResultDto
            {
                Name = node.Text,
                Path = node.FullPath,
                Type = NodeDto.GetTypeName(node.Value),
                Value = NodeDto.FormatValue(node.Value)
            };
        }
    }

    internal static class ExtractExporter
    {
        public static List<ExtractedFileDto> ExportAuto(Wz_Node node, string outputDirectory, bool recursive)
        {
            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);

            var files = new List<ExtractedFileDto>();
            if (recursive)
            {
                foreach (var current in Traverse(node))
                {
                    ExportSingle(current, fullOutputDirectory, node, files);
                }
            }
            else
            {
                ExportSingle(node, fullOutputDirectory, node, files);
            }

            return files;
        }

        public static ExtractedFileDto ExportXml(Wz_Node node, string output)
        {
            string fullOutput = Path.GetFullPath(output);
            if (Directory.Exists(fullOutput) || string.IsNullOrEmpty(Path.GetExtension(fullOutput)))
            {
                Directory.CreateDirectory(fullOutput);
                fullOutput = Path.Combine(fullOutput, SanitizeFileName(node.Text) + ".xml");
            }
            else
            {
                string directory = Path.GetDirectoryName(fullOutput);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
            }

            var settings = new XmlWriterSettings
            {
                Indent = true
            };
            using (var writer = XmlWriter.Create(fullOutput, settings))
            {
                node.DumpAsXml(writer);
            }

            return new ExtractedFileDto
            {
                SourcePath = node.FullPath,
                OutputPath = fullOutput,
                Type = "xml",
                Bytes = new FileInfo(fullOutput).Length
            };
        }

        private static IEnumerable<Wz_Node> Traverse(Wz_Node root)
        {
            var stack = new Stack<Wz_Node>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                Wz_Node node = NodePath.ExtractImageNode(stack.Pop(), true);
                if (node == null)
                {
                    continue;
                }

                yield return node;

                var children = node.Nodes.ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    stack.Push(children[i]);
                }
            }
        }

        private static void ExportSingle(Wz_Node node, string outputDirectory, Wz_Node root, List<ExtractedFileDto> files)
        {
            node = NodePath.ExtractImageNode(node, true);
            if (node == null || node.Value == null || node.Value is Wz_File || node.Value is Wz_Image)
            {
                return;
            }

            if (node.Value is Wz_Png)
            {
                ExportPng(node, outputDirectory, root, files);
            }
            else if (node.Value is Wz_Sound)
            {
                ExportSound(node, outputDirectory, root, files);
            }
            else if (node.Value is Wz_RawData)
            {
                ExportBlob(node, outputDirectory, root, "raw", ".bin", files);
            }
            else if (node.Value is Wz_Video)
            {
                ExportBlob(node, outputDirectory, root, "video", ".mcv", files);
            }
            else
            {
                ExportText(node, outputDirectory, root, files);
            }
        }

        private static void ExportPng(Wz_Node node, string outputDirectory, Wz_Node root, List<ExtractedFileDto> files)
        {
            var png = (Wz_Png)node.Value;
            for (int page = 0; page < png.ActualPages; page++)
            {
                string suffix = png.ActualPages > 1 ? ".p" + (page + 1) : string.Empty;
                string path = GetOutputPath(outputDirectory, root, node, suffix + ".png");
                EnsureParentDirectory(path);

                using (var bitmap = png.ExtractPng(page))
                {
                    bitmap.Save(path, ImageFormat.Png);
                }

                files.Add(CreateFileDto(node, path, "png"));
            }
        }

        private static void ExportSound(Wz_Node node, string outputDirectory, Wz_Node root, List<ExtractedFileDto> files)
        {
            var sound = (Wz_Sound)node.Value;
            byte[] data = sound.ExtractSound();
            string type = sound.SoundType.ToString().ToLowerInvariant();
            string extension = GetSoundExtension(sound.SoundType);

            if (data == null)
            {
                data = new byte[sound.DataLength];
                sound.CopyTo(data, 0);
                type = "sound";
            }

            string path = GetOutputPath(outputDirectory, root, node, extension);
            EnsureParentDirectory(path);
            File.WriteAllBytes(path, data);
            files.Add(CreateFileDto(node, path, type));
        }

        private static void ExportBlob(Wz_Node node, string outputDirectory, Wz_Node root, string type, string extension, List<ExtractedFileDto> files)
        {
            byte[] data;
            if (node.Value is Wz_RawData)
            {
                var rawData = (Wz_RawData)node.Value;
                data = new byte[rawData.Length];
                rawData.CopyTo(data, 0);
            }
            else
            {
                var video = (Wz_Video)node.Value;
                data = new byte[video.Length];
                video.CopyTo(data, 0);
            }

            string path = GetOutputPath(outputDirectory, root, node, extension);
            EnsureParentDirectory(path);
            File.WriteAllBytes(path, data);
            files.Add(CreateFileDto(node, path, type));
        }

        private static void ExportText(Wz_Node node, string outputDirectory, Wz_Node root, List<ExtractedFileDto> files)
        {
            string value = NodeDto.FormatValue(node.Value) ?? Convert.ToString(node.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
            string path = GetOutputPath(outputDirectory, root, node, ".txt");
            EnsureParentDirectory(path);
            File.WriteAllText(path, value);
            files.Add(CreateFileDto(node, path, NodeDto.GetTypeName(node.Value)));
        }

        private static string GetSoundExtension(Wz_SoundType type)
        {
            switch (type)
            {
                case Wz_SoundType.Mp3:
                    return ".mp3";
                case Wz_SoundType.Pcm:
                    return ".wav";
                default:
                    return ".bin";
            }
        }

        private static string GetOutputPath(string outputDirectory, Wz_Node root, Wz_Node node, string extension)
        {
            var segments = GetRelativeSegments(root, node);
            if (segments.Count == 0)
            {
                segments.Add(SanitizeFileName(node.Text));
            }

            segments[segments.Count - 1] = segments[segments.Count - 1] + extension;
            return Path.Combine(new[] { outputDirectory }.Concat(segments).ToArray());
        }

        private static List<string> GetRelativeSegments(Wz_Node root, Wz_Node node)
        {
            var segments = new List<string>();
            Wz_Node current = node;
            while (current != null && current != root)
            {
                segments.Add(SanitizeFileName(current.Text));
                current = current.ParentNode;
            }

            segments.Reverse();
            return segments;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "_";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            return new string(chars);
        }

        private static void EnsureParentDirectory(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static ExtractedFileDto CreateFileDto(Wz_Node node, string path, string type)
        {
            return new ExtractedFileDto
            {
                SourcePath = node.FullPath,
                OutputPath = path,
                Type = type,
                Bytes = new FileInfo(path).Length
            };
        }
    }

    internal sealed class ExtractResultDto
    {
        public string InputPath { get; set; }
        public string SourcePath { get; set; }
        public List<ExtractedFileDto> Files { get; set; }

        public static ExtractResultDto Create(string inputPath, Wz_Node node)
        {
            return new ExtractResultDto
            {
                InputPath = Path.GetFullPath(inputPath),
                SourcePath = node.FullPath,
                Files = new List<ExtractedFileDto>()
            };
        }
    }

    internal sealed class ExtractedFileDto
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public string Type { get; set; }
        public long Bytes { get; set; }
    }

    internal sealed class UsageException : Exception
    {
        public UsageException(string message)
            : base(message)
        {
        }
    }

    internal sealed class WzLoadException : Exception
    {
        public WzLoadException(string message)
            : base(message)
        {
        }

        public WzLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
