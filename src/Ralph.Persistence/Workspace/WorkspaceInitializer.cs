using System.Text.Json;
using Ralph.Persistence.Config;

namespace Ralph.Persistence.Workspace;

public sealed class WorkspaceInitializer
{
    public const string GuardrailsFileName = "guardrails.md";
    public const string ProgressFileName = "progress.md";
    public const string ErrorsLogFileName = "errors.log";
    public const string ActivityLogFileName = "activity.log";
    public const string ExecutionLogFileName = "execution.log";
    public const string EventsLogFileName = "events.jsonl";
    public const string TuiDebugLogFileName = "tui-debug.log";
    public const string StateFileName = "state.json";
    public const string HeartbeatFileName = "heartbeat.json";
    public const string IterationFileName = ".iteration";
    public const string ConfigFileName = "config.json";
    public const string ReportsDirName = "reports";
    public const string ContextDirName = "context";
    public const string CheckpointsDirName = "checkpoints";
    public const string PluginsDirName = "plugins";
    public const string RecipesDirName = "recipes";
    public const string AdaptersDirName = "adapters";
    public const string RalphDirName = ".ralph";
    public const string PrdFileName = "PRD.md";

    public string GetRalphDir(string workingDirectory) =>
        Path.Combine(workingDirectory, RalphDirName);

