using Ralph.Engines.Abstractions;

namespace Ralph.Engines.Agent;

public sealed class OpenCodeEngine : IEngine
{
    private readonly AgentEngine _inner = new("opencode", "opencode");
    public string Name => _inner.Name;
    public Task<EngineResult> RunAsync(EngineRequest request, CancellationToken cancellationToken = default) =>
        _inner.RunAsync(request, cancellationToken);
}
