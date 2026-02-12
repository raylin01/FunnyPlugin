using System.Reflection;
using System.Text.Json;

namespace Funnies;

public static class ConfigPersistence
{
    public static bool TryPersist(out string details)
    {
        if (TryPersistWithPluginApi(out details))
            return true;

        if (TryPersistByPathWrite(out details))
            return true;

        return false;
    }

    private static bool TryPersistWithPluginApi(out string details)
    {
        details = "No plugin config save API found.";
        var plugin = Globals.Plugin;
        var pluginType = plugin.GetType();

        try
        {
            var methods = pluginType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var method in methods.Where(m => string.Equals(m.Name, "SaveConfig", StringComparison.Ordinal)))
            {
                var parameters = method.GetParameters();
                if (parameters.Length != 1) continue;
                if (!parameters[0].ParameterType.IsAssignableFrom(typeof(FunniesConfig)) &&
                    !typeof(FunniesConfig).IsAssignableFrom(parameters[0].ParameterType))
                    continue;

                method.Invoke(plugin, [Globals.Config]);
                details = $"Saved via plugin API ({pluginType.Name}.{method.Name}).";
                return true;
            }

            foreach (var method in methods.Where(m => string.Equals(m.Name, "SaveConfig", StringComparison.Ordinal) && m.IsGenericMethodDefinition))
            {
                var generic = method.MakeGenericMethod(typeof(FunniesConfig));
                if (generic.GetParameters().Length != 1) continue;

                generic.Invoke(plugin, [Globals.Config]);
                details = $"Saved via plugin API ({pluginType.Name}.{method.Name}<FunniesConfig>).";
                return true;
            }
        }
        catch (Exception ex)
        {
            details = $"Plugin API save failed: {ex.Message}";
            return false;
        }

        return false;
    }

    private static bool TryPersistByPathWrite(out string details)
    {
        var configPath = ResolveConfigPath();
        if (configPath == null)
        {
            details = "Could not resolve plugin config path.";
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(configPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                details = "Resolved config path had no valid directory.";
                return false;
            }

            Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(Globals.Config, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(configPath, json);
            details = $"Saved config to {configPath}";
            return true;
        }
        catch (Exception ex)
        {
            details = $"Config file write failed: {ex.Message}";
            return false;
        }
    }

    private static string? ResolveConfigPath()
    {
        var configRoot = ResolveConfigRoot();
        if (configRoot == null) return null;

        var assemblyName = Globals.Plugin.GetType().Assembly.GetName().Name ?? "Funnies";
        var pluginTypeName = Globals.Plugin.GetType().Name;
        var moduleName = Globals.Plugin.ModuleName;

        var names = new[]
        {
            assemblyName,
            pluginTypeName,
            moduleName,
            moduleName.Replace(" ", string.Empty)
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        foreach (var name in names)
        {
            var directPath = Path.Combine(configRoot, name, $"{name}.json");
            if (File.Exists(directPath)) return directPath;
        }

        if (Directory.Exists(configRoot))
        {
            try
            {
                var allJson = Directory.EnumerateFiles(configRoot, "*.json", SearchOption.AllDirectories).ToList();
                foreach (var candidate in allJson)
                {
                    var fileName = Path.GetFileNameWithoutExtension(candidate);
                    var directoryName = Path.GetFileName(Path.GetDirectoryName(candidate));
                    if (names.Contains(fileName, StringComparer.OrdinalIgnoreCase) ||
                        names.Contains(directoryName, StringComparer.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
            catch
            {
                // Ignore scan failures and use fallback below.
            }
        }

        return Path.Combine(configRoot, assemblyName, $"{assemblyName}.json");
    }

    private static string? ResolveConfigRoot()
    {
        var assemblyLocation = Globals.Plugin.GetType().Assembly.Location;
        if (string.IsNullOrWhiteSpace(assemblyLocation)) return null;

        var pluginPath = Path.GetDirectoryName(assemblyLocation);
        if (string.IsNullOrWhiteSpace(pluginPath)) return null;

        var marker = Path.Combine("addons", "counterstrikesharp");
        var normalizedPluginPath = Path.GetFullPath(pluginPath);
        var markerIndex = normalizedPluginPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0) return null;

        var cssRoot = normalizedPluginPath[..(markerIndex + marker.Length)];
        return Path.Combine(cssRoot, "configs", "plugins");
    }
}
