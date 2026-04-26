using Ralph.Core.Adapters;

namespace Ralph.Cli.Commands;

public sealed class AdaptersCommand
{
    private readonly EngineAdapterCatalog _catalog;

    public AdaptersCommand(EngineAdapterCatalog catalog)
    {
        _catalog = catalog;
    }

    public int Execute(string workingDirectory, string subCommand, IReadOnlyList<string> args, bool force)
    {
        if (subCommand.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var adapter in _catalog.LoadAll(workingDirectory))
                Console.WriteLine($"{adapter.Name}  {adapter.Command ?? adapter.Name}");
            return 0;
        }

        if (subCommand.Equals("new", StringComparison.OrdinalIgnoreCase) && args.Count > 0)
        {
            Console.WriteLine(_catalog.CreateTemplate(workingDirectory, args[0], force));
            return 0;
        }

        Console.Error.WriteLine("Usage: ralph adapters <list|new> [name] [--force]");
        return 1;
    }
}
