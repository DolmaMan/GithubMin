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

        CurrentPage = tokenStorageService.HasActiveSession ? new ProjectsPageViewModel(this) : new LoginPageViewModel(this);
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

    [ObservableProperty]
    private ObservableObject? currentDialog;

    public bool HasDialog => CurrentDialog is not null;

    partial void OnCurrentDialogChanged(ObservableObject? value) => OnPropertyChanged(nameof(HasDialog));

    public void OpenLoginPage(string? statusMessage = null)
    {
        var page = new LoginPageViewModel(this);
        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            page.StatusMessage = statusMessage;
        }

        CurrentPage = page;
    }

    public void ShowRegister() => CurrentPage = new RegisterPageViewModel(this);

    public void Logout()
    {
        TokenStorageService.ClearAuthentication();
        OpenLoginPage();
    }

    public void ShowDialog(ObservableObject dialog) => CurrentDialog = dialog;

    public void CloseDialog() => CurrentDialog = null;

    public void HandleAuthenticationExpired(string? statusMessage = null)
    {
        TokenStorageService.ClearAuthentication();
        OpenLoginPage(statusMessage ?? "Сохраненная сессия завершилась. Войдите снова.");
    }

    public async Task InitializeAsync()
    {
        if (TokenStorageService.IsSessionExpired)
        {
            HandleAuthenticationExpired("Срок действия сохраненной сессии истек. Войдите снова.");
            return;
        }

        if (TokenStorageService.HasActiveSession)
        {
            await ShowProjectsAsync();
        }
    }

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
