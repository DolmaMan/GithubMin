using System.Windows;
using GithubMinClient.Services;
using GithubMinClient.ViewModels;

namespace GithubMinClient;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var tokenStorage = new TokenStorageService();
        var apiClient = new ApiClient(tokenStorage);
        var notificationService = new NotificationService();
        var fileDialogService = new FileDialogService();
        var localProjectStorageService = new LocalProjectStorageService();
        var archiveService = new ArchiveService();

        var authService = new AuthService(apiClient);
        var projectService = new ProjectService(apiClient);
        var userService = new UserService(apiClient);
        var branchService = new BranchService(apiClient);
        var commitService = new CommitService(apiClient, archiveService);
        var mergeService = new MergeService(apiClient);

        var viewModel = new MainViewModel(
            authService,
            projectService,
            userService,
            branchService,
            commitService,
            mergeService,
            tokenStorage,
            localProjectStorageService,
            fileDialogService,
            notificationService);

        var window = new MainWindow
        {
            DataContext = viewModel
        };
        window.Show();
        await viewModel.InitializeAsync();
    }
}
