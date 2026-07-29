using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace OnboardMe.Web.Services.RepoIngestion;

public sealed class RepositoryIngestionService(
    IHttpClientFactory httpClientFactory,
    IRepositoryIndexingStatusStore statusStore,
    ILogger<RepositoryIngestionService> logger) : IRepositoryIngestionService
{
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

            foreach (var item in tree.Where(treeItem => string.Equals(treeItem.Type, "blob", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var path = item.Path;
                var extension = Path.GetExtension(path);
                var size = item.Size ?? 0;

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
                    status.Files.Add(file);
                    continue;
                }

                if (RepoIngestionRules.IsBinaryPath(path))
                {
                    file.Status = RepositoryFileIndexStatus.Skipped;
                    file.SkipReason = "binary-file";
                    status.Files.Add(file);
                    continue;
                }

                if (size > MaxFileSizeBytes)
                {
                    file.Status = RepositoryFileIndexStatus.Skipped;
                    file.SkipReason = "oversized-file";
                    status.Files.Add(file);
                    continue;
                }

                try
                {
                    file.Content = await GetBlobContentAsync(client, owner, repository, item.Sha, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch blob {Sha} ({Path}) from {Owner}/{Repository}", item.Sha, path, owner, repository);
                    file.Status = RepositoryFileIndexStatus.Failed;
                    file.ErrorMessage = ex.Message;
                }

                status.Files.Add(file);
            }

            status.CompletedAtUtc = DateTimeOffset.UtcNow;
            status.State = RepositoryIndexingState.Completed;
            if (status.FailedCount > 0)
            {
                status.State = RepositoryIndexingState.CompletedWithErrors;
            }

            await statusStore.SaveAsync(status, cancellationToken);
            return status;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Repository ingestion failed for {Owner}/{Repository}", owner, repository);
            status.State = RepositoryIndexingState.Failed;
            status.ErrorMessage = ex.Message;
            status.CompletedAtUtc = DateTimeOffset.UtcNow;
            await statusStore.SaveAsync(status, cancellationToken);
            return status;
        }
    }

    public Task<RepositoryIndexingStatus?> GetLatestStatusAsync(string owner, string repository, CancellationToken cancellationToken = default)
        => statusStore.GetAsync(owner, repository, cancellationToken);

    private static async Task<GitHubRepositoryMetadata> GetRepositoryMetadataAsync(HttpClient client, string owner, string repository, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"repos/{owner}/{repository}", cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, owner, repository, cancellationToken);
        return (await response.Content.ReadFromJsonAsync<GitHubRepositoryMetadata>(cancellationToken))
            ?? throw new InvalidOperationException("GitHub repository metadata response was empty.");
    }

    private static async Task<IReadOnlyList<GitHubTreeItem>> GetRepositoryTreeAsync(HttpClient client, string owner, string repository, string branch, CancellationToken cancellationToken)
    {
        var branchRef = Uri.EscapeDataString(branch);
        using var response = await client.GetAsync($"repos/{owner}/{repository}/git/trees/{branchRef}?recursive=1", cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, owner, repository, cancellationToken);

        var tree = await response.Content.ReadFromJsonAsync<GitHubTreeResponse>(cancellationToken);
        return tree?.Tree ?? [];
    }

    private static async Task<string> GetBlobContentAsync(HttpClient client, string owner, string repository, string sha, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{owner}/{repository}/git/blobs/{sha}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));

        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessStatusCodeAsync(response, owner, repository, cancellationToken);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return Encoding.UTF8.GetString(bytes);
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

    private sealed class GitHubRepositoryMetadata
    {
        [JsonPropertyName("default_branch")]
        public required string DefaultBranch { get; init; }
    }

    private sealed class GitHubTreeResponse
    {
        [JsonPropertyName("tree")]
        public List<GitHubTreeItem> Tree { get; init; } = [];
    }

    private sealed class GitHubTreeItem
    {
        [JsonPropertyName("path")]
        public required string Path { get; init; }

        [JsonPropertyName("type")]
        public required string Type { get; init; }

        [JsonPropertyName("sha")]
        public required string Sha { get; init; }

        [JsonPropertyName("size")]
        public long? Size { get; init; }
    }

    public const string GitHubApiClientName = "GitHubApi";
}
