using Ralph.Engines.Tokens;

namespace Ralph.Engines.Abstractions;

public sealed class EngineResult
{
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public string? ExecutedCommand { get; init; }
    public IReadOnlyList<string> ExecutedArgs { get; init; } = Array.Empty<string>();
    public TimeSpan Duration { get; init; }
    public CompletionSignal CompletionSignal { get; init; }
    public IReadOnlyList<DetectedErrorKind> DetectedErrors { get; init; } = Array.Empty<DetectedErrorKind>();
    public TokenUsage? TokenUsage { get; init; }
    public bool HadStructuredError { get; init; }
    public string? RawTranscriptPath { get; init; }
}
