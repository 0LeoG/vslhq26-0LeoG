using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnboardMe.Web.Services.RepoIngestion;

namespace OnboardMe.Web.Tests;

public class AzureOpenAiRepositoryOverviewServiceTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsAiSummary_WhenResponseIsValidJson()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = """
                        {
                          "architectureSummary": "This is a layered web app with ingestion and chat services.",
                          "mainWorkflows": ["Index repository", "Search with embeddings"],
                          "entryPoints": [
                            { "path": "src/OnboardMe.Web/Program.cs", "whyItMatters": "Bootstraps DI and endpoints." },
                            { "path": "src/OnboardMe.Web/Components/Pages/Chat.razor", "whyItMatters": "Main user interaction flow." }
                          ],
                          "risksAndUnknowns": ["Some files are skipped due to size limits."]
                        }
                        """
                    }
                }
            }
        }));

        var service = BuildService(handler, isConfigured: true);

        var result = await service.GenerateAsync(BuildStatus());

        Assert.True(result.IsAiGenerated);
        Assert.Equal(2, result.MainWorkflows.Count);
        Assert.Equal(2, result.EntryPoints.Count);
        Assert.Contains("layered web app", result.Narrative, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.FallbackReason);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsDeterministicFallback_WhenChatIsNotConfigured()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new
        {
            choices = new[] { new { message = new { content = "{}" } } }
        }));

        var service = BuildService(handler, isConfigured: false);

        var result = await service.GenerateAsync(BuildStatus());

        Assert.False(result.IsAiGenerated);
        Assert.NotNull(result.FallbackReason);
        Assert.Contains("not configured", result.FallbackReason!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.Narrative);
        Assert.NotEmpty(result.EntryPoints);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsDeterministicFallback_WhenResponseIsInvalidJson()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new
        {
            choices = new[]
            {
                new { message = new { content = "not-json" } }
            }
        }));

        var service = BuildService(handler, isConfigured: true);

        var result = await service.GenerateAsync(BuildStatus());

        Assert.False(result.IsAiGenerated);
        Assert.NotNull(result.FallbackReason);
        Assert.Contains("not valid", result.FallbackReason!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.RisksAndUnknowns);
    }

    [Fact]
    public async Task GenerateAsync_ParsesResponse_WhenJsonIsWrappedInMarkdownFence()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = """
                        ```json
                        {
                          "architectureSummary": "Fenced JSON summary.",
                          "mainWorkflows": ["Index repository"],
                          "entryPoints": [
                            { "path": "src/OnboardMe.Web/Program.cs", "whyItMatters": "Configures services." }
                          ],
                          "risksAndUnknowns": ["None detected"]
                        }
                        ```
                        """
                    }
                }
            }
        }));

        var service = BuildService(handler, isConfigured: true);

        var result = await service.GenerateAsync(BuildStatus());

        Assert.True(result.IsAiGenerated);
        Assert.Equal("Fenced JSON summary.", result.Narrative);
        Assert.Single(result.EntryPoints);
    }

    [Fact]
    public async Task GenerateAsync_ParsesResponse_WhenJsonIsWrappedInProse()
    {
        var handler = new StubHttpMessageHandler(_ => JsonResponse(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = """
                        Here is the repository analysis in JSON:
                        {
                          "architectureSummary": "Prose-wrapped JSON summary.",
                          "mainWorkflows": ["Index repository", "Generate embeddings"],
                          "entryPoints": [
                            { "path": "src/OnboardMe.Web/Components/Pages/RepoOverview.razor", "whyItMatters": "Shows insights to users." }
                          ],
                          "risksAndUnknowns": ["Skipped files may hide details"]
                        }
                        Thanks!
                        """
                    }
                }
            }
        }));

        var service = BuildService(handler, isConfigured: true);

        var result = await service.GenerateAsync(BuildStatus());

        Assert.True(result.IsAiGenerated);
        Assert.Equal("Prose-wrapped JSON summary.", result.Narrative);
        Assert.Equal(2, result.MainWorkflows.Count);
    }

    private static AzureOpenAiRepositoryOverviewService BuildService(StubHttpMessageHandler handler, bool isConfigured)
    {
        var options = isConfigured
            ? Options.Create(new AzureOpenAiEmbeddingsOptions
            {
                Endpoint = "https://aoai.test/",
                ApiKey = "local-test-key",
                ChatDeployment = "chat-test",
                ApiVersion = "2024-02-01"
            })
            : Options.Create(new AzureOpenAiEmbeddingsOptions
            {
                Endpoint = "https://aoai.test/",
                ApiKey = "local-test-key",
                ApiVersion = "2024-02-01"
            });

        return new AzureOpenAiRepositoryOverviewService(
            new StubHttpClientFactory(new HttpClient(handler)),
            options,
            NullLogger<AzureOpenAiRepositoryOverviewService>.Instance);
    }

    private static RepositoryIndexingStatus BuildStatus()
    {
        var status = new RepositoryIndexingStatus
        {
            Owner = "octocat",
            Repository = "hello-world",
            StartedAtUtc = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero),
            CompletedAtUtc = new DateTimeOffset(2026, 7, 1, 12, 5, 0, TimeSpan.Zero),
            State = RepositoryIndexingState.CompletedWithErrors,
            TotalFileCount = 4,
            ProcessedFileCount = 4,
            ProcessedChunkCount = 5,
            EmbeddedChunkCount = 5
        };

        status.Files.Add(new RepositoryFileIngestionRecord
        {
            Path = "README.md",
            Sha = "sha-readme",
            SizeBytes = 10,
            Extension = ".md",
            Language = "Markdown",
            Status = RepositoryFileIndexStatus.Indexed,
            Content = "# Overview\nThis app indexes repositories and supports chat over code."
        });

        status.Files.Add(new RepositoryFileIngestionRecord
        {
            Path = "src/OnboardMe.Web/Program.cs",
            Sha = "sha-program",
            SizeBytes = 10,
            Extension = ".cs",
            Language = "C#",
            Status = RepositoryFileIndexStatus.Indexed,
            Content = "var builder = WebApplication.CreateBuilder(args);\nbuilder.Services.AddSingleton<IRepositoryIngestionService, RepositoryIngestionService>();"
        });

        status.Files.Add(new RepositoryFileIngestionRecord
        {
            Path = "src/OnboardMe.Web/Services/RepoIngestion/Ingestion/RepositoryIngestionService.cs",
            Sha = "sha-ingestion",
            SizeBytes = 10,
            Extension = ".cs",
            Language = "C#",
            Status = RepositoryFileIndexStatus.Indexed,
            Content = "public sealed class RepositoryIngestionService { }"
        });

        status.Files.Add(new RepositoryFileIngestionRecord
        {
            Path = "assets/large.bin",
            Sha = "sha-bin",
            SizeBytes = 5_000_000,
            Extension = ".bin",
            Language = "Binary",
            Status = RepositoryFileIndexStatus.Skipped,
            SkipReason = "oversized-file"
        });

        return status;
    }

    private static HttpResponseMessage JsonResponse<T>(T payload)
        => new(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
}
