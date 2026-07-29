namespace OnboardMe.Web.Models;

public sealed class RepositoryOverviewSnapshot
{
    public required string Owner { get; init; }

    public required string Repository { get; init; }

    public required RepositoryIndexingState State { get; init; }

    public required DateTimeOffset LastUpdatedUtc { get; init; }

    public required string Summary { get; init; }

    public required IReadOnlyList<RepositoryOverviewTopLevelItem> TopLevelItems { get; init; }

    public required IReadOnlyList<RepositoryOverviewNotableFile> NotableFiles { get; init; }
}

public sealed class RepositoryOverviewTopLevelItem
{
    public required string Name { get; init; }

    public required string Kind { get; init; }

    public required int FileCount { get; init; }
}

public sealed class RepositoryOverviewNotableFile
{
    public required string Path { get; init; }

    public required string Category { get; init; }
}
