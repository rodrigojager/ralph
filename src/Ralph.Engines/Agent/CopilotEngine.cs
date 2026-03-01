using Ralph.Engines.Abstractions;

namespace Ralph.Engines.Agent;

public sealed class CopilotEngine : IEngine
{
    private readonly AgentEngine _inner = new("copilot", "copilot");
    public string Name => _inner.Name;
    public Task<EngineResult> RunAsync(EngineRequest request, CancellationToken cancellationToken = default) =>
        _inner.RunAsync(request, cancellationToken);
}
