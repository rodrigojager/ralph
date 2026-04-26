using System.Text.Json.Serialization;

namespace Ralph.Persistence.Config;

public sealed class RalphConfig
{
    [JsonPropertyName("engines")]
    public Dictionary<string, EngineConfigEntry>? Engines { get; set; }

    [JsonPropertyName("parallel")]
    public ParallelConfigEntry? Parallel { get; set; }

    [JsonPropertyName("fallback_engines")]
    public List<string>? FallbackEngines { get; set; }

    [JsonPropertyName("context_rotation")]
    public ContextRotationConfigEntry? ContextRotation { get; set; }

    [JsonPropertyName("browser")]
    public BrowserConfigEntry? Browser { get; set; }

    [JsonPropertyName("run")]
    public RunConfigEntry? Run { get; set; }

    [JsonPropertyName("sandbox")]
    public SandboxConfigEntry? Sandbox { get; set; }

    [JsonPropertyName("security")]
    public SecurityConfigEntry? Security { get; set; }

    public static RalphConfig Default => new()
    {
        Engines = new Dictionary<string, EngineConfigEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["cursor"] = new EngineConfigEntry { DefaultModel = null, Command = null },
            ["codex"] = new EngineConfigEntry { DefaultModel = null, Command = GetCodexCommandDefault() },
            ["claude"] = new EngineConfigEntry { DefaultModel = null, Command = "claude" },
            ["opencode"] = new EngineConfigEntry { DefaultModel = null, Command = "opencode" },
            ["qwen"] = new EngineConfigEntry { DefaultModel = null, Command = "qwen" },
            ["droid"] = new EngineConfigEntry { DefaultModel = null, Command = "droid" },
            ["copilot"] = new EngineConfigEntry { DefaultModel = null, Command = "copilot" },
            ["gemini"] = new EngineConfigEntry { DefaultModel = null, Command = null }
        },
        Parallel = new ParallelConfigEntry { MaxParallel = 2, IntegrationStrategy = "no-merge" },
        FallbackEngines = new List<string>(),
        ContextRotation = new ContextRotationConfigEntry
        {
            MaxTotalTokensPerRun = null,
            OnSignal = "warn"
        },
        Browser = new BrowserConfigEntry { Enabled = false, Command = null },
        Sandbox = new SandboxConfigEntry { Enabled = false, Provider = "process", Image = null },
        Security = new SecurityConfigEntry { Mode = "safe" },
        Run = new RunConfigEntry
        {
            NoChangePolicy = "fallback",
            NoChangeMaxAttempts = 3,
            NoChangeStopOnMaxAttempts = true,
            IncludeProgressContext = false,
            IncludeRepoMapContext = false,
            AutoCheckpoints = false
        }
    };

    private static string GetCodexCommandDefault() =>
        OperatingSystem.IsWindows() ? "codex.cmd" : "codex";
}

public sealed class ParallelConfigEntry
{
    [JsonPropertyName("max_parallel")]
    public int? MaxParallel { get; set; }

    [JsonPropertyName("integration_strategy")]
    public string? IntegrationStrategy { get; set; }
}

public sealed class ContextRotationConfigEntry
{
    [JsonPropertyName("max_total_tokens_per_run")]
    public int? MaxTotalTokensPerRun { get; set; }

    [JsonPropertyName("on_signal")]
    public string? OnSignal { get; set; }
}

public sealed class BrowserConfigEntry
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }
}

public sealed class RunConfigEntry
{
    [JsonPropertyName("no_change_policy")]
    public string? NoChangePolicy { get; set; }

    [JsonPropertyName("no_change_max_attempts")]
    public int? NoChangeMaxAttempts { get; set; }

    [JsonPropertyName("no_change_stop_on_max_attempts")]
    public bool? NoChangeStopOnMaxAttempts { get; set; }

    [JsonPropertyName("include_progress_context")]
    public bool? IncludeProgressContext { get; set; }

    [JsonPropertyName("include_repo_map_context")]
    public bool? IncludeRepoMapContext { get; set; }

    [JsonPropertyName("auto_checkpoints")]
    public bool? AutoCheckpoints { get; set; }
}

public sealed class EngineConfigEntry
{
    [JsonPropertyName("default_model")]
    public string? DefaultModel { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("adapter")]
    public string? Adapter { get; set; }
}

public sealed class SandboxConfigEntry
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("network")]
    public string? Network { get; set; }
}

public sealed class SecurityConfigEntry
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}
