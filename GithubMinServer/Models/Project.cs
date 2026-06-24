namespace GithubMinServer.Models;

public class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public ProjectVisibility Visibility { get; set; } = ProjectVisibility.Private;
    public Guid? DefaultBranchId { get; set; }
    public Guid? ActiveBranchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User? Owner { get; set; }
    public Branch? DefaultBranch { get; set; }
    public Branch? ActiveBranch { get; set; }
    public ICollection<Branch> Branches { get; set; } = new List<Branch>();
    public ICollection<Commit> Commits { get; set; } = new List<Commit>();
}
