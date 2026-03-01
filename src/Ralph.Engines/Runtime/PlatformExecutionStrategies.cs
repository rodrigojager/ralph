namespace Ralph.Engines.Runtime;

internal interface IPlatformExecutionStrategy
{
    (string FileName, IReadOnlyList<string> Args) BuildLaunch(string command, IReadOnlyList<string>? prefixArgs, IReadOnlyList<string> args);
    PromptTransportMode ResolvePromptTransport(EngineExecutionProfile profile);
}

internal static class PlatformExecutionStrategyFactory
{
    public static IPlatformExecutionStrategy CreateCurrent()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsExecutionStrategy();
        if (OperatingSystem.IsMacOS())
            return new MacExecutionStrategy();
        return new LinuxExecutionStrategy();
    }
}

internal sealed class WindowsExecutionStrategy : IPlatformExecutionStrategy
{
    public (string FileName, IReadOnlyList<string> Args) BuildLaunch(string command, IReadOnlyList<string>? prefixArgs, IReadOnlyList<string> args)
    {
        if (!ShouldWrapWithCmd(command))
        {
            var direct = new List<string>();
            if (prefixArgs != null) direct.AddRange(prefixArgs);
            direct.AddRange(args);
            return (command, direct);
        }

        var wrapped = new List<string> { "/c", command };
        if (prefixArgs != null) wrapped.AddRange(prefixArgs);
        wrapped.AddRange(args);
        return ("cmd.exe", wrapped);
    }

    public PromptTransportMode ResolvePromptTransport(EngineExecutionProfile profile)
    {
        if (profile.EngineName is "cursor" or "gemini")
            return PromptTransportMode.Stdin;
        return profile.PromptTransport;
    }

    private static bool ShouldWrapWithCmd(string command)
    {
        if (command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(Path.GetExtension(command)))
            return false;

        return CommandExistsInPath(command + ".cmd") || CommandExistsInPath(command + ".bat");
    }

    private static bool CommandExistsInPath(string name)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var full = Path.Combine(dir.Trim(), name);
            if (File.Exists(full))
                return true;
        }
        return false;
    }
}

internal sealed class LinuxExecutionStrategy : IPlatformExecutionStrategy
{
    public (string FileName, IReadOnlyList<string> Args) BuildLaunch(string command, IReadOnlyList<string>? prefixArgs, IReadOnlyList<string> args)
    {
        var merged = new List<string>();
        if (prefixArgs != null) merged.AddRange(prefixArgs);
        merged.AddRange(args);
        return (command, merged);
    }

    public PromptTransportMode ResolvePromptTransport(EngineExecutionProfile profile) => profile.PromptTransport;
}

internal sealed class MacExecutionStrategy : IPlatformExecutionStrategy
{
    public (string FileName, IReadOnlyList<string> Args) BuildLaunch(string command, IReadOnlyList<string>? prefixArgs, IReadOnlyList<string> args)
    {
        var merged = new List<string>();
        if (prefixArgs != null) merged.AddRange(prefixArgs);
        merged.AddRange(args);
        return (command, merged);
    }

    public PromptTransportMode ResolvePromptTransport(EngineExecutionProfile profile) => profile.PromptTransport;
}
