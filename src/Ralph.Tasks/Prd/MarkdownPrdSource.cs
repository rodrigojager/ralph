namespace Ralph.Tasks.Prd;

public sealed class MarkdownPrdSource : IPrdSource
{
    public string Name => "markdown";

    public PrdDocument Load(string pathOrIdentifier)
    {
        return PrdParser.Parse(pathOrIdentifier);
    }

    public void MarkCompleted(string pathOrIdentifier, PrdDocument document, int taskIndex)
    {
        PrdWriter.MarkTaskCompleted(pathOrIdentifier, document, taskIndex);
    }
}
