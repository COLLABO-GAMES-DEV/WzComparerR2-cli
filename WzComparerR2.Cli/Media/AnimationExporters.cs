using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using WzComparerR2.Common;
using WzComparerR2.Encoders;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal static class AnimationFrameExporter
    {
        public static AnimationFramesResultDto ExportFrames(Wz_Node node, string outputDirectory)
        {
            string fullOutputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(fullOutputDirectory);

            var result = new AnimationFramesResultDto
            {
                SourcePath = node.FullPath,
                OutputDirectory = fullOutputDirectory,
                Frames = new List<AnimationFrameDto>()
            };

            var frameNodes = GetFrameNodes(node);
            foreach (var frameNode in frameNodes)
            {
                string frameDirectory = Path.Combine(fullOutputDirectory, frameNode.Index.ToString("d4"));
                var files = ExtractExporter.ExportAuto(frameNode.Node, frameDirectory, true);
                result.Frames.Add(new AnimationFrameDto
                {
                    Index = frameNode.Index,
                    SourcePath = frameNode.Node.FullPath,
                    Delay = ReadDelay(frameNode.Node),
                    Files = files
                });
            }

            result.FrameCount = result.Frames.Count;
            result.ManifestPath = Path.Combine(fullOutputDirectory, "frames.json");
            File.WriteAllText(result.ManifestPath, JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            return result;
        }

        private static List<AnimationFrameNode> GetFrameNodes(Wz_Node node)
        {
            var frames = new List<AnimationFrameNode>();
            foreach (Wz_Node child in node.Nodes)
            {
                int index;
                if (int.TryParse(child.Text, out index))
                {
                    frames.Add(new AnimationFrameNode(index, NodePath.ExtractImageNode(child, true)));
                }
            }

            if (frames.Count == 0)
            {
                frames.Add(new AnimationFrameNode(0, node));
            }

            return frames.OrderBy(frame => frame.Index).ToList();
        }

        private static int? ReadDelay(Wz_Node frameNode)
        {
            foreach (Wz_Node child in frameNode.Nodes)
            {
                if (string.Equals(child.Text, "delay", StringComparison.OrdinalIgnoreCase))
                {
                    int delay;
                    string value = NodeDto.FormatValue(child.Value);
                    if (int.TryParse(value, out delay))
                    {
                        return delay;
                    }
                }
            }
            return null;
        }

        private struct AnimationFrameNode
        {
            public AnimationFrameNode(int index, Wz_Node node)
            {
                this.Index = index;
                this.Node = node;
            }

            public int Index { get; private set; }
            public Wz_Node Node { get; private set; }
        }
    }

    internal sealed class AnimationFramesResultDto
    {
        public string SourcePath { get; set; }
        public string OutputDirectory { get; set; }
        public string ManifestPath { get; set; }
        public int FrameCount { get; set; }
        public List<AnimationFrameDto> Frames { get; set; }
    }

    internal sealed class AnimationFrameDto
    {
        public int Index { get; set; }
        public string SourcePath { get; set; }
        public int? Delay { get; set; }
        public List<ExtractedFileDto> Files { get; set; }
    }

    internal static class AnimationGifExporter
    {
        public static AnimationGifResultDto ExportGif(Wz_Node node, string outputPath, AnimationGifOptions options)
        {
            return Export(node, outputPath, new BuildInGifEncoder(), options);
        }

        public static AnimationGifResultDto ExportApng(Wz_Node node, string outputPath, AnimationGifOptions options, bool optimize)
        {
            return Export(node, outputPath, new BuildInApngEncoder { OptimizeEnabled = optimize }, options);
        }

        public static AnimationGifResultDto ExportFfmpeg(Wz_Node node, string outputPath, AnimationGifOptions options, string ffmpegPath, string ffmpegArgs)
        {
            return Export(node, outputPath, new FFmpegEncoder
            {
                FFmpegBinPath = ffmpegPath,
                FFmpegArgumentFormat = ffmpegArgs
            }, options);
        }

        private static AnimationGifResultDto Export(Wz_Node node, string outputPath, GifEncoder encoder, AnimationGifOptions options)
        {
            string fullOutputPath = Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(fullOutputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Gif gif = Gif.CreateFromNode(node, FindLinkedNode);
            if (gif == null || gif.Frames.Count == 0)
            {
                throw new UsageException("No GIF-compatible bitmap frames were found at path: " + node.FullPath);
            }
            gif = ApplyOptions(gif, options);
            if (gif.Frames.Count == 0)
            {
                throw new UsageException("No frames remain after applying frame range options.");
            }

            Rectangle rect = gif.GetRect();
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                throw new UsageException("Animation frame bounds are empty: " + node.FullPath);
            }

            using (encoder)
            {
                encoder.Init(fullOutputPath, rect.Width, rect.Height);
                gif.SaveGif(encoder, fullOutputPath, options.Background, options.MinAlpha);
            }

            var result = new AnimationGifResultDto
            {
                SourcePath = node.FullPath,
                OutputPath = fullOutputPath,
                Width = rect.Width,
                Height = rect.Height,
                FrameCount = gif.Frames.Count,
                Bytes = new FileInfo(fullOutputPath).Length,
                StartFrame = options.StartFrame,
                EndFrame = options.EndFrame,
                Scale = options.Scale,
                DelayOverride = options.DelayOverride,
                OriginOverride = options.OriginOverrideText,
                Frames = CollectFrameMetadata(node, options)
            };
            return result;
        }

        private static Gif ApplyOptions(Gif source, AnimationGifOptions options)
        {
            var result = new Gif();
            for (int index = 0; index < source.Frames.Count; index++)
            {
                if (index < options.StartFrame)
                {
                    continue;
                }
                if (options.EndFrame.HasValue && index > options.EndFrame.Value)
                {
                    continue;
                }

                var frame = source.Frames[index] as GifFrame;
                if (frame == null)
                {
                    continue;
                }

                Bitmap bitmap = options.Scale == 1d ? frame.Bitmap : ScaleBitmap(frame.Bitmap, options.Scale);
                Point origin = options.OriginOverride ?? ScalePoint(frame.Origin, options.Scale);
                int delay = options.DelayOverride ?? frame.Delay;
                if (delay <= 0)
                {
                    delay = 120;
                }

                result.Frames.Add(new GifFrame(bitmap, origin, delay)
                {
                    A0 = frame.A0,
                    A1 = frame.A1
                });
            }
            return result;
        }

        private static Bitmap ScaleBitmap(Bitmap bitmap, double scale)
        {
            if (bitmap == null)
            {
                return null;
            }

            int width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            int height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(scaled))
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(bitmap, new Rectangle(0, 0, width, height));
            }
            return scaled;
        }

        private static Point ScalePoint(Point point, double scale)
        {
            if (scale == 1d)
            {
                return point;
            }
            return new Point(
                (int)Math.Round(point.X * scale),
                (int)Math.Round(point.Y * scale));
        }

        private static Wz_Node FindLinkedNode(string fullPath, Wz_File sourceWzFile)
        {
            return null;
        }

        private static List<AnimationGifFrameDto> CollectFrameMetadata(Wz_Node node, AnimationGifOptions options)
        {
            var frames = new List<AnimationGifFrameDto>();
            foreach (Wz_Node child in node.Nodes)
            {
                int index;
                if (!int.TryParse(child.Text, out index))
                {
                    continue;
                }
                if (index < options.StartFrame)
                {
                    continue;
                }
                if (options.EndFrame.HasValue && index > options.EndFrame.Value)
                {
                    continue;
                }

                Wz_Node frameNode = NodePath.ExtractImageNode(child, true) ?? child;
                if (!(frameNode.Value is Wz_Png))
                {
                    continue;
                }

                frames.Add(new AnimationGifFrameDto
                {
                    Index = index,
                    SourcePath = frameNode.FullPath,
                    Delay = options.DelayOverride ?? ReadDelay(frameNode)
                });
            }

            return frames.OrderBy(frame => frame.Index).ToList();
        }

        private static int ReadDelay(Wz_Node frameNode)
        {
            Wz_Node delayNode = frameNode.FindNodeByPath("delay");
            int delay = delayNode.GetValueEx<int>(0);
            return delay <= 0 ? 120 : delay;
        }
    }

    internal sealed class AnimationGifOptions
    {
        public Color Background { get; private set; }
        public int MinAlpha { get; private set; }
        public int StartFrame { get; private set; }
        public int? EndFrame { get; private set; }
        public int? DelayOverride { get; private set; }
        public double Scale { get; private set; }
        public Point? OriginOverride { get; private set; }
        public string OriginOverrideText { get; private set; }

        public static AnimationGifOptions FromArgs(ParsedArgs args)
        {
            var options = new AnimationGifOptions
            {
                Background = ParseColor(args.GetValue("background") ?? "transparent"),
                MinAlpha = Math.Max(0, Math.Min(255, args.GetInt("min-alpha", 0))),
                StartFrame = args.GetInt("start-frame", 0),
                EndFrame = ParseOptionalNonNegativeInt(args.GetValue("end-frame"), "end-frame"),
                DelayOverride = ParseOptionalPositiveInt(args.GetValue("delay"), "delay"),
                Scale = ParseScale(args.GetValue("scale"))
            };
            options.OriginOverride = ParseOrigin(args.GetValue("origin"), out string originText);
            options.OriginOverrideText = originText;
            if (options.StartFrame < 0)
            {
                throw new UsageException("animate --start-frame must be a non-negative integer.");
            }
            if (options.EndFrame.HasValue && options.EndFrame.Value < options.StartFrame)
            {
                throw new UsageException("animate --end-frame must be greater than or equal to --start-frame.");
            }
            return options;
        }

        private static Color ParseColor(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || string.Equals(value, "transparent", StringComparison.OrdinalIgnoreCase))
            {
                return Color.Transparent;
            }
            if (value.Length == 7 && value[0] == '#')
            {
                int r;
                int g;
                int b;
                if (int.TryParse(value.Substring(1, 2), NumberStyles.HexNumber, null, out r)
                    && int.TryParse(value.Substring(3, 2), NumberStyles.HexNumber, null, out g)
                    && int.TryParse(value.Substring(5, 2), NumberStyles.HexNumber, null, out b))
                {
                    return Color.FromArgb(255, r, g, b);
                }
            }

            throw new UsageException("animate gif --background must be transparent or #RRGGBB.");
        }

        private static int? ParseOptionalNonNegativeInt(string value, string optionName)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            int parsed;
            if (!int.TryParse(value, out parsed) || parsed < 0)
            {
                throw new UsageException("animate --" + optionName + " must be a non-negative integer.");
            }
            return parsed;
        }

        private static int? ParseOptionalPositiveInt(string value, string optionName)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }
            int parsed;
            if (!int.TryParse(value, out parsed) || parsed <= 0)
            {
                throw new UsageException("animate --" + optionName + " must be a positive integer.");
            }
            return parsed;
        }

        private static double ParseScale(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 1d;
            }
            double parsed;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) || parsed <= 0d)
            {
                throw new UsageException("animate --scale must be a positive number.");
            }
            return parsed;
        }

        private static Point? ParseOrigin(string value, out string originText)
        {
            originText = null;
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            var parts = value.Split(',');
            if (parts.Length != 2)
            {
                throw new UsageException("animate --origin must use x,y format.");
            }

            int x;
            int y;
            if (!int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y))
            {
                throw new UsageException("animate --origin must use integer x,y values.");
            }

            originText = x + "," + y;
            return new Point(x, y);
        }
    }

    internal sealed class AnimationGifResultDto
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public int FrameCount { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long Bytes { get; set; }
        public int StartFrame { get; set; }
        public int? EndFrame { get; set; }
        public double Scale { get; set; }
        public int? DelayOverride { get; set; }
        public string OriginOverride { get; set; }
        public List<AnimationGifFrameDto> Frames { get; set; }
    }

    internal sealed class AnimationGifFrameDto
    {
        public int Index { get; set; }
        public string SourcePath { get; set; }
        public int Delay { get; set; }
    }
}
