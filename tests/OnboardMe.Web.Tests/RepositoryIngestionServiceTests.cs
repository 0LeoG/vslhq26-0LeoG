using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OnboardMe.Web.Services.RepoIngestion;

namespace OnboardMe.Web.Tests;

public class RepositoryIngestionServiceTests
{
    [Fact]
    public async Task IngestRepositoryAsync_IndexesTextAndSkipsUnsupportedFiles()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            var pathAndQuery = request.RequestUri?.PathAndQuery ?? string.Empty;

            if (request.Method == HttpMethod.Get && pathAndQuery == "/repos/octocat/hello-world")
            {
                return JsonResponse(new { default_branch = "main" });
            }

            if (request.Method == HttpMethod.Get && pathAndQuery == "/repos/octocat/hello-world/git/trees/main?recursive=1")
            {
                return JsonResponse(new
                {
                    tree = new[]
                    {
                        new { path = "src/App.cs", type = "blob", sha = "sha-app", size = 50 },
                        new { path = "node_modules/a.js", type = "blob", sha = "sha-generated", size = 60 },
                        new { path = "assets/logo.png", type = "blob", sha = "sha-binary", size = 10 },
                        new { path = "src/Large.md", type = "blob", sha = "sha-large", size = 600000 }
                    }
                });
            }

            if (request.Method == HttpMethod.Get && pathAndQuery == "/repos/octocat/hello-world/git/blobs/sha-app")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("public class App {}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };

        var service = new RepositoryIngestionService(
            new StubHttpClientFactory(httpClient),
            new InMemoryRepositoryIndexingStatusStore(),
            NullLogger<RepositoryIngestionService>.Instance);

        var result = await service.IngestRepositoryAsync("octocat", "hello-world");

        Assert.Equal(RepositoryIndexingState.Completed, result.State);
        Assert.Equal(1, result.IndexedCount);
        Assert.Equal(3, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);

        var indexedFile = Assert.Single(result.Files, file => file.Status == RepositoryFileIndexStatus.Indexed);
        Assert.Equal("src/App.cs", indexedFile.Path);
        Assert.Equal("C#", indexedFile.Language);
        Assert.Equal("public class App {}", indexedFile.Content);
    }

    private static HttpResponseMessage JsonResponse<T>(T payload)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
