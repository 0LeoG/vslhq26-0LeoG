using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace OnboardMe.Web.Services.RepoIngestion;

public sealed class AzureOpenAiEmbeddingService(
    IHttpClientFactory httpClientFactory,
    IOptions<AzureOpenAiEmbeddingsOptions> optionsAccessor,
    ILogger<AzureOpenAiEmbeddingService> logger) : IAzureOpenAiEmbeddingService
{
    public const string AzureOpenAiClientName = "AzureOpenAiEmbeddings";

    private const int MaxAttempts = 3;
    private const int EmbeddingBatchSize = 64;
    private const int MaxConcurrentEmbeddingRequests = 3;

    public async Task<IReadOnlyList<RepositoryChunkEmbeddingRecord>> GenerateEmbeddingsAsync(
        string owner,
        string repository,
        IReadOnlyList<RepositoryContentChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return [];
        }

        var options = optionsAccessor.Value;
        if (!options.IsConfigured)
        {
            var message = "Azure OpenAI embeddings are not configured. Provide Endpoint, ApiKey, and EmbeddingsDeployment before ingesting a repository.";
            logger.LogWarning(message);
            throw new InvalidOperationException(message);
        }

        var endpoint = options.Endpoint!.TrimEnd('/');
        var deployment = Uri.EscapeDataString(options.EmbeddingsDeployment!);
        var apiVersion = string.IsNullOrWhiteSpace(options.ApiVersion) ? "2024-02-01" : options.ApiVersion.Trim();
        var requestUri = $"{endpoint}/openai/deployments/{deployment}/embeddings?api-version={Uri.EscapeDataString(apiVersion)}";
        var batchRanges = BuildBatchRanges(chunks.Count, EmbeddingBatchSize);
        var results = new RepositoryChunkEmbeddingRecord[chunks.Count];
        using var throttle = new SemaphoreSlim(MaxConcurrentEmbeddingRequests);

        var batchTasks = batchRanges.Select(async range =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var batchRecords = await GenerateEmbeddingBatchAsync(owner, repository, chunks, range.Start, range.Count, options, requestUri, cancellationToken);
                for (var i = 0; i < batchRecords.Count; i++)
                {
                    results[range.Start + i] = batchRecords[i];
                }
            }
            finally
            {
                throttle.Release();
            }
        }).ToArray();

        await Task.WhenAll(batchTasks);
        return results;
    }

    private async Task<IReadOnlyList<RepositoryChunkEmbeddingRecord>> GenerateEmbeddingBatchAsync(
        string owner,
        string repository,
        IReadOnlyList<RepositoryContentChunk> chunks,
        int batchStart,
        int batchCount,
        AzureOpenAiEmbeddingsOptions options,
        string requestUri,
        CancellationToken cancellationToken)
    {
        var batchInputs = new string[batchCount];
        for (var i = 0; i < batchCount; i++)
        {
            batchInputs[i] = chunks[batchStart + i].Content;
        }

        HttpStatusCode? lastStatusCode = null;
        string? lastDetails = null;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = JsonContent.Create(new AzureEmbeddingsRequest { Input = batchInputs })
                };
                request.Headers.Add("api-key", options.ApiKey);

                using var response = await httpClientFactory.CreateClient(AzureOpenAiClientName).SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return await BuildEmbeddingRecordsAsync(response, owner, repository, chunks, batchStart, batchCount, cancellationToken);
                }

                lastStatusCode = response.StatusCode;
                lastDetails = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsTransientStatusCode(response.StatusCode) && attempt < MaxAttempts)
                {
                    logger.LogWarning(
                        "Azure OpenAI embedding request attempt {Attempt} failed with transient status {StatusCode} for batch starting at {BatchStart}. Retrying.",
                        attempt,
                        (int)response.StatusCode,
                        batchStart);
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                    continue;
                }

                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastException = ex;
                if (attempt < MaxAttempts)
                {
                    logger.LogWarning(ex, "Azure OpenAI embedding request attempt {Attempt} failed for batch starting at {BatchStart}. Retrying.", attempt, batchStart);
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                    continue;
                }
            }
        }

        if (lastException is not null)
        {
            throw new InvalidOperationException("Azure OpenAI embedding request failed after retries.", lastException);
        }

        throw new InvalidOperationException($"Azure OpenAI embedding request failed with {(int?)lastStatusCode} {lastStatusCode}: {lastDetails}");
    }

    private static async Task<IReadOnlyList<RepositoryChunkEmbeddingRecord>> BuildEmbeddingRecordsAsync(
        HttpResponseMessage response,
        string owner,
        string repository,
        IReadOnlyList<RepositoryContentChunk> chunks,
        int batchStart,
        int batchCount,
        CancellationToken cancellationToken)
    {
        var parsed = await response.Content.ReadFromJsonAsync<AzureEmbeddingsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Azure OpenAI embeddings response was empty.");

        var orderedData = parsed.Data
            .OrderBy(item => item.Index)
            .ToArray();

        if (orderedData.Length != batchCount)
        {
            throw new InvalidOperationException($"Azure OpenAI embeddings response count mismatch. Expected {batchCount}, got {orderedData.Length}.");
        }

        var createdAt = DateTimeOffset.UtcNow;
        var results = new List<RepositoryChunkEmbeddingRecord>(batchCount);
        for (var i = 0; i < batchCount; i++)
        {
            var chunk = chunks[batchStart + i];
            var embedding = orderedData[i].Embedding ?? [];
            results.Add(new RepositoryChunkEmbeddingRecord
            {
                Owner = owner,
                Repository = repository,
                ChunkId = chunk.ChunkId,
                SourcePath = chunk.SourcePath,
                SourceSha = chunk.SourceSha,
                ChunkIndex = chunk.ChunkIndex,
                StartLine = chunk.StartLine,
                EndLine = chunk.EndLine,
                Strategy = chunk.Strategy,
                Content = chunk.Content,
                Embedding = embedding.ToArray(),
                CreatedAtUtc = createdAt
            });
        }

        return results;
    }

    private static TimeSpan GetRetryDelay(int attempt)
        => TimeSpan.FromMilliseconds(250 * attempt);

    private static IReadOnlyList<(int Start, int Count)> BuildBatchRanges(int totalCount, int batchSize)
    {
        if (totalCount <= 0)
        {
            return [];
        }

        var ranges = new List<(int Start, int Count)>((totalCount + batchSize - 1) / batchSize);
        for (var start = 0; start < totalCount; start += batchSize)
        {
            var count = Math.Min(batchSize, totalCount - start);
            ranges.Add((start, count));
        }

        return ranges;
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout
           || statusCode == (HttpStatusCode)429
           || (int)statusCode >= 500;

    private sealed class AzureEmbeddingsRequest
    {
        public required string[] Input { get; init; }
    }

    private sealed class AzureEmbeddingsResponse
    {
        public List<AzureEmbeddingsResponseData> Data { get; init; } = [];
    }

    private sealed class AzureEmbeddingsResponseData
    {
        public int Index { get; init; }

        public float[]? Embedding { get; init; }
    }
}
