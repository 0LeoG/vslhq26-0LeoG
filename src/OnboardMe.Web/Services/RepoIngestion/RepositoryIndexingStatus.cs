namespace OnboardMe.Web.Services.RepoIngestion;

public sealed class RepositoryIndexingStatus
{
    public required string Owner { get; init; }

    public required string Repository { get; init; }

    public string Branch { get; set; } = "unknown";

    public required DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public RepositoryIndexingState State { get; set; }

    public string? ErrorMessage { get; set; }

    public List<RepositoryFileIngestionRecord> Files { get; } = [];

    public int EmbeddedChunkCount { get; set; }

    public int TotalFileCount { get; set; }

    public int ProcessedFileCount { get; set; }

    public int ProcessedChunkCount { get; set; }

    public string? CurrentFilePath { get; set; }

    public int IndexedCount => Files.Count(file => file.Status == RepositoryFileIndexStatus.Indexed);

    public int SkippedCount => Files.Count(file => file.Status == RepositoryFileIndexStatus.Skipped);

    public int FailedCount => Files.Count(file => file.Status == RepositoryFileIndexStatus.Failed);
}
