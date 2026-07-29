namespace OnboardMe.Web.Services.RepoIngestion;

public interface IRepositoryEmbeddingStore
{
    Task ReplaceRepositoryEmbeddingsAsync(string owner, string repository, IReadOnlyList<RepositoryChunkEmbeddingRecord> embeddings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryChunkEmbeddingRecord>> GetRepositoryEmbeddingsAsync(string owner, string repository, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <paramref name="topK"/> most similar chunks for the given <paramref name="owner"/>/<paramref name="repository"/>
    /// workspace, ranked by cosine similarity to <paramref name="queryEmbedding"/>. Results never bleed across repos.
    /// </summary>
    Task<IReadOnlyList<VectorSearchResult>> SearchByEmbeddingAsync(
        string owner,
        string repository,
        IReadOnlyList<float> queryEmbedding,
        int topK = 5,
        CancellationToken cancellationToken = default);
}
