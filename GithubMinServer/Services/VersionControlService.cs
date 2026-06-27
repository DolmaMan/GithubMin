using GithubMinServer.Contracts;
using GithubMinServer.Data;
using GithubMinServer.Models;
using Microsoft.EntityFrameworkCore;

namespace GithubMinServer.Services;

public class VersionControlService(AppDbContext dbContext, SnapshotStorageService snapshotStorageService)
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly SnapshotStorageService _snapshotStorageService = snapshotStorageService;

    public async Task<MergeResponse> MergeAsync(
        Project project,
        Branch sourceBranch,
        Branch targetBranch,
        User author,
        string? message,
        CancellationToken cancellationToken)
    {
        if (sourceBranch.Id == targetBranch.Id)
        {
            throw new InvalidOperationException("Исходная и целевая ветки должны отличаться.");
        }

        if (sourceBranch.HeadCommitId is null)
        {
            throw new InvalidOperationException("В исходной ветке нет коммитов для слияния.");
        }

        if (targetBranch.HeadCommitId is null)
        {
            throw new InvalidOperationException("В целевой ветке нет коммитов для слияния.");
        }

        var commits = await _dbContext.Commits
            .Where(commit => commit.ProjectId == project.Id)
            .Include(commit => commit.Author)
            .Include(commit => commit.Branch)
            .ToListAsync(cancellationToken);

        var commitMap = commits.ToDictionary(commit => commit.Id);
        var commonAncestorId = FindCommonAncestor(sourceBranch.HeadCommitId.Value, targetBranch.HeadCommitId.Value, commitMap);

        var baseFiles = commonAncestorId.HasValue
            ? await _snapshotStorageService.ReadSnapshotAsync(commitMap[commonAncestorId.Value].SnapshotPath, cancellationToken)
            : new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        var sourceFiles = await _snapshotStorageService.ReadSnapshotAsync(commitMap[sourceBranch.HeadCommitId.Value].SnapshotPath, cancellationToken);
        var targetFiles = await _snapshotStorageService.ReadSnapshotAsync(commitMap[targetBranch.HeadCommitId.Value].SnapshotPath, cancellationToken);

        var conflicts = new List<string>();
        var mergedFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var allPaths = baseFiles.Keys
            .Union(sourceFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .Union(targetFiles.Keys, StringComparer.OrdinalIgnoreCase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var path in allPaths)
        {
            var baseExists = baseFiles.TryGetValue(path, out var baseBytes);
            var sourceExists = sourceFiles.TryGetValue(path, out var sourceBytes);
            var targetExists = targetFiles.TryGetValue(path, out var targetBytes);

            var sourceChanged = !StatesEqual(baseExists, baseBytes, sourceExists, sourceBytes);
            var targetChanged = !StatesEqual(baseExists, baseBytes, targetExists, targetBytes);

            if (sourceChanged && targetChanged && !StatesEqual(sourceExists, sourceBytes, targetExists, targetBytes))
            {
                conflicts.Add(path);
                continue;
            }

            var resolvedState = ResolveState(
                sourceChanged,
                targetChanged,
                sourceExists,
                sourceBytes,
                targetExists,
                targetBytes,
                baseExists,
                baseBytes);

            if (resolvedState.Exists)
            {
                mergedFiles[path] = resolvedState.Content!;
            }
        }

        if (conflicts.Count > 0)
        {
            return new MergeResponse(false, "При слиянии обнаружены конфликты файлов.", null, conflicts);
        }

        var mergeCommit = new Commit
        {
            ProjectId = project.Id,
            BranchId = targetBranch.Id,
            ParentCommitId = targetBranch.HeadCommitId,
            MergeParentCommitId = sourceBranch.HeadCommitId,
            AuthorId = author.Id,
            Message = string.IsNullOrWhiteSpace(message)
                ? $"Слияние ветки {sourceBranch.Name} в ветку {targetBranch.Name}"
                : message.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        mergeCommit.SnapshotPath = await _snapshotStorageService.SaveArchiveFromFilesAsync(
            project.Id,
            mergeCommit.Id,
            mergedFiles,
            cancellationToken);

        _dbContext.Commits.Add(mergeCommit);
        targetBranch.HeadCommitId = mergeCommit.Id;
        project.ActiveBranchId = targetBranch.Id;
        project.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _dbContext.Entry(mergeCommit).Reference(commit => commit.Author).LoadAsync(cancellationToken);
        await _dbContext.Entry(mergeCommit).Reference(commit => commit.Branch).LoadAsync(cancellationToken);

        return new MergeResponse(true, "Слияние успешно выполнено.", mergeCommit.ToSummaryResponse(), []);
    }

    private static Guid? FindCommonAncestor(Guid sourceHeadId, Guid targetHeadId, IReadOnlyDictionary<Guid, Commit> commits)
    {
        var sourceAncestors = BuildAncestorDistanceMap(sourceHeadId, commits);
        var targetAncestors = BuildAncestorDistanceMap(targetHeadId, commits);

        var sharedAncestors = sourceAncestors.Keys
            .Intersect(targetAncestors.Keys)
            .OrderBy(commitId => sourceAncestors[commitId] + targetAncestors[commitId])
            .ThenBy(commitId => Math.Max(sourceAncestors[commitId], targetAncestors[commitId]))
            .ToArray();

        return sharedAncestors.Length == 0 ? null : sharedAncestors[0];
    }

    private static Dictionary<Guid, int> BuildAncestorDistanceMap(Guid startCommitId, IReadOnlyDictionary<Guid, Commit> commits)
    {
        var distances = new Dictionary<Guid, int>();
        var queue = new Queue<(Guid CommitId, int Distance)>();
        queue.Enqueue((startCommitId, 0));

        while (queue.Count > 0)
        {
            var (commitId, distance) = queue.Dequeue();
            if (!distances.TryAdd(commitId, distance) || !commits.TryGetValue(commitId, out var commit))
            {
                continue;
            }

            if (commit.ParentCommitId.HasValue)
            {
                queue.Enqueue((commit.ParentCommitId.Value, distance + 1));
            }

            if (commit.MergeParentCommitId.HasValue)
            {
                queue.Enqueue((commit.MergeParentCommitId.Value, distance + 1));
            }
        }

        return distances;
    }

    private static (bool Exists, byte[]? Content) ResolveState(
        bool sourceChanged,
        bool targetChanged,
        bool sourceExists,
        byte[]? sourceBytes,
        bool targetExists,
        byte[]? targetBytes,
        bool baseExists,
        byte[]? baseBytes)
    {
        if (sourceChanged)
        {
            return sourceExists ? (true, sourceBytes) : (false, null);
        }

        if (targetChanged)
        {
            return targetExists ? (true, targetBytes) : (false, null);
        }

        if (targetExists)
        {
            return (true, targetBytes);
        }

        return baseExists ? (true, baseBytes) : (false, null);
    }

    private static bool StatesEqual(bool leftExists, byte[]? leftBytes, bool rightExists, byte[]? rightBytes)
    {
        if (leftExists != rightExists)
        {
            return false;
        }

        if (!leftExists)
        {
            return true;
        }

        return leftBytes!.AsSpan().SequenceEqual(rightBytes);
    }
}
