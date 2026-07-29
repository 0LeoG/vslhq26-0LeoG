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
            logger.LogWarning("Azure OpenAI embeddings are not configured. Skipping embedding generation.");
            return [];
        }

        var endpoint = options.Endpoint!.TrimEnd('/');
        var deployment = Uri.EscapeDataString(options.EmbeddingsDeployment!);
        var apiVersion = string.IsNullOrWhiteSpace(options.ApiVersion) ? "2024-02-01" : options.ApiVersion.Trim();
        var requestUri = $"{endpoint}/openai/deployments/{deployment}/embeddings?api-version={Uri.EscapeDataString(apiVersion)}";
        var inputs = chunks.Select(chunk => chunk.Content).ToArray();

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
                    Content = JsonContent.Create(new AzureEmbeddingsRequest { Input = inputs })
                };
                request.Headers.Add("api-key", options.ApiKey);

                using var response = await httpClientFactory.CreateClient(AzureOpenAiClientName).SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return await BuildEmbeddingRecordsAsync(response, owner, repository, chunks, cancellationToken);
                }

                lastStatusCode = response.StatusCode;
                lastDetails = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsTransientStatusCode(response.StatusCode) && attempt < MaxAttempts)
                {
                    logger.LogWarning(
                        "Azure OpenAI embedding request attempt {Attempt} failed with transient status {StatusCode}. Retrying.",
                        attempt,
                        (int)response.StatusCode);
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
                    logger.LogWarning(ex, "Azure OpenAI embedding request attempt {Attempt} failed. Retrying.", attempt);
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
        CancellationToken cancellationToken)
    {
        var parsed = await response.Content.ReadFromJsonAsync<AzureEmbeddingsResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Azure OpenAI embeddings response was empty.");

        var orderedData = parsed.Data
            .OrderBy(item => item.Index)
            .ToArray();

        if (orderedData.Length != chunks.Count)
        {
            throw new InvalidOperationException($"Azure OpenAI embeddings response count mismatch. Expected {chunks.Count}, got {orderedData.Length}.");
        }

        var createdAt = DateTimeOffset.UtcNow;
        var results = new List<RepositoryChunkEmbeddingRecord>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
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
