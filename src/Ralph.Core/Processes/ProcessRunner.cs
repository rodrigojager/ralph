using System.Diagnostics;
using System.Text;

namespace Ralph.Core.Processes;

public sealed class ProcessRunResult
{
    public int ExitCode { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public bool TimedOut { get; init; }
}

public static class ProcessRunner
{
    public static async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
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

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        if (!process.Start())
            return new ProcessRunResult { ExitCode = 1, Stderr = "process failed to start" };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
            return new ProcessRunResult
            {
                ExitCode = process.ExitCode,
                Stdout = stdout.ToString(),
                Stderr = stderr.ToString()
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new ProcessRunResult
            {
                ExitCode = 124,
                Stdout = stdout.ToString(),
                Stderr = stderr + $"[timeout] process exceeded {timeout.TotalSeconds:F0}s.",
                TimedOut = true
            };
        }
    }

    public static ProcessRunResult Run(
        string fileName,
        IReadOnlyList<string> args,
        string workingDirectory,
        TimeSpan timeout)
    {
        return RunAsync(fileName, args, workingDirectory, timeout).GetAwaiter().GetResult();
    }

    public static async Task<ProcessRunResult> RunShellAsync(
        string command,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var fileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        var args = OperatingSystem.IsWindows()
            ? new[] { "/c", command }
            : new[] { "-lc", command };
        return await RunAsync(fileName, args, workingDirectory, timeout, cancellationToken);
    }

    private static void TryKill(Process process)
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
    }
}
