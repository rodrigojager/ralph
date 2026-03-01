namespace Ralph.Core.Prompting;

public static class PromptBuilder
{
    public static string BuildContext(string? guardrailsContent, string? progressContent, string taskText)
    {
        var parts = new List<string>();
        if (HasMeaningfulGuardrails(guardrailsContent))
            parts.Add($"## Guardrails\n{guardrailsContent!.Trim()}");
        if (!string.IsNullOrWhiteSpace(progressContent))
            parts.Add($"## Progress\n{progressContent.Trim()}");
        parts.Add($"## Current task\n{taskText.Trim()}");
        return string.Join("\n\n", parts);
    }

    private static bool HasMeaningfulGuardrails(string? guardrailsContent)
    {
        if (string.IsNullOrWhiteSpace(guardrailsContent))
            return false;

        var normalized = guardrailsContent
            .Replace("\r", "")
            .Trim()
            .ToLowerInvariant();

        if (normalized == "# guardrails")
            return false;

        if (normalized == "# guardrails\n\nadd constraints and rules here."
            || normalized == "# guardrails\nadd constraints and rules here.")
            return false;

        return true;
    }
}
