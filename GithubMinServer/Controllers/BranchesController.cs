using GithubMinServer.Contracts;
using GithubMinServer.Data;
using GithubMinServer.Extensions;
using GithubMinServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GithubMinServer.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/branches")]
public class BranchesController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<BranchResponse>>> GetBranches(Guid projectId, CancellationToken cancellationToken)
    {
        var userId = TryGetCurrentUserId();
        var project = await _dbContext.Projects
            .AsNoTracking()
            .Where(item => item.Id == projectId && (item.Visibility == ProjectVisibility.Public || item.OwnerId == userId))
            .Include(item => item.Branches)
                .ThenInclude(branch => branch.Commits)
            .FirstOrDefaultAsync(cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        return Ok(project.Branches
            .OrderBy(branch => branch.Name)
            .Select(branch => branch.ToResponse(project.ActiveBranchId))
            .ToArray());
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<BranchResponse>> CreateBranch(Guid projectId, CreateBranchRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        var project = await _dbContext.Projects
            .Include(item => item.Branches)
            .FirstOrDefaultAsync(item => item.Id == projectId && item.OwnerId == userId, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var branchName = request.Name.Trim();
        if (project.Branches.Any(branch => branch.Name.ToLower() == branchName.ToLower()))
        {
            return Conflict("A branch with the same name already exists.");
        }

        Guid? startFromCommitId = request.StartFromCommitId;
        if (startFromCommitId.HasValue)
        {
            var commitExists = await _dbContext.Commits.AnyAsync(
                commit => commit.Id == startFromCommitId && commit.ProjectId == projectId,
                cancellationToken);

            if (!commitExists)
            {
                return BadRequest("Start commit does not exist in this project.");
            }
        }
        else
        {
            startFromCommitId = await _dbContext.Branches
                .Where(branch => branch.Id == project.ActiveBranchId)
                .Select(branch => branch.HeadCommitId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var branchEntity = new Branch
        {
            ProjectId = projectId,
            Name = branchName,
            HeadCommitId = startFromCommitId,
            CreatedFromCommitId = startFromCommitId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Branches.Add(branchEntity);
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetBranches), new { projectId }, branchEntity.ToResponse(project.ActiveBranchId));
    }

    [Authorize]
    [HttpPost("switch")]
    public async Task<ActionResult<BranchResponse>> SwitchBranch(Guid projectId, SwitchBranchRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        var project = await _dbContext.Projects
            .Include(item => item.Branches)
                .ThenInclude(branch => branch.Commits)
            .FirstOrDefaultAsync(item => item.Id == projectId && item.OwnerId == userId, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var branch = project.Branches.FirstOrDefault(item => item.Id == request.BranchId);
        if (branch is null)
        {
            return BadRequest("Branch does not belong to the specified project.");
        }

        project.ActiveBranchId = branch.Id;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(branch.ToResponse(project.ActiveBranchId));
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
}
