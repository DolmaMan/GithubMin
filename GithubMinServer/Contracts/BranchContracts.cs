using System.ComponentModel.DataAnnotations;

namespace GithubMinServer.Contracts;

public class CreateBranchRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public Guid? StartFromCommitId { get; set; }
}

public class SwitchBranchRequest
{
    [Required]
    public Guid BranchId { get; set; }
}

public record BranchResponse(
    Guid Id,
    Guid ProjectId,
    string Name,
    Guid? HeadCommitId,
    Guid? CreatedFromCommitId,
    bool IsActive,
    int CommitCount,
    DateTimeOffset CreatedAt);
