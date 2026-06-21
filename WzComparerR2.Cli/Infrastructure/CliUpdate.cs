using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WzComparerR2.Cli
{
    internal sealed class UpdateReleaseDto
    {
        public string Repository { get; set; }
        public string ApiUrl { get; set; }
        public string CurrentVersion { get; set; }
        public string Name { get; set; }
        public string TagName { get; set; }
        public string Body { get; set; }
        public string CreatedAt { get; set; }
        public string HtmlUrl { get; set; }
        public bool? UpdateAvailable { get; set; }
        public List<UpdateAssetDto> Assets { get; set; }
        public UpdateAssetDto SelectedAsset { get; set; }

        public UpdateAssetDto SelectAsset(string assetKind)
        {
            string kind = string.IsNullOrWhiteSpace(assetKind) ? "net8" : assetKind.ToLowerInvariant();
            if (Assets == null || Assets.Count == 0)
            {
                return null;
            }

            return Assets.FirstOrDefault(asset => string.Equals(asset.Kind, kind, StringComparison.OrdinalIgnoreCase))
                ?? Assets.FirstOrDefault(asset => asset.Name != null && asset.Name.IndexOf(kind, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? Assets.FirstOrDefault(asset => string.Equals(asset.Kind, "zip", StringComparison.OrdinalIgnoreCase));
        }
    }

    internal sealed class UpdateAssetDto
    {
        public string Kind { get; set; }
        public string Name { get; set; }
        public string Label { get; set; }
        public string ContentType { get; set; }
        public string BrowserDownloadUrl { get; set; }
        public long Size { get; set; }

        public static UpdateAssetDto FromJson(JsonElement asset)
        {
            string name = GetString(asset, "name");
            return new UpdateAssetDto
            {
                Kind = DetectKind(name),
                Name = name,
                Label = GetString(asset, "label"),
                ContentType = GetString(asset, "content_type"),
                BrowserDownloadUrl = GetString(asset, "browser_download_url"),
                Size = GetLong(asset, "size")
            };
        }

        private static string DetectKind(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "zip";
            }

            string lower = name.ToLowerInvariant();
            if (lower.Contains("net10"))
            {
                return "net10";
            }
            if (lower.Contains("net8"))
            {
                return "net8";
            }
            if (lower.Contains("net6"))
            {
                return "net6";
            }
            if (lower.Contains("net462"))
            {
                return "net462";
            }
            return lower.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "zip" : "asset";
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            JsonElement value;
            return element.TryGetProperty(propertyName, out value) && value.ValueKind != JsonValueKind.Null
                ? value.GetString()
                : null;
        }

        private static long GetLong(JsonElement element, string propertyName)
        {
            JsonElement value;
            return element.TryGetProperty(propertyName, out value) && value.TryGetInt64(out long result)
                ? result
                : 0;
        }
    }

    internal sealed class UpdateDownloadResultDto
    {
        public UpdateReleaseDto Release { get; set; }
        public UpdateAssetDto Asset { get; set; }
        public string OutputPath { get; set; }

        public static UpdateDownloadResultDto FromRelease(UpdateReleaseDto release, string outputPath)
        {
            return new UpdateDownloadResultDto
            {
                Release = release,
                Asset = release.SelectedAsset,
                OutputPath = outputPath
            };
        }
    }

    internal sealed class UpdateApplyPlanDto
    {
        public string Mode { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public UpdateReleaseDto Release { get; set; }
        public UpdateAssetDto Asset { get; set; }
        public string AssetKind { get; set; }
        public string DownloadPath { get; set; }
        public string UpdaterPath { get; set; }
        public string UpdaterVersionArg { get; set; }
        public bool ProcessStarted { get; set; }
        public int? ProcessId { get; set; }

        public static UpdateApplyPlanDto FromRelease(UpdateReleaseDto release, ParsedArgs args, string assetKind)
        {
            bool execute = args.HasFlag("execute");
            var plan = new UpdateApplyPlanDto
            {
                Mode = execute ? "execute" : "dry-run",
                Success = true,
                Release = release,
                Asset = release.SelectedAsset,
                AssetKind = assetKind,
                DownloadPath = args.GetValue("download"),
                UpdaterPath = args.GetValue("updater"),
                UpdaterVersionArg = GetUpdaterVersionArg(assetKind)
            };

            if (plan.Asset == null)
            {
                plan.Success = false;
                plan.Message = "No update asset matched --asset " + assetKind + ".";
            }
            else if (execute && string.IsNullOrEmpty(plan.UpdaterPath))
            {
                plan.Success = false;
                plan.Message = "update apply --execute requires --updater <path>.";
            }
            else if (execute && string.IsNullOrEmpty(plan.UpdaterVersionArg))
            {
                plan.Success = false;
                plan.Message = "The existing external updater only supports net462, net6, and net8 assets.";
            }
            else if (execute)
            {
                plan.Message = "External updater will be launched after the asset is downloaded.";
            }
            else
            {
                plan.Message = "Dry-run only. Re-run with --execute --updater <path> to launch the external updater.";
            }

            return plan;
        }

        private static string GetUpdaterVersionArg(string kind)
        {
            switch ((kind ?? string.Empty).ToLowerInvariant())
            {
                case "net462":
                    return "4";
                case "net6":
                    return "6";
                case "net8":
                    return "8";
                default:
                    return null;
            }
        }
    }

    internal static class UpdateClient
    {
        private const string DefaultRepository = "seotbeo/WzComparerR2";

        public static async System.Threading.Tasks.Task<UpdateReleaseDto> QueryLatestAsync(ParsedArgs args)
        {
            string repository = args.GetValue("repo") ?? DefaultRepository;
            if (!Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
            {
                throw new UsageException("--repo must use owner/name format.");
            }

            string apiUrl = "https://api.github.com/repos/" + repository + "/releases/latest";
            int timeoutSeconds = args.GetInt("timeout", 15);
            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
            {
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.UserAgent.ParseAdd("wcr2-cli/" + ProgramVersion);

                using (var response = await client.SendAsync(request).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using (var document = JsonDocument.Parse(json))
                    {
                        JsonElement root = document.RootElement;
                        string currentVersion = args.GetValue("current-version") ?? GetDefaultCurrentVersion();
                        var release = new UpdateReleaseDto
                        {
                            Repository = repository,
                            ApiUrl = apiUrl,
                            CurrentVersion = currentVersion,
                            Name = GetString(root, "name"),
                            TagName = GetString(root, "tag_name"),
                            Body = GetString(root, "body"),
                            CreatedAt = GetString(root, "created_at"),
                            HtmlUrl = GetString(root, "html_url"),
                            Assets = new List<UpdateAssetDto>()
                        };
                        release.UpdateAvailable = IsUpdateAvailable(currentVersion, release.TagName ?? release.Name);

                        JsonElement assets;
                        if (root.TryGetProperty("assets", out assets) && assets.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement asset in assets.EnumerateArray())
                            {
                                release.Assets.Add(UpdateAssetDto.FromJson(asset));
                            }
                        }

                        return release;
                    }
                }
            }
        }

        public static async System.Threading.Tasks.Task<string> DownloadAssetAsync(UpdateAssetDto asset, string outputDirectory, bool force, int timeoutSeconds)
        {
            if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
            {
                throw new UsageException("Selected update asset has no download URL.");
            }

            string directory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(directory);
            string fileName = string.IsNullOrEmpty(asset.Name) ? "update.zip" : asset.Name;
            string outputPath = Path.Combine(directory, fileName);
            await DownloadAssetToFileAsync(asset, outputPath, force, timeoutSeconds).ConfigureAwait(false);
            return outputPath;
        }

        public static async System.Threading.Tasks.Task ExecuteApplyAsync(UpdateApplyPlanDto plan, bool force, int timeoutSeconds)
        {
            if (!plan.Success)
            {
                throw new UsageException(plan.Message);
            }

            string updaterPath = Path.GetFullPath(plan.UpdaterPath);
            if (!File.Exists(updaterPath))
            {
                throw new FileNotFoundException("Updater executable not found: " + updaterPath);
            }

            string downloadPath = plan.DownloadPath;
            if (string.IsNullOrEmpty(downloadPath))
            {
                downloadPath = Path.Combine(Path.GetTempPath(), "wcr2-update-" + Guid.NewGuid().ToString("N") + ".zip");
            }
            downloadPath = Path.GetFullPath(downloadPath);

            if (!File.Exists(downloadPath))
            {
                string directory = Path.GetDirectoryName(downloadPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                await DownloadAssetToFileAsync(plan.Asset, downloadPath, force, timeoutSeconds).ConfigureAwait(false);
            }

            var psi = new ProcessStartInfo(updaterPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(updaterPath)
            };
            psi.ArgumentList.Add(downloadPath);
            psi.ArgumentList.Add(plan.UpdaterVersionArg);

            var process = Process.Start(psi);
            plan.DownloadPath = downloadPath;
            plan.ProcessStarted = process != null;
            plan.ProcessId = process == null ? (int?)null : process.Id;
            plan.Message = process == null ? "Failed to start updater process." : "Updater process started.";
            plan.Success = process != null;
        }

        private static async System.Threading.Tasks.Task DownloadAssetToFileAsync(UpdateAssetDto asset, string outputPath, bool force, int timeoutSeconds)
        {
            if (File.Exists(outputPath))
            {
                if (!force)
                {
                    throw new UsageException("Output file already exists. Use --force to overwrite: " + outputPath);
                }
                File.Delete(outputPath);
            }

            using (var client = new HttpClient())
            using (var request = new HttpRequestMessage(HttpMethod.Get, asset.BrowserDownloadUrl))
            {
                client.Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds));
                if (!string.IsNullOrEmpty(asset.ContentType))
                {
                    request.Headers.Accept.ParseAdd(asset.ContentType);
                }
                request.Headers.UserAgent.ParseAdd("wcr2-cli/" + ProgramVersion);

                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    bool fileCreated = false;
                    try
                    {
                        using (var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var fileStream = File.Create(outputPath))
                        {
                            fileCreated = true;
                            await responseStream.CopyToAsync(fileStream).ConfigureAwait(false);
                        }
                    }
                    catch
                    {
                        if (fileCreated && File.Exists(outputPath))
                        {
                            File.Delete(outputPath);
                        }
                        throw;
                    }
                }
            }
        }

        private static string ProgramVersion
        {
            get { return "0.1.0"; }
        }

        private static string GetDefaultCurrentVersion()
        {
            var assembly = Assembly.GetEntryAssembly();
            var informational = assembly == null
                ? null
                : assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return string.IsNullOrEmpty(informational) ? ProgramVersion : informational;
        }

        private static string GetString(JsonElement element, string propertyName)
        {
            JsonElement value;
            return element.TryGetProperty(propertyName, out value) && value.ValueKind != JsonValueKind.Null
                ? value.GetString()
                : null;
        }

        private static bool? IsUpdateAvailable(string currentVersion, string latestVersion)
        {
            Version currentBuild;
            Version latestBuild;
            if (TryParseBuildVersion(currentVersion, out currentBuild) && TryParseBuildVersion(latestVersion, out latestBuild))
            {
                return latestBuild.Build > currentBuild.Build
                    || (latestBuild.Build == currentBuild.Build && latestBuild.Revision > currentBuild.Revision);
            }

            Version current;
            Version latest;
            if (TryParseVersion(currentVersion, out current) && TryParseVersion(latestVersion, out latest))
            {
                return latest > current;
            }

            return null;
        }

        private static bool TryParseBuildVersion(string value, out Version result)
        {
            var match = Regex.Match(value ?? string.Empty, @"(\d{6})(\d{2})$");
            if (match.Success
                && int.TryParse(match.Groups[1].Value, out int build)
                && int.TryParse(match.Groups[2].Value, out int revision))
            {
                result = new Version(0, 0, build, revision);
                return true;
            }

            result = null;
            return false;
        }

        private static bool TryParseVersion(string value, out Version result)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(1);
            }

            return Version.TryParse(normalized, out result);
        }
    }
}
