using System.Text;
using System.Text.Json;
using Ralph.Core.Localization;

namespace Ralph.Core.RunLoop.OutputAdapters;

internal sealed class CursorStreamJsonOutputAdapter : IEngineOutputDisplayAdapter
{
    public bool CanHandle(string engineName) =>
        engineName.Equals("cursor", StringComparison.OrdinalIgnoreCase);

    public string Adapt(string rawStdout, IStringCatalog strings)
    {
        if (string.IsNullOrWhiteSpace(rawStdout))
            return rawStdout;

        var summary = new CursorSummary();
        var sawJsonLine = false;
        var parsedEvents = 0;

        foreach (var rawLine in rawStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("{", StringComparison.Ordinal))
                continue;

            sawJsonLine = true;
            if (!TryParseJson(line, out var root))
                continue;
            parsedEvents++;

            ApplyEvent(root, summary);
        }

        var lines = BuildDisplayLines(summary, strings);
        if (lines.Count > 0)
            return string.Join(Environment.NewLine, lines);

        if (sawJsonLine)
            return parsedEvents > 0 ? string.Empty : rawStdout;

        var plain = FilterPlainText(rawStdout);
        return plain.Count > 0 ? string.Join(Environment.NewLine, plain) : rawStdout;
    }

    private static bool TryParseJson(string jsonLine, out JsonElement root)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            root = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            root = default;
            return false;
        }
    }

    private static void ApplyEvent(JsonElement root, CursorSummary summary)
    {
        var type = GetString(root, "type");
        if (string.IsNullOrWhiteSpace(type))
            return;

        if (type.Equals("thinking", StringComparison.OrdinalIgnoreCase)
            || type.Equals("reasoning", StringComparison.OrdinalIgnoreCase))
            return;

        if (type.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(summary.CurrentTask))
            {
                var userText = ExtractMessageText(root);
                if (TryExtractCurrentTask(userText, out var currentTask))
                    summary.CurrentTask = currentTask;
            }

            return;
        }

        if (type.Equals("assistant", StringComparison.OrdinalIgnoreCase))
        {
            var assistantText = ExtractMessageText(root);
            if (!string.IsNullOrWhiteSpace(assistantText) && !LooksLikeInternalReasoning(assistantText))
                summary.LastAssistantText = assistantText.Trim();
            return;
        }

        if (type.Equals("result", StringComparison.OrdinalIgnoreCase))
        {
            if (root.TryGetProperty("is_error", out var isErrorNode) && isErrorNode.ValueKind is JsonValueKind.True or JsonValueKind.False)
                summary.ResultIsError = isErrorNode.GetBoolean();

            var resultText = ExtractResultText(root);
            if (!string.IsNullOrWhiteSpace(resultText) && !LooksLikeInternalReasoning(resultText))
                summary.ResultText = resultText.Trim();
        }
    }

    private static List<string> BuildDisplayLines(CursorSummary summary, IStringCatalog strings)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(summary.CurrentTask))
            lines.Add($"{strings.Get("cursor.output.current_task")}: {NormalizeSingleLine(summary.CurrentTask)}");

        var finalText = string.IsNullOrWhiteSpace(summary.ResultText)
            ? summary.LastAssistantText
            : summary.ResultText;

        if (!string.IsNullOrWhiteSpace(finalText))
        {
            if (summary.ResultIsError.HasValue)
            {
                var statusKey = summary.ResultIsError.Value ? "cursor.output.error" : "cursor.output.success";
                lines.Add($"[{strings.Get(statusKey)}] - {NormalizeSingleLine(finalText)}");
            }
            else
            {
                lines.Add(NormalizeSingleLine(finalText));
            }
        }
        else if (summary.ResultIsError.HasValue)
        {
            var statusKey = summary.ResultIsError.Value ? "cursor.output.error" : "cursor.output.success";
            lines.Add($"[{strings.Get(statusKey)}]");
        }

        return RemoveAdjacentDuplicates(lines);
    }

    private static string ExtractResultText(JsonElement root)
    {
        if (!root.TryGetProperty("result", out var resultNode))
            return string.Empty;

        return ExtractAnyText(resultNode);
    }

    private static string ExtractMessageText(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var messageNode))
            return string.Empty;

        return ExtractAnyText(messageNode);
    }

    private static string ExtractAnyText(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.String)
            return node.GetString() ?? string.Empty;

        if (node.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in node.EnumerateArray())
            {
                var chunk = ExtractAnyText(item);
                if (string.IsNullOrWhiteSpace(chunk))
                    continue;

                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(chunk.Trim());
            }
            return sb.ToString();
        }

        if (node.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (node.TryGetProperty("content", out var contentNode))
        {
            var contentText = ExtractAnyText(contentNode);
            if (!string.IsNullOrWhiteSpace(contentText))
                return contentText;
        }

        var direct = GetString(node, "text", "message", "delta", "result");
        if (!string.IsNullOrWhiteSpace(direct))
            return direct;

        return string.Empty;
    }

    private static bool TryExtractCurrentTask(string promptText, out string currentTask)
    {
        if (string.IsNullOrWhiteSpace(promptText))
        {
            currentTask = string.Empty;
            return false;
        }

        var normalized = promptText.Replace("\r\n", "\n");
        var marker = "## Current task";
        var start = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            currentTask = string.Empty;
            return false;
        }

        var firstNewLine = normalized.IndexOf('\n', start);
        if (firstNewLine < 0 || firstNewLine + 1 >= normalized.Length)
        {
            currentTask = string.Empty;
            return false;
        }

        var afterHeader = normalized[(firstNewLine + 1)..];
        var nextSection = afterHeader.IndexOf("\n## ", StringComparison.OrdinalIgnoreCase);
        if (nextSection >= 0)
            afterHeader = afterHeader[..nextSection];

        var task = NormalizeSingleLine(afterHeader);
        if (string.IsNullOrWhiteSpace(task))
        {
            currentTask = string.Empty;
            return false;
        }

        currentTask = task;
        return true;
    }

    private static string GetString(JsonElement node, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!node.TryGetProperty(key, out var prop))
                continue;
            if (prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static List<string> FilterPlainText(string rawStdout)
    {
        var filtered = new List<string>();
        string? previous = null;
        foreach (var original in rawStdout.Split('\n'))
        {
            var line = original.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (line.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.StartsWith("Reading from stdin via:", StringComparison.OrdinalIgnoreCase))
                continue;
            if (LooksLikeInternalReasoning(line))
                continue;
            if (previous != null && previous.Equals(line, StringComparison.Ordinal))
                continue;

            filtered.Add(line);
            previous = line;
        }

        return filtered;
    }

    private static bool LooksLikeInternalReasoning(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.TrimStart();
        return normalized.StartsWith("The user wants me to", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("The user asked me to", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("O usuário quer que eu", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("<thinking>", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("</thinking>", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSingleLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var parts = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();
        return string.Join(" ", parts);
    }

    private static List<string> RemoveAdjacentDuplicates(List<string> lines)
    {
        if (lines.Count <= 1)
            return lines;

        var deduped = new List<string>(lines.Count);
        string? previous = null;
        foreach (var line in lines)
        {
            if (previous != null && previous.Equals(line, StringComparison.Ordinal))
                continue;

            deduped.Add(line);
            previous = line;
        }

        return deduped;
    }

    private sealed class CursorSummary
    {
        public string CurrentTask { get; set; } = string.Empty;
        public string LastAssistantText { get; set; } = string.Empty;
        public string ResultText { get; set; } = string.Empty;
        public bool? ResultIsError { get; set; }
    }
}
