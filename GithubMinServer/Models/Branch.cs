namespace GithubMinServer.Models;

public class Branch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? HeadCommitId { get; set; }
    public Guid? CreatedFromCommitId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Project? Project { get; set; }
    public Commit? HeadCommit { get; set; }
    public Commit? CreatedFromCommit { get; set; }
    public ICollection<Commit> Commits { get; set; } = new List<Commit>();
}
