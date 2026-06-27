using System.IO;
using System.Text.Json;
using GithubMinClient.Models;

namespace GithubMinClient.Services;

public class LocalProjectStorageService
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GithubMinClient",
        "projects.json");

    public string? GetDirectory(Guid projectId)
    {
        return Load().FirstOrDefault(item => item.ProjectId == projectId)?.LocalWorkingDirectory;
    }

    public void SetDirectory(Guid projectId, string directory)
    {
        var items = Load();
        var item = items.FirstOrDefault(value => value.ProjectId == projectId);
        if (item is null)
        {
            items.Add(new LocalProjectSettings { ProjectId = projectId, LocalWorkingDirectory = directory });
        }
        else
        {
            item.LocalWorkingDirectory = directory;
        }

        Save(items);
    }

    private List<LocalProjectSettings> Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return [];
        }

        var json = File.ReadAllText(_settingsPath);
        return JsonSerializer.Deserialize<List<LocalProjectSettings>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }

    private void Save(List<LocalProjectSettings> items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }
}
