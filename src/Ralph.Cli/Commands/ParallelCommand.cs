using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ralph.Core.Localization;
using Ralph.Core.RunLoop;
using Ralph.Persistence.Config;
using Ralph.Persistence.Workspace;
using Ralph.Tasks.Prd;

namespace Ralph.Cli.Commands;

public sealed class ParallelCommand
{
    private readonly RunLoopService _runLoop;
    private readonly WorkspaceInitializer _workspaceInit;
    private readonly ConfigStore _configStore;

    public ParallelCommand(RunLoopService runLoop, WorkspaceInitializer workspaceInit, ConfigStore configStore)
    {
        _runLoop = runLoop;
        _workspaceInit = workspaceInit;
        _configStore = configStore;
    }

    public async Task<int> ExecuteAsync(
        string workingDirectory,
        string prdPath,
        int? maxParallel,
        string? engine,
        string? model,
        int? maxTokens,
        double? temperature,
        IReadOnlyList<string>? extraArgs,
        bool verbose,
        bool dryRun,
        bool retryFailed,
        string? integrationStrategyOverride,
        IStringCatalog s,
        IReadOnlyList<string>? taskOverrides = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveMaxParallel = ResolveMaxParallel(workingDirectory, maxParallel);
        var integrationStrategy = ResolveIntegrationStrategy(workingDirectory, integrationStrategyOverride);
        var plannedTasks = ResolveTasks(prdPath, taskOverrides, s);
        if (plannedTasks == null)
            return 1;
        if (plannedTasks.Count == 0)
        {
            Console.WriteLine(s.Get("parallel.no_tasks"));
            return 0;
        }

        if (dryRun)
        {
            Console.WriteLine(s.Format("parallel.start", plannedTasks.Count, effectiveMaxParallel));
            Console.WriteLine(s.Get("parallel.dry_run"));
            foreach (var task in plannedTasks)
                Console.WriteLine($"  - {task.Text}");
            return 0;
        }

        Console.WriteLine(s.Format("parallel.start", plannedTasks.Count, effectiveMaxParallel));
        var fromPrd = taskOverrides == null || taskOverrides.Count == 0;
        var markLock = new object();
        var workerId = $"parallel-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var claimStore = fromPrd ? new TaskClaimStore(Path.Combine(workingDirectory, ".ralph", "state", "task_claims.json")) : null;

        var hasGroupsOrDependencies = plannedTasks.Any(t => t.DependsOnGroups.Count > 0 || !string.Equals(t.Group, "default", StringComparison.OrdinalIgnoreCase));
        var firstPass = hasGroupsOrDependencies
            ? await ExecuteByDependenciesAsync(
                workingDirectory,
                prdPath,
                plannedTasks,
                effectiveMaxParallel,
                fromPrd,
                markLock,
                engine,
                model,
                maxTokens,
                temperature,
                extraArgs,
                verbose,
                integrationStrategy,
                workerId,
                claimStore,
                s,
                cancellationToken)
            : await ExecuteBatchAsync(
                workingDirectory,
                prdPath,
                plannedTasks,
                effectiveMaxParallel,
                fromPrd,
                markLock,
                engine,
                model,
                maxTokens,
                temperature,
                extraArgs,
                verbose,
                integrationStrategy,
                workerId,
                claimStore,
                s,
                cancellationToken);
        var failed = firstPass.Where(r => !r.Success).Select(r => r.Task).ToList();
        if (retryFailed && failed.Count > 0)
        {
            Console.WriteLine(s.Format("parallel.retry_failed", failed.Count));
            var secondPass = await ExecuteBatchAsync(
                workingDirectory,
                prdPath,
                failed,
                effectiveMaxParallel,
                fromPrd,
                markLock,
                engine,
                model,
                maxTokens,
                temperature,
                extraArgs,
                verbose,
                integrationStrategy,
                workerId,
                claimStore,
                s,
                cancellationToken);

            var retryMap = secondPass.ToDictionary(x => x.Task.Text, x => x.Success, StringComparer.Ordinal);
            for (var idx = 0; idx < firstPass.Count; idx++)
            {
                var entry = firstPass[idx];
                if (!entry.Success && retryMap.TryGetValue(entry.Task.Text, out var retriedSuccess))
                    firstPass[idx] = entry with { Success = retriedSuccess };
            }
        }

        var ordered = firstPass.OrderBy(r => r.Task.Text, StringComparer.Ordinal).ToList();
        var ok = ordered.Count(r => r.Success);
        var fail = ordered.Count - ok;
        foreach (var entry in ordered.Where(r => !r.Success))
            Console.WriteLine(s.Format("parallel.failed_task", entry.Task.Text));

        Console.WriteLine(s.Format("parallel.summary", ok, fail));
        return fail == 0 ? 0 : 1;
    }

