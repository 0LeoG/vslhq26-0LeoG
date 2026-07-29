using System.Text.Json.Serialization;

namespace OnboardMe.Web.Services.RepoIngestion;

internal sealed class GitHubTreeResponse
{
    [JsonPropertyName("tree")]
    public List<GitHubTreeItem> Tree { get; init; } = [];
}