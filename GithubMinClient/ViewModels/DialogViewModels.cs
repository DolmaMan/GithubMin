using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubMinClient.Models;
using GithubMinClient.Services;

namespace GithubMinClient.ViewModels;

public abstract partial class DialogViewModelBase : ObservableObject
{
    protected DialogViewModelBase(MainViewModel main)
    {
        Main = main;
    }

    protected MainViewModel Main { get; }

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [RelayCommand]
    private void Close() => Main.CloseDialog();

    protected async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            IsBusy = true;
            StatusMessage = string.Empty;
            await action();
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            Main.HandleAuthenticationExpired();
        }
        catch (ApiException exception)
        {
            StatusMessage = exception.Message;
        }
        catch (Exception exception)
        {
            StatusMessage = UserMessageTranslator.GetMessageException(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public partial class CreateProjectDialogViewModel : DialogViewModelBase
{
    private readonly ProjectsPageViewModel page;

    public CreateProjectDialogViewModel(MainViewModel main, ProjectsPageViewModel page) : base(main)
    {
        this.page = page;
        NewProjectVisibilityOption = VisibilityOptions[0];
    }

    public IReadOnlyList<ProjectVisibilityOption> VisibilityOptions { get; } =
    [
        new(ProjectVisibility.Private, "Приватный"),
        new(ProjectVisibility.Public, "Публичный")
    ];

    [ObservableProperty]
    private string newProjectName = string.Empty;

    [ObservableProperty]
    private string newProjectDescription = string.Empty;

    [ObservableProperty]
    private ProjectVisibilityOption newProjectVisibilityOption;

    [ObservableProperty]
    private string newProjectDirectory = string.Empty;

    [RelayCommand]
    private void ChooseDirectory()
    {
        var directory = Main.FileDialogService.SelectFolder("Выберите рабочую директорию проекта");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            NewProjectDirectory = directory;
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewProjectName))
            {
                StatusMessage = "Введите название проекта.";
                return;
            }

            if (string.IsNullOrWhiteSpace(NewProjectDirectory) || !Directory.Exists(NewProjectDirectory))
            {
                StatusMessage = "Выберите существующую рабочую директорию.";
                return;
            }

            var project = await Main.ProjectService.CreateProjectAsync(new CreateProjectRequest
            {
                Name = NewProjectName.Trim(),
                Description = NewProjectDescription.Trim(),
                Visibility = NewProjectVisibilityOption.Value
            });

            Main.LocalProjectStorageService.SetDirectory(project.Id, NewProjectDirectory);
            await page.LoadAsync();
            page.StatusMessage = "Проект создан.";
            Main.CloseDialog();
        });
    }
}

public partial class UserSearchDialogViewModel : DialogViewModelBase
{
    public UserSearchDialogViewModel(MainViewModel main) : base(main)
    {
    }

    public ObservableCollection<UserSearchItemViewModel> Users { get; } = [];
    public ObservableCollection<ProjectItemViewModel> UserProjects { get; } = [];

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private UserSearchItemViewModel? selectedUser;

    [ObservableProperty]
    private ProjectItemViewModel? selectedProject;

    [RelayCommand]
    private async Task SearchAsync()
    {
        await RunSafelyAsync(async () =>
        {
            Users.Clear();
            UserProjects.Clear();
            SelectedUser = null;
            SelectedProject = null;

            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                StatusMessage = "Введите имя пользователя для поиска.";
                return;
            }

            var users = await Main.UserService.SearchUsersAsync(SearchQuery.Trim());
            foreach (var user in users)
            {
                Users.Add(new UserSearchItemViewModel(user));
            }

            StatusMessage = users.Count == 0 ? "Пользователи не найдены." : string.Empty;
        });
    }

    [RelayCommand]
    private async Task LoadProjectsAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (SelectedUser is null)
            {
                StatusMessage = "Выберите пользователя.";
                return;
            }

            UserProjects.Clear();
            SelectedProject = null;
            var projects = await Main.ProjectService.GetUserPublicProjectsAsync(SelectedUser.Id);
            foreach (var project in projects)
            {
                UserProjects.Add(new ProjectItemViewModel(project, Main.LocalProjectStorageService.GetDirectory(project.Id)));
            }

            StatusMessage = projects.Count == 0
                ? "У выбранного пользователя нет публичных проектов."
                : $"Найдено {projects.Count} репозиториев пользователя {SelectedUser.Username}.";
        });
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (SelectedProject is null)
        {
            StatusMessage = "Выберите репозиторий.";
            return;
        }

        Main.CloseDialog();
        await Main.ShowProjectDetailsAsync(SelectedProject);
    }
}

public partial class ProjectDirectoryDialogViewModel : DialogViewModelBase
{
    private readonly ProjectDetailsPageViewModel page;

    public ProjectDirectoryDialogViewModel(MainViewModel main, ProjectDetailsPageViewModel page) : base(main)
    {
        this.page = page;
        DirectoryPath = page.WorkingDirectory;
    }

    [ObservableProperty]
    private string directoryPath = string.Empty;

    [RelayCommand]
    private void ChooseDirectory()
    {
        var directory = Main.FileDialogService.SelectFolder("Выберите рабочую директорию проекта");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            DirectoryPath = directory;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(DirectoryPath) || !Directory.Exists(DirectoryPath))
            {
                StatusMessage = "Выберите существующую директорию.";
                return;
            }

