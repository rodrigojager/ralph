using Ralph.Persistence.Workspace;
using Ralph.Core.Localization;

namespace Ralph.Cli.Commands;

public sealed class CleanCommand
{
    private readonly WorkspaceInitializer _workspaceInit;

    public CleanCommand(WorkspaceInitializer workspaceInit)
    {
        _workspaceInit = workspaceInit;
    }

    public int Execute(string workingDirectory, bool force, IStringCatalog s)
    {
        var ralphDir = _workspaceInit.GetRalphDir(workingDirectory);
        if (!Directory.Exists(ralphDir))
        {
            Console.WriteLine(s.Get("clean.nothing"));
            return 0;
        }
        if (!force)
        {
            Console.WriteLine(s.Get("clean.force_required"));
            return 1;
        }
        try
        {
            Directory.Delete(ralphDir, recursive: true);
            Console.WriteLine(s.Format("clean.done", ralphDir));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(s.Format("clean.fail", ex.Message));
            return 1;
        }
    }
}
