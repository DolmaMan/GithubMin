using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GithubMinClient.Models;
using GithubMinClient.Services;

namespace GithubMinClient.ViewModels;

public partial class ProjectDetailsPageViewModel(MainViewModel main, Guid projectId) : ObservableObject
{
    public ObservableCollection<BranchResponse> Branches { get; } = [];
    public ObservableCollection<CommitSummaryResponse> Commits { get; } = [];

    [ObservableProperty]
    private ProjectDetailsResponse? project;

    [ObservableProperty]
    private BranchResponse? selectedBranch;

    [ObservableProperty]
    private BranchResponse? selectedSourceBranch;

    [ObservableProperty]
    private BranchResponse? selectedTargetBranch;

    [ObservableProperty]
    private CommitSummaryResponse? selectedCommit;

    [ObservableProperty]
    private CommitSummaryResponse? selectedStartCommit;

    [ObservableProperty]
    private string workingDirectory = string.Empty;

    [ObservableProperty]
    private string commitMessage = string.Empty;

    [ObservableProperty]
    private string newBranchName = string.Empty;

    [ObservableProperty]
    private string mergeMessage = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    public string Title => Project is null ? "Проект" : $"{Project.Name} ({GetVisibilityText(Project.Visibility)})";

    partial void OnProjectChanged(ProjectDetailsResponse? value) => OnPropertyChanged(nameof(Title));

    public async Task RefreshAsync()
    {
        await RunSafelyAsync(async () =>
        {
            Project = await main.ProjectService.GetProjectAsync(projectId);
            WorkingDirectory = main.LocalProjectStorageService.GetDirectory(projectId) ?? string.Empty;

            Branches.Clear();
            foreach (var branch in await main.BranchService.GetBranchesAsync(projectId))
            {
                Branches.Add(branch);
            }

            Commits.Clear();
            foreach (var commit in await main.CommitService.GetCommitsAsync(projectId))
            {
                Commits.Add(commit);
            }

            SelectedBranch = Branches.FirstOrDefault(branch => branch.IsActive) ?? Branches.FirstOrDefault();
            SelectedSourceBranch = Branches.FirstOrDefault(branch => branch.Id == SelectedSourceBranch?.Id) ?? Branches.FirstOrDefault();
            SelectedTargetBranch = Branches.FirstOrDefault(branch => branch.Id == SelectedTargetBranch?.Id) ?? SelectedBranch;
        });
    }

    [RelayCommand]
    private Task ReloadAsync() => RefreshAsync();

    [RelayCommand]
    private void ChangeDirectory()
    {
        var directory = main.FileDialogService.SelectFolder("Выберите рабочую директорию проекта");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        main.LocalProjectStorageService.SetDirectory(projectId, directory);
        WorkingDirectory = directory;
        StatusMessage = "Рабочая директория обновлена.";
    }

    [RelayCommand]
    private async Task CreateCommitAsync()
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
                var directory = main.FileDialogService.SelectFolder("Рабочая директория не найдена. Выберите новую папку");
                if (string.IsNullOrWhiteSpace(directory))
                {
                    StatusMessage = "Рабочая директория не выбрана.";
                    return;
                }

                main.LocalProjectStorageService.SetDirectory(projectId, directory);
                WorkingDirectory = directory;
            }

            StatusMessage = "Архивация и отправка коммита...";
            await main.CommitService.CreateCommitAsync(projectId, SelectedBranch?.Id, CommitMessage.Trim(), WorkingDirectory);
            CommitMessage = string.Empty;
            StatusMessage = "Коммит создан.";
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task CreateBranchAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewBranchName))
            {
                StatusMessage = "Введите имя ветки.";
                return;
            }

            await main.BranchService.CreateBranchAsync(projectId, new CreateBranchRequest
            {
                Name = NewBranchName.Trim(),
                StartFromCommitId = SelectedStartCommit?.Id
            });

            NewBranchName = string.Empty;
            SelectedStartCommit = null;
            StatusMessage = "Ветка создана.";
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task SwitchBranchAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (SelectedBranch is null)
            {
                StatusMessage = "Выберите ветку.";
                return;
            }

            await main.BranchService.SwitchBranchAsync(projectId, SelectedBranch.Id);
            StatusMessage = "Активная ветка переключена. Локальные файлы не изменены.";
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task DownloadCommitAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (SelectedCommit is null)
            {
                StatusMessage = "Выберите снапшот.";
                return;
            }

            var safeProjectName = string.Join("-", (Project?.Name ?? "проект").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            var path = main.FileDialogService.SelectZipSavePath($"{safeProjectName}-{SelectedCommit.Id.ToString()[..8]}.zip");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            await main.CommitService.DownloadCommitAsync(SelectedCommit.Id, path);
            StatusMessage = "Снапшот скачан. Рабочая директория не изменена.";
        });
    }

    [RelayCommand]
    private async Task RestoreCommitAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (SelectedCommit is null)
            {
                StatusMessage = "Выберите снапшот.";
                return;
            }

            if (string.IsNullOrWhiteSpace(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
            {
                var directory = main.FileDialogService.SelectFolder("Выберите директорию для восстановления снапшота");
                if (string.IsNullOrWhiteSpace(directory))
                {
                    StatusMessage = "Директория восстановления не выбрана.";
                    return;
                }

                main.LocalProjectStorageService.SetDirectory(projectId, directory);
                WorkingDirectory = directory;
            }

            var confirmed = main.NotificationService.Confirm("Восстановление снапшота перезапишет файлы с совпадающими именами в рабочей директории. Продолжить?");
            if (!confirmed)
            {
                return;
            }

            StatusMessage = "Скачивание и восстановление снапшота...";
            await main.CommitService.RestoreCommitAsync(SelectedCommit.Id, WorkingDirectory);
            StatusMessage = "Снапшот восстановлен в рабочую директорию.";
        });
    }

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

            var response = await main.MergeService.MergeAsync(projectId, new MergeRequest
            {
                SourceBranchId = SelectedSourceBranch.Id,
                TargetBranchId = SelectedTargetBranch.Id,
                Message = string.IsNullOrWhiteSpace(MergeMessage) ? null : MergeMessage.Trim()
            });

            MergeMessage = string.Empty;
            StatusMessage = response.Success ? "Слияние выполнено." : response.Message;
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task BackAsync() => await main.ShowProjectsAsync();

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

    private static string GetVisibilityText(ProjectVisibility visibility) =>
        visibility == ProjectVisibility.Public ? "Публичный" : "Приватный";
}