    public string GetGuardrailsPath(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), GuardrailsFileName);

    public string GetProgressPath(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), ProgressFileName);

    public string GetErrorsLogPath(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), ErrorsLogFileName);

    public string GetActivityLogPath(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), ActivityLogFileName);

    public string GetExecutionLogPath(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), ExecutionLogFileName);

    public string GetEventsLogPath(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), EventsLogFileName);

    public string GetTuiDebugLogPath(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), TuiDebugLogFileName);

    public string GetStatePath(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), StateFileName);

    public string GetHeartbeatPath(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), HeartbeatFileName);

    public string GetIterationPath(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), IterationFileName);

    public string GetConfigPath(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), ConfigFileName);

    public string GetReportsDir(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), ReportsDirName);

    public string GetContextDir(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), ContextDirName);

    public string GetRepoMapPath(string workingDirectory) =>
        Path.Combine(GetContextDir(workingDirectory), "repo-map.md");

    public string GetCheckpointsDir(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), CheckpointsDirName);

    public string GetPluginsDir(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), PluginsDirName);

    public string GetRecipesDir(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), RecipesDirName);

    public string GetAdaptersDir(string workingDirectory) =>
        Path.Combine(GetRalphDir(workingDirectory), AdaptersDirName);

    public string GetLatestReportMarkdownPath(string workingDirectory) =>
        Path.Combine(GetReportsDir(workingDirectory), "latest.md");

    public string GetPrdPath(string workingDirectory) =>
        Path.Combine(workingDirectory, PrdFileName);

    public void Initialize(string workingDirectory, bool force = false)
    {
        var ralphDir = GetRalphDir(workingDirectory);
        if (Directory.Exists(ralphDir) && !force)
        {
            EnsureFilesExist(workingDirectory);
            return;
        }
        Directory.CreateDirectory(ralphDir);

        WriteIfMissing(GetGuardrailsPath(workingDirectory), "# Guardrails\n\nAdd constraints and rules here.\n");
        WriteIfMissing(GetProgressPath(workingDirectory), "# Progress\n\n");
        WriteIfMissing(GetErrorsLogPath(workingDirectory), "");
        WriteIfMissing(GetActivityLogPath(workingDirectory), "");
        WriteIfMissing(GetExecutionLogPath(workingDirectory), "");
        WriteIfMissing(GetEventsLogPath(workingDirectory), "");
        WriteIfMissing(GetTuiDebugLogPath(workingDirectory), "");
        WriteIfMissing(GetStatePath(workingDirectory), "{\"iteration\":0,\"retries\":0}\n");
        WriteIfMissing(GetHeartbeatPath(workingDirectory), "{}\n");
        WriteIfMissing(GetIterationPath(workingDirectory), "0");
        WriteIfMissing(GetConfigPath(workingDirectory), GetDefaultConfigContent());
        WriteIfMissing(GetPrdPath(workingDirectory), GetDefaultPrdContent());
        Directory.CreateDirectory(GetReportsDir(workingDirectory));
        Directory.CreateDirectory(GetContextDir(workingDirectory));
        Directory.CreateDirectory(GetCheckpointsDir(workingDirectory));
        Directory.CreateDirectory(GetPluginsDir(workingDirectory));
        Directory.CreateDirectory(GetRecipesDir(workingDirectory));
        Directory.CreateDirectory(GetAdaptersDir(workingDirectory));

        UpdateGitExclude(workingDirectory);
    }

    private void EnsureFilesExist(string workingDirectory)
    {
        WriteIfMissing(GetGuardrailsPath(workingDirectory), "# Guardrails\n\nAdd constraints and rules here.\n");
        WriteIfMissing(GetProgressPath(workingDirectory), "# Progress\n\n");
        WriteIfMissing(GetErrorsLogPath(workingDirectory), "");
        WriteIfMissing(GetActivityLogPath(workingDirectory), "");
        WriteIfMissing(GetExecutionLogPath(workingDirectory), "");
        WriteIfMissing(GetEventsLogPath(workingDirectory), "");
        WriteIfMissing(GetTuiDebugLogPath(workingDirectory), "");
        WriteIfMissing(GetStatePath(workingDirectory), "{\"iteration\":0,\"retries\":0}\n");
        WriteIfMissing(GetHeartbeatPath(workingDirectory), "{}\n");
        WriteIfMissing(GetIterationPath(workingDirectory), "0");
        WriteIfMissing(GetConfigPath(workingDirectory), GetDefaultConfigContent());
        WriteIfMissing(GetPrdPath(workingDirectory), GetDefaultPrdContent());
        Directory.CreateDirectory(GetReportsDir(workingDirectory));
        Directory.CreateDirectory(GetContextDir(workingDirectory));
        Directory.CreateDirectory(GetCheckpointsDir(workingDirectory));
        Directory.CreateDirectory(GetPluginsDir(workingDirectory));
        Directory.CreateDirectory(GetRecipesDir(workingDirectory));
        Directory.CreateDirectory(GetAdaptersDir(workingDirectory));
        UpdateGitExclude(workingDirectory);
    }

    private static string GetDefaultPrdContent() =>
        "---\n" +
        "task: Ralph task list\n" +
        "engine: cursor\n" +
        "---\n" +
        "# PRD\n\n" +
        "- [ ] First task: edit this file and add your tasks\n" +
        "- [ ] Second task: use ralph run to process them\n";

    private static string GetDefaultConfigContent()
    {
        return JsonSerializer.Serialize(RalphConfig.Default) + "\n";
    }

    private static void WriteIfMissing(string path, string content)
    {
        if (File.Exists(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }

    private static void UpdateGitExclude(string workingDirectory)
    {
        var gitDir = ResolveGitDirectory(workingDirectory);
        if (gitDir == null)
            return;

        var excludePath = Path.Combine(gitDir, "info", "exclude");
        var entry = ".ralph/";
        Directory.CreateDirectory(Path.GetDirectoryName(excludePath)!);

        if (!File.Exists(excludePath))
        {
            File.WriteAllText(excludePath, "# Ralph\n.ralph/\n");
            return;
        }

        var content = File.ReadAllText(excludePath);
        if (content.Contains(entry, StringComparison.Ordinal))
            return;

        var prefix = content.EndsWith('\n') ? string.Empty : Environment.NewLine;
        File.AppendAllText(excludePath, $"{prefix}{entry}{Environment.NewLine}");
    }

    private static string? ResolveGitDirectory(string workingDirectory)
    {
        var gitPath = Path.Combine(workingDirectory, ".git");
        if (Directory.Exists(gitPath))
            return gitPath;

        if (!File.Exists(gitPath))
            return null;

        var firstLine = File.ReadLines(gitPath).FirstOrDefault()?.Trim();
        const string prefix = "gitdir:";
        if (string.IsNullOrWhiteSpace(firstLine) || !firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var relativePath = firstLine[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        return Path.GetFullPath(Path.Combine(workingDirectory, relativePath));
    }

    public bool IsInitialized(string workingDirectory)
    {
        var ralphDir = GetRalphDir(workingDirectory);
        if (!Directory.Exists(ralphDir)) return false;
        return File.Exists(GetStatePath(workingDirectory));
    }
}
