using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
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

        public static List<ExtractedFileDto> ExportMedia(Wz_Node node, string outputDirectory, bool recursive, string kind)
        {
            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);

            var files = new List<ExtractedFileDto>();
            if (recursive)
            {
                foreach (var current in Traverse(node))
                {
                    if (IsMediaKind(current, kind))
                    {
                        ExportSingle(current, fullOutputDirectory, node, files);
                    }
                }
            }
            else
            {
                Wz_Node current = NodePath.ExtractImageNode(node, true);
                if (IsMediaKind(current, kind))
                {
                    ExportSingle(current, fullOutputDirectory, current, files);
                }
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

        public static string WriteManifest(ExtractResultDto result, string manifestPath)
        {
            string fullPath = Path.GetFullPath(manifestPath);
            EnsureParentDirectory(fullPath);
            File.WriteAllText(fullPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            return fullPath;
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

        private static bool IsMediaKind(Wz_Node node, string kind)
        {
            node = NodePath.ExtractImageNode(node, true);
            if (node == null)
            {
                return false;
            }

            if (string.Equals(kind, "sound", StringComparison.OrdinalIgnoreCase))
            {
                return node.Value is Wz_Sound;
            }
            if (string.Equals(kind, "image", StringComparison.OrdinalIgnoreCase))
            {
                return node.Value is Wz_Png;
            }

            return false;
        }

        private static void ExportPng(Wz_Node node, string outputDirectory, Wz_Node root, List<ExtractedFileDto> files)
        {
            var png = (Wz_Png)node.Value;
            for (int page = 0; page < png.ActualPages; page++)
            {
                string suffix = png.ActualPages > 1 ? ".p" + (page + 1) : string.Empty;
                string path = GetOutputPath(outputDirectory, root, node, suffix + ".png");
                EnsureParentDirectory(path);

                if (OperatingSystem.IsWindows())
                {
                    using (var bitmap = png.ExtractPng(page))
                    {
                        bitmap.Save(path, ImageFormat.Png);
                    }
                }
                else
                {
                    CrossPlatformPngWriter.Save(png, page, path);
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
                Bytes = new FileInfo(path).Length,
                Sha256 = ComputeSha256(path)
            };
        }

        private static string ComputeSha256(string path)
        {
            using (var sha256 = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
            }
        }
    }

    internal sealed class ExtractResultDto
    {
        public string InputPath { get; set; }
        public string SourcePath { get; set; }
        public string ManifestPath { get; set; }
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
        public string Sha256 { get; set; }
    }
}
