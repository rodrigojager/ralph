using System.Text.Json;

namespace Ralph.Engines.Runtime;

internal enum PromptTransportMode
{
    Argument,
    Stdin
}

internal enum EngineOutputMode
{
    PlainText,
    StreamJson
}

internal sealed record EngineExecutionProfile(
    string EngineName,
    PromptTransportMode PromptTransport,
    EngineOutputMode OutputMode,
    bool RequiresShellOnWindows,
    string? PromptFlag = null)
{
    public static EngineExecutionProfile For(string engineName)
    {
        var normalized = engineName.Trim().ToLowerInvariant();
        return normalized switch
        {
            "codex" => new EngineExecutionProfile(normalized, PromptTransportMode.Stdin, EngineOutputMode.StreamJson, true),
            "cursor" => new EngineExecutionProfile(normalized, PromptTransportMode.Argument, EngineOutputMode.StreamJson, true, "-p"),
            "gemini" => new EngineExecutionProfile(normalized, PromptTransportMode.Argument, EngineOutputMode.StreamJson, true, "-p"),
            _ => new EngineExecutionProfile(normalized, PromptTransportMode.Argument, EngineOutputMode.PlainText, true)
        };
    }
}

internal sealed record StreamJsonParseResult(bool HasStructuredError, string AssistantText);

internal static class StreamJsonOutputParser
{
    public static StreamJsonParseResult Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return new StreamJsonParseResult(false, string.Empty);

        var hasError = false;
        var textParts = new List<string>();
        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (!line.StartsWith("{", StringComparison.Ordinal))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (IsErrorEvent(root))
                    hasError = true;

                var text = ExtractText(root);
                if (!string.IsNullOrWhiteSpace(text))
                    textParts.Add(text);
            }
            catch
            {
                // Ignore malformed lines to keep parser tolerant.
            }
        }

        return new StreamJsonParseResult(hasError, string.Join(Environment.NewLine, textParts));
    }

    private static bool IsErrorEvent(JsonElement root)
    {
        if (TryGetString(root, out var type, "type", "event", "kind")
            && type.Contains("error", StringComparison.OrdinalIgnoreCase))
            return true;

        if (TryGetString(root, out var level, "level", "severity")
            && level.Contains("error", StringComparison.OrdinalIgnoreCase))
            return true;

        return root.TryGetProperty("error", out _);
    }

    private static string ExtractText(JsonElement root)
    {
        if (TryGetString(root, out var value, "text", "message", "content", "delta"))
            return value;

        if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var chunks = new List<string>();
            foreach (var item in content.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    chunks.Add(item.GetString() ?? string.Empty);
                else if (TryGetString(item, out var nested, "text", "message", "content"))
                    chunks.Add(nested);
            }
            return string.Join(string.Empty, chunks.Where(c => !string.IsNullOrWhiteSpace(c)));
        }

        return string.Empty;
    }

    private static bool TryGetString(JsonElement root, out string value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!root.TryGetProperty(key, out var prop))
                continue;

            if (prop.ValueKind == JsonValueKind.String)
            {
                value = prop.GetString() ?? string.Empty;
                return true;
            }

            if (prop.ValueKind == JsonValueKind.Object && TryGetString(prop, out value, "text", "message", "content"))
                return true;
        }

        value = string.Empty;
        return false;
    }
}
