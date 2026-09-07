using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using WzComparerR2.Headless.Wz;
using WzComparerR2.WzLib;

namespace WzComparerR2.Headless.Media
{
    public sealed class RelatedImageExportOptions
    {
        public string OutputDirectory { get; set; }
        public string ManifestPath { get; set; }
        public int ParentDepth { get; set; }
        public int MaxFiles { get; set; }
        public HeadlessWzLoadOptions LoadOptions { get; set; }
    }

    public sealed class RelatedImageExportResultDto
    {
        public string OutputDirectory { get; set; }
        public string ManifestPath { get; set; }
        public int ParentDepth { get; set; }
        public int MaxFiles { get; set; }
        public int SourceMatchCount { get; set; }
        public int Count { get { return this.Files == null ? 0 : this.Files.Count; } }
        public bool Truncated { get; set; }
        public List<RelatedImageExportFileDto> Files { get; set; }
        public List<string> Errors { get; set; }
    }

    public sealed class RelatedImageExportFileDto
    {
        public string SourceInputPath { get; set; }
        public string SourceMatchPath { get; set; }
        public string ExportRootPath { get; set; }
        public string Path { get; set; }
        public int? Page { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; }
        public string OutputPath { get; set; }
        public long Bytes { get; set; }
        public string Sha256 { get; set; }
    }

    public static class RelatedImageExporter
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static RelatedImageExportResultDto Export(IReadOnlyList<ImageSearchMatchDto> matches, RelatedImageExportOptions options)
        {
            if (matches == null)
            {
                throw new ArgumentNullException(nameof(matches));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                throw new ArgumentException("Related image export requires OutputDirectory.", nameof(options));
            }

            int parentDepth = Math.Max(1, options.ParentDepth);
            int maxFiles = options.MaxFiles <= 0 ? 100 : options.MaxFiles;
            string outputDirectory = Path.GetFullPath(options.OutputDirectory);
            Directory.CreateDirectory(outputDirectory);

            var result = new RelatedImageExportResultDto
            {
                OutputDirectory = outputDirectory,
                ManifestPath = string.IsNullOrWhiteSpace(options.ManifestPath) ? null : Path.GetFullPath(options.ManifestPath),
                ParentDepth = parentDepth,
                MaxFiles = maxFiles,
                SourceMatchCount = matches.Count,
                Files = new List<RelatedImageExportFileDto>(),
                Errors = new List<string>()
            };

            var loadedContexts = new Dictionary<string, HeadlessWzLoadContext>(StringComparer.OrdinalIgnoreCase);
            var exported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (ImageSearchMatchDto match in matches)
                {
                    if (result.Files.Count >= maxFiles)
                    {
                        result.Truncated = true;
                        break;
                    }

                    ExportRelatedMatch(match, parentDepth, maxFiles, options.LoadOptions, outputDirectory, loadedContexts, exported, result);
                }
            }
            finally
            {
                foreach (HeadlessWzLoadContext context in loadedContexts.Values)
                {
                    context.Dispose();
                }
            }

            if (!string.IsNullOrWhiteSpace(result.ManifestPath))
            {
                string directory = Path.GetDirectoryName(result.ManifestPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(result.ManifestPath, JsonSerializer.Serialize(result, JsonOptions));
            }

            return result;
        }