    private async Task<List<ParallelTaskResult>> ExecuteBatchAsync(
        string workingDirectory,
        string prdPath,
        IReadOnlyList<ParallelTaskPlan> tasks,
        int maxParallel,
        bool fromPrd,
        object markLock,
        string? engine,
        string? model,
        int? maxTokens,
        double? temperature,
        IReadOnlyList<string>? extraArgs,
        bool verbose,
        string integrationStrategy,
        string workerId,
        TaskClaimStore? claimStore,
        IStringCatalog s,
        CancellationToken cancellationToken)
    {
        var semaphore = new SemaphoreSlim(maxParallel, maxParallel);
        var results = new ConcurrentBag<ParallelTaskResult>();
        var canUseWorktree = IsGitRepository(workingDirectory);

        var workers = tasks.Select(async task =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var runDirectory = workingDirectory;
                string? worktreePath = null;
                string? branchName = null;

                if (canUseWorktree)
                {
                    branchName = BuildBranchName(task.Text);
                    worktreePath = Path.Combine(workingDirectory, ".ralph", "worktrees", branchName);
                    if (CreateWorktree(workingDirectory, branchName, worktreePath))
                        runDirectory = worktreePath;
                    else
                        worktreePath = null;
                }

                try
                {
                    var claimed = claimStore == null || claimStore.TryClaim(task.TaskId, task.Text, task.Index, workerId, TimeSpan.FromMinutes(10));
                    if (!claimed)
                    {
                        results.Add(new ParallelTaskResult(task, false));
                        return;
                    }

                    var result = await _runLoop.RunSingleTaskAsync(
                        runDirectory,
                        task.Text,
                        engine,
                        model,
                        maxTokens,
                        temperature,
                        extraArgs,
                        cancellationToken,
                        verbose);

                    if (result.Completed && worktreePath != null && branchName != null)
                    {
                        var integrated = IntegrateParallelResult(workingDirectory, runDirectory, branchName, task.Text, integrationStrategy);
                        if (!integrated)
                            result = new RunLoopResult { Completed = false };
                    }

                    if (result.Completed && fromPrd && task.Index.HasValue)
                    {
                        lock (markLock)
                        {
                            var latest = PrdParser.Parse(prdPath);
                            var idx = task.Index.Value;
                            if (idx >= 0 && idx < latest.TaskEntries.Count && !latest.TaskEntries[idx].IsCompleted)
                                PrdWriter.MarkTaskCompleted(prdPath, latest, idx);
                        }
                    }

                    results.Add(new ParallelTaskResult(task, result.Completed));
                }
                finally
                {
                    claimStore?.ReleaseByWorker(task.TaskId, task.Text, workerId);
                    if (worktreePath != null && branchName != null && !integrationStrategy.Equals("no-merge", StringComparison.OrdinalIgnoreCase))
                        CleanupWorktree(workingDirectory, worktreePath, branchName);
                }
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        await Task.WhenAll(workers);
        return results.ToList();
    }

    private async Task<List<ParallelTaskResult>> ExecuteByDependenciesAsync(
        string workingDirectory,
        string prdPath,
        IReadOnlyList<ParallelTaskPlan> allTasks,
        int maxParallel,
        bool fromPrd,
        object markLock,
        string? engine,
        string? model,
        int? maxTokens,
        double? temperature,
        IReadOnlyList<string>? extraArgs,
        bool verbose,
        string integrationStrategy,
        string workerId,
        TaskClaimStore? claimStore,
        IStringCatalog s,
        CancellationToken cancellationToken)
    {
        var pending = allTasks.ToList();
        var completedGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ParallelTaskResult>();

        while (pending.Count > 0)
        {
            var ready = pending
                .Where(t => t.DependsOnGroups.All(g => completedGroups.Contains(g)))
                .ToList();
            if (ready.Count == 0)
            {
                // Circular or missing dependency; fail remaining tasks.
                results.AddRange(pending.Select(t => new ParallelTaskResult(t, false)));
                break;
            }

            var batch = await ExecuteBatchAsync(
                workingDirectory,
                prdPath,
                ready,
                maxParallel,
                fromPrd,
                markLock,
                engine,
                model,
                maxTokens,
                temperature,
                extraArgs,
                verbose,
                integrationStrategy,
                workerId,
                claimStore,
                s,
                cancellationToken);
            results.AddRange(batch);
            foreach (var task in ready)
                pending.Remove(task);

            var successfulGroups = batch.Where(r => r.Success).Select(r => r.Task.Group).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var g in successfulGroups)
                completedGroups.Add(g);
        }

        return results;
    }

