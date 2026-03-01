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
        Run = new RunConfigEntry
        {
            NoChangePolicy = "fallback",
            NoChangeMaxAttempts = 3,
            IncludeProgressContext = false
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

    [JsonPropertyName("include_progress_context")]
    public bool? IncludeProgressContext { get; set; }
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
}
