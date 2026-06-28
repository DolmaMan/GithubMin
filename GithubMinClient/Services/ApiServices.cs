using System.IO;
using GithubMinClient.Models;

namespace GithubMinClient.Services;

public class AuthService(ApiClient apiClient)
{
    public Task<AuthResponse> RegisterAsync(RegisterRequest request) => apiClient.PostAsync<AuthResponse>("api/auth/register", request);
    public Task<AuthResponse> LoginAsync(LoginRequest request) => apiClient.PostAsync<AuthResponse>("api/auth/login", request);
}

public class ProjectService(ApiClient apiClient)
{
    public Task<List<ProjectSummaryResponse>> GetMyProjectsAsync() => apiClient.GetAsync<List<ProjectSummaryResponse>>("api/projects/my");
    public Task<List<ProjectSummaryResponse>> GetPublicProjectsAsync() => apiClient.GetAsync<List<ProjectSummaryResponse>>("api/projects/public");
    public Task<List<ProjectSummaryResponse>> SearchProjectsAsync(string query) => apiClient.GetAsync<List<ProjectSummaryResponse>>($"api/projects/search?query={Uri.EscapeDataString(query)}");
    public Task<List<ProjectSummaryResponse>> GetUserPublicProjectsAsync(Guid userId) => apiClient.GetAsync<List<ProjectSummaryResponse>>($"api/users/{userId}/projects/public");
    public Task<ProjectDetailsResponse> CreateProjectAsync(CreateProjectRequest request) => apiClient.PostAsync<ProjectDetailsResponse>("api/projects", request);
    public Task<ProjectDetailsResponse> GetProjectAsync(Guid projectId) => apiClient.GetAsync<ProjectDetailsResponse>($"api/projects/{projectId}");
}

public class UserService(ApiClient apiClient)
{
    public Task<List<PublicUserResponse>> SearchUsersAsync(string query) => apiClient.GetAsync<List<PublicUserResponse>>($"api/users/search?query={Uri.EscapeDataString(query)}");
}

public class BranchService(ApiClient apiClient)
{
    public Task<List<BranchResponse>> GetBranchesAsync(Guid projectId) => apiClient.GetAsync<List<BranchResponse>>($"api/projects/{projectId}/branches");
    public Task<BranchResponse> CreateBranchAsync(Guid projectId, CreateBranchRequest request) => apiClient.PostAsync<BranchResponse>($"api/projects/{projectId}/branches", request);
    public Task<BranchResponse> SwitchBranchAsync(Guid projectId, Guid branchId) => apiClient.PostAsync<BranchResponse>($"api/projects/{projectId}/branches/switch", new SwitchBranchRequest { BranchId = branchId });
}

public class CommitService(ApiClient apiClient, ArchiveService archiveService)
{
    private const long MaxArchiveSizeBytes = 4096L * 1024L * 1024L;

    public Task<List<CommitSummaryResponse>> GetCommitsAsync(Guid projectId, Guid? branchId = null)
    {
        var url = branchId.HasValue
            ? $"api/projects/{projectId}/commits?branchId={branchId.Value}"
            : $"api/projects/{projectId}/commits";

        return apiClient.GetAsync<List<CommitSummaryResponse>>(url);
    }

    public async Task<CommitSummaryResponse> CreateCommitAsync(Guid projectId, Guid? branchId, string message, string workingDirectory)
    {
        var archivePath = await archiveService.CreateProjectArchiveAsync(workingDirectory);
        try
        {
            var archiveSize = new FileInfo(archivePath).Length;
            if (archiveSize > MaxArchiveSizeBytes)
            {
                throw new InvalidOperationException("Созданный архив превышает лимит 4096 МБ. Уменьшите рабочую директорию или исключите лишние файлы.");
            }

            return await apiClient.PostMultipartAsync<CommitSummaryResponse>($"api/projects/{projectId}/commits", message, branchId, archivePath);
        }
        finally
        {
            archiveService.TryDeleteArchive(archivePath);
        }
    }

    public Task DownloadCommitAsync(Guid commitId, string destinationPath) => apiClient.DownloadAsync($"api/commits/{commitId}/download", destinationPath);

    public async Task RestoreCommitAsync(Guid commitId, string workingDirectory)
    {
        var archivePath = Path.Combine(Path.GetTempPath(), $"githubmin-restore-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadCommitAsync(commitId, archivePath);
            await archiveService.ExtractArchiveAsync(archivePath, workingDirectory);
        }
        finally
        {
            archiveService.TryDeleteArchive(archivePath);
        }
    }

    public async Task ReplaceWorkingDirectoryWithCommitAsync(Guid? commitId, string workingDirectory)
    {
        if (commitId is null)
        {
            await archiveService.ClearDirectoryAsync(workingDirectory);
            return;
        }

        await RestoreCommitAsync(commitId.Value, workingDirectory);
    }

    public async Task<bool> HasUncommittedChangesAsync(Guid? baselineCommitId, string workingDirectory)
    {
        if (baselineCommitId is null)
        {
            return await archiveService.HasTrackedFilesAsync(workingDirectory);
        }

        var archivePath = Path.Combine(Path.GetTempPath(), $"githubmin-compare-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadCommitAsync(baselineCommitId.Value, archivePath);
            return await archiveService.HasChangesComparedToArchiveAsync(archivePath, workingDirectory);
        }
        finally
        {
            archiveService.TryDeleteArchive(archivePath);
        }
    }
}

public class MergeService(ApiClient apiClient)
{
    public Task<MergeResponse> MergeAsync(Guid projectId, MergeRequest request) => apiClient.PostAsync<MergeResponse>($"api/projects/{projectId}/merge", request);
}
