using System.Text.Json.Serialization;

namespace OnboardMe.Web.Models;

public sealed class GitHubTreeResponse
{
    [JsonPropertyName("tree")]
    public List<GitHubTreeItem> Tree { get; init; } = [];
}
