namespace Ralph.Tasks.Prd;

/// <summary>
/// Abstraction for PRD task source. Implementations: Markdown (default), YAML, GitHub Issues (future).
/// </summary>
public interface IPrdSource
{
    string Name { get; }
    PrdDocument Load(string pathOrIdentifier);
    void MarkCompleted(string pathOrIdentifier, PrdDocument document, int taskIndex);
}
