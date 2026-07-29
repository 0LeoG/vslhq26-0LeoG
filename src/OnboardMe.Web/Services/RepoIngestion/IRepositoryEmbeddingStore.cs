namespace OnboardMe.Web.Services.RepoIngestion;

public interface IRepositoryEmbeddingStore
{
    Task ReplaceRepositoryEmbeddingsAsync(string owner, string repository, IReadOnlyList<RepositoryChunkEmbeddingRecord> embeddings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryChunkEmbeddingRecord>> GetRepositoryEmbeddingsAsync(string owner, string repository, CancellationToken cancellationToken = default);
}
