using System.ComponentModel.DataAnnotations;

namespace GithubMinServer.Contracts;

public class CreateCommitRequest
{
    public Guid? BranchId { get; set; }

    [Required]
    [StringLength(300, MinimumLength = 1)]
    public string Message { get; set; } = string.Empty;

    [Required]
    public IFormFile? Archive { get; set; }
}

public record CommitSummaryResponse(
    Guid Id,
    Guid ProjectId,
    Guid BranchId,
    string BranchName,
    Guid? ParentCommitId,
    Guid? MergeParentCommitId,
    Guid AuthorId,
    string AuthorUsername,
    string Message,
    DateTimeOffset CreatedAt);

public record CommitDetailsResponse(
    Guid Id,
    Guid ProjectId,
    Guid BranchId,
    string BranchName,
    Guid? ParentCommitId,
    Guid? MergeParentCommitId,
    Guid AuthorId,
    string AuthorUsername,
    string Message,
    string SnapshotFileName,
    DateTimeOffset CreatedAt);
