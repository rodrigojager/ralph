namespace Ralph.Engines.Abstractions;

public interface IEngine
{
    string Name { get; }
    Task<EngineResult> RunAsync(EngineRequest request, CancellationToken cancellationToken = default);
}
