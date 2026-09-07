using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WzComparerR2.WzLib;

namespace WzComparerR2.Headless
{
    internal sealed class SearchOptions
    {
        private Regex pathRegex;

        public string NameQuery { get; set; }
        public string ValueQuery { get; set; }
        public string PathQuery { get; private set; }
        public string Type { get; private set; }
        public int MaxResults { get; private set; }
        public bool ExtractImages { get; set; }

        public static SearchOptions FromArgs(ParsedArgs args)
        {
            var options = new SearchOptions
            {
                PathQuery = args.GetValue("match-path"),
                Type = args.GetValue("type"),
                MaxResults = args.GetInt("max-results", 100)
            };

            if (!string.IsNullOrEmpty(options.PathQuery))
            {
                string pattern = args.HasFlag("regex")
                    ? options.PathQuery
                    : GlobToRegexPattern(options.PathQuery);
                try
                {
                    options.pathRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException ex)
                {
                    throw new UsageException("Invalid --match-path pattern: " + ex.Message);
                }
            }

            return options;
        }

        public bool Matches(Wz_Node node)
        {
            if (!string.IsNullOrEmpty(this.Type) && !TypeMatches(node, this.Type))
            {
                return false;
            }

            string value = NodeDto.FormatValue(node.Value);
            bool nameMatched = !string.IsNullOrEmpty(this.NameQuery)
                && node.Text != null
                && node.Text.IndexOf(this.NameQuery, StringComparison.OrdinalIgnoreCase) >= 0;
            bool valueMatched = !string.IsNullOrEmpty(this.ValueQuery)
                && value != null
                && value.IndexOf(this.ValueQuery, StringComparison.OrdinalIgnoreCase) >= 0;
            bool pathMatched = this.pathRegex != null
                && this.pathRegex.IsMatch(NormalizePath(node.FullPath));

            return nameMatched || valueMatched || pathMatched;
        }

        private static bool TypeMatches(Wz_Node node, string type)
        {
            string nodeType = NodeDto.GetTypeName(node.Value);
            if (string.Equals(nodeType, type, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(type, "file", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(nodeType, "wz-file", StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(type, "dir", StringComparison.OrdinalIgnoreCase))
            {
                return node.Value == null;
            }

            if (string.Equals(type, "string", StringComparison.OrdinalIgnoreCase))
            {
                return node.Value is string;
            }

            return false;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');
        }

        private static string GlobToRegexPattern(string glob)
        {
            bool hasWildcard = glob.IndexOf('*') >= 0 || glob.IndexOf('?') >= 0;
            if (!hasWildcard)
            {
                return Regex.Escape(NormalizePath(glob));
            }

            string normalized = NormalizePath(glob);
            var builder = new System.Text.StringBuilder();
            builder.Append('^');
            foreach (char ch in normalized)
            {
                switch (ch)
                {
                    case '*':
                        builder.Append(".*");
                        break;
                    case '?':
                        builder.Append('.');
                        break;
                    default:
                        builder.Append(Regex.Escape(ch.ToString()));
                        break;
                }
            }
            builder.Append('$');
            return builder.ToString();
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
            if (value is Wz_Video)
            {
                return "video";
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
}
