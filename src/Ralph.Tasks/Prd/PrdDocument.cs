namespace Ralph.Tasks.Prd;

public sealed class PrdDocument
{
    public IReadOnlyList<string> RawLines { get; init; } = Array.Empty<string>();
    public PrdFrontmatter? Frontmatter { get; init; }
    public IReadOnlyList<PrdTaskEntry> TaskEntries { get; init; } = Array.Empty<PrdTaskEntry>();

    public string? GetSharedContext()
    {
        if (RawLines.Count == 0)
            return null;

        var start = 0;
        if (RawLines[0].Trim() == "---")
        {
            start = 1;
            while (start < RawLines.Count && RawLines[start].Trim() != "---")
                start++;
            if (start < RawLines.Count)
                start++;
        }

        var end = TaskEntries.Count == 0
            ? RawLines.Count
            : TaskEntries.Min(t => t.LineIndex);

        if (end <= start)
            return null;

        var sharedLines = RawLines
            .Skip(start)
            .Take(end - start)
            .ToArray();

        var sharedContext = string.Join(Environment.NewLine, sharedLines).Trim();
        return string.IsNullOrWhiteSpace(sharedContext) ? null : sharedContext;
    }

    public string? GetBodyWithoutFrontmatter()
    {
        if (RawLines.Count == 0)
            return null;

        var start = 0;
        if (RawLines[0].Trim() == "---")
        {
            start = 1;
            while (start < RawLines.Count && RawLines[start].Trim() != "---")
                start++;
            if (start < RawLines.Count)
                start++;
        }

        var body = string.Join(Environment.NewLine, RawLines.Skip(start)).Trim();
        return string.IsNullOrWhiteSpace(body) ? null : body;
    }

    public int? GetNextPendingTaskIndex()
    {
        for (var i = 0; i < TaskEntries.Count; i++)
        {
            if (TaskEntries[i].IsPending)
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
