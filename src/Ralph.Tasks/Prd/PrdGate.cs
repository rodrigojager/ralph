namespace Ralph.Tasks.Prd;

public sealed class PrdGate
{
    public string Name { get; init; } = "gate";
    public string Command { get; init; } = string.Empty;
    public bool Required { get; init; } = true;
    public string Policy { get; init; } = "block";
}
