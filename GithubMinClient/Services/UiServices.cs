using System.IO;
using System.Windows;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace GithubMinClient.Services;

public class NotificationService
{
    public void Info(string message) => System.Windows.MessageBox.Show(message, "Мини-гит", MessageBoxButton.OK, MessageBoxImage.Information);
    public void Error(string message) => System.Windows.MessageBox.Show(message, "Мини-гит", MessageBoxButton.OK, MessageBoxImage.Error);
    public bool Confirm(string message) => System.Windows.MessageBox.Show(message, "Мини-гит", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}

public class FileDialogService
{
    public string? SelectFolder(string description = "Выберите папку")
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public string? SelectZipSavePath(string defaultFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Архивы .zip (*.zip)|*.zip",
            FileName = Path.GetFileName(defaultFileName),
            AddExtension = true,
            DefaultExt = ".zip"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
