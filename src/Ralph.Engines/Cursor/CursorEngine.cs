using Ralph.Engines.Abstractions;
using Ralph.Engines.Agent;

namespace Ralph.Engines.Cursor;

public sealed class CursorEngine : IEngine
{
    private readonly AgentEngine _inner = new("cursor", "cursor-agent");

    public string Name => _inner.Name;

    public Task<EngineResult> RunAsync(EngineRequest request, CancellationToken cancellationToken = default) =>
        _inner.RunAsync(request, cancellationToken);
}
