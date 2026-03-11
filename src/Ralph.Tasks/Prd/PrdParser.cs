using System.Text.RegularExpressions;
using System.Text.Json;

namespace Ralph.Tasks.Prd;

public static class PrdParser
{
    private static readonly Regex TaskLineRegex = new(
        @"^(\s*)-\s+\[([ xX~])\]\s*(.*)$",
        RegexOptions.Compiled);

    public static PrdDocument Parse(string fullPath)
    {
        var ext = Path.GetExtension(fullPath);
        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return ParseJson(fullPath);

        var lines = File.ReadAllLines(fullPath);
        if (ext.Equals(".yaml", StringComparison.OrdinalIgnoreCase) || ext.Equals(".yml", StringComparison.OrdinalIgnoreCase))
            return ParseYamlLines(lines);
        return ParseLines(lines);
    }

    public static PrdDocument ParseContent(string content)
    {
        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        return ParseLines(lines);
    }

    public static PrdDocument ParseLines(string[] lines)
    {
        PrdFrontmatter? frontmatter = null;
        var taskEntries = new List<PrdTaskEntry>();
        var i = 0;

        if (lines.Length > 0 && lines[0].Trim() == "---")
        {
            var fmLines = new List<string>();
            i = 1;
            while (i < lines.Length && lines[i].Trim() != "---")
            {
                fmLines.Add(lines[i]);
                i++;
            }
            if (i < lines.Length) i++;
            frontmatter = ParseFrontmatter(fmLines);
        }

        while (i < lines.Length)
        {
            var line = lines[i];
            if (IsFenceLine(line))
            {
                i++;
                while (i < lines.Length && !IsFenceLine(lines[i]))
                    i++;
                if (i < lines.Length)
                    i++;
                continue;
            }
            var match = TaskLineRegex.Match(line);
            if (match.Success)
            {
                var status = ParseTaskStatusMarker(match.Groups[2].Value);
                var displayText = match.Groups[3].Value.Trim();
                taskEntries.Add(new PrdTaskEntry
                {
                    LineIndex = i,
                    Status = status,
                    RawLine = line,
                    DisplayText = displayText
                });
            }
            i++;
        }

        return new PrdDocument
        {
            RawLines = lines,
            Frontmatter = frontmatter,
            TaskEntries = taskEntries
        };
    }

    private static PrdFrontmatter ParseFrontmatter(List<string> lines)
    {
        var task = (string?)null;
        var testCommand = (string?)null;
        var lintCommand = (string?)null;
        var browserCommand = (string?)null;
        var engine = (string?)null;
        var model = (string?)null;
        foreach (var line in lines)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim().Trim('"', '\'');
            switch (key)
            {
                case "task": task = value; break;
                case "test_command": testCommand = value; break;
                case "lint_command": lintCommand = value; break;
                case "browser_command": browserCommand = value; break;
                case "engine": engine = value; break;
                case "model": model = value; break;
            }
        }
        return new PrdFrontmatter { Task = task, TestCommand = testCommand, LintCommand = lintCommand, BrowserCommand = browserCommand, Engine = engine, Model = model };
    }

    private static bool IsFenceLine(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("```", StringComparison.Ordinal);
    }

    private static PrdDocument ParseYamlLines(string[] lines)
    {
        string? task = null;
        string? testCommand = null;
        string? lintCommand = null;
        string? browserCommand = null;
        string? engine = null;
        string? model = null;
        var taskEntries = new List<PrdTaskEntry>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var kv = line.IndexOf(':');
            if (kv > 0)
            {
                var key = line[..kv].Trim().ToLowerInvariant();
                var value = line[(kv + 1)..].Trim().Trim('"', '\'');
                switch (key)
                {
                    case "task": task = value; break;
                    case "test_command": testCommand = value; break;
                    case "lint_command": lintCommand = value; break;
                    case "browser_command": browserCommand = value; break;
                    case "engine": engine = value; break;
                    case "model": model = value; break;
                }
            }

            var match = TaskLineRegex.Match(line);
            if (!match.Success) continue;
            var status = ParseTaskStatusMarker(match.Groups[2].Value);
            var displayText = match.Groups[3].Value.Trim();
            taskEntries.Add(new PrdTaskEntry
            {
                LineIndex = i,
                Status = status,
                RawLine = line,
                DisplayText = displayText
            });
        }

        return new PrdDocument
        {
            RawLines = lines,
            Frontmatter = new PrdFrontmatter
            {
                Task = task,
                TestCommand = testCommand,
                LintCommand = lintCommand,
                BrowserCommand = browserCommand,
                Engine = engine,
                Model = model
            },
            TaskEntries = taskEntries
        };
    }

    private static PrdDocument ParseJson(string fullPath)
    {
        var json = File.ReadAllText(fullPath);
        using var doc = JsonDocument.Parse(json);
        var tasks = new List<PrdTaskEntry>();

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var text = TryGetString(item, "text") ?? TryGetString(item, "task") ?? $"Task {idx + 1}";
                var status = TryGetTaskStatus(item);
                tasks.Add(new PrdTaskEntry
                {
                    LineIndex = idx,
                    Status = status,
                    RawLine = text,
                    DisplayText = text
                });
                idx++;
            }
        }

        return new PrdDocument
        {
            RawLines = Array.Empty<string>(),
            Frontmatter = null,
            TaskEntries = tasks
        };
    }

    private static string? TryGetString(JsonElement item, string property)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;
        if (!item.TryGetProperty(property, out var p))
            return null;
        return p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }

    private static bool? TryGetBool(JsonElement item, string property)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;
        if (!item.TryGetProperty(property, out var p))
            return null;
        return p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False ? p.GetBoolean() : null;
    }

    private static PrdTaskStatus ParseTaskStatusMarker(string raw)
    {
        var marker = raw.Trim();
        if (marker.Equals("x", StringComparison.OrdinalIgnoreCase))
            return PrdTaskStatus.Completed;
        if (marker == "~")
            return PrdTaskStatus.SkippedForReview;
        return PrdTaskStatus.Pending;
    }

    private static PrdTaskStatus TryGetTaskStatus(JsonElement item)
    {
        var status = TryGetString(item, "status")?.Trim().ToLowerInvariant();
        return status switch
        {
            "completed" or "done" => PrdTaskStatus.Completed,
            "skipped_for_review" or "skipped-review" or "manual_review" or "manual-review" => PrdTaskStatus.SkippedForReview,
            _ => (TryGetBool(item, "completed") ?? false) ? PrdTaskStatus.Completed : PrdTaskStatus.Pending
        };
    }
}
