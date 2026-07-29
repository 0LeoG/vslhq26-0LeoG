using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

            if (request.Method == HttpMethod.Post
                && string.Equals(request.RequestUri?.AbsoluteUri, "https://aoai.test/openai/deployments/embeddings-test/embeddings?api-version=2024-02-01", StringComparison.Ordinal))
            {
                return JsonResponse(new
                {
                    data = new[]
                    {
                        new
                        {
                            index = 0,
                            embedding = new[] { 0.11f, 0.22f, 0.33f }
                        }
                    }
                });
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
            new AzureOpenAiEmbeddingService(
                new StubHttpClientFactory(httpClient),
                Options.Create(new AzureOpenAiEmbeddingsOptions
                {
                    Endpoint = "https://aoai.test/",
                    ApiKey = "local-test-key",
                    EmbeddingsDeployment = "embeddings-test",
                    ApiVersion = "2024-02-01"
                }),
                NullLogger<AzureOpenAiEmbeddingService>.Instance),
            new InMemoryRepositoryEmbeddingStore(),
            NullLogger<RepositoryIngestionService>.Instance,
            new HttpContextAccessor(),
            new ConfigurationBuilder().AddInMemoryCollection().Build());

        var result = await service.IngestRepositoryAsync("octocat", "hello-world");

        Assert.Equal(RepositoryIndexingState.Completed, result.State);
        Assert.Equal(1, result.IndexedCount);
        Assert.Equal(3, result.SkippedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(1, result.EmbeddedChunkCount);
        Assert.Equal(4, result.TotalFileCount);
        Assert.Equal(4, result.ProcessedFileCount);
        Assert.Equal(1, result.ProcessedChunkCount);
        Assert.Null(result.CurrentFilePath);

        var indexedFile = Assert.Single(result.Files, file => file.Status == RepositoryFileIndexStatus.Indexed);
        Assert.Equal("src/App.cs", indexedFile.Path);
        Assert.Equal("C#", indexedFile.Language);
        Assert.Equal("public class App {}", indexedFile.Content);
        var chunk = Assert.Single(indexedFile.Chunks);
        Assert.Equal("src/App.cs", chunk.SourcePath);
        Assert.Equal("sha-app:0", chunk.ChunkId);
        Assert.Equal(1, chunk.StartLine);
        Assert.Equal(1, chunk.EndLine);
        Assert.Equal("public class App {}", chunk.Content);
    }

    [Fact]
    public async Task RegenerateEmbeddingsAsync_RebuildsEmbeddingsForIndexedRepository()
    {
        var embeddingCalls = 0;
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
                        new { path = "src/App.cs", type = "blob", sha = "sha-app", size = 50 }
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

            if (request.Method == HttpMethod.Post
                && string.Equals(request.RequestUri?.AbsoluteUri, "https://aoai.test/openai/deployments/embeddings-test/embeddings?api-version=2024-02-01", StringComparison.Ordinal))
            {
                embeddingCalls++;
                return JsonResponse(new
                {
                    data = new[]
                    {
                        new
                        {
                            index = 0,
                            embedding = new[] { 0.11f, 0.22f, 0.33f }
                        }
                    }
                });
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
            new AzureOpenAiEmbeddingService(
                new StubHttpClientFactory(httpClient),
                Options.Create(new AzureOpenAiEmbeddingsOptions
                {
                    Endpoint = "https://aoai.test/",
                    ApiKey = "local-test-key",
                    EmbeddingsDeployment = "embeddings-test",
                    ApiVersion = "2024-02-01"
                }),
                NullLogger<AzureOpenAiEmbeddingService>.Instance),
            new InMemoryRepositoryEmbeddingStore(),
            NullLogger<RepositoryIngestionService>.Instance,
            new HttpContextAccessor(),
            new ConfigurationBuilder().AddInMemoryCollection().Build());

        await service.IngestRepositoryAsync("octocat", "hello-world");
        var embeddedCount = await service.RegenerateEmbeddingsAsync("octocat", "hello-world");

        Assert.Equal(1, embeddedCount);
        Assert.Equal(2, embeddingCalls);
    }

    [Fact]
    public async Task IngestRepositoryAsync_ReportsEmbeddingConfigurationProblems()
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
                        new { path = "src/App.cs", type = "blob", sha = "sha-app", size = 50 }
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
            new AzureOpenAiEmbeddingService(
                new StubHttpClientFactory(httpClient),
                Options.Create(new AzureOpenAiEmbeddingsOptions()),
                NullLogger<AzureOpenAiEmbeddingService>.Instance),
            new InMemoryRepositoryEmbeddingStore(),
            NullLogger<RepositoryIngestionService>.Instance,
            new HttpContextAccessor(),
            new ConfigurationBuilder().AddInMemoryCollection().Build());

        var result = await service.IngestRepositoryAsync("octocat", "hello-world");

        Assert.Equal(RepositoryIndexingState.CompletedWithErrors, result.State);
        Assert.Contains("Azure OpenAI embeddings", result.ErrorMessage);
    }

    [Fact]
    public async Task IngestRepositoryAsync_SavesRunningProgressWhileProcessingFiles()
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
                        new { path = "README.md", type = "blob", sha = "sha-readme", size = 30 }
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

            if (request.Method == HttpMethod.Get && pathAndQuery == "/repos/octocat/hello-world/git/blobs/sha-readme")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("# README")
                };
            }

            if (request.Method == HttpMethod.Post
                && string.Equals(request.RequestUri?.AbsoluteUri, "https://aoai.test/openai/deployments/embeddings-test/embeddings?api-version=2024-02-01", StringComparison.Ordinal))
            {
                return JsonResponse(new
                {
                    data = new[]
                    {
                        new
                        {
                            index = 0,
                            embedding = new[] { 0.11f, 0.22f, 0.33f }
                        },
                        new
                        {
                            index = 1,
                            embedding = new[] { 0.44f, 0.55f, 0.66f }
                        }
                    }
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.github.com/")
        };

        var statusStore = new RecordingRepositoryIndexingStatusStore();
        var service = new RepositoryIngestionService(
            new StubHttpClientFactory(httpClient),
            statusStore,
            new AzureOpenAiEmbeddingService(
                new StubHttpClientFactory(httpClient),
                Options.Create(new AzureOpenAiEmbeddingsOptions
                {
                    Endpoint = "https://aoai.test/",
                    ApiKey = "local-test-key",
                    EmbeddingsDeployment = "embeddings-test",
                    ApiVersion = "2024-02-01"
                }),
                NullLogger<AzureOpenAiEmbeddingService>.Instance),
            new InMemoryRepositoryEmbeddingStore(),
            NullLogger<RepositoryIngestionService>.Instance,
            new HttpContextAccessor(),
            new ConfigurationBuilder().AddInMemoryCollection().Build());

        _ = await service.IngestRepositoryAsync("octocat", "hello-world");

        Assert.Contains(statusStore.Snapshots, status =>
            status.State == RepositoryIndexingState.Running &&
            status.TotalFileCount == 2 &&
            status.ProcessedFileCount == 1 &&
            status.ProcessedChunkCount >= 1 &&
            !string.IsNullOrWhiteSpace(status.CurrentFilePath));
    }

    [Fact]
    public void ApplyAuthorization_SetsBearerHeaderWhenTokenIsProvided()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/octocat/hello-world");

        GitHubAuthenticationHelper.ApplyAuthorization(request, "github-token");

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("github-token", request.Headers.Authorization?.Parameter);
    }

    private static HttpResponseMessage JsonResponse<T>(T payload)
        => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

    private sealed class RecordingRepositoryIndexingStatusStore : IRepositoryIndexingStatusStore
    {
        private readonly List<RepositoryIndexingStatus> snapshots = [];

        public IReadOnlyList<RepositoryIndexingStatus> Snapshots => snapshots;

        public Task SaveAsync(RepositoryIndexingStatus status, CancellationToken cancellationToken = default)
        {
            snapshots.Add(new RepositoryIndexingStatus
            {
                Owner = status.Owner,
                Repository = status.Repository,
                Branch = status.Branch,
                StartedAtUtc = status.StartedAtUtc,
                CompletedAtUtc = status.CompletedAtUtc,
                State = status.State,
                ErrorMessage = status.ErrorMessage,
                EmbeddedChunkCount = status.EmbeddedChunkCount,
                TotalFileCount = status.TotalFileCount,
                ProcessedFileCount = status.ProcessedFileCount,
                ProcessedChunkCount = status.ProcessedChunkCount,
                CurrentFilePath = status.CurrentFilePath
            });
            return Task.CompletedTask;
        }

        public Task<RepositoryIndexingStatus?> GetAsync(string owner, string repository, CancellationToken cancellationToken = default)
            => Task.FromResult<RepositoryIndexingStatus?>(snapshots.LastOrDefault());
    }
}
