using System.Text.Json.Serialization;
using Ralph.Engines.Abstractions;

namespace Ralph.Core.Adapters;

public sealed class EngineAdapterManifest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("command")]
    public string? Command { get; init; }

    [JsonPropertyName("prompt_transport")]
    public string? PromptTransport { get; init; }

    [JsonPropertyName("output_mode")]
    public string? OutputMode { get; init; }

    [JsonPropertyName("prompt_flag")]
    public string? PromptFlag { get; init; }

    [JsonPropertyName("model_flag")]
    public string? ModelFlag { get; init; }

    [JsonPropertyName("default_args")]
    public IReadOnlyList<string> DefaultArgs { get; init; } = Array.Empty<string>();

    [JsonPropertyName("safe_args")]
    public IReadOnlyList<string> SafeArgs { get; init; } = Array.Empty<string>();

    [JsonPropertyName("auto_args")]
    public IReadOnlyList<string> AutoArgs { get; init; } = Array.Empty<string>();

    [JsonPropertyName("dangerous_args")]
    public IReadOnlyList<string> DangerousArgs { get; init; } = Array.Empty<string>();

    public EngineAdapterOptions ToOptions() => new()
    {
        Name = Name,
        PromptTransport = PromptTransport,
        OutputMode = OutputMode,
        PromptFlag = PromptFlag,
        ModelFlag = ModelFlag,
        DefaultArgs = DefaultArgs,
        SafeArgs = SafeArgs,
        AutoArgs = AutoArgs,
        DangerousArgs = DangerousArgs
    };
}
