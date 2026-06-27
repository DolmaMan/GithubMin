namespace GithubMinClient.Models;

public enum ProjectVisibility
{
    Private = 0,
    Public = 1
}

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public UserResponse User { get; set; } = new();
}

public class UserResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class CreateProjectRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ProjectVisibility Visibility { get; set; } = ProjectVisibility.Private;
}

public class UpdateProjectRequest : CreateProjectRequest;

public class ProjectSummaryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ProjectVisibility Visibility { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerUsername { get; set; } = string.Empty;
    public Guid? DefaultBranchId { get; set; }
    public Guid? ActiveBranchId { get; set; }
    public int BranchCount { get; set; }
    public int CommitCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class ProjectDetailsResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ProjectVisibility Visibility { get; set; }
    public Guid OwnerId { get; set; }
    public string OwnerUsername { get; set; } = string.Empty;
    public Guid? DefaultBranchId { get; set; }
    public Guid? ActiveBranchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<BranchResponse> Branches { get; set; } = [];
    public List<CommitSummaryResponse> RecentCommits { get; set; } = [];
}

public class CreateBranchRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? StartFromCommitId { get; set; }
}

public class SwitchBranchRequest
{
    public Guid BranchId { get; set; }
}

public class BranchResponse
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? HeadCommitId { get; set; }
    public Guid? CreatedFromCommitId { get; set; }
    public bool IsActive { get; set; }
    public int CommitCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class CommitSummaryResponse
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public Guid? ParentCommitId { get; set; }
    public Guid? MergeParentCommitId { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorUsername { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

public class MergeRequest
{
    public Guid SourceBranchId { get; set; }
    public Guid TargetBranchId { get; set; }
    public string? Message { get; set; }
}

public class MergeResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public CommitSummaryResponse? MergeCommit { get; set; }
    public List<string> Conflicts { get; set; } = [];
}

public class LocalProjectSettings
{
    public Guid ProjectId { get; set; }
    public string LocalWorkingDirectory { get; set; } = string.Empty;
}
