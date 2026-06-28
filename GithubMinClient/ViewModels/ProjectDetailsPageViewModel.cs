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

    public Guid ProjectId => projectId;

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
    public string OwnerUsername => Project?.OwnerUsername ?? string.Empty;
    public string VisibilityText => Project is null ? string.Empty : GetVisibilityText(Project.Visibility);
    public string ActiveBranchName => Branches.FirstOrDefault(branch => branch.IsActive)?.Name ?? "Не выбрана";
    public int BranchCount => Branches.Count;
    public int CommitCount => Commits.Count;
    public bool CanEditProject =>
        Project is not null && main.TokenStorageService.IsCurrentUser(Project.OwnerId, Project.OwnerUsername);

    partial void OnProjectChanged(ProjectDetailsResponse? value)
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(OwnerUsername));
        OnPropertyChanged(nameof(VisibilityText));
        OnPropertyChanged(nameof(CanEditProject));
        NotifyEditCommandStatesChanged();
    }

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
            OnPropertyChanged(nameof(ActiveBranchName));
            OnPropertyChanged(nameof(BranchCount));
            OnPropertyChanged(nameof(CommitCount));
        });
    }

    [RelayCommand]
    private Task ReloadAsync() => RefreshAsync();

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private void OpenDirectoryDialog()
    {
        if (!EnsureCanEditProject())
        {
            return;
        }

        main.ShowDialog(new ProjectDirectoryDialogViewModel(main, this));
    }

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private void OpenBranchDialog()
    {
        if (!EnsureCanEditProject())
        {
            return;
        }

        main.ShowDialog(new BranchManagementDialogViewModel(main, this));
    }

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private void OpenCommitDialog()
    {
        if (!EnsureCanEditProject())
        {
            return;
        }

        main.ShowDialog(new CreateCommitDialogViewModel(main, this));
    }

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private void OpenMergeDialog()
    {
        if (!EnsureCanEditProject())
        {
            return;
        }

        main.ShowDialog(new MergeDialogViewModel(main, this));
    }

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private void OpenEditProjectDialog()
    {
        if (!EnsureCanEditProject())
        {
            return;
        }

        main.ShowDialog(new EditProjectDialogViewModel(main, null, this));
    }

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private void ChangeDirectory()
    {
        if (!EnsureCanEditProject())
        {
            return;
        }

        var directory = main.FileDialogService.SelectFolder("Выберите рабочую директорию проекта");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        main.LocalProjectStorageService.SetDirectory(projectId, directory);
        WorkingDirectory = directory;
        StatusMessage = "Рабочая директория обновлена.";
    }

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private async Task CreateCommitAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (!EnsureCanEditProject())
            {
                return;
            }

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

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private async Task CreateBranchAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (!EnsureCanEditProject())
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(NewBranchName))
            {
                StatusMessage = "Введите имя ветки.";
                return;
            }

            if (!await EnsureNoUncommittedChangesBeforeBranchSwitchAsync())
            {
                return;
            }

            var createdBranch = await main.BranchService.CreateBranchAsync(projectId, new CreateBranchRequest
            {
                Name = NewBranchName.Trim(),
                StartFromCommitId = SelectedStartCommit?.Id
            });

            var switchedBranch = await main.BranchService.SwitchBranchAsync(projectId, createdBranch.Id);
            var restoreCommitId = GetRestoreCommitId(switchedBranch);
            if (!await ReplaceWorkingDirectoryWithCommitAsync(
                    restoreCommitId,
                    "Ветка создана. Рабочая директория заменяется файлами родительского коммита..."))
            {
                return;
            }

            NewBranchName = string.Empty;
            SelectedStartCommit = null;
            StatusMessage = restoreCommitId is null
                ? "Ветка создана и переключена. Рабочая директория очищена, потому что в ветке нет коммитов."
                : "Ветка создана и переключена. Рабочая директория заменена файлами родительского коммита.";
            await RefreshAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private async Task SwitchBranchAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (!EnsureCanEditProject())
            {
                return;
            }

            if (SelectedBranch is null)
            {
                StatusMessage = "Выберите ветку.";
                return;
            }

            var activeBranch = GetActiveBranch();
            if (activeBranch?.Id == SelectedBranch.Id)
            {
                StatusMessage = "Эта ветка уже активна.";
                return;
            }

            if (!await EnsureNoUncommittedChangesBeforeBranchSwitchAsync())
            {
                return;
            }

            var switchedBranch = await main.BranchService.SwitchBranchAsync(projectId, SelectedBranch.Id);
            var restoreCommitId = GetRestoreCommitId(switchedBranch);
            if (!await ReplaceWorkingDirectoryWithCommitAsync(
                    restoreCommitId,
                    "Активная ветка переключена. Рабочая директория заменяется файлами последнего коммита ветки..."))
            {
                return;
            }

            StatusMessage = restoreCommitId is null
                ? "Активная ветка переключена. Рабочая директория очищена, потому что в ветке нет коммитов."
                : "Активная ветка переключена. Рабочая директория заменена файлами последнего коммита ветки.";
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

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private async Task RestoreCommitAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (!EnsureCanEditProject())
            {
                return;
            }

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

            var confirmed = main.NotificationService.Confirm("Восстановление снапшота полностью очистит рабочую директорию и заполнит ее файлами выбранного снапшота. Продолжить?");
            if (!confirmed)
            {
                return;
            }

            StatusMessage = "Скачивание снапшота, очистка рабочей директории и восстановление файлов...";
            await main.CommitService.RestoreCommitAsync(SelectedCommit.Id, WorkingDirectory);
            StatusMessage = "Снапшот восстановлен в рабочую директорию.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanEditProject))]
    private async Task MergeAsync()
    {
        await RunSafelyAsync(async () =>
        {
            if (!EnsureCanEditProject())
            {
                return;
            }

            if (SelectedSourceBranch is null || SelectedTargetBranch is null)
            {
                StatusMessage = "Выберите исходную и целевую ветки.";
                return;
            }

            if (!await EnsureNoUncommittedChangesBeforeBranchSwitchAsync())
            {
                return;
            }

            var response = await main.MergeService.MergeAsync(projectId, new MergeRequest
            {
                SourceBranchId = SelectedSourceBranch.Id,
                TargetBranchId = SelectedTargetBranch.Id,
                Message = string.IsNullOrWhiteSpace(MergeMessage) ? null : MergeMessage.Trim()
            });

            MergeMessage = string.Empty;
            if (response.Success && response.MergeCommit is not null)
            {
                if (!await ReplaceWorkingDirectoryWithCommitAsync(
                    response.MergeCommit.Id,
                    "Слияние выполнено. Рабочая директория заменяется файлами merge-коммита..."))
                {
                    await RefreshAsync();
                    return;
                }
            }

            StatusMessage = response.Message;
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task BackAsync() => await main.ShowProjectsAsync();

    public async Task<bool> ReplaceWorkingDirectoryWithCommitAsync(Guid? commitId, string statusMessage)
    {
        if (string.IsNullOrWhiteSpace(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
        {
            var directory = main.FileDialogService.SelectFolder("Выберите рабочую директорию проекта");
            if (string.IsNullOrWhiteSpace(directory))
            {
                StatusMessage = "Рабочая директория не выбрана.";
                return false;
            }

            main.LocalProjectStorageService.SetDirectory(projectId, directory);
            WorkingDirectory = directory;
        }

        StatusMessage = statusMessage;
        await main.CommitService.ReplaceWorkingDirectoryWithCommitAsync(commitId, WorkingDirectory);
        return true;
    }

    public async Task<bool> EnsureNoUncommittedChangesBeforeBranchSwitchAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
        {
            var directory = main.FileDialogService.SelectFolder("Выберите рабочую директорию проекта");
            if (string.IsNullOrWhiteSpace(directory))
            {
                StatusMessage = "Рабочая директория не выбрана.";
                return false;
            }

            main.LocalProjectStorageService.SetDirectory(projectId, directory);
            WorkingDirectory = directory;
        }

        var activeBranch = GetActiveBranch();
        StatusMessage = "Проверка незакоммиченных изменений в текущей ветке...";
        var hasChanges = await main.CommitService.HasUncommittedChangesAsync(
            activeBranch is null ? null : GetRestoreCommitId(activeBranch),
            WorkingDirectory);
        if (!hasChanges)
        {
            return true;
        }

        StatusMessage = "В текущей ветке есть незакоммиченные изменения. Сначала создайте коммит, затем переключайте ветку.";
        return false;
    }

    private BranchResponse? GetActiveBranch() => Branches.FirstOrDefault(branch => branch.IsActive);

    public static Guid? GetRestoreCommitId(BranchResponse branch) => branch.HeadCommitId ?? branch.CreatedFromCommitId;

    private bool EnsureCanEditProject()
    {
        if (CanEditProject)
        {
            return true;
        }

        StatusMessage = "Этот репозиторий доступен только для просмотра. Изменять его может только владелец.";
        return false;
    }

    private void NotifyEditCommandStatesChanged()
    {
        OpenDirectoryDialogCommand.NotifyCanExecuteChanged();
        OpenBranchDialogCommand.NotifyCanExecuteChanged();
        OpenCommitDialogCommand.NotifyCanExecuteChanged();
        OpenMergeDialogCommand.NotifyCanExecuteChanged();
        OpenEditProjectDialogCommand.NotifyCanExecuteChanged();
        ChangeDirectoryCommand.NotifyCanExecuteChanged();
        CreateCommitCommand.NotifyCanExecuteChanged();
        CreateBranchCommand.NotifyCanExecuteChanged();
        SwitchBranchCommand.NotifyCanExecuteChanged();
        RestoreCommitCommand.NotifyCanExecuteChanged();
        MergeCommand.NotifyCanExecuteChanged();
    }

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
