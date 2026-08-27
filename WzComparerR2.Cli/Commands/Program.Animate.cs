using System;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
        private static int RunAnimate(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintAnimateHelp();
                return ExitSuccess;
            }
            string subCommand = args.Positionals[0].ToLowerInvariant();
            if (subCommand == "gif")
            {
                return RunAnimateGif(args);
            }
            if (subCommand == "apng")
            {
                return RunAnimateApng(args);
            }
            if (subCommand == "ffmpeg")
            {
                return RunAnimateFfmpeg(args);
            }
            if (subCommand != "frames")
            {
                throw new UsageException("Unknown animate command: " + args.Positionals[0]);
            }
            string input = RequireInputAt(args, 1, "animate frames <wz-file-or-dir> --path <wz-path> --out <dir> [--json]");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("animate frames requires --path <wz-path>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("animate frames requires --out <dir>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                var result = AnimationFrameExporter.ExportFrames(node, output);
                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Frames: " + result.FrameCount);
                    writer.WriteLine("Output: " + result.OutputDirectory);
                    writer.WriteLine("Manifest: " + result.ManifestPath);
                    foreach (var frame in result.Frames)
                    {
                        writer.WriteLine(frame.Index + "\tdelay=" + frame.Delay + "\tfiles=" + frame.Files.Count);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunAnimateFfmpeg(ParsedArgs args)
        {
            string input = RequireInputAt(args, 1, "animate ffmpeg <wz-file-or-dir> --path <wz-path> --out <file> [--ffmpeg <path>] [--ffmpeg-args <format>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("animate ffmpeg requires --path <wz-path>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("animate ffmpeg requires --out <file>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                var result = AnimationGifExporter.ExportFfmpeg(node, output, AnimationGifOptions.FromArgs(args), args.GetValue("ffmpeg"), args.GetValue("ffmpeg-args"));
                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Frames: " + result.FrameCount);
                    writer.WriteLine("Output: " + result.OutputPath);
                    writer.WriteLine("Bytes: " + result.Bytes);
                    writer.WriteLine("Canvas: " + result.Width + "x" + result.Height);
                    foreach (var frame in result.Frames)
                    {
                        writer.WriteLine(frame.Index + "\tdelay=" + frame.Delay + "\tpath=" + frame.SourcePath);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunAnimateApng(ParsedArgs args)
        {
            string input = RequireInputAt(args, 1, "animate apng <wz-file-or-dir> --path <wz-path> --out <file.png> [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--optimize] [--json]");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("animate apng requires --path <wz-path>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("animate apng requires --out <file.png>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                var result = AnimationGifExporter.ExportApng(node, output, AnimationGifOptions.FromArgs(args), args.HasFlag("optimize"));
                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Frames: " + result.FrameCount);
                    writer.WriteLine("Output: " + result.OutputPath);
                    writer.WriteLine("Bytes: " + result.Bytes);
                    writer.WriteLine("Canvas: " + result.Width + "x" + result.Height);
                    foreach (var frame in result.Frames)
                    {
                        writer.WriteLine(frame.Index + "\tdelay=" + frame.Delay + "\tpath=" + frame.SourcePath);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunAnimateGif(ParsedArgs args)
        {
            string input = RequireInputAt(args, 1, "animate gif <wz-file-or-dir> --path <wz-path> --out <file.gif> [--background #RRGGBB|transparent] [--min-alpha <0-255>] [--start-frame <n>] [--end-frame <n>] [--delay <ms>] [--scale <factor>] [--origin <x,y>] [--json]");
            string nodePath = args.GetValue("path");
            string output = args.GetValue("out") ?? args.GetValue("output");
            bool json = args.HasFlag("json");
            if (string.IsNullOrEmpty(nodePath))
            {
                throw new UsageException("animate gif requires --path <wz-path>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("animate gif requires --out <file.gif>.");
            }

            using (var context = WzLoadContext.Load(input, WzLoadOptions.FromArgs(args)))
            {
                Wz_Node node = ResolveRequiredNode(context.Root, nodePath, true);
                var result = AnimationGifExporter.ExportGif(node, output, AnimationGifOptions.FromArgs(args));
                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Frames: " + result.FrameCount);
                    writer.WriteLine("Output: " + result.OutputPath);
                    writer.WriteLine("Bytes: " + result.Bytes);
                    writer.WriteLine("Canvas: " + result.Width + "x" + result.Height);
                    foreach (var frame in result.Frames)
                    {
                        writer.WriteLine(frame.Index + "\tdelay=" + frame.Delay + "\tpath=" + frame.SourcePath);
                    }
                });
            }

            return ExitSuccess;
        }
    }
}
