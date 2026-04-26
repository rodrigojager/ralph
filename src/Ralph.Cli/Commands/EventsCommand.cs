using Ralph.Persistence.Workspace;

namespace Ralph.Cli.Commands;

public sealed class EventsCommand
{
    private readonly WorkspaceInitializer _workspace;

    public EventsCommand(WorkspaceInitializer workspace)
    {
        _workspace = workspace;
    }

    public async Task<int> ExecuteAsync(string workingDirectory, string subCommand, bool follow, CancellationToken cancellationToken = default)
    {
        var path = _workspace.GetEventsLogPath(workingDirectory);
        if (!File.Exists(path))
        {
            Console.WriteLine("No events found.");
            return 0;
        }

        if (!follow)
        {
            foreach (var line in File.ReadLines(path).TakeLast(200))
                Console.WriteLine(line);
            return 0;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(0, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null)
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }
            Console.WriteLine(line);
        }

        return 0;
    }
}
