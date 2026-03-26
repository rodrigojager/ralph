namespace Ralph.Engines.Abstractions;

public sealed class EngineRequest
{
    public required string WorkingDirectory { get; init; }
    public required string TaskText { get; init; }
    public string? PrdPath { get; init; }
    public string? GuardrailsPath { get; init; }
    public string? ProgressPath { get; init; }
    public string? CommandOverride { get; init; }
    public IReadOnlyList<string>? CommandPrefixArgs { get; init; }
    public string? CommandResolutionSource { get; init; }
    public string? ModelOverride { get; init; }
    public IReadOnlyList<string>? ExtraArgsPassthrough { get; init; }
    public bool Fast { get; init; }
}
