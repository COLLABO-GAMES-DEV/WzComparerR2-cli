using System;
using System.Linq;

namespace WzComparerR2.Cli
{
    internal static partial class Program
    {
        private static async System.Threading.Tasks.Task<int> RunUpdate(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintUpdateHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            if (subCommand != "check" && subCommand != "download" && subCommand != "apply")
            {
                throw new UsageException("Unknown update command: " + args.Positionals[0]);
            }

            if (subCommand == "download" && string.IsNullOrEmpty(args.GetValue("out")))
            {
                throw new UsageException("update download requires --out <dir>.");
            }

            string assetKind = args.GetValue("asset") ?? "net8";
            if (!IsUpdateAssetKind(assetKind))
            {
                throw new UsageException("--asset must be net8, net10, net6, net462, or zip.");
            }
            var result = await UpdateClient.QueryLatestAsync(args).ConfigureAwait(false);
            result.SelectedAsset = result.SelectAsset(assetKind);

            if (subCommand == "download")
            {
                if (result.SelectedAsset == null)
                {
                    throw new UsageException("No update asset matched --asset " + assetKind + ".");
                }

                string outputPath = await UpdateClient.DownloadAssetAsync(result.SelectedAsset, args.GetValue("out"), args.HasFlag("force"), args.GetInt("timeout", 30)).ConfigureAwait(false);
                var download = UpdateDownloadResultDto.FromRelease(result, outputPath);
                WriteOutput(download, args.HasFlag("json"),
                    writer =>
                    {
                        writer.WriteLine("Downloaded: " + download.OutputPath);
                        writer.WriteLine("Asset: " + download.Asset.Name + " (" + download.Asset.Size + " bytes)");
                    });
                return ExitSuccess;
            }

            if (subCommand == "apply")
            {
                var plan = UpdateApplyPlanDto.FromRelease(result, args, assetKind);
                if (args.HasFlag("execute"))
                {
                    await UpdateClient.ExecuteApplyAsync(plan, args.HasFlag("force"), args.GetInt("timeout", 30)).ConfigureAwait(false);
                }

                WriteOutput(plan, args.HasFlag("json"),
                    writer =>
                    {
                        writer.WriteLine("Update apply mode: " + plan.Mode);
                        writer.WriteLine("Release: " + plan.Release.TagName + " updateAvailable=" + FormatNullableBool(plan.Release.UpdateAvailable));
                        writer.WriteLine("Asset: " + (plan.Asset == null ? "(none)" : plan.Asset.Name));
                        writer.WriteLine("Updater: " + (plan.UpdaterPath ?? "(required for --execute)"));
                        writer.WriteLine(plan.Message);
                    });
                return plan.Success ? ExitSuccess : ExitUsage;
            }

            WriteOutput(result, args.HasFlag("json"),
                writer =>
                {
                    writer.WriteLine("Repository: " + result.Repository);
                    writer.WriteLine("Current: " + (result.CurrentVersion ?? "(unknown)"));
                    writer.WriteLine("Latest: " + (result.TagName ?? result.Name));
                    writer.WriteLine("Update available: " + FormatNullableBool(result.UpdateAvailable));
                    if (result.SelectedAsset != null)
                    {
                        writer.WriteLine("Selected asset: " + result.SelectedAsset.Name);
                        writer.WriteLine("Download: " + result.SelectedAsset.BrowserDownloadUrl);
                    }
            });
            return ExitSuccess;
        }

        private static int RunConfig(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintConfigHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            var store = CliConfigStore.Open(args.GetValue("config"));
            string profile = GetConfigProfile(args);

            switch (subCommand)
            {
                case "path":
                {
                    var result = ConfigPathDto.FromStore(store);
                    WriteOutput(result, args.HasFlag("json"),
                        writer =>
                        {
                            writer.WriteLine(result.Path);
                            writer.WriteLine("exists=" + result.Exists.ToString().ToLowerInvariant());
                        });
                    return ExitSuccess;
                }
                case "list":
                {
                    var result = ConfigListDto.FromStore(store, profile);
                    WriteOutput(result, args.HasFlag("json"),
                        writer =>
                        {
                            if (result.Values.Count == 0)
                            {
                                writer.WriteLine("(empty)");
                                return;
                            }

                            foreach (var item in result.Values)
                            {
                                writer.WriteLine(item.Key + "=" + item.Value);
                            }
                        });
                    return ExitSuccess;
                }
                case "get":
                {
                    if (args.Positionals.Count < 2)
                    {
                        throw new UsageException("Usage: wcr2 config get <key> [--json]");
                    }

                    string key = ResolveConfigKey(args.Positionals[1], profile);
                    var result = ConfigValueDto.FromStore(store, key);
                    WriteOutput(result, args.HasFlag("json"),
                        writer =>
                        {
                            if (result.Found)
                            {
                                writer.WriteLine(result.Value);
                            }
                            else
                            {
                                writer.WriteLine("Config key not found: " + key);
                            }
                        });
                    return result.Found ? ExitSuccess : ExitNotFound;
                }
                case "set":
                {
                    if (args.Positionals.Count < 3)
                    {
                        throw new UsageException("Usage: wcr2 config set <key> <value> [--json]");
                    }

                    string key = ResolveConfigKey(args.Positionals[1], profile);
                    string value = args.Positionals[2];
                    store.Set(key, value);
                    store.Save();
                    var result = ConfigValueDto.FromStore(store, key);
                    WriteOutput(result, args.HasFlag("json"),
                        writer => writer.WriteLine(result.Key + "=" + result.Value));
                    return ExitSuccess;
                }
                case "unset":
                {
                    if (args.Positionals.Count < 2)
                    {
                        throw new UsageException("Usage: wcr2 config unset <key> [--json]");
                    }

                    string key = ResolveConfigKey(args.Positionals[1], profile);
                    bool removed = store.Unset(key);
                    store.Save();
                    var result = new ConfigUnsetDto
                    {
                        Path = store.Path,
                        Key = key,
                        Removed = removed,
                        Count = store.Values.Count
                    };
                    WriteOutput(result, args.HasFlag("json"),
                        writer => writer.WriteLine((removed ? "removed " : "not found ") + key));
                    return ExitSuccess;
                }
                default:
                    throw new UsageException("Unknown config command: " + args.Positionals[0]);
            }
        }

