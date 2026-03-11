using Ralph.Persistence.Workspace;
using Ralph.Core.Localization;

namespace Ralph.Cli.Commands;

public sealed class RulesCommand
{
    private readonly WorkspaceInitializer _workspaceInit;

    public RulesCommand(WorkspaceInitializer workspaceInit)
    {
        _workspaceInit = workspaceInit;
    }

    public int Execute(string workingDirectory, string subCommand, string? argument, bool force, IStringCatalog s)
    {
        var guardrailsPath = _workspaceInit.GetGuardrailsPath(workingDirectory);

        switch (subCommand.ToLowerInvariant())
        {
            case "list":
                return ListRules(guardrailsPath, s);

            case "add":
                if (string.IsNullOrWhiteSpace(argument))
                {
                    Console.Error.WriteLine(s.Get("rules.usage_add"));
                    return 1;
                }
                return AddRule(workingDirectory, guardrailsPath, argument.Trim(), s);

            case "clear":
                return ClearRules(guardrailsPath, force, s);

            default:
                Console.Error.WriteLine(s.Get("rules.usage"));
                return 1;
        }
    }

    private static int ListRules(string guardrailsPath, IStringCatalog s)
    {
        if (!File.Exists(guardrailsPath))
        {
            Console.WriteLine(s.Get("rules.empty"));
            return 0;
        }

        var content = File.ReadAllText(guardrailsPath).TrimEnd();
        if (string.IsNullOrWhiteSpace(content))
        {
            Console.WriteLine(s.Get("rules.empty"));
            return 0;
        }

        Console.WriteLine(content);
        return 0;
    }

    private int AddRule(string workingDirectory, string guardrailsPath, string rule, IStringCatalog s)
    {
        // Ensure .ralph/ exists, initialize if needed
        if (!_workspaceInit.IsInitialized(workingDirectory))
            _workspaceInit.Initialize(workingDirectory);

        var content = File.Exists(guardrailsPath)
            ? File.ReadAllText(guardrailsPath)
            : "# Guardrails\n\n";

        // Ensure file ends with newline before appending
        if (!content.EndsWith('\n'))
            content += '\n';

        content += $"- {rule}\n";
        File.WriteAllText(guardrailsPath, content);
        Console.WriteLine(s.Format("rules.added", rule));
        return 0;
    }

    private static int ClearRules(string guardrailsPath, bool force, IStringCatalog s)
    {
        if (!File.Exists(guardrailsPath))
        {
            Console.WriteLine(s.Get("rules.empty"));
            return 0;
        }

        if (!force)
        {
            Console.WriteLine(s.Get("rules.force_required"));
            return 1;
        }

        File.WriteAllText(guardrailsPath, "# Guardrails\n\nAdd constraints and rules here.\n");
        Console.WriteLine(s.Get("rules.cleared"));
        return 0;
    }
}
