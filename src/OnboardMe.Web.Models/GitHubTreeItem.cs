using System.Text.Json.Serialization;

namespace OnboardMe.Web.Models;

public sealed class GitHubTreeItem
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("sha")]
    public required string Sha { get; init; }

    [JsonPropertyName("size")]
    public long? Size { get; init; }
}
