namespace OnboardMe.Web.Services.RepoIngestion;

/// <summary>
/// Abstraction for generating grounded chat answers from Azure OpenAI.
/// </summary>
public interface IAzureOpenAiChatService
{
    /// <summary>
    /// Sends the <paramref name="question"/> and the pre-retrieved <paramref name="contextChunks"/>
    /// to Azure OpenAI and returns a grounded answer with file-path citations.
    /// </summary>
    /// <param name="owner">GitHub repository owner.</param>
    /// <param name="repository">GitHub repository name.</param>
    /// <param name="question">The natural-language question to answer.</param>
    /// <param name="contextChunks">Ranked chunks to use as context (highest-score first).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ChatAnswer"/> containing the answer text and file citations.</returns>
    Task<ChatAnswer> AnswerAsync(
        string owner,
        string repository,
        string question,
        IReadOnlyList<VectorSearchResult> contextChunks,
        CancellationToken cancellationToken = default);
}
