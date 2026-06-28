using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

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
            var rootPath = EnsureTrailingSeparator(Path.GetFullPath(destinationDirectory));
            var archiveFullPath = Path.GetFullPath(archivePath);
            if (Path.GetPathRoot(rootPath)?.Equals(rootPath, StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new InvalidOperationException("Нельзя восстанавливать снапшот в корень диска.");
            }

            if (archiveFullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Архив снапшота не должен находиться внутри директории восстановления.");
            }

            using var archive = ZipFile.OpenRead(archivePath);
            ValidateArchiveEntries(archive, rootPath);
            ClearDirectory(destinationDirectory);

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var targetPath = GetSafeTargetPath(rootPath, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath);
            }
        });
    }

    public Task ClearDirectoryAsync(string destinationDirectory)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(destinationDirectory))
            {
                throw new InvalidOperationException("Не выбрана директория для восстановления.");
            }

            var rootPath = EnsureTrailingSeparator(Path.GetFullPath(destinationDirectory));
            if (Path.GetPathRoot(rootPath)?.Equals(rootPath, StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new InvalidOperationException("Нельзя очищать корень диска.");
            }

            Directory.CreateDirectory(destinationDirectory);
            ClearDirectory(destinationDirectory);
        });
    }

    public Task<bool> HasTrackedFilesAsync(string workingDirectory)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                throw new InvalidOperationException("Рабочая директория не найдена.");
            }

            return Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories)
                .Any(filePath => !ShouldSkipFile(workingDirectory, filePath));
        });
    }

    public Task<bool> HasChangesComparedToArchiveAsync(string archivePath, string workingDirectory)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                throw new InvalidOperationException("Рабочая директория не найдена.");
            }

            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                throw new InvalidOperationException("Архив снапшота не найден.");
            }

            using var archive = ZipFile.OpenRead(archivePath);
            var archiveEntries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .ToDictionary(entry => NormalizeEntryName(entry.FullName), StringComparer.OrdinalIgnoreCase);

            var localFiles = Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories)
                .Where(filePath => !ShouldSkipFile(workingDirectory, filePath))
                .Select(filePath => new
                {
                    Path = filePath,
                    RelativePath = NormalizeEntryName(Path.GetRelativePath(workingDirectory, filePath))
                })
                .ToArray();

            if (archiveEntries.Count != localFiles.Length)
            {
                return true;
            }

            foreach (var localFile in localFiles)
            {
                if (!archiveEntries.TryGetValue(localFile.RelativePath, out var archiveEntry))
                {
                    return true;
                }

                var localInfo = new FileInfo(localFile.Path);
                if (localInfo.Length != archiveEntry.Length)
                {
                    return true;
                }

                if (!HashesEqual(localFile.Path, archiveEntry))
                {
                    return true;
                }
            }

            return false;
        });
    }

    private static void ValidateArchiveEntries(ZipArchive archive, string rootPath)
    {
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            GetSafeTargetPath(rootPath, entry.FullName);
        }
    }

    private static string GetSafeTargetPath(string rootPath, string entryName)
    {
        var normalizedName = entryName.Replace('/', Path.DirectorySeparatorChar);
        var targetPath = Path.GetFullPath(Path.Combine(rootPath, normalizedName));
        if (!targetPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Архив содержит небезопасные пути к файлам.");
        }

        return targetPath;
    }

    private static string NormalizeEntryName(string path) => path.Replace('\\', '/');

    private static bool HashesEqual(string filePath, ZipArchiveEntry archiveEntry)
    {
        using var sha256 = SHA256.Create();
        using var fileStream = File.OpenRead(filePath);
        using var entryStream = archiveEntry.Open();
        var fileHash = sha256.ComputeHash(fileStream);
        var entryHash = sha256.ComputeHash(entryStream);
        return fileHash.AsSpan().SequenceEqual(entryHash);
    }

    private static void ClearDirectory(string directory)
    {
        foreach (var filePath in Directory.EnumerateFiles(directory))
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
            File.Delete(filePath);
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(directory))
        {
            ResetAttributes(directoryPath);
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static void ResetAttributes(string directory)
    {
        foreach (var filePath in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(directoryPath, FileAttributes.Normal);
        }

        File.SetAttributes(directory, FileAttributes.Normal);
    }

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;

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
