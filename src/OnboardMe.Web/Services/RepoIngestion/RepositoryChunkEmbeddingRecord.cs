namespace OnboardMe.Web.Services.RepoIngestion;

public sealed class RepositoryChunkEmbeddingRecord
{
    public required string Owner { get; init; }

    public required string Repository { get; init; }

    public required string ChunkId { get; init; }

    public required string SourcePath { get; init; }

    public required string SourceSha { get; init; }

    public required int ChunkIndex { get; init; }

    public required int StartLine { get; init; }

    public required int EndLine { get; init; }

    public required string Strategy { get; init; }

    public required string Content { get; init; }

    public required IReadOnlyList<float> Embedding { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }
}
