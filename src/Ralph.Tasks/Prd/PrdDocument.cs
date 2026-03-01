namespace Ralph.Tasks.Prd;

public sealed class PrdDocument
{
    public IReadOnlyList<string> RawLines { get; init; } = Array.Empty<string>();
    public PrdFrontmatter? Frontmatter { get; init; }
    public IReadOnlyList<PrdTaskEntry> TaskEntries { get; init; } = Array.Empty<PrdTaskEntry>();

    public int? GetNextPendingTaskIndex()
    {
        for (var i = 0; i < TaskEntries.Count; i++)
        {
            if (!TaskEntries[i].IsCompleted)
                return i;
        }
        return null;
    }

    public PrdTaskEntry? GetNextPendingTask()
    {
        var idx = GetNextPendingTaskIndex();
        return idx.HasValue && idx.Value < TaskEntries.Count ? TaskEntries[idx.Value] : null;
    }
}