        private static void ExportRelatedMatch(
            ImageSearchMatchDto match,
            int parentDepth,
            int maxFiles,
            HeadlessWzLoadOptions loadOptions,
            string outputDirectory,
            Dictionary<string, HeadlessWzLoadContext> loadedContexts,
            HashSet<string> exported,
            RelatedImageExportResultDto result)
        {
            if (match == null || string.IsNullOrWhiteSpace(match.SourceInputPath) || string.IsNullOrWhiteSpace(match.Path))
            {
                return;
            }

            string exportRootPath = GetAncestorPath(match.Path, parentDepth);
            if (string.IsNullOrWhiteSpace(exportRootPath))
            {
                return;
            }

            try
            {
                HeadlessWzLoadContext context = GetContext(match.SourceInputPath, loadOptions, loadedContexts);
                Wz_Node root = HeadlessNodePath.Resolve(context.Root, exportRootPath, true);
                if (root == null)
                {
                    result.Errors.Add("WZ path not found: " + exportRootPath);
                    return;
                }

                string groupDirectory = Path.Combine(outputDirectory, SanitizeFileName(exportRootPath));
                Directory.CreateDirectory(groupDirectory);

                foreach (Wz_Node node in Traverse(root))
                {
                    Wz_Node imageNode = HeadlessNodePath.ExtractImageNode(node, true);
                    var png = imageNode == null ? null : imageNode.Value as Wz_Png;
                    if (png == null)
                    {
                        continue;
                    }

                    int pages = Math.Max(1, png.ActualPages);
                    for (int page = 0; page < pages; page++)
                    {
                        if (result.Files.Count >= maxFiles)
                        {
                            result.Truncated = true;
                            return;
                        }

                        string key = match.SourceInputPath + "\n" + imageNode.FullPath + "\n" + page.ToString(CultureInfo.InvariantCulture);
                        if (!exported.Add(key))
                        {
                            continue;
                        }

                        string fileName = result.Files.Count.ToString("D4", CultureInfo.InvariantCulture)
                            + "_" + SanitizeFileName(imageNode.FullPath);
                        if (pages > 1)
                        {
                            fileName += ".p" + (page + 1).ToString(CultureInfo.InvariantCulture);
                        }
                        fileName += ".png";

                        string outputPath = Path.Combine(groupDirectory, fileName);
                        CrossPlatformPngWriter.Save(png, page, outputPath);
                        var info = new FileInfo(outputPath);
                        result.Files.Add(new RelatedImageExportFileDto
                        {
                            SourceInputPath = Path.GetFullPath(match.SourceInputPath),
                            SourceMatchPath = match.Path,
                            ExportRootPath = exportRootPath,
                            Path = imageNode.FullPath,
                            Page = pages > 1 ? page : (int?)null,
                            Width = png.Width,
                            Height = png.Height,
                            Format = png.Format.ToString(),
                            OutputPath = outputPath,
                            Bytes = info.Length,
                            Sha256 = ComputeSha256(outputPath)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add(GetInnermostMessage(ex));
            }
        }

        private static HeadlessWzLoadContext GetContext(string inputPath, HeadlessWzLoadOptions loadOptions, Dictionary<string, HeadlessWzLoadContext> loadedContexts)
        {
            string fullPath = Path.GetFullPath(inputPath);
            HeadlessWzLoadContext context;
            if (!loadedContexts.TryGetValue(fullPath, out context))
            {
                context = HeadlessWzLoadContext.Load(fullPath, loadOptions ?? new HeadlessWzLoadOptions());
                loadedContexts.Add(fullPath, context);
            }

            return context;
        }

        private static IEnumerable<Wz_Node> Traverse(Wz_Node root)
        {
            if (root == null)
            {
                yield break;
            }

            var stack = new Stack<Wz_Node>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                Wz_Node node = HeadlessNodePath.ExtractImageNode(stack.Pop(), true);
                if (node == null)
                {
                    continue;
                }

                yield return node;

                for (int i = node.Nodes.Count - 1; i >= 0; i--)
                {
                    stack.Push(node.Nodes[i]);
                }
            }
        }

        private static string GetAncestorPath(string path, int parentDepth)
        {
            string current = path;
            for (int i = 0; i < parentDepth; i++)
            {
                current = GetParentPath(current);
                if (string.IsNullOrWhiteSpace(current))
                {
                    return null;
                }
            }

            return current;
        }

        private static string GetParentPath(string path)
        {
            int index = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/'));
            return index <= 0 ? null : path.Substring(0, index);
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = new char[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                chars[i] = invalid.Contains(ch) || ch == '\\' || ch == '/' || ch == ':' ? '_' : ch;
            }

            string sanitized = new string(chars);
            return sanitized.Length > 180 ? sanitized.Substring(sanitized.Length - 180) : sanitized;
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
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
    }
}
