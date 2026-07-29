namespace OnboardMe.Web.Services.RepoIngestion;

public sealed class AzureOpenAiEmbeddingsOptions
{
    public const string SectionName = "AzureOpenAI";

    public string? Endpoint { get; init; }

    public string? ApiKey { get; init; }

    public string? EmbeddingsDeployment { get; init; }

    public string? ChatDeployment { get; init; }

    public string ApiVersion { get; init; } = "2024-02-01";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(EmbeddingsDeployment);

    public bool IsChatConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ChatDeployment);
}
