using Ralph.Cli.Commands;
using Ralph.Cli.Infrastructure;
using System.Diagnostics;
using Ralph.Core.Config;
using Ralph.Core.Localization;
using Ralph.Core.RunLoop;
using Ralph.Engines.Agent;
using Ralph.Engines.Cursor;
using Ralph.Engines.Registry;
using Ralph.Persistence.Config;
using Ralph.Persistence.State;
using Ralph.Persistence.Workspace;
using Ralph.Tasks.Prd;
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
var skipTests = true;
var runMode = RunCommandMode.Loop;
var runModeWasSet = false;
var runModeSetBy = (string?)null;
var force = false;
var runPersistent = true;
var workerRun = false;
var noCommit = false;
var fast = false;
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
var runTestsWasSet = false;
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
    if (a == "--ui")
    {
        Console.Error.WriteLine(s.Format("error.missing_value", "--ui"));
        return 1;
    }
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
    if (a == "--skip-tests")
    {
        if (runTestsWasSet && skipTests == false)
        {
            Console.Error.WriteLine(s.Get("run.test_mode_conflict"));
            return 1;
        }
        skipTests = true;
        runTestsWasSet = true;
        i++;
        continue;
    }
    if (a == "--run-tests")
    {
        if (runTestsWasSet && skipTests == true)
        {
            Console.Error.WriteLine(s.Get("run.test_mode_conflict"));
            return 1;
        }
        skipTests = false;
        runTestsWasSet = true;
        i++;
        continue;
    }
    if (a == "--fast")   { fast = true; i++; continue; }
    if (a == "--worker-run") { workerRun = true; i++; continue; }
    if (a == "--fail-fast") { runPersistent = false; i++; continue; }
    if (a == "--mode" && i + 1 < argsList.Count)
    {
        var value = argsList[i + 1].Trim().ToLowerInvariant();
        if (value is not ("loop" or "wiggum"))
        {
            Console.Error.WriteLine(s.Format("run.invalid_mode", value));
            return 1;
        }
        var modeValue = value == "loop" ? RunCommandMode.Loop : RunCommandMode.Wiggum;
        if (runModeWasSet && runMode != modeValue)
        {
            Console.Error.WriteLine(s.Format("run.mode_conflict", runModeSetBy ?? "--loop", "--mode " + value));
            return 1;
        }
        runMode = modeValue;
        runModeWasSet = true;
        runModeSetBy = "--mode";
        i += 2;
        continue;
    }
    if (a == "--mode")
    {
        Console.Error.WriteLine(s.Format("error.missing_value", "--mode"));
        return 1;
    }
    if (a == "--loop" || a == "-l" || a == "-loop")
    {
        if (runModeWasSet && runMode != RunCommandMode.Loop)
        {
            Console.Error.WriteLine(s.Format("run.mode_conflict", runModeSetBy ?? "--wiggum", a));
            return 1;
        }
        runMode = RunCommandMode.Loop;
        runModeWasSet = true;
        runModeSetBy = a;
        i++;
        continue;
    }
    if (a == "--wiggum" || a == "-w" || a == "-wiggum")
    {
        if (runModeWasSet && runMode != RunCommandMode.Wiggum)
        {
            Console.Error.WriteLine(s.Format("run.mode_conflict", runModeSetBy ?? "--loop", a));
            return 1;
        }
        runMode = RunCommandMode.Wiggum;
        runModeWasSet = true;
        runModeSetBy = a;
        i++;
        continue;
    }
    if (a == "--no-lint" || a == "--skip-lint") { skipLint = true; i++; continue; }
    if (a == "--no-commit") { noCommit = true; i++; continue; }
    if (a == "--dry-run") { dryRun = true; i++; continue; }
    if (a == "--retry-failed") { retryFailed = true; i++; continue; }
    if (a == "--parallel-integration")
    {
        if (i + 1 >= argsList.Count)
        {
            Console.Error.WriteLine(s.Format("error.missing_value", "--parallel-integration"));
            return 1;
        }
        parallelIntegration = argsList[i + 1];
        i += 2;
        continue;
    }
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
    if (a == "--no-change-policy")
    {
        Console.Error.WriteLine(s.Format("error.missing_value", "--no-change-policy"));
        return 1;
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
    if (a == "--no-change-max-retries")
    {
        Console.Error.WriteLine(s.Format("error.missing_value", "--no-change-max-retries"));
        return 1;
    }
    if (a == "--no-change-stop-on-max-retries") { noChangeStopOnMaxAttemptsOverride = true; i++; continue; }
    if (a == "--no-change-continue-on-max-retries") { noChangeStopOnMaxAttemptsOverride = false; i++; continue; }
    if (a == "--verbose" || a == "-v") { verbose = true; i++; continue; }
    if (a == "--branch-per-task") { branchPerTask = true; i++; continue; }
    if (a == "--base-branch")
    {
        if (i + 1 >= argsList.Count)
        {
            Console.Error.WriteLine(s.Format("error.missing_value", "--base-branch"));
            return 1;
        }
        baseBranch = argsList[i + 1];
        i += 2;
        continue;
    }
    if (a == "--create-pr") { createPr = true; i++; continue; }
    if (a == "--draft-pr")  { draftPr = true; createPr = true; i++; continue; }
    if (a == "--force")  { force = true; runPersistent = true; i++; continue; }
    if (a == "--yes" || a == "-y") { yes = true; i++; continue; }
    if (a == "--non-interactive") { nonInteractive = true; i++; continue; }
    if (a == "--processes") { doctorProcesses = true; i++; continue; }
    if (a == "--sonnet") { engine ??= "claude"; model = "claude-sonnet-4-6"; i++; continue; }
    if (a == "--opus")   { engine ??= "claude"; model = "claude-opus-4-6";   i++; continue; }
    if (a == "--haiku")  { engine ??= "claude"; model = "claude-haiku-4-5";  i++; continue; }
    if (a == "--engine" && i + 1 < argsList.Count) { engine = argsList[i + 1]; i += 2; continue; }
    if (a == "--engine")
    {
        if (i + 1 >= argsList.Count)
        {
            Console.Error.WriteLine(s.Format("error.missing_value", "--engine"));
            return 1;
        }
        engine = argsList[i + 1];
        i += 2;
        continue;
    }
    if (a == "--model")
    {
        if (i + 1 >= argsList.Count)
        {
            Console.Error.WriteLine(s.Format("error.missing_value", "--model"));
            return 1;
        }
        model = argsList[i + 1];
        i += 2;
        continue;
    }
    if (a == "--prd")
    {
        if (i + 1 >= argsList.Count)
        {
            Console.Error.WriteLine(s.Format("error.missing_value", "--prd"));
            return 1;
        }
        prdOverride = argsList[i + 1];
        i += 2;
        continue;
    }
    if (a == "--repo")
    {
        if (i + 1 >= argsList.Count)
        {
            Console.Error.WriteLine(s.Format("error.missing_value", "--repo"));
            return 1;
        }
        githubRepo = argsList[i + 1];
        i += 2;
        continue;
    }
    if (a == "--label")
    {
        if (i + 1 >= argsList.Count)
        {
            Console.Error.WriteLine(s.Format("error.missing_value", "--label"));
            return 1;
        }
        githubLabel = argsList[i + 1];
        i += 2;
        continue;
    }
    if (a == "--state")
    {
        if (i + 1 >= argsList.Count)
        {
            Console.Error.WriteLine(s.Format("error.missing_value", "--state"));
            return 1;
        }
        githubState = argsList[i + 1];
        i += 2;
        continue;
    }
    if (a == "--output")
    {
        if (i + 1 >= argsList.Count)
        {
            Console.Error.WriteLine(s.Format("error.missing_value", "--output"));
            return 1;
        }
        outputOverride = argsList[i + 1];
        i += 2;
        continue;
    }
    if ((a == "-r" || a == "--retries" || a == "--max-retries") && i + 1 < argsList.Count)
    {
        if (!int.TryParse(argsList[i + 1], out var mr) || mr < 1)
        {
            Console.Error.WriteLine(s.Get("config.invalid_int"));
            return 1;
        }
        maxRetries = mr;
        i += 2;
        continue;
    }
    if (a == "--retry-delay" && i + 1 < argsList.Count)
    {
        if (!int.TryParse(argsList[i + 1], out var rd) || rd < 0)
        {
            Console.Error.WriteLine(s.Get("config.invalid_int"));
            return 1;
        }
        retryDelaySeconds = rd;
        i += 2;
        continue;
    }
    if (a == "--retry-delay")
    {
        Console.Error.WriteLine(s.Format("error.missing_value", "--retry-delay"));
        return 1;
    }
    if (a == "--max-iterations" && i + 1 < argsList.Count)
    {
        if (!int.TryParse(argsList[i + 1], out var mi) || mi < 1)
        {
            Console.Error.WriteLine(s.Get("config.invalid_int"));
            return 1;
        }
        maxIterations = mi;
        i += 2;
        continue;
    }
    if (a == "--max-iterations")
    {
        Console.Error.WriteLine(s.Format("error.missing_value", "--max-iterations"));
        return 1;
    }
    if (a == "--max-parallel" && i + 1 < argsList.Count)
    {
        if (!int.TryParse(argsList[i + 1], out var mp) || mp < 1)
        {
            Console.Error.WriteLine(s.Get("config.invalid_int"));
            return 1;
        }
        maxParallel = mp;
        i += 2;
        continue;
    }
    if (a == "--max-parallel")
    {
        Console.Error.WriteLine(s.Format("error.missing_value", "--max-parallel"));
        return 1;
    }
    if (a == "--max-tokens" && i + 1 < argsList.Count)
    {
        if (!int.TryParse(argsList[i + 1], out var mt) || mt < 1)
        {
            Console.Error.WriteLine(s.Get("config.invalid_int"));
            return 1;
        }
        maxTokens = mt;
        i += 2;
        continue;
    }
    if (a == "--max-tokens")
    {
        Console.Error.WriteLine(s.Format("error.missing_value", "--max-tokens"));
        return 1;
    }
    if (a == "--temperature" && i + 1 < argsList.Count)
    {
        if (!double.TryParse(argsList[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t))
        {
            Console.Error.WriteLine(s.Get("config.invalid_float"));
            return 1;
        }
        temperature = t;
        i += 2;
        continue;
    }
    if (a == "--temperature")
    {
        Console.Error.WriteLine(s.Format("error.missing_value", "--temperature"));
        return 1;
    }
    if ((a == "-r" || a == "--retries" || a == "--max-retries") )
    {
        Console.Error.WriteLine(s.Format("error.missing_value", a));
        return 1;
    }
    if (a == "--follow") { followLogs = true; i++; continue; }
    if (a == "--level" && i + 1 < argsList.Count) { logsLevel = argsList[i + 1]; i += 2; continue; }
    if (a == "--level")
    {
        Console.Error.WriteLine(s.Format("error.missing_value", "--level"));
        return 1;
    }
    if (a == "--since" && i + 1 < argsList.Count) { logsSince = argsList[i + 1]; i += 2; continue; }
    if (a == "--since")
    {
        Console.Error.WriteLine(s.Format("error.missing_value", "--since"));
        return 1;
    }
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

if (command == "once" && runModeWasSet)
{
    Console.Error.WriteLine(s.Format("run.mode_not_supported", "--wiggum/--loop/--mode", "once"));
    return 1;
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
var runCmd         = new RunCommand(runLoop, s);
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

        if (command == "run" && !workerRun)
        {
            return await RunSupervisedRunAsync(
                rawArgs: args,
                workingDirectory: cwd,
                prdPath: prdPath,
                workspaceInit: workspaceInit,
                cancellationToken: cancellation.Token);
        }

        if (tuiDashboard != null)
        {
            // TUI path: start the command on the thread pool so its synchronous
            // startup code does NOT write to the console before Terminal.Gui takes
            // over. Then run Terminal.Gui blocking on the MAIN THREAD.
            var cmdTask = command == "run"
                ? Task.Run(() => runCmd.ExecuteAsync(cwd, prdPath, skipTests, skipLint, engine, model, maxTokens, temperature, passthroughArgs, maxRetries, retryDelaySeconds, maxIterations: maxIterations, dryRun: dryRun, verbose: verbose, branchPerTask: branchPerTask, baseBranch: baseBranch, createPr: createPr, draftPr: draftPr, autoRollback: autoRollback, debugEngineJson: debugEngineJson, ignoreContextStops: ignoreContextStops, noChangePolicyOverride: noChangePolicyOverride, noChangeMaxAttemptsOverride: noChangeMaxAttemptsOverride, noChangeStopOnMaxAttemptsOverride: noChangeStopOnMaxAttemptsOverride, mode: runMode, force: runPersistent, noCommit: noCommit, fast: fast, cancellationToken: cancellation.Token))
                : Task.Run(() => onceCmd.ExecuteAsync(cwd, prdPath, skipTests, skipLint, engine, model, maxTokens, temperature, passthroughArgs, dryRun: dryRun, verbose: verbose, taskOverride: inlineTask, branchPerTask: branchPerTask, baseBranch: baseBranch, createPr: createPr, draftPr: draftPr, autoRollback: autoRollback, debugEngineJson: debugEngineJson, ignoreContextStops: ignoreContextStops, noChangePolicyOverride: noChangePolicyOverride, noChangeMaxAttemptsOverride: noChangeMaxAttemptsOverride, noChangeStopOnMaxAttemptsOverride: noChangeStopOnMaxAttemptsOverride, noCommit: noCommit, fast: fast, cancellationToken: cancellation.Token));

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
            return await runCmd.ExecuteAsync(cwd, prdPath, skipTests, skipLint, engine, model, maxTokens, temperature, passthroughArgs, maxRetries, retryDelaySeconds, maxIterations: maxIterations, dryRun: dryRun, verbose: verbose, branchPerTask: branchPerTask, baseBranch: baseBranch, createPr: createPr, draftPr: draftPr, autoRollback: autoRollback, debugEngineJson: debugEngineJson, ignoreContextStops: ignoreContextStops, noChangePolicyOverride: noChangePolicyOverride, noChangeMaxAttemptsOverride: noChangeMaxAttemptsOverride, noChangeStopOnMaxAttemptsOverride: noChangeStopOnMaxAttemptsOverride, mode: runMode, force: runPersistent, noCommit: noCommit, fast: fast, cancellationToken: cancellation.Token);

        if (inlineTask != null)
            return await onceCmd.ExecuteAsync(cwd, prdPath, skipTests, skipLint, engine, model, maxTokens, temperature, passthroughArgs, dryRun: dryRun, verbose: verbose, taskOverride: inlineTask, branchPerTask: branchPerTask, baseBranch: baseBranch, createPr: createPr, draftPr: draftPr, autoRollback: autoRollback, debugEngineJson: debugEngineJson, ignoreContextStops: ignoreContextStops, noChangePolicyOverride: noChangePolicyOverride, noChangeMaxAttemptsOverride: noChangeMaxAttemptsOverride, noChangeStopOnMaxAttemptsOverride: noChangeStopOnMaxAttemptsOverride, noCommit: noCommit, fast: fast, cancellationToken: cancellation.Token);

        return await onceCmd.ExecuteAsync(cwd, prdPath, skipTests, skipLint, engine, model, maxTokens, temperature, passthroughArgs, dryRun: dryRun, verbose: verbose, branchPerTask: branchPerTask, baseBranch: baseBranch, createPr: createPr, draftPr: draftPr, autoRollback: autoRollback, debugEngineJson: debugEngineJson, ignoreContextStops: ignoreContextStops, noChangePolicyOverride: noChangePolicyOverride, noChangeMaxAttemptsOverride: noChangeMaxAttemptsOverride, noChangeStopOnMaxAttemptsOverride: noChangeStopOnMaxAttemptsOverride, noCommit: noCommit, fast: fast, cancellationToken: cancellation.Token);
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

static async Task<int> RunSupervisedRunAsync(
    string[] rawArgs,
    string workingDirectory,
    string prdPath,
    WorkspaceInitializer workspaceInit,
    CancellationToken cancellationToken)
{
    const int maxCrashRetriesPerTask = 3;
    var crashCounts = new Dictionary<string, int>(StringComparer.Ordinal);

    while (true)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var before = ReadPendingTaskState(prdPath);
        if (before.PendingIndex is null)
            return 0;

        var childArgs = BuildWorkerArguments(rawArgs);
        var startInfo = BuildSelfStartInfo(workingDirectory, childArgs);
        if (startInfo is null)
            throw new InvalidOperationException("Unable to resolve the current ralph executable for supervised run.");

        using var child = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start supervised ralph worker process.");

        using var cancelRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!child.HasExited)
                    child.Kill(entireProcessTree: true);
            }
            catch
            {
                // best effort
            }
        });

        await child.WaitForExitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var after = ReadPendingTaskState(prdPath);
        if (after.PendingIndex is null)
            return 0;

        if (HasPendingProgress(before, after))
        {
            crashCounts.Clear();
            continue;
        }

        if (child.ExitCode == 130)
            return 130;

        var state = TryReadRalphState(workspaceInit.GetStatePath(workingDirectory));
        var taskKey = BuildCrashTaskKey(before, after, state);
        var crashCount = crashCounts.TryGetValue(taskKey, out var existing) ? existing + 1 : 1;
        crashCounts[taskKey] = crashCount;

        TryAppendExecutionLog(
            workspaceInit,
            workingDirectory,
            "run",
            "worker_restarting",
            $"exit_code={child.ExitCode};task={SanitizeLogValue(taskKey)};attempt={crashCount}");

        if (crashCount >= maxCrashRetriesPerTask)
        {
            MarkCurrentTaskSkippedForReview(prdPath, before, after, state);
            crashCounts.Remove(taskKey);
            TryAppendExecutionLog(
                workspaceInit,
                workingDirectory,
                "run",
                "worker_marked_review",
                $"task={SanitizeLogValue(taskKey)}");
        }
        else
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }
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

