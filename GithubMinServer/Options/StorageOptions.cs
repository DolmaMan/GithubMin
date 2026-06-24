namespace GithubMinServer.Options;

public class StorageOptions
{
    public const string SectionName = "Storage";

    public string RootPath { get; set; } = "Storage";
    public int MaxArchiveSizeMb { get; set; } = 200;
}
