namespace Ralph.Tasks.Prd;

public sealed class PrdTaskEntry
{
    public int LineIndex { get; init; }
    public PrdTaskStatus Status { get; init; }
    public bool IsPending => Status == PrdTaskStatus.Pending;
    public bool IsCompleted => Status == PrdTaskStatus.Completed;
    public bool IsSkippedForReview => Status == PrdTaskStatus.SkippedForReview;
    public bool IsResolved => Status != PrdTaskStatus.Pending;
    public string Marker => Status switch
    {
        PrdTaskStatus.Completed => "[x]",
        PrdTaskStatus.SkippedForReview => "[~]",
        _ => "[ ]"
    };
    public string RawLine { get; init; } = string.Empty;
    public string DisplayText { get; init; } = string.Empty;
    public string? Id { get; init; }
    public string Group { get; init; } = "default";
    public IReadOnlyList<string> DependsOn { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AcceptanceCriteria { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FilesAllowed { get; init; } = Array.Empty<string>();
    public IReadOnlyList<PrdGate> Gates { get; init; } = Array.Empty<PrdGate>();
    public int? Complexity { get; init; }
    public string? Priority { get; init; }
    public string? Notes { get; init; }
}