    private int ResolveMaxParallel(string workingDirectory, int? fromFlag)
    {
        if (fromFlag is > 0)
            return fromFlag.Value;

        var configPath = _workspaceInit.GetConfigPath(workingDirectory);
        var config = File.Exists(configPath) ? _configStore.Load(configPath) : RalphConfig.Default;
        return Math.Max(1, config.Parallel?.MaxParallel ?? 2);
    }

    private string ResolveIntegrationStrategy(string workingDirectory, string? overrideValue)
    {
        if (!string.IsNullOrWhiteSpace(overrideValue))
            return NormalizeIntegrationStrategy(overrideValue);
        var configPath = _workspaceInit.GetConfigPath(workingDirectory);
        var config = File.Exists(configPath) ? _configStore.Load(configPath) : RalphConfig.Default;
        return NormalizeIntegrationStrategy(config.Parallel?.IntegrationStrategy);
    }

    private static string NormalizeIntegrationStrategy(string? value)
    {
        var v = (value ?? "no-merge").Trim().ToLowerInvariant();
        return v is "merge" or "create-pr" ? v : "no-merge";
    }

    private static List<ParallelTaskPlan>? ResolveTasks(string prdPath, IReadOnlyList<string>? taskOverrides, IStringCatalog s)
    {
        if (taskOverrides != null && taskOverrides.Count > 0)
            return taskOverrides
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t =>
                {
                    var parsed = ParseTaskAnnotations(t.Trim());
                    return new ParallelTaskPlan(parsed.CleanText, null, BuildTaskId(null, parsed.CleanText), parsed.Group, parsed.DependsOnGroups);
                })
                .ToList();

        if (!File.Exists(prdPath))
        {
            Console.Error.WriteLine(s.Format("error.prd_not_found", prdPath));
            return null;
        }

