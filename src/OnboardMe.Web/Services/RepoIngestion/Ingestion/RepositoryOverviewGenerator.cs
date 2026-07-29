namespace OnboardMe.Web.Services.RepoIngestion;

public static class RepositoryOverviewGenerator
{
    private static readonly string[] ConfigFileNames =
    [
        "appsettings.json",
        "appsettings.development.json",
        "appsettings.example.json",
        ".env",
        ".env.example",
        "package.json",
        "docker-compose.yml",
        "dockerfile"
    ];

    private static readonly string[] EntryPointFileNames =
    [
        "program.cs",
        "main.cs",
        "main.py",
        "app.py",
        "index.js",
        "main.js",
        "index.ts",
        "main.ts"
    ];

    public static RepositoryOverviewSnapshot Create(RepositoryIndexingStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var files = status.Files
            .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var topLevelItems = files
            .GroupBy(
                file => file.Path.Split('/', StringSplitOptions.RemoveEmptyEntries)[0],
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var hasNestedPath = group.Any(file => file.Path.Contains('/'));
                return new RepositoryOverviewTopLevelItem
                {
                    Name = group.Key,
                    Kind = hasNestedPath ? "Directory" : "File",
                    FileCount = group.Count()
                };
            })
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var notableFiles = files
            .Select(file => new
            {
                file.Path,
                Category = GetNotableCategory(file.Path)
            })
            .Where(file => file.Category is not null)
            .Select(file => new RepositoryOverviewNotableFile
            {
                Path = file.Path,
                Category = file.Category!
            })
            .Take(20)
            .ToList();

        var topLevelFolderCount = topLevelItems.Count(item => item.Kind == "Directory");
        var topLevelFileCount = topLevelItems.Count(item => item.Kind == "File");
        var summary = $"{status.Owner}/{status.Repository} has {files.Count} tracked files " +
                      $"across {topLevelFolderCount} top-level folders and {topLevelFileCount} top-level files. " +
                      $"{status.IndexedCount} indexed, {status.SkippedCount} skipped, {status.FailedCount} failed.";

        return new RepositoryOverviewSnapshot
        {
            Owner = status.Owner,
            Repository = status.Repository,
            State = status.State,
            LastUpdatedUtc = status.CompletedAtUtc ?? status.StartedAtUtc,
            Summary = summary,
            TopLevelItems = topLevelItems,
            NotableFiles = notableFiles
        };
    }

    private static string? GetNotableCategory(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var fileName = normalizedPath.Split('/').Last();

        if (fileName.StartsWith("readme", StringComparison.OrdinalIgnoreCase))
        {
            return "README";
        }

        if (ConfigFileNames.Any(name => string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase)))
        {
            return "Config";
        }

        if (EntryPointFileNames.Any(name => string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase)))
        {
            return "Entry point";
        }

        if (normalizedPath.Contains("/services/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("services/", StringComparison.OrdinalIgnoreCase))
        {
            return "Main service";
        }

        return null;
    }
}
