using System.Collections.Concurrent;

namespace OnboardMe.Web.Services.RepoIngestion;

public sealed class InMemoryRepositoryEmbeddingStore : IRepositoryEmbeddingStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, RepositoryChunkEmbeddingRecord>> embeddingsByRepository = new(StringComparer.OrdinalIgnoreCase);

    public Task ReplaceRepositoryEmbeddingsAsync(string owner, string repository, IReadOnlyList<RepositoryChunkEmbeddingRecord> embeddings, CancellationToken cancellationToken = default)
    {
        var byChunkId = new ConcurrentDictionary<string, RepositoryChunkEmbeddingRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var embedding in embeddings)
        {
            byChunkId[embedding.ChunkId] = embedding;
        }

        embeddingsByRepository[BuildKey(owner, repository)] = byChunkId;
        return Task.CompletedTask;
    }

    public Task UpsertRepositoryEmbeddingsAsync(string owner, string repository, IReadOnlyList<RepositoryChunkEmbeddingRecord> embeddings, CancellationToken cancellationToken = default)
    {
        if (embeddings.Count == 0)
        {
            return Task.CompletedTask;
        }

        var byChunkId = embeddingsByRepository.GetOrAdd(
            BuildKey(owner, repository),
            _ => new ConcurrentDictionary<string, RepositoryChunkEmbeddingRecord>(StringComparer.OrdinalIgnoreCase));

        foreach (var embedding in embeddings)
        {
            byChunkId[embedding.ChunkId] = embedding;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RepositoryChunkEmbeddingRecord>> GetRepositoryEmbeddingsAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        if (embeddingsByRepository.TryGetValue(BuildKey(owner, repository), out var embeddingsByChunkId))
        {
            return Task.FromResult<IReadOnlyList<RepositoryChunkEmbeddingRecord>>(embeddingsByChunkId.Values.ToArray());
        }

        return Task.FromResult<IReadOnlyList<RepositoryChunkEmbeddingRecord>>([]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VectorSearchResult>> SearchByEmbeddingAsync(
        string owner,
        string repository,
        IReadOnlyList<float> queryEmbedding,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        if (topK <= 0)
        {
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);
        }

        if (!embeddingsByRepository.TryGetValue(BuildKey(owner, repository), out var allChunksByChunkId)
            || allChunksByChunkId.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);
        }

        var allChunks = allChunksByChunkId.Values;

        var queryNorm = ComputeNorm(queryEmbedding);
        if (queryNorm == 0f)
        {
            return Task.FromResult<IReadOnlyList<VectorSearchResult>>([]);
        }

        // Score every chunk with cosine similarity, then take the top-K.
        var results = allChunks
            .Select(chunk => new VectorSearchResult
            {
                Chunk = chunk,
                Score = CosineSimilarity(queryEmbedding, queryNorm, chunk.Embedding)
            })
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToArray();

        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
    }

    /// <summary>Computes cosine similarity between the pre-normalised query and a stored chunk embedding.</summary>
    private static float CosineSimilarity(IReadOnlyList<float> query, float queryNorm, IReadOnlyList<float> stored)
    {
        var storedNorm = ComputeNorm(stored);
        if (storedNorm == 0f)
        {
            return 0f;
        }

        var dot = 0f;
        var length = Math.Min(query.Count, stored.Count);
        for (var i = 0; i < length; i++)
        {
            dot += query[i] * stored[i];
        }

        return dot / (queryNorm * storedNorm);
    }

    private static float ComputeNorm(IReadOnlyList<float> vector)
    {
        var sumOfSquares = 0f;
        for (var i = 0; i < vector.Count; i++)
        {
            sumOfSquares += vector[i] * vector[i];
        }

        return MathF.Sqrt(sumOfSquares);
    }

    private static string BuildKey(string owner, string repository) => $"{owner}/{repository}";
}
