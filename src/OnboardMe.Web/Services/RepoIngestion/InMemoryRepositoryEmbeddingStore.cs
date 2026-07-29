using System.Collections.Concurrent;

namespace OnboardMe.Web.Services.RepoIngestion;

public sealed class InMemoryRepositoryEmbeddingStore : IRepositoryEmbeddingStore
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<RepositoryChunkEmbeddingRecord>> embeddingsByRepository = new(StringComparer.OrdinalIgnoreCase);

    public Task ReplaceRepositoryEmbeddingsAsync(string owner, string repository, IReadOnlyList<RepositoryChunkEmbeddingRecord> embeddings, CancellationToken cancellationToken = default)
    {
        embeddingsByRepository[BuildKey(owner, repository)] = embeddings.ToArray();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RepositoryChunkEmbeddingRecord>> GetRepositoryEmbeddingsAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        if (embeddingsByRepository.TryGetValue(BuildKey(owner, repository), out var embeddings))
        {
            return Task.FromResult(embeddings);
        }

        return Task.FromResult<IReadOnlyList<RepositoryChunkEmbeddingRecord>>([]);
    }

    private static string BuildKey(string owner, string repository) => $"{owner}/{repository}";
}
