namespace Ralph.Engines.Tokens;

public sealed class TokenUsage
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int TotalTokens => InputTokens + OutputTokens;

    public decimal? EstimatedCostUsd { get; init; }
    public double? ContextUsedPercent { get; init; }

    public static TokenUsage Empty => new();

    public override string ToString() =>
        $"in={InputTokens} out={OutputTokens} total={TotalTokens}" +
        (EstimatedCostUsd.HasValue ? $" ~${EstimatedCostUsd.Value:F4}" : "") +
        (ContextUsedPercent.HasValue ? $" ctx={ContextUsedPercent.Value:F0}%" : "");
}