static ProcessStartInfo? BuildSelfStartInfo(string workingDirectory, IReadOnlyList<string> childArgs)
{
    var hostPath = CurrentExecutableLocator.Resolve(baseDirectory: AppContext.BaseDirectory);
    if (string.IsNullOrWhiteSpace(hostPath))
        return null;

    var startInfo = new ProcessStartInfo
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = false,
        RedirectStandardError = false,
        RedirectStandardInput = false,
        UseShellExecute = false
    };

    if (hostPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    {
        startInfo.FileName = "dotnet";
        startInfo.ArgumentList.Add(hostPath);
    }
    else
    {
        startInfo.FileName = hostPath;
    }

    foreach (var arg in childArgs)
        startInfo.ArgumentList.Add(arg);

    return startInfo;
}

static List<string> BuildWorkerArguments(string[] rawArgs)
{
    var args = rawArgs
        .Where(a => !string.Equals(a, "--worker-run", StringComparison.OrdinalIgnoreCase))
        .ToList();

    var passthroughIndex = args.FindIndex(a => a == "--");
    if (passthroughIndex >= 0)
        args.Insert(passthroughIndex, "--worker-run");
    else
        args.Add("--worker-run");

    return args;
}

static (PrdDocument Document, int? PendingIndex) ReadPendingTaskState(string prdPath)
{
    var doc = PrdParser.Parse(prdPath);
    return (doc, doc.GetNextPendingTaskIndex());
}

