using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WzComparerR2.Headless
{
    internal sealed class SkillBatchExportOptions
    {
        public List<SkillBatchExportRequestDto> Requests { get; private set; }
        public string OutputRoot { get; private set; }
        public string ManifestPath { get; private set; }
        public string OutputPattern { get; private set; }
        public bool ContinueOnError { get; private set; }
        public bool SkipExisting { get; private set; }

        public static SkillBatchExportOptions FromArgs(ParsedArgs args)
        {
            string outputRoot = args.GetValue("out-root") ?? args.GetValue("out") ?? args.GetValue("output");
            if (string.IsNullOrWhiteSpace(outputRoot))
            {
                throw new UsageException("skill export-batch requires --out-root <output-dir>.");
            }

            var requests = new List<SkillBatchExportRequestDto>();
            AddInlineRequests(requests, args.GetValue("ids"));
            AddInlineRequests(requests, args.GetValue("id"));
            AddFileRequests(requests, args.GetValue("ids-file"));
            AddInlineNameRequests(requests, args.GetValue("names"));
            AddNameFileRequests(requests, args.GetValue("names-file"));
            if (requests.Count == 0)
            {
                throw new UsageException("skill export-batch requires --ids <id,id>, --ids-file <path>, --names <name,name>, or --names-file <path>.");
            }

            return new SkillBatchExportOptions
            {
                Requests = requests,
                OutputRoot = Path.GetFullPath(outputRoot),
                ManifestPath = args.GetValue("manifest"),
                OutputPattern = args.GetValue("output-pattern"),
                ContinueOnError = args.HasFlag("continue-on-error"),
                SkipExisting = args.HasFlag("skip-existing")
            };
        }

        private static void AddInlineRequests(List<SkillBatchExportRequestDto> requests, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (string part in value.Split(new[] { ',', ';', Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                string id = part.Trim();
                if (id.Length > 0)
                {
                    requests.Add(new SkillBatchExportRequestDto { Id = id });
                }
            }
        }

        private static void AddFileRequests(List<SkillBatchExportRequestDto> requests, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path);
            string text = File.ReadAllText(fullPath);
            string trimmed = text.TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                AddJsonFileRequests(requests, fullPath, text);
                return;
            }

            int lineNumber = 0;
            foreach (string rawLine in File.ReadLines(fullPath))
            {
                lineNumber++;
                string line = rawLine.Trim(' ', '\r', '\n');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { '\t' }, 2);
                string id = parts[0].Trim();
                if (id.Length == 0)
                {
                    throw new UsageException("skill export-batch ids file has an empty id at line " + lineNumber + ".");
                }

                requests.Add(new SkillBatchExportRequestDto
                {
                    Id = id,
                    RelativeOutput = parts.Length > 1 ? parts[1].Trim() : null
                });
            }
        }

        private static void AddJsonFileRequests(List<SkillBatchExportRequestDto> requests, string path, string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new UsageException("skill export-batch JSON ids file must be an array.");
                }

                int index = 0;
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    index++;
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        string id = element.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            requests.Add(new SkillBatchExportRequestDto { Id = id.Trim() });
                        }
                        continue;
                    }

                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        throw new UsageException("skill export-batch JSON ids file entry " + index + " must be a string or object.");
                    }

                    string objectId = ReadStringProperty(element, "id") ?? ReadStringProperty(element, "SkillId");
                    if (string.IsNullOrWhiteSpace(objectId))
                    {
                        throw new UsageException("skill export-batch JSON ids file entry " + index + " is missing id.");
                    }

                    requests.Add(new SkillBatchExportRequestDto
                    {
                        Id = objectId.Trim(),
                        RelativeOutput = ReadStringProperty(element, "relativeOutput")
                            ?? ReadStringProperty(element, "relativeOut")
                            ?? ReadStringProperty(element, "folder")
                            ?? ReadStringProperty(element, "out")
                    });
                }
            }
        }

        private static void AddInlineNameRequests(List<SkillBatchExportRequestDto> requests, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (string part in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = part.Trim();
                if (name.Length > 0)
                {
                    requests.Add(new SkillBatchExportRequestDto { Name = name });
                }
            }
        }

        private static void AddNameFileRequests(List<SkillBatchExportRequestDto> requests, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path);
            string text = File.ReadAllText(fullPath);
            string trimmed = text.TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                AddJsonNameFileRequests(requests, fullPath, text);
                return;
            }

            int lineNumber = 0;
            foreach (string rawLine in File.ReadLines(fullPath))
            {
                lineNumber++;
                string line = rawLine.Trim(' ', '\r', '\n');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                requests.Add(ParseNameLine(line.Split(new[] { '\t' }, StringSplitOptions.None), lineNumber));
            }
        }

        private static SkillBatchExportRequestDto ParseNameLine(string[] parts, int lineNumber)
        {
            if (parts.Length == 1)
            {
                string name = parts[0].Trim();
                if (name.Length == 0)
                {
                    throw new UsageException("skill export-batch names file has an empty name at line " + lineNumber + ".");
                }
                return new SkillBatchExportRequestDto { Name = name };
            }
            if (parts.Length == 2)
            {
                string jobCode = parts[0].Trim();
                string name = parts[1].Trim();
                if (name.Length == 0)
                {
                    throw new UsageException("skill export-batch names file has an empty name at line " + lineNumber + ".");
                }
                return new SkillBatchExportRequestDto { JobCode = jobCode, Name = name };
            }
            if (parts.Length == 3)
            {
                if (LooksLikeJobNameCodeNameLine(parts))
                {
                    string threePartJobName = parts[0].Trim();
                    string threePartJobCodeWithName = parts[1].Trim();
                    string threePartSkillName = parts[2].Trim();
                    if (threePartSkillName.Length == 0)
                    {
                        throw new UsageException("skill export-batch names file has an empty name at line " + lineNumber + ".");
                    }
                    return new SkillBatchExportRequestDto { JobName = threePartJobName, JobCode = threePartJobCodeWithName, Name = threePartSkillName };
                }

                string threePartJobCode = parts[0].Trim();
                string threePartName = parts[1].Trim();
                string threePartRelativeOutput = parts[2].Trim();
                if (threePartName.Length == 0)
                {
                    throw new UsageException("skill export-batch names file has an empty name at line " + lineNumber + ".");
                }
                return new SkillBatchExportRequestDto { JobCode = threePartJobCode, Name = threePartName, RelativeOutput = threePartRelativeOutput };
            }

            string jobName = parts[0].Trim();
            string fourPartJobCode = parts[1].Trim();
            string fourPartName = parts[2].Trim();
            string fourPartRelativeOutput = parts[3].Trim();
            if (fourPartName.Length == 0)
            {
                throw new UsageException("skill export-batch names file has an empty name at line " + lineNumber + ".");
            }
            return new SkillBatchExportRequestDto
            {
                JobName = jobName,
                JobCode = fourPartJobCode,
                Name = fourPartName,
                RelativeOutput = fourPartRelativeOutput
            };
        }

        private static bool LooksLikeJobNameCodeNameLine(string[] parts)
        {
            if (parts == null || parts.Length != 3)
            {
                return false;
            }

            return !LooksLikeCompactJobCode(parts[0])
                && LooksLikeCompactJobCode(parts[1])
                && !string.IsNullOrWhiteSpace(parts[2]);
        }

        private static bool LooksLikeCompactJobCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmed = value.Trim();
            return trimmed.Length <= 8
                && trimmed.Any(char.IsDigit)
                && trimmed.All(char.IsLetterOrDigit);
        }

        private static void AddJsonNameFileRequests(List<SkillBatchExportRequestDto> requests, string path, string json)
        {
            using (JsonDocument document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new UsageException("skill export-batch JSON names file must be an array.");
                }

                int index = 0;
                foreach (JsonElement element in document.RootElement.EnumerateArray())
                {
                    index++;
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        string name = element.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            requests.Add(new SkillBatchExportRequestDto { Name = name.Trim() });
                        }
                        continue;
                    }

                    if (element.ValueKind != JsonValueKind.Object)
                    {
                        throw new UsageException("skill export-batch JSON names file entry " + index + " must be a string or object.");
                    }

                    string objectName = ReadStringProperty(element, "name") ?? ReadStringProperty(element, "skillName");
                    if (string.IsNullOrWhiteSpace(objectName))
                    {
                        throw new UsageException("skill export-batch JSON names file entry " + index + " is missing name.");
                    }

                    requests.Add(new SkillBatchExportRequestDto
                    {
                        Name = objectName.Trim(),
                        JobCode = ReadStringProperty(element, "jobCode") ?? ReadStringProperty(element, "job"),
                        JobName = ReadStringProperty(element, "jobName"),
                        RelativeOutput = ReadStringProperty(element, "relativeOutput")
                            ?? ReadStringProperty(element, "relativeOut")
                            ?? ReadStringProperty(element, "folder")
                            ?? ReadStringProperty(element, "out")
                    });
                }
            }
        }

        private static string ReadStringProperty(JsonElement element, string name)
        {
            JsonElement value;
            if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }
    }

    internal static class SkillBatchExporter
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        };

        public static SkillBatchExportManifestDto Export(
            string skillInput,
            ParsedArgs args,
            SkillSpriteExportOptions exportOptions,
            SkillBatchExportOptions batchOptions)
        {
            Directory.CreateDirectory(batchOptions.OutputRoot);

            var manifest = new SkillBatchExportManifestDto
            {
                SkillInputPath = Path.GetFullPath(skillInput),
                OutputRoot = batchOptions.OutputRoot,
                StartedAt = DateTimeOffset.Now.ToString("o"),
                Items = new List<SkillBatchExportItemDto>(),
                Diagnostics = new List<string>()
            };

            using (var session = SkillSpriteExporter.OpenSession(skillInput, args, exportOptions))
            {
                using (var nameResolver = SkillNameResolver.OpenSession(session.Repository))
                {
                    foreach (var request in batchOptions.Requests)
                    {
                        var item = ExportOne(session, nameResolver, request, args, batchOptions);
                        manifest.Items.Add(item);
                        if (item.Status == "failed" && !batchOptions.ContinueOnError)
                        {
                            manifest.Diagnostics.Add("Stopped after first failed skill; use --continue-on-error to process remaining entries.");
                            break;
                        }
                    }
                }
            }

            manifest.CompletedAt = DateTimeOffset.Now.ToString("o");
            FillSummary(manifest);
            if (!string.IsNullOrWhiteSpace(batchOptions.ManifestPath))
            {
                manifest.ManifestPath = WriteManifest(manifest, batchOptions.ManifestPath);
            }
            return manifest;
        }

        public static string WriteManifest(SkillBatchExportManifestDto manifest, string path)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(fullPath, JsonSerializer.Serialize(manifest, JsonOptions));
            return fullPath;
        }

        private static SkillBatchExportItemDto ExportOne(
            SkillSpriteExporter.SkillSpriteExportSession session,
            SkillNameResolveSession nameResolver,
            SkillBatchExportRequestDto request,
            ParsedArgs args,
            SkillBatchExportOptions batchOptions)
        {
            var item = new SkillBatchExportItemDto
            {
                Id = request.Id,
                RequestedName = request.Name,
                RequestedJobName = request.JobName,
                RequestedJobCode = request.JobCode
            };

            var stopwatch = Stopwatch.StartNew();
            try
            {
                string skillId = ResolveRequestSkillId(nameResolver, request, item);
                string outputDirectory = ResolveOutputDirectory(batchOptions, request, item, skillId);
                item.Id = skillId;
                item.RelativeOutput = GetRelativeOutput(batchOptions.OutputRoot, outputDirectory);
                item.OutputDirectory = outputDirectory;

                if (batchOptions.SkipExisting && File.Exists(Path.Combine(outputDirectory, "export-result.json")))
                {
                    item.Status = "skipped-existing";
                    item.ExitCode = 0;
                    FillCountsFromExistingDirectory(item, outputDirectory);
                    return item;
                }

                SkillSpriteExportResultDto result = session.Export(skillId, outputDirectory);
                item.Status = "exported";
                item.ExitCode = 0;
                item.SkillName = result.SkillInfo == null ? null : result.SkillInfo.Name;
                item.ExportedFileCount = result.ExportedFileCount;
                item.SpriteFileCount = result.SpriteFileCount;
                item.SoundFileCount = result.SoundFileCount;
                item.VideoFileCount = result.VideoFileCount;
                item.RelatedFileCount = result.RelatedFileCount;
                item.MetadataFileCount = result.MetadataFileCount;
                item.ExportResultPath = WriteExportResult(result, outputDirectory);
                item.SkillInfoPath = Path.Combine(outputDirectory, "skill-info.json");
                item.ResourcesPath = Path.Combine(outputDirectory, "resources.json");
                return item;
            }
            catch (Exception ex) when (ex is UsageException || ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is WzLoadException)
            {
                item.Status = "failed";
                item.ExitCode = 1;
                item.Error = ex.Message;
                item.ErrorType = ex.GetType().Name;
                return item;
            }
            finally
            {
                stopwatch.Stop();
                item.DurationMs = stopwatch.ElapsedMilliseconds;
            }
        }

        private static string ResolveRequestSkillId(
            SkillNameResolveSession nameResolver,
            SkillBatchExportRequestDto request,
            SkillBatchExportItemDto item)
        {
            if (!string.IsNullOrWhiteSpace(request.Id))
            {
                return request.Id.Trim();
            }
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new UsageException("skill export-batch entry is missing id or name.");
            }

            var resolveOptions = new SkillNameSearchOptions
            {
                Name = request.Name.Trim(),
                JobCode = SkillNameSearchOptions.NormalizeJobCode(request.JobCode),
                MaxResults = 10,
                RequireData = true
            };
            SkillNameSearchResultDto resolution = nameResolver.Resolve(resolveOptions);
            item.ResolveStatus = resolution.Status;
            item.ResolveCandidateCount = resolution.ReturnedCount;
            item.ResolveCandidates = resolution.Candidates;
            if (!string.Equals(resolution.Status, "resolved", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(resolution.ResolvedId))
            {
                throw new UsageException("skill name could not be resolved: " + request.Name + " (" + resolution.Status + ")");
            }

            item.ResolvedId = resolution.ResolvedId;
            item.ResolvedName = resolution.ResolvedName;
            item.ResolvedJobCode = resolution.ResolvedJobCode;
            return resolution.ResolvedId;
        }

        private static string WriteExportResult(SkillSpriteExportResultDto result, string outputDirectory)
        {
            string path = Path.Combine(outputDirectory, "export-result.json");
            File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions));
            return path;
        }

        private static string ResolveOutputDirectory(
            SkillBatchExportOptions batchOptions,
            SkillBatchExportRequestDto request,
            SkillBatchExportItemDto item,
            string skillId)
        {
            string relative;
            if (!string.IsNullOrWhiteSpace(request.RelativeOutput))
            {
                relative = NormalizeRelativeOutput(request.RelativeOutput);
            }
            else if (!string.IsNullOrWhiteSpace(batchOptions.OutputPattern))
            {
                relative = NormalizeRelativeOutput(ApplyOutputPattern(batchOptions.OutputPattern, request, item, skillId));
            }
            else
            {
                relative = SanitizeSegment(skillId);
            }

            return Path.GetFullPath(Path.Combine(batchOptions.OutputRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string ApplyOutputPattern(
            string pattern,
            SkillBatchExportRequestDto request,
            SkillBatchExportItemDto item,
            string skillId)
        {
            string name = FirstNonEmpty(item.ResolvedName, request.Name, skillId);
            string jobCode = FirstNonEmpty(item.ResolvedJobCode, request.JobCode, "unknown");
            string jobName = FirstNonEmpty(request.JobName, jobCode);
            string result = pattern ?? string.Empty;
            result = result.Replace("{id}", SanitizeSegment(skillId));
            result = result.Replace("{skillId}", SanitizeSegment(skillId));
            result = result.Replace("{name}", SanitizeSegment(name));
            result = result.Replace("{skillName}", SanitizeSegment(name));
            result = result.Replace("{jobCode}", SanitizeSegment(jobCode));
            result = result.Replace("{job}", SanitizeSegment(jobCode));
            result = result.Replace("{jobName}", SanitizeSegment(jobName));
            result = result.Replace("{requestedName}", SanitizeSegment(request.Name ?? string.Empty));
            return result;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            return string.Empty;
        }

        private static string NormalizeRelativeOutput(string value)
        {
            string normalized = (value ?? string.Empty).Replace('\\', '/').Trim('/');
            if (normalized.Length == 0)
            {
                throw new UsageException("skill export-batch relative output cannot be empty.");
            }
            if (Path.IsPathRooted(value) || normalized.Split('/').Any(part => part == ".." || part.Length == 0))
            {
                throw new UsageException("skill export-batch relative output must stay under --out-root.");
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var segments = new List<string>();
            foreach (string segment in normalized.Split('/'))
            {
                segments.Add(new string(segment.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()));
            }
            return string.Join("/", segments);
        }

        private static string GetRelativeOutput(string outputRoot, string outputDirectory)
        {
            Uri root = new Uri(AppendDirectorySeparator(Path.GetFullPath(outputRoot)));
            Uri output = new Uri(AppendDirectorySeparator(Path.GetFullPath(outputDirectory)));
            return Uri.UnescapeDataString(root.MakeRelativeUri(output).ToString()).TrimEnd('/');
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }
            return path + Path.DirectorySeparatorChar;
        }

        private static void FillCountsFromExistingDirectory(SkillBatchExportItemDto item, string outputDirectory)
        {
            if (!Directory.Exists(outputDirectory))
            {
                return;
            }

            var files = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories).ToList();
            item.ExportedFileCount = files.Count(IsExportedAsset);
            item.MetadataFileCount = files.Count(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase));
            item.SpriteFileCount = files.Count(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase));
            item.SoundFileCount = files.Count(path => string.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase));
            item.VideoFileCount = files.Count(path => string.Equals(Path.GetExtension(path), ".mcv", StringComparison.OrdinalIgnoreCase)
                || IsUnderSegment(path, "video"));
            item.ExportResultPath = Path.Combine(outputDirectory, "export-result.json");
            item.SkillInfoPath = Path.Combine(outputDirectory, "skill-info.json");
            item.ResourcesPath = Path.Combine(outputDirectory, "resources.json");
        }

        private static bool IsExportedAsset(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension != ".json";
        }

        private static bool IsUnderSegment(string path, string segment)
        {
            return path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => string.Equals(part, segment, StringComparison.OrdinalIgnoreCase));
        }

        private static void FillSummary(SkillBatchExportManifestDto manifest)
        {
            manifest.Total = manifest.Items.Count;
            manifest.Succeeded = manifest.Items.Count(item => item.Status == "exported");
            manifest.Skipped = manifest.Items.Count(item => item.Status == "skipped-existing");
            manifest.Failed = manifest.Items.Count(item => item.Status == "failed");
            manifest.ExportedFileCount = manifest.Items.Sum(item => item.ExportedFileCount);
            manifest.SpriteFileCount = manifest.Items.Sum(item => item.SpriteFileCount);
            manifest.SoundFileCount = manifest.Items.Sum(item => item.SoundFileCount);
            manifest.VideoFileCount = manifest.Items.Sum(item => item.VideoFileCount);
            manifest.RelatedFileCount = manifest.Items.Sum(item => item.RelatedFileCount);
            manifest.MetadataFileCount = manifest.Items.Sum(item => item.MetadataFileCount);
            manifest.DurationMs = manifest.Items.Sum(item => item.DurationMs);
        }

        private static string SanitizeSegment(string value)
        {
            string normalized = SkillSpriteExportOptions.NormalizePath(value);
            if (normalized.Length == 0)
            {
                return "_";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            return new string(normalized.Select(ch => ch == '/' || invalid.Contains(ch) ? '_' : ch).ToArray());
        }
    }

    internal sealed class SkillBatchExportRequestDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string JobName { get; set; }
        public string JobCode { get; set; }
        public string RelativeOutput { get; set; }
    }

    internal sealed class SkillBatchExportManifestDto
    {
        public string SkillInputPath { get; set; }
        public string OutputRoot { get; set; }
        public string ManifestPath { get; set; }
        public string StartedAt { get; set; }
        public string CompletedAt { get; set; }
        public long DurationMs { get; set; }
        public int Total { get; set; }
        public int Succeeded { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public int ExportedFileCount { get; set; }
        public int SpriteFileCount { get; set; }
        public int SoundFileCount { get; set; }
        public int VideoFileCount { get; set; }
        public int RelatedFileCount { get; set; }
        public int MetadataFileCount { get; set; }
        public List<string> Diagnostics { get; set; }
        public List<SkillBatchExportItemDto> Items { get; set; }
    }

    internal sealed class SkillBatchExportItemDto
    {
        public string Id { get; set; }
        public string RequestedName { get; set; }
        public string RequestedJobName { get; set; }
        public string RequestedJobCode { get; set; }
        public string ResolvedId { get; set; }
        public string ResolvedName { get; set; }
        public string ResolvedJobCode { get; set; }
        public string ResolveStatus { get; set; }
        public int ResolveCandidateCount { get; set; }
        public List<SkillNameCandidateDto> ResolveCandidates { get; set; }
        public string SkillName { get; set; }
        public string RelativeOutput { get; set; }
        public string OutputDirectory { get; set; }
        public string Status { get; set; }
        public int ExitCode { get; set; }
        public string Error { get; set; }
        public string ErrorType { get; set; }
        public long DurationMs { get; set; }
        public int ExportedFileCount { get; set; }
        public int SpriteFileCount { get; set; }
        public int SoundFileCount { get; set; }
        public int VideoFileCount { get; set; }
        public int RelatedFileCount { get; set; }
        public int MetadataFileCount { get; set; }
        public string ExportResultPath { get; set; }
        public string SkillInfoPath { get; set; }
        public string ResourcesPath { get; set; }
    }
}
