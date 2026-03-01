using Ralph.Persistence.Workspace;
using Ralph.Core.Localization;

namespace Ralph.Cli.Commands;

public sealed class InitCommand
{
    private readonly WorkspaceInitializer _initializer;

    public InitCommand(WorkspaceInitializer initializer)
    {
        _initializer = initializer;
    }

    public int Execute(string workingDirectory, bool force, IStringCatalog s)
    {
        var wasInitialized = _initializer.IsInitialized(workingDirectory);
        _initializer.Initialize(workingDirectory, force);
        var ralphDir = _initializer.GetRalphDir(workingDirectory);
        if (wasInitialized && !force)
            Console.WriteLine(s.Format("init.already", ralphDir));
        else
            Console.WriteLine(s.Format("init.done", ralphDir));
        return 0;
    }
}
