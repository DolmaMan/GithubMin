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
            await page.SetSelectedProjectAsync(page.CreateItem(new ProjectSummaryResponse
            {
                Id = project.Id,
                Name = project.Name,
                Description = project.Description,
                Visibility = project.Visibility,
                OwnerId = project.OwnerId,
                OwnerUsername = project.OwnerUsername,
                DefaultBranchId = project.DefaultBranchId,
                ActiveBranchId = project.ActiveBranchId,
                BranchCount = project.Branches.Count,
                CommitCount = project.RecentCommits.Count,
                CreatedAt = project.CreatedAt,
                UpdatedAt = project.UpdatedAt
            }));
            await page.LoadAsync();
            page.StatusMessage = "Проект создан.";
            Main.CloseDialog();
        });
    }
}

public partial class EditProjectDialogViewModel : DialogViewModelBase
{
    private readonly ProjectsPageViewModel? projectsPage;
    private readonly ProjectDetailsPageViewModel projectPage;

    public EditProjectDialogViewModel(MainViewModel main, ProjectsPageViewModel? projectsPage, ProjectDetailsPageViewModel projectPage) : base(main)
    {
        this.projectsPage = projectsPage;
        this.projectPage = projectPage;

        VisibilityOptions =
        [
            new ProjectVisibilityOption(ProjectVisibility.Private, "Приватный"),
            new ProjectVisibilityOption(ProjectVisibility.Public, "Публичный")
        ];

        Name = projectPage.Project?.Name ?? string.Empty;
        Description = projectPage.Project?.Description ?? string.Empty;
        Visibility = VisibilityOptions.First(option => option.Value == (projectPage.Project?.Visibility ?? ProjectVisibility.Private));
    }

    public IReadOnlyList<ProjectVisibilityOption> VisibilityOptions { get; }

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private ProjectVisibilityOption visibility;

    [RelayCommand]
    private async Task SaveAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                StatusMessage = "Введите название проекта.";
                return;
            }

            var updatedProject = await Main.ProjectService.UpdateProjectAsync(projectPage.ProjectId, new UpdateProjectRequest
            {
                Name = Name.Trim(),
                Description = Description.Trim(),
                Visibility = Visibility.Value
            });

            await projectPage.RefreshAsync();
            if (projectsPage is not null)
            {
                await projectsPage.SetSelectedProjectAsync(projectsPage.CreateItem(new ProjectSummaryResponse
                {
                    Id = updatedProject.Id,
                    Name = updatedProject.Name,
                    Description = updatedProject.Description,
                    Visibility = updatedProject.Visibility,
                    OwnerId = updatedProject.OwnerId,
                    OwnerUsername = updatedProject.OwnerUsername,
                    DefaultBranchId = updatedProject.DefaultBranchId,
                    ActiveBranchId = updatedProject.ActiveBranchId,
                    BranchCount = updatedProject.Branches.Count,
                    CommitCount = updatedProject.RecentCommits.Count,
                    CreatedAt = updatedProject.CreatedAt,
                    UpdatedAt = updatedProject.UpdatedAt
                }));
                projectsPage.StatusMessage = "Репозиторий обновлен.";
            }
            else
            {
                projectPage.StatusMessage = "Репозиторий обновлен.";
            }

            Main.CloseDialog();
        });
    }
}

public partial class MyRepositoriesDialogViewModel : DialogViewModelBase
{
    private readonly ProjectsPageViewModel page;

    public MyRepositoriesDialogViewModel(MainViewModel main, ProjectsPageViewModel page) : base(main)
    {
        this.page = page;
    }

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = [];

    [ObservableProperty]
    private ProjectItemViewModel? selectedProject;

    public async Task LoadAsync()
    {
        await RunSafelyAsync(async () =>
        {
            Projects.Clear();
            SelectedProject = null;

            var projects = await Main.ProjectService.GetMyProjectsAsync();
            foreach (var project in projects)
            {
                Projects.Add(page.CreateItem(project));
            }
        });
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

    [RelayCommand]
    private async Task ChooseProjectAsync()
    {
        if (SelectedProject is null)
        {
            StatusMessage = "Выберите репозиторий.";
            return;
        }

        await page.SetSelectedProjectAsync(SelectedProject);
        page.StatusMessage = $"Выбран репозиторий {SelectedProject.Name}.";
        Main.CloseDialog();
    }
}

public partial class RepositorySearchDialogViewModel : DialogViewModelBase
{
    private readonly ProjectsPageViewModel page;

