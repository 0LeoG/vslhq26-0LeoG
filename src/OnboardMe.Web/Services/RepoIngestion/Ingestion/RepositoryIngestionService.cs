using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Collections.Concurrent;
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
    private const int MaxConcurrentFileFetches = 8;
    private const int MaxConcurrentEmbeddingCalls = 2;

    private readonly ConcurrentDictionary<string, Task<RepositoryIndexingStatus>> runningIngestions = new(StringComparer.OrdinalIgnoreCase);

    public async Task<RepositoryIndexingStatus> StartRepositoryIngestionAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(owner, repository);
        if (runningIngestions.TryGetValue(key, out var existingTask) && !existingTask.IsCompleted)
        {
            return await GetLatestStatusAsync(owner, repository, cancellationToken)
                ?? new RepositoryIndexingStatus
                {
                    Owner = owner,
                    Repository = repository,
                    Branch = "unknown",
                    StartedAtUtc = DateTimeOffset.UtcNow,
                    State = RepositoryIndexingState.Running
                };
        }

        var accessToken = await ResolveGitHubAccessTokenAsync(cancellationToken);
        var task = IngestRepositoryCoreAsync(owner, repository, accessToken, CancellationToken.None);
        runningIngestions[key] = task;
        _ = task.ContinueWith(
            _ => runningIngestions.TryRemove(key, out Task<RepositoryIndexingStatus>? _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return await GetLatestStatusAsync(owner, repository, cancellationToken)
            ?? new RepositoryIndexingStatus
            {
                Owner = owner,
                Repository = repository,
                Branch = "unknown",
                StartedAtUtc = DateTimeOffset.UtcNow,
                State = RepositoryIndexingState.Running
            };
    }

    public async Task<RepositoryIndexingStatus> IngestRepositoryAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        var accessToken = await ResolveGitHubAccessTokenAsync(cancellationToken);
        return await IngestRepositoryCoreAsync(owner, repository, accessToken, cancellationToken);
    }

    private async Task<RepositoryIndexingStatus> IngestRepositoryCoreAsync(
        string owner,
        string repository,
        string? accessToken,
        CancellationToken cancellationToken)
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
        using var statusWriteLock = new SemaphoreSlim(1, 1);

        async Task SaveStatusAsync(Action<RepositoryIndexingStatus>? mutator = null)
        {
            await statusWriteLock.WaitAsync(cancellationToken);
            try
            {
                mutator?.Invoke(status);
                await statusStore.SaveAsync(status, cancellationToken);
            }
            finally
            {
                statusWriteLock.Release();
            }
        }

        await SaveStatusAsync();
        await embeddingStore.ReplaceRepositoryEmbeddingsAsync(owner, repository, [], cancellationToken);

        try
        {
            var repositoryMetadata = await GetRepositoryMetadataAsync(client, owner, repository, accessToken, cancellationToken);
            status.Branch = repositoryMetadata.DefaultBranch;

            var tree = await GetRepositoryTreeAsync(client, owner, repository, repositoryMetadata.DefaultBranch, accessToken, cancellationToken);
            var filesToProcess = tree
                .Where(treeItem => string.Equals(treeItem.Type, "blob", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            status.TotalFileCount = filesToProcess.Length;
            await SaveStatusAsync();

            using var fileProcessingThrottle = new SemaphoreSlim(MaxConcurrentFileFetches);
            using var embeddingThrottle = new SemaphoreSlim(MaxConcurrentEmbeddingCalls);
            var processingTasks = filesToProcess.Select(async item =>
            {
                await fileProcessingThrottle.WaitAsync(cancellationToken);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var path = item.Path;
                    var extension = Path.GetExtension(path);
                    var size = item.Size ?? 0;
                    await SaveStatusAsync(currentStatus => currentStatus.CurrentFilePath = path);

                    async Task MarkFileProcessedAsync(RepositoryFileIngestionRecord processedFile)
                        => await SaveStatusAsync(currentStatus =>
                        {
                            currentStatus.Files.Add(processedFile);
                            currentStatus.ProcessedFileCount++;
                            currentStatus.ProcessedChunkCount += processedFile.Chunks.Count;
                        });

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
                        return;
                    }

                    if (RepoIngestionRules.IsBinaryPath(path))
                    {
                        file.Status = RepositoryFileIndexStatus.Skipped;
                        file.SkipReason = "binary-file";
                        await MarkFileProcessedAsync(file);
                        return;
                    }

                    if (size > MaxFileSizeBytes)
                    {
                        file.Status = RepositoryFileIndexStatus.Skipped;
                        file.SkipReason = "oversized-file";
                        await MarkFileProcessedAsync(file);
                        return;
                    }

                    try
                    {
                        file.Content = await GetBlobContentAsync(client, owner, repository, item.Sha, accessToken, cancellationToken);
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

                    if (file.Status == RepositoryFileIndexStatus.Indexed && file.Chunks.Count > 0)
                    {
                        await embeddingThrottle.WaitAsync(cancellationToken);
                        try
                        {
                            var embeddings = await embeddingService.GenerateEmbeddingsAsync(owner, repository, file.Chunks, cancellationToken);
                            await embeddingStore.UpsertRepositoryEmbeddingsAsync(owner, repository, embeddings, cancellationToken);
                            await SaveStatusAsync(currentStatus => currentStatus.EmbeddedChunkCount += embeddings.Count);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Embedding generation failed for {Owner}/{Repository} file {Path}", owner, repository, path);
                            await SaveStatusAsync(currentStatus =>
                            {
                                var embeddingFailure = $"Embedding generation failed for {path}: {ex.Message}";
                                currentStatus.ErrorMessage = string.IsNullOrWhiteSpace(currentStatus.ErrorMessage)
                                    ? embeddingFailure
                                    : $"{currentStatus.ErrorMessage} | {embeddingFailure}";
                            });
                        }
                        finally
                        {
                            embeddingThrottle.Release();
                        }
                    }
                }
                finally
                {
                    fileProcessingThrottle.Release();
                }
            }).ToArray();

            await Task.WhenAll(processingTasks);

            status.CompletedAtUtc = DateTimeOffset.UtcNow;
            status.State = RepositoryIndexingState.Completed;
            if (status.FailedCount > 0 || !string.IsNullOrWhiteSpace(status.ErrorMessage))
            {
                status.State = RepositoryIndexingState.CompletedWithErrors;
            }

            status.CurrentFilePath = null;
            await SaveStatusAsync();
            return status;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Repository ingestion failed for {Owner}/{Repository}", owner, repository);
            status.State = RepositoryIndexingState.Failed;
            status.ErrorMessage = ex.Message;
            status.CompletedAtUtc = DateTimeOffset.UtcNow;
            status.CurrentFilePath = null;
            await SaveStatusAsync();
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

    private async Task<GitHubRepositoryMetadata> GetRepositoryMetadataAsync(
        HttpClient client,
        string owner,
        string repository,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{owner}/{repository}");
        await AddGitHubAuthenticationAsync(request, accessToken, cancellationToken);

        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, owner, repository, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<GitHubRepositoryMetadata>(cancellationToken))
            ?? throw new InvalidOperationException("GitHub repository metadata response was empty.");
    }

    private async Task<IReadOnlyList<GitHubTreeItem>> GetRepositoryTreeAsync(
        HttpClient client,
        string owner,
        string repository,
        string branch,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        var branchRef = Uri.EscapeDataString(branch);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{owner}/{repository}/git/trees/{branchRef}?recursive=1");
        await AddGitHubAuthenticationAsync(request, accessToken, cancellationToken);

        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, owner, repository, cancellationToken);

        var tree = await response.Content.ReadFromJsonAsync<GitHubTreeResponse>(cancellationToken);
        return tree?.Tree ?? [];
    }

    private async Task<string> GetBlobContentAsync(HttpClient client, string owner, string repository, string sha, string? accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{owner}/{repository}/git/blobs/{sha}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));
        await AddGitHubAuthenticationAsync(request, accessToken, cancellationToken);

        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, owner, repository, cancellationToken);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return Encoding.UTF8.GetString(bytes);
    }

    private async Task AddGitHubAuthenticationAsync(HttpRequestMessage request, string? accessToken, CancellationToken cancellationToken)
    {
        var token = accessToken ?? await ResolveGitHubAccessTokenAsync(cancellationToken);
        GitHubAuthenticationHelper.ApplyAuthorization(request, token);
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

    private static string BuildKey(string owner, string repository) => $"{owner}/{repository}";
}
