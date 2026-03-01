using Ralph.Engines.Abstractions;

namespace Ralph.Engines.Fake;

public sealed class FakeEngine : IEngine
{
    private readonly Func<EngineRequest, CancellationToken, Task<EngineResult>> _run;

    public FakeEngine(string name, Func<EngineRequest, CancellationToken, Task<EngineResult>>? run = null)
    {
        Name = name;
        _run = run ?? ((req, ct) => Task.FromResult(new EngineResult
        {
            ExitCode = 0,
            CompletionSignal = CompletionSignal.Complete,
            Duration = TimeSpan.Zero
        }));
    }

    public string Name { get; }

    public Task<EngineResult> RunAsync(EngineRequest request, CancellationToken cancellationToken = default) =>
        _run(request, cancellationToken);
}
