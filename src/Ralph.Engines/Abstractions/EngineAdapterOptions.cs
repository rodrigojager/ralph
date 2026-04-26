namespace Ralph.Engines.Abstractions;

public sealed class EngineAdapterOptions
{
    public string? Name { get; init; }
    public string? PromptTransport { get; init; }
    public string? OutputMode { get; init; }
    public string? PromptFlag { get; init; }
    public IReadOnlyList<string> DefaultArgs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SafeArgs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AutoArgs { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DangerousArgs { get; init; } = Array.Empty<string>();
    public string? ModelFlag { get; init; }
}

public sealed class EngineSandboxOptions
{
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "process";
    public string? Image { get; init; }
    public string? Network { get; init; }
}
