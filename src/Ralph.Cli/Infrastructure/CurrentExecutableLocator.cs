using System.Reflection;

namespace Ralph.Cli.Infrastructure;

internal static class CurrentExecutableLocator
{
    public static string? Resolve(
        string? processPath = null,
        string? baseDirectory = null,
        string? assemblyLocation = null,
        string? assemblyName = null)
    {
        processPath ??= Environment.ProcessPath;
        baseDirectory ??= AppContext.BaseDirectory;
        assemblyName ??= !string.IsNullOrWhiteSpace(assemblyLocation)
            ? Path.GetFileNameWithoutExtension(assemblyLocation)
            : Assembly.GetExecutingAssembly().GetName().Name;
        if (string.IsNullOrWhiteSpace(assemblyName))
            assemblyName = "ralph";

        var preferredHost = Path.Combine(baseDirectory, GetHostFileName(assemblyName));
        if (File.Exists(preferredHost))
            return preferredHost;

        if (!string.IsNullOrWhiteSpace(assemblyLocation) && File.Exists(assemblyLocation) && IsExecutablePath(assemblyLocation))
            return assemblyLocation;

        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
            return null;

        if (string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                assemblyName,
                StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        if (Path.GetExtension(processPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var siblingHost = Path.Combine(Path.GetDirectoryName(processPath)!, GetHostFileName(assemblyName));
            if (File.Exists(siblingHost))
                return siblingHost;
        }

        return null;
    }

    private static bool IsExecutablePath(string path)
    {
        if (OperatingSystem.IsWindows())
            return Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);

        return !Path.HasExtension(path);
    }

    private static string GetHostFileName(string assemblyName) =>
        OperatingSystem.IsWindows() ? $"{assemblyName}.exe" : assemblyName;
}
