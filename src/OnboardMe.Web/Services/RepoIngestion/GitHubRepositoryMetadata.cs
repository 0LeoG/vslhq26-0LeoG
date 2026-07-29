using System.Text.Json.Serialization;

namespace OnboardMe.Web.Services.RepoIngestion;

internal sealed class GitHubRepositoryMetadata
{
    [JsonPropertyName("default_branch")]
    public required string DefaultBranch { get; init; }
}