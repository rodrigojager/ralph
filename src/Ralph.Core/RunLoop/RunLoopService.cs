using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ralph.Core.Git;
using Ralph.Core.Localization;
using Ralph.Core.Prompting;
using Ralph.Core.Reports;
using Ralph.Core.RunLoop.OutputAdapters;
using Ralph.Engines.Abstractions;
using Ralph.Engines.Registry;
using Ralph.Engines.Tokens;
using Ralph.Persistence.Config;
using Ralph.Persistence.State;
using Ralph.Persistence.Workspace;
using Ralph.Tasks.Prd;
using Ralph.UI.Abstractions;

namespace Ralph.Core.RunLoop;

public sealed class RunLoopService
{
    private readonly EngineRegistry _engineRegistry;
    private readonly StateStore _stateStore;
    private readonly WorkspaceInitializer _workspaceInit;
    private readonly IUserInteraction _ui;
    private readonly ITerminalView? _terminalView;
    private readonly RetryPolicy _retryPolicy;
    private readonly GutterDetector _gutterDetector;
    private readonly GitService _git;
    private readonly IStringCatalog _s;
    private readonly RunReportWriter _reportWriter;
    private readonly EngineCommandResolver _commandResolver;
    private readonly EngineOutputDisplayAdapterRegistry _outputAdapterRegistry;
    private readonly Dictionary<string, bool?> _codexModelAvailability = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Called after each engine run with (sessionInputTokens, sessionOutputTokens, latestTokenUsage).
    /// Used by TUI to update the Tokens panel.
    /// </summary>
    public Action<int, int, TokenUsage?>? OnTokensUpdated { get; set; }

    public RunLoopService(
        EngineRegistry engineRegistry,
        StateStore stateStore,
        WorkspaceInitializer workspaceInit,
        IUserInteraction ui,
        ITerminalView? terminalView = null,
        RetryPolicy? retryPolicy = null,
        GutterDetector? gutterDetector = null,
        GitService? git = null,
        IStringCatalog? strings = null,
        EngineCommandResolver? commandResolver = null)
    {
        _engineRegistry = engineRegistry;
        _stateStore = stateStore;
        _workspaceInit = workspaceInit;
        _ui = ui;
        _terminalView = terminalView;
        _retryPolicy = retryPolicy ?? new RetryPolicy();
        _gutterDetector = gutterDetector ?? new GutterDetector();
        _git = git ?? new GitService();
        _s = strings ?? StringCatalog.Default();
        _reportWriter = new RunReportWriter(_workspaceInit);
        _commandResolver = commandResolver ?? new EngineCommandResolver();
        _outputAdapterRegistry = new EngineOutputDisplayAdapterRegistry(_s);
    }

