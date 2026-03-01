using Ralph.Core.RunLoop;

namespace Ralph.Cli.Commands;

public sealed class RunCommand
{
    private readonly RunLoopService _runLoop;

    public RunCommand(RunLoopService runLoop)
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
        int? maxRetries = null,
        int? retryDelaySeconds = null,
        int? maxIterations = null,
        bool dryRun = false,
        bool verbose = false,
        bool branchPerTask = false,
        string? baseBranch = null,
        bool createPr = false,
        bool draftPr = false,
        bool autoRollback = false,
        bool debugEngineJson = false,
        CancellationToken cancellationToken = default)
    {
        if (dryRun)
        {
            var dryResult = await _runLoop.DryRunAsync(workingDirectory, prdPath, engine, model, maxTokens, temperature, maxIterations);
            return dryResult.Completed ? 0 : 1;
        }

        var result = await _runLoop.RunAsync(
            workingDirectory,
            prdPath,
            maxIterations,
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
