using Ralph.Core.Context;

namespace Ralph.Cli.Commands;

public sealed class ContextCommand
{
    private readonly ContextPackBuilder _builder;

    public ContextCommand(ContextPackBuilder builder)
    {
        _builder = builder;
    }

    public int Execute(string workingDirectory, string subCommand)
    {
        if (subCommand.Equals("refresh", StringComparison.OrdinalIgnoreCase))
        {
            var path = _builder.RefreshRepoMap(workingDirectory);
            Console.WriteLine(path);
            return 0;
        }

        if (subCommand.Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(_builder.ReadRepoMapIfAvailable(workingDirectory, refreshIfMissing: false) ?? "No repo map found. Run: ralph context refresh");
            return 0;
        }

        Console.Error.WriteLine("Usage: ralph context <refresh|show>");
        return 1;
    }
}