static bool HasPendingProgress((PrdDocument Document, int? PendingIndex) before, (PrdDocument Document, int? PendingIndex) after)
{
    if (before.PendingIndex is null && after.PendingIndex is null)
        return true;

    if (before.PendingIndex.HasValue != after.PendingIndex.HasValue)
        return true;

    if (before.PendingIndex.HasValue && after.PendingIndex.HasValue)
        return before.PendingIndex.Value != after.PendingIndex.Value;

    return false;
}

static RalphState? TryReadRalphState(string statePath)
{
    try
    {
        if (!File.Exists(statePath))
            return null;

        var json = File.ReadAllText(statePath);
        return System.Text.Json.JsonSerializer.Deserialize<RalphState>(json);
    }
    catch
    {
        return null;
    }
}

static string BuildCrashTaskKey((PrdDocument Document, int? PendingIndex) before, (PrdDocument Document, int? PendingIndex) after, RalphState? state)
{
    var index = state?.CurrentTaskIndex ?? before.PendingIndex ?? after.PendingIndex ?? -1;
    var text =
        state?.CurrentTaskText
        ?? (before.PendingIndex.HasValue ? before.Document.TaskEntries[before.PendingIndex.Value].DisplayText : null)
        ?? (after.PendingIndex.HasValue ? after.Document.TaskEntries[after.PendingIndex.Value].DisplayText : null)
        ?? "unknown-task";

    return $"{index}:{text}";
}

static void MarkCurrentTaskSkippedForReview(
    string prdPath,
    (PrdDocument Document, int? PendingIndex) before,
    (PrdDocument Document, int? PendingIndex) after,
    RalphState? state)
{
    var doc = PrdParser.Parse(prdPath);
    var candidateIndex = state?.CurrentTaskIndex;
    if (candidateIndex is null || candidateIndex < 0 || candidateIndex >= doc.TaskEntries.Count || !doc.TaskEntries[candidateIndex.Value].IsPending)
        candidateIndex = before.PendingIndex ?? after.PendingIndex;

    if (candidateIndex is null)
        return;

    PrdWriter.MarkTaskSkippedForReview(prdPath, doc, candidateIndex.Value);
}

static string SanitizeLogValue(string value) =>
    value.Replace('\r', ' ').Replace('\n', ' ').Trim();
