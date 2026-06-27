using System.Collections.ObjectModel;
using System.IO;
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
        newProjectVisibilityOption = VisibilityOptions[0];
    }

    public ObservableCollection<ProjectItemViewModel> MyProjects { get; } = [];
    public ObservableCollection<ProjectItemViewModel> PublicProjects { get; } = [];

    public IReadOnlyList<ProjectVisibilityOption> VisibilityOptions { get; } =
    [
        new(ProjectVisibility.Private, "Приватный"),
        new(ProjectVisibility.Public, "Публичный")
    ];

    [ObservableProperty]
    private ProjectItemViewModel? selectedMyProject;

    [ObservableProperty]
    private ProjectItemViewModel? selectedPublicProject;

    partial void OnSelectedMyProjectChanged(ProjectItemViewModel? value)
    {
        if (value is not null)
        {
            SelectedPublicProject = null;
        }
    }

    partial void OnSelectedPublicProjectChanged(ProjectItemViewModel? value)
    {
        if (value is not null)
        {
            SelectedMyProject = null;
        }
    }

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private string newProjectName = string.Empty;

    [ObservableProperty]
    private string newProjectDescription = string.Empty;

    [ObservableProperty]
    private ProjectVisibilityOption newProjectVisibilityOption;

    [ObservableProperty]
    private string newProjectDirectory = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    public async Task LoadAsync()
    {
        await RunSafelyAsync(async () =>
        {
            MyProjects.Clear();
            PublicProjects.Clear();

            var myProjects = await main.ProjectService.GetMyProjectsAsync();
            foreach (var project in myProjects)
            {
                MyProjects.Add(ToItem(project));
            }

            var publicProjects = await main.ProjectService.GetPublicProjectsAsync();
            foreach (var project in publicProjects)
            {
                PublicProjects.Add(ToItem(project));
            }
        });
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private async Task SearchAsync()
    {
        await RunSafelyAsync(async () =>
        {
            PublicProjects.Clear();
            var projects = string.IsNullOrWhiteSpace(SearchQuery)
                ? await main.ProjectService.GetPublicProjectsAsync()
                : await main.ProjectService.SearchProjectsAsync(SearchQuery.Trim());

            foreach (var project in projects)
            {
                PublicProjects.Add(ToItem(project));
            }
        });
    }

    [RelayCommand]
    private void ChooseNewProjectDirectory()
    {
        var directory = main.FileDialogService.SelectFolder("Выберите рабочую директорию проекта");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            NewProjectDirectory = directory;
        }
    }

    [RelayCommand]
    private async Task CreateProjectAsync()
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

            var project = await main.ProjectService.CreateProjectAsync(new CreateProjectRequest
            {
                Name = NewProjectName.Trim(),
                Description = NewProjectDescription.Trim(),
                Visibility = NewProjectVisibilityOption.Value
            });

            main.LocalProjectStorageService.SetDirectory(project.Id, NewProjectDirectory);
            NewProjectName = string.Empty;
            NewProjectDescription = string.Empty;
            NewProjectDirectory = string.Empty;
            NewProjectVisibilityOption = VisibilityOptions[0];
            StatusMessage = "Проект создан.";
            await LoadAsync();
        });
    }

    [RelayCommand]
    private async Task OpenSelectedProjectAsync()
    {
        var selectedProject = SelectedMyProject ?? SelectedPublicProject;
        if (selectedProject is null)
        {
            StatusMessage = "Выберите проект.";
            return;
        }

        await main.ShowProjectDetailsAsync(selectedProject);
    }

    [RelayCommand]
    private void AttachDirectoryToSelectedProject()
    {
        var selectedProject = SelectedMyProject ?? SelectedPublicProject;
        if (selectedProject is null)
        {
            StatusMessage = "Выберите проект.";
            return;
        }

        var directory = main.FileDialogService.SelectFolder("Выберите локальную рабочую директорию");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        main.LocalProjectStorageService.SetDirectory(selectedProject.Id, directory);
        selectedProject.LocalWorkingDirectory = directory;
        StatusMessage = "Локальная директория привязана.";
    }

    [RelayCommand]
    private void Logout() => main.ShowLogin();

    private ProjectItemViewModel ToItem(ProjectSummaryResponse project) =>
        new(project, main.LocalProjectStorageService.GetDirectory(project.Id));

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            IsBusy = true;
            StatusMessage = string.Empty;
            await action();
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

public record ProjectVisibilityOption(ProjectVisibility Value, string Text);
