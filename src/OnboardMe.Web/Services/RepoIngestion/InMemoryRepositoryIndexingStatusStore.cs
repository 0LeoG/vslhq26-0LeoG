using System.Collections.Concurrent;

namespace OnboardMe.Web.Services.RepoIngestion;

public sealed class InMemoryRepositoryIndexingStatusStore : IRepositoryIndexingStatusStore
{
    private readonly ConcurrentDictionary<string, RepositoryIndexingStatus> statuses = new(StringComparer.OrdinalIgnoreCase);

    public Task SaveAsync(RepositoryIndexingStatus status, CancellationToken cancellationToken = default)
    {
        statuses[BuildKey(status.Owner, status.Repository)] = status;
        return Task.CompletedTask;
    }

    public Task<RepositoryIndexingStatus?> GetAsync(string owner, string repository, CancellationToken cancellationToken = default)
    {
        statuses.TryGetValue(BuildKey(owner, repository), out var status);
        return Task.FromResult(status);
    }

    public Task<IReadOnlyList<RepositoryIndexingStatus>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<RepositoryIndexingStatus>>(statuses.Values.ToArray());

    private static string BuildKey(string owner, string repository) => $"{owner}/{repository}";
}
