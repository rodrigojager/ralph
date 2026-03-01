using Ralph.Engines.Abstractions;

namespace Ralph.Engines.Agent;

public sealed class DroidEngine : IEngine
{
    private readonly AgentEngine _inner = new("droid", "droid");
    public string Name => _inner.Name;
    public Task<EngineResult> RunAsync(EngineRequest request, CancellationToken cancellationToken = default) =>
        _inner.RunAsync(request, cancellationToken);
}
