using Ralph.Cli.Commands;
using Ralph.Core.Config;
using Ralph.Core.Localization;
using Ralph.Core.RunLoop;
using Ralph.Engines.Agent;
using Ralph.Engines.Cursor;
using Ralph.Engines.Registry;
using Ralph.Persistence.Config;
using Ralph.Persistence.State;
using Ralph.Persistence.Workspace;
using Ralph.UI.Abstractions;
using Ralph.UI.Console;
using Ralph.UI.Tui;

// ── Version ───────────────────────────────────────────────────────────────────
var asm = System.Reflection.Assembly.GetExecutingAssembly();
var verAttr = (System.Reflection.AssemblyInformationalVersionAttribute?)
    System.Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
var version = verAttr?.InformationalVersion ?? "unknown";

var argsList = args.Length > 0 ? args.ToList() : new List<string> { "--help" };
Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

if (argsList.Contains("--version") || argsList.Contains("-V"))
{
    Console.WriteLine($"ralph {version}");
    return 0;
}

// ── Global config + i18n ──────────────────────────────────────────────────────
var globalConfig = GlobalConfig.Load();
var s = StringCatalog.Load(globalConfig.Lang);
var cancellation = new CancellationTokenSource();
var cancelRequested = 0;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    if (Interlocked.Exchange(ref cancelRequested, 1) == 0)
    {
        Console.Error.WriteLine(s.Get("run.cancel_requested"));
        cancellation.Cancel();
        return;
    }

    Console.Error.WriteLine(s.Get("run.force_exit_requested"));
    Environment.Exit(130);
};

// ── Parse args ────────────────────────────────────────────────────────────────
var uiMode = globalConfig.EffectiveUi();

var cwd = Directory.GetCurrentDirectory();

