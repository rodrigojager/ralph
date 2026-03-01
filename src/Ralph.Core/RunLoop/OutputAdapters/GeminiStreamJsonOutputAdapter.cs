using System.Text;
using System.Text.Json;
using Ralph.Core.Localization;

namespace Ralph.Core.RunLoop.OutputAdapters;

internal sealed class GeminiStreamJsonOutputAdapter : IEngineOutputDisplayAdapter
{
    public bool CanHandle(string engineName) =>
        engineName.Equals("gemini", StringComparison.OrdinalIgnoreCase);

    public string Adapt(string rawStdout, IStringCatalog strings)
    {
        if (string.IsNullOrWhiteSpace(rawStdout))
            return rawStdout;

        var lines = new List<string>();
        var assistantBuffer = new StringBuilder();

        foreach (var rawLine in rawStdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("{", StringComparison.Ordinal))
                continue;

            if (!TryParseEvent(line, out var ev))
                continue;

            switch (ev.Type)
            {
                case "message":
                    if (ev.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                        && !ev.IsThinking
                        && !string.IsNullOrWhiteSpace(ev.Content)
                        && !LooksLikeThinking(ev.Content))
                    {
                        assistantBuffer.Append(ev.Content);
                    }
                    else
                    {
                        FlushAssistantBuffer(lines, assistantBuffer);
                    }
                    break;

                case "content":
                    if (!ev.IsThinking
                        && !string.IsNullOrWhiteSpace(ev.Content)
                        && !LooksLikeThinking(ev.Content))
                    {
                        assistantBuffer.Append(ev.Content);
                    }
                    else
                    {
                        FlushAssistantBuffer(lines, assistantBuffer);
                    }
                    break;

                case "tool_use":
                    FlushAssistantBuffer(lines, assistantBuffer);
                    if (!string.IsNullOrWhiteSpace(ev.ToolName))
                        lines.Add($"[tool] {ev.ToolName}");
                    break;

                case "tool_result":
                    FlushAssistantBuffer(lines, assistantBuffer);
                    if (!string.IsNullOrWhiteSpace(ev.ToolStatus))
                        lines.Add($"[tool:{ev.ToolStatus}]");
                    break;

                case "result":
                    FlushAssistantBuffer(lines, assistantBuffer);
                    break;

                default:
                    FlushAssistantBuffer(lines, assistantBuffer);
                    break;
            }
        }

        FlushAssistantBuffer(lines, assistantBuffer);
        return lines.Count == 0 ? rawStdout : string.Join(Environment.NewLine, lines);
    }

    private static bool TryParseEvent(string jsonLine, out GeminiEvent ev)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var typeNode) && typeNode.ValueKind == JsonValueKind.String
                ? (typeNode.GetString() ?? string.Empty)
                : string.Empty;

            var role = root.TryGetProperty("role", out var roleNode) && roleNode.ValueKind == JsonValueKind.String
                ? (roleNode.GetString() ?? string.Empty)
                : string.Empty;

            var content = TryGetText(root);

            var toolName = root.TryGetProperty("tool_name", out var toolNode) && toolNode.ValueKind == JsonValueKind.String
                ? (toolNode.GetString() ?? string.Empty)
                : string.Empty;

            var toolStatus = root.TryGetProperty("status", out var statusNode) && statusNode.ValueKind == JsonValueKind.String
                ? (statusNode.GetString() ?? string.Empty)
                : string.Empty;

            var isThinking = HasThinkingMarker(root);
            ev = new GeminiEvent(type, role, content, toolName, toolStatus, isThinking);
            return !string.IsNullOrWhiteSpace(ev.Type);
        }
        catch
        {
            ev = GeminiEvent.Empty;
            return false;
        }
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

    private static bool LooksLikeThinking(string content)
    {
        return content.Contains("<thinking>", StringComparison.OrdinalIgnoreCase)
               || content.Contains("</thinking>", StringComparison.OrdinalIgnoreCase)
               || content.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
               || content.Contains("thought process", StringComparison.OrdinalIgnoreCase);
    }

    private static string TryGetText(JsonElement root)
    {
        if (root.TryGetProperty("content", out var contentNode) && contentNode.ValueKind == JsonValueKind.String)
            return contentNode.GetString() ?? string.Empty;
        if (root.TryGetProperty("text", out var textNode) && textNode.ValueKind == JsonValueKind.String)
            return textNode.GetString() ?? string.Empty;
        if (root.TryGetProperty("message", out var messageNode) && messageNode.ValueKind == JsonValueKind.String)
            return messageNode.GetString() ?? string.Empty;
        if (root.TryGetProperty("delta", out var deltaNode) && deltaNode.ValueKind == JsonValueKind.String)
            return deltaNode.GetString() ?? string.Empty;
        return string.Empty;
    }

    private readonly record struct GeminiEvent(
        string Type,
        string Role,
        string Content,
        string ToolName,
        string ToolStatus,
        bool IsThinking)
    {
        public static GeminiEvent Empty => new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false);
    }

    private static bool HasThinkingMarker(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var prop in node.EnumerateObject())
        {
            if (prop.Name.Contains("thinking", StringComparison.OrdinalIgnoreCase)
                || prop.Name.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
                || (prop.NameEquals("thought") && prop.Value.ValueKind == JsonValueKind.True))
                return true;
        }

        return false;
    }
}
