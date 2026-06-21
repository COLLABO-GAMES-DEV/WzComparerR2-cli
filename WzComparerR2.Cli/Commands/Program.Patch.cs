using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WzComparerR2.Patcher;
using WzComparerR2.Patcher.Builder;


namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
        private static int RunPatch(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintPatchHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            switch (subCommand)
            {
                case "inspect":
                    return RunPatchInspect(args);
                case "dry-run":
                    return RunPatchDryRun(args);
                case "apply":
                    return RunPatchApply(args);
                default:
                    throw new UsageException("Unknown patch command: " + subCommand);
            }
        }

        private static int RunPatchInspect(ParsedArgs args)
        {
            if (args.Positionals.Count < 2)
            {
                throw new UsageException("Usage: wcr2 patch inspect <patch-file> [--json]");
            }

            string patchFile = args.Positionals[1];
            bool json = args.HasFlag("json");
            string output = args.GetValue("out") ?? args.GetValue("output");

            if (!File.Exists(patchFile))
            {
                throw new FileNotFoundException("Input path not found: " + patchFile);
            }

            using (var patcher = new WzPatcher(patchFile))
            {
                long dataPosition = patcher.PrePatch(CancellationToken.None);
                var result = PatchInspectResultDto.FromPatcher(Path.GetFullPath(patchFile), dataPosition, patcher);
                if (!string.IsNullOrEmpty(output))
                {
                    result.OutputPath = WriteJsonFile(result, output);
                }

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Patch: " + result.PatchFilePath);
                    writer.WriteLine("Parts: " + result.PartCount + " Create: " + result.CreateCount + " Rebuild: " + result.RebuildCount + " Delete: " + result.DeleteCount);
                    writer.WriteLine("KMST1125: " + FormatNullableBool(result.IsKmst1125Format));
                    writer.WriteLine("Notice length: " + result.NoticeLength);
                    if (!string.IsNullOrEmpty(result.OutputPath))
                    {
                        writer.WriteLine("Output: " + result.OutputPath);
                    }
                    foreach (var part in result.Parts)
                    {
                        writer.WriteLine(part.Type + "\t" + part.FileName + "\tnewLength=" + part.NewFileLength + "\toldChecksum=" + part.OldChecksum + "\tnewChecksum=" + part.NewChecksum);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunPatchDryRun(ParsedArgs args)
        {
            if (args.Positionals.Count < 2)
            {
                throw new UsageException("Usage: wcr2 patch dry-run <patch-file> --target <dir> [--json]");
            }

            string patchFile = args.Positionals[1];
            string target = args.GetValue("target");
            bool json = args.HasFlag("json");
            string output = args.GetValue("out") ?? args.GetValue("output");

            if (string.IsNullOrEmpty(target))
            {
                throw new UsageException("patch dry-run requires --target <dir>.");
            }
            if (!File.Exists(patchFile))
            {
                throw new FileNotFoundException("Input path not found: " + patchFile);
            }
            if (!Directory.Exists(target))
            {
                throw new DirectoryNotFoundException("Target directory not found: " + target);
            }

            using (var patcher = new WzPatcher(patchFile))
            {
                long dataPosition = patcher.PrePatch(CancellationToken.None);
                var inspect = PatchInspectResultDto.FromPatcher(Path.GetFullPath(patchFile), dataPosition, patcher);
                var result = PatchDryRunResultDto.FromInspect(inspect, Path.GetFullPath(target), patcher.PatchParts);

                if (!string.IsNullOrEmpty(output))
                {
                    result.OutputPath = WriteJsonFile(result, output);
                }

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Patch: " + result.PatchFilePath);
                    writer.WriteLine("Target: " + result.TargetDirectory);
                    writer.WriteLine("Actions: create=" + result.CreateCount + " rebuild=" + result.RebuildCount + " delete=" + result.DeleteCount);
                    writer.WriteLine("Validation: ok=" + result.ValidCount + " missing=" + result.MissingCount + " mismatch=" + result.ChecksumMismatchCount + " unchecked=" + result.UncheckedCount);
                    if (!string.IsNullOrEmpty(result.OutputPath))
                    {
                        writer.WriteLine("Output: " + result.OutputPath);
                    }
                    foreach (var action in result.Actions)
                    {
                        writer.WriteLine(action.Action + "\t" + action.Status + "\t" + action.FileName);
                    }
                });
            }

            return ExitSuccess;
        }

        private static int RunPatchApply(ParsedArgs args)
        {
            if (args.Positionals.Count < 2)
            {
                throw new UsageException("Usage: wcr2 patch apply <patch-file> --target <dir> --out <dir> [--log <file>] [--json]");
            }

            string patchFile = args.Positionals[1];
            string target = args.GetValue("target");
            string output = args.GetValue("out") ?? args.GetValue("output");
            string logPath = args.GetValue("log");
            bool json = args.HasFlag("json");

            if (string.IsNullOrEmpty(target))
            {
                throw new UsageException("patch apply requires --target <dir>.");
            }
            if (string.IsNullOrEmpty(output))
            {
                throw new UsageException("patch apply requires --out <dir>.");
            }
            if (!File.Exists(patchFile))
            {
                throw new FileNotFoundException("Input path not found: " + patchFile);
            }
            if (!Directory.Exists(target))
            {
                throw new DirectoryNotFoundException("Target directory not found: " + target);
            }

            string targetFullPath = Path.GetFullPath(target);
            string outputFullPath = Path.GetFullPath(output);
            ValidatePatchOutputDirectory(targetFullPath, outputFullPath);

            using (var patcher = new WzPatcher(patchFile))
            {
                long dataPosition = patcher.PrePatch(CancellationToken.None);
                var inspect = PatchInspectResultDto.FromPatcher(Path.GetFullPath(patchFile), dataPosition, patcher);
                var dryRun = PatchDryRunResultDto.FromInspect(inspect, targetFullPath, patcher.PatchParts);

                Directory.CreateDirectory(outputFullPath);
                var copyStats = CopyDirectory(targetFullPath, outputFullPath);

                var result = PatchApplyResultDto.FromDryRun(dryRun, outputFullPath, copyStats);
                var events = new List<PatchApplyEventDto>();
                patcher.PatchingStateChanged += (sender, eventArgs) =>
                {
                    if (eventArgs != null)
                    {
                        events.Add(PatchApplyEventDto.FromEvent(eventArgs));
                    }
                };

                patcher.Patch(outputFullPath, outputFullPath, CancellationToken.None);
                result.Events = events;
                result.EventCount = events.Count;

                if (!string.IsNullOrEmpty(logPath))
                {
                    result.LogPath = WritePatchApplyLog(result, logPath);
                }

                WriteOutput(result, json, writer =>
                {
                    writer.WriteLine("Patch: " + result.PatchFilePath);
                    writer.WriteLine("Target: " + result.TargetDirectory);
                    writer.WriteLine("Output: " + result.OutputDirectory);
                    writer.WriteLine("Copied files: " + result.CopiedFileCount + " bytes=" + result.CopiedBytes);
                    writer.WriteLine("Patch parts: " + result.PartCount + " events=" + result.EventCount);
                    if (!string.IsNullOrEmpty(result.LogPath))
                    {
                        writer.WriteLine("Log: " + result.LogPath);
                    }
                });
            }

            return ExitSuccess;
        }

        private static void ValidatePatchOutputDirectory(string targetFullPath, string outputFullPath)
        {
            if (PathsEqual(targetFullPath, outputFullPath)
                || IsSubPathOf(outputFullPath, targetFullPath)
                || IsSubPathOf(targetFullPath, outputFullPath))
            {
                throw new UsageException("--out must be separate from --target.");
            }

            if (Directory.Exists(outputFullPath) && Directory.EnumerateFileSystemEntries(outputFullPath).Any())
            {
                throw new UsageException("--out directory must not exist or must be empty.");
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(TrimDirectorySeparator(left), TrimDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSubPathOf(string path, string possibleParent)
        {
            string normalizedPath = TrimDirectorySeparator(path) + Path.DirectorySeparatorChar;
            string normalizedParent = TrimDirectorySeparator(possibleParent) + Path.DirectorySeparatorChar;
            return normalizedPath.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase)
                && !PathsEqual(path, possibleParent);
        }

        private static string TrimDirectorySeparator(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static DirectoryCopyStats CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            var stats = new DirectoryCopyStats();
            foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
            }

            foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDirectory, file);
                string destination = Path.Combine(destinationDirectory, relative);
                string destinationParent = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationParent))
                {
                    Directory.CreateDirectory(destinationParent);
                }
                File.Copy(file, destination, false);
                stats.FileCount++;
                stats.Bytes += new FileInfo(file).Length;
            }

            return stats;
        }

        private static string WritePatchApplyLog(PatchApplyResultDto result, string logPath)
        {
            string fullPath = Path.GetFullPath(logPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var writer = new StreamWriter(fullPath))
            {
                writer.WriteLine("Patch: " + result.PatchFilePath);
                writer.WriteLine("Target: " + result.TargetDirectory);
                writer.WriteLine("Output: " + result.OutputDirectory);
                writer.WriteLine("CopiedFiles: " + result.CopiedFileCount);
                writer.WriteLine("CopiedBytes: " + result.CopiedBytes);
                writer.WriteLine();
                foreach (var evt in result.Events)
                {
                    writer.WriteLine(evt.State + "\t" + evt.FileName + "\t" + evt.CurrentFileLength);
                }
            }

            return fullPath;
        }

    }
}
