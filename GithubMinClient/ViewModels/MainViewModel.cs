using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubMinClient.Services;

namespace GithubMinClient.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public MainViewModel(
        AuthService authService,
        ProjectService projectService,
        BranchService branchService,
        CommitService commitService,
        MergeService mergeService,
        TokenStorageService tokenStorageService,
        LocalProjectStorageService localProjectStorageService,
        FileDialogService fileDialogService,
        NotificationService notificationService)
    {
        AuthService = authService;
        ProjectService = projectService;
        BranchService = branchService;
        CommitService = commitService;
        MergeService = mergeService;
        TokenStorageService = tokenStorageService;
        LocalProjectStorageService = localProjectStorageService;
        FileDialogService = fileDialogService;
        NotificationService = notificationService;

        CurrentPage = new LoginPageViewModel(this);
    }

    internal AuthService AuthService { get; }
    internal ProjectService ProjectService { get; }
    internal BranchService BranchService { get; }
    internal CommitService CommitService { get; }
    internal MergeService MergeService { get; }
    internal TokenStorageService TokenStorageService { get; }
    internal LocalProjectStorageService LocalProjectStorageService { get; }
    internal FileDialogService FileDialogService { get; }
    internal NotificationService NotificationService { get; }

    [ObservableProperty]
    private ObservableObject currentPage;

    public void ShowLogin()
    {
        TokenStorageService.Clear();
        CurrentPage = new LoginPageViewModel(this);
    }

    public void ShowRegister() => CurrentPage = new RegisterPageViewModel(this);

    public async Task ShowProjectsAsync()
    {
        var page = new ProjectsPageViewModel(this);
        CurrentPage = page;
        await page.LoadAsync();
    }

    public async Task ShowProjectDetailsAsync(ProjectItemViewModel project)
    {
        var page = new ProjectDetailsPageViewModel(this, project.Id);
        CurrentPage = page;
        await page.RefreshAsync();
    }
}
