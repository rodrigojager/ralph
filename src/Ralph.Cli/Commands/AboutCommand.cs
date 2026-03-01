using Ralph.Core.Localization;

namespace Ralph.Cli.Commands;

public sealed class AboutCommand
{
    public int Execute(string version, IStringCatalog s)
    {
        Console.WriteLine();
        Console.WriteLine(s.Get("about.title"));
        Console.WriteLine(new string('-', 40));
        Console.WriteLine(s.Format("about.version", version));
        Console.WriteLine(s.Get("about.author"));
        Console.WriteLine(s.Get("about.github"));
        Console.WriteLine(s.Get("about.linkedin"));
        Console.WriteLine(s.Get("about.repo"));
        Console.WriteLine(s.Get("about.license"));
        Console.WriteLine(s.Get("about.ralph_loop"));
        Console.WriteLine(s.Get("about.inspiration"));
        Console.WriteLine();
        return 0;
    }
}
