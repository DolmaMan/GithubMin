using CommunityToolkit.Mvvm.ComponentModel;
using GithubMinClient.Models;

namespace GithubMinClient.ViewModels;

public partial class ProjectItemViewModel(ProjectSummaryResponse project, string? localDirectory) : ObservableObject
{
    public ProjectSummaryResponse Project { get; } = project;

    [ObservableProperty]
    private string? localWorkingDirectory = localDirectory;

    public Guid Id => Project.Id;
    public string Name => Project.Name;
    public string Description => Project.Description;
    public ProjectVisibility Visibility => Project.Visibility;
    public string VisibilityText => Visibility == ProjectVisibility.Public ? "Публичный" : "Приватный";
    public string OwnerUsername => Project.OwnerUsername;
    public int BranchCount => Project.BranchCount;
    public int CommitCount => Project.CommitCount;
    public DateTimeOffset UpdatedAt => Project.UpdatedAt;
    public bool HasLocalDirectory => !string.IsNullOrWhiteSpace(LocalWorkingDirectory);
    public string LocalStatus => HasLocalDirectory ? LocalWorkingDirectory! : "Локальная папка не привязана";

    partial void OnLocalWorkingDirectoryChanged(string? value)
    {
        OnPropertyChanged(nameof(HasLocalDirectory));
        OnPropertyChanged(nameof(LocalStatus));
    }
}
