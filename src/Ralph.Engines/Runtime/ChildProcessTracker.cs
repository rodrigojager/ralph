using System.Collections.Concurrent;
using System.Diagnostics;

namespace Ralph.Engines.Runtime;

internal static class ChildProcessTracker
{
    private static readonly ConcurrentDictionary<int, Process> Processes = new();

    static ChildProcessTracker()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => KillAll();
    }

    public static IDisposable Track(Process process)
    {
        if (process.Id > 0)
            Processes[process.Id] = process;
        return new Lease(process.Id);
    }

    private static void KillAll()
    {
        foreach (var (_, process) in Processes)
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

    private sealed class Lease : IDisposable
    {
        private readonly int _pid;
        private int _disposed;

        public Lease(int pid)
        {
            _pid = pid;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            if (_pid > 0)
                Processes.TryRemove(_pid, out _);
        }
    }
}
