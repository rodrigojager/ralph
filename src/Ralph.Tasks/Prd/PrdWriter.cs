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
            MarkTaskStatusInJson(prdPath, taskIndex, completed: true, "completed");
            return;
        }
        if (ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase) || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            if (taskIndex >= 0 && taskIndex < document.TaskEntries.Count && !TaskMarkerRegex.IsMatch(document.TaskEntries[taskIndex].RawLine))
            {
                MarkTaskStatusInStructuredYaml(prdPath, taskIndex, "completed");
                return;
            }
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
            MarkTaskStatusInJson(prdPath, taskIndex, completed: false, "skipped_for_review");
            return;
        }
        if (ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase) || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase))
        {
            if (taskIndex >= 0 && taskIndex < document.TaskEntries.Count && !TaskMarkerRegex.IsMatch(document.TaskEntries[taskIndex].RawLine))
            {
                MarkTaskStatusInStructuredYaml(prdPath, taskIndex, "skipped_for_review");
                return;
            }
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

    private static void MarkTaskStatusInJson(string prdPath, int taskIndex, bool completed, string status)
    {
        var json = File.ReadAllText(prdPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var rootIsArray = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array;
        var source = rootIsArray
            ? doc.RootElement
            : doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object && doc.RootElement.TryGetProperty("tasks", out var tasksElement)
                ? tasksElement
                : default;
        if (source.ValueKind != System.Text.Json.JsonValueKind.Array)
            throw new InvalidOperationException("JSON task source must be an array or an object with a tasks array.");

        var list = new List<Dictionary<string, object?>>();
        foreach (var item in source.EnumerateArray())
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

        list[taskIndex]["completed"] = completed;
        list[taskIndex]["status"] = status;

        object outputObject = list;
        if (!rootIsArray)
        {
            var root = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.NameEquals("tasks"))
                    root[p.Name] = list;
                else
                    root[p.Name] = ConvertJsonValue(p.Value);
            }
            outputObject = root;
        }

        var output = System.Text.Json.JsonSerializer.Serialize(outputObject, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(prdPath, output);
    }

    private static object? ConvertJsonValue(System.Text.Json.JsonElement value)
    {
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => value.GetString(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Number => value.TryGetInt64(out var n) ? n : value.GetDouble(),
            System.Text.Json.JsonValueKind.Array => value.EnumerateArray().Select(ConvertJsonValue).ToArray(),
            System.Text.Json.JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonValue(p.Value), StringComparer.OrdinalIgnoreCase),
            _ => null
        };
    }

    private static void MarkTaskStatusInStructuredYaml(string prdPath, int taskIndex, string status)
    {
        var lines = File.ReadAllLines(prdPath).ToList();
        var seenTasks = false;
        var currentTask = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.Equals("tasks:", StringComparison.OrdinalIgnoreCase))
            {
                seenTasks = true;
                continue;
            }
            if (!seenTasks)
                continue;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                currentTask++;
                if (currentTask > taskIndex)
                    break;
                continue;
            }
            if (currentTask == taskIndex && trimmed.StartsWith("status:", StringComparison.OrdinalIgnoreCase))
            {
                var indent = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)];
                lines[i] = $"{indent}status: {status}";
                WriteLines(prdPath, lines);
                return;
            }
        }

        currentTask = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
                continue;
            currentTask++;
            if (currentTask != taskIndex)
                continue;
            var indent = lines[i][..(lines[i].Length - lines[i].TrimStart().Length)] + "  ";
            lines.Insert(i + 1, $"{indent}status: {status}");
            WriteLines(prdPath, lines);
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(taskIndex));
    }

    private static string ReplaceTaskMarker(string line, string marker)
        => TaskMarkerRegex.Replace(line, $"[{marker}]", 1);
}
