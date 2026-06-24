using GithubMinServer.Contracts;
using GithubMinServer.Data;
using GithubMinServer.Extensions;
using GithubMinServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GithubMinServer.Controllers;

[ApiController]
[Authorize]
[Route("api/projects/{projectId:guid}/merge")]
public class MergeController(AppDbContext dbContext, VersionControlService versionControlService) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly VersionControlService _versionControlService = versionControlService;

    [HttpPost]
    public async Task<ActionResult<MergeResponse>> Merge(Guid projectId, MergeRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(item => item.Id == projectId && item.OwnerId == userId, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var branches = await _dbContext.Branches
            .Where(branch => branch.ProjectId == projectId &&
                             (branch.Id == request.SourceBranchId || branch.Id == request.TargetBranchId))
            .ToListAsync(cancellationToken);

        var sourceBranch = branches.FirstOrDefault(branch => branch.Id == request.SourceBranchId);
        var targetBranch = branches.FirstOrDefault(branch => branch.Id == request.TargetBranchId);

        if (sourceBranch is null || targetBranch is null)
        {
            return BadRequest("Both source and target branches must belong to the project.");
        }

        var author = await _dbContext.Users.FirstAsync(user => user.Id == userId, cancellationToken);

        try
        {
            var result = await _versionControlService.MergeAsync(
                project,
                sourceBranch,
                targetBranch,
                author,
                request.Message,
                cancellationToken);

            if (!result.Success)
            {
                return Conflict(result);
            }

            return Ok(result);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(exception.Message);
        }
    }
}
