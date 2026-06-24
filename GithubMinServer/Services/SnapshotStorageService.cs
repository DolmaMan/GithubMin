using System.IO.Compression;
using GithubMinServer.Options;
using Microsoft.Extensions.Options;

namespace GithubMinServer.Services;

public class SnapshotStorageService(IWebHostEnvironment environment, IOptions<StorageOptions> options)
{
    private readonly IWebHostEnvironment _environment = environment;
    private readonly StorageOptions _options = options.Value;

    public void EnsureStorageRootExists()
    {
        Directory.CreateDirectory(GetStorageRoot());
    }

    public async Task<string> SaveUploadedArchiveAsync(Guid projectId, Guid commitId, IFormFile archive, CancellationToken cancellationToken)
    {
        if (archive.Length == 0)
        {
            throw new InvalidOperationException("Archive is empty.");
        }

        var maxBytes = _options.MaxArchiveSizeMb * 1024L * 1024L;
        if (archive.Length > maxBytes)
        {
            throw new InvalidOperationException($"Archive exceeds the limit of {_options.MaxArchiveSizeMb} MB.");
        }

        var relativePath = BuildRelativeSnapshotPath(projectId, commitId);
        var absolutePath = GetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using (var fileStream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var inputStream = archive.OpenReadStream())
        {
            await inputStream.CopyToAsync(fileStream, cancellationToken);
        }

        ValidateZipFile(absolutePath);
        return relativePath;
    }

    public async Task<string> SaveArchiveFromFilesAsync(
        Guid projectId,
        Guid commitId,
        IReadOnlyDictionary<string, byte[]> files,
        CancellationToken cancellationToken)
    {
        var relativePath = BuildRelativeSnapshotPath(projectId, commitId);
        var absolutePath = GetAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using var fileStream = new FileStream(absolutePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var file in files.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            var entry = archive.CreateEntry(NormalizeEntryName(file.Key), CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await entryStream.WriteAsync(file.Value, cancellationToken);
        }

        return relativePath;
    }

    public async Task<Dictionary<string, byte[]>> ReadSnapshotAsync(string relativePath, CancellationToken cancellationToken)
    {
        var absolutePath = GetAbsolutePath(relativePath);
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("Snapshot archive was not found.", absolutePath);
        }

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        await using var fileStream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            await using var entryStream = entry.Open();
            using var memoryStream = new MemoryStream();
            await entryStream.CopyToAsync(memoryStream, cancellationToken);
            files[NormalizeEntryName(entry.FullName)] = memoryStream.ToArray();
        }

        return files;
    }

    public string GetArchiveAbsolutePath(string relativePath) => GetAbsolutePath(relativePath);

    private string BuildRelativeSnapshotPath(Guid projectId, Guid commitId) =>
        Path.Combine("projects", projectId.ToString(), "commits", commitId.ToString(), "snapshot.zip");

    private string GetAbsolutePath(string relativePath) =>
        Path.Combine(GetStorageRoot(), relativePath);

    private string GetStorageRoot()
    {
        var rootPath = Path.IsPathRooted(_options.RootPath)
            ? _options.RootPath
            : Path.Combine(_environment.ContentRootPath, _options.RootPath);

        return rootPath;
    }

    private static void ValidateZipFile(string absolutePath)
    {
        try
        {
            using var stream = new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            _ = archive.Entries.Count;
        }
        catch
        {
            File.Delete(absolutePath);
            throw new InvalidOperationException("Uploaded file is not a valid ZIP archive.");
        }
    }

    private static string NormalizeEntryName(string path) => path.Replace('\\', '/');
}
