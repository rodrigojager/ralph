using Ralph.Core.RunLoop;
using Ralph.Core.Localization;
using Ralph.Engines.Abstractions;
using Ralph.Tasks.Prd;

namespace Ralph.Cli.Commands;

public enum RunCommandMode
{
    Loop,
    Wiggum
}

public sealed class RunCommand
{
    private readonly RunLoopService _runLoop;
    private readonly IStringCatalog _s;

    public RunCommand(RunLoopService runLoop, IStringCatalog strings)
    {
        _runLoop = runLoop;
        _s = strings;
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
        bool ignoreContextStops = true,
        string? noChangePolicyOverride = null,
        int? noChangeMaxAttemptsOverride = null,
        bool? noChangeStopOnMaxAttemptsOverride = null,
        RunCommandMode mode = RunCommandMode.Loop,
        bool force = false,
        bool noCommit = false,
        bool fast = false,
        IReadOnlyList<PrdGate>? cliGates = null,
        string? securityModeOverride = null,
        EngineSandboxOptions? sandboxOverride = null,
        CancellationToken cancellationToken = default)
    {
        if (dryRun)
        {
            var dryResult = await _runLoop.DryRunAsync(workingDirectory, prdPath, engine, model, maxTokens, temperature, maxIterations);
            return dryResult.Completed ? 0 : 1;
        }

        if (mode is RunCommandMode.Loop)
            return await RunLoopModeAsync(
                workingDirectory,
                prdPath,
                skipTests,
                skipLint,
                engine,
                model,
                maxTokens,
                temperature,
                extraArgs,
                maxIterations,
                verbose,
                branchPerTask,
                baseBranch,
                createPr,
                draftPr,
                autoRollback,
                debugEngineJson,
                ignoreContextStops,
                noChangePolicyOverride,
                noChangeMaxAttemptsOverride,
                noChangeStopOnMaxAttemptsOverride,
                maxRetries,
                force,
                noCommit,
                fast,
                cliGates,
                securityModeOverride,
                sandboxOverride,
                cancellationToken);

        return await RunWiggumModeAsync(
            workingDirectory,
            prdPath,
            skipTests,
            skipLint,
            engine,
            model,
            maxTokens,
            temperature,
            extraArgs,
            maxIterations,
            maxRetries,
            verbose,
            branchPerTask,
            baseBranch,
            createPr,
            draftPr,
            autoRollback,
            debugEngineJson,
            ignoreContextStops,
            noChangePolicyOverride,
            noChangeMaxAttemptsOverride,
            noChangeStopOnMaxAttemptsOverride,
            noCommit,
            fast,
            cliGates,
            securityModeOverride,
            sandboxOverride,
            cancellationToken);
    }

    private async Task<int> RunLoopModeAsync(
        string workingDirectory,
        string prdPath,
        bool skipTests,
        bool skipLint,
        string? engine,
        string? model,
        int? maxTokens,
        double? temperature,
        IReadOnlyList<string>? extraArgs,
        int? maxIterations,
        bool verbose,
        bool branchPerTask,
        string? baseBranch,
        bool createPr,
        bool draftPr,
        bool autoRollback,
        bool debugEngineJson,
        bool ignoreContextStops,
        string? noChangePolicyOverride,
        int? noChangeMaxAttemptsOverride,
        bool? noChangeStopOnMaxAttemptsOverride,
        int? taskRetryLimit,
        bool force,
        bool noCommit,
        bool fast,
        IReadOnlyList<PrdGate>? cliGates,
        string? securityModeOverride,
        EngineSandboxOptions? sandboxOverride,
        CancellationToken cancellationToken)
    {
        var iterationsDone = 0;
        var taskAttempt = 0;
        while (!maxIterations.HasValue || iterationsDone < maxIterations.Value)
        {
            var before = ReadNextPending(prdPath);
            if (before.PendingIndex is null)
                return 0;

            taskAttempt++;
            var result = await _runLoop.RunAsync(
                workingDirectory,
                prdPath,
                1,
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
                debugEngineJson,
                ignoreContextStops,
                noChangePolicyOverride,
                noChangeMaxAttemptsOverride,
                noChangeStopOnMaxAttemptsOverride,
                noCommit,
                fast,
                PromptContextMode.LoopTaskScoped,
                cliGates,
                securityModeOverride,
                sandboxOverride);

            var after = ReadNextPending(prdPath);
            var progressed = HasProgress(before, after);

            if (progressed)
            {
                iterationsDone++;
                taskAttempt = 0;
                continue;
            }

            if (taskRetryLimit.HasValue && !force && taskAttempt < taskRetryLimit.Value)
            {
                Console.WriteLine(_s.Format("run.retrying", taskAttempt, taskRetryLimit.Value));
                continue;
            }

            if (!force)
                return result.Gutter ? 2 : 1;

            if (before.PendingIndex is null)
                return 0;

            if (taskRetryLimit.HasValue && taskAttempt >= taskRetryLimit.Value)
            {
                Console.Error.WriteLine(_s.Format("run.task_retry_limit_reached", before.PendingIndex.Value + 1, taskRetryLimit.Value));
                PrdWriter.MarkTaskSkippedForReview(prdPath, before.Document, before.PendingIndex.Value);
                iterationsDone++;
                taskAttempt = 0;
                continue;
            }

            PrdWriter.MarkTaskSkippedForReview(prdPath, before.Document, before.PendingIndex.Value);
            iterationsDone++;
            taskAttempt = 0;
            Console.Error.WriteLine(_s.Get("run.no_change_marked_for_review"));
        }

        var finalDoc = PrdParser.Parse(prdPath);
        return finalDoc.TaskEntries.Any(t => t.IsPending) ? 1 : 0;
    }

