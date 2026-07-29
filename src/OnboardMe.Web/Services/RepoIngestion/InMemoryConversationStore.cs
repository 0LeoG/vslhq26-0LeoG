using System.Collections.Concurrent;

namespace OnboardMe.Web.Services.RepoIngestion;

/// <summary>
/// In-memory implementation of <see cref="IConversationStore"/>.
/// Conversations are bounded to <see cref="MaxMessages"/> messages per session/repository key.
/// State is lost on application restart; replace with a database-backed implementation for production.
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    /// <summary>
    /// Maximum number of messages retained per conversation. Oldest messages are evicted once
    /// this limit is reached, keeping the history window manageable for prompt construction.
    /// </summary>
    public const int MaxMessages = 20;

    // Key format: "{sessionId}|{owner}/{repository}" (case-insensitive owner/repo).
    private readonly ConcurrentDictionary<string, Queue<ConversationMessage>> _histories
        = new(StringComparer.OrdinalIgnoreCase);

    // Per-key locks so we never lose messages under concurrent access.
    private readonly ConcurrentDictionary<string, object> _locks
        = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ConversationMessage>> GetHistoryAsync(
        string sessionId,
        string owner,
        string repository,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(sessionId, owner, repository);
        if (_histories.TryGetValue(key, out var queue))
        {
            lock (GetLock(key))
            {
                return Task.FromResult<IReadOnlyList<ConversationMessage>>(queue.ToArray());
            }
        }

        return Task.FromResult<IReadOnlyList<ConversationMessage>>([]);
    }

    /// <inheritdoc/>
    public Task AddMessageAsync(
        string sessionId,
        string owner,
        string repository,
        ConversationMessage message,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(sessionId, owner, repository);
        var queue = _histories.GetOrAdd(key, _ => new Queue<ConversationMessage>());

        lock (GetLock(key))
        {
            queue.Enqueue(message);
            while (queue.Count > MaxMessages)
            {
                queue.Dequeue();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ClearAsync(
        string sessionId,
        string owner,
        string repository,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(sessionId, owner, repository);
        if (_histories.TryGetValue(key, out var queue))
        {
            lock (GetLock(key))
            {
                queue.Clear();
            }
        }

        return Task.CompletedTask;
    }

    private object GetLock(string key)
        => _locks.GetOrAdd(key, _ => new object());

    private static string BuildKey(string sessionId, string owner, string repository)
        => $"{sessionId}|{owner}/{repository}";
}
