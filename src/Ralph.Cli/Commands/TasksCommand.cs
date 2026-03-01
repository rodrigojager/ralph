using Ralph.Tasks.Prd;
using Ralph.Core.Localization;

namespace Ralph.Cli.Commands;

public sealed class TasksCommand
{
    public int Execute(string prdPath, string subCommand, string? argument, IStringCatalog s)
    {
        if (!File.Exists(prdPath))
        {
            Console.Error.WriteLine(s.Format("error.prd_not_found", prdPath));
            return 1;
        }
        var doc = PrdParser.Parse(prdPath);
        switch (subCommand.ToLowerInvariant())
        {
            case "list":
                for (var i = 0; i < doc.TaskEntries.Count; i++)
                {
                    var e = doc.TaskEntries[i];
                    var mark = e.IsCompleted ? "[x]" : "[ ]";
                    Console.WriteLine($"  {i + 1}. {mark} {e.DisplayText}");
                }
                return 0;
            case "next":
                var next = doc.GetNextPendingTask();
                if (next == null) Console.WriteLine(s.Get("tasks.none"));
                else Console.WriteLine(next.DisplayText);
                return 0;
            case "done":
                if (string.IsNullOrWhiteSpace(argument))
                {
                    var done = doc.TaskEntries.Count(e => e.IsCompleted);
                    Console.WriteLine(s.Format("tasks.done_summary", done, doc.TaskEntries.Count));
                    return 0;
                }
                return MarkTaskAsDone(prdPath, doc, argument, s);
            default:
                Console.Error.WriteLine(s.Get("tasks.usage"));
                return 1;
        }
    }

    private static int MarkTaskAsDone(string prdPath, PrdDocument doc, string argument, IStringCatalog s)
    {
        int? index = null;
        if (argument.Equals("next", StringComparison.OrdinalIgnoreCase))
            index = doc.GetNextPendingTaskIndex();
        else if (int.TryParse(argument, out var oneBased))
            index = oneBased - 1;

        if (!index.HasValue || index.Value < 0 || index.Value >= doc.TaskEntries.Count)
        {
            Console.Error.WriteLine(s.Get("tasks.usage_done"));
            return 1;
        }

        PrdWriter.MarkTaskCompleted(prdPath, doc, index.Value);
        Console.WriteLine(s.Format("tasks.done_ok", index.Value + 1));
        return 0;
    }
}
