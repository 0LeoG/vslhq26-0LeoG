namespace OnboardMe.Web.Models;

public sealed class StartHereSuggestion
{
    public required string Path { get; init; }

    public required string Reason { get; init; }
}
