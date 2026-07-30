namespace OnboardMe.Web.Models;

/// <summary>Request body for the chat endpoint.</summary>
public sealed class ChatRequest
{
    /// <summary>The natural-language question to answer.</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>Maximum number of context chunks to retrieve. Defaults to 5 when omitted or <= 0.</summary>
    public int? TopK { get; init; }
}
