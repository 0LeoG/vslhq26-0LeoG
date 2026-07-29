namespace OnboardMe.Web.Services.RepoIngestion;

public interface IRepositoryIngestionService
{
    Task<RepositoryIndexingStatus> IngestRepositoryAsync(string owner, string repository, CancellationToken cancellationToken = default);

    Task<RepositoryIndexingStatus?> GetLatestStatusAsync(string owner, string repository, CancellationToken cancellationToken = default);
}

public interface IRepositoryIndexingStatusStore
{
    Task SaveAsync(RepositoryIndexingStatus status, CancellationToken cancellationToken = default);

    Task<RepositoryIndexingStatus?> GetAsync(string owner, string repository, CancellationToken cancellationToken = default);
}
