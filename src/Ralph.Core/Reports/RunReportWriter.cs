using System.Text;
using System.Text.Json;
using Ralph.Persistence.Workspace;

namespace Ralph.Core.Reports;

public sealed class RunReportWriter
{
    private readonly WorkspaceInitializer _workspace;

    public RunReportWriter(WorkspaceInitializer workspace)
    {
        _workspace = workspace;
    }

    public void Write(
        string workingDirectory,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        bool completed,
        IReadOnlyList<RunReportTaskEntry> tasks)
    {
        var reportsDir = _workspace.GetReportsDir(workingDirectory);
        Directory.CreateDirectory(reportsDir);

        var report = new RunReportDocument
        {
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            DurationSeconds = (endedAtUtc - startedAtUtc).TotalSeconds,
            Completed = completed,
            Tasks = tasks.ToList()
        };

        var timestamp = startedAtUtc.ToString("yyyyMMdd-HHmmss");
        var jsonPath = Path.Combine(reportsDir, $"{timestamp}.json");
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(jsonPath, json);

        var latestMd = BuildLatestMarkdown(report, jsonPath, workingDirectory);
        File.WriteAllText(_workspace.GetLatestReportMarkdownPath(workingDirectory), latestMd);
    }

    private static string BuildLatestMarkdown(RunReportDocument report, string jsonPath, string workingDirectory)
    {
        var relJson = Path.GetRelativePath(workingDirectory, jsonPath).Replace('\\', '/');
        var sb = new StringBuilder();
        sb.AppendLine("# Ralph Run Report");
        sb.AppendLine();
        sb.AppendLine($"- Started (UTC): {report.StartedAtUtc:O}");
        sb.AppendLine($"- Ended (UTC): {report.EndedAtUtc:O}");
        sb.AppendLine($"- Duration (s): {report.DurationSeconds:F1}");
        sb.AppendLine($"- Completed: {report.Completed}");
        sb.AppendLine($"- JSON: `{relJson}`");
        sb.AppendLine();
        sb.AppendLine("| Task | Engine | Duration(s) | Exit | Retries | Tokens(in/out/total) | Status |");
        sb.AppendLine("|---|---|---:|---:|---:|---|---|");

        foreach (var t in report.Tasks)
        {
            var tokenCol = $"{t.InputTokens}/{t.OutputTokens}/{t.TotalTokens}";
            sb.AppendLine($"| {EscapePipe(t.Task)} | {EscapePipe(t.Engine)} | {t.DurationSeconds:F1} | {t.ExitCode} | {t.Retries} | {tokenCol} | {EscapePipe(t.Status)} |");
        }

        return sb.ToString();
    }

    private static string EscapePipe(string value) => (value ?? string.Empty).Replace("|", "\\|");
}

public sealed class RunReportDocument
{
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset EndedAtUtc { get; init; }
    public double DurationSeconds { get; init; }
    public bool Completed { get; init; }
    public List<RunReportTaskEntry> Tasks { get; init; } = new();
}

public sealed class RunReportTaskEntry
{
    public string Task { get; init; } = string.Empty;
    public string Engine { get; init; } = string.Empty;
    public double DurationSeconds { get; init; }
    public int ExitCode { get; init; }
    public int Retries { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int TotalTokens { get; init; }
    public string Status { get; init; } = string.Empty;
}
