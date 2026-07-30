namespace OnboardMe.Web.Models;

public sealed class RepositoryOverviewSnapshot
{
    public required string Owner { get; init; }

    public required string Repository { get; init; }

    public required RepositoryIndexingState State { get; init; }

    public required DateTimeOffset LastUpdatedUtc { get; init; }

    public required string Summary { get; init; }

    public required int TrackedFileCount { get; init; }

    public required int TopLevelDirectoryCount { get; init; }

    public required int TopLevelFileCount { get; init; }

    public required int IndexedFileCount { get; init; }

    public required int SkippedFileCount { get; init; }

    public required int FailedFileCount { get; init; }

    public required int EmbeddedChunkCount { get; init; }

    public required int ProcessedChunkCount { get; init; }

    public required double ProcessingCoveragePercent { get; init; }

    public required bool IsPartial { get; init; }

    public required IReadOnlyList<RepositoryOverviewTopLevelItem> TopLevelItems { get; init; }

    public required IReadOnlyList<RepositoryOverviewNotableFile> NotableFiles { get; init; }

    public required IReadOnlyList<RepositoryOverviewLanguageBreakdownItem> Languages { get; init; }

    public required IReadOnlyList<RepositoryOverviewSkipReasonBreakdownItem> SkipReasons { get; init; }
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

public sealed class RepositoryOverviewLanguageBreakdownItem
{
    public required string Language { get; init; }

    public required int FileCount { get; init; }
}

public sealed class RepositoryOverviewSkipReasonBreakdownItem
{
    public required string SkipReason { get; init; }

    public required int FileCount { get; init; }
}

public sealed class RepositoryOverviewAiSummary
{
    public required string Narrative { get; init; }

    public required IReadOnlyList<string> MainWorkflows { get; init; }

    public required IReadOnlyList<string> RisksAndUnknowns { get; init; }

    public required IReadOnlyList<RepositoryOverviewAiEntryPoint> EntryPoints { get; init; }

    public required IReadOnlyList<string> SourceFiles { get; init; }

    public required bool IsAiGenerated { get; init; }

    public string? FallbackReason { get; init; }
}

public sealed class RepositoryOverviewAiEntryPoint
{
    public required string Path { get; init; }

    public required string WhyItMatters { get; init; }
}
