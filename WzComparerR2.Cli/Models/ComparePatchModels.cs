using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using WzComparerR2.Common;
using WzComparerR2.Patcher;
using WzComparerR2.Patcher.Builder;
using WzComparerR2.WzLib;

namespace WzComparerR2.Cli
{
    internal sealed class CompareOptions
    {
        public string ChangeType { get; private set; }
        public int MaxResults { get; private set; }
        public bool IgnoreImageBinary { get; private set; }

        public static CompareOptions FromArgs(ParsedArgs args)
        {
            string changeType = args.GetValue("type");
            if (!string.IsNullOrEmpty(changeType)
                && !string.Equals(changeType, "added", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(changeType, "removed", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(changeType, "changed", StringComparison.OrdinalIgnoreCase))
            {
                throw new UsageException("--type must be added, removed, or changed.");
            }

            return new CompareOptions
            {
                ChangeType = changeType,
                MaxResults = args.GetInt("max-results", 1000),
                IgnoreImageBinary = args.HasFlag("ignore-image-binary")
            };
        }

        public bool Includes(string changeType)
        {
            return string.IsNullOrEmpty(this.ChangeType)
                || string.Equals(this.ChangeType, changeType, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class CompareResultDto
    {
        public string OldInputPath { get; set; }
        public string NewInputPath { get; set; }
        public string OutputPath { get; set; }
        public string Path { get; set; }
        public int Added { get; set; }
        public int Removed { get; set; }
        public int Changed { get; set; }
        public int MaxResults { get; set; }
        public bool IgnoreImageBinary { get; set; }
        public bool Truncated { get; set; }
        public List<CompareChangeDto> Differences { get; set; }

        public static CompareResultDto Create(string oldInputPath, string newInputPath, string path)
        {
            return new CompareResultDto
            {
                OldInputPath = oldInputPath,
                NewInputPath = newInputPath,
                Path = path,
                MaxResults = 1000,
                Differences = new List<CompareChangeDto>()
            };
        }

        public void Count(string changeType)
        {
            if (string.Equals(changeType, "added", StringComparison.OrdinalIgnoreCase))
            {
                this.Added++;
            }
            else if (string.Equals(changeType, "removed", StringComparison.OrdinalIgnoreCase))
            {
                this.Removed++;
            }
            else if (string.Equals(changeType, "changed", StringComparison.OrdinalIgnoreCase))
            {
                this.Changed++;
            }
        }
    }

    internal sealed class CompareChangeDto
    {
        public string Path { get; set; }
        public string ChangeType { get; set; }
        public string OldType { get; set; }
        public string NewType { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }

        public static CompareChangeDto FromNodes(Wz_Node oldNode, Wz_Node newNode)
        {
            if (oldNode == null && newNode == null)
            {
                return null;
            }

            if (oldNode == null)
            {
                return Create(newNode.FullPath, "added", null, newNode);
            }

            if (newNode == null)
            {
                return Create(oldNode.FullPath, "removed", oldNode, null);
            }

            string oldType = NodeDto.GetTypeName(oldNode.Value);
            string newType = NodeDto.GetTypeName(newNode.Value);
            string oldValue = NodeDto.FormatValue(oldNode.Value);
            string newValue = NodeDto.FormatValue(newNode.Value);
            if (string.Equals(oldType, newType, StringComparison.Ordinal)
                && string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                return null;
            }

            return new CompareChangeDto
            {
                Path = newNode.FullPath ?? oldNode.FullPath,
                ChangeType = "changed",
                OldType = oldType,
                NewType = newType,
                OldValue = oldValue,
                NewValue = newValue
            };
        }

        private static CompareChangeDto Create(string path, string changeType, Wz_Node oldNode, Wz_Node newNode)
        {
            return new CompareChangeDto
            {
                Path = path,
                ChangeType = changeType,
                OldType = oldNode == null ? null : NodeDto.GetTypeName(oldNode.Value),
                NewType = newNode == null ? null : NodeDto.GetTypeName(newNode.Value),
                OldValue = oldNode == null ? null : NodeDto.FormatValue(oldNode.Value),
                NewValue = newNode == null ? null : NodeDto.FormatValue(newNode.Value)
            };
        }
    }

    internal struct NodePair
    {
        public NodePair(Wz_Node oldNode, Wz_Node newNode)
        {
            this.OldNode = oldNode;
            this.NewNode = newNode;
        }

        public Wz_Node OldNode { get; private set; }
        public Wz_Node NewNode { get; private set; }
    }

    internal sealed class PatchInspectResultDto
    {
        public string PatchFilePath { get; set; }
        public string OutputPath { get; set; }
        public long DataPosition { get; set; }
        public bool? IsKmst1125Format { get; set; }
        public int PartCount { get; set; }
        public int CreateCount { get; set; }
        public int RebuildCount { get; set; }
        public int DeleteCount { get; set; }
        public int OldFileHashCount { get; set; }
        public int NoticeLength { get; set; }
        public string NoticeText { get; set; }
        public List<PatchPartDto> Parts { get; set; }

        public static PatchInspectResultDto FromPatcher(string patchFilePath, long dataPosition, WzPatcher patcher)
        {
            var parts = patcher.PatchParts == null
                ? new List<PatchPartDto>()
                : patcher.PatchParts.Select(PatchPartDto.FromPart).ToList();

            return new PatchInspectResultDto
            {
                PatchFilePath = patchFilePath,
                DataPosition = dataPosition,
                IsKmst1125Format = patcher.IsKMST1125Format,
                PartCount = parts.Count,
                CreateCount = parts.Count(part => part.Type == "create"),
                RebuildCount = parts.Count(part => part.Type == "rebuild"),
                DeleteCount = parts.Count(part => part.Type == "delete"),
                OldFileHashCount = patcher.OldFileHash == null ? 0 : patcher.OldFileHash.Count,
                NoticeLength = patcher.NoticeText == null ? 0 : patcher.NoticeText.Length,
                NoticeText = patcher.NoticeText,
                Parts = parts
            };
        }
    }

    internal sealed class PatchPartDto
    {
        public string FileName { get; set; }
        public string Type { get; set; }
        public string WzType { get; set; }
        public long Offset { get; set; }
        public int? OldFileLength { get; set; }
        public int NewFileLength { get; set; }
        public string OldChecksum { get; set; }
        public string NewChecksum { get; set; }

        public static PatchPartDto FromPart(PatchPartContext part)
        {
            return new PatchPartDto
            {
                FileName = part.FileName,
                Type = GetPatchTypeName(part.Type),
                WzType = part.WzType.ToString(),
                Offset = part.Offset,
                OldFileLength = part.OldFileLength,
                NewFileLength = part.NewFileLength,
                OldChecksum = part.OldChecksum.HasValue ? "0x" + part.OldChecksum.Value.ToString("x8") : null,
                NewChecksum = "0x" + part.NewChecksum.ToString("x8")
            };
        }

        private static string GetPatchTypeName(int type)
        {
            switch (type)
            {
                case 0:
                    return "create";
                case 1:
                    return "rebuild";
                case 2:
                    return "delete";
                default:
                    return "unknown";
            }
        }
    }

    internal sealed class PatchDryRunResultDto
    {
        public string PatchFilePath { get; set; }
        public string TargetDirectory { get; set; }
        public string OutputPath { get; set; }
        public bool? IsKmst1125Format { get; set; }
        public int PartCount { get; set; }
        public int CreateCount { get; set; }
        public int RebuildCount { get; set; }
        public int DeleteCount { get; set; }
        public int ValidCount { get; set; }
        public int MissingCount { get; set; }
        public int ChecksumMismatchCount { get; set; }
        public int UncheckedCount { get; set; }
        public List<PatchDryRunActionDto> Actions { get; set; }

        public static PatchDryRunResultDto FromInspect(PatchInspectResultDto inspect, string targetDirectory, List<PatchPartContext> parts)
        {
            var actions = parts == null
                ? new List<PatchDryRunActionDto>()
                : parts.Select(part => PatchDryRunActionDto.FromPart(part, targetDirectory)).ToList();

            return new PatchDryRunResultDto
            {
                PatchFilePath = inspect.PatchFilePath,
                TargetDirectory = targetDirectory,
                IsKmst1125Format = inspect.IsKmst1125Format,
                PartCount = actions.Count,
                CreateCount = actions.Count(action => action.Action == "create"),
                RebuildCount = actions.Count(action => action.Action == "rebuild"),
                DeleteCount = actions.Count(action => action.Action == "delete"),
                ValidCount = actions.Count(action => action.Status == "valid"),
                MissingCount = actions.Count(action => action.Status == "missing"),
                ChecksumMismatchCount = actions.Count(action => action.Status == "checksum-mismatch"),
                UncheckedCount = actions.Count(action => action.Status == "unchecked" || action.Status == "exists" || action.Status == "absent"),
                Actions = actions
            };
        }
    }

    internal sealed class PatchDryRunActionDto
    {
        public string FileName { get; set; }
        public string TargetPath { get; set; }
        public string Action { get; set; }
        public string Status { get; set; }
        public bool Exists { get; set; }
        public long? ExistingLength { get; set; }
        public int? NewFileLength { get; set; }
        public string ExpectedOldChecksum { get; set; }
        public string ActualOldChecksum { get; set; }
        public string NewChecksum { get; set; }
        public string Message { get; set; }

        public static PatchDryRunActionDto FromPart(PatchPartContext part, string targetDirectory)
        {
            string targetPath = Path.Combine(targetDirectory, NormalizePatchPath(part.FileName));
            bool isDirectory = part.FileName.EndsWith("\\", StringComparison.Ordinal) || part.FileName.EndsWith("/", StringComparison.Ordinal);
            bool exists = isDirectory ? Directory.Exists(targetPath) : File.Exists(targetPath);
            var dto = new PatchDryRunActionDto
            {
                FileName = part.FileName,
                TargetPath = targetPath,
                Action = GetActionName(part.Type),
                Exists = exists,
                NewFileLength = part.NewFileLength,
                ExpectedOldChecksum = part.OldChecksum.HasValue ? FormatChecksum(part.OldChecksum.Value) : null,
                NewChecksum = FormatChecksum(part.NewChecksum)
            };

            if (!isDirectory && exists)
            {
                dto.ExistingLength = new FileInfo(targetPath).Length;
            }

            switch (part.Type)
            {
                case 0:
                    dto.Status = exists ? "exists" : "absent";
                    dto.Message = exists ? "Target already exists; create would overwrite if applied." : "Target does not exist; create can add it.";
                    break;
                case 1:
                    ValidateExistingFile(dto, part, targetPath, exists);
                    break;
                case 2:
                    dto.Status = exists ? "exists" : "missing";
                    dto.Message = exists ? "Target exists and would be deleted." : "Target is already missing.";
                    break;
                default:
                    dto.Status = "unknown";
                    dto.Message = "Unknown patch action type.";
                    break;
            }

            return dto;
        }

        private static void ValidateExistingFile(PatchDryRunActionDto dto, PatchPartContext part, string targetPath, bool exists)
        {
            if (!exists)
            {
                dto.Status = "missing";
                dto.Message = "Required old file is missing.";
                return;
            }

            if (!part.OldChecksum.HasValue)
            {
                dto.Status = "unchecked";
                dto.Message = "No old checksum is available in this patch part.";
                return;
            }

            try
            {
                using (var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    uint actual = CheckSum.ComputeHash(stream, stream.Length, CancellationToken.None);
                    dto.ActualOldChecksum = FormatChecksum(actual);
                    if (actual == part.OldChecksum.Value)
                    {
                        dto.Status = "valid";
                        dto.Message = "Old checksum matches.";
                    }
                    else
                    {
                        dto.Status = "checksum-mismatch";
                        dto.Message = "Old checksum does not match.";
                    }
                }
            }
            catch (Exception ex)
            {
                dto.Status = "read-error";
                dto.Message = ex.Message;
            }
        }

        private static string NormalizePatchPath(string fileName)
        {
            string path = fileName ?? string.Empty;
            return path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string GetActionName(int type)
        {
            switch (type)
            {
                case 0:
                    return "create";
                case 1:
                    return "rebuild";
                case 2:
                    return "delete";
                default:
                    return "unknown";
            }
        }

        private static string FormatChecksum(uint checksum)
        {
            return "0x" + checksum.ToString("x8");
        }
    }

    internal sealed class DirectoryCopyStats
    {
        public int FileCount { get; set; }
        public long Bytes { get; set; }
    }

    internal sealed class PatchApplyResultDto
    {
        public string PatchFilePath { get; set; }
        public string TargetDirectory { get; set; }
        public string OutputDirectory { get; set; }
        public string LogPath { get; set; }
        public int PartCount { get; set; }
        public int CreateCount { get; set; }
        public int RebuildCount { get; set; }
        public int DeleteCount { get; set; }
        public int CopiedFileCount { get; set; }
        public long CopiedBytes { get; set; }
        public int EventCount { get; set; }
        public List<PatchApplyEventDto> Events { get; set; }

        public static PatchApplyResultDto FromDryRun(PatchDryRunResultDto dryRun, string outputDirectory, DirectoryCopyStats copyStats)
        {
            return new PatchApplyResultDto
            {
                PatchFilePath = dryRun.PatchFilePath,
                TargetDirectory = dryRun.TargetDirectory,
                OutputDirectory = outputDirectory,
                PartCount = dryRun.PartCount,
                CreateCount = dryRun.CreateCount,
                RebuildCount = dryRun.RebuildCount,
                DeleteCount = dryRun.DeleteCount,
                CopiedFileCount = copyStats.FileCount,
                CopiedBytes = copyStats.Bytes,
                Events = new List<PatchApplyEventDto>()
            };
        }
    }

    internal sealed class PatchApplyEventDto
    {
        public string State { get; set; }
        public string FileName { get; set; }
        public long CurrentFileLength { get; set; }

        public static PatchApplyEventDto FromEvent(PatchingEventArgs args)
        {
            return new PatchApplyEventDto
            {
                State = args.State.ToString(),
                FileName = args.Part == null ? null : args.Part.FileName,
                CurrentFileLength = args.CurrentFileLength
            };
        }
    }
}