    public async Task<RunLoopResult> RunAsync(
        string workingDirectory,
        string prdPath,
        int? maxIterations = null,
        bool skipTests = false,
        bool skipLint = false,
        string? engineName = null,
        string? modelOverride = null,
        int? maxTokensOverride = null,
        double? temperatureOverride = null,
        IReadOnlyList<string>? extraArgsPassthrough = null,
        CancellationToken cancellationToken = default,
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
        bool noCommit = false,
        bool fast = false,
        PromptContextMode promptContextMode = PromptContextMode.LoopTaskScoped)
    {
        if (!_workspaceInit.IsInitialized(workingDirectory))
            _workspaceInit.Initialize(workingDirectory);
        var runStartedAt = DateTimeOffset.UtcNow;
        var reportEntries = new List<RunReportTaskEntry>();

        // Resolve base branch once before the loop
        if (branchPerTask && !noCommit && _git.IsGitRepo(workingDirectory))
        {
            baseBranch ??= _git.GetCurrentBranch(workingDirectory);
            if (verbose) _ui.WriteVerbose($"[verbose] Branch mode:  base={baseBranch}, createPr={createPr}, draft={draftPr}");
        }
        else if (branchPerTask && !noCommit)
        {
            _ui.WriteWarn(_s.Get("run.branch_mode_disabled"));
            branchPerTask = false;
        }

        if (verbose)
        {
            _ui.WriteVerbose($"[verbose] PRD:          {prdPath}");
            _ui.WriteVerbose($"[verbose] Working dir:  {workingDirectory}");
            _ui.WriteVerbose($"[verbose] Skip tests:   {skipTests}");
            _ui.WriteVerbose($"[verbose] Skip lint:    {skipLint}");
            if (maxIterations.HasValue) _ui.WriteVerbose($"[verbose] Max iters:    {maxIterations.Value}");
        }

        var statePath = _workspaceInit.GetStatePath(workingDirectory);
        var heartbeatPath = _workspaceInit.GetHeartbeatPath(workingDirectory);
        var guardrailsPath = _workspaceInit.GetGuardrailsPath(workingDirectory);
        var progressPath = _workspaceInit.GetProgressPath(workingDirectory);
        var activityPath = _workspaceInit.GetActivityLogPath(workingDirectory);
        var errorsPath = _workspaceInit.GetErrorsLogPath(workingDirectory);
        var executionPath = _workspaceInit.GetExecutionLogPath(workingDirectory);
        DetectUnexpectedShutdown(statePath, errorsPath, executionPath);
        MarkRunState(statePath, heartbeatPath, runStartedAt, "running", "begin");

        AppendExecutionEvent(
            executionPath,
            "run_started",
            "begin",
            details: $"prd={SanitizeLogValue(Path.GetRelativePath(workingDirectory, prdPath))}");

        var iteration = 0;
        var anyTaskCompletedInRun = false;
        var sessionInputTokens = 0;
        var sessionOutputTokens = 0;
        while (maxIterations == null || iteration < maxIterations.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var doc = PrdParser.Parse(prdPath);
            var totalTasks = doc.TaskEntries.Count;
            var completedTasks = doc.TaskEntries.Count(t => t.IsResolved);
            var nextIndex = doc.GetNextPendingTaskIndex();
            if (nextIndex == null)
            {
                if (doc.TaskEntries.Any(t => t.IsSkippedForReview))
                    _ui.WriteWarn(_s.Get("run.manual_review_remaining"));
                _ui.WriteInfo(_s.Get("run.all_done"));
                _ui.WriteInfo(_s.Format("run.summary_complete", totalTasks, totalTasks, iteration));
                _terminalView?.SetStatus(_s.Get("run.status_all_tasks_completed"));
                _terminalView?.SetProgress(totalTasks, totalTasks);
                WriteSessionTokensSummary(sessionInputTokens, sessionOutputTokens);
                AppendExecutionEvent(
                    executionPath,
                    "run_finished",
                    "all_tasks_completed",
                    details: $"iterations={iteration}");
                MarkRunStopped(statePath, heartbeatPath, "all_tasks_completed");
                var done = new RunLoopResult { Completed = true };
                WriteRunReport(workingDirectory, runStartedAt, done.Completed, reportEntries);
                return done;
            }

            var taskEntry = doc.TaskEntries[nextIndex.Value];
            var taskSnapshot = CaptureTaskSnapshot(workingDirectory, autoRollback);
            var taskStartedAt = DateTimeOffset.UtcNow;
            MarkTaskStarted(statePath, heartbeatPath, taskEntry.DisplayText, nextIndex.Value, totalTasks, taskStartedAt);
            AppendExecutionEvent(
                executionPath,
                "task_started",
                "iteration",
                task: taskEntry.DisplayText,
                taskIndex: nextIndex.Value + 1,
                totalTasks: totalTasks,
                details: $"iteration={iteration + 1}");

            // Branch per task: create a dedicated branch before running the engine
            string? taskBranch = null;
            if (branchPerTask && baseBranch != null)
            {
                taskBranch = GitService.SlugifyBranch(taskEntry.DisplayText);
                _terminalView?.SetStatus(_s.Format("run.status_creating_branch", taskBranch));
                var branched = _git.CreateAndCheckoutBranch(workingDirectory, taskBranch, baseBranch);
                if (branched)
                    _ui.WriteInfo(_s.Format("run.branch_created", taskBranch));
                else
                {
                    _ui.WriteWarn(_s.Format("run.branch_create_failed", taskBranch));
                    taskBranch = null;
                }
                if (verbose) _ui.WriteVerbose($"[verbose] Task branch:  {taskBranch ?? "(failed, continuing)"}");
            }

            var configPath = _workspaceInit.GetConfigPath(workingDirectory);
            var config = File.Exists(configPath) ? new ConfigStore().Load(configPath) : RalphConfig.Default;
            var rotationConfig = config.ContextRotation ?? new ContextRotationConfigEntry { OnSignal = "warn" };
            var noChangePolicy = ParseNoChangePolicy(noChangePolicyOverride ?? config.Run?.NoChangePolicy);
            var noChangeMaxAttempts = Math.Max(1, noChangeMaxAttemptsOverride ?? config.Run?.NoChangeMaxAttempts ?? 3);
            var noChangeStopOnMaxAttempts = noChangeStopOnMaxAttemptsOverride ?? config.Run?.NoChangeStopOnMaxAttempts ?? true;
            var engineNameToUse = engineName ?? doc.Frontmatter?.Engine ?? "cursor";
            var engineCandidates = BuildEngineCandidates(engineNameToUse, config);
            var activeEngineName = engineCandidates[0];
            var activeEngine = _engineRegistry.Get(activeEngineName);
            if (activeEngine == null)
            {
                _ui.WriteError(_s.Format("run.engine_not_found", activeEngineName, string.Join(", ", _engineRegistry.GetRegisteredNames())));
                _ui.WriteInfo(_s.Format("run.summary_partial", completedTasks, totalTasks));
                _terminalView?.SetStatus(_s.Format("run.status_engine_not_found", activeEngineName));
                AppendExecutionEvent(
                    executionPath,
                    "run_stopped",
                    "engine_not_found",
                    task: taskEntry.DisplayText,
                    engine: activeEngineName,
                    taskIndex: nextIndex.Value + 1,
                    totalTasks: totalTasks);
                MarkRunStopped(statePath, heartbeatPath, "engine_not_found");
                return new RunLoopResult { Completed = false, Gutter = true };
            }

            _terminalView?.SetStatus(_s.Format("run.status_iteration_engine", iteration + 1, activeEngineName));
            _terminalView?.SetProgress(completedTasks + 1, totalTasks, taskEntry.DisplayText);

            var guardrails = File.Exists(guardrailsPath) ? File.ReadAllText(guardrailsPath) : null;
            var cursorMode = activeEngineName.Equals("cursor", StringComparison.OrdinalIgnoreCase);
            var context = BuildPrdExecutionContext(doc, guardrails, taskEntry.DisplayText, promptContextMode);
            if (cursorMode)
                context += "\n\n## Execution mode\nExecute the task now. Make the file changes directly and do not ask confirmation questions.";

            var engineConfig = config.Engines?.TryGetValue(activeEngineName, out var ec) == true ? ec : null;
            var modelToUse = modelOverride ?? doc.Frontmatter?.Model ?? engineConfig?.DefaultModel;
            var maxTokensToUse = maxTokensOverride ?? engineConfig?.MaxTokens;
            var temperatureToUse = temperatureOverride ?? engineConfig?.Temperature;
            var resolvedCommand = _commandResolver.ResolveForExecution(activeEngineName, config);
            var browserCommand = doc.Frontmatter?.BrowserCommand;
            if (string.IsNullOrWhiteSpace(browserCommand) && config.Browser?.Enabled == true)
                browserCommand = config.Browser.Command;
            var fastModeForEngine = ResolveFastMode(activeEngineName, modelToUse, fast);
            if (fast && !fastModeForEngine)
                _ui.WriteWarn(_s.Format("run.fast_not_supported", activeEngineName, modelToUse ?? "(default)"));
            var modelAvailability = await CheckCodexModelAvailabilityAsync(
                workingDirectory,
                activeEngineName,
                modelToUse,
                resolvedCommand,
                cancellationToken);
            if (modelAvailability == false)
            {
                _ui.WriteError(_s.Format("run.model_unavailable", activeEngineName, modelToUse));
                AppendExecutionEvent(
                    executionPath,
                    "run_stopped",
                    "model_unavailable",
                    task: taskEntry.DisplayText,
                    engine: activeEngineName,
                    taskIndex: nextIndex.Value + 1,
                    totalTasks: totalTasks,
                    details: $"model={modelToUse}");
                MarkRunStopped(statePath, heartbeatPath, "model_unavailable");
                return new RunLoopResult { Completed = false };
            }

            var normalizedExtraArgs = BuildExtraArgs(activeEngineName, maxTokensToUse, temperatureToUse, extraArgsPassthrough);

            var request = new EngineRequest
            {
                WorkingDirectory = workingDirectory,
                TaskText = context,
                PrdPath = prdPath,
                GuardrailsPath = guardrailsPath,
                ProgressPath = progressPath,
                CommandOverride = resolvedCommand.Executable,
                CommandPrefixArgs = resolvedCommand.PrefixArgs,
                CommandResolutionSource = resolvedCommand.Source,
                ModelOverride = modelToUse,
                ExtraArgsPassthrough = normalizedExtraArgs,
                Fast = fastModeForEngine
            };

            if (verbose)
            {
                _ui.WriteVerbose($"[verbose] --- Iteration {iteration + 1} ---");
                _ui.WriteVerbose($"[verbose] Engine:       {activeEngineName}");
                _ui.WriteVerbose($"[verbose] Command:      {resolvedCommand.Display()} [{resolvedCommand.Source}]");
                _ui.WriteVerbose($"[verbose] Model:        {modelToUse ?? "(default)"}");
                if (normalizedExtraArgs?.Count > 0)
                    _ui.WriteVerbose($"[verbose] Extra args:   {string.Join(" ", normalizedExtraArgs)}");
                _ui.WriteVerbose($"[verbose] Task ({nextIndex.Value + 1}/{totalTasks}): {taskEntry.DisplayText}");
                _ui.WriteVerbose($"[verbose] Prompt:\n{context}");
            }

            var beforeSnapshot = CaptureWorkspaceSnapshot(workingDirectory);
            var taskInputTokens = 0;
            var taskOutputTokens = 0;

            var attempt = 0;
            var engineCandidateIndex = 0;
            EngineResult? result = null;
        EngineRunStart:
            while (true)
            {
                MarkTaskEngine(statePath, heartbeatPath, activeEngineName, taskEntry.DisplayText, nextIndex.Value, totalTasks);
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var heartbeatTask = RunTaskHeartbeatAsync(
                    statePath,
                    heartbeatPath,
                    taskEntry.DisplayText,
                    nextIndex.Value,
                    totalTasks,
                    activeEngineName,
                    taskStartedAt,
                    heartbeatCts.Token);
                using var spinnerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var spinnerTask = RunEngineSpinnerAsync(activeEngineName, spinnerCts.Token);
                try
                {
                    result = await activeEngine.RunAsync(request, cancellationToken);
                }
                finally
                {
                    heartbeatCts.Cancel();
                    try { await heartbeatTask; } catch { }
                    spinnerCts.Cancel();
                    try { await spinnerTask; } catch { }
                }
                _terminalView?.SetStatus(_s.Format("run.status_engine_attempt_exit", activeEngineName, attempt + 1, result.ExitCode));
                WriteEngineDebugSnapshot(
                    enabled: debugEngineJson,
                    workingDirectory: workingDirectory,
                    taskIndex: nextIndex.Value + 1,
                    totalTasks: totalTasks,
                    attempt: attempt + 1,
                    engineName: activeEngineName,
                    request: request,
                    result: result);

                if (verbose)
                {
                    _ui.WriteVerbose($"[verbose] Exit code:    {result.ExitCode}");
                    _ui.WriteVerbose($"[verbose] Duration:     {result.Duration.TotalSeconds:F1}s");
                    if (result.TokenUsage != null)
                    {
                        var tu = result.TokenUsage;
                        _ui.WriteVerbose(_s.Format("run.tokens", tu.InputTokens, tu.OutputTokens, tu.TotalTokens));
                    }
                    if (!string.IsNullOrWhiteSpace(result.Stdout))
                        _ui.WriteVerbose($"[verbose] Stdout:\n{result.Stdout.TrimEnd()}");
                    if (!string.IsNullOrWhiteSpace(result.Stderr))
                        _ui.WriteVerbose($"[verbose] Stderr:\n{result.Stderr.TrimEnd()}");
                }

                if (result.TokenUsage != null)
                {
                    sessionInputTokens += result.TokenUsage.InputTokens;
                    sessionOutputTokens += result.TokenUsage.OutputTokens;
                    taskInputTokens += result.TokenUsage.InputTokens;
                    taskOutputTokens += result.TokenUsage.OutputTokens;
                }
                OnTokensUpdated?.Invoke(sessionInputTokens, sessionOutputTokens, result.TokenUsage);

                var displayStdout = _outputAdapterRegistry.Adapt(activeEngineName, result.Stdout);
                if (!string.IsNullOrWhiteSpace(displayStdout))
                    foreach (var line in displayStdout.TrimEnd().Split('\n'))
                        _terminalView?.WriteLine(line.TrimEnd());

                if (result.ExitCode == 0 && !result.HadStructuredError)
                    break;

                if (ShouldTryFallbackEngine(result, engineCandidateIndex, engineCandidates))
                {
                    engineCandidateIndex++;
                    var fallbackName = engineCandidates[engineCandidateIndex];
                    var fallback = _engineRegistry.Get(fallbackName);
                    if (fallback != null)
                    {
                        activeEngineName = fallbackName;
                        activeEngine = fallback;
                        engineConfig = config.Engines?.TryGetValue(activeEngineName, out var fallbackConfig) == true ? fallbackConfig : null;
                        var fallbackResolvedCommand = _commandResolver.ResolveForExecution(activeEngineName, config);
                        var fallbackModel = modelOverride ?? doc.Frontmatter?.Model ?? engineConfig?.DefaultModel;
                        var fallbackMaxTokens = maxTokensOverride ?? engineConfig?.MaxTokens;
                        var fallbackTemperature = temperatureOverride ?? engineConfig?.Temperature;
                        var fallbackFast = ResolveFastMode(fallbackName, fallbackModel, fast);
                        if (fast && !fallbackFast)
                            _ui.WriteWarn(_s.Format("run.fast_not_supported", fallbackName, fallbackModel ?? "(default)"));
                        request = new EngineRequest
                        {
                            WorkingDirectory = request.WorkingDirectory,
                            TaskText = request.TaskText,
                            PrdPath = request.PrdPath,
                            GuardrailsPath = request.GuardrailsPath,
                            ProgressPath = request.ProgressPath,
                            CommandOverride = fallbackResolvedCommand.Executable,
                            CommandPrefixArgs = fallbackResolvedCommand.PrefixArgs,
                            CommandResolutionSource = fallbackResolvedCommand.Source,
                            ModelOverride = fallbackModel,
                            ExtraArgsPassthrough = BuildExtraArgs(activeEngineName, fallbackMaxTokens, fallbackTemperature, extraArgsPassthrough),
                            Fast = fallbackFast
                        };
                        _ui.WriteWarn(_s.Format("run.fallback_engine", activeEngineName));
                        continue;
                    }
                }

                _gutterDetector.RecordAttempt(nextIndex.Value, false);
                var effectiveCommand = BuildEffectiveCommandForLogs(request);
                var effectiveArgs = BuildEffectiveArgsForLogs(request);
                var errBlob =
                    $"ResolutionSource={request.CommandResolutionSource ?? "unknown"}\n" +
                    $"Command={effectiveCommand}\n" +
                    $"Args={effectiveArgs}\n" +
                    $"ExitCode={result.ExitCode}\n" +
                    $"Stderr:\n{result.Stderr}\n" +
                    $"Stdout:\n{result.Stdout}";
                AppendToFile(errorsPath, $"[{DateTime.UtcNow:O}] Engine '{activeEngineName}' attempt {attempt + 1}\n{errBlob}\n---\n");
                _ui.WriteWarn(_s.Format("run.engine_failed_with_log", activeEngineName, result.ExitCode));
                var taskShort = taskEntry.DisplayText.Length > 45 ? taskEntry.DisplayText[..45] + "…" : taskEntry.DisplayText;
                _ui.WriteError(_s.Format("run.engine_failed_task", activeEngineName, nextIndex.Value + 1, totalTasks, taskShort, result.ExitCode));
                AppendExecutionEvent(
                    executionPath,
                    "engine_attempt_failed",
                    "engine_exit_non_zero",
                    task: taskEntry.DisplayText,
                    engine: activeEngineName,
                    taskIndex: nextIndex.Value + 1,
                    totalTasks: totalTasks,
                    attempt: attempt + 1,
                    exitCode: result.ExitCode,
                    details: $"errors={SanitizeLogValue(Path.GetRelativePath(workingDirectory, errorsPath))}");
                // Always show the meaningful error lines (filtered: no metadata noise)
                var rawOutput = !string.IsNullOrWhiteSpace(result.Stderr) ? result.Stderr : result.Stdout;
                if (!string.IsNullOrWhiteSpace(rawOutput))
                    foreach (var line in FilterMeaningfulLines(rawOutput))
                        _ui.WriteError($"  {line}");
                else if (!verbose)
                    _ui.WriteError(_s.Format("run.no_output_hint", activeEngineName));
                // In verbose mode also dump everything for debugging
                if (verbose)
                {
                    if (!string.IsNullOrWhiteSpace(result.Stderr))
                        foreach (var line in result.Stderr.TrimEnd().Split('\n'))
                            _ui.WriteVerbose($"  stderr: {line.TrimEnd()}");
                    else if (!string.IsNullOrWhiteSpace(result.Stdout))
                        foreach (var line in result.Stdout.TrimEnd().Split('\n'))
                            _ui.WriteVerbose($"  stdout: {line.TrimEnd()}");
                }
                if (_gutterDetector.IsGutter && !ignoreContextStops)
                {
                    _ui.WriteWarn(_s.Format("run.gutter_details", _retryPolicy.MaxRetries));
                    _ui.WriteInfo(_s.Format("run.summary_partial", completedTasks, totalTasks));
                    _terminalView?.SetStatus(_s.Get("run.status_gutter"));
                    WriteSessionTokensSummary(sessionInputTokens, sessionOutputTokens);
                    AppendExecutionEvent(
                        executionPath,
                        "run_stopped",
                        "gutter",
                        task: taskEntry.DisplayText,
                        engine: activeEngineName,
                        taskIndex: nextIndex.Value + 1,
                        totalTasks: totalTasks,
                        attempt: attempt + 1,
                        exitCode: result?.ExitCode);
                    MarkRunStopped(statePath, heartbeatPath, "gutter");
                    var gutterResult = new RunLoopResult { Completed = false, Gutter = true };
                    AttemptRollbackIfEnabled(workingDirectory, taskSnapshot, taskEntry.DisplayText, "gutter", errorsPath);
                    reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "gutter"));
                    WriteRunReport(workingDirectory, runStartedAt, gutterResult.Completed, reportEntries);
                    return gutterResult;
                }
                if (_gutterDetector.IsGutter && ignoreContextStops)
                {
                    _ui.WriteWarn(_s.Format("run.gutter_details", _retryPolicy.MaxRetries));
                    _ui.WriteWarn(_s.Get("run.gutter_ignored"));
                    _gutterDetector.Reset();
                }
                if (!_retryPolicy.ShouldRetry(result, attempt))
                    break;
                attempt++;
                if (verbose)
                    _ui.WriteVerbose(_s.Format("run.retrying", attempt + 1, _retryPolicy.MaxRetries));
                await _retryPolicy.WaitBeforeRetryAsync(cancellationToken);
            }

