using System.Diagnostics;
using Ralph.Core.Localization;
using Ralph.Persistence.Config;
using Ralph.Persistence.Workspace;

namespace Ralph.Cli.Commands;

public sealed class DoctorCommand
{
    private readonly WorkspaceInitializer _workspaceInit;
    private readonly ConfigStore _configStore;
    private readonly EngineCommandResolver _commandResolver;

    public DoctorCommand(
        WorkspaceInitializer workspaceInit,
        ConfigStore configStore,
        EngineCommandResolver? commandResolver = null)
    {
        _workspaceInit = workspaceInit;
        _configStore = configStore;
        _commandResolver = commandResolver ?? new EngineCommandResolver();
    }

    public int Execute(string workingDirectory, IStringCatalog s, bool autoApprove = false, bool nonInteractive = false, bool showProcesses = false)
    {
        Console.WriteLine(s.Get("doctor.header"));
        Console.WriteLine(s.Get("doctor.separator"));
        var ok = true;

        if (_workspaceInit.IsInitialized(workingDirectory))
            Console.WriteLine(s.Get("doctor.workspace_ok"));
        else
        {
            Console.WriteLine(s.Get("doctor.workspace_missing"));
            ok = false;
        }

        var prdPath = ResolvePrdPath(workingDirectory);
        if (prdPath != null)
            Console.WriteLine(s.Format("doctor.prd_ok", Path.GetFileName(prdPath)));
        else
        {
            Console.WriteLine(s.Get("doctor.prd_missing"));
            ok = false;
        }

        var configPath = _workspaceInit.GetConfigPath(workingDirectory);
        var config = File.Exists(configPath) ? _configStore.Load(configPath) : RalphConfig.Default;
        var known = GetKnownEngineNames();
        foreach (var name in known)
            CheckEngineCommand(name, config, s, nonInteractive);

        if (config.Engines != null)
        {
            foreach (var (alias, entry) in config.Engines)
            {
                if (known.Contains(alias, StringComparer.OrdinalIgnoreCase))
                    continue;
                CheckEngineCommand(alias, config, s, nonInteractive);
            }
        }

        try
        {
            var dir = new DirectoryInfo(Path.Combine(workingDirectory, ".git"));
            if (dir.Exists)
            {
                Console.WriteLine(s.Get("doctor.git_ok"));
            }
            else
            {
                Console.WriteLine(s.Get("doctor.git_missing"));
                var shouldInit = autoApprove;
                if (!autoApprove && !nonInteractive && !Console.IsInputRedirected)
                {
                    Console.Write(s.Get("doctor.git_init_prompt"));
                    var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
                    shouldInit = answer is "y" or "yes" or "s" or "sim";
                }
                else if (!autoApprove && nonInteractive)
                {
                    Console.WriteLine(s.Get("doctor.git_init_skipped_non_interactive"));
                }

                if (shouldInit)
                {
                    if (TryRunGit(workingDirectory, "init", 10000))
                    {
                        Console.WriteLine(s.Get("doctor.git_init_ok"));
                        TryCreateInitialCommit(workingDirectory, s);
                        Console.WriteLine(s.Get("doctor.git_ok"));
                    }
                    else
                    {
                        Console.WriteLine(s.Get("doctor.git_init_fail"));
                    }
                }
                else if (!nonInteractive || !autoApprove)
                {
                    Console.WriteLine(s.Get("doctor.git_init_aborted"));
                }
            }
        }
        catch { Console.WriteLine(s.Get("doctor.git_unknown")); }

        var gumPath = FindGumInPath();
        if (gumPath != null)
            Console.WriteLine(s.Format("doctor.gum_ok", gumPath));
        else
            Console.WriteLine(s.Get("doctor.gum_missing"));

        if (showProcesses)
            PrintProcessSnapshot(s);

        return ok ? 0 : 1;
    }

    private static void PrintProcessSnapshot(IStringCatalog s)
    {
        Console.WriteLine(s.Get("doctor.process_header"));
        var interesting = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ralph",
            "dotnet",
            "node",
            "cursor",
            "codex",
            "claude",
            "gemini",
            "agent"
        };

