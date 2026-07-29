using System.Text.Json.Serialization;

namespace OnboardMe.Web.Models;

public sealed class GitHubRepositoryMetadata
{
    [JsonPropertyName("default_branch")]
    public required string DefaultBranch { get; init; }
}
