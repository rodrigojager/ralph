using System.Diagnostics;
using System.Text;

namespace Ralph.Persistence.Config;

public sealed class EngineCommandResolver
{
    public ResolvedEngineCommand ResolveForExecution(string engineName, RalphConfig config)
    {
        var candidates = BuildCandidates(engineName, config);
        foreach (var candidate in candidates)
        {
            if (IsCallable(engineName, candidate))
            {
                return new ResolvedEngineCommand(
                    candidate.Executable,
                    candidate.PrefixArgs,
                    candidate.Source,
                    true);
            }
        }

        var fallback = SelectFallbackCandidate(engineName, candidates);
        return new ResolvedEngineCommand(
            fallback.Executable,
            fallback.PrefixArgs,
            fallback.Source,
            false);
    }

    public ProbeResult Probe(string engineName, RalphConfig config)
    {
        var candidates = BuildCandidates(engineName, config);
        foreach (var candidate in candidates)
        {
            var (callable, authRequired, authHint) = TryProbeWithOutput(engineName, candidate);
            if (callable)
            {
                var resolved = new ResolvedEngineCommand(
                    candidate.Executable,
                    candidate.PrefixArgs,
                    candidate.Source,
                    !authRequired);
                return new ProbeResult(true, resolved, candidates, authRequired, authHint);
            }
        }

        return new ProbeResult(false, null, candidates, false, null);
    }

    private List<EngineCommandCandidate> BuildCandidates(string engineName, RalphConfig config)
    {
        var list = new List<EngineCommandCandidate>();
        var configCommand = GetConfigCommand(engineName, config);
        if (!string.IsNullOrWhiteSpace(configCommand))
        {
            var parsed = ParseCommand(configCommand!);
            if (parsed.Executable is { Length: > 0 })
                list.Add(new EngineCommandCandidate(parsed.Executable, parsed.PrefixArgs, "config"));
        }

        foreach (var candidate in GetDefaultCandidates(engineName))
        {
            if (list.Any(x => x.SameAs(candidate)))
                continue;
            list.Add(candidate);
        }

        if (list.Count == 0)
            list.Add(new EngineCommandCandidate(engineName, Array.Empty<string>(), "default"));
        return list;
    }

    private static string? GetConfigCommand(string engineName, RalphConfig config)
    {
        if (config.Engines == null)
            return null;
        return config.Engines.TryGetValue(engineName, out var entry) ? entry.Command : null;
    }

    private static IReadOnlyList<EngineCommandCandidate> GetDefaultCandidates(string engineName)
    {
        var engine = engineName.ToLowerInvariant();
        return engine switch
        {
            "cursor" => new[]
            {
                new EngineCommandCandidate("cursor-agent", Array.Empty<string>(), "default"),
                new EngineCommandCandidate("agent", Array.Empty<string>(), "default"),
                new EngineCommandCandidate("cursor", new[] { "agent" }, "default")
            },
            "codex" => OperatingSystem.IsWindows()
                ? new[]
                {
                    new EngineCommandCandidate("codex.cmd", Array.Empty<string>(), "default"),
                    new EngineCommandCandidate("codex", Array.Empty<string>(), "default")
                }
                : new[] { new EngineCommandCandidate("codex", Array.Empty<string>(), "default") },
            "gemini" => OperatingSystem.IsWindows()
                ? BuildGeminiWindowsCandidates()
                : new[] { new EngineCommandCandidate("gemini", Array.Empty<string>(), "default") },
            _ => new[] { new EngineCommandCandidate(engineName, Array.Empty<string>(), "default") }
        };
    }

    private static IReadOnlyList<EngineCommandCandidate> BuildGeminiWindowsCandidates()
    {
        var candidates = new List<EngineCommandCandidate>();
        var geminiCmd = FindCommandInPath("gemini.cmd");
        if (geminiCmd != null)
        {
            var scriptPath = Path.Combine(
                Path.GetDirectoryName(geminiCmd)!,
                "node_modules",
                "@google",
                "gemini-cli",
                "dist",
                "index.js");
            if (File.Exists(scriptPath))
            {
                candidates.Add(new EngineCommandCandidate(
                    "node",
                    new[] { "--no-warnings=DEP0040", scriptPath },
                    "default"));
            }
        }

        candidates.Add(new EngineCommandCandidate("gemini", Array.Empty<string>(), "default"));
        candidates.Add(new EngineCommandCandidate("gemini.cmd", Array.Empty<string>(), "default"));
        return candidates;
    }

    private static EngineCommandCandidate SelectFallbackCandidate(string engineName, IReadOnlyList<EngineCommandCandidate> candidates)
    {
        if (candidates.Count == 0)
            return new EngineCommandCandidate(engineName, Array.Empty<string>(), "default");

        if (engineName.Equals("cursor", StringComparison.OrdinalIgnoreCase))
        {
            var cursorAgent = candidates.FirstOrDefault(c =>
                c.Executable.Equals("cursor", StringComparison.OrdinalIgnoreCase)
                && c.PrefixArgs.Count == 1
                && c.PrefixArgs[0].Equals("agent", StringComparison.OrdinalIgnoreCase));
            if (cursorAgent is not null)
                return cursorAgent;

            var agent = candidates.FirstOrDefault(c =>
                c.Executable.Equals("agent", StringComparison.OrdinalIgnoreCase)
                && c.PrefixArgs.Count == 0);
            if (agent is not null)
                return agent;
        }

        if (engineName.Equals("gemini", StringComparison.OrdinalIgnoreCase))
        {
            var node = candidates.FirstOrDefault(c =>
                c.Executable.Equals("node", StringComparison.OrdinalIgnoreCase));
            if (node is not null)
                return node;

            var gemini = candidates.FirstOrDefault(c =>
                c.Executable.Equals("gemini", StringComparison.OrdinalIgnoreCase));
            if (gemini is not null)
                return gemini;
        }

        return candidates[0];
    }