            Main.LocalProjectStorageService.SetDirectory(page.ProjectId, DirectoryPath);
            page.WorkingDirectory = DirectoryPath;
            page.StatusMessage = "Рабочая директория обновлена.";
            await page.RefreshAsync();
            Main.CloseDialog();
        });
    }
}

public partial class BranchManagementDialogViewModel : DialogViewModelBase
{
    private readonly ProjectDetailsPageViewModel page;

    public BranchManagementDialogViewModel(MainViewModel main, ProjectDetailsPageViewModel page) : base(main)
    {
        this.page = page;
        foreach (var branch in page.Branches)
        {
            Branches.Add(branch);
        }

        foreach (var commit in page.Commits)
        {
            Commits.Add(commit);
        }

        SelectedBranch = Branches.FirstOrDefault(branch => branch.IsActive) ?? Branches.FirstOrDefault();
    }

    public ObservableCollection<BranchResponse> Branches { get; } = [];
    public ObservableCollection<CommitSummaryResponse> Commits { get; } = [];

    [ObservableProperty]
    private BranchResponse? selectedBranch;

    [ObservableProperty]
    private string newBranchName = string.Empty;

    [ObservableProperty]
    private CommitSummaryResponse? selectedStartCommit;

    [RelayCommand]
    private async Task SwitchAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (SelectedBranch is null)
            {
                StatusMessage = "Выберите ветку.";
                return;
            }

            await Main.BranchService.SwitchBranchAsync(page.ProjectId, SelectedBranch.Id);
            await page.RefreshAsync();
            page.StatusMessage = "Активная ветка переключена. Локальные файлы не изменены.";
            Main.CloseDialog();
        });
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewBranchName))
            {
                StatusMessage = "Введите имя ветки.";
                return;
            }

            await Main.BranchService.CreateBranchAsync(page.ProjectId, new CreateBranchRequest
            {
                Name = NewBranchName.Trim(),
                StartFromCommitId = SelectedStartCommit?.Id
            });

            await page.RefreshAsync();
            page.StatusMessage = "Ветка создана.";
            Main.CloseDialog();
        });
    }
}

public partial class CreateCommitDialogViewModel : DialogViewModelBase
{
    private readonly ProjectDetailsPageViewModel page;

    public CreateCommitDialogViewModel(MainViewModel main, ProjectDetailsPageViewModel page) : base(main)
    {
        this.page = page;
        foreach (var branch in page.Branches)
        {
            Branches.Add(branch);
        }

        SelectedBranch = page.SelectedBranch ?? Branches.FirstOrDefault(branch => branch.IsActive) ?? Branches.FirstOrDefault();
        WorkingDirectory = page.WorkingDirectory;
    }

    public ObservableCollection<BranchResponse> Branches { get; } = [];

    [ObservableProperty]
    private BranchResponse? selectedBranch;

    [ObservableProperty]
    private string workingDirectory = string.Empty;

    [ObservableProperty]
    private string commitMessage = string.Empty;

    [RelayCommand]
    private void ChooseDirectory()
    {
        var directory = Main.FileDialogService.SelectFolder("Выберите рабочую директорию проекта");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            WorkingDirectory = directory;
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(CommitMessage))
            {
                StatusMessage = "Введите сообщение коммита.";
                return;
            }

            if (string.IsNullOrWhiteSpace(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
            {
                StatusMessage = "Выберите существующую рабочую директорию.";
                return;
            }

            page.WorkingDirectory = WorkingDirectory;
            Main.LocalProjectStorageService.SetDirectory(page.ProjectId, WorkingDirectory);
            await Main.CommitService.CreateCommitAsync(page.ProjectId, SelectedBranch?.Id, CommitMessage.Trim(), WorkingDirectory);
            await page.RefreshAsync();
            page.StatusMessage = "Коммит создан.";
            Main.CloseDialog();
        });
    }
}

public partial class MergeDialogViewModel : DialogViewModelBase
{
    private readonly ProjectDetailsPageViewModel page;

    public MergeDialogViewModel(MainViewModel main, ProjectDetailsPageViewModel page) : base(main)
    {
        this.page = page;
        foreach (var branch in page.Branches)
        {
            Branches.Add(branch);
        }

        SelectedTargetBranch = page.SelectedBranch ?? Branches.FirstOrDefault(branch => branch.IsActive) ?? Branches.FirstOrDefault();
        SelectedSourceBranch = Branches.FirstOrDefault(branch => branch.Id != SelectedTargetBranch?.Id) ?? Branches.FirstOrDefault();
    }

    public ObservableCollection<BranchResponse> Branches { get; } = [];

    [ObservableProperty]
    private BranchResponse? selectedSourceBranch;

    [ObservableProperty]
    private BranchResponse? selectedTargetBranch;

    [ObservableProperty]
    private string mergeMessage = string.Empty;

    [RelayCommand]
    private async Task MergeAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (SelectedSourceBranch is null || SelectedTargetBranch is null)
            {
                StatusMessage = "Выберите исходную и целевую ветки.";
                return;
            }

            var response = await Main.MergeService.MergeAsync(page.ProjectId, new MergeRequest
            {
                SourceBranchId = SelectedSourceBranch.Id,
                TargetBranchId = SelectedTargetBranch.Id,
                Message = string.IsNullOrWhiteSpace(MergeMessage) ? null : MergeMessage.Trim()
            });

            if (!response.Success)
            {
                StatusMessage = response.Message;
                return;
            }

            await page.RefreshAsync();
            page.StatusMessage = "Слияние выполнено.";
            Main.CloseDialog();
        });
    }
}
