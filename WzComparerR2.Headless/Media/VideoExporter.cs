using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using WzComparerR2.WzLib;

namespace WzComparerR2.Headless
{
    internal sealed class VideoExportOptions
    {
        public string Format { get; private set; }
        public string FfmpegPath { get; private set; }
        public int MaxFrames { get; private set; }
        public bool KeepWorkFiles { get; private set; }

        public static VideoExportOptions FromArgs(ParsedArgs args)
        {
            return FromArgs(args, "mcv");
        }

        public static VideoExportOptions FromArgs(ParsedArgs args, string defaultFormat)
        {
            string format = args.GetValue("video-format") ?? args.GetValue("format") ?? defaultFormat ?? "mcv";
            string decode = args.GetValue("decode");
            if (!string.IsNullOrEmpty(decode))
            {
                format = decode;
            }
            else if (args.HasFlag("decode") && string.Equals(format, "mcv", StringComparison.OrdinalIgnoreCase))
            {
                format = "frames";
            }

            format = format.ToLowerInvariant();
            if (format == "png")
            {
                format = "frames";
            }
            if (format != "mcv" && format != "frames" && format != "gif" && format != "both")
            {
                throw new UsageException("video export --format must be one of: mcv, frames, png, gif, both.");
            }

            int maxFrames = args.GetInt("max-frames", 0);
            if (maxFrames < 0)
            {
                throw new UsageException("video export --max-frames must be zero or a positive integer.");
            }

            return new VideoExportOptions
            {
                Format = format,
                FfmpegPath = args.GetValue("ffmpeg") ?? "ffmpeg",
                MaxFrames = maxFrames,
                KeepWorkFiles = args.HasFlag("keep-video-work")
            };
        }
    }

    internal static class VideoExporter
    {
        internal sealed class DecodedVideoFrameFile
        {
            public string Path { get; set; }
            public int FrameIndex { get; set; }
            public int FrameCount { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public double? DelayMs { get; set; }
            public double? StartMs { get; set; }
            public string Format { get; set; }
        }

        public static List<ExtractedFileDto> Export(Wz_Node node, string outputDirectory, bool recursive, VideoExportOptions options)
        {
            string fullOutputDirectory = Path.GetFullPath(outputDirectory);

            var files = new List<ExtractedFileDto>();
            if (recursive)
            {
                foreach (Wz_Node current in Traverse(node))
                {
                    if (current.Value is Wz_Video)
                    {
                        ExportVideoNode(current, fullOutputDirectory, node, options, files);
                    }
                }
            }
            else
            {
                Wz_Node current = NodePath.ExtractImageNode(node, true);
                if (current != null && current.Value is Wz_Video)
                {
                    ExportVideoNode(current, fullOutputDirectory, current, options, files);
                }
            }

            return files;
        }

        public static ExtractedFileDto ExportMcv(Wz_Node node, string outputDirectory, Wz_Node root)
        {
            var files = new List<ExtractedFileDto>();
            ExportMcv(node, outputDirectory, root, files);
            return files[0];
        }

        public static List<DecodedVideoFrameFile> DecodeFrameFiles(Wz_Node node, string outputDirectory, string ffmpegPath, int maxFrames)
        {
            if (node == null || !(node.Value is Wz_Video))
            {
                return new List<DecodedVideoFrameFile>();
            }

            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);

            byte[] data = CopyVideoData(node);
            var header = ((Wz_Video)node.Value).ReadVideoFileHeader();
            int frameLimit = maxFrames <= 0 ? header.FrameCount : Math.Min(maxFrames, header.FrameCount);
            if (frameLimit <= 0)
            {
                return new List<DecodedVideoFrameFile>();
            }

