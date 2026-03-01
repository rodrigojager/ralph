using System.Text;
using System.Text.Json;
using Ralph.Core.Localization;

namespace Ralph.Core.RunLoop.OutputAdapters;

internal sealed class CodexStreamJsonOutputAdapter : IEngineOutputDisplayAdapter
{
    public bool CanHandle(string engineName) =>
        engineName.Equals("codex", StringComparison.OrdinalIgnoreCase);

    public string Adapt(string rawStdout, IStringCatalog strings)
    {
        if (string.IsNullOrWhiteSpace(rawStdout))
            return rawStdout;

        var lines = new List<string>();
        var assistantBuffer = new StringBuilder();
        var sawJson = false;
        var parsedEvents = 0;

        foreach (var rawLine in rawStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = NormalizeLine(rawLine);
            if (!line.StartsWith("{", StringComparison.Ordinal))
                continue;

            sawJson = true;
            if (!TryParseEvent(line, out var ev))
                continue;
            parsedEvents++;

            if (ev.Type.Equals("turn.completed", StringComparison.OrdinalIgnoreCase))
            {
                FlushAssistantBuffer(lines, assistantBuffer);
                continue;
            }

            if (IsAssistantLikeRole(ev.Role)
                && !ev.IsThinking
                && !LooksLikeInternalReasoning(ev.Content)
                && !string.IsNullOrWhiteSpace(ev.Content))
            {
                assistantBuffer.Append(ev.Content);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ev.Content)
                && !ev.IsThinking
                && !LooksLikeInternalReasoning(ev.Content))
            {
                assistantBuffer.Append(ev.Content);
            }
        }

        FlushAssistantBuffer(lines, assistantBuffer);
        if (lines.Count > 0)
            return string.Join(Environment.NewLine, lines);

        if (sawJson)
            return parsedEvents > 0 ? string.Empty : rawStdout;

        return rawStdout;
    }

    private static string NormalizeLine(string rawLine)
    {
        var line = rawLine.Trim();
        if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            line = line["data:".Length..].Trim();
        return line;
    }

    private static bool TryParseEvent(string jsonLine, out CodexEvent ev)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            var type = GetString(root, "type");
            var role = GetString(root, "role");
            var content = GetString(root, "text", "content", "message", "delta");
            var isThinking = false;

            if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
            {
                var itemType = GetString(item, "type");
                if (itemType.Equals("agent_message", StringComparison.OrdinalIgnoreCase))
                    role = "assistant";
                else if (!string.IsNullOrWhiteSpace(itemType))
                    role = itemType;

                if (string.IsNullOrWhiteSpace(content))
                    content = GetString(item, "text", "content", "message");

                isThinking = itemType.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
                             || itemType.Contains("thinking", StringComparison.OrdinalIgnoreCase)
                             || itemType.Contains("analysis", StringComparison.OrdinalIgnoreCase);
            }

            ev = new CodexEvent(type, role, content, isThinking);
            return !string.IsNullOrWhiteSpace(ev.Type) || !string.IsNullOrWhiteSpace(ev.Content);
        }
        catch
        {
            ev = CodexEvent.Empty;
            return false;
        }
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

    private static bool IsAssistantLikeRole(string role)
    {
        return role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
               || role.Equals("agent_message", StringComparison.OrdinalIgnoreCase)
               || role.Equals("agent", StringComparison.OrdinalIgnoreCase)
               || role.Equals("model", StringComparison.OrdinalIgnoreCase);
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

    private static void FlushAssistantBuffer(List<string> lines, StringBuilder assistantBuffer)
    {
        if (assistantBuffer.Length == 0)
            return;

        var text = assistantBuffer.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(text))
            lines.Add(text);
        assistantBuffer.Clear();
    }

    private readonly record struct CodexEvent(
        string Type,
        string Role,
        string Content,
        bool IsThinking)
    {
        public static CodexEvent Empty => new(string.Empty, string.Empty, string.Empty, false);
    }
}
