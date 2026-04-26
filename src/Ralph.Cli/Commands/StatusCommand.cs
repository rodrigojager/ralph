using System.Text.Json;
using Ralph.Persistence.State;
using Ralph.Persistence.Workspace;
using Ralph.Tasks.Prd;

namespace Ralph.Cli.Commands;

public sealed class StatusCommand
{
    private readonly WorkspaceInitializer _workspace;
    private readonly StateStore _stateStore;

    public StatusCommand(WorkspaceInitializer workspace, StateStore stateStore)
    {
        _workspace = workspace;
        _stateStore = stateStore;
    }

    public int Execute(string workingDirectory, string prdPath, bool json)
    {
        var state = _stateStore.Load(_workspace.GetStatePath(workingDirectory));
        var taskTotal = 0;
        var taskDone = 0;
        var taskReview = 0;
        string? nextTask = null;

        if (File.Exists(prdPath))
        {
            var doc = PrdParser.Parse(prdPath);
            taskTotal = doc.TaskEntries.Count;
            taskDone = doc.TaskEntries.Count(t => t.IsCompleted);
            taskReview = doc.TaskEntries.Count(t => t.IsSkippedForReview);
            var next = doc.GetNextPendingTaskIndex();
            if (next.HasValue)
                nextTask = doc.TaskEntries[next.Value].DisplayText;
        }

        var payload = new
        {
            initialized = _workspace.IsInitialized(workingDirectory),
            run_status = state.RunStatus,
            current_run_id = state.CurrentRunId,
            current_engine = state.CurrentEngine,
            current_task_index = state.CurrentTaskIndex,
            current_task_text = state.CurrentTaskText,
            last_task_index = state.LastTaskIndex,
            last_task_text = state.LastTaskText,
            last_exit_reason = state.LastExitReason,
            last_exit_at = state.LastExitAt,
            tasks = new
            {
                total = taskTotal,
                completed = taskDone,
                skipped_for_review = taskReview,
                pending = Math.Max(0, taskTotal - taskDone - taskReview),
                next = nextTask
            }
        };

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Console.WriteLine($"Workspace: {(payload.initialized ? "initialized" : "not initialized")}");
        Console.WriteLine($"Run:       {payload.run_status ?? "idle"}");
        Console.WriteLine($"Engine:    {payload.current_engine ?? "-"}");
        Console.WriteLine($"Task:      {payload.current_task_index?.ToString() ?? "-"} {payload.current_task_text ?? ""}".TrimEnd());
        Console.WriteLine($"Progress:  {taskDone}/{taskTotal} completed, {taskReview} review");
        if (!string.IsNullOrWhiteSpace(nextTask))
            Console.WriteLine($"Next:      {nextTask}");
        return 0;
    }
}
