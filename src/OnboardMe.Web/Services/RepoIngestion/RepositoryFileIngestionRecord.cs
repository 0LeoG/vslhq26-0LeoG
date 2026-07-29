namespace OnboardMe.Web.Services.RepoIngestion;

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