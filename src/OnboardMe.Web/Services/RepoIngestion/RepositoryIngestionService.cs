using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;

namespace OnboardMe.Web.Services.RepoIngestion;

public sealed class RepositoryIngestionService(
    IHttpClientFactory httpClientFactory,
    IRepositoryIndexingStatusStore statusStore,
    IAzureOpenAiEmbeddingService embeddingService,
    IRepositoryEmbeddingStore embeddingStore,
    ILogger<RepositoryIngestionService> logger,
    IHttpContextAccessor httpContextAccessor,
    IConfiguration configuration) : IRepositoryIngestionService
{
    public const string GitHubApiClientName = "GitHubApi";

    // First-pass ingestion keeps each file under 512 KB so fetches stay reliable.
    private const int MaxFileSizeBytes = 512 * 1024;

    public async Task<RepositoryIndexingStatus> IngestRepositoryAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(GitHubApiClientName);
        var status = new RepositoryIndexingStatus
        {
            Owner = owner,
            Repository = repository,
            Branch = "unknown",
            StartedAtUtc = DateTimeOffset.UtcNow,
            State = RepositoryIndexingState.Running
        };

        await statusStore.SaveAsync(status, cancellationToken);

        try
        {
            var repositoryMetadata = await GetRepositoryMetadataAsync(client, owner, repository, cancellationToken);
            status.Branch = repositoryMetadata.DefaultBranch;

            var tree = await GetRepositoryTreeAsync(client, owner, repository, repositoryMetadata.DefaultBranch, cancellationToken);
            var filesToProcess = tree
                .Where(treeItem => string.Equals(treeItem.Type, "blob", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            status.TotalFileCount = filesToProcess.Length;
            await statusStore.SaveAsync(status, cancellationToken);

            foreach (var item in filesToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var path = item.Path;
                var extension = Path.GetExtension(path);
                var size = item.Size ?? 0;
                status.CurrentFilePath = path;

                async Task MarkFileProcessedAsync(RepositoryFileIngestionRecord processedFile)
                {
                    status.Files.Add(processedFile);
                    status.ProcessedFileCount++;
                    status.ProcessedChunkCount += processedFile.Chunks.Count;
                    await statusStore.SaveAsync(status, cancellationToken);
                }

                var file = new RepositoryFileIngestionRecord
                {
                    Path = path,
                    Sha = item.Sha,
                    SizeBytes = size,
                    Extension = extension,
                    Language = RepoIngestionRules.DetectLanguage(path),
                    Status = RepositoryFileIndexStatus.Indexed
                };

                if (RepoIngestionRules.IsGeneratedPath(path))
                {
                    file.Status = RepositoryFileIndexStatus.Skipped;
                    file.SkipReason = "generated-path";
                    await MarkFileProcessedAsync(file);
                    continue;
                }

                if (RepoIngestionRules.IsBinaryPath(path))
                {
                    file.Status = RepositoryFileIndexStatus.Skipped;
                    file.SkipReason = "binary-file";
                    await MarkFileProcessedAsync(file);
                    continue;
                }

                if (size > MaxFileSizeBytes)
                {
                    file.Status = RepositoryFileIndexStatus.Skipped;
                    file.SkipReason = "oversized-file";
                    await MarkFileProcessedAsync(file);
                    continue;
                }

                try
                {
                    file.Content = await GetBlobContentAsync(client, owner, repository, item.Sha, cancellationToken);
                    if (!string.IsNullOrWhiteSpace(file.Content))
                    {
                        file.Chunks.AddRange(RepositoryContentChunker.ChunkFile(path, item.Sha, file.Content, file.Language));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch blob {Sha} ({Path}) from {Owner}/{Repository}", item.Sha, path, owner, repository);
                    file.Status = RepositoryFileIndexStatus.Failed;
                    file.ErrorMessage = ex.Message;
                }

                await MarkFileProcessedAsync(file);
            }

            var chunks = status.Files
                .Where(file => file.Status == RepositoryFileIndexStatus.Indexed)
                .SelectMany(file => file.Chunks)
                .ToArray();

            try
            {
                var embeddings = await embeddingService.GenerateEmbeddingsAsync(owner, repository, chunks, cancellationToken);
                await embeddingStore.ReplaceRepositoryEmbeddingsAsync(owner, repository, embeddings, cancellationToken);
                status.EmbeddedChunkCount = embeddings.Count;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Embedding generation failed for {Owner}/{Repository}", owner, repository);
                status.ErrorMessage = string.IsNullOrWhiteSpace(status.ErrorMessage)
                    ? $"Embedding generation failed: {ex.Message}"
                    : $"{status.ErrorMessage} | Embedding generation failed: {ex.Message}";
            }

            status.CompletedAtUtc = DateTimeOffset.UtcNow;
            status.State = RepositoryIndexingState.Completed;
            if (status.FailedCount > 0 || !string.IsNullOrWhiteSpace(status.ErrorMessage))
            {
                status.State = RepositoryIndexingState.CompletedWithErrors;
            }

            status.CurrentFilePath = null;
            await statusStore.SaveAsync(status, cancellationToken);
            return status;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Repository ingestion failed for {Owner}/{Repository}", owner, repository);
            status.State = RepositoryIndexingState.Failed;
            status.ErrorMessage = ex.Message;
            status.CompletedAtUtc = DateTimeOffset.UtcNow;
            status.CurrentFilePath = null;
            await statusStore.SaveAsync(status, cancellationToken);
            return status;
        }
    }

    public Task<RepositoryIndexingStatus?> GetLatestStatusAsync(string owner, string repository, CancellationToken cancellationToken = default)
        => statusStore.GetAsync(owner, repository, cancellationToken);

    public async Task<int> RegenerateEmbeddingsAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        var status = await statusStore.GetAsync(owner, repository, cancellationToken)
            ?? throw new InvalidOperationException($"No repository status found for {owner}/{repository}.");

        var chunks = status.Files
            .Where(file => file.Status == RepositoryFileIndexStatus.Indexed)
            .SelectMany(file => file.Chunks)
            .ToArray();

        var embeddings = await embeddingService.GenerateEmbeddingsAsync(owner, repository, chunks, cancellationToken);
        await embeddingStore.ReplaceRepositoryEmbeddingsAsync(owner, repository, embeddings, cancellationToken);
        status.EmbeddedChunkCount = embeddings.Count;
        await statusStore.SaveAsync(status, cancellationToken);
        return embeddings.Count;
    }

    private async Task<GitHubRepositoryMetadata> GetRepositoryMetadataAsync(HttpClient client, string owner, string repository, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{owner}/{repository}");
        await AddGitHubAuthenticationAsync(request, cancellationToken);

        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, owner, repository, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<GitHubRepositoryMetadata>(cancellationToken))
            ?? throw new InvalidOperationException("GitHub repository metadata response was empty.");
    }

    private async Task<IReadOnlyList<GitHubTreeItem>> GetRepositoryTreeAsync(HttpClient client, string owner, string repository, string branch, CancellationToken cancellationToken)
    {
        var branchRef = Uri.EscapeDataString(branch);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{owner}/{repository}/git/trees/{branchRef}?recursive=1");
        await AddGitHubAuthenticationAsync(request, cancellationToken);

        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, owner, repository, cancellationToken);

        var tree = await response.Content.ReadFromJsonAsync<GitHubTreeResponse>(cancellationToken);
        return tree?.Tree ?? [];
    }

    private async Task<string> GetBlobContentAsync(HttpClient client, string owner, string repository, string sha, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{owner}/{repository}/git/blobs/{sha}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));
        await AddGitHubAuthenticationAsync(request, cancellationToken);

        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, owner, repository, cancellationToken);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task AddGitHubAuthenticationAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = await ResolveGitHubAccessTokenAsync(cancellationToken);
        GitHubAuthenticationHelper.ApplyAuthorization(request, accessToken);
    }

    private async Task<string?> ResolveGitHubAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (httpContextAccessor.HttpContext is not null)
        {
            try
            {
                var token = await httpContextAccessor.HttpContext.GetTokenAsync("access_token");
                if (!string.IsNullOrWhiteSpace(token))
                {
                    return token;
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Unable to read GitHub access token from the current request context.");
            }
        }

        return configuration["GitHub:Token"] ?? configuration["GitHub:AccessToken"];
    }

    private static async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response, string owner, string repository, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var details = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Repository {owner}/{repository} was not found.");
        }

        throw new InvalidOperationException($"GitHub request failed with {(int)response.StatusCode}: {details}");
    }
}
