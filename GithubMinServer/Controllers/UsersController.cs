using GithubMinServer.Contracts;
using GithubMinServer.Data;
using GithubMinServer.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GithubMinServer.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyCollection<PublicUserResponse>>> SearchUsers(
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return Ok(Array.Empty<PublicUserResponse>());
        }

        var queryLower = normalizedQuery.ToLowerInvariant();
        var users = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Username.ToLower().Contains(queryLower))
            .OrderBy(user => user.Username)
            .Take(30)
            .ToListAsync(cancellationToken);

        return Ok(users.Select(user => user.ToPublicResponse()).ToArray());
    }

    [HttpGet("{userId:guid}/projects/public")]
    public async Task<ActionResult<IReadOnlyCollection<ProjectSummaryResponse>>> GetPublicProjects(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var projects = await _dbContext.Projects
            .AsNoTracking()
            .Where(project => project.OwnerId == userId && project.Visibility == ProjectVisibility.Public)
            .Include(project => project.Owner)
            .Include(project => project.Branches)
            .Include(project => project.Commits)
            .ToListAsync(cancellationToken);

        return Ok(projects
            .OrderByDescending(project => project.UpdatedAt)
            .Select(project => project.ToSummaryResponse())
            .ToArray());
}
}
