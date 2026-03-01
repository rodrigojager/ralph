using Ralph.Core.RunLoop;

namespace Ralph.Cli.Commands;

public sealed class OnceCommand
{
    private readonly RunLoopService _runLoop;

    public OnceCommand(RunLoopService runLoop)
    {
        _runLoop = runLoop;
    }

    public async Task<int> ExecuteAsync(
        string workingDirectory,
        string prdPath,
        bool skipTests = false,
        bool skipLint = false,
        string? engine = null,
        string? model = null,
        int? maxTokens = null,
        double? temperature = null,
        IReadOnlyList<string>? extraArgs = null,
        bool verbose = false,
        string? taskOverride = null,
        bool branchPerTask = false,
        string? baseBranch = null,
        bool createPr = false,
        bool draftPr = false,
        bool autoRollback = false,
        bool debugEngineJson = false,
        CancellationToken cancellationToken = default)
    {
        if (taskOverride != null)
        {
            var brownfieldResult = await _runLoop.RunSingleTaskAsync(
                workingDirectory,
                taskOverride,
                engine,
                model,
                maxTokens,
                temperature,
                extraArgs,
                cancellationToken,
                verbose,
                debugEngineJson);
            return brownfieldResult.Completed ? 0 : 1;
        }

        var result = await _runLoop.OnceAsync(
            workingDirectory,
            prdPath,
            skipTests,
            skipLint,
            engine,
            model,
            maxTokens,
            temperature,
            extraArgs,
            cancellationToken,
            verbose,
            branchPerTask,
            baseBranch,
            createPr,
            draftPr,
            autoRollback,
            debugEngineJson);
        return result.Completed ? 0 : (result.Gutter ? 2 : 1);
    }
}