    private async Task<int> RunWiggumModeAsync(
        string workingDirectory,
        string prdPath,
        bool skipTests,
        bool skipLint,
        string? engine,
        string? model,
        int? maxTokens,
        double? temperature,
        IReadOnlyList<string>? extraArgs,
        int? maxIterations,
        int? taskAttemptLimit,
        bool verbose,
        bool branchPerTask,
        string? baseBranch,
        bool createPr,
        bool draftPr,
        bool autoRollback,
        bool debugEngineJson,
        bool ignoreContextStops,
        string? noChangePolicyOverride,
        int? noChangeMaxAttemptsOverride,
        bool? noChangeStopOnMaxAttemptsOverride,
        bool noCommit,
        bool fast,
        IReadOnlyList<PrdGate>? cliGates,
        string? securityModeOverride,
        EngineSandboxOptions? sandboxOverride,
        CancellationToken cancellationToken)
    {
        var completedInLoop = 0;
        while (!maxIterations.HasValue || completedInLoop < maxIterations.Value)
        {
            var before = ReadNextPending(prdPath);
            if (before.PendingIndex is null)
                return 0;

            var attempts = 0;
            while (taskAttemptLimit is null || attempts < taskAttemptLimit.Value)
            {
                attempts++;
                await _runLoop.RunAsync(
                    workingDirectory,
                    prdPath,
                    1,
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
                    debugEngineJson,
                    ignoreContextStops,
                    noChangePolicyOverride,
                    noChangeMaxAttemptsOverride,
                    noChangeStopOnMaxAttemptsOverride,
                    noCommit,
                    fast,
                    PromptContextMode.WiggumFullPrd,
                    cliGates,
                    securityModeOverride,
                    sandboxOverride);

                var after = ReadNextPending(prdPath);
                if (HasProgress(before, after))
                {
                    completedInLoop++;
                    break;
                }

                if (taskAttemptLimit.HasValue && attempts >= taskAttemptLimit.Value)
                {
                    PrdWriter.MarkTaskSkippedForReview(prdPath, before.Document, before.PendingIndex.Value);
                    Console.Error.WriteLine(_s.Format("run.task_retry_limit_reached", before.PendingIndex.Value + 1, taskAttemptLimit.Value));
                    completedInLoop++;
                    break;
                }

                await Task.Yield();
            }

            if (!taskAttemptLimit.HasValue)
            {
                continue;
            }
        }

        var finalDoc = PrdParser.Parse(prdPath);
        return finalDoc.TaskEntries.Any(t => t.IsPending) ? 1 : 0;
    }

    private static RunState ReadNextPending(string prdPath)
    {
        var doc = PrdParser.Parse(prdPath);
        return new RunState(doc, doc.GetNextPendingTaskIndex());
    }

    private static bool HasProgress(RunState before, RunState after)
    {
        if (before.PendingIndex is null && after.PendingIndex is null)
            return true;
        if (before.PendingIndex.HasValue != after.PendingIndex.HasValue)
            return true;
        if (before.PendingIndex.HasValue && after.PendingIndex.HasValue)
            return before.PendingIndex.Value != after.PendingIndex.Value;

        return false;
    }

    private readonly record struct RunState(
        PrdDocument Document,
        int? PendingIndex);

}
