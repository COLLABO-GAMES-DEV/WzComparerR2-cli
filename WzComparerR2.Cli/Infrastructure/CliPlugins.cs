using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace WzComparerR2.Cli
{
    public interface ICliCommandProvider
    {
        IEnumerable<CliCommandDescriptor> GetCommands();

        int Execute(string commandName, IReadOnlyList<string> args, ICliCommandContext context);
    }

    public interface ICliCommandContext
    {
        TextWriter Output { get; }
        TextWriter Error { get; }
        string WorkingDirectory { get; }
        string CliVersion { get; }
        string GetConfigValue(string key);
    }

    public sealed class CliCommandDescriptor
    {
        public string Name { get; set; }
        public string Summary { get; set; }
        public string Usage { get; set; }
    }

    internal sealed class CliCommandContext : ICliCommandContext
    {
        private readonly CliConfigStore store;
        private readonly TextWriter output;
        private readonly TextWriter error;

        public CliCommandContext(CliConfigStore store, TextWriter output, TextWriter error)
        {
            this.store = store;
            this.output = output;
            this.error = error;
        }

        public TextWriter Output => this.output;

        public TextWriter Error => this.error;

        public string WorkingDirectory => Directory.GetCurrentDirectory();

        public string CliVersion => "0.1.0";

        public string GetConfigValue(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            string value;
            return this.store.Values.TryGetValue(key, out value) ? value : null;
        }
    }

    internal sealed class CliPluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;

        public CliPluginLoadContext(string pluginPath)
            : base(isCollectible: true)
        {
            this.resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            Assembly current = typeof(ICliCommandProvider).Assembly;
            if (string.Equals(assemblyName.Name, current.GetName().Name, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            string assemblyPath = this.resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath == null ? null : LoadFromAssemblyPath(assemblyPath);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string libraryPath = this.resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return libraryPath == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(libraryPath);
        }
    }

    internal static class CliPluginRegistry
    {
        public static CliPluginDiscoveryDto Discover(ParsedArgs args)
        {
            var directories = ResolveDirectories(args);
            var result = new CliPluginDiscoveryDto
            {
                Directories = directories,
                Plugins = new List<CliPluginDto>()
            };

            foreach (string directory in directories)
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    result.Plugins.Add(InspectFile(file));
                }
            }

            result.PluginCount = result.Plugins.Count;
            result.LoadFailedCount = result.Plugins.Count(plugin => !plugin.Success);
            return result;
        }

        public static CliPluginDto InspectFile(string file)
        {
            string fullPath = Path.GetFullPath(file);
            var dto = new CliPluginDto
            {
                Path = fullPath,
                Status = "failed",
                ProviderTypes = new List<string>(),
                GuiPluginEntryTypes = new List<string>(),
                Commands = new List<CliPluginCommandDto>()
            };

            if (!File.Exists(fullPath))
            {
                dto.Error = "Plugin assembly not found.";
                return dto;
            }

            try
            {
                AssemblyName name = AssemblyName.GetAssemblyName(fullPath);
                dto.AssemblyName = name.Name;
                dto.AssemblyVersion = name.Version?.ToString();
            }
            catch (Exception ex)
            {
                dto.Error = "Invalid assembly: " + ex.Message;
                return dto;
            }

            var context = new CliPluginLoadContext(fullPath);
            try
            {
                Assembly assembly = context.LoadFromAssemblyPath(fullPath);
                var types = GetLoadableTypes(assembly, dto);
                foreach (Type type in types)
                {
                    if (typeof(ICliCommandProvider).IsAssignableFrom(type) && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) != null)
                    {
                        dto.ProviderTypes.Add(type.FullName);
                        AddCommands(dto, type);
                    }
                    else if (IsGuiPluginEntry(type))
                    {
                        dto.GuiPluginEntryTypes.Add(type.FullName);
                    }
                }

                dto.Status = dto.ProviderTypes.Count > 0 ? "cli-plugin" : dto.GuiPluginEntryTypes.Count > 0 ? "gui-plugin" : "assembly";
                dto.Success = true;
                return dto;
            }
            catch (Exception ex)
            {
                dto.Error = ex.Message;
                return dto;
            }
            finally
            {
                context.Unload();
            }
        }

        public static CliPluginRunResultDto Execute(ParsedArgs args, string commandName, IReadOnlyList<string> commandArgs)
        {
            var discovery = Discover(args);
            var result = new CliPluginRunResultDto
            {
                Command = commandName,
                ExitCode = 1,
                SearchedPluginCount = discovery.Plugins.Count
            };

            if (discovery.LoadFailedCount > 0)
            {
                result.Message = "Some plugin assemblies failed to load; continuing with available CLI providers.";
            }

            foreach (var plugin in discovery.Plugins.Where(plugin => plugin.Success && plugin.Commands.Any(command => CommandNameMatches(command.Name, commandName))))
            {
                string fullPath = plugin.Path;
                var context = new CliPluginLoadContext(fullPath);
                try
                {
                    Assembly assembly = context.LoadFromAssemblyPath(fullPath);
                    foreach (Type type in GetLoadableTypes(assembly, null))
                    {
                        if (!typeof(ICliCommandProvider).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
                        {
                            continue;
                        }

                        var provider = (ICliCommandProvider)Activator.CreateInstance(type);
                        var commands = SafeGetCommands(provider);
                        if (!commands.Any(command => CommandNameMatches(command.Name, commandName)))
                        {
                            continue;
                        }

                        bool captureOutput = args.HasFlag("json");
                        var outputWriter = captureOutput ? new StringWriter() : Console.Out;
                        var errorWriter = captureOutput ? new StringWriter() : Console.Error;
                        result.PluginPath = fullPath;
                        result.ProviderType = type.FullName;
                        result.ExitCode = provider.Execute(commandName, commandArgs, new CliCommandContext(CliConfigStore.Open(args.GetValue("config")), outputWriter, errorWriter));
                        if (captureOutput)
                        {
                            result.Stdout = outputWriter.ToString();
                            result.Stderr = errorWriter.ToString();
                        }
                        result.Success = result.ExitCode == 0;
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    result.PluginPath = fullPath;
                    result.Error = ex.Message;
                    result.ExitCode = 5;
                    return result;
                }
                finally
                {
                    context.Unload();
                }
            }

            result.Error = "No CLI plugin command matched: " + commandName;
            return result;
        }

        private static List<string> ResolveDirectories(ParsedArgs args)
        {
            var directories = new List<string>();
            AddDirectory(directories, args.GetValue("plugin-dir"));

            var store = CliConfigStore.Open(args.GetValue("config"));
            string configured;
            if (store.Values.TryGetValue("plugin-dir", out configured))
            {
                AddDirectory(directories, configured);
            }

            string env = Environment.GetEnvironmentVariable("WCR2_CLI_PLUGIN_DIR");
            if (!string.IsNullOrWhiteSpace(env))
            {
                foreach (string item in env.Split(Path.PathSeparator))
                {
                    AddDirectory(directories, item);
                }
            }

            AddDirectory(directories, Path.Combine(AppContext.BaseDirectory, "CliPlugin"));
            AddDirectory(directories, Path.Combine(Directory.GetCurrentDirectory(), "CliPlugin"));

            if (args.HasFlag("include-gui-plugin-dir"))
            {
                AddDirectory(directories, Path.Combine(AppContext.BaseDirectory, "Plugin"));
                AddDirectory(directories, Path.Combine(Directory.GetCurrentDirectory(), "Plugin"));
            }

            return directories;
        }

        private static void AddDirectory(List<string> directories, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string fullPath = Path.GetFullPath(path);
            if (!directories.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                directories.Add(fullPath);
            }
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly, CliPluginDto dto)
        {
            try
            {
                return assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                if (dto != null)
                {
                    dto.Error = string.Join(Environment.NewLine, ex.LoaderExceptions.Where(item => item != null).Select(item => item.Message));
                }
                return ex.Types.Where(type => type != null && type.IsPublic);
            }
        }

        private static void AddCommands(CliPluginDto dto, Type providerType)
        {
            try
            {
                var provider = (ICliCommandProvider)Activator.CreateInstance(providerType);
                foreach (var command in SafeGetCommands(provider))
                {
                    if (string.IsNullOrWhiteSpace(command.Name))
                    {
                        continue;
                    }

                    dto.Commands.Add(new CliPluginCommandDto
                    {
                        ProviderType = providerType.FullName,
                        Name = command.Name,
                        Summary = command.Summary,
                        Usage = command.Usage
                    });
                }
            }
            catch (Exception ex)
            {
                dto.Error = "Provider discovery failed for " + providerType.FullName + ": " + ex.Message;
            }
        }

        private static List<CliCommandDescriptor> SafeGetCommands(ICliCommandProvider provider)
        {
            return (provider.GetCommands() ?? Enumerable.Empty<CliCommandDescriptor>())
                .Where(command => command != null)
                .ToList();
        }

        private static bool CommandNameMatches(string left, string right)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsGuiPluginEntry(Type type)
        {
            for (Type current = type.BaseType; current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, "WzComparerR2.PluginBase.PluginEntry", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal sealed class CliPluginDiscoveryDto
    {
        public List<string> Directories { get; set; }
        public int PluginCount { get; set; }
        public int LoadFailedCount { get; set; }
        public List<CliPluginDto> Plugins { get; set; }
    }

    internal sealed class CliPluginDto
    {
        public string Path { get; set; }
        public string AssemblyName { get; set; }
        public string AssemblyVersion { get; set; }
        public string Status { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public List<string> ProviderTypes { get; set; }
        public List<string> GuiPluginEntryTypes { get; set; }
        public List<CliPluginCommandDto> Commands { get; set; }
    }

    internal sealed class CliPluginCommandDto
    {
        public string ProviderType { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        public string Usage { get; set; }
    }

    internal sealed class CliPluginCommandListItemDto
    {
        public string PluginPath { get; set; }
        public string ProviderType { get; set; }
        public string Name { get; set; }
        public string Summary { get; set; }
        public string Usage { get; set; }
    }

    internal sealed class CliPluginRunResultDto
    {
        public string Command { get; set; }
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public int SearchedPluginCount { get; set; }
        public string PluginPath { get; set; }
        public string ProviderType { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
        public string Stdout { get; set; }
        public string Stderr { get; set; }
    }
}
