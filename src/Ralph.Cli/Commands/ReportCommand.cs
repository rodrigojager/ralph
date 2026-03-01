using Ralph.Core.Localization;
using Ralph.Persistence.Workspace;

namespace Ralph.Cli.Commands;

public sealed class ReportCommand
{
    private readonly WorkspaceInitializer _workspace;

    public ReportCommand(WorkspaceInitializer workspace)
    {
        _workspace = workspace;
    }

    public int Execute(string workingDirectory, string subCommand, IStringCatalog s)
    {
        if (!subCommand.Equals("last", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(s.Get("report.usage"));
            return 1;
        }

        var latestPath = _workspace.GetLatestReportMarkdownPath(workingDirectory);
        if (!File.Exists(latestPath))
        {
            Console.WriteLine(s.Get("report.none"));
            return 0;
        }

        Console.WriteLine(File.ReadAllText(latestPath));
        return 0;
    }
}
