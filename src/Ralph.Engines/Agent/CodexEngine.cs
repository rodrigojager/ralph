using Ralph.Engines.Abstractions;

namespace Ralph.Engines.Agent;

public sealed class CodexEngine : IEngine
{
    private readonly AgentEngine _inner = new("codex", OperatingSystem.IsWindows() ? "codex.cmd" : "codex");
    public string Name => _inner.Name;
    public Task<EngineResult> RunAsync(EngineRequest request, CancellationToken cancellationToken = default) =>
        _inner.RunAsync(request, cancellationToken);
}
