using GithubMinServer.Contracts;
using GithubMinServer.Data;
using GithubMinServer.Extensions;
using GithubMinServer.Models;
using GithubMinServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GithubMinServer.Controllers;

[ApiController]
public class CommitsController(AppDbContext dbContext, SnapshotStorageService snapshotStorageService) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly SnapshotStorageService _snapshotStorageService = snapshotStorageService;

    [AllowAnonymous]
    [HttpGet("api/projects/{projectId:guid}/commits")]
    public async Task<ActionResult<IReadOnlyCollection<CommitSummaryResponse>>> GetCommits(
        Guid projectId,
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        var userId = TryGetCurrentUserId();
        var hasAccess = await _dbContext.Projects.AnyAsync(
            project => project.Id == projectId && (project.Visibility == ProjectVisibility.Public || project.OwnerId == userId),
            cancellationToken);

        if (!hasAccess)
        {
            return NotFound();
        }

        var commitsQuery = _dbContext.Commits
            .AsNoTracking()
            .Where(commit => commit.ProjectId == projectId)
            .Include(commit => commit.Author)
            .Include(commit => commit.Branch)
            .OrderByDescending(commit => commit.CreatedAt)
            .AsQueryable();

        if (branchId.HasValue)
        {
            commitsQuery = commitsQuery.Where(commit => commit.BranchId == branchId.Value);
        }

        var commits = await commitsQuery.ToListAsync(cancellationToken);
        return Ok(commits.Select(commit => commit.ToSummaryResponse()).ToArray());
    }

    [Authorize]
    [Consumes("multipart/form-data")]
    [HttpPost("api/projects/{projectId:guid}/commits")]
    public async Task<ActionResult<CommitSummaryResponse>> CreateCommit(
        Guid projectId,
        [FromForm] CreateCommitRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        var project = await _dbContext.Projects
            .Include(item => item.Branches)
            .FirstOrDefaultAsync(item => item.Id == projectId && item.OwnerId == userId, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest("Введите сообщение коммита.");
        }
        if (request.Archive is null)
        {
            return BadRequest("Архив коммита обязателен.");
        }

        var branch = ResolveBranchForCommit(project, request.BranchId);
        if (branch is null)
        {
            return BadRequest("Целевая ветка не найдена.");
        }

        var commit = new Commit
        {
            ProjectId = project.Id,
            BranchId = branch.Id,
            ParentCommitId = branch.HeadCommitId,
            AuthorId = userId,
            Message = request.Message.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            commit.SnapshotPath = await _snapshotStorageService.SaveUploadedArchiveAsync(
                project.Id,
                commit.Id,
                request.Archive,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
        catch (IOException exception)
        {
            return BadRequest($"Не удалось сохранить архив: {exception.Message}");
        }

        _dbContext.Commits.Add(commit);
        branch.HeadCommitId = commit.Id;
        project.ActiveBranchId = branch.Id;
        project.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _dbContext.Entry(commit).Reference(item => item.Author).LoadAsync(cancellationToken);
        await _dbContext.Entry(commit).Reference(item => item.Branch).LoadAsync(cancellationToken);

        return CreatedAtAction(nameof(GetCommitById), new { commitId = commit.Id }, commit.ToSummaryResponse());
    }

    [AllowAnonymous]
    [HttpGet("api/commits/{commitId:guid}")]
    public async Task<ActionResult<CommitDetailsResponse>> GetCommitById(Guid commitId, CancellationToken cancellationToken)
    {
        var userId = TryGetCurrentUserId();
        var commit = await _dbContext.Commits
            .AsNoTracking()
            .Where(item => item.Id == commitId && (item.Project!.Visibility == ProjectVisibility.Public || item.Project.OwnerId == userId))
            .Include(item => item.Project)
            .Include(item => item.Author)
            .Include(item => item.Branch)
            .FirstOrDefaultAsync(cancellationToken);

        if (commit is null)
        {
            return NotFound();
        }

        return Ok(commit.ToDetailsResponse());
    }

    [AllowAnonymous]
    [HttpGet("api/commits/{commitId:guid}/download")]
    public async Task<IActionResult> DownloadCommit(Guid commitId, CancellationToken cancellationToken)
    {
        var userId = TryGetCurrentUserId();
        var commit = await _dbContext.Commits
            .AsNoTracking()
            .Where(item => item.Id == commitId && (item.Project!.Visibility == ProjectVisibility.Public || item.Project.OwnerId == userId))
            .Include(item => item.Project)
            .FirstOrDefaultAsync(cancellationToken);

        if (commit is null)
        {
            return NotFound();
        }

        var archivePath = _snapshotStorageService.GetArchiveAbsolutePath(commit.SnapshotPath);
        if (!System.IO.File.Exists(archivePath))
        {
            return NotFound("Архив снапшота отсутствует на сервере.");
        }

        var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileName = $"{ToSafeFileName(commit.Project?.Name ?? "project")}-{commit.Id.ToString()[..8]}.zip";
        return File(stream, "application/zip", fileName, enableRangeProcessing: true);
    }

    private Branch? ResolveBranchForCommit(Project project, Guid? branchId)
    {
        if (branchId.HasValue)
        {
            return project.Branches.FirstOrDefault(branch => branch.Id == branchId.Value);
        }

        if (project.ActiveBranchId.HasValue)
        {
            return project.Branches.FirstOrDefault(branch => branch.Id == project.ActiveBranchId.Value);
        }

        if (project.DefaultBranchId.HasValue)
        {
            return project.Branches.FirstOrDefault(branch => branch.Id == project.DefaultBranchId.Value);
        }

        return project.Branches.FirstOrDefault();
    }

    private Guid? TryGetCurrentUserId()
    {
        try
        {
            return User.Identity?.IsAuthenticated == true ? User.GetRequiredUserId() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ToSafeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = new string(value.Select(character => invalidChars.Contains(character) ? '-' : character).ToArray());
        return string.IsNullOrWhiteSpace(safeName) ? "project" : safeName.Trim();
    }
}
