namespace OnboardMe.Web.Services.RepoIngestion;

public enum RepositoryIndexingState
{
    Running,
    Completed,
    CompletedWithErrors,
    Failed
}