using System.Text.RegularExpressions;
using System.Text.Json;

namespace Ralph.Tasks.Prd;

public static class PrdParser
{
    private static readonly Regex TaskLineRegex = new(
        @"^(\s*)-\s+\[([ xX~])\]\s*(.*)$",
        RegexOptions.Compiled);
    private static readonly Regex InlineMetadataRegex = new(
        @"\[(id|group|depends|depends_on|priority|complexity|files_allowed|gate|gates)\s*:\s*([^\]]+)\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                var inline = ParseInlineMetadata(match.Groups[3].Value.Trim());
                var block = ParseTaskMetadata(lines, i, match.Groups[1].Value.Length);
                var metadata = inline.Merge(block);
                taskEntries.Add(new PrdTaskEntry
                {
                    LineIndex = i,
                    Status = status,
                    RawLine = line,
                    DisplayText = inline.CleanText,
                    Id = metadata.Id,
                    Group = metadata.Group ?? "default",
                    DependsOn = metadata.DependsOn,
                    AcceptanceCriteria = metadata.AcceptanceCriteria,
                    FilesAllowed = metadata.FilesAllowed,
                    Gates = metadata.Gates,
                    Complexity = metadata.Complexity,
                    Priority = metadata.Priority,
                    Notes = metadata.Notes
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
        var securityMode = (string?)null;
        var sandbox = (string?)null;
        var includeRepoMap = (bool?)null;
        var gates = new List<PrdGate>();
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
                case "security":
                case "security_mode": securityMode = value; break;
                case "sandbox": sandbox = value; break;
                case "repo_map":
                case "include_repo_map":
                    if (bool.TryParse(value, out var repoMap))
                        includeRepoMap = repoMap;
                    break;
                case "gate":
                case "gates":
                    gates.AddRange(ParseGateList(value));
                    break;
            }
        }
        gates.AddRange(ParseFrontmatterGateBlocks(lines));
        return new PrdFrontmatter
        {
            Task = task,
            TestCommand = testCommand,
            LintCommand = lintCommand,
            BrowserCommand = browserCommand,
            Engine = engine,
            Model = model,
            SecurityMode = securityMode,
            Sandbox = sandbox,
            IncludeRepoMap = includeRepoMap,
            Gates = gates
        };
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
        string? securityMode = null;
        string? sandbox = null;
        bool? includeRepoMap = null;
        var gates = new List<PrdGate>();
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
                    case "security":
                    case "security_mode": securityMode = value; break;
                    case "sandbox": sandbox = value; break;
                    case "repo_map":
                    case "include_repo_map":
                        if (bool.TryParse(value, out var repoMap))
                            includeRepoMap = repoMap;
                        break;
                    case "gate":
                    case "gates":
                        gates.AddRange(ParseGateList(value));
                        break;
                }
            }

            var match = TaskLineRegex.Match(line);
            if (!match.Success) continue;
            var status = ParseTaskStatusMarker(match.Groups[2].Value);
            var inline = ParseInlineMetadata(match.Groups[3].Value.Trim());
            var block = ParseTaskMetadata(lines, i, match.Groups[1].Value.Length);
            var metadata = inline.Merge(block);
            taskEntries.Add(new PrdTaskEntry
            {
                LineIndex = i,
                Status = status,
                RawLine = line,
                DisplayText = inline.CleanText,
                Id = metadata.Id,
                Group = metadata.Group ?? "default",
                DependsOn = metadata.DependsOn,
                AcceptanceCriteria = metadata.AcceptanceCriteria,
                FilesAllowed = metadata.FilesAllowed,
                Gates = metadata.Gates,
                Complexity = metadata.Complexity,
                Priority = metadata.Priority,
                Notes = metadata.Notes
            });
        }

        if (taskEntries.Count == 0)
            taskEntries = ParseStructuredYamlTasks(lines);

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
                Model = model,
                SecurityMode = securityMode,
                Sandbox = sandbox,
                IncludeRepoMap = includeRepoMap,
                Gates = gates.Concat(ParseFrontmatterGateBlocks(lines)).ToArray()
            },
            TaskEntries = taskEntries
        };
    }

    private static PrdDocument ParseJson(string fullPath)
    {
        var json = File.ReadAllText(fullPath);
        using var doc = JsonDocument.Parse(json);
        var tasks = new List<PrdTaskEntry>();
        var root = doc.RootElement;
        var source = root;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("tasks", out var taskArray))
            source = taskArray;

        if (source.ValueKind == JsonValueKind.Array)
        {
            var idx = 0;
            foreach (var item in source.EnumerateArray())
            {
                var text = TryGetString(item, "title")
                           ?? TryGetString(item, "text")
                           ?? TryGetString(item, "task")
                           ?? $"Task {idx + 1}";
                var status = TryGetTaskStatus(item);
                tasks.Add(new PrdTaskEntry
                {
                    LineIndex = idx,
                    Status = status,
                    RawLine = text,
                    DisplayText = text,
                    Id = TryGetString(item, "id"),
                    Group = TryGetString(item, "group") ?? "default",
                    DependsOn = TryGetStringArray(item, "depends_on", "depends"),
                    AcceptanceCriteria = TryGetStringArray(item, "acceptance_criteria", "acceptance"),
                    FilesAllowed = TryGetStringArray(item, "files_allowed"),
                    Gates = TryGetGateArray(item, "gates", "gate"),
                    Complexity = TryGetInt(item, "complexity"),
                    Priority = TryGetString(item, "priority"),
                    Notes = TryGetString(item, "notes")
                });
                idx++;
            }
        }

        return new PrdDocument
        {
            RawLines = Array.Empty<string>(),
            Frontmatter = root.ValueKind == JsonValueKind.Object ? ParseJsonFrontmatter(root) : null,
            TaskEntries = tasks
        };
    }

    private static PrdFrontmatter ParseJsonFrontmatter(JsonElement root)
    {
        var fm = root.TryGetProperty("frontmatter", out var nested) && nested.ValueKind == JsonValueKind.Object
            ? nested
            : root;

        return new PrdFrontmatter
        {
            Task = TryGetString(fm, "task"),
            TestCommand = TryGetString(fm, "test_command"),
            LintCommand = TryGetString(fm, "lint_command"),
            BrowserCommand = TryGetString(fm, "browser_command"),
            Engine = TryGetString(fm, "engine"),
            Model = TryGetString(fm, "model"),
            SecurityMode = TryGetString(fm, "security_mode") ?? TryGetString(fm, "security"),
            Sandbox = TryGetString(fm, "sandbox"),
            IncludeRepoMap = TryGetBool(fm, "include_repo_map") ?? TryGetBool(fm, "repo_map"),
            Gates = TryGetGateArray(fm, "gates", "gate")
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

    private static int? TryGetInt(JsonElement item, string property)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;
        if (!item.TryGetProperty(property, out var p))
            return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var value))
            return value;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out value))
            return value;
        return null;
    }

    private static IReadOnlyList<string> TryGetStringArray(JsonElement item, params string[] properties)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return Array.Empty<string>();
        foreach (var property in properties)
        {
            if (!item.TryGetProperty(property, out var p))
                continue;
            return ReadStringList(p);
        }
        return Array.Empty<string>();
    }

    private static IReadOnlyList<PrdGate> TryGetGateArray(JsonElement item, params string[] properties)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return Array.Empty<PrdGate>();
        foreach (var property in properties)
        {
            if (!item.TryGetProperty(property, out var p))
                continue;

            if (p.ValueKind == JsonValueKind.String)
                return ParseGateList(p.GetString() ?? string.Empty);

            if (p.ValueKind == JsonValueKind.Array)
            {
                var gates = new List<PrdGate>();
                foreach (var gate in p.EnumerateArray())
                {
                    if (gate.ValueKind == JsonValueKind.String)
                    {
                        gates.AddRange(ParseGateList(gate.GetString() ?? string.Empty));
                        continue;
                    }

                    if (gate.ValueKind != JsonValueKind.Object)
                        continue;

                    var name = TryGetString(gate, "name") ?? "gate";
                    var command = TryGetString(gate, "command") ?? string.Empty;
                    gates.Add(new PrdGate
                    {
                        Name = name,
                        Command = command,
                        Required = TryGetBool(gate, "required") ?? true,
                        Policy = TryGetString(gate, "policy") ?? "block"
                    });
                }
                return gates;
            }
        }
        return Array.Empty<PrdGate>();
    }

    private static IReadOnlyList<string> ReadStringList(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToArray();

        if (element.ValueKind == JsonValueKind.String)
            return SplitList(element.GetString());

        return Array.Empty<string>();
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

    private static TaskMetadata ParseInlineMetadata(string text)
    {
        var metadata = new TaskMetadata { CleanText = InlineMetadataRegex.Replace(text, string.Empty).Trim() };
        foreach (Match match in InlineMetadataRegex.Matches(text))
            metadata.Apply(match.Groups[1].Value, match.Groups[2].Value);
        return metadata;
    }

    private static TaskMetadata ParseTaskMetadata(string[] lines, int taskLineIndex, int taskIndent)
    {
        var metadata = new TaskMetadata();
        for (var i = taskLineIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (TaskLineRegex.IsMatch(line) || IsFenceLine(line))
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (CountLeadingWhitespace(line) <= taskIndent && line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                break;

            var trimmed = line.Trim();
            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
                continue;

            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim().Trim('"', '\'');
            metadata.Apply(key, value);
        }
        return metadata;
    }

    private static List<PrdGate> ParseFrontmatterGateBlocks(IReadOnlyList<string> lines)
    {
        var gates = new List<PrdGate>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Trim().Equals("gates:", StringComparison.OrdinalIgnoreCase))
                continue;

            for (var j = i + 1; j < lines.Count; j++)
            {
                var trimmed = lines[j].Trim();
                if (trimmed.Length == 0)
                    continue;
                if (!lines[j].StartsWith(" ", StringComparison.Ordinal) && !lines[j].StartsWith("\t", StringComparison.Ordinal))
                    break;
                if (!trimmed.StartsWith("-", StringComparison.Ordinal))
                    continue;

                var raw = trimmed[1..].Trim();
                if (raw.Contains(':', StringComparison.Ordinal) || raw.Contains('=', StringComparison.Ordinal))
                {
                    var parts = raw.Split(new[] { ':', '=' }, 2);
                    gates.Add(new PrdGate { Name = parts[0].Trim(), Command = parts[1].Trim().Trim('"', '\'') });
                }
                else
                {
                    gates.Add(new PrdGate { Name = raw, Command = string.Empty });
                }
            }
        }
        return gates;
    }

    private static List<ParallelYamlTaskBuilder> ReadStructuredYamlTaskBlocks(string[] lines)
    {
        var blocks = new List<ParallelYamlTaskBuilder>();
        var inTasks = false;
        ParallelYamlTaskBuilder? current = null;

        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (trimmed.Equals("tasks:", StringComparison.OrdinalIgnoreCase))
            {
                inTasks = true;
                continue;
            }
            if (!inTasks)
                continue;
            if (trimmed.Length == 0)
                continue;
            if (!raw.StartsWith(" ", StringComparison.Ordinal) && !raw.StartsWith("\t", StringComparison.Ordinal) && !trimmed.StartsWith("-", StringComparison.Ordinal))
                break;

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                current = new ParallelYamlTaskBuilder();
                blocks.Add(current);
                ApplyYamlKeyValue(current, trimmed[2..]);
                continue;
            }

            if (current != null)
                ApplyYamlKeyValue(current, trimmed);
        }

        return blocks;
    }

    private static List<PrdTaskEntry> ParseStructuredYamlTasks(string[] lines)
    {
        var output = new List<PrdTaskEntry>();
        var blocks = ReadStructuredYamlTaskBlocks(lines);
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var text = block.Get("title") ?? block.Get("text") ?? block.Get("task") ?? $"Task {i + 1}";
            output.Add(new PrdTaskEntry
            {
                LineIndex = i,
                Status = ParseStructuredStatus(block.Get("status"), block.Get("completed")),
                RawLine = text,
                DisplayText = text,
                Id = block.Get("id"),
                Group = block.Get("group") ?? "default",
                DependsOn = SplitList(block.Get("depends_on") ?? block.Get("depends")),
                AcceptanceCriteria = SplitList(block.Get("acceptance_criteria") ?? block.Get("acceptance")),
                FilesAllowed = SplitList(block.Get("files_allowed")),
                Gates = ParseGateList(block.Get("gates") ?? block.Get("gate")),
                Complexity = int.TryParse(block.Get("complexity"), out var complexity) ? complexity : null,
                Priority = block.Get("priority"),
                Notes = block.Get("notes")
            });
        }
        return output;
    }

    private static void ApplyYamlKeyValue(ParallelYamlTaskBuilder builder, string raw)
    {
        var colon = raw.IndexOf(':');
        if (colon <= 0)
            return;
        var key = raw[..colon].Trim();
        var value = raw[(colon + 1)..].Trim().Trim('"', '\'');
        builder.Set(key, value);
    }

    private static PrdTaskStatus ParseStructuredStatus(string? status, string? completed)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        if (normalized is "completed" or "done" or "x")
            return PrdTaskStatus.Completed;
        if (normalized is "skipped_for_review" or "manual_review" or "skipped-review" or "~")
            return PrdTaskStatus.SkippedForReview;
        if (bool.TryParse(completed, out var isCompleted) && isCompleted)
            return PrdTaskStatus.Completed;
        return PrdTaskStatus.Pending;
    }

    private static List<PrdGate> ParseGateList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new List<PrdGate>();

        var normalized = raw.Trim().Trim('[', ']');
        var separators = normalized.Contains(';') ? new[] { ';' } : new[] { ',' };
        return normalized
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseGate)
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .ToList();
    }

    private static PrdGate ParseGate(string raw)
    {
        var parts = raw.Split('|', StringSplitOptions.TrimEntries);
        var nameAndCommand = parts[0];
        var split = nameAndCommand.Split(new[] { '=' }, 2);
        if (split.Length == 1)
            split = nameAndCommand.Split(new[] { ':' }, 2);

        var gate = new PrdGate
        {
            Name = split[0].Trim(),
            Command = split.Length > 1 ? split[1].Trim().Trim('"', '\'') : string.Empty,
            Required = true,
            Policy = "block"
        };

        foreach (var option in parts.Skip(1))
        {
            var kv = option.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2)
                continue;
            if (kv[0].Equals("required", StringComparison.OrdinalIgnoreCase) && bool.TryParse(kv[1], out var required))
                gate = new PrdGate { Name = gate.Name, Command = gate.Command, Required = required, Policy = gate.Policy };
            if (kv[0].Equals("policy", StringComparison.OrdinalIgnoreCase))
                gate = new PrdGate { Name = gate.Name, Command = gate.Command, Required = gate.Required, Policy = kv[1] };
        }

        return gate;
    }

    private static IReadOnlyList<string> SplitList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();
        return raw.Trim().Trim('[', ']')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim('"', '\''))
            .Where(x => x.Length > 0)
            .ToArray();
    }

    private static int CountLeadingWhitespace(string value)
    {
        var count = 0;
        foreach (var ch in value)
        {
            if (ch is not (' ' or '\t'))
                break;
            count++;
        }
        return count;
    }

    private sealed class TaskMetadata
    {
        public string CleanText { get; set; } = string.Empty;
        public string? Id { get; private set; }
        public string? Group { get; private set; }
        public IReadOnlyList<string> DependsOn { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> AcceptanceCriteria { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> FilesAllowed { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<PrdGate> Gates { get; private set; } = Array.Empty<PrdGate>();
        public int? Complexity { get; private set; }
        public string? Priority { get; private set; }
        public string? Notes { get; private set; }

        public void Apply(string key, string value)
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "id": Id = EmptyToNull(value); break;
                case "group":
                case "parallel_group": Group = EmptyToNull(value); break;
                case "depends":
                case "depends_on": DependsOn = SplitList(value); break;
                case "acceptance":
                case "acceptance_criteria": AcceptanceCriteria = SplitList(value); break;
                case "files_allowed": FilesAllowed = SplitList(value); break;
                case "gate":
                case "gates": Gates = ParseGateList(value); break;
                case "complexity":
                    if (int.TryParse(value, out var complexity))
                        Complexity = complexity;
                    break;
                case "priority": Priority = EmptyToNull(value); break;
                case "notes": Notes = EmptyToNull(value); break;
            }
        }

        public TaskMetadata Merge(TaskMetadata other)
        {
            return new TaskMetadata
            {
                CleanText = string.IsNullOrWhiteSpace(CleanText) ? other.CleanText : CleanText,
                Id = Id ?? other.Id,
                Group = Group ?? other.Group,
                DependsOn = DependsOn.Count > 0 ? DependsOn : other.DependsOn,
                AcceptanceCriteria = AcceptanceCriteria.Count > 0 ? AcceptanceCriteria : other.AcceptanceCriteria,
                FilesAllowed = FilesAllowed.Count > 0 ? FilesAllowed : other.FilesAllowed,
                Gates = Gates.Count > 0 ? Gates : other.Gates,
                Complexity = Complexity ?? other.Complexity,
                Priority = Priority ?? other.Priority,
                Notes = Notes ?? other.Notes
            };
        }

        private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed class ParallelYamlTaskBuilder
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
        public void Set(string key, string value) => _values[key.Trim()] = value.Trim();
        public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;
    }
}
