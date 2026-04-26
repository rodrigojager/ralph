using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Ralph.Engines.Abstractions;
using Ralph.Engines.Runtime;
using Ralph.Engines.Tokens;

namespace Ralph.Engines.Agent;

public sealed class AgentEngine : IEngine
{
    private readonly string _commandName;
    private readonly EngineExecutionProfile _profile;
    private readonly IPlatformExecutionStrategy _platformStrategy;
    private static readonly TimeSpan WatchdogPingInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan WatchdogWarnThreshold = ReadSecondsFromEnv("RALPH_WATCHDOG_WARN_SECONDS", 120);
    private static readonly TimeSpan WatchdogKillThreshold = ReadSecondsFromEnv("RALPH_WATCHDOG_KILL_SECONDS", 1200);

    public AgentEngine(string name, string commandName)
    {
        Name = name;
        _commandName = commandName;
        _profile = EngineExecutionProfile.For(name);
        _platformStrategy = PlatformExecutionStrategyFactory.CreateCurrent();
    }

    public string Name { get; }

    public async Task<EngineResult> RunAsync(EngineRequest request, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var exitCode = -1;
        var completionSignal = CompletionSignal.None;
        var detectedErrors = new List<DetectedErrorKind>();
        var hadStructuredError = false;

        var profile = ResolveProfile(request);
        var argsForLog = BuildArgsCore(request, profile);

        try
        {
            var command = ResolveCommand(request.CommandOverride ?? _commandName);
            var launch = _platformStrategy.BuildLaunch(command, request.CommandPrefixArgs, argsForLog);
            launch = ApplySandboxIfRequested(request, launch);
            var promptTransport = _platformStrategy.ResolvePromptTransport(profile);
            var redirectStdin = promptTransport == PromptTransportMode.Stdin;

            var lastHeartbeatUtc = DateTime.UtcNow;
            var warnedAboutInactivity = false;
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = launch.FileName,
                    WorkingDirectory = request.WorkingDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = redirectStdin,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            foreach (var token in launch.Args)
                process.StartInfo.ArgumentList.Add(token);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                stdout.AppendLine(e.Data);
                lastHeartbeatUtc = DateTime.UtcNow;
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                stderr.AppendLine(e.Data);
                lastHeartbeatUtc = DateTime.UtcNow;
            };

            process.Start();
            using var trackedProcess = ChildProcessTracker.Track(process);

            if (redirectStdin)
            {
                process.StandardInput.Write(request.TaskText ?? string.Empty);
                process.StandardInput.Close();
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cancelRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best effort
                }
            });

            using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var watchdog = Task.Run(async () =>
            {
                var lastCpu = TimeSpan.Zero;
                try
                {
                    while (!watchdogCts.Token.IsCancellationRequested && !process.HasExited)
                    {
                        await Task.Delay(WatchdogPingInterval, watchdogCts.Token);
                        if (process.HasExited) break;

                        var now = DateTime.UtcNow;
                        var cpu = process.TotalProcessorTime;
                        var cpuAdvanced = cpu > lastCpu + TimeSpan.FromMilliseconds(100);
                        if (cpuAdvanced)
                        {
                            lastCpu = cpu;
                            lastHeartbeatUtc = now;
                        }

                        var inactiveFor = now - lastHeartbeatUtc;
                        if (inactiveFor < WatchdogWarnThreshold)
                            continue;

                        if (!warnedAboutInactivity)
                        {
                            warnedAboutInactivity = true;
                            stderr.AppendLine($"[watchdog] inactivity warning: no heartbeat for {inactiveFor.TotalSeconds:F0}s.");
                        }

                        if (inactiveFor < WatchdogKillThreshold)
                            continue;

                        stderr.AppendLine($"[watchdog] hard stall detected (> {WatchdogKillThreshold.TotalSeconds:F0}s without heartbeat). Killing engine process.");
                        detectedErrors.Add(DetectedErrorKind.Unknown);
                        try
                        {
                            process.Kill(entireProcessTree: true);
                        }
                        catch
                        {
                            // best effort
                        }
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // expected
                }
                catch
                {
                    // best effort
                }
            }, watchdogCts.Token);

            await process.WaitForExitAsync(cancellationToken);
            watchdogCts.Cancel();
            try { await watchdog; } catch { }

            exitCode = process.ExitCode;
            sw.Stop();

            var outText = stdout.ToString();
            var errText = stderr.ToString();

            if (profile.OutputMode == EngineOutputMode.StreamJson)
            {
                var parsed = StreamJsonOutputParser.Parse(outText);
                hadStructuredError = parsed.HasStructuredError;
            }

            if (errText.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                detectedErrors.Add(DetectedErrorKind.RateLimit);
            if (errText.Contains("auth", StringComparison.OrdinalIgnoreCase) || errText.Contains("unauthorized", StringComparison.OrdinalIgnoreCase))
                detectedErrors.Add(DetectedErrorKind.Auth);
            if (errText.Contains("network", StringComparison.OrdinalIgnoreCase) || errText.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                detectedErrors.Add(DetectedErrorKind.Network);
            if (hadStructuredError)
                detectedErrors.Add(DetectedErrorKind.Unknown);

            if (exitCode == 0 && !hadStructuredError)
                completionSignal = CompletionSignal.Complete;
            else if (errText.Contains("GUTTER", StringComparison.OrdinalIgnoreCase) || outText.Contains("GUTTER", StringComparison.OrdinalIgnoreCase))
                completionSignal = CompletionSignal.Gutter;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            stderr.AppendLine(ex.Message);
            if (IsCommandNotFound(ex))
                detectedErrors.Add(DetectedErrorKind.CommandNotFound);
            else
                detectedErrors.Add(DetectedErrorKind.Network);
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();
        var tokenUsage = TokenUsageParser.Parse(stdoutText, stderrText, Name);

        return new EngineResult
        {
            ExitCode = exitCode,
            Stdout = stdoutText,
            Stderr = stderrText,
            ExecutedCommand = CommandToLog(request),
            ExecutedArgs = argsForLog,
            Duration = sw.Elapsed,
            CompletionSignal = completionSignal,
            DetectedErrors = detectedErrors,
            TokenUsage = tokenUsage,
            HadStructuredError = hadStructuredError
        };
    }

    private List<string> BuildArgs(EngineRequest request)
    {
        return BuildArgsCore(request, ResolveProfile(request));
    }

    private List<string> BuildArgsCore(EngineRequest request, EngineExecutionProfile profile)
    {
        var args = new List<string>();
        var passthrough = request.ExtraArgsPassthrough;

        if (request.Adapter != null)
        {
            args.AddRange(request.Adapter.DefaultArgs);
            args.AddRange(ResolveAdapterSafetyArgs(request.Adapter, request.SecurityMode));
            if (!string.IsNullOrEmpty(request.ModelOverride))
            {
                args.Add(request.Adapter.ModelFlag ?? "--model");
                args.Add(request.ModelOverride);
            }
            if (passthrough != null)
                args.AddRange(passthrough);
            var useStdinForAdapter = _platformStrategy.ResolvePromptTransport(profile) == PromptTransportMode.Stdin;
            if (!useStdinForAdapter && !string.IsNullOrWhiteSpace(request.TaskText))
            {
                if (!string.IsNullOrWhiteSpace(profile.PromptFlag))
                    args.Add(profile.PromptFlag!);
                args.Add(request.TaskText!);
            }
            return args;
        }

        if (Name.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("exec");
            args.Add("--skip-git-repo-check");
            if (!HasSandboxOrAutoFlag(passthrough))
            {
                if (IsDangerousMode(request.SecurityMode))
                    args.Add("--dangerously-bypass-approvals-and-sandbox");
                else
                    args.Add("--full-auto");
            }
            if (request.Fast)
            {
                args.Add("-c");
                args.Add("service_tier=\"fast\"");
                args.Add("-c");
                args.Add("features.fast_mode=true");
            }
            args.Add("--json");
            if (!HasCodexReasoningEffortOverride(passthrough))
            {
                args.Add("-c");
                args.Add(request.Fast ? "model_reasoning_effort=\"low\"" : "model_reasoning_effort=\"high\"");
            }
            args.Add("-");
        }
        else if (Name.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-p");
            if (IsDangerousMode(request.SecurityMode) && !HasClaudePermissionFlag(passthrough))
            {
                args.Add("--permission-mode");
                args.Add("bypassPermissions");
            }
        }
        else if (Name.Equals("gemini", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--output-format");
            args.Add("stream-json");
            if (!IsSafeMode(request.SecurityMode))
                args.Add("--yolo");
            if (_platformStrategy.ResolvePromptTransport(profile) == PromptTransportMode.Argument)
                args.Add("-p");
        }
        else if (Name.Equals("cursor", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("--force");
            args.Add("--output-format");
            args.Add("stream-json");
            if (_platformStrategy.ResolvePromptTransport(profile) == PromptTransportMode.Argument)
                args.Add("-p");
        }

        if (!string.IsNullOrEmpty(request.ModelOverride))
        {
            args.Add("--model");
            args.Add(request.ModelOverride);
        }

        if (passthrough != null)
            args.AddRange(passthrough);

        var useStdin = _platformStrategy.ResolvePromptTransport(profile) == PromptTransportMode.Stdin;
        if (!useStdin && !string.IsNullOrWhiteSpace(request.TaskText) && !Name.Equals("codex", StringComparison.OrdinalIgnoreCase))
            args.Add(request.TaskText!);

        return args;
    }

    private EngineExecutionProfile ResolveProfile(EngineRequest request)
    {
        return request.Adapter == null ? _profile : EngineExecutionProfile.FromAdapter(Name, request.Adapter);
    }

    private static IReadOnlyList<string> ResolveAdapterSafetyArgs(EngineAdapterOptions adapter, string? securityMode)
    {
        if (IsDangerousMode(securityMode))
            return adapter.DangerousArgs;
        if (string.Equals(securityMode, "auto", StringComparison.OrdinalIgnoreCase))
            return adapter.AutoArgs.Count > 0 ? adapter.AutoArgs : adapter.SafeArgs;
        return adapter.SafeArgs;
    }

    private static (string FileName, IReadOnlyList<string> Args) ApplySandboxIfRequested(
        EngineRequest request,
        (string FileName, IReadOnlyList<string> Args) launch)
    {
        var sandbox = request.Sandbox;
        if (sandbox?.Enabled != true)
            return launch;

        var provider = (sandbox.Provider ?? "process").Trim().ToLowerInvariant();
        if (provider is not ("docker" or "podman"))
            return launch;
        if (string.IsNullOrWhiteSpace(sandbox.Image))
            return launch;

        var args = new List<string>
        {
            "run",
            "--rm",
            "-i",
            "-v",
            $"{Path.GetFullPath(request.WorkingDirectory)}:/workspace",
            "-w",
            "/workspace"
        };
        if (!string.IsNullOrWhiteSpace(sandbox.Network))
        {
            args.Add("--network");
            args.Add(sandbox.Network!);
        }
        args.Add(sandbox.Image!);
        args.Add(launch.FileName);
        args.AddRange(launch.Args);
        return (provider, args);
    }

    private static bool HasSandboxOrAutoFlag(IReadOnlyList<string>? passthrough)
    {
        if (passthrough == null || passthrough.Count == 0)
            return false;

        foreach (var arg in passthrough)
        {
            if (arg.Contains("--sandbox", StringComparison.OrdinalIgnoreCase)
                || arg.Contains("--full-auto", StringComparison.OrdinalIgnoreCase)
                || arg.Contains("--dangerously-bypass-approvals-and-sandbox", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasClaudePermissionFlag(IReadOnlyList<string>? passthrough)
    {
        if (passthrough == null || passthrough.Count == 0)
            return false;

        foreach (var arg in passthrough)
        {
            if (arg.Contains("--permission-mode", StringComparison.OrdinalIgnoreCase)
                || arg.Contains("--dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase)
                || arg.Contains("--allow-dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool HasCodexReasoningEffortOverride(IReadOnlyList<string>? passthrough)
    {
        if (passthrough == null || passthrough.Count == 0)
            return false;

        foreach (var arg in passthrough)
        {
            if (arg.Contains("model_reasoning_effort", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsCommandNotFound(Exception ex)
    {
        if (ex is Win32Exception { NativeErrorCode: 2 })
            return true;
        var msg = ex.Message;
        return msg.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("o sistema não pode encontrar o arquivo especificado", StringComparison.OrdinalIgnoreCase);
    }

    private string ResolveCommand(string command)
    {
        if (OperatingSystem.IsWindows() && Name.Equals("codex", StringComparison.OrdinalIgnoreCase) && command.Equals("codex", StringComparison.OrdinalIgnoreCase))
            return "codex.cmd";

        return command;
    }

    private static string CommandToLog(EngineRequest request)
    {
        var cmd = request.CommandOverride ?? "(default)";
        if (request.CommandPrefixArgs == null || request.CommandPrefixArgs.Count == 0)
            return cmd;
        return cmd + " " + string.Join(" ", request.CommandPrefixArgs);
    }

    private static TimeSpan ReadSecondsFromEnv(string name, int defaultSeconds)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (int.TryParse(raw, out var seconds) && seconds > 0)
            return TimeSpan.FromSeconds(seconds);
        return TimeSpan.FromSeconds(defaultSeconds);
    }

    private static bool IsSafeMode(string? securityMode)
    {
        return string.IsNullOrWhiteSpace(securityMode)
               || securityMode.Equals("safe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDangerousMode(string? securityMode)
    {
        if (string.Equals(securityMode, "dangerous", StringComparison.OrdinalIgnoreCase))
            return true;
        var safeMode = Environment.GetEnvironmentVariable("RALPH_CODEX_SAFE_MODE");
        if (string.Equals(safeMode, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(safeMode, "true", StringComparison.OrdinalIgnoreCase))
            return false;
        var dangerous = Environment.GetEnvironmentVariable("RALPH_DANGEROUS_MODE");
        return string.Equals(dangerous, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(dangerous, "true", StringComparison.OrdinalIgnoreCase);
    }
}
