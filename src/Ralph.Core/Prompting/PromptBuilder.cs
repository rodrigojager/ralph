namespace Ralph.Core.Prompting;

public static class PromptBuilder
{
    public static string BuildLoopContext(string? guardrailsContent, string? sharedPrdContent, string taskText)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sharedPrdContent))
            parts.Add($"## PRD context\n{sharedPrdContent.Trim()}");
        if (HasMeaningfulGuardrails(guardrailsContent))
            parts.Add($"## Guardrails\n{guardrailsContent!.Trim()}");
        parts.Add($"## Current task\n{taskText.Trim()}");
        return string.Join("\n\n", parts);
    }

    public static string BuildWiggumContext(string? guardrailsContent, string? fullPrdContent, string taskText)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(fullPrdContent))
            parts.Add($"## Full PRD\n{fullPrdContent.Trim()}");
        if (HasMeaningfulGuardrails(guardrailsContent))
            parts.Add($"## Guardrails\n{guardrailsContent!.Trim()}");
        parts.Add($"## Active task\n{taskText.Trim()}");
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