            var contextSignal = DetectContextSignal(result?.Stdout, result?.Stderr);
            var tokenThresholdReached = IsTokenThresholdReached(rotationConfig.MaxTotalTokensPerRun, sessionInputTokens, sessionOutputTokens);
            if (contextSignal != ContextSignal.None || tokenThresholdReached)
            {
                var outcome = EvaluateContextRotation(rotationConfig, contextSignal, tokenThresholdReached);
                if (outcome == ContextRotationOutcome.Warn)
                {
                    _ui.WriteWarn(_s.Format("run.context_signal_warn", contextSignal.ToString().ToUpperInvariant(), sessionInputTokens + sessionOutputTokens));
                }
                else if (outcome == ContextRotationOutcome.Rotate)
                {
                    _ui.WriteWarn(_s.Get("run.context_signal_rotate"));
                }
                else if (outcome == ContextRotationOutcome.Defer)
                {
                    if (ignoreContextStops)
                    {
                        _ui.WriteWarn(_s.Get("run.context_signal_defer_ignored"));
                    }
                    else
                    {
                        _ui.WriteWarn(_s.Get("run.context_signal_defer"));
                        WriteSessionTokensSummary(sessionInputTokens, sessionOutputTokens);
                        AppendExecutionEvent(
                            executionPath,
                            "run_stopped",
                            "context_defer",
                            task: taskEntry.DisplayText,
                            engine: activeEngineName,
                            taskIndex: nextIndex.Value + 1,
                            totalTasks: totalTasks,
                            exitCode: result?.ExitCode);
                        MarkRunStopped(statePath, heartbeatPath, "context_defer");
                        var deferred = new RunLoopResult { Completed = false };
                        WriteRunReport(workingDirectory, runStartedAt, deferred.Completed, reportEntries);
                        return deferred;
                    }
                }
                else if (outcome == ContextRotationOutcome.Gutter)
                {
                    if (ignoreContextStops)
                    {
                        _ui.WriteWarn(_s.Get("run.context_signal_gutter_ignored"));
                    }
                    else
                    {
                        _ui.WriteWarn(_s.Get("run.context_signal_gutter"));
                        WriteSessionTokensSummary(sessionInputTokens, sessionOutputTokens);
                        AppendExecutionEvent(
                            executionPath,
                            "run_stopped",
                            "context_gutter",
                            task: taskEntry.DisplayText,
                            engine: activeEngineName,
                            taskIndex: nextIndex.Value + 1,
                            totalTasks: totalTasks,
                            exitCode: result?.ExitCode);
                        MarkRunStopped(statePath, heartbeatPath, "context_gutter");
                        var gutter = new RunLoopResult { Completed = false, Gutter = true };
                        WriteRunReport(workingDirectory, runStartedAt, gutter.Completed, reportEntries);
                        return gutter;
                    }
                }
            }