        List<Process> processes;
        try
        {
            processes = Process.GetProcesses()
                .Where(p => interesting.Contains(p.ProcessName))
                .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Id)
                .ToList();
        }
        catch
        {
            Console.WriteLine(s.Get("doctor.process_unavailable"));
            return;
        }

        if (processes.Count == 0)
        {
            Console.WriteLine(s.Get("doctor.process_none"));
            return;
        }

        Console.WriteLine(s.Format("doctor.process_count", processes.Count));
        foreach (var process in processes)
        {
            string startedAt;
            try { startedAt = process.StartTime.ToString("yyyy-MM-dd HH:mm:ss"); }
            catch { startedAt = "?"; }
            Console.WriteLine(s.Format("doctor.process_item", process.Id, process.ProcessName, startedAt));
        }
    }

    private static string? ResolvePrdPath(string workingDirectory)
    {
        var candidates = new[]
        {
            Path.Combine(workingDirectory, "PRD.md"),
            Path.Combine(workingDirectory, "PRD.yaml"),
            Path.Combine(workingDirectory, "PRD.yml")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static IReadOnlyList<string> GetKnownEngineNames()
    {
        return ["cursor", "codex", "claude", "opencode", "qwen", "droid", "copilot", "gemini"];
    }

    private void CheckEngineCommand(string displayName, RalphConfig config, IStringCatalog s, bool nonInteractive)
    {
        var probe = _commandResolver.Probe(displayName, config);
        if (probe.Resolved != null && probe.AuthRequired)
        {
            Console.WriteLine(s.Format("doctor.engine_auth_required", displayName));
            if (!string.IsNullOrWhiteSpace(probe.AuthHint))
                Console.WriteLine(s.Format("doctor.engine_auth_hint", probe.AuthHint));
            if (!Console.IsInputRedirected && !nonInteractive)
            {
                Console.Write(s.Get("doctor.engine_auth_prompt"));
                var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (answer is "y" or "yes" or "s" or "sim")
                    TryOpenAuthDocs(displayName);
            }
            return;
        }
        if (probe.Available && probe.Resolved != null)
        {
            var details = $"{displayName} ({probe.Resolved.Display()})";
            Console.WriteLine(s.Format("doctor.engine_ok", details));
            return;
        }

        var attempted = string.Join(", ", probe.TriedCandidates.Select(c => c.Display()));
        Console.WriteLine(s.Format("doctor.engine_missing", displayName, attempted));
    }

    private static void TryOpenAuthDocs(string engineName)
    {
        var url = engineName.ToLowerInvariant() switch
        {
            "gemini" => "https://google-gemini.github.io/gemini-cli/docs/get-started/authentication",
            "claude" => "https://docs.anthropic.com/claude/docs/get-started-with-claude",
            "codex" => "https://codex.anthropic.com/docs",
            _ => null
        };
        if (url != null)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch { /* best effort */ }
        }
    }

    private static string? FindGumInPath()
    {
        var name = OperatingSystem.IsWindows() ? "gum.exe" : "gum";
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var full = Path.Combine(dir.Trim(), name);
            if (File.Exists(full))
                return full;
        }
        return null;
    }

    private static bool TryRunGit(string workingDirectory, string args, int timeoutMs)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc == null)
                return false;
            proc.WaitForExit(timeoutMs);
            return proc.HasExited && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TryCreateInitialCommit(string workingDirectory, IStringCatalog s)
    {
        if (!TryRunGit(workingDirectory, "add -A", 10000))
        {
            Console.WriteLine(s.Get("doctor.git_commit_skipped"));
            return;
        }

        if (TryRunGit(workingDirectory, "commit -m \"chore: initialize repository\"", 10000))
        {
            Console.WriteLine(s.Get("doctor.git_commit_ok"));
            return;
        }

        Console.WriteLine(s.Get("doctor.git_commit_skipped"));
    }
}
