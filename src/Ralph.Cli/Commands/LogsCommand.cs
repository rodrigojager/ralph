using Ralph.Persistence.Workspace;
using Ralph.Core.Localization;

namespace Ralph.Cli.Commands;

public sealed class LogsCommand
{
    private readonly WorkspaceInitializer _workspaceInit;

    public LogsCommand(WorkspaceInitializer workspaceInit)
    {
        _workspaceInit = workspaceInit;
    }

    public async Task<int> ExecuteAsync(string workingDirectory, string subCommand, bool follow, string? level, string? since, IStringCatalog s, CancellationToken cancellationToken = default)
    {
        if (subCommand.ToLowerInvariant() != "tail")
        {
            Console.Error.WriteLine(s.Get("logs.usage"));
            return 1;
        }
        var activityPath = _workspaceInit.GetActivityLogPath(workingDirectory);
        var errorsPath = _workspaceInit.GetErrorsLogPath(workingDirectory);
        if (!File.Exists(activityPath) && !File.Exists(errorsPath))
        {
            Console.WriteLine(s.Get("logs.no_logs"));
            return 0;
        }
        var normalizedLevel = (level ?? "info").ToLowerInvariant();
        var showAll = normalizedLevel == "all";
        var path = normalizedLevel == "error" ? errorsPath : activityPath;
        if (!showAll && !File.Exists(path))
        {
            Console.WriteLine(s.Get("logs.no_logs"));
            return 0;
        }
        if (!follow)
        {
            var lines = showAll
                ? ReadMergedLines(activityPath, errorsPath)
                : await File.ReadAllLinesAsync(path, cancellationToken);
            foreach (var line in ApplySinceFilter(lines, since))
                Console.WriteLine(line);
            return 0;
        }
        if (showAll)
        {
            Console.Error.WriteLine(s.Get("logs.follow_all_unsupported"));
            return 1;
        }
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line != null && ShouldIncludeLine(line, since))
                Console.WriteLine(line);
            else await Task.Delay(500, cancellationToken);
        }
        return 0;
    }

    private static string[] ReadMergedLines(string activityPath, string errorsPath)
    {
        var list = new List<(DateTimeOffset? Ts, string Line)>();
        if (File.Exists(activityPath))
            list.AddRange(File.ReadAllLines(activityPath).Select(l => (ParseTimestamp(l), l)));
        if (File.Exists(errorsPath))
            list.AddRange(File.ReadAllLines(errorsPath).Select(l => (ParseTimestamp(l), l)));
        return list
            .OrderBy(x => x.Ts ?? DateTimeOffset.MinValue)
            .Select(x => x.Line)
            .ToArray();
    }

    private static IEnumerable<string> ApplySinceFilter(IEnumerable<string> lines, string? since)
    {
        foreach (var line in lines)
        {
            if (ShouldIncludeLine(line, since))
                yield return line;
        }
    }

    private static bool ShouldIncludeLine(string line, string? since)
    {
        if (string.IsNullOrWhiteSpace(since))
            return true;
        if (!DateTimeOffset.TryParse(since, out var sinceTime))
            return true;
        if (!line.StartsWith("[", StringComparison.Ordinal))
            return true;
        var end = line.IndexOf(']');
        if (end <= 1)
            return true;
        var ts = line[1..end];
        if (!DateTimeOffset.TryParse(ts, out var entryTime))
            return true;
        return entryTime >= sinceTime;
    }

    private static DateTimeOffset? ParseTimestamp(string line)
    {
        if (!line.StartsWith("[", StringComparison.Ordinal))
            return null;
        var end = line.IndexOf(']');
        if (end <= 1)
            return null;
        var ts = line[1..end];
        return DateTimeOffset.TryParse(ts, out var parsed) ? parsed : null;
    }
}
