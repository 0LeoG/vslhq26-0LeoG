using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnboardMe.Web.Services.RepoIngestion;

namespace OnboardMe.Web.Tests;

public class AzureOpenAiChatServiceTests
{
    // -------------------------------------------------------------------------
    // Happy path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnswerAsync_ReturnsAnswerAndCitations_WhenApiSucceeds()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new
        {
            choices = new[]
            {
                new { message = new { content = "Use the `App` class in src/App.cs." } }
            }
        }));

        var service = BuildService(handler);
        var chunks = BuildChunks("src/App.cs", 1, 20, "src/Program.cs", 5, 30);

        var result = await service.AnswerAsync("owner", "repo", "What does the App class do?", chunks);

        Assert.False(string.IsNullOrWhiteSpace(result.Answer));
        Assert.Equal(2, result.Citations.Count);
        Assert.Equal("src/App.cs",     result.Citations[0].Path);
        Assert.Equal("src/Program.cs", result.Citations[1].Path);
    }

    [Fact]
    public async Task AnswerAsync_DeduplicatesCitations_WhenSameChunkAppearsMultipleTimes()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new
        {
            choices = new[] { new { message = new { content = "Answer text." } } }
        }));

        var service = BuildService(handler);

        // Two chunks pointing to the exact same file/line range.
        var chunks = new List<VectorSearchResult>
        {
            MakeResult("src/App.cs", 1, 20),
            MakeResult("src/App.cs", 1, 20)
        };

        var result = await service.AnswerAsync("owner", "repo", "question?", chunks);

        Assert.Single(result.Citations);
        Assert.Equal("src/App.cs", result.Citations[0].Path);
    }

    // -------------------------------------------------------------------------
    // Retry behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnswerAsync_RetriesTransientFailure_AndReturnsAnswer()
    {
        var callCount = 0;
        var handler = new StubHttpMessageHandler(_ =>
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
                choices = new[] { new { message = new { content = "Retried answer." } } }
            });
        });

        var service = BuildService(handler);

        var result = await service.AnswerAsync("owner", "repo", "question?", BuildChunks("a.cs", 1, 5));

        Assert.Equal(2, callCount);
        Assert.Equal("Retried answer.", result.Answer);
    }

    // -------------------------------------------------------------------------
    // Error conditions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AnswerAsync_ThrowsInvalidOperationException_WhenNotConfigured()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = new AzureOpenAiChatService(
            new StubHttpClientFactory(new HttpClient(handler)),
            Options.Create(new AzureOpenAiEmbeddingsOptions
            {
                // ChatDeployment intentionally left null → IsChatConfigured == false
                Endpoint = "https://aoai.test/",
                ApiKey   = "key"
            }),
            NullLogger<AzureOpenAiChatService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AnswerAsync("owner", "repo", "question?", []));
    }

    [Fact]
    public async Task AnswerAsync_ThrowsInvalidOperationException_AfterPersistentFailure()
    {
        var handler = new StubHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("server error")
            });

        var service = BuildService(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AnswerAsync("owner", "repo", "question?", BuildChunks("a.cs", 1, 5)));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static AzureOpenAiChatService BuildService(StubHttpMessageHandler handler)
        => new(
            new StubHttpClientFactory(new HttpClient(handler)),
            Options.Create(new AzureOpenAiEmbeddingsOptions
            {
                Endpoint         = "https://aoai.test/",
                ApiKey           = "local-test-key",
                ChatDeployment   = "chat-test",
                ApiVersion       = "2024-02-01"
            }),
            NullLogger<AzureOpenAiChatService>.Instance);

    private static IReadOnlyList<VectorSearchResult> BuildChunks(
        string path1, int start1, int end1,
        string? path2 = null, int start2 = 0, int end2 = 0)
    {
        var list = new List<VectorSearchResult> { MakeResult(path1, start1, end1) };
        if (path2 is not null)
        {
            list.Add(MakeResult(path2, start2, end2));
        }

        return list;
    }

    private static VectorSearchResult MakeResult(string path, int startLine, int endLine)
        => new()
        {
            Score = 0.9f,
            Chunk = new RepositoryChunkEmbeddingRecord
            {
                Owner      = "owner",
                Repository = "repo",
                ChunkId    = $"{path}:{startLine}",
                SourcePath = path,
                SourceSha  = "abc123",
                ChunkIndex = 0,
                StartLine  = startLine,
                EndLine    = endLine,
                Strategy   = "line-window",
                Content    = $"// content of {path}",
                Embedding  = [0.1f, 0.2f],
                CreatedAtUtc = DateTimeOffset.UtcNow
            }
        };

    private static HttpResponseMessage JsonResponse<T>(T payload)
        => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
}