string? command = null;
var skipTests = false;
var force = false;
var yes = false;
var nonInteractive = false;
var doctorProcesses = false;
var engine = (string?)null;
var model = (string?)null;
var maxRetries = (int?)null;
var retryDelaySeconds = (int?)null;
var maxIterations = (int?)null;
var maxParallel = (int?)null;
var maxTokens = (int?)null;
var temperature = (double?)null;
var followLogs = false;
var logsLevel = (string?)null;
var logsSince = (string?)null;
var prdOverride = (string?)null;
var githubRepo = (string?)null;
var githubLabel = (string?)null;
var githubState = (string?)null;
var outputOverride = (string?)null;
var dryRun = false;
var verbose = false;
var skipLint = false;
var branchPerTask = false;
var baseBranch = (string?)null;
var createPr = false;
var draftPr = false;
var retryFailed = false;
var autoRollback = false;
var debugEngineJson = false;
var ignoreContextStops = true;
var noChangePolicyOverride = (string?)null;
var noChangeMaxAttemptsOverride = (int?)null;
var noChangeStopOnMaxAttemptsOverride = (bool?)null;
var parallelIntegration = (string?)null;
var positionals = new List<string>();
var passthroughArgs = new List<string>();
var i = 0;
while (i < argsList.Count)
{
    var a = argsList[i];
    if (a == "--")
    {
        passthroughArgs.AddRange(argsList.Skip(i + 1));
        break;
    }
    if (a == "--egine")
    {
        Console.Error.WriteLine(s.Format("error.invalid_flag_typo", "--egine", "--engine"));
        return 1;
    }
    if (a == "--ui" && i + 1 < argsList.Count) { uiMode = argsList[i + 1]; i += 2; continue; }
    if (a == "init")     { command = "init";     i++; continue; }
    if (a == "setup")    { command = "init";     i++; continue; }
    if (a == "run")      { command = "run";      i++; continue; }
    if (a == "loop")     { command = "run";      i++; continue; }
    if (a == "once")     { command = "once";     i++; continue; }
    if (a == "parallel") { command = "parallel"; i++; continue; }
    if (a == "install")  { command = "install";  i++; continue; }
    if (a == "config")   { command = "config";   i++; continue; }
    if (a == "tasks")    { command = "tasks";    i++; continue; }
    if (a == "logs")     { command = "logs";     i++; continue; }
    if (a == "doctor")   { command = "doctor";   i++; continue; }
    if (a == "clean")    { command = "clean";    i++; continue; }
    if (a == "rules")    { command = "rules";    i++; continue; }
    if (a == "lang")     { command = "lang";     i++; continue; }
    if (a == "ui")       { command = "ui";       i++; continue; }
    if (a == "about")    { command = "about";    i++; continue; }
    if (a == "update")   { command = "update";   i++; continue; }
    if (a == "report")   { command = "report";   i++; continue; }
    if (a == "--skip-tests") { skipTests = true; i++; continue; }
    if (a == "--no-lint" || a == "--skip-lint") { skipLint = true; i++; continue; }
    if (a == "--fast")   { skipTests = true; skipLint = true; i++; continue; }
    if (a == "--dry-run") { dryRun = true; i++; continue; }
    if (a == "--retry-failed") { retryFailed = true; i++; continue; }
    if (a == "--parallel-integration" && i + 1 < argsList.Count) { parallelIntegration = argsList[i + 1]; i += 2; continue; }
    if (a == "--auto-rollback") { autoRollback = true; i++; continue; }
    if (a == "--debug-engine-json") { debugEngineJson = true; i++; continue; }
    if (a == "--ignore-context-stops") { ignoreContextStops = true; i++; continue; }
    if (a == "--respect-context-stops") { ignoreContextStops = false; i++; continue; }
    if (a == "--ignore-gutter") { ignoreContextStops = true; i++; continue; } // backward-compatible alias
    if (a == "--respect-gutter") { ignoreContextStops = false; i++; continue; } // backward-compatible alias
    if (a == "--no-change-policy" && i + 1 < argsList.Count)
    {
        var policy = argsList[i + 1].Trim().ToLowerInvariant();
        if (policy is not ("fallback" or "retry" or "fail-fast"))
        {
            Console.Error.WriteLine(s.Get("config.invalid_no_change_policy"));
            return 1;
        }
        noChangePolicyOverride = policy;
        i += 2;
        continue;
    }
    if (a == "--no-change-max-retries" && i + 1 < argsList.Count)
    {
        if (!int.TryParse(argsList[i + 1], out var attempts))
        {
            Console.Error.WriteLine(s.Get("config.invalid_int"));
            return 1;
        }
        noChangeMaxAttemptsOverride = Math.Max(1, attempts);
        i += 2;
        continue;
    }
    if (a == "--no-change-stop-on-max-retries") { noChangeStopOnMaxAttemptsOverride = true; i++; continue; }
    if (a == "--no-change-continue-on-max-retries") { noChangeStopOnMaxAttemptsOverride = false; i++; continue; }
    if (a == "--verbose" || a == "-v") { verbose = true; i++; continue; }
    if (a == "--branch-per-task") { branchPerTask = true; i++; continue; }
    if (a == "--base-branch" && i + 1 < argsList.Count) { baseBranch = argsList[i + 1]; i += 2; continue; }
    if (a == "--create-pr") { createPr = true; i++; continue; }
    if (a == "--draft-pr")  { draftPr = true; createPr = true; i++; continue; }
    if (a == "--force")  { force = true; i++; continue; }
    if (a == "--yes" || a == "-y") { yes = true; i++; continue; }
    if (a == "--non-interactive") { nonInteractive = true; i++; continue; }
    if (a == "--processes") { doctorProcesses = true; i++; continue; }
    if (a == "--sonnet") { engine ??= "claude"; model = "claude-sonnet-4-6"; i++; continue; }
    if (a == "--opus")   { engine ??= "claude"; model = "claude-opus-4-6";   i++; continue; }
    if (a == "--haiku")  { engine ??= "claude"; model = "claude-haiku-4-5";  i++; continue; }
    if (a == "--engine" && i + 1 < argsList.Count) { engine = argsList[i + 1]; i += 2; continue; }
    if (a == "--model"  && i + 1 < argsList.Count) { model  = argsList[i + 1]; i += 2; continue; }
    if (a == "--prd"    && i + 1 < argsList.Count) { prdOverride = argsList[i + 1]; i += 2; continue; }
    if (a == "--repo"   && i + 1 < argsList.Count) { githubRepo  = argsList[i + 1]; i += 2; continue; }
    if (a == "--label"  && i + 1 < argsList.Count) { githubLabel = argsList[i + 1]; i += 2; continue; }
    if (a == "--state"  && i + 1 < argsList.Count) { githubState = argsList[i + 1]; i += 2; continue; }
    if (a == "--output" && i + 1 < argsList.Count) { outputOverride = argsList[i + 1]; i += 2; continue; }
    if (a == "--max-retries"    && i + 1 < argsList.Count) { int.TryParse(argsList[i + 1], out var mr); maxRetries = mr; i += 2; continue; }
    if (a == "--retry-delay"    && i + 1 < argsList.Count) { int.TryParse(argsList[i + 1], out var rd); retryDelaySeconds = rd; i += 2; continue; }
    if (a == "--max-iterations" && i + 1 < argsList.Count) { int.TryParse(argsList[i + 1], out var mi); maxIterations = mi; i += 2; continue; }
    if (a == "--max-parallel"   && i + 1 < argsList.Count) { int.TryParse(argsList[i + 1], out var mp); maxParallel = mp; i += 2; continue; }
    if (a == "--max-tokens"     && i + 1 < argsList.Count) { int.TryParse(argsList[i + 1], out var mt); maxTokens = mt; i += 2; continue; }
    if (a == "--temperature"    && i + 1 < argsList.Count) { double.TryParse(argsList[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t); temperature = t; i += 2; continue; }
    if (a == "--follow") { followLogs = true; i++; continue; }
    if (a == "--level"  && i + 1 < argsList.Count) { logsLevel = argsList[i + 1]; i += 2; continue; }
    if (a == "--since"  && i + 1 < argsList.Count) { logsSince = argsList[i + 1]; i += 2; continue; }
    if (a == "--help" || a == "-h")
    {
        PrintHelp(s);
        return 0;
    }
    if (a.StartsWith("--", StringComparison.Ordinal))
    {
        Console.Error.WriteLine(s.Format("error.unknown_option", a));
        return 1;
    }
    positionals.Add(a);
    i++;
}

// ── UI layer ──────────────────────────────────────────────────────────────────
IUserInteraction ui = new ConsoleInteraction();
ITerminalView terminalView = new ConsoleTerminalView();
TuiDashboard? tuiDashboard = null;
var disableRichUi = nonInteractive || Console.IsOutputRedirected || Console.IsErrorRedirected;

// TUI is only used for "run" and "once" commands — it needs a long-running
// command to display. Everything else gets Spectre.
var tuiForRun = !disableRichUi && uiMode == "tui" && command is "run" or "once";

if (uiMode == "none" || disableRichUi)
{
    // Safe fallback must remain observable; never silence execution.
    ui = new ConsoleInteraction();
    terminalView = new ConsoleTerminalView();
}
else if (tuiForRun)
{
    // Set up TUI interaction wrappers. The actual Terminal.Gui Application
    // will be started on the MAIN THREAD via RunBlocking() when the command
    // is ready to execute — this avoids the Windows console-handle issue that
    // occurs when Terminal.Gui runs on a background thread.
    TuiDashboard? dashboardRef = null;
    var fallbackInteraction = new ConsoleInteraction();
    var fallbackTerminal    = new ConsoleTerminalView();
    var tuiInteraction  = new TuiInteraction(fallbackInteraction, () => dashboardRef?.IsHealthy == true);
    var tuiTerminalView = new TuiTerminalView(fallbackTerminal,   () => dashboardRef?.IsHealthy == true);
    tuiDashboard = new TuiDashboard(tuiInteraction, tuiTerminalView, s);
    dashboardRef = tuiDashboard;
    ui          = tuiInteraction;
    terminalView = tuiTerminalView;
}
else
{
    try
    {
        ui = new Ralph.UI.Spectre.SpectreInteraction(ui);
        terminalView = new Ralph.UI.Spectre.SpectreTerminalView();
    }
    catch
    {
        // Fallback: keep plain console
    }

    if (uiMode is "gum" or "spectre+gum")
    {
        try
        {
            ui = new Ralph.UI.Gum.GumInteraction(ui);
        }
        catch
        {
            // keep current ui
        }
    }
}

// ── Core services ─────────────────────────────────────────────────────────────
var registry = new EngineRegistry();
registry.Register(new CursorEngine());
registry.Register(new CodexEngine());
registry.Register(new ClaudeEngine());
registry.Register(new OpenCodeEngine());
registry.Register(new QwenEngine());
registry.Register(new DroidEngine());
registry.Register(new CopilotEngine());
registry.Register(new GeminiEngine());
var stateStore = new StateStore();
var workspaceInit = new WorkspaceInitializer();
var retryPolicy = new RetryPolicy();
var gutterDetector = new GutterDetector();
var runLoop = new RunLoopService(registry, stateStore, workspaceInit, ui, terminalView, retryPolicy, gutterDetector, strings: s);

if (tuiDashboard != null)
{
    runLoop.OnTokensUpdated = (inputTotal, outputTotal, latest) =>
    {
        tuiDashboard.SessionInputTokens  = inputTotal;
        tuiDashboard.SessionOutputTokens = outputTotal;
        tuiDashboard.LatestTokenUsage    = latest;
    };
}

// ── Commands ──────────────────────────────────────────────────────────────────
var initCmd        = new InitCommand(workspaceInit);
var runCmd         = new RunCommand(runLoop);
var onceCmd        = new OnceCommand(runLoop);
var parallelCmd    = new ParallelCommand(runLoop, workspaceInit, new ConfigStore());
var installCmd     = new InstallCommand();
var configCmd      = new ConfigCommand(workspaceInit, new ConfigStore());
var tasksCmd       = new TasksCommand();
var logsCmd        = new LogsCommand(workspaceInit);
var doctorCmd      = new DoctorCommand(workspaceInit, new ConfigStore());
var cleanCmd       = new CleanCommand(workspaceInit);
var githubSyncCmd  = new GitHubIssuesSyncCommand();
var rulesCmd       = new RulesCommand(workspaceInit);
var langCmd        = new LangCommand();
var uiCmd          = new UiCommand();
var aboutCmd       = new AboutCommand();
var updateCmd      = new UpdateCommand();
var reportCmd      = new ReportCommand(workspaceInit);
var prdPath        = ResolvePrdPath(cwd, prdOverride);

if (command == null)
{
    Console.Error.WriteLine(s.Get("error.usage"));
    return 1;
}

try
{
    if (command == "init")
        return initCmd.Execute(cwd, force, s);

    if (command == "install")
    {
        var installDir = positionals.Count > 0 ? positionals[0] : null;
        return installCmd.Execute(installDir);
    }

    if (command == "lang")
    {
        var sub = positionals.Count > 0 ? positionals[0] : "current";
        var arg = positionals.Count > 1 ? positionals[1] : null;
        return langCmd.Execute(sub, arg, s);
    }

    if (command == "ui")
    {
        var sub = positionals.Count > 0 ? positionals[0] : "current";
        var arg = positionals.Count > 1 ? positionals[1] : null;
        return uiCmd.Execute(sub, arg, s);
    }

    if (command == "about")
        return aboutCmd.Execute(version, s);

    if (command == "update")
        return await updateCmd.ExecuteAsync(version, s);

    if (command == "report")
    {
        var sub = positionals.Count > 0 ? positionals[0] : "last";
        return reportCmd.Execute(cwd, sub, s);
    }

    if (command == "config")
    {
        var sub = positionals.Count > 0 ? positionals[0] : "list";
        var key = positionals.Count > 1 ? positionals[1] : null;
        var val = positionals.Count > 2 ? positionals[2] : null;
        return configCmd.Execute(cwd, sub, key, val, s);
    }

    if (command == "tasks")
    {
        var sub = positionals.Count > 0 ? positionals[0] : "list";
        if (sub.Equals("sync", StringComparison.OrdinalIgnoreCase))
            return await githubSyncCmd.ExecuteAsync(cwd, githubRepo, githubLabel, githubState, outputOverride, s, force);
        var arg = positionals.Count > 1 ? positionals[1] : null;
        return tasksCmd.Execute(prdPath, sub, arg, s);
    }

    if (command == "logs")
    {
        var sub = positionals.Count > 0 ? positionals[0] : "tail";
        return await logsCmd.ExecuteAsync(cwd, sub, followLogs, logsLevel, logsSince, s);
    }

    if (command == "doctor")
        return doctorCmd.Execute(cwd, s, autoApprove: yes, nonInteractive: nonInteractive, showProcesses: doctorProcesses);

    if (command == "clean")
        return cleanCmd.Execute(cwd, force, s);

    if (command == "rules")
    {
        var sub = positionals.Count > 0 ? positionals[0] : "list";
        var arg = positionals.Count > 1 ? positionals[1] : null;
        return rulesCmd.Execute(cwd, sub, arg, force, s);
    }

    if (command == "parallel")
    {
        var limit = maxParallel ?? 2;
        return await parallelCmd.ExecuteAsync(
            cwd,
            prdPath,
            limit,
            engine,
            model,
            maxTokens,
            temperature,
            passthroughArgs,
            verbose,
            dryRun,
            retryFailed,
            parallelIntegration,
            s,
            positionals,
            cancellation.Token);
    }

    if (command == "run" || command == "once")
    {
        if (maxRetries.HasValue)        retryPolicy.MaxRetries  = maxRetries.Value;
        if (retryDelaySeconds.HasValue) retryPolicy.RetryDelay  = TimeSpan.FromSeconds(retryDelaySeconds.Value);

        var inlineTask = command == "once" && positionals.Count > 0 ? positionals[0] : null;

        // Validate PRD paths before starting TUI
        if (command == "run" && !File.Exists(prdPath))
        {
            Console.Error.WriteLine(s.Format("error.prd_not_found", prdPath));
            return 1;
        }
        if (command == "once" && inlineTask == null && !File.Exists(prdPath))
        {
            Console.Error.WriteLine(s.Format("error.prd_not_found_hint", prdPath));
            return 1;
        }

        if (tuiDashboard != null)
        {
            // TUI path: start the command on the thread pool so its synchronous
            // startup code does NOT write to the console before Terminal.Gui takes
            // over. Then run Terminal.Gui blocking on the MAIN THREAD.
            var cmdTask = command == "run"
                ? Task.Run(() => runCmd.ExecuteAsync(cwd, prdPath, skipTests, skipLint, engine, model, maxTokens, temperature, passthroughArgs, maxIterations: maxIterations, dryRun: dryRun, verbose: verbose, branchPerTask: branchPerTask, baseBranch: baseBranch, createPr: createPr, draftPr: draftPr, autoRollback: autoRollback, debugEngineJson: debugEngineJson, ignoreContextStops: ignoreContextStops, noChangePolicyOverride: noChangePolicyOverride, noChangeMaxAttemptsOverride: noChangeMaxAttemptsOverride, noChangeStopOnMaxAttemptsOverride: noChangeStopOnMaxAttemptsOverride, cancellationToken: cancellation.Token))
                : Task.Run(() => onceCmd.ExecuteAsync(cwd, prdPath, skipTests, skipLint, engine, model, maxTokens, temperature, passthroughArgs, dryRun: dryRun, verbose: verbose, taskOverride: inlineTask, branchPerTask: branchPerTask, baseBranch: baseBranch, createPr: createPr, draftPr: draftPr, autoRollback: autoRollback, debugEngineJson: debugEngineJson, ignoreContextStops: ignoreContextStops, noChangePolicyOverride: noChangePolicyOverride, noChangeMaxAttemptsOverride: noChangeMaxAttemptsOverride, noChangeStopOnMaxAttemptsOverride: noChangeStopOnMaxAttemptsOverride, cancellationToken: cancellation.Token));

            tuiDashboard.MonitoredTask = cmdTask;
            try
            {
                tuiDashboard.RunBlocking();
            }
            catch (Exception tuiEx)
            {
                // TUI failed mid-run; fall through and await the command task normally.
                Console.Error.WriteLine(s.Format("ui.tui_runtime_error", tuiEx.Message));
                Console.Error.WriteLine(s.Format("ui.tui_debug_log", workspaceInit.GetTuiDebugLogPath(cwd)));
            }
            return await cmdTask;
        }

        // Non-TUI path: direct async execution.
        if (command == "run")
            return await runCmd.ExecuteAsync(cwd, prdPath, skipTests, skipLint, engine, model, maxTokens, temperature, passthroughArgs, maxIterations: maxIterations, dryRun: dryRun, verbose: verbose, branchPerTask: branchPerTask, baseBranch: baseBranch, createPr: createPr, draftPr: draftPr, autoRollback: autoRollback, debugEngineJson: debugEngineJson, ignoreContextStops: ignoreContextStops, noChangePolicyOverride: noChangePolicyOverride, noChangeMaxAttemptsOverride: noChangeMaxAttemptsOverride, noChangeStopOnMaxAttemptsOverride: noChangeStopOnMaxAttemptsOverride, cancellationToken: cancellation.Token);

        if (inlineTask != null)
            return await onceCmd.ExecuteAsync(cwd, prdPath, skipTests, skipLint, engine, model, maxTokens, temperature, passthroughArgs, dryRun: dryRun, verbose: verbose, taskOverride: inlineTask, branchPerTask: branchPerTask, baseBranch: baseBranch, createPr: createPr, draftPr: draftPr, autoRollback: autoRollback, debugEngineJson: debugEngineJson, ignoreContextStops: ignoreContextStops, noChangePolicyOverride: noChangePolicyOverride, noChangeMaxAttemptsOverride: noChangeMaxAttemptsOverride, noChangeStopOnMaxAttemptsOverride: noChangeStopOnMaxAttemptsOverride, cancellationToken: cancellation.Token);

        return await onceCmd.ExecuteAsync(cwd, prdPath, skipTests, skipLint, engine, model, maxTokens, temperature, passthroughArgs, dryRun: dryRun, verbose: verbose, branchPerTask: branchPerTask, baseBranch: baseBranch, createPr: createPr, draftPr: draftPr, autoRollback: autoRollback, debugEngineJson: debugEngineJson, ignoreContextStops: ignoreContextStops, noChangePolicyOverride: noChangePolicyOverride, noChangeMaxAttemptsOverride: noChangeMaxAttemptsOverride, noChangeStopOnMaxAttemptsOverride: noChangeStopOnMaxAttemptsOverride, cancellationToken: cancellation.Token);
    }
}
catch (OperationCanceledException)
{
    TryAppendExecutionLog(workspaceInit, cwd, command, "process_canceled", "ctrl_c");
    Console.Error.WriteLine(s.Get("run.operation_canceled"));
    return 130;
}
catch (Exception ex)
{
    TryAppendExecutionLog(workspaceInit, cwd, command, "process_failed", "unhandled_exception", ex.Message);
    Console.Error.WriteLine(s.Format("error.unhandled", ex.Message));
    return 1;
}
finally
{
    tuiDashboard?.Dispose();
}

return 0;

// ── Local functions ───────────────────────────────────────────────────────────

static string ResolvePrdPath(string cwd, string? prdOverride)
{
    if (!string.IsNullOrWhiteSpace(prdOverride))
    {
        var path = prdOverride!;
        if (!Path.IsPathRooted(path))
            path = Path.Combine(cwd, path);
        return path;
    }
    var md   = Path.Combine(cwd, "PRD.md");   if (File.Exists(md))   return md;
    var yaml = Path.Combine(cwd, "PRD.yaml"); if (File.Exists(yaml)) return yaml;
    var yml  = Path.Combine(cwd, "PRD.yml");  if (File.Exists(yml))  return yml;
    return md;
}

static void PrintHelp(IStringCatalog s)
{
    Console.WriteLine(s.Get("help.header"));
    Console.WriteLine(s.Get("help.full"));
}

static void TryAppendExecutionLog(WorkspaceInitializer workspaceInit, string cwd, string? command, string eventName, string reason, string? details = null)
{
    if (command is not ("run" or "once" or "parallel"))
        return;

    try
    {
        var path = workspaceInit.GetExecutionLogPath(cwd);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var parts = new List<string>
        {
            $"event={eventName}",
            $"reason={reason}",
            $"command={command}"
        };

        if (!string.IsNullOrWhiteSpace(details))
            parts.Add($"details={SanitizeLogValue(details)}");

        File.AppendAllText(path, $"[{DateTime.UtcNow:O}] {string.Join(" | ", parts)}{Environment.NewLine}");
    }
    catch
    {
        // Do not mask the original exception/cancellation.
    }
}

static string SanitizeLogValue(string value) =>
    value.Replace('\r', ' ').Replace('\n', ' ').Trim();
