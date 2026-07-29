namespace OnboardMe.Web.Services.RepoIngestion;

public interface IRepositoryIngestionService
{
    Task<RepositoryIndexingStatus> StartRepositoryIngestionAsync(string owner, string repository, CancellationToken cancellationToken = default);

    Task<RepositoryIndexingStatus> IngestRepositoryAsync(string owner, string repository, CancellationToken cancellationToken = default);

    Task<RepositoryIndexingStatus?> GetLatestStatusAsync(string owner, string repository, CancellationToken cancellationToken = default);

    Task<int> RegenerateEmbeddingsAsync(string owner, string repository, CancellationToken cancellationToken = default);
}
