namespace OnboardMe.Web.Models;

/// <summary>Request body for the semantic search endpoint.</summary>
public sealed class SearchRequest
{
    /// <summary>The natural-language question to search for.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Maximum number of results to return. Defaults to 5 when omitted or <= 0.</summary>
    public int? TopK { get; init; }
}
