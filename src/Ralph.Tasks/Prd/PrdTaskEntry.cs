namespace Ralph.Tasks.Prd;

public sealed class PrdTaskEntry
{
    public int LineIndex { get; init; }
    public bool IsCompleted { get; init; }
    public string RawLine { get; init; } = string.Empty;
    public string DisplayText { get; init; } = string.Empty;
}
