namespace OnboardMe.Web.Services.RepoIngestion;

public interface IAzureOpenAiEmbeddingService
{
    Task<IReadOnlyList<RepositoryChunkEmbeddingRecord>> GenerateEmbeddingsAsync(
        string owner,
        string repository,
        IReadOnlyList<RepositoryContentChunk> chunks,
        CancellationToken cancellationToken = default);
}