            string workDirectory = Path.Combine(fullOutputDirectory, ".mcv-work");
            Directory.CreateDirectory(workDirectory);
            try
            {
                string baseIvf = Path.Combine(workDirectory, "base.ivf");
                WriteIvf(baseIvf, header, data, frameLimit, alpha: false);

                bool hasAlpha = (header.DataFlag & McvDataFlags.AlphaMap) != 0
                    && header.Frames.Any(frame => frame.AlphaDataOffset >= 0 && frame.AlphaDataCount > 0);
                string alphaIvf = null;
                if (hasAlpha)
                {
                    alphaIvf = Path.Combine(workDirectory, "alpha.ivf");
                    WriteIvf(alphaIvf, header, data, frameLimit, alpha: true);
                }

                DecodeFrames(baseIvf, alphaIvf, hasAlpha, fullOutputDirectory, string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath);
                return GetDecodedFrameFiles(fullOutputDirectory, header, frameLimit);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(workDirectory))
                    {
                        Directory.Delete(workDirectory, recursive: true);
                    }
                }
                catch
                {
                    // Search/export callers can safely ignore temporary work directory cleanup failures.
                }
            }
        }

        private static void ExportVideoNode(Wz_Node node, string outputDirectory, Wz_Node root, VideoExportOptions options, List<ExtractedFileDto> files)
        {
            if (options.Format == "mcv")
            {
                ExportMcv(node, outputDirectory, root, files);
                return;
            }

            string nodeOutputDirectory = GetOutputDirectory(outputDirectory, root, node);
            Directory.CreateDirectory(nodeOutputDirectory);

            byte[] data = CopyVideoData(node);
            var header = ((Wz_Video)node.Value).ReadVideoFileHeader();
            int frameLimit = options.MaxFrames == 0 ? header.FrameCount : Math.Min(options.MaxFrames, header.FrameCount);
            string workDirectory = Path.Combine(nodeOutputDirectory, ".mcv-work");
            Directory.CreateDirectory(workDirectory);

            string baseIvf = Path.Combine(workDirectory, "base.ivf");
            WriteIvf(baseIvf, header, data, frameLimit, alpha: false);

            bool hasAlpha = (header.DataFlag & McvDataFlags.AlphaMap) != 0
                && header.Frames.Any(frame => frame.AlphaDataOffset >= 0 && frame.AlphaDataCount > 0);
            string alphaIvf = null;
            if (hasAlpha)
            {
                alphaIvf = Path.Combine(workDirectory, "alpha.ivf");
                WriteIvf(alphaIvf, header, data, frameLimit, alpha: true);
            }

            if (options.Format == "frames" || options.Format == "both")
            {
                DecodeFrames(baseIvf, alphaIvf, hasAlpha, nodeOutputDirectory, options.FfmpegPath);
                AddFrameFiles(node, nodeOutputDirectory, files);
            }

            if (options.Format == "gif" || options.Format == "both")
            {
                string gifPath = Path.Combine(nodeOutputDirectory, SanitizeFileName(node.Text) + ".gif");
                DecodeGif(baseIvf, alphaIvf, hasAlpha, header, gifPath, options.FfmpegPath, workDirectory);
                files.Add(CreateFileDto(node, node, gifPath, "gif"));
            }

            if (!options.KeepWorkFiles)
            {
                Directory.Delete(workDirectory, recursive: true);
            }
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

        private static void ExportMcv(Wz_Node node, string outputDirectory, Wz_Node root, List<ExtractedFileDto> files)
        {
            string path = GetOutputPath(outputDirectory, root, node, ".mcv");
            EnsureParentDirectory(path);
            File.WriteAllBytes(path, CopyVideoData(node));
            files.Add(CreateFileDto(node, root, path, "mcv"));
        }

        private static byte[] CopyVideoData(Wz_Node node)
        {
            var video = (Wz_Video)node.Value;
            byte[] data = new byte[video.Length];
            video.CopyTo(data, 0);
            return data;
        }

        private static void WriteIvf(string path, McvHeader header, byte[] mcvData, int frameLimit, bool alpha)
        {
            using (var output = File.Create(path))
            using (var writer = new BinaryWriter(output))
            {
                writer.Write(new[] { (byte)'D', (byte)'K', (byte)'I', (byte)'F' });
                writer.Write((ushort)0);
                writer.Write((ushort)32);
                writer.Write(header.FourCC);
                writer.Write((ushort)header.Width);
                writer.Write((ushort)header.Height);
                writer.Write(1000u);
                writer.Write(1u);
                writer.Write((uint)frameLimit);
                writer.Write(0u);

                for (int i = 0; i < frameLimit; i++)
                {
                    McvFrameInfo frame = header.Frames[i];
                    long offset = alpha ? frame.AlphaDataOffset : frame.DataOffset;
                    int count = alpha ? frame.AlphaDataCount : frame.DataCount;
                    if (offset < 0 || count <= 0)
                    {
                        count = 0;
                    }

                    writer.Write((uint)count);
                    writer.Write((ulong)i);
                    if (count > 0)
                    {
                        writer.Write(mcvData, checked((int)offset), count);
                    }
                }
            }
        }

        private static void DecodeFrames(string baseIvf, string alphaIvf, bool hasAlpha, string outputDirectory, string ffmpegPath)
        {
            string pattern = Path.Combine(outputDirectory, "frame-%04d.png");
            if (hasAlpha)
            {
                RunFfmpeg(ffmpegPath, "-y", "-i", baseIvf, "-i", alphaIvf, "-filter_complex", "[1:v]format=gray[alpha];[0:v][alpha]alphamerge", pattern);
            }
            else
            {
                RunFfmpeg(ffmpegPath, "-y", "-i", baseIvf, pattern);
            }
        }

        private static void DecodeGif(string baseIvf, string alphaIvf, bool hasAlpha, McvHeader header, string gifPath, string ffmpegPath, string workDirectory)
        {
            EnsureParentDirectory(gifPath);
            if (hasAlpha)
            {
                string frameDirectory = Path.Combine(workDirectory, "gif-frames");
                string flattenedDirectory = Path.Combine(workDirectory, "gif-flattened");
                Directory.CreateDirectory(frameDirectory);
                Directory.CreateDirectory(flattenedDirectory);
                DecodeFrames(baseIvf, alphaIvf, true, frameDirectory, ffmpegPath);

                string frameRate = GetFrameRate(header);
                RunFfmpeg(
                    ffmpegPath,
                    "-y",
                    "-f", "lavfi",
                    "-i", "color=c=black:s=" + header.Width + "x" + header.Height + ":r=" + frameRate,
                    "-framerate", frameRate,
                    "-i", Path.Combine(frameDirectory, "frame-%04d.png"),
                    "-filter_complex", "[0:v][1:v]overlay=shortest=1:format=auto,format=rgb24",
                    Path.Combine(flattenedDirectory, "frame-%04d.png"));
                RunFfmpeg(
                    ffmpegPath,
                    "-y",
                    "-framerate", frameRate,
                    "-i", Path.Combine(flattenedDirectory, "frame-%04d.png"),
                    "-filter_complex", "split[s0][s1];[s0]palettegen=stats_mode=single[p];[s1][p]paletteuse=new=1:dither=none",
                    gifPath);
            }
            else
            {
                RunFfmpeg(ffmpegPath, "-y", "-i", baseIvf, "-filter_complex", "split[s0][s1];[s0]palettegen=stats_mode=single[p];[s1][p]paletteuse=new=1:dither=none", gifPath);
            }
        }

        private static string GetFrameRate(McvHeader header)
        {
            long delay = header.Frames.FirstOrDefault()?.DelayInNanoseconds ?? 0;
            if (delay <= 0)
            {
                return "16";
            }

            double fps = 1000000000.0 / delay;
            return Math.Max(1.0, Math.Min(120.0, fps)).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void RunFfmpeg(string ffmpegPath, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = false
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                    {
                        throw new UsageException("ffmpeg failed while decoding MCV video. " + FirstNonEmptyLine(stderr));
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new UsageException("ffmpeg was not found. Install ffmpeg or pass --ffmpeg <path>. " + ex.Message);
            }
        }

        private static void AddFrameFiles(Wz_Node node, string outputDirectory, List<ExtractedFileDto> files)
        {
            foreach (string path in Directory.EnumerateFiles(outputDirectory, "frame-*.png").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                files.Add(CreateFileDto(node, node, path, "png"));
            }
        }

        private static List<DecodedVideoFrameFile> GetDecodedFrameFiles(string outputDirectory, McvHeader header, int frameLimit)
        {
            var result = new List<DecodedVideoFrameFile>();
            string[] paths = Directory.EnumerateFiles(outputDirectory, "frame-*.png")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            long startNanoseconds = 0;
            int count = Math.Min(frameLimit, paths.Length);
            string format = FormatFourCC(header.FourCC);
            for (int i = 0; i < count; i++)
            {
                McvFrameInfo frame = header.Frames[i];
                result.Add(new DecodedVideoFrameFile
                {
                    Path = paths[i],
                    FrameIndex = i,
                    FrameCount = header.FrameCount,
                    Width = header.Width,
                    Height = header.Height,
                    DelayMs = ToMilliseconds(frame.DelayInNanoseconds),
                    StartMs = startNanoseconds / 1000000.0,
                    Format = format
                });
                if (frame.DelayInNanoseconds > 0)
                {
                    startNanoseconds += frame.DelayInNanoseconds;
                }
            }

            return result;
        }

        private static double? ToMilliseconds(long nanoseconds)
        {
            return nanoseconds > 0 ? nanoseconds / 1000000.0 : (double?)null;
        }

        private static string FormatFourCC(uint fourCC)
        {
            return Encoding.ASCII.GetString(BitConverter.GetBytes(fourCC)).TrimEnd('\0');
        }

        private static string FirstNonEmptyLine(params string[] values)
        {
            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                using (var reader = new StringReader(value))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            return line.Trim();
                        }
                    }
                }
            }
            return string.Empty;
        }

        private static string GetOutputDirectory(string outputDirectory, Wz_Node root, Wz_Node node)
        {
            var segments = GetRelativeSegments(root, node);
            if (segments.Count == 0)
            {
                segments.Add(SanitizeFileName(node.Text));
            }
            return Path.Combine(new[] { outputDirectory }.Concat(segments).ToArray());
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
            return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        }

        private static void EnsureParentDirectory(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static ExtractedFileDto CreateFileDto(Wz_Node node, Wz_Node root, string path, string type)
        {
            var dto = new ExtractedFileDto
            {
                SourcePath = node.FullPath,
                OutputPath = path,
                Type = type,
                Bytes = new FileInfo(path).Length,
                Sha256 = ComputeSha256(path),
                RelativePath = string.Join("/", GetRelativeSegments(root, node)),
                FrameIndex = ExtractedFileMetadata.TryParseFrameIndex(node.Text)
            };
            ExtractedFileMetadata.ApplyIntrinsicMetadata(dto, node);
            ExtractedFileMetadata.ApplyNodeMetadata(dto, node);
            return dto;
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
}
