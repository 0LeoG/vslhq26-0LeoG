using OnboardMe.Web.Services.RepoIngestion;

namespace OnboardMe.Web.Tests;

public class InMemoryRepositoryEmbeddingStoreTests
{
    // Helper: build a minimal embedding record with a given float[] vector.
    private static RepositoryChunkEmbeddingRecord MakeRecord(string owner, string repo, string chunkId, float[] embedding)
        => new()
        {
            Owner = owner,
            Repository = repo,
            ChunkId = chunkId,
            SourcePath = "src/File.cs",
            SourceSha = "abc123",
            ChunkIndex = 0,
            StartLine = 1,
            EndLine = 5,
            Strategy = "code-block",
            Content = $"content for {chunkId}",
            Embedding = embedding,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

    [Fact]
    public async Task SearchByEmbeddingAsync_ReturnsResultsRankedBySimilarity()
    {
        var store = new InMemoryRepositoryEmbeddingStore();

        // Three chunks: one parallel (most similar), one perpendicular (score ≈ 0), one anti-parallel (least similar).
        await store.ReplaceRepositoryEmbeddingsAsync("octocat", "hello-world",
        [
            MakeRecord("octocat", "hello-world", "chunk-parallel",      [1f, 0f]),
            MakeRecord("octocat", "hello-world", "chunk-perpendicular", [0f, 1f]),
            MakeRecord("octocat", "hello-world", "chunk-anti",          [-1f, 0f])
        ]);

        var results = await store.SearchByEmbeddingAsync("octocat", "hello-world", queryEmbedding: [1f, 0f], topK: 3);

        Assert.Equal(3, results.Count);
        Assert.Equal("chunk-parallel",      results[0].Chunk.ChunkId);
        Assert.Equal("chunk-perpendicular", results[1].Chunk.ChunkId);
        Assert.Equal("chunk-anti",          results[2].Chunk.ChunkId);

        Assert.True(results[0].Score > results[1].Score);
        Assert.True(results[1].Score > results[2].Score);
    }

    [Fact]
    public async Task SearchByEmbeddingAsync_RespectsTopK()
    {
        var store = new InMemoryRepositoryEmbeddingStore();

        await store.ReplaceRepositoryEmbeddingsAsync("octocat", "hello-world",
        [
            MakeRecord("octocat", "hello-world", "c1", [1f, 0f]),
            MakeRecord("octocat", "hello-world", "c2", [0.9f, 0.1f]),
            MakeRecord("octocat", "hello-world", "c3", [0f, 1f])
        ]);

        var results = await store.SearchByEmbeddingAsync("octocat", "hello-world", queryEmbedding: [1f, 0f], topK: 2);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchByEmbeddingAsync_IsolatesResultsByRepo()
    {
        var store = new InMemoryRepositoryEmbeddingStore();

        await store.ReplaceRepositoryEmbeddingsAsync("octocat", "repo-a",
        [
            MakeRecord("octocat", "repo-a", "a-chunk", [1f, 0f])
        ]);

        await store.ReplaceRepositoryEmbeddingsAsync("octocat", "repo-b",
        [
            MakeRecord("octocat", "repo-b", "b-chunk", [1f, 0f])
        ]);

        var results = await store.SearchByEmbeddingAsync("octocat", "repo-a", queryEmbedding: [1f, 0f], topK: 10);

        Assert.Single(results);
        Assert.Equal("a-chunk", results[0].Chunk.ChunkId);
    }

    [Fact]
    public async Task SearchByEmbeddingAsync_ReturnsEmptyWhenNoEmbeddingsStored()
    {
        var store = new InMemoryRepositoryEmbeddingStore();

        var results = await store.SearchByEmbeddingAsync("octocat", "hello-world", queryEmbedding: [1f, 0f]);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchByEmbeddingAsync_ReturnsEmptyForZeroQueryVector()
    {
        var store = new InMemoryRepositoryEmbeddingStore();

        await store.ReplaceRepositoryEmbeddingsAsync("octocat", "hello-world",
        [
            MakeRecord("octocat", "hello-world", "c1", [1f, 0f])
        ]);

        var results = await store.SearchByEmbeddingAsync("octocat", "hello-world", queryEmbedding: [0f, 0f]);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchByEmbeddingAsync_ReturnsEmptyForNonPositiveTopK()
    {
        var store = new InMemoryRepositoryEmbeddingStore();

        await store.ReplaceRepositoryEmbeddingsAsync("octocat", "hello-world",
        [
            MakeRecord("octocat", "hello-world", "c1", [1f, 0f])
        ]);

        var results = await store.SearchByEmbeddingAsync("octocat", "hello-world", queryEmbedding: [1f, 0f], topK: 0);

        Assert.Empty(results);
    }

    [Fact]
    public async Task UpsertRepositoryEmbeddingsAsync_MergesAndReplacesByChunkId()
    {
        var store = new InMemoryRepositoryEmbeddingStore();

        await store.ReplaceRepositoryEmbeddingsAsync("octocat", "hello-world",
        [
            MakeRecord("octocat", "hello-world", "c1", [1f, 0f])
        ]);

        await store.UpsertRepositoryEmbeddingsAsync("octocat", "hello-world",
        [
            MakeRecord("octocat", "hello-world", "c1", [0.5f, 0.5f]),
            MakeRecord("octocat", "hello-world", "c2", [0f, 1f])
        ]);

        var all = await store.GetRepositoryEmbeddingsAsync("octocat", "hello-world");

        Assert.Equal(2, all.Count);
        var c1 = Assert.Single(all, e => e.ChunkId == "c1");
        Assert.Equal(0.5f, c1.Embedding[0]);
        Assert.Equal(0.5f, c1.Embedding[1]);
        Assert.Contains(all, e => e.ChunkId == "c2");
    }
}
