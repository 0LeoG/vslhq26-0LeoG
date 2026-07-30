using OnboardMe.Web.Services.RepoIngestion;

namespace OnboardMe.Web.Tests;

public class RepositoryOverviewGeneratorTests
{
    [Fact]
    public void Create_BuildsTopLevelItemsAndNotableFiles()
    {
        var status = new RepositoryIndexingStatus
        {
            Owner = "octocat",
            Repository = "hello-world",
            StartedAtUtc = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
            CompletedAtUtc = new DateTimeOffset(2026, 7, 1, 12, 5, 0, TimeSpan.Zero),
            State = RepositoryIndexingState.Completed,
            TotalFileCount = 5,
            ProcessedFileCount = 5,
            ProcessedChunkCount = 7,
            EmbeddedChunkCount = 7
        };
        status.Files.Add(new RepositoryFileIngestionRecord
        {
            Path = "README.md",
            Sha = "sha-readme",
            SizeBytes = 10,
            Extension = ".md",
            Language = "Markdown",
            Status = RepositoryFileIndexStatus.Indexed
        });
        status.Files.Add(new RepositoryFileIngestionRecord
        {
            Path = "appsettings.json",
            Sha = "sha-config",
            SizeBytes = 10,
            Extension = ".json",
            Language = "JSON",
            Status = RepositoryFileIndexStatus.Indexed
        });
        status.Files.Add(new RepositoryFileIngestionRecord
        {
            Path = "src/Program.cs",
            Sha = "sha-program",
            SizeBytes = 10,
            Extension = ".cs",
            Language = "C#",
            Status = RepositoryFileIndexStatus.Indexed
        });
        status.Files.Add(new RepositoryFileIngestionRecord
        {
            Path = "src/Services/RepoIngestionService.cs",
            Sha = "sha-service",
            SizeBytes = 10,
            Extension = ".cs",
            Language = "C#",
            Status = RepositoryFileIndexStatus.Indexed
        });
        status.Files.Add(new RepositoryFileIngestionRecord
        {
            Path = "tests/RepoTests.cs",
            Sha = "sha-test",
            SizeBytes = 10,
            Extension = ".cs",
            Language = "C#",
            Status = RepositoryFileIndexStatus.Skipped,
            SkipReason = "generated-path"
        });

        var overview = RepositoryOverviewGenerator.Create(status);

        Assert.Equal(2, overview.TopLevelItems.Count(item => item.Kind == "File"));
        Assert.Equal(2, overview.TopLevelItems.Count(item => item.Kind == "Directory"));
        Assert.Contains(overview.TopLevelItems, item => item.Name == "src" && item.Kind == "Directory" && item.FileCount == 2);
        Assert.Contains(overview.TopLevelItems, item => item.Name == "README.md" && item.Kind == "File");

        Assert.Contains(overview.NotableFiles, file => file.Path == "README.md" && file.Category == "Documentation");
        Assert.Contains(overview.NotableFiles, file => file.Path == "appsettings.json" && file.Category == "Config");
        Assert.Contains(overview.NotableFiles, file => file.Path == "src/Program.cs" && file.Category == "Entry point");
        Assert.Contains(overview.NotableFiles, file => file.Path == "src/Services/RepoIngestionService.cs" && file.Category == "Main service");

        Assert.Equal(5, overview.TrackedFileCount);
        Assert.Equal(2, overview.TopLevelDirectoryCount);
        Assert.Equal(2, overview.TopLevelFileCount);
        Assert.Equal(4, overview.IndexedFileCount);
        Assert.Equal(1, overview.SkippedFileCount);
        Assert.Equal(0, overview.FailedFileCount);
        Assert.Equal(7, overview.EmbeddedChunkCount);
        Assert.Equal(7, overview.ProcessedChunkCount);
        Assert.Equal(100, overview.ProcessingCoveragePercent);
        Assert.False(overview.IsPartial);

        Assert.Contains(overview.Languages, item => item.Language == "C#" && item.FileCount == 3);
        Assert.Contains(overview.SkipReasons, item => item.SkipReason == "generated-path" && item.FileCount == 1);

        Assert.Equal(status.CompletedAtUtc, overview.LastUpdatedUtc);
        Assert.Contains("tracked files", overview.Summary);
    }

    [Fact]
    public void Create_UsesStartedTimeWhenCompletedTimeIsMissing()
    {
        var started = new DateTimeOffset(2026, 7, 2, 9, 0, 0, TimeSpan.Zero);
        var status = new RepositoryIndexingStatus
        {
            Owner = "octocat",
            Repository = "pending-index",
            StartedAtUtc = started,
            State = RepositoryIndexingState.Running,
            TotalFileCount = 10,
            ProcessedFileCount = 3
        };

        var overview = RepositoryOverviewGenerator.Create(status);

        Assert.Equal(started, overview.LastUpdatedUtc);
        Assert.True(overview.IsPartial);
        Assert.Equal(30, overview.ProcessingCoveragePercent);
    }
}
