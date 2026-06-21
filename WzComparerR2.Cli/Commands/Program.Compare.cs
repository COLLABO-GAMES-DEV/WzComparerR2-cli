using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using WzComparerR2.WzLib;


namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
        private static int RunCompare(ParsedArgs args)
        {
            if (args.Positionals.Count < 2)
            {
                throw new UsageException("Usage: wcr2 compare <old-file-or-dir> <new-file-or-dir> [--path <wz-path>] [--type added|removed|changed] [--out <json>] [--json]");
            }

            string oldInput = args.Positionals[0];
            string newInput = args.Positionals[1];
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            string format = args.GetValue("format");
            bool json = args.HasFlag("json");
            bool extractImages = args.HasFlag("extract-images");
            var options = CompareOptions.FromArgs(args);
            string outputFormat = ResolveCompareOutputFormat(format, output);

            using (var oldContext = WzLoadContext.Load(oldInput, WzLoadOptions.FromArgs(args)))
            using (var newContext = WzLoadContext.Load(newInput, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node oldNode = NodePath.Resolve(oldContext.Root, nodePath, extractImages);
                Wz_Node newNode = NodePath.Resolve(newContext.Root, nodePath, extractImages);

                if (oldNode == null && newNode == null)
                {
                    throw new UsageException("WZ path not found in either input: " + nodePath);
                }

                var result = CompareResultDto.Create(oldContext.InputPath, newContext.InputPath, nodePath);
                result.MaxResults = options.MaxResults;
                result.IgnoreImageBinary = options.IgnoreImageBinary;
                CompareNodes(oldNode, newNode, options, result, extractImages);

                if (!string.IsNullOrEmpty(output))
                {
                    result.OutputPath = WriteCompareFile(result, output, outputFormat);
                }

                if (string.IsNullOrEmpty(output) && IsMarkdownFormat(outputFormat))
                {
                    Console.WriteLine(FormatCompareMarkdown(result));
                    return ExitSuccess;
                }

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Compared: " + result.OldInputPath);
                    writer.WriteLine("With: " + result.NewInputPath);
                    writer.WriteLine("Added: " + result.Added + " Removed: " + result.Removed + " Changed: " + result.Changed);
                    if (!string.IsNullOrEmpty(result.OutputPath))
                    {
                        writer.WriteLine("Output: " + result.OutputPath);
                    }
                    if (result.Truncated)
                    {
                        writer.WriteLine("Results truncated at " + result.MaxResults + " item(s).");
                    }
                    foreach (var diff in result.Differences)
                    {
                        writer.WriteLine(diff.ChangeType + "\t" + diff.Path + "\t" + diff.OldType + " -> " + diff.NewType);
                    }
                });
            }

            return ExitSuccess;
        }

        private static string WriteJsonFile<T>(T value, string output)
        {
            string fullPath = Path.GetFullPath(output);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, JsonSerializer.Serialize(value, JsonOptions));
            return fullPath;
        }

        private static string WriteCompareFile(CompareResultDto result, string output, string format)
        {
            string fullPath = Path.GetFullPath(output);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string content = IsMarkdownFormat(format)
                ? FormatCompareMarkdown(result)
                : JsonSerializer.Serialize(result, JsonOptions);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        private static string ResolveCompareOutputFormat(string format, string output)
        {
            if (!string.IsNullOrEmpty(format))
            {
                if (IsMarkdownFormat(format) || string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                {
                    return format;
                }
                throw new UsageException("Unsupported compare format: " + format);
            }

            string ext = string.IsNullOrEmpty(output) ? null : Path.GetExtension(output);
            return string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ext, ".markdown", StringComparison.OrdinalIgnoreCase)
                ? "markdown"
                : "json";
        }

        private static bool IsMarkdownFormat(string format)
        {
            return string.Equals(format, "md", StringComparison.OrdinalIgnoreCase)
                || string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatCompareMarkdown(CompareResultDto result)
        {
            var writer = new System.Text.StringBuilder();
            writer.AppendLine("# WzComparerR2 Compare Report");
            writer.AppendLine();
            writer.AppendLine("- Old: `" + EscapeMarkdownInline(result.OldInputPath) + "`");
            writer.AppendLine("- New: `" + EscapeMarkdownInline(result.NewInputPath) + "`");
            if (!string.IsNullOrEmpty(result.Path))
            {
                writer.AppendLine("- Path: `" + EscapeMarkdownInline(result.Path) + "`");
            }
            writer.AppendLine("- Added: " + result.Added);
            writer.AppendLine("- Removed: " + result.Removed);
            writer.AppendLine("- Changed: " + result.Changed);
            writer.AppendLine("- Truncated: " + result.Truncated.ToString().ToLowerInvariant());
            writer.AppendLine();
            writer.AppendLine("| Change | Path | Old | New |");
            writer.AppendLine("| --- | --- | --- | --- |");

            foreach (var diff in result.Differences)
            {
                writer.Append("| ");
                writer.Append(EscapeMarkdownCell(diff.ChangeType));
                writer.Append(" | `");
                writer.Append(EscapeMarkdownInline(diff.Path));
                writer.Append("` | ");
                writer.Append(EscapeMarkdownCell(FormatCompareSide(diff.OldType, diff.OldValue)));
                writer.Append(" | ");
                writer.Append(EscapeMarkdownCell(FormatCompareSide(diff.NewType, diff.NewValue)));
                writer.AppendLine(" |");
            }

            return writer.ToString();
        }

        private static string FormatCompareSide(string type, string value)
        {
            if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            return string.IsNullOrEmpty(value) ? type : type + " " + value;
        }

        private static string EscapeMarkdownInline(string value)
        {
            return (value ?? string.Empty).Replace("`", "\\`");
        }

        private static string EscapeMarkdownCell(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }

        private static void CompareNodes(Wz_Node oldNode, Wz_Node newNode, CompareOptions options, CompareResultDto result, bool extractImages)
        {
            var stack = new Stack<NodePair>();
            stack.Push(new NodePair(oldNode, newNode));

            while (stack.Count > 0)
            {
                NodePair pair = stack.Pop();
                Wz_Node left = NodePath.ExtractImageNode(pair.OldNode, extractImages);
                Wz_Node right = NodePath.ExtractImageNode(pair.NewNode, extractImages);

                CompareChangeDto diff = CompareChangeDto.FromNodes(left, right);
                if (diff != null)
                {
                    result.Count(diff.ChangeType);
                    if (options.Includes(diff.ChangeType))
                    {
                        if (result.Differences.Count >= options.MaxResults)
                        {
                            result.Truncated = true;
                            continue;
                        }
                        result.Differences.Add(diff);
                    }
                }

                if (result.Truncated)
                {
                    continue;
                }

                foreach (var childPair in EnumerateChildPairs(left, right).Reverse())
                {
                    stack.Push(childPair);
                }
            }
        }

        private static IEnumerable<NodePair> EnumerateChildPairs(Wz_Node oldNode, Wz_Node newNode)
        {
            var oldChildren = BuildNodeMap(oldNode);
            var newChildren = BuildNodeMap(newNode);
            var names = new SortedSet<string>(oldChildren.Keys, StringComparer.OrdinalIgnoreCase);
            names.UnionWith(newChildren.Keys);

            foreach (string name in names)
            {
                Wz_Node oldChild;
                Wz_Node newChild;
                oldChildren.TryGetValue(name, out oldChild);
                newChildren.TryGetValue(name, out newChild);
                yield return new NodePair(oldChild, newChild);
            }
        }

        private static Dictionary<string, Wz_Node> BuildNodeMap(Wz_Node node)
        {
            var map = new Dictionary<string, Wz_Node>(StringComparer.OrdinalIgnoreCase);
            if (node == null)
            {
                return map;
            }

            foreach (Wz_Node child in node.Nodes)
            {
                if (!map.ContainsKey(child.Text))
                {
                    map.Add(child.Text, child);
                }
            }
            return map;
        }

    }
}
