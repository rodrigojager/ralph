using Ralph.Core.Localization;

namespace Ralph.Core.RunLoop.OutputAdapters;

internal interface IEngineOutputDisplayAdapter
{
    bool CanHandle(string engineName);
    string Adapt(string rawStdout, IStringCatalog strings);
}
