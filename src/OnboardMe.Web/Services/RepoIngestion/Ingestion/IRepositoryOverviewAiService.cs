namespace OnboardMe.Web.Services.RepoIngestion;

public interface IRepositoryOverviewAiService
{
    Task<RepositoryOverviewAiSummary> GenerateAsync(
        RepositoryIndexingStatus status,
        CancellationToken cancellationToken = default);
}
