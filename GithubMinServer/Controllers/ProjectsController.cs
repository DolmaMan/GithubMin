using GithubMinServer.Contracts;
using GithubMinServer.Data;
using GithubMinServer.Extensions;
using GithubMinServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GithubMinServer.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [Authorize]
    [HttpGet("my")]
    public async Task<ActionResult<IReadOnlyCollection<ProjectSummaryResponse>>> GetMyProjects(CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        var projects = await _dbContext.Projects
            .AsNoTracking()
            .Where(project => project.OwnerId == userId)
            .Include(project => project.Owner)
            .Include(project => project.Branches)
            .Include(project => project.Commits)
            .OrderByDescending(project => project.UpdatedAt)
            .ToListAsync(cancellationToken);

        return Ok(projects.Select(project => project.ToSummaryResponse()).ToArray());
    }

    [AllowAnonymous]
    [HttpGet("public")]
    public async Task<ActionResult<IReadOnlyCollection<ProjectSummaryResponse>>> GetPublicProjects(CancellationToken cancellationToken)
    {
        var projects = await _dbContext.Projects
            .AsNoTracking()
            .Where(project => project.Visibility == ProjectVisibility.Public)
            .Include(project => project.Owner)
            .Include(project => project.Branches)
            .Include(project => project.Commits)
            .OrderByDescending(project => project.UpdatedAt)
            .ToListAsync(cancellationToken);

        return Ok(projects.Select(project => project.ToSummaryResponse()).ToArray());
    }

    [AllowAnonymous]
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyCollection<ProjectSummaryResponse>>> SearchProjects([FromQuery] string? query, CancellationToken cancellationToken)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return Ok(Array.Empty<ProjectSummaryResponse>());
        }

        var queryLower = normalizedQuery.ToLowerInvariant();
        var projects = await _dbContext.Projects
            .AsNoTracking()
            .Where(project => project.Visibility == ProjectVisibility.Public &&
                              (project.Name.ToLower().Contains(queryLower) || project.Description.ToLower().Contains(queryLower)))
            .Include(project => project.Owner)
            .Include(project => project.Branches)
            .Include(project => project.Commits)
            .OrderByDescending(project => project.UpdatedAt)
            .ToListAsync(cancellationToken);

        return Ok(projects.Select(project => project.ToSummaryResponse()).ToArray());
    }

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<ProjectDetailsResponse>> CreateProject(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        var projectName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return BadRequest("Введите название проекта.");
        }

        var project = new Project
        {
            Name = projectName,
            Description = request.Description.Trim(),
            Visibility = request.Visibility,
            OwnerId = userId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var mainBranch = new Branch
        {
            ProjectId = project.Id,
            Name = "main",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Branches.Add(mainBranch);
        await _dbContext.SaveChangesAsync(cancellationToken);

        project.DefaultBranchId = mainBranch.Id;
        project.ActiveBranchId = mainBranch.Id;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var createdProject = await LoadProjectForDetailsAsync(project.Id, userId, cancellationToken);
        return CreatedAtAction(nameof(GetProjectById), new { id = project.Id }, createdProject!.ToDetailsResponse());
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDetailsResponse>> GetProjectById(Guid id, CancellationToken cancellationToken)
    {
        var userId = TryGetCurrentUserId();
        var project = await LoadProjectForDetailsAsync(id, userId, cancellationToken);
        if (project is null)
        {
            return NotFound();
        }

        return Ok(project.ToDetailsResponse());
    }

    [Authorize]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProjectDetailsResponse>> UpdateProject(Guid id, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        var project = await _dbContext.Projects
            .FirstOrDefaultAsync(item => item.Id == id && item.OwnerId == userId, cancellationToken);

        if (project is null)
        {
            return NotFound();
        }

        var projectName = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return BadRequest("Введите название проекта.");
        }

        project.Name = projectName;
        project.Description = request.Description.Trim();
        project.Visibility = request.Visibility;
        project.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var updatedProject = await LoadProjectForDetailsAsync(id, userId, cancellationToken);
        return Ok(updatedProject!.ToDetailsResponse());
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

    private Task<Project?> LoadProjectForDetailsAsync(Guid projectId, Guid? userId, CancellationToken cancellationToken) =>
        _dbContext.Projects
            .AsNoTracking()
            .Where(project => project.Id == projectId && (project.Visibility == ProjectVisibility.Public || project.OwnerId == userId))
            .Include(project => project.Owner)
            .Include(project => project.Branches)
                .ThenInclude(branch => branch.Commits)
            .Include(project => project.Commits)
                .ThenInclude(commit => commit.Author)
            .Include(project => project.Commits)
                .ThenInclude(commit => commit.Branch)
            .FirstOrDefaultAsync(cancellationToken);
}
