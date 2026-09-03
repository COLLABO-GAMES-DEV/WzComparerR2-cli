using System;
using System.Collections.Generic;
using System.Linq;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal sealed class ExtractedFileDto
    {
        public string SourcePath { get; set; }
        public string MetadataPath { get; set; }
        public string OutputPath { get; set; }
        public string Type { get; set; }
        public long Bytes { get; set; }
        public string Sha256 { get; set; }
        public string RelativePath { get; set; }
        public int? FrameIndex { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string Format { get; set; }
        public int? Pages { get; set; }
        public int? DataLength { get; set; }
        public int? Ms { get; set; }
        public int? Channels { get; set; }
        public int? Frequency { get; set; }
        public string SoundType { get; set; }
        public int? FrameCount { get; set; }
        public string VideoFlags { get; set; }
        public ExtractedPointDto Origin { get; set; }
        public ExtractedPointDto Lt { get; set; }
        public ExtractedPointDto Rb { get; set; }
        public int? Z { get; set; }
        public int? Delay { get; set; }
        public string OutlinkPath { get; set; }
        public string InlinkPath { get; set; }
        public Dictionary<string, ExtractedNodeValueDto> Metadata { get; set; }
    }

    internal sealed class ExtractedPointDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public string Raw { get; set; }
    }

    internal sealed class ExtractedNodeValueDto
    {
        public string Type { get; set; }
        public string Value { get; set; }
        public string Path { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
    }

    internal static class ExtractedFileMetadata
    {
        public static void ApplyIntrinsicMetadata(ExtractedFileDto dto, Wz_Node node)
        {
            if (dto == null || node == null)
            {
                return;
            }

            var png = node.Value as Wz_Png;
            if (png != null)
            {
                dto.Width = png.Width;
                dto.Height = png.Height;
                dto.Format = png.Format.ToString();
                dto.Pages = png.ActualPages;
                return;
            }

            var sound = node.Value as Wz_Sound;
            if (sound != null)
            {
                dto.DataLength = sound.DataLength;
                dto.Ms = sound.Ms;
                dto.Channels = sound.Channels;
                dto.Frequency = sound.Frequency;
                dto.SoundType = sound.SoundType.ToString();
                return;
            }

            var video = node.Value as Wz_Video;
            if (video != null)
            {
                dto.DataLength = video.Length;
                try
                {
                    var header = video.ReadVideoFileHeader();
                    dto.Width = header.Width;
                    dto.Height = header.Height;
                    dto.FrameCount = header.FrameCount;
                    dto.Format = FourCCToString(header.FourCC);
                    dto.VideoFlags = header.DataFlag.ToString();
                }
                catch
                {
                    // The media export itself already succeeded; keep header metadata optional.
                }
                return;
            }

            var raw = node.Value as Wz_RawData;
            if (raw != null)
            {
                dto.DataLength = raw.Length;
            }
        }

        public static void ApplyNodeMetadata(ExtractedFileDto dto, Wz_Node metadataNode)
        {
            if (dto == null || metadataNode == null)
            {
                return;
            }

            Dictionary<string, ExtractedNodeValueDto> metadata = CollectDirectMetadata(metadataNode);
            if (metadata.Count == 0)
            {
                return;
            }

            dto.MetadataPath = metadataNode.FullPath;
            dto.Metadata = MergeMetadata(dto.Metadata, metadata);
            dto.OutlinkPath = ReadString(metadata, "_outlink") ?? dto.OutlinkPath;
            dto.InlinkPath = ReadString(metadata, "_inlink") ?? dto.InlinkPath;
            dto.Origin = ReadPoint(metadata, "origin") ?? dto.Origin;
            dto.Lt = ReadPoint(metadata, "lt") ?? dto.Lt;
            dto.Rb = ReadPoint(metadata, "rb") ?? dto.Rb;
            dto.Z = ReadInt(metadata, "z") ?? dto.Z;
            dto.Delay = ReadInt(metadata, "delay") ?? dto.Delay;
        }

        public static Dictionary<string, ExtractedNodeValueDto> CollectDirectNodeMetadata(Wz_Node node)
        {
            return CollectDirectMetadata(node);
        }

        public static void EnrichFromMetadataRoot(IList<ExtractedFileDto> files, Wz_Node exportedRoot, Wz_Node metadataRoot)
        {
            if (files == null || exportedRoot == null || metadataRoot == null)
            {
                return;
            }

            var lookup = BuildMetadataLookup(metadataRoot);
            if (lookup.Count == 0)
            {
                return;
            }

            string exportedRootPath = NormalizePath(exportedRoot.FullPath);
            foreach (ExtractedFileDto file in files)
            {
                string relativePath = GetRelativeSourcePath(exportedRootPath, file.SourcePath);
                Wz_Node metadataNode;
                if (relativePath != null && lookup.TryGetValue(relativePath, out metadataNode))
                {
                    ApplyNodeMetadata(file, metadataNode);
                }
            }
        }

        public static int? TryParseFrameIndex(string text)
        {
            int value;
            if (int.TryParse(text, out value))
            {
                return value;
            }
            return null;
        }

        private static Dictionary<string, Wz_Node> BuildMetadataLookup(Wz_Node root)
        {
            var result = new Dictionary<string, Wz_Node>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in TraverseWithRelativePath(root))
            {
                if (!HasDirectMetadata(item.Node))
                {
                    continue;
                }
                if (!result.ContainsKey(item.RelativePath))
                {
                    result.Add(item.RelativePath, item.Node);
                }
            }
            return result;
        }

        private static IEnumerable<MetadataNodePath> TraverseWithRelativePath(Wz_Node root)
        {
            var stack = new Stack<MetadataNodePath>();
            stack.Push(new MetadataNodePath(root, string.Empty));

            while (stack.Count > 0)
            {
                MetadataNodePath current = stack.Pop();
                Wz_Node node = NodePath.ExtractImageNode(current.Node, true);
                if (node == null)
                {
                    continue;
                }

                yield return new MetadataNodePath(node, current.RelativePath);

                var children = node.Nodes.Cast<Wz_Node>().ToList();
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    string relativePath = string.IsNullOrEmpty(current.RelativePath)
                        ? children[i].Text
                        : current.RelativePath + "/" + children[i].Text;
                    stack.Push(new MetadataNodePath(children[i], NormalizePath(relativePath)));
                }
            }
        }

        private static string GetRelativeSourcePath(string exportedRootPath, string sourcePath)
        {
            string normalized = NormalizePath(sourcePath);
            if (normalized.Length == 0)
            {
                return string.Empty;
            }
            if (!string.IsNullOrEmpty(exportedRootPath)
                && normalized.StartsWith(exportedRootPath, StringComparison.OrdinalIgnoreCase))
            {
                string relative = normalized.Substring(exportedRootPath.Length).Trim('/');
                return relative;
            }
            return normalized;
        }

        private static bool HasDirectMetadata(Wz_Node node)
        {
            return CollectDirectMetadata(node).Count > 0;
        }

        private static Dictionary<string, ExtractedNodeValueDto> CollectDirectMetadata(Wz_Node node)
        {
            var result = new Dictionary<string, ExtractedNodeValueDto>(StringComparer.OrdinalIgnoreCase);
            foreach (Wz_Node child in node.Nodes)
            {
                if (!IsScalarMetadataValue(child.Value))
                {
                    continue;
                }

                string value = NodeDto.FormatValue(child.Value);
                if (value == null)
                {
                    value = Convert.ToString(child.Value, System.Globalization.CultureInfo.InvariantCulture);
                }
                if (value == null)
                {
                    continue;
                }

                var item = new ExtractedNodeValueDto
                {
                    Type = NodeDto.GetTypeName(child.Value),
                    Value = value,
                    Path = child.FullPath
                };

                var point = ToPoint(child.Value, value);
                if (point != null)
                {
                    item.X = point.X;
                    item.Y = point.Y;
                }

                result[child.Text] = item;
            }
            return result;
        }

        private static Dictionary<string, ExtractedNodeValueDto> MergeMetadata(
            Dictionary<string, ExtractedNodeValueDto> existing,
            Dictionary<string, ExtractedNodeValueDto> additional)
        {
            if (existing == null || existing.Count == 0)
            {
                return new Dictionary<string, ExtractedNodeValueDto>(additional, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var item in additional)
            {
                existing[item.Key] = item.Value;
            }
            return existing;
        }

        private static bool IsScalarMetadataValue(object value)
        {
            if (value == null || value is Wz_File || value is Wz_Image || value is Wz_Png || value is Wz_Sound || value is Wz_RawData || value is Wz_Video)
            {
                return false;
            }
            return true;
        }

        private static string ReadString(Dictionary<string, ExtractedNodeValueDto> metadata, string key)
        {
            ExtractedNodeValueDto value;
            if (metadata != null && metadata.TryGetValue(key, out value))
            {
                return value.Value;
            }
            return null;
        }

        private static int? ReadInt(Dictionary<string, ExtractedNodeValueDto> metadata, string key)
        {
            string raw = ReadString(metadata, key);
            int value;
            if (int.TryParse(raw, out value))
            {
                return value;
            }
            return null;
        }

        private static ExtractedPointDto ReadPoint(Dictionary<string, ExtractedNodeValueDto> metadata, string key)
        {
            ExtractedNodeValueDto value;
            if (metadata != null && metadata.TryGetValue(key, out value) && value.X.HasValue && value.Y.HasValue)
            {
                return new ExtractedPointDto
                {
                    X = value.X.Value,
                    Y = value.Y.Value,
                    Raw = value.Value
                };
            }
            return null;
        }

        private static ExtractedPointDto ToPoint(object rawValue, string formatted)
        {
            var vector = rawValue as Wz_Vector;
            if (vector != null)
            {
                return new ExtractedPointDto
                {
                    X = vector.X,
                    Y = vector.Y,
                    Raw = formatted
                };
            }

            if (string.IsNullOrEmpty(formatted))
            {
                return null;
            }

            string[] parts = formatted.Split(',');
            int x;
            int y;
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), out x)
                && int.TryParse(parts[1].Trim(), out y))
            {
                return new ExtractedPointDto
                {
                    X = x,
                    Y = y,
                    Raw = formatted
                };
            }
            return null;
        }

        private static string FourCCToString(uint fourCC)
        {
            var bytes = BitConverter.GetBytes(fourCC);
            return new string(bytes.Select(value => value >= 32 && value <= 126 ? (char)value : '?').ToArray());
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').Trim('/');
        }

        private sealed class MetadataNodePath
        {
            public MetadataNodePath(Wz_Node node, string relativePath)
            {
                Node = node;
                RelativePath = relativePath;
            }

            public Wz_Node Node { get; private set; }
            public string RelativePath { get; private set; }
        }
    }
}
