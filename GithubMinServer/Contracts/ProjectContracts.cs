using System.ComponentModel.DataAnnotations;
using GithubMinServer.Models;

namespace GithubMinServer.Contracts;

public class CreateProjectRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public ProjectVisibility Visibility { get; set; } = ProjectVisibility.Private;
}

public class UpdateProjectRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000)]
    public string Description { get; set; } = string.Empty;

    [Required]
    public ProjectVisibility Visibility { get; set; } = ProjectVisibility.Private;
}

public record ProjectSummaryResponse(
    Guid Id,
    string Name,
    string Description,
    ProjectVisibility Visibility,
    Guid OwnerId,
    string OwnerUsername,
    Guid? DefaultBranchId,
    Guid? ActiveBranchId,
    int BranchCount,
    int CommitCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record ProjectDetailsResponse(
    Guid Id,
    string Name,
    string Description,
    ProjectVisibility Visibility,
    Guid OwnerId,
    string OwnerUsername,
    Guid? DefaultBranchId,
    Guid? ActiveBranchId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<BranchResponse> Branches,
    IReadOnlyCollection<CommitSummaryResponse> RecentCommits);
