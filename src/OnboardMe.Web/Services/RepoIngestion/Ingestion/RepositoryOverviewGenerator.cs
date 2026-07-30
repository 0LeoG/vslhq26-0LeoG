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
            .Select(file => file.Path.Replace('\\', '/'))
            .Where(path => path.Length > 0)
            .GroupBy(
                path => path.Split('/', StringSplitOptions.RemoveEmptyEntries)[0],
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var hasNestedPath = group.Any(path => path.Contains('/'));
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

        var languages = files
            .Where(file => !string.IsNullOrWhiteSpace(file.Language))
            .GroupBy(file => file.Language.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new RepositoryOverviewLanguageBreakdownItem
            {
                Language = group.Key,
                FileCount = group.Count()
            })
            .OrderByDescending(item => item.FileCount)
            .ThenBy(item => item.Language, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        var skipReasons = files
            .Where(file => file.Status == RepositoryFileIndexStatus.Skipped)
            .GroupBy(file => string.IsNullOrWhiteSpace(file.SkipReason) ? "unspecified" : file.SkipReason!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new RepositoryOverviewSkipReasonBreakdownItem
            {
                SkipReason = group.Key,
                FileCount = group.Count()
            })
            .OrderByDescending(item => item.FileCount)
            .ThenBy(item => item.SkipReason, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var topLevelFolderCount = topLevelItems.Count(item => item.Kind == "Directory");
        var topLevelFileCount = topLevelItems.Count(item => item.Kind == "File");
        var expectedFileCount = status.TotalFileCount > 0 ? status.TotalFileCount : files.Count;
        var processingCoveragePercent = expectedFileCount == 0
            ? 0
            : Math.Round(status.ProcessedFileCount * 100d / expectedFileCount, 1);

        var isPartial = status.State is not (RepositoryIndexingState.Completed or RepositoryIndexingState.CompletedWithErrors)
                        || status.FailedCount > 0
                        || processingCoveragePercent < 100;

        var summary = $"{status.Owner}/{status.Repository} has {files.Count} tracked files " +
                      $"across {topLevelFolderCount} top-level folders and {topLevelFileCount} top-level files. " +
                      $"{status.IndexedCount} indexed, {status.SkippedCount} skipped, {status.FailedCount} failed. " +
                      $"{processingCoveragePercent:0.0}% of discovered files were processed.";

        return new RepositoryOverviewSnapshot
        {
            Owner = status.Owner,
            Repository = status.Repository,
            State = status.State,
            LastUpdatedUtc = status.CompletedAtUtc ?? status.StartedAtUtc,
            Summary = summary,
            TrackedFileCount = files.Count,
            TopLevelDirectoryCount = topLevelFolderCount,
            TopLevelFileCount = topLevelFileCount,
            IndexedFileCount = status.IndexedCount,
            SkippedFileCount = status.SkippedCount,
            FailedFileCount = status.FailedCount,
            EmbeddedChunkCount = status.EmbeddedChunkCount,
            ProcessedChunkCount = status.ProcessedChunkCount,
            ProcessingCoveragePercent = processingCoveragePercent,
            IsPartial = isPartial,
            TopLevelItems = topLevelItems,
            NotableFiles = notableFiles,
            Languages = languages,
            SkipReasons = skipReasons
        };
    }

    private static string? GetNotableCategory(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var fileName = normalizedPath.Split('/').Last();

        if (fileName.StartsWith("readme", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("contributing", StringComparison.OrdinalIgnoreCase))
        {
            return "Documentation";
        }

        if (fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return "Solution";
        }

        if (fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
        {
            return "Project definition";
        }

        if (normalizedPath.StartsWith(".github/workflows/", StringComparison.OrdinalIgnoreCase))
        {
            return "CI workflow";
        }

        if (ConfigFileNames.Any(name => string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase)))
        {
            return "Config";
        }

        if (EntryPointFileNames.Any(name => string.Equals(fileName, name, StringComparison.OrdinalIgnoreCase)))
        {
            return "Entry point";
        }

        if (normalizedPath.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("tests/", StringComparison.OrdinalIgnoreCase))
        {
            return "Tests";
        }

        if (normalizedPath.Contains("/controllers/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/endpoints/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return "API surface";
        }

        if (normalizedPath.Contains("/services/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("services/", StringComparison.OrdinalIgnoreCase))
        {
            return "Main service";
        }

        return null;
    }
}
