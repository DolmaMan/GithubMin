using System.ComponentModel.DataAnnotations;

namespace GithubMinServer.Contracts;

public class MergeRequest
{
    [Required]
    public Guid SourceBranchId { get; set; }

    [Required]
    public Guid TargetBranchId { get; set; }

    [StringLength(300)]
    public string? Message { get; set; }
}

public record MergeResponse(
    bool Success,
    string Message,
    CommitSummaryResponse? MergeCommit,
    IReadOnlyCollection<string> Conflicts);
