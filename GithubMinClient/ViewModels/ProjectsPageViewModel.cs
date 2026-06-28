using System.IO;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubMinClient.Models;
using GithubMinClient.Services;

namespace GithubMinClient.ViewModels;

public partial class ProjectsPageViewModel : ObservableObject
{
    private readonly MainViewModel main;

    public ProjectsPageViewModel(MainViewModel main)
    {
        this.main = main;
    }

    [ObservableProperty]
    private ProjectItemViewModel? selectedProject;

    [ObservableProperty]
    private ProjectDetailsPageViewModel? selectedProjectPage;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    public bool HasSelectedProject => SelectedProjectPage is not null;
    public string SelectedProjectName => SelectedProjectPage?.Project?.Name ?? "Репозиторий не выбран";
    public string SelectedProjectOwner => SelectedProjectPage?.OwnerUsername ?? "-";
    public string SelectedProjectVisibility => SelectedProjectPage?.VisibilityText ?? "-";
    public string SelectedProjectDescription =>
        string.IsNullOrWhiteSpace(SelectedProjectPage?.Project?.Description)
            ? "Репозиторий не выбран"
            : SelectedProjectPage.Project.Description;
    public string SelectedProjectLocalStatus => string.IsNullOrWhiteSpace(SelectedProjectPage?.WorkingDirectory)
        ? "Локальная папка не привязана"
        : SelectedProjectPage.WorkingDirectory;
    public string SelectedProjectBranchCount => SelectedProjectPage is null ? "-" : SelectedProjectPage.BranchCount.ToString();
    public string SelectedProjectCommitCount => SelectedProjectPage is null ? "-" : SelectedProjectPage.CommitCount.ToString();
    public string SelectedProjectActiveBranch => SelectedProjectPage?.ActiveBranchName ?? "-";
    public bool CanEditSelectedProject =>
        SelectedProjectPage?.Project is not null &&
        main.TokenStorageService.IsCurrentUser(SelectedProjectPage.Project.OwnerId, SelectedProjectPage.Project.OwnerUsername);

    partial void OnSelectedProjectChanged(ProjectItemViewModel? value) => NotifySelectionChanged();

