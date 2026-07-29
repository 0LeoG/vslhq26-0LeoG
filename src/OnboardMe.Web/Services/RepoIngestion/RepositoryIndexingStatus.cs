namespace OnboardMe.Web.Services.RepoIngestion;

public enum RepositoryIndexingState
{
    Running,
    Completed,
    CompletedWithErrors,
    Failed
}

public enum RepositoryFileIndexStatus
{
    Indexed,
    Skipped,
    Failed
}

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

    public int IndexedCount => Files.Count(file => file.Status == RepositoryFileIndexStatus.Indexed);

    public int SkippedCount => Files.Count(file => file.Status == RepositoryFileIndexStatus.Skipped);

    public int FailedCount => Files.Count(file => file.Status == RepositoryFileIndexStatus.Failed);
}

public sealed class RepositoryFileIngestionRecord
{
    public required string Path { get; init; }

    public required string Sha { get; init; }

    public long SizeBytes { get; init; }

    public required string Extension { get; init; }

    public required string Language { get; init; }

    public required RepositoryFileIndexStatus Status { get; set; }

    public string? SkipReason { get; set; }

    public string? ErrorMessage { get; set; }

    public string? Content { get; set; }
}
