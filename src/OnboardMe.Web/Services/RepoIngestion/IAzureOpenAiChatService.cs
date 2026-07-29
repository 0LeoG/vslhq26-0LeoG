namespace OnboardMe.Web.Services.RepoIngestion;

/// <summary>
/// Abstraction for generating grounded chat answers from Azure OpenAI.
/// </summary>
public interface IAzureOpenAiChatService
{
    /// <summary>
    /// Rewrites a follow-up <paramref name="question"/> into a self-contained retrieval query
    /// using the <paramref name="recentHistory"/> of the current conversation. Returns the
    /// original question unchanged when history is empty.
    /// </summary>
    /// <param name="question">The raw user question (may be a follow-up).</param>
    /// <param name="recentHistory">Recent conversation turns used as rewrite context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A standalone query string suitable for semantic search.</returns>
    Task<string> RewriteQueryAsync(
        string question,
        IReadOnlyList<ConversationMessage> recentHistory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the <paramref name="question"/> and the pre-retrieved <paramref name="contextChunks"/>
    /// to Azure OpenAI and returns a grounded answer with file-path citations.
    /// </summary>
    /// <param name="owner">GitHub repository owner.</param>
    /// <param name="repository">GitHub repository name.</param>
    /// <param name="question">The natural-language question to answer.</param>
    /// <param name="contextChunks">Ranked chunks to use as context (highest-score first).</param>
    /// <param name="conversationHistory">
    /// Optional prior conversation turns included for multi-turn context.
    /// Retrieval results, not prior assistant messages, remain the authoritative source of citations.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ChatAnswer"/> containing the answer text and file citations.</returns>
    Task<ChatAnswer> AnswerAsync(
        string owner,
        string repository,
        string question,
        IReadOnlyList<VectorSearchResult> contextChunks,
        IReadOnlyList<ConversationMessage>? conversationHistory = null,
        CancellationToken cancellationToken = default);
}
