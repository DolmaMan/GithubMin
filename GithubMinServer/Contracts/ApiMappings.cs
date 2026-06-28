using GithubMinServer.Models;

namespace GithubMinServer.Contracts;

public static class ApiMappings
{
    public static UserResponse ToResponse(this User user) =>
        new(user.Id, user.Username, user.Email, user.CreatedAt);

    public static PublicUserResponse ToPublicResponse(this User user) =>
        new(user.Id, user.Username, user.CreatedAt);

    public static ProjectSummaryResponse ToSummaryResponse(this Project project) =>
        new(
            project.Id,
            project.Name,
            project.Description,
            project.Visibility,
            project.OwnerId,
            project.Owner?.Username ?? string.Empty,
            project.DefaultBranchId,
            project.ActiveBranchId,
            project.Branches.Count,
            project.Commits.Count,
            project.CreatedAt,
            project.UpdatedAt);

    public static ProjectDetailsResponse ToDetailsResponse(this Project project) =>
        new(
            project.Id,
            project.Name,
            project.Description,
            project.Visibility,
            project.OwnerId,
            project.Owner?.Username ?? string.Empty,
            project.DefaultBranchId,
            project.ActiveBranchId,
            project.CreatedAt,
            project.UpdatedAt,
            project.Branches
                .OrderBy(branch => branch.Name)
                .Select(branch => branch.ToResponse(project.ActiveBranchId))
                .ToArray(),
            project.Commits
                .OrderByDescending(commit => commit.CreatedAt)
                .Take(20)
                .Select(commit => commit.ToSummaryResponse())
                .ToArray());

    public static BranchResponse ToResponse(this Branch branch, Guid? activeBranchId) =>
        new(
            branch.Id,
            branch.ProjectId,
            branch.Name,
            branch.HeadCommitId,
            branch.CreatedFromCommitId,
            activeBranchId == branch.Id,
            branch.Commits.Count,
            branch.CreatedAt);

    public static CommitSummaryResponse ToSummaryResponse(this Commit commit) =>
        new(
            commit.Id,
            commit.ProjectId,
            commit.BranchId,
            commit.Branch?.Name ?? string.Empty,
            commit.ParentCommitId,
            commit.MergeParentCommitId,
            commit.AuthorId,
            commit.Author?.Username ?? string.Empty,
            commit.Message,
            commit.CreatedAt);

    public static CommitDetailsResponse ToDetailsResponse(this Commit commit) =>
        new(
            commit.Id,
            commit.ProjectId,
            commit.BranchId,
            commit.Branch?.Name ?? string.Empty,
            commit.ParentCommitId,
            commit.MergeParentCommitId,
            commit.AuthorId,
            commit.Author?.Username ?? string.Empty,
            commit.Message,
            Path.GetFileName(commit.SnapshotPath),
            commit.CreatedAt);
}
