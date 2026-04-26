using Ralph.Persistence.Workspace;

namespace Ralph.Cli.Commands;

public sealed class RecipesCommand
{
    private readonly WorkspaceInitializer _workspace;

    public RecipesCommand(WorkspaceInitializer workspace)
    {
        _workspace = workspace;
    }

    public int Execute(string workingDirectory, string subCommand, IReadOnlyList<string> args, bool force)
    {
        var dir = _workspace.GetRecipesDir(workingDirectory);
        Directory.CreateDirectory(dir);

        if (subCommand.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.md").OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                Console.WriteLine(Path.GetFileNameWithoutExtension(file));
            return 0;
        }

        if (subCommand.Equals("new", StringComparison.OrdinalIgnoreCase) && args.Count > 0)
        {
            var path = Path.Combine(dir, Sanitize(args[0]) + ".md");
            if (!File.Exists(path) || force)
            {
                File.WriteAllText(path,
                    $"# {args[0]}\n\n" +
                    "Describe the repeatable Ralph workflow, default engine, optional gates, and expected PRD conventions here.\n");
            }
            Console.WriteLine(path);
            return 0;
        }

        Console.Error.WriteLine("Usage: ralph recipes <list|new> [name] [--force]");
        return 1;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
    }
}