        var doc = PrdParser.Parse(prdPath);
        return doc.TaskEntries
            .Select((t, i) => new { Task = t, Index = i })
            .Where(x => !x.Task.IsCompleted)
            .Select(x =>
            {
                var parsed = ParseTaskAnnotations(x.Task.DisplayText);
                return new ParallelTaskPlan(parsed.CleanText, x.Index, BuildTaskId(x.Index, parsed.CleanText), parsed.Group, parsed.DependsOnGroups);
            })
            .ToList();
    }

    private static (string CleanText, string Group, IReadOnlyList<string> DependsOnGroups) ParseTaskAnnotations(string text)
    {
        var group = "default";
        var depends = new List<string>();
        var cleaned = text;

        var groupMatch = Regex.Match(cleaned, @"\[(?:parallel_group|group)\s*:\s*([a-zA-Z0-9_\-]+)\]", RegexOptions.IgnoreCase);
        if (groupMatch.Success)
        {
            group = groupMatch.Groups[1].Value.Trim();
            cleaned = cleaned.Replace(groupMatch.Value, "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        var dependsMatch = Regex.Match(cleaned, @"\[(?:depends_on|depends)\s*:\s*([a-zA-Z0-9_\-,\s]+)\]", RegexOptions.IgnoreCase);
        if (dependsMatch.Success)
        {
            depends = dependsMatch.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            cleaned = cleaned.Replace(dependsMatch.Value, "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        return (cleaned, group, depends);
    }

    private static string BuildTaskId(int? index, string text)
    {
        using var sha = SHA256.Create();
        var raw = $"{index?.ToString() ?? "na"}::{text.Trim()}";
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }

    private static bool IsGitRepository(string workingDirectory)
    {
        return RunGit(workingDirectory, "rev-parse --is-inside-work-tree", 3000) == 0;
    }

    private static bool CreateWorktree(string workingDirectory, string branchName, string worktreePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);
        if (Directory.Exists(worktreePath) && Directory.EnumerateFileSystemEntries(worktreePath).Any())
            return false;

        var exit = RunGit(workingDirectory, $"worktree add -b {Quote(branchName)} {Quote(worktreePath)}", 15000);
        return exit == 0;
    }

    private static bool IntegrateParallelResult(string rootWorkingDirectory, string worktreeDirectory, string branchName, string taskText, string strategy)
    {
        if (!CommitWorktreeChanges(worktreeDirectory, taskText))
            return false;

        if (strategy.Equals("merge", StringComparison.OrdinalIgnoreCase))
            return MergeBranch(rootWorkingDirectory, branchName);

        if (strategy.Equals("create-pr", StringComparison.OrdinalIgnoreCase))
            return PushAndCreatePr(rootWorkingDirectory, branchName, taskText);

        return true;
    }

    private static bool CommitWorktreeChanges(string worktreeDirectory, string taskText)
    {
        if (RunGit(worktreeDirectory, "add -A", 10000) != 0)
            return false;
        var msg = $"ralph: parallel task - {taskText.Replace("\"", "'")}";
        var commitExit = RunGit(worktreeDirectory, $"commit -m {Quote(msg)}", 12000);
        if (commitExit == 0)
            return true;
        // If there are no changes to commit, still consider success.
        var status = RunGit(worktreeDirectory, "status --porcelain", 5000);
        return status == 0;
    }

    private static bool MergeBranch(string rootWorkingDirectory, string branchName)
    {
        var baseBranch = GetCurrentBranch(rootWorkingDirectory);
        if (string.IsNullOrWhiteSpace(baseBranch))
            return false;
        if (RunGit(rootWorkingDirectory, $"checkout {Quote(baseBranch)}", 8000) != 0)
            return false;
        return RunGit(rootWorkingDirectory, $"merge --no-ff {Quote(branchName)}", 15000) == 0;
    }

    private static bool PushAndCreatePr(string rootWorkingDirectory, string branchName, string taskText)
    {
        if (RunGit(rootWorkingDirectory, $"push -u origin {Quote(branchName)}", 15000) != 0)
            return false;
        var title = $"ralph parallel: {taskText.Replace("\"", "'")}";
        var body = "Automated parallel task by ralph.";
        return RunGh(rootWorkingDirectory, $"pr create --title {Quote(title)} --body {Quote(body)} --head {Quote(branchName)}", 15000) == 0;
    }

    private static string? GetCurrentBranch(string workingDirectory)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --abbrev-ref HEAD",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc == null)
                return null;
            proc.WaitForExit(5000);
            return proc.ExitCode == 0 ? proc.StandardOutput.ReadToEnd().Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static int RunGh(string workingDirectory, string args, int timeoutMs)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = args,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc == null)
                return 1;
            proc.WaitForExit(timeoutMs);
            return proc.HasExited ? proc.ExitCode : 1;
        }
        catch
        {
            return 1;
        }
    }

    private static void CleanupWorktree(string workingDirectory, string worktreePath, string branchName)
    {
        RunGit(workingDirectory, $"worktree remove --force {Quote(worktreePath)}", 10000);
        RunGit(workingDirectory, $"branch -D {Quote(branchName)}", 5000);
    }

    private static int RunGit(string workingDirectory, string args, int timeoutMs)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc == null)
                return 1;
            proc.WaitForExit(timeoutMs);
            return proc.HasExited ? proc.ExitCode : 1;
        }
        catch
        {
            return 1;
        }
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static string BuildBranchName(string taskText)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var safe = new string(taskText
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray())
            .Trim('-');

        if (safe.Length == 0)
            safe = "task";
        if (safe.Length > 40)
            safe = safe[..40].Trim('-');
        var branch = $"ralph-par-{safe}-{suffix}";
        return branch[..Math.Min(63, branch.Length)];
    }

    private sealed record ParallelTaskPlan(string Text, int? Index, string TaskId, string Group, IReadOnlyList<string> DependsOnGroups);
    private sealed record ParallelTaskResult(ParallelTaskPlan Task, bool Success);
}

internal sealed class TaskClaimStore
{
    private readonly string _path;
    private readonly string _lockPath;
    private static readonly TimeSpan LockTtl = TimeSpan.FromSeconds(30);

    public TaskClaimStore(string path)
    {
        _path = path;
        _lockPath = path + ".lock";
    }