    public RepositorySearchDialogViewModel(MainViewModel main, ProjectsPageViewModel page) : base(main)
    {
        this.page = page;
    }

    public ObservableCollection<ProjectItemViewModel> Projects { get; } = [];

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private ProjectItemViewModel? selectedProject;

    public async Task LoadAsync()
    {
        await SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await RunSafelyAsync(async () =>
        {
            Projects.Clear();
            SelectedProject = null;

            var projects = string.IsNullOrWhiteSpace(SearchQuery)
                ? await Main.ProjectService.GetPublicProjectsAsync()
                : await Main.ProjectService.SearchProjectsAsync(SearchQuery.Trim());

            foreach (var project in projects)
            {
                Projects.Add(page.CreateItem(project));
            }

            StatusMessage = projects.Count == 0 ? "Репозитории не найдены." : string.Empty;
        });
    }

    [RelayCommand]
    private async Task ChooseProjectAsync()
    {
        if (SelectedProject is null)
        {
            StatusMessage = "Выберите репозиторий.";
            return;
        }

        await page.SetSelectedProjectAsync(SelectedProject);
        page.StatusMessage = $"Выбран репозиторий {SelectedProject.Name}.";
        Main.CloseDialog();
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

            if (SelectedBranch.IsActive)
            {
                StatusMessage = "Эта ветка уже активна.";
                return;
            }

            if (!await page.EnsureNoUncommittedChangesBeforeBranchSwitchAsync())
            {
                StatusMessage = page.StatusMessage;
                return;
            }

            var switchedBranch = await Main.BranchService.SwitchBranchAsync(page.ProjectId, SelectedBranch.Id);
            var restoreCommitId = ProjectDetailsPageViewModel.GetRestoreCommitId(switchedBranch);
            if (!await page.ReplaceWorkingDirectoryWithCommitAsync(
                    restoreCommitId,
                    "Активная ветка переключена. Рабочая директория заменяется файлами последнего коммита ветки..."))
            {
                StatusMessage = page.StatusMessage;
                return;
            }

            await page.RefreshAsync();
            page.StatusMessage = restoreCommitId is null
                ? "Активная ветка переключена. Рабочая директория очищена, потому что в ветке нет коммитов."
                : "Активная ветка переключена. Рабочая директория заменена файлами последнего коммита ветки.";
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

            if (!await page.EnsureNoUncommittedChangesBeforeBranchSwitchAsync())
            {
                StatusMessage = page.StatusMessage;
                return;
            }

            var createdBranch = await Main.BranchService.CreateBranchAsync(page.ProjectId, new CreateBranchRequest
            {
                Name = NewBranchName.Trim(),
                StartFromCommitId = SelectedStartCommit?.Id
            });

            var switchedBranch = await Main.BranchService.SwitchBranchAsync(page.ProjectId, createdBranch.Id);
            var restoreCommitId = ProjectDetailsPageViewModel.GetRestoreCommitId(switchedBranch);
            if (!await page.ReplaceWorkingDirectoryWithCommitAsync(
                    restoreCommitId,
                    "Ветка создана. Рабочая директория заменяется файлами родительского коммита..."))
            {
                StatusMessage = page.StatusMessage;
                return;
            }

            await page.RefreshAsync();
            page.StatusMessage = restoreCommitId is null
                ? "Ветка создана и переключена. Рабочая директория очищена, потому что в ветке нет коммитов."
                : "Ветка создана и переключена. Рабочая директория заменена файлами родительского коммита.";
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

            if (!await page.EnsureNoUncommittedChangesBeforeBranchSwitchAsync())
            {
                StatusMessage = page.StatusMessage;
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

            if (response.MergeCommit is not null)
            {
                if (!await page.ReplaceWorkingDirectoryWithCommitAsync(
                    response.MergeCommit.Id,
                    "Слияние выполнено. Рабочая директория заменяется файлами merge-коммита..."))
                {
                    StatusMessage = page.StatusMessage;
                    await page.RefreshAsync();
                    return;
                }
            }

            await page.RefreshAsync();
            page.StatusMessage = response.Message;
            Main.CloseDialog();
        });
    }
}
