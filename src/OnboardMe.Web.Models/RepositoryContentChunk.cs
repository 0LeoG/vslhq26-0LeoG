namespace OnboardMe.Web.Models;

public sealed class RepositoryContentChunk
{
    public required string ChunkId { get; init; }

    public required string SourcePath { get; init; }

    public required string SourceSha { get; init; }

    public required int ChunkIndex { get; init; }

    public required string Strategy { get; init; }

    public required int StartLine { get; init; }

    public required int EndLine { get; init; }

    public required string Content { get; init; }
}
