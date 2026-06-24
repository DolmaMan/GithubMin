namespace GithubMinServer.Models;

public class Commit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid BranchId { get; set; }
    public Guid? ParentCommitId { get; set; }
    public Guid? MergeParentCommitId { get; set; }
    public Guid AuthorId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SnapshotPath { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Project? Project { get; set; }
    public Branch? Branch { get; set; }
    public Commit? ParentCommit { get; set; }
    public Commit? MergeParentCommit { get; set; }
    public User? Author { get; set; }
}
