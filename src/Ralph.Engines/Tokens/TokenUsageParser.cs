using System.Text.RegularExpressions;
using System.Globalization;

namespace Ralph.Engines.Tokens;

internal static class TokenUsageParser
{
    public static TokenUsage? Parse(string stdout, string stderr, string engineName)
    {
        var combined = $"{stdout}\n{stderr}";

        var input = ParseFirst(
            combined,
            @"""input_tokens""\s*:\s*([0-9][0-9\.,_]*)",
            @"""inputTokenCount""\s*:\s*([0-9][0-9\.,_]*)",
            @"""inputTokens""\s*:\s*([0-9][0-9\.,_]*)",
            @"""prompt_tokens""\s*:\s*([0-9][0-9\.,_]*)",
            @"""promptTokens""\s*:\s*([0-9][0-9\.,_]*)",
            @"\binput[_\s-]*tokens?\b\s*(?:[:=]\s*|\r?\n\s*)([0-9][0-9\.,_]*)",
            @"\bprompt[_\s-]*tokens?\b\s*(?:[:=]\s*|\r?\n\s*)([0-9][0-9\.,_]*)",
            @"\bin\b\s*(?:[:=]\s*|\r?\n\s*)([0-9][0-9\.,_]*)"
        );

        var output = ParseFirst(
            combined,
            @"""output_tokens""\s*:\s*([0-9][0-9\.,_]*)",
            @"""outputTokenCount""\s*:\s*([0-9][0-9\.,_]*)",
            @"""outputTokens""\s*:\s*([0-9][0-9\.,_]*)",
            @"""completion_tokens""\s*:\s*([0-9][0-9\.,_]*)",
            @"""completionTokens""\s*:\s*([0-9][0-9\.,_]*)",
            @"\boutput[_\s-]*tokens?\b\s*(?:[:=]\s*|\r?\n\s*)([0-9][0-9\.,_]*)",
            @"\bcompletion[_\s-]*tokens?\b\s*(?:[:=]\s*|\r?\n\s*)([0-9][0-9\.,_]*)",
            @"\bout\b\s*(?:[:=]\s*|\r?\n\s*)([0-9][0-9\.,_]*)"
        );

        var total = ParseFirst(
            combined,
            @"""total_tokens""\s*:\s*([0-9][0-9\.,_]*)",
            @"""totalTokenCount""\s*:\s*([0-9][0-9\.,_]*)",
            @"""totalTokens""\s*:\s*([0-9][0-9\.,_]*)",
            @"\btotal[_\s-]*tokens?\b\s*(?:[:=]\s*|\r?\n\s*)([0-9][0-9\.,_]*)",
            @"\btokens[_\s-]*used\b\s*(?:[:=]\s*|\r?\n\s*)([0-9][0-9\.,_]*)"
        );

        var estimatedCost = ParseDecimal(
            combined,
            @"""estimated_cost_usd""\s*:\s*([0-9]+(?:\.[0-9]+)?)",
            @"\bcost(?:_usd)?\b\s*[:=]\s*\$?([0-9]+(?:\.[0-9]+)?)"
        );
        var contextUsed = ParseDouble(
            combined,
            @"""context_used_percent""\s*:\s*([0-9]+(?:\.[0-9]+)?)",
            @"\bcontext(?:_used)?(?:_percent)?\b\s*[:=]\s*([0-9]+(?:\.[0-9]+)?)%?"
        );

        if (input.HasValue || output.HasValue)
            return new TokenUsage { InputTokens = input ?? 0, OutputTokens = output ?? 0, EstimatedCostUsd = estimatedCost, ContextUsedPercent = contextUsed };

        if (total.HasValue)
            return new TokenUsage { InputTokens = 0, OutputTokens = total.Value, EstimatedCostUsd = estimatedCost, ContextUsedPercent = contextUsed };

        return null;
    }

    private static int? ParseFirst(string input, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && TryParseFlexibleInt(match.Groups[1].Value, out var value))
                return value;
        }

        return null;
    }

    private static bool TryParseFlexibleInt(string raw, out int value)
    {
        var normalized = raw.Trim()
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static decimal? ParseDecimal(string input, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
        }
        return null;
    }

    private static double? ParseDouble(string input, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return value;
        }
        return null;
    }
}
