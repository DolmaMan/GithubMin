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
        if (!commitMap.ContainsKey(sourceBranch.HeadCommitId.Value) || !commitMap.ContainsKey(targetBranch.HeadCommitId.Value))
        {
            throw new InvalidOperationException("Одна из веток указывает на несуществующий коммит.");
        }

        var sourceFiles = await _snapshotStorageService.ReadSnapshotAsync(commitMap[sourceBranch.HeadCommitId.Value].SnapshotPath, cancellationToken);
        var targetFiles = await _snapshotStorageService.ReadSnapshotAsync(commitMap[targetBranch.HeadCommitId.Value].SnapshotPath, cancellationToken);

        var mergedFiles = MergeSnapshots(sourceFiles, targetFiles);

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

        return new MergeResponse(true, "Слияние успешно выполнено. При совпадении файлов выбрана версия из исходной ветки.", mergeCommit.ToSummaryResponse(), []);
    }

    private static Dictionary<string, byte[]> MergeSnapshots(
        IReadOnlyDictionary<string, byte[]> sourceFiles,
        IReadOnlyDictionary<string, byte[]> targetFiles)
    {
        var mergedFiles = new Dictionary<string, byte[]>(targetFiles, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceFile in sourceFiles)
        {
            mergedFiles[sourceFile.Key] = sourceFile.Value;
        }

        return mergedFiles;
    }
}