            if (result?.ExitCode != 0)
            {
                _ui.WriteWarn(_s.Format("run.engine_exit_stopping", result?.ExitCode ?? -1));
                _ui.WriteInfo(_s.Format("run.summary_partial", completedTasks, totalTasks));
                _terminalView?.SetStatus(_s.Format("run.status_stopped_exit_code", result?.ExitCode ?? -1));
                WriteSessionTokensSummary(sessionInputTokens, sessionOutputTokens);
                AppendExecutionEvent(
                    executionPath,
                    "run_stopped",
                    "engine_failed",
                    task: taskEntry.DisplayText,
                    engine: activeEngineName,
                    taskIndex: nextIndex.Value + 1,
                    totalTasks: totalTasks,
                    attempt: attempt + 1,
                    exitCode: result?.ExitCode,
                    details: $"errors={SanitizeLogValue(Path.GetRelativePath(workingDirectory, errorsPath))}");
                MarkRunStopped(statePath, heartbeatPath, "engine_failed");
                var failed = new RunLoopResult { Completed = false };
                AttemptRollbackIfEnabled(workingDirectory, taskSnapshot, taskEntry.DisplayText, "engine_failed", errorsPath);
                reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "failed"));
                WriteRunReport(workingDirectory, runStartedAt, failed.Completed, reportEntries);
                return failed;
            }

            var afterSnapshot = CaptureWorkspaceSnapshot(workingDirectory);
            if (!beforeSnapshot.Equals(afterSnapshot))
            {
                // workspace changed, proceed
            }
            else
            {
                _ui.WriteWarn(_s.Get("run.no_changes"));
                _terminalView?.SetStatus(_s.Get("run.status_no_workspace_changes"));
                var noChangeAction = ResolveNoChangeAction(noChangePolicy, attempt, noChangeMaxAttempts, noChangeStopOnMaxAttempts, engineCandidateIndex, engineCandidates.Count);
                if (noChangeAction == NoChangeAction.Retry && attempt + 1 < noChangeMaxAttempts)
                {
                    attempt++;
                    if (verbose)
                        _ui.WriteVerbose(_s.Format("run.retrying", attempt + 1, noChangeMaxAttempts));
                    await _retryPolicy.WaitBeforeRetryAsync(cancellationToken);
                    goto EngineRunStart;
                }
                if (noChangeAction == NoChangeAction.Fallback && engineCandidateIndex < engineCandidates.Count - 1)
                {
                    engineCandidateIndex++;
                    var fallbackName = engineCandidates[engineCandidateIndex];
                    var fallback = _engineRegistry.Get(fallbackName);
                    if (fallback != null)
                    {
                        activeEngineName = fallbackName;
                        activeEngine = fallback;
                        engineConfig = config.Engines?.TryGetValue(activeEngineName, out var fallbackConfig) == true ? fallbackConfig : null;
                        var fallbackResolvedCommand = _commandResolver.ResolveForExecution(activeEngineName, config);
                        var fallbackModel = modelOverride ?? doc.Frontmatter?.Model ?? engineConfig?.DefaultModel;
                        var fallbackMaxTokens = maxTokensOverride ?? engineConfig?.MaxTokens;
                        var fallbackTemperature = temperatureOverride ?? engineConfig?.Temperature;
                        request = new EngineRequest
                        {
                            WorkingDirectory = request.WorkingDirectory,
                            TaskText = request.TaskText,
                            PrdPath = request.PrdPath,
                            GuardrailsPath = request.GuardrailsPath,
                            ProgressPath = request.ProgressPath,
                            CommandOverride = fallbackResolvedCommand.Executable,
                            CommandPrefixArgs = fallbackResolvedCommand.PrefixArgs,
                            CommandResolutionSource = fallbackResolvedCommand.Source,
                            ModelOverride = fallbackModel,
                            ExtraArgsPassthrough = BuildExtraArgs(activeEngineName, fallbackMaxTokens, fallbackTemperature, extraArgsPassthrough),
                            Fast = fast
                        };
                        _ui.WriteWarn(_s.Format("run.fallback_engine", activeEngineName));
                        attempt = 0;
                        goto EngineRunStart;
                    }
                }
                _gutterDetector.RecordAttempt(nextIndex.Value, false);
                if (_gutterDetector.IsGutter && !ignoreContextStops)
                {
                    _ui.WriteWarn(_s.Format("run.gutter_details", _retryPolicy.MaxRetries));
                    _ui.WriteInfo(_s.Format("run.summary_partial", completedTasks, totalTasks));
                    _terminalView?.SetStatus(_s.Get("run.status_gutter"));
                    WriteSessionTokensSummary(sessionInputTokens, sessionOutputTokens);
                    MarkRunStopped(statePath, heartbeatPath, "no_changes_gutter");
                    var noChangeGutter = new RunLoopResult { Completed = false, Gutter = true };
                    AttemptRollbackIfEnabled(workingDirectory, taskSnapshot, taskEntry.DisplayText, "no_changes_gutter", errorsPath);
                    reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "gutter"));
                    WriteRunReport(workingDirectory, runStartedAt, noChangeGutter.Completed, reportEntries);
                    return noChangeGutter;
                }
                if (_gutterDetector.IsGutter && ignoreContextStops)
                {
                    _ui.WriteWarn(_s.Format("run.gutter_details", _retryPolicy.MaxRetries));
                    _ui.WriteWarn(_s.Get("run.gutter_ignored"));
                    _gutterDetector.Reset();
                }
                if (noChangeAction == NoChangeAction.FailFast)
                {
                    reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "no_changes"));
                    if (noChangePolicy == NoChangePolicy.Retry && noChangeStopOnMaxAttempts)
                        _ui.WriteWarn(_s.Format("run.no_change_max_retries_stop", noChangeMaxAttempts));
                    _ui.WriteWarn(_s.Get("run.no_change_fail_fast"));
                    AppendExecutionEvent(
                        executionPath,
                        "run_stopped",
                        "no_changes_fail_fast",
                        task: taskEntry.DisplayText,
                        engine: activeEngineName,
                        taskIndex: nextIndex.Value + 1,
                        totalTasks: totalTasks,
                        attempt: attempt + 1,
                        exitCode: result?.ExitCode,
                        details: $"policy={noChangePolicy}");
                    MarkRunStopped(statePath, heartbeatPath, "no_changes_fail_fast");
                    WriteRunReport(workingDirectory, runStartedAt, false, reportEntries);
                    return new RunLoopResult { Completed = false };
                }
                if (noChangeAction == NoChangeAction.Continue && !noChangeStopOnMaxAttempts)
                {
                    PrdWriter.MarkTaskSkippedForReview(prdPath, doc, nextIndex.Value);
                    var skippedState = _stateStore.Load(statePath);
                    skippedState.Iteration = iteration + 1;
                    skippedState.LastTaskIndex = nextIndex.Value;
                    skippedState.LastTaskText = taskEntry.DisplayText;
                    _stateStore.Save(statePath, skippedState);

                    AppendToFile(progressPath, $"- [{DateTime.UtcNow:O}] SKIPPED_FOR_REVIEW: {taskEntry.DisplayText}\n");
                    AppendToFile(activityPath, $"[{DateTime.UtcNow:O}] Skipped task {nextIndex.Value + 1} for manual review: {taskEntry.DisplayText}\n");
                    AppendExecutionEvent(
                        executionPath,
                        "task_blocked",
                        "skipped_for_review",
                        task: taskEntry.DisplayText,
                        engine: activeEngineName,
                        taskIndex: nextIndex.Value + 1,
                        totalTasks: totalTasks,
                        exitCode: result?.ExitCode,
                        details: $"policy={noChangePolicy}");
                    _ui.WriteWarn(_s.Get("run.no_change_marked_for_review"));
                    _gutterDetector.RecordAttempt(nextIndex.Value, true);
                    anyTaskCompletedInRun = true;
                    _terminalView?.SetStatus(_s.Format("run.status_skipped_task_review", nextIndex.Value + 1, totalTasks));
                    _terminalView?.SetProgress(completedTasks + 1, totalTasks, taskEntry.DisplayText);
                    reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "skipped_for_review"));
                }
                else
                {
                    reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "no_changes"));
                }
                iteration++;
                continue;
            }

            var lintCommand = doc.Frontmatter?.LintCommand;
            if (!skipLint && !string.IsNullOrWhiteSpace(lintCommand))
            {
                if (verbose) _ui.WriteVerbose($"[verbose] Running lint: {lintCommand}");
                _terminalView?.SetStatus(_s.Get("run.status_running_lint"));
                var lintPassed = await RunTestCommandAsync(workingDirectory, lintCommand, cancellationToken);
                if (!lintPassed)
                {
                    _ui.WriteWarn(_s.Get("run.lint_failed_not_marked"));
                    _terminalView?.SetStatus(_s.Get("run.status_lint_failed"));
                    _gutterDetector.RecordAttempt(nextIndex.Value, false);
                    if (_gutterDetector.IsGutter && !ignoreContextStops)
                    {
                        _ui.WriteInfo(_s.Format("run.summary_partial", completedTasks, totalTasks));
                        WriteSessionTokensSummary(sessionInputTokens, sessionOutputTokens);
                        AppendExecutionEvent(
                            executionPath,
                            "run_stopped",
                            "lint_failed_gutter",
                            task: taskEntry.DisplayText,
                            engine: activeEngineName,
                            taskIndex: nextIndex.Value + 1,
                            totalTasks: totalTasks,
                            exitCode: result?.ExitCode);
                        MarkRunStopped(statePath, heartbeatPath, "lint_failed_gutter");
                        var lintGutter = new RunLoopResult { Completed = false, Gutter = true };
                        AttemptRollbackIfEnabled(workingDirectory, taskSnapshot, taskEntry.DisplayText, "lint_failed_gutter", errorsPath);
                        reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "gutter"));
                        WriteRunReport(workingDirectory, runStartedAt, lintGutter.Completed, reportEntries);
                        return lintGutter;
                    }
                    if (_gutterDetector.IsGutter && ignoreContextStops)
                    {
                        _ui.WriteWarn(_s.Format("run.gutter_details", _retryPolicy.MaxRetries));
                        _ui.WriteWarn(_s.Get("run.gutter_ignored"));
                        _gutterDetector.Reset();
                    }
                    AttemptRollbackIfEnabled(workingDirectory, taskSnapshot, taskEntry.DisplayText, "lint_failed", errorsPath);
                    AppendExecutionEvent(
                        executionPath,
                        "task_blocked",
                        "lint_failed",
                        task: taskEntry.DisplayText,
                        engine: activeEngineName,
                        taskIndex: nextIndex.Value + 1,
                        totalTasks: totalTasks,
                        exitCode: result?.ExitCode);
                    reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "lint_failed"));
                    iteration++;
                    continue;
                }
            }

            var testCommand = doc.Frontmatter?.TestCommand;
            if (!skipTests && !string.IsNullOrWhiteSpace(testCommand))
            {
                var testPassed = await RunTestCommandAsync(workingDirectory, testCommand, cancellationToken);
                if (!testPassed)
                {
                    _ui.WriteWarn(_s.Get("run.tests_failed_not_marked"));
                    _terminalView?.SetStatus(_s.Get("run.status_tests_failed"));
                    _gutterDetector.RecordAttempt(nextIndex.Value, false);
                    if (_gutterDetector.IsGutter && !ignoreContextStops)
                    {
                        _ui.WriteInfo(_s.Format("run.summary_partial", completedTasks, totalTasks));
                        WriteSessionTokensSummary(sessionInputTokens, sessionOutputTokens);
                        AppendExecutionEvent(
                            executionPath,
                            "run_stopped",
                            "tests_failed_gutter",
                            task: taskEntry.DisplayText,
                            engine: activeEngineName,
                            taskIndex: nextIndex.Value + 1,
                            totalTasks: totalTasks,
                            exitCode: result?.ExitCode);
                        MarkRunStopped(statePath, heartbeatPath, "tests_failed_gutter");
                        var testGutter = new RunLoopResult { Completed = false, Gutter = true };
                        AttemptRollbackIfEnabled(workingDirectory, taskSnapshot, taskEntry.DisplayText, "tests_failed_gutter", errorsPath);
                        reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "gutter"));
                        WriteRunReport(workingDirectory, runStartedAt, testGutter.Completed, reportEntries);
                        return testGutter;
                    }
                    if (_gutterDetector.IsGutter && ignoreContextStops)
                    {
                        _ui.WriteWarn(_s.Format("run.gutter_details", _retryPolicy.MaxRetries));
                        _ui.WriteWarn(_s.Get("run.gutter_ignored"));
                        _gutterDetector.Reset();
                    }
                    AttemptRollbackIfEnabled(workingDirectory, taskSnapshot, taskEntry.DisplayText, "tests_failed", errorsPath);
                    AppendExecutionEvent(
                        executionPath,
                        "task_blocked",
                        "tests_failed",
                        task: taskEntry.DisplayText,
                        engine: activeEngineName,
                        taskIndex: nextIndex.Value + 1,
                        totalTasks: totalTasks,
                        exitCode: result?.ExitCode);
                    reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "tests_failed"));
                    iteration++;
                    continue;
                }
            }

            if (!string.IsNullOrWhiteSpace(browserCommand))
            {
                var browserPassed = await RunTestCommandAsync(workingDirectory, browserCommand, cancellationToken);
                if (!browserPassed)
                {
                    _ui.WriteWarn(_s.Get("run.browser_failed_not_marked"));
                    _terminalView?.SetStatus(_s.Get("run.status_browser_failed"));
                    _gutterDetector.RecordAttempt(nextIndex.Value, false);
                    if (_gutterDetector.IsGutter && !ignoreContextStops)
                    {
                        _ui.WriteInfo(_s.Format("run.summary_partial", completedTasks, totalTasks));
                        WriteSessionTokensSummary(sessionInputTokens, sessionOutputTokens);
                        AppendExecutionEvent(
                            executionPath,
                            "run_stopped",
                            "browser_failed_gutter",
                            task: taskEntry.DisplayText,
                            engine: activeEngineName,
                            taskIndex: nextIndex.Value + 1,
                            totalTasks: totalTasks,
                            exitCode: result?.ExitCode);
                        MarkRunStopped(statePath, heartbeatPath, "browser_failed_gutter");
                        var browserGutter = new RunLoopResult { Completed = false, Gutter = true };
                        AttemptRollbackIfEnabled(workingDirectory, taskSnapshot, taskEntry.DisplayText, "browser_failed_gutter", errorsPath);
                        reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "gutter"));
                        WriteRunReport(workingDirectory, runStartedAt, browserGutter.Completed, reportEntries);
                        return browserGutter;
                    }
                    if (_gutterDetector.IsGutter && ignoreContextStops)
                    {
                        _ui.WriteWarn(_s.Format("run.gutter_details", _retryPolicy.MaxRetries));
                        _ui.WriteWarn(_s.Get("run.gutter_ignored"));
                        _gutterDetector.Reset();
                    }
                    AttemptRollbackIfEnabled(workingDirectory, taskSnapshot, taskEntry.DisplayText, "browser_failed", errorsPath);
                    AppendExecutionEvent(
                        executionPath,
                        "task_blocked",
                        "browser_failed",
                        task: taskEntry.DisplayText,
                        engine: activeEngineName,
                        taskIndex: nextIndex.Value + 1,
                        totalTasks: totalTasks,
                        exitCode: result?.ExitCode);
                    reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? -1, attempt, taskInputTokens, taskOutputTokens, "browser_failed"));
                    iteration++;
                    continue;
                }
            }

            PrdWriter.MarkTaskCompleted(prdPath, doc, nextIndex.Value);
            var state = _stateStore.Load(statePath);
            state.Iteration = iteration + 1;
            state.LastTaskIndex = nextIndex.Value;
            state.LastTaskText = taskEntry.DisplayText;
            state.CurrentTaskIndex = null;
            state.CurrentTaskText = null;
            state.CurrentEngine = null;
            state.CurrentTaskStartedAt = null;
            state.RunStatus = "running";
            state.LastHeartbeat = DateTimeOffset.UtcNow;
            state.LastExitReason = "task_completed";
            state.LastExitAt = DateTimeOffset.UtcNow;
            WriteHeartbeat(heartbeatPath, state, taskEntry.DisplayText, nextIndex.Value, totalTasks, activeEngineName, "idle_between_tasks");
            _stateStore.Save(statePath, state);

            AppendToFile(progressPath, $"- [{DateTime.UtcNow:O}] {taskEntry.DisplayText}\n");
            AppendToFile(activityPath, $"[{DateTime.UtcNow:O}] Completed task {nextIndex.Value + 1}: {taskEntry.DisplayText}\n");
            AppendExecutionEvent(
                executionPath,
                "task_completed",
                "success",
                task: taskEntry.DisplayText,
                engine: activeEngineName,
                taskIndex: nextIndex.Value + 1,
                totalTasks: totalTasks,
                exitCode: result?.ExitCode,
                details: $"duration_s={DateTimeOffset.UtcNow.Subtract(taskStartedAt).TotalSeconds:F1}");
            _gutterDetector.RecordAttempt(nextIndex.Value, true);
            anyTaskCompletedInRun = true;
            _terminalView?.SetStatus(_s.Format("run.status_completed_task", nextIndex.Value + 1, totalTasks));
            _terminalView?.SetProgress(completedTasks + 1, totalTasks, taskEntry.DisplayText);

            // Git: commit + push + PR after task completes
            if (branchPerTask && !noCommit && taskBranch != null)
            {
                var commitMsg = $"ralph: task {nextIndex.Value + 1} - {taskEntry.DisplayText}";
                _terminalView?.SetStatus(_s.Get("run.status_committing"));
                if (_git.HasUncommittedChanges(workingDirectory))
                {
                    var committed = _git.CommitAll(workingDirectory, commitMsg);
                    if (verbose) _ui.WriteVerbose($"[verbose] Git commit:   {(committed ? "ok" : "failed")}");
                    if (committed)
                    {
                        _terminalView?.SetStatus(_s.Get("run.status_pushing"));
                        var pushed = _git.PushBranch(workingDirectory, taskBranch);
                        if (verbose) _ui.WriteVerbose($"[verbose] Git push:     {(pushed ? "ok" : "failed")}");
                        if (!pushed) _ui.WriteWarn(_s.Format("run.push_failed_local_commit", taskBranch));

                        if (createPr && pushed)
                        {
                            _terminalView?.SetStatus(_s.Get("run.status_creating_pr"));
                            var prBody = $"Automated by ralph.\n\nTask: {taskEntry.DisplayText}";
                            var prCreated = _git.CreatePullRequest(workingDirectory, taskEntry.DisplayText, prBody, draftPr);
                            if (prCreated)
                                _ui.WriteInfo($"PR created{(draftPr ? " (draft)" : "")} for: {taskEntry.DisplayText}");
                            else
                                _ui.WriteWarn(_s.Get("run.pr_creation_failed"));
                            if (verbose) _ui.WriteVerbose($"[verbose] PR created:   {prCreated}");
                        }
                    }
                }
                else
                {
                    if (verbose) _ui.WriteVerbose("[verbose] Git commit:   skipped (no changes)");
                }
            }

            reportEntries.Add(BuildReportEntry(taskEntry.DisplayText, activeEngineName, taskStartedAt, DateTimeOffset.UtcNow, result?.ExitCode ?? 0, attempt, taskInputTokens, taskOutputTokens, "success"));

            iteration++;
        }

        var finalDoc = PrdParser.Parse(prdPath);
        var allTasksCompleted = finalDoc.GetNextPendingTaskIndex() == null;
        if (!allTasksCompleted)
        {
            _ui.WriteWarn(_s.Get("run.max_iterations_pending"));
            _terminalView?.SetStatus(_s.Get("run.status_max_iterations_pending"));
        }
        else
        {
            _terminalView?.SetStatus(_s.Get("run.status_max_iterations"));
        }
        WriteSessionTokensSummary(sessionInputTokens, sessionOutputTokens);
        var finalResult = new RunLoopResult { Completed = allTasksCompleted || anyTaskCompletedInRun };
        AppendExecutionEvent(
            executionPath,
            "run_finished",
            finalResult.Completed ? (allTasksCompleted ? "all_tasks_completed" : "max_iterations_partial_success") : "max_iterations_incomplete",
            details: $"iterations={iteration}");
        MarkRunStopped(
            statePath,
            heartbeatPath,
            finalResult.Completed ? (allTasksCompleted ? "all_tasks_completed" : "max_iterations_partial_success") : "max_iterations_incomplete");
        WriteRunReport(workingDirectory, runStartedAt, finalResult.Completed, reportEntries);
        return finalResult;
    }

    public Task<RunLoopResult> DryRunAsync(
        string workingDirectory,
        string prdPath,
        string? engineName = null,
        string? modelOverride = null,
        int? maxTokensOverride = null,
        double? temperatureOverride = null,
        int? maxIterations = null)
    {
        Console.WriteLine($"ralph dry-run ({Path.GetFileName(prdPath)})");
        Console.WriteLine(new string('=', 40));

        var isInitialized = _workspaceInit.IsInitialized(workingDirectory);
        Console.WriteLine($"  Workspace:  {(isInitialized ? ".ralph/ initialized" : ".ralph/ NOT initialized (run 'ralph init')")}");

        var configPath = _workspaceInit.GetConfigPath(workingDirectory);
        var config = File.Exists(configPath) ? new ConfigStore().Load(configPath) : RalphConfig.Default;

        var doc = PrdParser.Parse(prdPath);
        var frontmatterEngine = doc.Frontmatter?.Engine;
        var frontmatterModel = doc.Frontmatter?.Model;
        var engineNameToUse = engineName ?? frontmatterEngine ?? "cursor";
        var engineConfig = config.Engines?.TryGetValue(engineNameToUse, out var ec) == true ? ec : null;
        var modelToUse = modelOverride ?? frontmatterModel ?? engineConfig?.DefaultModel;
        var resolvedCommand = _commandResolver.ResolveForExecution(engineNameToUse, config);
        var command = resolvedCommand.Display();

        Console.WriteLine($"  Engine:     {engineNameToUse} ({command})");
        Console.WriteLine($"  Model:      {(modelToUse ?? "(default)")}");
        if (maxTokensOverride.HasValue || engineConfig?.MaxTokens != null)
            Console.WriteLine($"  Max tokens: {maxTokensOverride ?? engineConfig?.MaxTokens}");
        if (maxIterations.HasValue)
            Console.WriteLine($"  Max iters:  {maxIterations.Value}");

        var guardrailsPath = _workspaceInit.GetGuardrailsPath(workingDirectory);
        var progressPath = _workspaceInit.GetProgressPath(workingDirectory);
        var hasGuardrails = File.Exists(guardrailsPath);
        var progressLines = File.Exists(progressPath) ? File.ReadAllLines(progressPath).Length : 0;
        Console.WriteLine($"  Guardrails: {(hasGuardrails ? $"yes ({Path.GetRelativePath(workingDirectory, guardrailsPath)})" : "none")}");
        Console.WriteLine($"  Progress:   {(progressLines > 0 ? $"{progressLines} entries in .ralph/progress.txt" : "none")}");

        Console.WriteLine();

        var pending = doc.TaskEntries.Where(t => t.IsPending).ToList();
        var completed = doc.TaskEntries.Count - pending.Count;
        Console.WriteLine($"Tasks: {completed}/{doc.TaskEntries.Count} completed, {pending.Count} pending");

        if (pending.Count == 0)
        {
            Console.WriteLine(_s.Get("run.dry_run_all_completed"));
        }
        else
        {
            var toShow = maxIterations.HasValue ? pending.Take(maxIterations.Value) : pending;
            Console.WriteLine();
            var idx = 0;
            foreach (var task in toShow)
            {
                Console.WriteLine($"  {++idx}. [ ] {task.DisplayText}");
                Console.WriteLine($"       Would run: {command}{(modelToUse != null ? $" --model {modelToUse}" : "")} \"<prompt>\"");
            }
            if (maxIterations.HasValue && pending.Count > maxIterations.Value)
                Console.WriteLine($"  ... and {pending.Count - maxIterations.Value} more (limited by --max-iterations {maxIterations.Value})");
        }

        Console.WriteLine();
        Console.WriteLine(_s.Get("run.dry_run_no_files"));
        return Task.FromResult(new RunLoopResult { Completed = true });
    }

    public Task<RunLoopResult> DryRunSingleTaskAsync(
        string workingDirectory,
        string taskText,
        string? engineName = null,
        string? modelOverride = null,
        int? maxTokensOverride = null,
        double? temperatureOverride = null,
        IReadOnlyList<string>? extraArgsPassthrough = null)
    {
        Console.WriteLine("ralph dry-run (single task)");
        Console.WriteLine(new string('=', 40));

        var isInitialized = _workspaceInit.IsInitialized(workingDirectory);
        Console.WriteLine($"  Workspace:  {(isInitialized ? ".ralph/ initialized" : ".ralph/ NOT initialized (run 'ralph init')")}");

        var configPath = _workspaceInit.GetConfigPath(workingDirectory);
        var config = File.Exists(configPath) ? new ConfigStore().Load(configPath) : RalphConfig.Default;

        var engineNameToUse = engineName ?? "cursor";
        var engineConfig = config.Engines?.TryGetValue(engineNameToUse, out var ec) == true ? ec : null;
        var modelToUse = modelOverride ?? engineConfig?.DefaultModel;
        var resolvedCommand = _commandResolver.ResolveForExecution(engineNameToUse, config);
        var command = resolvedCommand.Display();

        Console.WriteLine($"  Engine:     {engineNameToUse} ({command})");
        Console.WriteLine($"  Model:      {(modelToUse ?? "(default)")}");
        if (maxTokensOverride.HasValue || engineConfig?.MaxTokens != null)
            Console.WriteLine($"  Max tokens: {maxTokensOverride ?? engineConfig?.MaxTokens}");
        if (temperatureOverride.HasValue || engineConfig?.Temperature != null)
            Console.WriteLine($"  Temp:       {temperatureOverride ?? engineConfig?.Temperature}");

        var guardrailsPath = _workspaceInit.GetGuardrailsPath(workingDirectory);
        var hasGuardrails = File.Exists(guardrailsPath);
        Console.WriteLine($"  Guardrails: {(hasGuardrails ? $"yes ({Path.GetRelativePath(workingDirectory, guardrailsPath)})" : "none")}");
        if (extraArgsPassthrough is { Count: > 0 })
            Console.WriteLine($"  Extra args: {string.Join(" ", extraArgsPassthrough)}");

        Console.WriteLine();
        Console.WriteLine("Task: 1 pending");
        Console.WriteLine();
        Console.WriteLine($"  1. [ ] {taskText}");
        Console.WriteLine($"       Would run: {command}{(modelToUse != null ? $" --model {modelToUse}" : "")} \"<prompt>\"");
        Console.WriteLine();
        Console.WriteLine(_s.Get("run.dry_run_no_files"));
        return Task.FromResult(new RunLoopResult { Completed = true });
    }

    public async Task<RunLoopResult> OnceAsync(
        string workingDirectory,
        string prdPath,
        bool skipTests = false,
        bool skipLint = false,
        string? engineName = null,
        string? modelOverride = null,
        int? maxTokensOverride = null,
        double? temperatureOverride = null,
        IReadOnlyList<string>? extraArgsPassthrough = null,
        CancellationToken cancellationToken = default,
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
        bool noCommit = false,
        bool fast = false,
        PromptContextMode promptContextMode = PromptContextMode.LoopTaskScoped)
    {
        return await RunAsync(
            workingDirectory,
            prdPath,
            maxIterations: 1,
            skipTests,
            skipLint,
            engineName,
            modelOverride,
            maxTokensOverride,
            temperatureOverride,
            extraArgsPassthrough,
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
            promptContextMode);
    }

    public async Task<RunLoopResult> RunSingleTaskAsync(
        string workingDirectory,
        string taskText,
        string? engineName = null,
        string? modelOverride = null,
        int? maxTokensOverride = null,
        double? temperatureOverride = null,
        IReadOnlyList<string>? extraArgsPassthrough = null,
        CancellationToken cancellationToken = default,
        bool verbose = false,
        bool debugEngineJson = false,
        bool fast = false)
    {
        var executionPath = _workspaceInit.GetExecutionLogPath(workingDirectory);
        Directory.CreateDirectory(_workspaceInit.GetRalphDir(workingDirectory));
        AppendExecutionEvent(
            executionPath,
            "run_started",
            "single_task_begin",
            task: taskText);

        var configPath = _workspaceInit.GetConfigPath(workingDirectory);
        var config = File.Exists(configPath) ? new ConfigStore().Load(configPath) : RalphConfig.Default;

        var engineNameToUse = engineName ?? "cursor";
        var candidates = BuildEngineCandidates(engineNameToUse, config);
        var activeEngineName = candidates[0];
        var activeEngine = _engineRegistry.Get(activeEngineName);
        if (activeEngine == null)
        {
            _ui.WriteError(_s.Format("run.engine_not_found", activeEngineName, string.Join(", ", _engineRegistry.GetRegisteredNames())));
            AppendExecutionEvent(
                executionPath,
                "run_stopped",
                "engine_not_found",
                task: taskText,
                engine: activeEngineName);
            return new RunLoopResult { Completed = false };
        }

        var engineConfig = config.Engines?.TryGetValue(activeEngineName, out var ec) == true ? ec : null;
        var resolvedCommand = _commandResolver.ResolveForExecution(activeEngineName, config);
        var modelToUse = modelOverride ?? engineConfig?.DefaultModel;
        var maxTokensToUse = maxTokensOverride ?? engineConfig?.MaxTokens;
        var fastModeForEngine = ResolveFastMode(activeEngineName, modelToUse, fast);
        if (fast && !fastModeForEngine)
            _ui.WriteWarn(_s.Format("run.fast_not_supported", activeEngineName, modelToUse ?? "(default)"));
        var normalizedExtraArgs = BuildExtraArgs(activeEngineName, maxTokensToUse, null, extraArgsPassthrough);
        var modelAvailability = await CheckCodexModelAvailabilityAsync(
            workingDirectory,
            activeEngineName,
            modelToUse,
            resolvedCommand,
            cancellationToken);
        if (modelAvailability == false)
        {
            _ui.WriteError(_s.Format("run.model_unavailable", activeEngineName, modelToUse));
            AppendExecutionEvent(
                executionPath,
                "run_stopped",
                "model_unavailable",
                task: taskText,
                engine: activeEngineName,
                details: $"model={modelToUse}");
            return new RunLoopResult { Completed = false };
        }

        var guardrailsPath = _workspaceInit.GetGuardrailsPath(workingDirectory);
        var guardrails = File.Exists(guardrailsPath) ? File.ReadAllText(guardrailsPath) : null;
        var context = PromptBuilder.BuildLoopContext(guardrails, null, taskText);
        if (activeEngineName.Equals("cursor", StringComparison.OrdinalIgnoreCase))
            context += "\n\n## Execution mode\nExecute the task now. Do not ask for confirmation questions.";

        if (verbose)
        {
            _ui.WriteVerbose($"[verbose] Brownfield mode (no PRD)");
            _ui.WriteVerbose($"[verbose] Engine:       {activeEngineName}");
            _ui.WriteVerbose($"[verbose] Command:      {resolvedCommand.Display()} [{resolvedCommand.Source}]");
            _ui.WriteVerbose($"[verbose] Model:        {modelToUse ?? "(default)"}");
            _ui.WriteVerbose($"[verbose] Prompt:\n{context}");
        }

        _terminalView?.SetStatus(_s.Format("run.status_running_engine", activeEngineName));

        var request = new EngineRequest
        {
            WorkingDirectory = workingDirectory,
            TaskText = context,
            PrdPath = null,
            GuardrailsPath = guardrailsPath,
            ProgressPath = null,
            CommandOverride = resolvedCommand.Executable,
                CommandPrefixArgs = resolvedCommand.PrefixArgs,
                CommandResolutionSource = resolvedCommand.Source,
                ModelOverride = modelToUse,
                ExtraArgsPassthrough = normalizedExtraArgs,
                Fast = fastModeForEngine
            };

        var result = await activeEngine.RunAsync(request, cancellationToken);
        WriteEngineDebugSnapshot(
            enabled: debugEngineJson,
            workingDirectory: workingDirectory,
            taskIndex: 1,
            totalTasks: 1,
            attempt: 1,
            engineName: activeEngineName,
            request: request,
            result: result);
        if (result.ExitCode != 0 && ShouldTryFallbackEngine(result, 0, candidates))
        {
            var fallbackName = candidates[1];
            var fallbackEngine = _engineRegistry.Get(fallbackName);
            if (fallbackEngine != null)
            {
                var fallbackConfig = config.Engines?.TryGetValue(fallbackName, out var fc) == true ? fc : null;
                var fallbackResolved = _commandResolver.ResolveForExecution(fallbackName, config);
                var fallbackModel = modelOverride ?? fallbackConfig?.DefaultModel;
                var fallbackMaxTokens = maxTokensOverride ?? fallbackConfig?.MaxTokens;
                var fallbackFast = ResolveFastMode(fallbackName, fallbackModel, fast);
                if (fast && !fallbackFast)
                    _ui.WriteWarn(_s.Format("run.fast_not_supported", fallbackName, fallbackModel ?? "(default)"));
                var fallbackArgs = BuildExtraArgs(fallbackName, fallbackMaxTokens, null, extraArgsPassthrough);
                request = new EngineRequest
                {
                    WorkingDirectory = request.WorkingDirectory,
                    TaskText = request.TaskText,
                    PrdPath = request.PrdPath,
                    GuardrailsPath = request.GuardrailsPath,
                    ProgressPath = request.ProgressPath,
                    CommandOverride = fallbackResolved.Executable,
                    CommandPrefixArgs = fallbackResolved.PrefixArgs,
                    CommandResolutionSource = fallbackResolved.Source,
                    ModelOverride = fallbackModel,
                    ExtraArgsPassthrough = fallbackArgs,
                    Fast = fallbackFast
                };
                _ui.WriteWarn(_s.Format("run.fallback_engine", fallbackName));
                result = await fallbackEngine.RunAsync(request, cancellationToken);
                activeEngineName = fallbackName;
                WriteEngineDebugSnapshot(
                    enabled: debugEngineJson,
                    workingDirectory: workingDirectory,
                    taskIndex: 1,
                    totalTasks: 1,
                    attempt: 2,
                    engineName: activeEngineName,
                    request: request,
                    result: result);
            }
        }

        if (verbose)
        {
            _ui.WriteVerbose($"[verbose] Exit code:    {result.ExitCode}");
            _ui.WriteVerbose($"[verbose] Duration:     {result.Duration.TotalSeconds:F1}s");
            if (!string.IsNullOrWhiteSpace(result.Stdout))
                _ui.WriteVerbose($"[verbose] Stdout:\n{result.Stdout.TrimEnd()}");
            if (!string.IsNullOrWhiteSpace(result.Stderr))
                _ui.WriteVerbose($"[verbose] Stderr:\n{result.Stderr.TrimEnd()}");
        }

        OnTokensUpdated?.Invoke(
            result.TokenUsage?.InputTokens ?? 0,
            result.TokenUsage?.OutputTokens ?? 0,
            result.TokenUsage);

        var displayStdout = _outputAdapterRegistry.Adapt(activeEngineName, result.Stdout);
        if (!string.IsNullOrWhiteSpace(displayStdout))
            foreach (var line in displayStdout.TrimEnd().Split('\n'))
                _terminalView?.WriteLine(line.TrimEnd());

        if (result.ExitCode != 0)
        {
            _ui.WriteError(_s.Format("run.engine_failed", activeEngineName, result.ExitCode));
            AppendExecutionEvent(
                executionPath,
                "run_stopped",
                "engine_failed",
                task: taskText,
                engine: activeEngineName,
                exitCode: result.ExitCode);
            var rawOutput = !string.IsNullOrWhiteSpace(result.Stderr) ? result.Stderr : result.Stdout;
            if (!string.IsNullOrWhiteSpace(rawOutput))
                foreach (var line in FilterMeaningfulLines(rawOutput))
                    _ui.WriteError($"  {line}");
            if (verbose)
            {
                if (!string.IsNullOrWhiteSpace(result.Stderr))
                    foreach (var line in result.Stderr.TrimEnd().Split('\n'))
                        _ui.WriteVerbose($"  stderr: {line.TrimEnd()}");
                else if (!string.IsNullOrWhiteSpace(result.Stdout))
                    foreach (var line in result.Stdout.TrimEnd().Split('\n'))
                        _ui.WriteVerbose($"  stdout: {line.TrimEnd()}");
            }
            return new RunLoopResult { Completed = false };
        }

        var activityPath = _workspaceInit.GetActivityLogPath(workingDirectory);
        if (File.Exists(Path.GetDirectoryName(activityPath)!) || Directory.Exists(Path.GetDirectoryName(activityPath)!))
            AppendToFile(activityPath, $"[{DateTime.UtcNow:O}] Brownfield task: {taskText.Split('\n')[0].Trim()}\n");
        AppendExecutionEvent(
            executionPath,
            "run_finished",
            "success",
            task: taskText,
            engine: activeEngineName,
            exitCode: result.ExitCode);

        _terminalView?.SetStatus(_s.Get("run.status_task_completed"));
        _ui.WriteInfo(_s.Format("run.done_duration", result.Duration.TotalSeconds));
        WriteRunReport(
            workingDirectory,
            DateTimeOffset.UtcNow - result.Duration,
            true,
            [
                new RunReportTaskEntry
                {
                    Task = taskText,
                    Engine = activeEngineName,
                    DurationSeconds = result.Duration.TotalSeconds,
                    ExitCode = result.ExitCode,
                    Retries = 0,
                    InputTokens = result.TokenUsage?.InputTokens ?? 0,
                    OutputTokens = result.TokenUsage?.OutputTokens ?? 0,
                    TotalTokens = result.TokenUsage?.TotalTokens ?? 0,
                    Status = "success"
                }
            ]);
        return new RunLoopResult { Completed = true };
    }

    private static RunReportTaskEntry BuildReportEntry(string task, string engine, DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc, int exitCode, int retries, int inputTokens, int outputTokens, string status)
    {
        return new RunReportTaskEntry
        {
            Task = task,
            Engine = engine,
            DurationSeconds = (endedAtUtc - startedAtUtc).TotalSeconds,
            ExitCode = exitCode,
            Retries = retries,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            Status = status
        };
    }

    private void WriteRunReport(string workingDirectory, DateTimeOffset startedAtUtc, bool completed, IReadOnlyList<RunReportTaskEntry> entries)
    {
        try
        {
            _reportWriter.Write(workingDirectory, startedAtUtc, DateTimeOffset.UtcNow, completed, entries);
        }
        catch
        {
            // Non-fatal: report generation should not break execution.
        }
    }

    private void WriteSessionTokensSummary(int sessionInputTokens, int sessionOutputTokens)
    {
        if (sessionInputTokens <= 0 && sessionOutputTokens <= 0)
            return;

        var total = sessionInputTokens + sessionOutputTokens;
        _ui.WriteInfo(_s.Format("run.session_tokens", sessionInputTokens, sessionOutputTokens, total));
    }

    private static async Task<bool> RunTestCommandAsync(string workingDirectory, string command, CancellationToken cancellationToken)
    {
        try
        {
            var fileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
            var arguments = OperatingSystem.IsWindows()
                ? $"/c {command}"
                : $"-lc \"{command.Replace("\"", "\\\"")}\"";
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return false;
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static readonly IReadOnlyList<string[]> _codexModelProbeCommands = new[]
    {
        new[] { "--help" },
        new[] { "help" },
        new[] { "models" },
        new[] { "model", "list" },
        new[] { "models", "list" },
        new[] { "--list-models" },
        new[] { "list-models" },
        new[] { "--version" }
    };

    private static string BuildModelAvailabilityCacheKey(ResolvedEngineCommand command, string? model)
    {
        var prefix = command.PrefixArgs.Count == 0
            ? string.Empty
            : string.Join(" ", command.PrefixArgs);
        return $"{command.Executable}||{prefix}||{model}";
    }

    private async Task<bool?> CheckCodexModelAvailabilityAsync(
        string workingDirectory,
        string engineName,
        string? model,
        ResolvedEngineCommand resolvedCommand,
        CancellationToken cancellationToken)
    {
        if (!engineName.Equals("codex", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.IsNullOrWhiteSpace(model))
            return null;

        var key = BuildModelAvailabilityCacheKey(resolvedCommand, model);
        if (_codexModelAvailability.TryGetValue(key, out var cached))
            return cached;

        foreach (var probe in _codexModelProbeCommands)
        {
            var probeOutput = await RunCommandAndCaptureOutputAsync(
                workingDirectory,
                resolvedCommand.Executable,
                resolvedCommand.PrefixArgs,
                probe,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(probeOutput))
                continue;

            if (IsModelUnavailableResponse(probeOutput, model))
            {
                _codexModelAvailability[key] = false;
                return false;
            }

            if (HasModelInOutput(probeOutput, model))
            {
                _codexModelAvailability[key] = true;
                return true;
            }
        }

        _codexModelAvailability[key] = null;
        return null;
    }

    private static bool HasModelInOutput(string output, string model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return false;

        foreach (var line in output.Split('\n'))
        {
            if (line.IndexOf(model, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool IsModelUnavailableResponse(string output, string model)
    {
        if (string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(model))
            return false;

        var candidateLines = output.Replace('\r', '\n').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in candidateLines)
        {
            if (line.IndexOf("model", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (line.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("unknown", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (line.IndexOf(model, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }

        return false;
    }

    private static async Task<string?> RunCommandAndCaptureOutputAsync(
        string workingDirectory,
        string fileName,
        IReadOnlyList<string> prefixArgs,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var token in prefixArgs)
                process.StartInfo.ArgumentList.Add(token);
            foreach (var token in args)
                process.StartInfo.ArgumentList.Add(token);

            if (!process.Start())
                return null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(3000));
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cts.Token);
            var output = await outputTask;
            var error = await errorTask;
            return $"{output}{Environment.NewLine}{error}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static void AppendToFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.AppendAllText(path, content);
    }

    private static void AppendExecutionEvent(
        string path,
        string eventName,
        string reason,
        string? task = null,
        string? engine = null,
        int? taskIndex = null,
        int? totalTasks = null,
        int? attempt = null,
        int? exitCode = null,
        string? details = null)
    {
        var parts = new List<string>
        {
            $"event={eventName}",
            $"reason={reason}"
        };

        if (!string.IsNullOrWhiteSpace(task))
            parts.Add($"task={SanitizeLogValue(task)}");
        if (!string.IsNullOrWhiteSpace(engine))
            parts.Add($"engine={SanitizeLogValue(engine)}");
        if (taskIndex.HasValue)
            parts.Add($"task_index={taskIndex.Value}");
        if (totalTasks.HasValue)
            parts.Add($"total_tasks={totalTasks.Value}");
        if (attempt.HasValue)
            parts.Add($"attempt={attempt.Value}");
        if (exitCode.HasValue)
            parts.Add($"exit_code={exitCode.Value}");
        if (!string.IsNullOrWhiteSpace(details))
            parts.Add($"details={SanitizeLogValue(details, maxLength: 400)}");

        AppendToFile(path, $"[{DateTime.UtcNow:O}] {string.Join(" | ", parts)}{Environment.NewLine}");
    }

    private void DetectUnexpectedShutdown(string statePath, string errorsPath, string executionPath)
    {
        var state = _stateStore.Load(statePath);
        if (!string.Equals(state.RunStatus, "running", StringComparison.OrdinalIgnoreCase))
            return;
        if (state.CurrentTaskIndex == null && string.IsNullOrWhiteSpace(state.CurrentTaskText))
            return;

        state.UnexpectedShutdownDetected = true;
        state.RunStatus = "recovered_after_unexpected_shutdown";
        state.LastExitReason = "unexpected_shutdown_detected";
        state.LastExitAt = DateTimeOffset.UtcNow;
        _stateStore.Save(statePath, state);

        AppendToFile(
            errorsPath,
            $"[{DateTime.UtcNow:O}] unexpected shutdown detected while task {state.CurrentTaskIndex + 1}: {state.CurrentTaskText}{Environment.NewLine}---{Environment.NewLine}");
        AppendExecutionEvent(
            executionPath,
            "run_recovered",
            "unexpected_shutdown_detected",
            task: state.CurrentTaskText,
            taskIndex: state.CurrentTaskIndex + 1,
            details: $"last_heartbeat={state.LastHeartbeat:O}");
    }

    private void MarkRunState(string statePath, string heartbeatPath, DateTimeOffset runStartedAt, string runStatus, string exitReason)
    {
        var state = _stateStore.Load(statePath);
        state.RunStatus = runStatus;
        state.CurrentRunStartedAt = runStartedAt;
        state.LastHeartbeat = DateTimeOffset.UtcNow;
        state.LastExitReason = exitReason;
        state.LastExitAt = null;
        state.UnexpectedShutdownDetected = false;
        _stateStore.Save(statePath, state);
        WriteHeartbeat(heartbeatPath, state, state.CurrentTaskText, state.CurrentTaskIndex, null, state.CurrentEngine, runStatus);
    }

    private void MarkTaskStarted(string statePath, string heartbeatPath, string taskText, int taskIndex, int totalTasks, DateTimeOffset taskStartedAt)
    {
        var state = _stateStore.Load(statePath);
        state.CurrentTaskIndex = taskIndex;
        state.CurrentTaskText = taskText;
        state.CurrentTaskStartedAt = taskStartedAt;
        state.RunStatus = "running";
        state.LastHeartbeat = DateTimeOffset.UtcNow;
        state.LastExitReason = "task_started";
        state.LastExitAt = null;
        _stateStore.Save(statePath, state);
        WriteHeartbeat(heartbeatPath, state, taskText, taskIndex, totalTasks, state.CurrentEngine, "task_started");
    }

    private void MarkTaskEngine(string statePath, string heartbeatPath, string activeEngineName, string taskText, int taskIndex, int totalTasks)
    {
        var state = _stateStore.Load(statePath);
        state.CurrentEngine = activeEngineName;
        state.LastHeartbeat = DateTimeOffset.UtcNow;
        _stateStore.Save(statePath, state);
        WriteHeartbeat(heartbeatPath, state, taskText, taskIndex, totalTasks, activeEngineName, "engine_running");
    }

    private void MarkRunStopped(string statePath, string heartbeatPath, string exitReason)
    {
        var state = _stateStore.Load(statePath);
        state.RunStatus = "stopped";
        state.LastExitReason = exitReason;
        state.LastExitAt = DateTimeOffset.UtcNow;
        state.LastHeartbeat = DateTimeOffset.UtcNow;
        state.CurrentTaskIndex = null;
        state.CurrentTaskText = null;
        state.CurrentEngine = null;
        state.CurrentTaskStartedAt = null;
        _stateStore.Save(statePath, state);
        WriteHeartbeat(heartbeatPath, state, null, null, null, null, exitReason);
    }

    private async Task RunTaskHeartbeatAsync(
        string statePath,
        string heartbeatPath,
        string taskText,
        int taskIndex,
        int totalTasks,
        string engineName,
        DateTimeOffset taskStartedAt,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var state = _stateStore.Load(statePath);
                state.RunStatus = "running";
                state.CurrentTaskIndex = taskIndex;
                state.CurrentTaskText = taskText;
                state.CurrentTaskStartedAt = taskStartedAt;
                state.CurrentEngine = engineName;
                state.LastHeartbeat = DateTimeOffset.UtcNow;
                _stateStore.Save(statePath, state);
                WriteHeartbeat(heartbeatPath, state, taskText, taskIndex, totalTasks, engineName, "heartbeat");
                await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    private static void WriteHeartbeat(
        string heartbeatPath,
        RalphState state,
        string? taskText,
        int? taskIndex,
        int? totalTasks,
        string? engineName,
        string status)
    {
        try
        {
            var dir = Path.GetDirectoryName(heartbeatPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var payload = new HeartbeatDocument
            {
                Status = status,
                RunStatus = state.RunStatus,
                TaskIndex = taskIndex,
                TaskText = taskText,
                TotalTasks = totalTasks,
                Engine = engineName,
                Iteration = state.Iteration,
                LastHeartbeat = state.LastHeartbeat ?? DateTimeOffset.UtcNow,
                CurrentRunStartedAt = state.CurrentRunStartedAt,
                CurrentTaskStartedAt = state.CurrentTaskStartedAt,
                LastExitReason = state.LastExitReason,
                LastExitAt = state.LastExitAt,
                UnexpectedShutdownDetected = state.UnexpectedShutdownDetected
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            File.WriteAllText(heartbeatPath, json);
        }
        catch
        {
            // best effort
        }
    }

    private static string SanitizeLogValue(string value, int maxLength = 240)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (singleLine.Length <= maxLength)
            return singleLine;
        return singleLine[..maxLength] + "...";
    }

    private static void WriteEngineDebugSnapshot(
        bool enabled,
        string workingDirectory,
        int taskIndex,
        int totalTasks,
        int attempt,
        string engineName,
        EngineRequest request,
        EngineResult result)
    {
        if (!enabled)
            return;

        try
        {
            var dir = Path.Combine(workingDirectory, ".ralph", "debug", "engine-json");
            Directory.CreateDirectory(dir);
            var safeEngine = SanitizeFileNameSegment(engineName);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            var file = Path.Combine(dir, $"{stamp}-t{taskIndex:000}-of-{totalTasks:000}-{safeEngine}-a{attempt:00}.jsonl");
            var payload = new
            {
                timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                taskIndex,
                totalTasks,
                attempt,
                engine = engineName,
                exitCode = result.ExitCode,
                durationMs = (int)result.Duration.TotalMilliseconds,
                hadStructuredError = result.HadStructuredError,
                detectedErrors = result.DetectedErrors?.Select(e => e.ToString()).ToArray() ?? Array.Empty<string>(),
                tokenUsage = result.TokenUsage is null ? null : new
                {
                    inputTokens = result.TokenUsage.InputTokens,
                    outputTokens = result.TokenUsage.OutputTokens,
                    totalTokens = result.TokenUsage.TotalTokens,
                    contextUsedPercent = result.TokenUsage.ContextUsedPercent
                },
                command = new
                {
                    executable = request.CommandOverride,
                    prefixArgs = request.CommandPrefixArgs,
                    resolutionSource = request.CommandResolutionSource,
                    model = request.ModelOverride,
                    extraArgs = request.ExtraArgsPassthrough
                },
                stdout = result.Stdout,
                stderr = result.Stderr
            };
            var json = JsonSerializer.Serialize(payload);
            File.WriteAllText(file, json + Environment.NewLine);
        }
        catch
        {
            // Debug snapshot must never break run flow.
        }
    }

    private static string SanitizeFileNameSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private static WorkspaceSnapshot CaptureWorkspaceSnapshot(string workingDirectory)
    {
        using var sha = SHA256.Create();
        foreach (var file in Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(workingDirectory, file);
            if (IsIgnoredPath(rel))
                continue;
            var info = new FileInfo(file);
            var line = $"{rel}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n";
            var bytes = System.Text.Encoding.UTF8.GetBytes(line);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return new WorkspaceSnapshot(Convert.ToHexString(sha.Hash!));
    }

    private static bool IsIgnoredPath(string relativePath)
    {
        var p = relativePath.Replace('\\', '/');
        return p.StartsWith(".ralph/", StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(".git/", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildEffectiveCommandForLogs(EngineRequest request)
    {
        var command = request.CommandOverride ?? "(default)";
        var prefix = request.CommandPrefixArgs != null && request.CommandPrefixArgs.Count > 0
            ? " " + string.Join(" ", request.CommandPrefixArgs)
            : string.Empty;
        return command + prefix;
    }

    private static string BuildEffectiveArgsForLogs(EngineRequest request)
    {
        var args = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.ModelOverride))
        {
            args.Add("--model");
            args.Add(request.ModelOverride!);
        }

        if (request.ExtraArgsPassthrough != null && request.ExtraArgsPassthrough.Count > 0)
            args.AddRange(request.ExtraArgsPassthrough);

        if (!string.IsNullOrWhiteSpace(request.TaskText))
            args.Add("<task_text>");

        return args.Count == 0 ? "(none)" : string.Join(" ", args);
    }

    private static IReadOnlyList<string>? BuildExtraArgs(string engineName, int? maxTokens, double? temperature, IReadOnlyList<string>? passthrough)
    {
        if (!maxTokens.HasValue && !temperature.HasValue && (passthrough == null || passthrough.Count == 0))
            return null;
        var args = new List<string>();
        var supportsNormalizedSamplingArgs = !engineName.Equals("codex", StringComparison.OrdinalIgnoreCase);
        if (supportsNormalizedSamplingArgs)
        {
            if (maxTokens.HasValue)
            {
                args.Add("--max-tokens");
                args.Add(maxTokens.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (temperature.HasValue)
            {
                args.Add("--temperature");
                args.Add(temperature.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }
        if (passthrough != null)
            args.AddRange(passthrough);
        return args;
    }

    private static List<string> BuildEngineCandidates(string primary, RalphConfig config)
    {
        var ordered = new List<string>();
        AddDistinct(ordered, primary);
        if (config.FallbackEngines != null)
        {
            foreach (var fallback in config.FallbackEngines)
                AddDistinct(ordered, fallback);
        }
        return ordered;
    }

    private static void AddDistinct(List<string> list, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
            list.Add(value);
    }

    private static bool ShouldTryFallbackEngine(EngineResult result, int currentEngineIndex, IReadOnlyList<string> candidates)
    {
        if (currentEngineIndex >= candidates.Count - 1)
            return false;
        if (result.DetectedErrors == null || result.DetectedErrors.Count == 0)
            return false;
        return result.DetectedErrors.Contains(DetectedErrorKind.CommandNotFound)
               || result.DetectedErrors.Contains(DetectedErrorKind.RateLimit);
    }

    private static bool IsTokenThresholdReached(int? maxTotalTokensPerRun, int inputTokens, int outputTokens)
    {
        if (!maxTotalTokensPerRun.HasValue || maxTotalTokensPerRun.Value <= 0)
            return false;
        return (inputTokens + outputTokens) >= maxTotalTokensPerRun.Value;
    }

    private static bool ResolveFastMode(string engineName, string? modelOverride, bool fastRequested)
    {
        if (!fastRequested)
            return false;

        if (!engineName.Equals("codex", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(modelOverride))
            return true;

        return modelOverride.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPrdExecutionContext(
        PrdDocument doc,
        string? guardrails,
        string taskText,
        PromptContextMode promptContextMode)
    {
        return promptContextMode switch
        {
            PromptContextMode.WiggumFullPrd => PromptBuilder.BuildWiggumContext(
                guardrails,
                doc.GetBodyWithoutFrontmatter(),
                taskText),
            _ => PromptBuilder.BuildLoopContext(
                guardrails,
                doc.GetSharedContext(),
                taskText)
        };
    }

    private static ContextSignal DetectContextSignal(string? stdout, string? stderr)
    {
        var combined = $"{stdout}\n{stderr}";
        if (combined.Contains("GUTTER", StringComparison.OrdinalIgnoreCase))
            return ContextSignal.Gutter;
        if (combined.Contains("DEFER", StringComparison.OrdinalIgnoreCase))
            return ContextSignal.Defer;
        if (combined.Contains("ROTATE", StringComparison.OrdinalIgnoreCase))
            return ContextSignal.Rotate;
        if (combined.Contains("WARN", StringComparison.OrdinalIgnoreCase))
            return ContextSignal.Warn;
        return ContextSignal.None;
    }

    private static ContextRotationOutcome EvaluateContextRotation(ContextRotationConfigEntry cfg, ContextSignal signal, bool thresholdReached)
    {
        if (signal == ContextSignal.Gutter)
            return ContextRotationOutcome.Gutter;
        if (signal == ContextSignal.Defer)
            return ContextRotationOutcome.Defer;
        if (signal == ContextSignal.Rotate)
            return ContextRotationOutcome.Rotate;
        if (signal == ContextSignal.Warn)
            return ContextRotationOutcome.Warn;

        if (!thresholdReached)
            return ContextRotationOutcome.None;

        var mode = (cfg.OnSignal ?? "warn").Trim().ToLowerInvariant();
        return mode switch
        {
            "gutter" => ContextRotationOutcome.Gutter,
            "defer" => ContextRotationOutcome.Defer,
            "rotate" => ContextRotationOutcome.Rotate,
            _ => ContextRotationOutcome.Warn
        };
    }

    private TaskGitSnapshot? CaptureTaskSnapshot(string workingDirectory, bool autoRollback)
    {
        if (!autoRollback || !_git.IsGitRepo(workingDirectory))
            return null;
        return new TaskGitSnapshot(_git.GetUntrackedFiles(workingDirectory).ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private void AttemptRollbackIfEnabled(string workingDirectory, TaskGitSnapshot? snapshot, string taskText, string reason, string errorsPath)
    {
        if (snapshot == null)
            return;

        var restored = _git.RestoreTrackedFiles(workingDirectory);
        var currentUntracked = _git.GetUntrackedFiles(workingDirectory);
        var removed = 0;
        foreach (var rel in currentUntracked)
        {
            if (snapshot.PreUntrackedFiles.Contains(rel))
                continue;
            var fullPath = Path.Combine(workingDirectory, rel);
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    removed++;
                }
            }
            catch
            {
                // best effort
            }
        }

        var audit = $"[{DateTime.UtcNow:O}] rollback task='{taskText}' reason='{reason}' restoredTracked={restored} removedUntracked={removed}\n";
        AppendToFile(errorsPath, audit);
    }

    private async Task RunEngineSpinnerAsync(string engineName, CancellationToken cancellationToken)
    {
        if (_terminalView == null)
            return;

        var frames = new[] { "|", "/", "-", "\\" };
        var idx = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _terminalView.SetStatus(_s.Format("run.status_spinner", engineName, frames[idx++ % frames.Length]));
                await Task.Delay(250, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on cancellation
        }
    }

    /// <summary>
    /// Returns the last <paramref name="max"/> non-empty, non-separator lines from engine
    /// output. Taking from the END is reliable because engines emit metadata headers first
    /// and the actual error reason last.
    /// </summary>
    private static IEnumerable<string> FilterMeaningfulLines(string text, int max = 3)
    {
        return text.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0 && !l.All(c => c == '-' || c == '='))
            .TakeLast(max);
    }

    private static NoChangePolicy ParseNoChangePolicy(string? raw)
    {
        var normalized = raw?.Trim().ToLowerInvariant();
        return normalized switch
        {
            "retry" => NoChangePolicy.Retry,
            "fail-fast" => NoChangePolicy.FailFast,
            _ => NoChangePolicy.Fallback
        };
    }

    private static NoChangeAction ResolveNoChangeAction(
        NoChangePolicy policy,
        int currentAttempt,
        int maxAttempts,
        bool stopOnMaxAttempts,
        int currentEngineIndex,
        int totalEngines)
    {
        if (policy == NoChangePolicy.Retry)
            return currentAttempt + 1 < maxAttempts
                ? NoChangeAction.Retry
                : (stopOnMaxAttempts ? NoChangeAction.FailFast : NoChangeAction.Continue);

        if (policy == NoChangePolicy.FailFast)
            return NoChangeAction.FailFast;

        if (currentEngineIndex < totalEngines - 1)
            return NoChangeAction.Fallback;

        return stopOnMaxAttempts ? NoChangeAction.FailFast : NoChangeAction.Continue;
    }
}

public sealed class RunLoopResult
{
    public bool Completed { get; init; }
    public bool Gutter { get; init; }
}

internal readonly record struct WorkspaceSnapshot(string Hash);
internal sealed record TaskGitSnapshot(HashSet<string> PreUntrackedFiles);

internal enum ContextSignal
{
    None,
    Warn,
    Rotate,
    Defer,
    Gutter
}

internal enum ContextRotationOutcome
{
    None,
    Warn,
    Rotate,
    Defer,
    Gutter
}

internal enum NoChangePolicy
{
    Fallback,
    Retry,
    FailFast
}

internal enum NoChangeAction
{
    Continue,
    Fallback,
    Retry,
    FailFast
}

internal sealed class HeartbeatDocument
{
    public string? Status { get; set; }
    public string? RunStatus { get; set; }
    public int? TaskIndex { get; set; }
    public string? TaskText { get; set; }
    public int? TotalTasks { get; set; }
    public string? Engine { get; set; }
    public int Iteration { get; set; }
    public DateTimeOffset LastHeartbeat { get; set; }
    public DateTimeOffset? CurrentRunStartedAt { get; set; }
    public DateTimeOffset? CurrentTaskStartedAt { get; set; }
    public string? LastExitReason { get; set; }
    public DateTimeOffset? LastExitAt { get; set; }
    public bool UnexpectedShutdownDetected { get; set; }
}