        private static int RunPlugin(ParsedArgs args)
        {
            if (args.Positionals.Count == 0 || IsHelp(args.Positionals[0]))
            {
                PrintPluginHelp();
                return ExitSuccess;
            }

            string subCommand = args.Positionals[0].ToLowerInvariant();
            bool json = args.HasFlag("json");

            switch (subCommand)
            {
                case "list":
                {
                    var result = CliPluginRegistry.Discover(args);
                    WriteOutput(result, json,
                        writer =>
                        {
                            writer.WriteLine("Plugin directories:");
                            foreach (string directory in result.Directories)
                            {
                                writer.WriteLine("- " + directory);
                            }
                            writer.WriteLine("Plugins: " + result.Plugins.Count + " loadFailed=" + result.LoadFailedCount);
                            foreach (var plugin in result.Plugins)
                            {
                                writer.WriteLine(plugin.Status + "\tcommands=" + plugin.Commands.Count + "\t" + plugin.Path);
                                if (!string.IsNullOrEmpty(plugin.Error))
                                {
                                    writer.WriteLine("  error: " + plugin.Error);
                                }
                            }
                        });
                    return ExitSuccess;
                }
                case "inspect":
                {
                    if (args.Positionals.Count < 2)
                    {
                        throw new UsageException("Usage: wcr2 plugin inspect <assembly.dll> [--json]");
                    }

                    var result = CliPluginRegistry.InspectFile(args.Positionals[1]);
                    WriteOutput(result, json,
                        writer =>
                        {
                            writer.WriteLine(result.Status + "\t" + result.Path);
                            writer.WriteLine("Assembly: " + (result.AssemblyName ?? "(unknown)"));
                            writer.WriteLine("Version: " + (result.AssemblyVersion ?? "(unknown)"));
                            writer.WriteLine("CLI providers: " + result.ProviderTypes.Count + " GUI entries: " + result.GuiPluginEntryTypes.Count);
                            foreach (var command in result.Commands)
                            {
                                writer.WriteLine(command.Name + "\t" + command.ProviderType + FormatOptionalValue(command.Summary));
                            }
                            if (!string.IsNullOrEmpty(result.Error))
                            {
                                writer.WriteLine("Error: " + result.Error);
                            }
                        });
                    return result.Success ? ExitSuccess : ExitInternalError;
                }
                case "commands":
                {
                    var result = CliPluginRegistry.Discover(args);
                    var commands = result.Plugins
                        .SelectMany(plugin => plugin.Commands.Select(command => new CliPluginCommandListItemDto
                        {
                            PluginPath = plugin.Path,
                            ProviderType = command.ProviderType,
                            Name = command.Name,
                            Summary = command.Summary,
                            Usage = command.Usage
                        }))
                        .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(command => command.PluginPath, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    WriteOutput(commands, json,
                        writer =>
                        {
                            if (commands.Count == 0)
                            {
                                writer.WriteLine("(no cli plugin commands)");
                                return;
                            }

                            foreach (var command in commands)
                            {
                                writer.WriteLine(command.Name + "\t" + command.PluginPath + FormatOptionalValue(command.Summary));
                            }
                        });
                    return ExitSuccess;
                }
                case "run":
                {
                    if (args.Positionals.Count < 2)
                    {
                        throw new UsageException("Usage: wcr2 plugin run <command> [args...] [--plugin-dir <dir>]");
                    }

                    string commandName = args.Positionals[1];
                    var result = CliPluginRegistry.Execute(args, commandName, GetPluginCommandArguments(args));
                    WriteOutput(result, json,
                        writer =>
                        {
                            writer.WriteLine("Plugin command: " + result.Command);
                            writer.WriteLine("Provider: " + (result.ProviderType ?? "(not found)"));
                            writer.WriteLine("ExitCode: " + result.ExitCode);
                            if (!string.IsNullOrEmpty(result.Message))
                            {
                                writer.WriteLine(result.Message);
                            }
                            if (!string.IsNullOrEmpty(result.Error))
                            {
                                writer.WriteLine("Error: " + result.Error);
                            }
                        });
                    return result.ExitCode;
                }
                default:
                    throw new UsageException("Unknown plugin command: " + args.Positionals[0]);
            }
        }

    }
}