    partial void OnSelectedProjectPageChanging(ProjectDetailsPageViewModel? oldValue, ProjectDetailsPageViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= SelectedProjectPageOnPropertyChanged;
        }
    }

    partial void OnSelectedProjectPageChanged(ProjectDetailsPageViewModel? value)
    {
        if (value is not null)
        {
            value.PropertyChanged += SelectedProjectPageOnPropertyChanged;
        }

        NotifySelectionChanged();
    }

    public Task LoadAsync()
    {
        if (SelectedProjectPage is not null)
        {
            SelectedProjectPage.WorkingDirectory = main.LocalProjectStorageService.GetDirectory(SelectedProjectPage.ProjectId) ?? string.Empty;
            NotifySelectionChanged();
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task RefreshAsync() => RunSafelyAsync(async () =>
    {
        if (SelectedProjectPage is null)
        {
            StatusMessage = "Выберите репозиторий через одну из модалок.";
            return;
        }

        await SelectedProjectPage.RefreshAsync();
        SelectedProject = CreateItem(ToSummary(SelectedProjectPage.Project!));
        NotifySelectionChanged();
        StatusMessage = "Данные репозитория обновлены.";
    });

    [RelayCommand]
    private async Task OpenMyRepositoriesDialogAsync()
    {
        var dialog = new MyRepositoriesDialogViewModel(main, this);
        main.ShowDialog(dialog);
        await dialog.LoadAsync();
    }

    [RelayCommand]
    private async Task OpenRepositorySearchDialogAsync()
    {
        var dialog = new RepositorySearchDialogViewModel(main, this);
        main.ShowDialog(dialog);
        await dialog.LoadAsync();
    }

    [RelayCommand]
    private void OpenCreateProjectDialog() => main.ShowDialog(new CreateProjectDialogViewModel(main, this));

    [RelayCommand(CanExecute = nameof(CanEditSelectedProject))]
    private void OpenEditProjectDialog()
    {
        if (SelectedProjectPage is null)
        {
            StatusMessage = "Сначала выберите репозиторий.";
            return;
        }

        if (!EnsureCanEditSelectedProject())
        {
            return;
        }

        main.ShowDialog(new EditProjectDialogViewModel(main, this, SelectedProjectPage));
    }

    [RelayCommand]
    private async Task OpenSelectedProjectAsync()
    {
        if (SelectedProject is null)
        {
            StatusMessage = "Сначала выберите репозиторий.";
            return;
        }

        await main.ShowProjectDetailsAsync(SelectedProject);
    }

    [RelayCommand(CanExecute = nameof(CanEditSelectedProject))]
    private void AttachDirectoryToSelectedProject()
    {
        if (SelectedProject is null)
        {
            StatusMessage = "Сначала выберите репозиторий.";
            return;
        }

        if (!EnsureCanEditSelectedProject())
        {
            return;
        }

        var directory = main.FileDialogService.SelectFolder("Выберите локальную рабочую директорию");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        main.LocalProjectStorageService.SetDirectory(SelectedProject.Id, directory);
        SelectedProject.LocalWorkingDirectory = directory;
        if (SelectedProjectPage is not null)
        {
            SelectedProjectPage.WorkingDirectory = directory;
        }

        NotifySelectionChanged();
        StatusMessage = "Локальная директория привязана.";
    }

    [RelayCommand]
    private void Logout() => main.Logout();

    internal async Task SetSelectedProjectAsync(ProjectItemViewModel project)
    {
        var page = new ProjectDetailsPageViewModel(main, project.Id);
        await page.RefreshAsync();
        SelectedProjectPage = page;
        SelectedProject = CreateItem(ToSummary(page.Project!));
        NotifySelectionChanged();
    }

    internal ProjectItemViewModel CreateItem(ProjectSummaryResponse project) =>
        new(project, main.LocalProjectStorageService.GetDirectory(project.Id));

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            IsBusy = true;
            StatusMessage = string.Empty;
            await action();
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            main.HandleAuthenticationExpired();
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

    private static ProjectSummaryResponse ToSummary(ProjectDetailsResponse details) =>
        new()
        {
            Id = details.Id,
            Name = details.Name,
            Description = details.Description,
            Visibility = details.Visibility,
            OwnerId = details.OwnerId,
            OwnerUsername = details.OwnerUsername,
            DefaultBranchId = details.DefaultBranchId,
            ActiveBranchId = details.ActiveBranchId,
            BranchCount = details.Branches.Count,
            CommitCount = details.RecentCommits.Count,
            CreatedAt = details.CreatedAt,
            UpdatedAt = details.UpdatedAt
        };

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelectedProject));
        OnPropertyChanged(nameof(SelectedProjectName));
        OnPropertyChanged(nameof(SelectedProjectOwner));
        OnPropertyChanged(nameof(SelectedProjectVisibility));
        OnPropertyChanged(nameof(SelectedProjectDescription));
        OnPropertyChanged(nameof(SelectedProjectLocalStatus));
        OnPropertyChanged(nameof(SelectedProjectBranchCount));
        OnPropertyChanged(nameof(SelectedProjectCommitCount));
        OnPropertyChanged(nameof(SelectedProjectActiveBranch));
        OnPropertyChanged(nameof(CanEditSelectedProject));
        OpenEditProjectDialogCommand.NotifyCanExecuteChanged();
        AttachDirectoryToSelectedProjectCommand.NotifyCanExecuteChanged();
    }

    private bool EnsureCanEditSelectedProject()
    {
        if (CanEditSelectedProject)
        {
            return true;
        }

        StatusMessage = "Этот репозиторий доступен только для просмотра. Изменять его может только владелец.";
        return false;
    }

    private void SelectedProjectPageOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => NotifySelectionChanged();
}

public record ProjectVisibilityOption(ProjectVisibility Value, string Text);
