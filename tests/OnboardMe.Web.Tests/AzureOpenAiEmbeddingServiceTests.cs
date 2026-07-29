using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnboardMe.Web.Services.RepoIngestion;

namespace OnboardMe.Web.Tests;

public class AzureOpenAiEmbeddingServiceTests
{
    [Fact]
    public async Task GenerateEmbeddingsAsync_SplitsLargeInputsIntoBatches_AndPreservesOrder()
    {
        var requestInputCounts = new List<int>();
        var handler = new StubHttpMessageHandler(request =>
        {
            var payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            using var document = JsonDocument.Parse(payload);
            var inputCount = document.RootElement.GetProperty("input").GetArrayLength();
            requestInputCounts.Add(inputCount);

            var embeddings = Enumerable.Range(0, inputCount)
                .Select(index => new
                {
                    index,
                    embedding = new[] { (float)index, (float)(index + 1) }
                })
                .ToArray();

            return JsonResponse(new { data = embeddings });
        });

        var client = new HttpClient(handler);
        var service = new AzureOpenAiEmbeddingService(
            new StubHttpClientFactory(client),
            Options.Create(new AzureOpenAiEmbeddingsOptions
            {
                Endpoint = "https://aoai.test/",
                ApiKey = "local-test-key",
                EmbeddingsDeployment = "embeddings-test",
                ApiVersion = "2024-02-01"
            }),
            NullLogger<AzureOpenAiEmbeddingService>.Instance);

        var chunks = Enumerable.Range(0, 65)
            .Select(index => new RepositoryContentChunk
            {
                ChunkId = $"sha:{index}",
                SourcePath = "src/App.cs",
                SourceSha = "sha",
                ChunkIndex = index,
                Strategy = "line-window",
                StartLine = index + 1,
                EndLine = index + 1,
                Content = $"line {index}"
            })
            .ToArray();

        var embeddings = await service.GenerateEmbeddingsAsync("octocat", "hello-world", chunks);

        Assert.Equal(2, requestInputCounts.Count);
        Assert.Contains(64, requestInputCounts);
        Assert.Contains(1, requestInputCounts);
        Assert.Equal(65, embeddings.Count);

        for (var i = 0; i < embeddings.Count; i++)
        {
            Assert.Equal($"sha:{i}", embeddings[i].ChunkId);
            Assert.Equal(2, embeddings[i].Embedding.Count);
        }
    }

    [Fact]
    public async Task GenerateEmbeddingsAsync_RetriesTransientFailure_AndReturnsEmbeddings()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent("rate limited")
                };
            }

            return JsonResponse(new
            {
                data = new[]
                {
                    new { index = 0, embedding = new[] { 0.1f, 0.2f } }
                }
            });
        });

        var client = new HttpClient(handler);
        var service = new AzureOpenAiEmbeddingService(
            new StubHttpClientFactory(client),
            Options.Create(new AzureOpenAiEmbeddingsOptions
            {
                Endpoint = "https://aoai.test/",
                ApiKey = "local-test-key",
                EmbeddingsDeployment = "embeddings-test",
                ApiVersion = "2024-02-01"
            }),
            NullLogger<AzureOpenAiEmbeddingService>.Instance);

        var embeddings = await service.GenerateEmbeddingsAsync(
            "octocat",
            "hello-world",
            [
                new RepositoryContentChunk
                {
                    ChunkId = "sha-app:0",
                    SourcePath = "src/App.cs",
                    SourceSha = "sha-app",
                    ChunkIndex = 0,
                    Strategy = "line-window",
                    StartLine = 1,
                    EndLine = 1,
                    Content = "public class App {}"
                }
            ]);

        Assert.Equal(2, callCount);
        var embedding = Assert.Single(embeddings);
        Assert.Equal("sha-app:0", embedding.ChunkId);
        Assert.Equal(2, embedding.Embedding.Count);
    }

    private static HttpResponseMessage JsonResponse<T>(T payload)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
}
