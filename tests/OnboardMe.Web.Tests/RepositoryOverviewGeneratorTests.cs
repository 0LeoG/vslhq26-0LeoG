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
            State = RepositoryIndexingState.Completed
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
            Status = RepositoryFileIndexStatus.Skipped
        });

        var overview = RepositoryOverviewGenerator.Create(status);

        Assert.Equal(2, overview.TopLevelItems.Count(item => item.Kind == "File"));
        Assert.Equal(2, overview.TopLevelItems.Count(item => item.Kind == "Directory"));
        Assert.Contains(overview.TopLevelItems, item => item.Name == "src" && item.Kind == "Directory" && item.FileCount == 2);
        Assert.Contains(overview.TopLevelItems, item => item.Name == "README.md" && item.Kind == "File");

        Assert.Contains(overview.NotableFiles, file => file.Path == "README.md" && file.Category == "README");
        Assert.Contains(overview.NotableFiles, file => file.Path == "appsettings.json" && file.Category == "Config");
        Assert.Contains(overview.NotableFiles, file => file.Path == "src/Program.cs" && file.Category == "Entry point");
        Assert.Contains(overview.NotableFiles, file => file.Path == "src/Services/RepoIngestionService.cs" && file.Category == "Main service");
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
            State = RepositoryIndexingState.Running
        };

        var overview = RepositoryOverviewGenerator.Create(status);

        Assert.Equal(started, overview.LastUpdatedUtc);
    }
}
