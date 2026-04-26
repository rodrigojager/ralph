using System.Text.Json;
using Ralph.Core.Checkpoints;

namespace Ralph.Cli.Commands;

public sealed class CheckpointCommand
{
    private readonly CheckpointService _service;

    public CheckpointCommand(CheckpointService service)
    {
        _service = service;
    }

    public int Execute(string workingDirectory, string subCommand, IReadOnlyList<string> args, bool force, bool json)
    {
        if (subCommand.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            var label = args.Count > 0 ? string.Join(" ", args) : null;
            var checkpoint = _service.Create(workingDirectory, label);
            Write(checkpoint, json);
            return 0;
        }

        if (subCommand.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            var checkpoints = _service.List(workingDirectory);
            if (json)
            {
                Console.WriteLine(JsonSerializer.Serialize(checkpoints, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            foreach (var cp in checkpoints)
                Console.WriteLine($"{cp.Id}  {cp.CreatedAt:O}  {cp.GitBranch ?? "-"}  {cp.Label ?? ""}".TrimEnd());
            return 0;
        }

        if (subCommand.Equals("show", StringComparison.OrdinalIgnoreCase) && args.Count > 0)
        {
            var checkpoint = _service.Get(workingDirectory, args[0]);
            if (checkpoint == null)
            {
                Console.Error.WriteLine("Checkpoint not found.");
                return 1;
            }
            Write(checkpoint, json: true);
            return 0;
        }

        if (subCommand.Equals("restore", StringComparison.OrdinalIgnoreCase) && args.Count > 0)
        {
            var result = _service.Restore(workingDirectory, args[0], force);
            Console.WriteLine(result.Message);
            return result.Success ? 0 : 1;
        }

        Console.Error.WriteLine("Usage: ralph checkpoint <create|list|show|restore> [id|label] [--force] [--json]");
        return 1;
    }

    private static void Write(CheckpointMetadata checkpoint, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        Console.WriteLine($"Checkpoint: {checkpoint.Id}");
    }
}
