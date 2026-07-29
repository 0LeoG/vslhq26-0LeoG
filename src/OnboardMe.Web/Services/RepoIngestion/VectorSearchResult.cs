namespace OnboardMe.Web.Services.RepoIngestion;

/// <summary>
/// Holds a single result from a vector similarity search, pairing a chunk with its cosine similarity score.
/// </summary>
public sealed class VectorSearchResult
{
    /// <summary>The chunk record that matched the query embedding.</summary>
    public required RepositoryChunkEmbeddingRecord Chunk { get; init; }

    /// <summary>Cosine similarity score in the range [-1, 1]. Higher means more similar.</summary>
    public required float Score { get; init; }
}
