using Ralph.Engines.Abstractions;

namespace Ralph.Engines.Registry;

public sealed class EngineRegistry
{
    private readonly Dictionary<string, IEngine> _engines = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IEngine engine)
    {
        _engines[engine.Name] = engine;
    }

    public IEngine? Get(string name)
    {
        return _engines.TryGetValue(name ?? "", out var e) ? e : null;
    }

    public IEngine GetOrThrow(string name)
    {
        var e = Get(name);
        if (e == null)
            throw new InvalidOperationException($"Engine '{name}' is not registered.");
        return e;
    }

    public IReadOnlyList<string> GetRegisteredNames()
    {
        return _engines.Keys.ToList();
    }
}