    public bool TryClaim(string taskId, string taskText, int? lineIndex, string workerId, TimeSpan lease)
    {
        return WithLock(state =>
        {
            var now = DateTimeOffset.UtcNow;
            state.Claims.RemoveAll(c => c.LeaseExpiresAtUtc <= now);
            var existing = state.Claims.FirstOrDefault(c => c.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase));
            if (existing != null && !existing.WorkerId.Equals(workerId, StringComparison.OrdinalIgnoreCase))
                return false;

            if (existing == null)
            {
                state.Claims.Add(new TaskClaimEntry
                {
                    TaskId = taskId,
                    TaskTextSnapshot = taskText,
                    LineIndexSnapshot = lineIndex,
                    WorkerId = workerId,
                    ClaimedAtUtc = now,
                    LeaseExpiresAtUtc = now.Add(lease)
                });
            }
            else
            {
                existing.TaskTextSnapshot = taskText;
                existing.LineIndexSnapshot = lineIndex;
                existing.LeaseExpiresAtUtc = now.Add(lease);
            }

            return true;
        });
    }

    public void ReleaseByWorker(string taskId, string taskText, string workerId)
    {
        WithLock(state =>
        {
            state.Claims.RemoveAll(c =>
                c.WorkerId.Equals(workerId, StringComparison.OrdinalIgnoreCase) &&
                (c.TaskId.Equals(taskId, StringComparison.OrdinalIgnoreCase) ||
                 c.TaskTextSnapshot.Equals(taskText, StringComparison.Ordinal)));
            return true;
        });
    }

    private bool WithLock(Func<TaskClaimState, bool> action)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var owner = $"pid:{Environment.ProcessId}:{Guid.NewGuid():N}";
        for (var i = 0; i < 50; i++)
        {
            FileStream? lockStream = null;
            try
            {
                lockStream = AcquireLock(owner);
                if (lockStream == null)
                {
                    Thread.Sleep(50);
                    continue;
                }

                var state = Load();
                var result = action(state);
                Save(state);
                return result;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            finally
            {
                lockStream?.Dispose();
                TryDeleteOwnedLock(owner);
            }
        }

        return false;
    }

    private FileStream? AcquireLock(string owner)
    {
        try
        {
            // CreateNew keeps acquisition atomic across processes.
            var fs = new FileStream(_lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            WriteLockMetadata(fs, new TaskClaimLockMetadata
            {
                Owner = owner,
                ProcessId = Environment.ProcessId,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            fs.Flush(true);
            return fs;
        }
        catch (IOException)
        {
            TryBreakStaleLock();
            return null;
        }
    }

    private void TryBreakStaleLock()
    {
        try
        {
            if (!File.Exists(_lockPath))
                return;

            var metadata = ReadLockMetadata();
            if (metadata == null)
                return;

            var age = DateTimeOffset.UtcNow - metadata.CreatedAtUtc;
            if (age <= LockTtl)
                return;

            // Best-effort: if PID is alive, avoid stealing lock.
            if (metadata.ProcessId > 0 && IsProcessAlive(metadata.ProcessId))
                return;

            File.Delete(_lockPath);
        }
        catch
        {
            // best effort
        }
    }

    private TaskClaimLockMetadata? ReadLockMetadata()
    {
        try
        {
            var json = File.ReadAllText(_lockPath);
            return JsonSerializer.Deserialize<TaskClaimLockMetadata>(json);
        }
        catch
        {
            return null;
        }
    }

    private void TryDeleteOwnedLock(string owner)
    {
        try
        {
            if (!File.Exists(_lockPath))
                return;

            var metadata = ReadLockMetadata();
            if (metadata == null)
                return;
            if (!metadata.Owner.Equals(owner, StringComparison.Ordinal))
                return;

            File.Delete(_lockPath);
        }
        catch
        {
            // best effort
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteLockMetadata(Stream stream, TaskClaimLockMetadata metadata)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata));
        stream.SetLength(0);
        stream.Write(bytes, 0, bytes.Length);
        stream.Position = 0;
    }

    private TaskClaimState Load()
    {
        if (!File.Exists(_path))
            return new TaskClaimState();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<TaskClaimState>(json) ?? new TaskClaimState();
        }
        catch
        {
            return new TaskClaimState();
        }
    }

    private void Save(TaskClaimState state)
    {
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    private sealed class TaskClaimState
    {
        public List<TaskClaimEntry> Claims { get; set; } = new();
    }

    private sealed class TaskClaimEntry
    {
        public string TaskId { get; set; } = string.Empty;
        public string TaskTextSnapshot { get; set; } = string.Empty;
        public int? LineIndexSnapshot { get; set; }
        public string WorkerId { get; set; } = string.Empty;
        public DateTimeOffset ClaimedAtUtc { get; set; }
        public DateTimeOffset LeaseExpiresAtUtc { get; set; }
    }

    private sealed class TaskClaimLockMetadata
    {
        public string Owner { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
    }
}
