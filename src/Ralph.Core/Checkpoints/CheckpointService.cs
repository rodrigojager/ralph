using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ralph.Core.Processes;
using Ralph.Persistence.State;
using Ralph.Persistence.Workspace;

namespace Ralph.Core.Checkpoints;

public sealed class CheckpointService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly WorkspaceInitializer _workspace;
    private readonly StateStore _stateStore;

    public CheckpointService(WorkspaceInitializer? workspace = null, StateStore? stateStore = null)
    {
        _workspace = workspace ?? new WorkspaceInitializer();
        _stateStore = stateStore ?? new StateStore();
    }

    public CheckpointMetadata Create(string workingDirectory, string? label = null)
    {
        if (!_workspace.IsInitialized(workingDirectory))
            _workspace.Initialize(workingDirectory);

        var id = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        var dir = Path.Combine(_workspace.GetCheckpointsDir(workingDirectory), id);
        Directory.CreateDirectory(dir);

        var diff = RunGit(workingDirectory, "diff", "--binary", "HEAD");
        var status = RunGit(workingDirectory, "status", "--short", "--branch");
        var head = RunGit(workingDirectory, "rev-parse", "HEAD")?.Trim();
        var branch = RunGit(workingDirectory, "rev-parse", "--abbrev-ref", "HEAD")?.Trim();
        var patchPath = Path.Combine(dir, "workspace.patch");
        File.WriteAllText(patchPath, diff ?? string.Empty);

        var metadata = new CheckpointMetadata
        {
            Id = id,
            Label = label,
            CreatedAt = DateTimeOffset.UtcNow,
            GitHead = head,
            GitBranch = branch,
            HasPatch = !string.IsNullOrWhiteSpace(diff),
            WorkspaceHash = ComputeWorkspaceHash(workingDirectory),
            Status = status
        };

        File.WriteAllText(Path.Combine(dir, "checkpoint.json"), JsonSerializer.Serialize(metadata, JsonOptions) + Environment.NewLine);

        var statePath = _workspace.GetStatePath(workingDirectory);
        if (File.Exists(statePath))
            File.Copy(statePath, Path.Combine(dir, WorkspaceInitializer.StateFileName), overwrite: true);

        return metadata;
    }

    public IReadOnlyList<CheckpointMetadata> List(string workingDirectory)
    {
        var root = _workspace.GetCheckpointsDir(workingDirectory);
        if (!Directory.Exists(root))
            return Array.Empty<CheckpointMetadata>();

        return Directory.EnumerateFiles(root, "checkpoint.json", SearchOption.AllDirectories)
            .Select(TryLoad)
            .Where(m => m != null)
            .Select(m => m!)
            .OrderByDescending(m => m.CreatedAt)
            .ToList();
    }

    public CheckpointMetadata? Get(string workingDirectory, string id)
    {
        var path = Path.Combine(_workspace.GetCheckpointsDir(workingDirectory), id, "checkpoint.json");
        return TryLoad(path);
    }

    public CheckpointRestoreResult Restore(string workingDirectory, string id, bool force = false)
    {
        var metadata = Get(workingDirectory, id);
        if (metadata == null)
            return new CheckpointRestoreResult(false, "Checkpoint not found.");

        var currentHead = RunGit(workingDirectory, "rev-parse", "HEAD")?.Trim();
        if (!string.IsNullOrWhiteSpace(metadata.GitHead)
            && !string.Equals(currentHead, metadata.GitHead, StringComparison.OrdinalIgnoreCase)
            && !force)
        {
            return new CheckpointRestoreResult(false, "Current git HEAD differs from checkpoint. Use --force if you understand the risk.");
        }

        var status = RunGit(workingDirectory, "status", "--porcelain");
        if (!string.IsNullOrWhiteSpace(status) && !force)
            return new CheckpointRestoreResult(false, "Workspace is dirty. Use --force to apply the checkpoint patch anyway.");

        var patchPath = Path.Combine(_workspace.GetCheckpointsDir(workingDirectory), id, "workspace.patch");
        if (File.Exists(patchPath) && new FileInfo(patchPath).Length > 0)
        {
            var apply = ProcessRunner.Run("git", new[] { "apply", "--index", patchPath }, workingDirectory, TimeSpan.FromSeconds(30));
            if (apply.ExitCode != 0)
                return new CheckpointRestoreResult(false, apply.Stderr.Trim());
        }

        var stateSnapshot = Path.Combine(_workspace.GetCheckpointsDir(workingDirectory), id, WorkspaceInitializer.StateFileName);
        if (File.Exists(stateSnapshot))
        {
            var state = _stateStore.Load(stateSnapshot);
            _stateStore.Save(_workspace.GetStatePath(workingDirectory), state);
        }

        return new CheckpointRestoreResult(true, "Checkpoint restored.");
    }

    private static CheckpointMetadata? TryLoad(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<CheckpointMetadata>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? RunGit(string workingDirectory, params string[] args)
    {
        try
        {
            var result = ProcessRunner.Run("git", args, workingDirectory, TimeSpan.FromSeconds(10));
            return result.ExitCode == 0 ? result.Stdout : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeWorkspaceHash(string workingDirectory)
    {
        using var sha = SHA256.Create();
        foreach (var file in Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(workingDirectory, file).Replace('\\', '/');
            if (rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith(".ralph/checkpoints/", StringComparison.OrdinalIgnoreCase))
                continue;
            var info = new FileInfo(file);
            var line = $"{rel}|{info.Length}|{info.LastWriteTimeUtc.Ticks}\n";
            var bytes = System.Text.Encoding.UTF8.GetBytes(line);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }
}

public sealed class CheckpointMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("git_head")]
    public string? GitHead { get; init; }

    [JsonPropertyName("git_branch")]
    public string? GitBranch { get; init; }

    [JsonPropertyName("has_patch")]
    public bool HasPatch { get; init; }

    [JsonPropertyName("workspace_hash")]
    public string? WorkspaceHash { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

public sealed record CheckpointRestoreResult(bool Success, string Message);
