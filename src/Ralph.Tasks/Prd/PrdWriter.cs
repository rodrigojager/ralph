using System.Text.RegularExpressions;

namespace Ralph.Tasks.Prd;

public static class PrdWriter
{
    private static readonly Regex TaskMarkerRegex = new(@"\[(?: |x|X|~)\]", RegexOptions.Compiled);

    public static void MarkTaskCompleted(string prdPath, PrdDocument document, int taskIndex)
    {
        var ext = Path.GetExtension(prdPath);
        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            MarkTaskCompletedInJson(prdPath, taskIndex);
            return;
        }

        if (taskIndex < 0 || taskIndex >= document.TaskEntries.Count)
            throw new ArgumentOutOfRangeException(nameof(taskIndex));
        var entry = document.TaskEntries[taskIndex];
        var lineIndex = entry.LineIndex;
        var lines = document.RawLines.ToList();
        lines[lineIndex] = ReplaceTaskMarker(lines[lineIndex], "x");
        WriteLines(prdPath, lines);
    }

    public static void MarkTaskCompletedByLineIndex(string prdPath, IReadOnlyList<string> lines, int lineIndex)
    {
        if (lineIndex < 0 || lineIndex >= lines.Count)
            throw new ArgumentOutOfRangeException(nameof(lineIndex));
        var list = lines.ToList();
        list[lineIndex] = ReplaceTaskMarker(list[lineIndex], "x");
        WriteLines(prdPath, list);
    }

    public static void MarkTaskSkippedForReview(string prdPath, PrdDocument document, int taskIndex)
    {
        var ext = Path.GetExtension(prdPath);
        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            MarkTaskSkippedForReviewInJson(prdPath, taskIndex);
            return;
        }

        if (taskIndex < 0 || taskIndex >= document.TaskEntries.Count)
            throw new ArgumentOutOfRangeException(nameof(taskIndex));
        var entry = document.TaskEntries[taskIndex];
        var lineIndex = entry.LineIndex;
        var lines = document.RawLines.ToList();
        lines[lineIndex] = ReplaceTaskMarker(lines[lineIndex], "~");
        WriteLines(prdPath, lines);
    }

    private static void WriteLines(string prdPath, List<string> lines)
    {
        var rawContent = File.ReadAllText(prdPath);
        var newline = rawContent.Contains("\r\n") ? "\r\n" : "\n";
        var output = string.Join(newline, lines);
        if (rawContent.EndsWith("\r\n", StringComparison.Ordinal) || rawContent.EndsWith("\n", StringComparison.Ordinal))
            output += newline;
        File.WriteAllText(prdPath, output, System.Text.Encoding.UTF8);
    }

    private static void MarkTaskCompletedInJson(string prdPath, int taskIndex)
    {
        var json = File.ReadAllText(prdPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new InvalidOperationException("JSON task source must be an array.");

        var list = new List<Dictionary<string, object?>>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != System.Text.Json.JsonValueKind.Object)
                continue;
            var obj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in item.EnumerateObject())
                obj[p.Name] = p.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => p.Value.GetString(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Number => p.Value.TryGetInt64(out var n) ? n : p.Value.GetDouble(),
                    _ => p.Value.ToString()
                };
            list.Add(obj);
        }

        if (taskIndex < 0 || taskIndex >= list.Count)
            throw new ArgumentOutOfRangeException(nameof(taskIndex));

        list[taskIndex]["completed"] = true;
        list[taskIndex]["status"] = "completed";
        var output = System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(prdPath, output);
    }

    private static void MarkTaskSkippedForReviewInJson(string prdPath, int taskIndex)
    {
        var json = File.ReadAllText(prdPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new InvalidOperationException("JSON task source must be an array.");

        var list = new List<Dictionary<string, object?>>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != System.Text.Json.JsonValueKind.Object)
                continue;
            var obj = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in item.EnumerateObject())
                obj[p.Name] = p.Value.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => p.Value.GetString(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Number => p.Value.TryGetInt64(out var n) ? n : p.Value.GetDouble(),
                    _ => p.Value.ToString()
                };
            list.Add(obj);
        }

        if (taskIndex < 0 || taskIndex >= list.Count)
            throw new ArgumentOutOfRangeException(nameof(taskIndex));

        list[taskIndex]["completed"] = false;
        list[taskIndex]["status"] = "skipped_for_review";
        var output = System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(prdPath, output);
    }

    private static string ReplaceTaskMarker(string line, string marker)
        => TaskMarkerRegex.Replace(line, $"[{marker}]", 1);
}