    private static (bool Callable, bool AuthRequired, string? AuthHint) TryProbeWithOutput(string engineName, EngineCommandCandidate candidate)
    {
        foreach (var probeArg in GetProbeArgs(engineName))
        {
            try
            {
                var launch = BuildLaunch(candidate.Executable, candidate.PrefixArgs, new[] { probeArg });
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = launch.FileName,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                foreach (var token in launch.Args)
                    process.StartInfo.ArgumentList.Add(token);
                if (!process.Start())
                    continue;
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);
                var combined = stdout + "\n" + stderr;
                var auth = DetectAuthRequired(combined);
                return (true, auth.Required, auth.Hint);
            }
            catch
            {
                // try next
            }
        }

        return (false, false, null);
    }

    private static (bool Required, string? Hint) DetectAuthRequired(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return (false, null);
        var patterns = new[] { "GEMINI_API_KEY", "GOOGLE_GENAI_USE_VERTEXAI", "GOOGLE_GENAI_USE_GCA", "set an Auth method", "Auth method", "API key", "API_KEY", "unauthorized", "authentication", "not logged in" };
        var hint = "Set GEMINI_API_KEY or configure ~/.gemini/settings.json";
        foreach (var pattern in patterns)
        {
            if (output.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                var line = output.Trim().Split('\n').FirstOrDefault(l => l.Contains(pattern, StringComparison.OrdinalIgnoreCase))?.Trim();
                return (true, pattern == "GEMINI_API_KEY" || pattern == "set an Auth method" ? hint : (line ?? hint));
            }
        }
        return (false, null);
    }

    private static bool IsCallable(string engineName, EngineCommandCandidate candidate)
    {
        foreach (var probeArg in GetProbeArgs(engineName))
        {
            try
            {
                var launch = BuildLaunch(candidate.Executable, candidate.PrefixArgs, new[] { probeArg });
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = launch.FileName,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                foreach (var token in launch.Args)
                    process.StartInfo.ArgumentList.Add(token);
                if (!process.Start())
                    continue;
                process.WaitForExit(2500);
                return true;
            }
            catch
            {
                // try next
            }
        }

        return false;
    }

    private static (string FileName, IReadOnlyList<string> Args) BuildLaunch(
        string command,
        IReadOnlyList<string> prefixArgs,
        IReadOnlyList<string> args)
    {
        if (!ShouldWrapWithCmd(command))
            return (command, prefixArgs.Concat(args).ToArray());

        var wrapped = new List<string> { "/c", command };
        wrapped.AddRange(prefixArgs);
        wrapped.AddRange(args);
        return ("cmd.exe", wrapped);
    }

    private static IReadOnlyList<string> GetProbeArgs(string engineName)
    {
        return engineName.ToLowerInvariant() switch
        {
            "cursor" => new[] { "--version", "version", "--help" },
            "codex" => new[] { "--version", "version", "--help" },
            "claude" => new[] { "--version", "version", "--help" },
            "opencode" => new[] { "--version", "version", "--help" },
            "qwen" => new[] { "--version", "version", "--help" },
            "droid" => new[] { "--version", "version", "--help" },
            "copilot" => new[] { "--version", "version", "--help" },
            "gemini" => new[] { "--version", "version", "--help" },
            _ => new[] { "--version", "--help", "version" }
        };
    }

    private static (string Executable, IReadOnlyList<string> PrefixArgs) ParseCommand(string command)
    {
        var tokens = SplitCommandLine(command);
        if (tokens.Count == 0) return (string.Empty, Array.Empty<string>());
        return (tokens[0], tokens.Skip(1).ToArray());
    }

    private static List<string> SplitCommandLine(string command)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < command.Length; i++)
        {
            var ch = command[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            result.Add(current.ToString());
        return result;
    }

    private static bool ShouldWrapWithCmd(string command)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrWhiteSpace(Path.GetExtension(command)))
            return false;

        return CommandExistsInPath(command + ".cmd") || CommandExistsInPath(command + ".bat");
    }

    private static bool CommandExistsInPath(string name)
    {
        return FindCommandInPath(name) != null;
    }

    private static string? FindCommandInPath(string name)
    {
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
}

public sealed record EngineCommandCandidate(
    string Executable,
    IReadOnlyList<string> PrefixArgs,
    string Source)
{
    public bool SameAs(EngineCommandCandidate other)
    {
        if (!Executable.Equals(other.Executable, StringComparison.OrdinalIgnoreCase))
            return false;
        if (PrefixArgs.Count != other.PrefixArgs.Count)
            return false;
        for (var i = 0; i < PrefixArgs.Count; i++)
        {
            if (!PrefixArgs[i].Equals(other.PrefixArgs[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    public string Display()
    {
        return PrefixArgs.Count == 0
            ? Executable
            : $"{Executable} {string.Join(" ", PrefixArgs)}";
    }
}

public sealed record ResolvedEngineCommand(
    string Executable,
    IReadOnlyList<string> PrefixArgs,
    string Source,
    bool Available)
{
    public string Display()
    {
        return PrefixArgs.Count == 0
            ? Executable
            : $"{Executable} {string.Join(" ", PrefixArgs)}";
    }
}

public sealed record ProbeResult(
    bool Available,
    ResolvedEngineCommand? Resolved,
    IReadOnlyList<EngineCommandCandidate> TriedCandidates,
    bool AuthRequired = false,
    string? AuthHint = null);
