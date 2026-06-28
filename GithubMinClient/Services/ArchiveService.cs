using System.IO;
using System.IO.Compression;

namespace GithubMinClient.Services;

public class ArchiveService
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        "bin",
        "obj"
    };

    private static readonly HashSet<string> ExcludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".suo",
        ".user"
    };

    public Task<string> CreateProjectArchiveAsync(string workingDirectory)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                throw new InvalidOperationException("Рабочая директория не найдена.");
            }

            var tempArchivePath = Path.Combine(Path.GetTempPath(), $"githubmin-{Guid.NewGuid():N}.zip");
            using var archive = ZipFile.Open(tempArchivePath, ZipArchiveMode.Create);

            foreach (var filePath in Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories))
            {
                if (ShouldSkipFile(workingDirectory, filePath))
                {
                    continue;
                }

                var entryName = Path.GetRelativePath(workingDirectory, filePath).Replace('\\', '/');
                archive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Fastest);
            }

            return tempArchivePath;
        });
    }

    public void TryDeleteArchive(string archivePath)
    {
        try
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
        catch
        {
            // Очистка временного архива выполняется по возможности.
        }
    }

    public Task ExtractArchiveAsync(string archivePath, string destinationDirectory)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                throw new InvalidOperationException("Архив снапшота не найден.");
            }

            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new InvalidOperationException("Не выбрана директория для восстановления.");
            }

            Directory.CreateDirectory(destinationDirectory);

            using var archive = ZipFile.OpenRead(archivePath);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var normalizedName = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                var targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, normalizedName));
                var rootPath = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
                if (!targetPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Архив содержит небезопасные пути к файлам.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }
        });
    }

    private static bool ShouldSkipFile(string rootDirectory, string filePath)
    {
        var relativePath = Path.GetRelativePath(rootDirectory, filePath);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (segments.Any(segment => ExcludedDirectories.Contains(segment)))
        {
            return true;
        }

        return ExcludedExtensions.Contains(Path.GetExtension(filePath));
    }
}
