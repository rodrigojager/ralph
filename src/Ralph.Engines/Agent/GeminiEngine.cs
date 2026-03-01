using Ralph.Engines.Abstractions;

namespace Ralph.Engines.Agent;

public sealed class GeminiEngine : IEngine
{
    private readonly AgentEngine _inner = new("gemini", OperatingSystem.IsWindows() ? "gemini.cmd" : "gemini");
    public string Name => _inner.Name;
    public Task<EngineResult> RunAsync(EngineRequest request, CancellationToken cancellationToken = default) =>
        _inner.RunAsync(request, cancellationToken);
}
