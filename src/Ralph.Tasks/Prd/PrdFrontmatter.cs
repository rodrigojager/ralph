namespace Ralph.Tasks.Prd;

public sealed class PrdFrontmatter
{
    public string? Task { get; init; }
    public string? TestCommand { get; init; }
    public string? LintCommand { get; init; }
    public string? BrowserCommand { get; init; }
    public string? Engine { get; init; }
    public string? Model { get; init; }
    public string? SecurityMode { get; init; }
    public string? Sandbox { get; init; }
    public bool? IncludeRepoMap { get; init; }
    public IReadOnlyList<PrdGate> Gates { get; init; } = Array.Empty<PrdGate>();
}
