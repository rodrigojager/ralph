using Ralph.Core.Localization;

namespace Ralph.Core.RunLoop.OutputAdapters;

internal sealed class EngineOutputDisplayAdapterRegistry
{
    private readonly IReadOnlyList<IEngineOutputDisplayAdapter> _adapters;
    private readonly IStringCatalog _strings;

    public EngineOutputDisplayAdapterRegistry(IStringCatalog strings)
    {
        _strings = strings;
        _adapters = new IEngineOutputDisplayAdapter[]
        {
            new CursorStreamJsonOutputAdapter(),
            new CodexStreamJsonOutputAdapter(),
            new GeminiStreamJsonOutputAdapter()
        };
    }

    public string Adapt(string engineName, string rawStdout)
    {
        if (string.IsNullOrWhiteSpace(rawStdout))
            return rawStdout;

        foreach (var adapter in _adapters)
        {
            if (!adapter.CanHandle(engineName))
                continue;
            return adapter.Adapt(rawStdout, _strings);
        }

        return rawStdout;
    }
}
