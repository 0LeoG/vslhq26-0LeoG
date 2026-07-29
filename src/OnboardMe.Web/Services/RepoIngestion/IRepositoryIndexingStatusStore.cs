namespace OnboardMe.Web.Services.RepoIngestion;

public interface IRepositoryIndexingStatusStore
{
    Task SaveAsync(RepositoryIndexingStatus status, CancellationToken cancellationToken = default);

    Task<RepositoryIndexingStatus?> GetAsync(string owner, string repository, CancellationToken cancellationToken = default);
}