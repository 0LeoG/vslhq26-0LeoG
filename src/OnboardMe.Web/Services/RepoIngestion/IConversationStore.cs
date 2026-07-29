namespace OnboardMe.Web.Services.RepoIngestion;

/// <summary>
/// Abstraction for persisting bounded conversation histories, isolated by session and repository.
/// Implement <see cref="IConversationStore"/> with a database-backed store for production use;
/// the MVP ships with <see cref="InMemoryConversationStore"/>.
/// </summary>
public interface IConversationStore
{
    /// <summary>
    /// Returns the current bounded conversation history for the given session and repository.
    /// Returns an empty list when no history exists.
    /// </summary>
    Task<IReadOnlyList<ConversationMessage>> GetHistoryAsync(
        string sessionId,
        string owner,
        string repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends <paramref name="message"/> to the conversation, evicting the oldest message(s)
    /// when the history bound is reached.
    /// </summary>
    Task AddMessageAsync(
        string sessionId,
        string owner,
        string repository,
        ConversationMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all messages for the given session and repository, starting a new conversation.
    /// </summary>
    Task ClearAsync(
        string sessionId,
        string owner,
        string repository,
        CancellationToken cancellationToken = default);
}
