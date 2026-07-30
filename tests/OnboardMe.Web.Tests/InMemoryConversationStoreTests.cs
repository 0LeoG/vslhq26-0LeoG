using OnboardMe.Web.Services.RepoIngestion;

namespace OnboardMe.Web.Tests;

public class InMemoryConversationStoreTests
{
    // -------------------------------------------------------------------------
    // GetHistoryAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetHistoryAsync_ReturnsEmpty_WhenNoHistoryExists()
    {
        var store = new InMemoryConversationStore();

        var history = await store.GetHistoryAsync("session1", "owner", "repo");

        Assert.Empty(history);
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsMessages_InAdditionOrder()
    {
        var store = new InMemoryConversationStore();

        await store.AddMessageAsync("s1", "owner", "repo", UserMessage("hello"));
        await store.AddMessageAsync("s1", "owner", "repo", AssistantMessage("hi"));

        var history = await store.GetHistoryAsync("s1", "owner", "repo");

        Assert.Equal(2, history.Count);
        Assert.Equal(ConversationRole.User,      history[0].Role);
        Assert.Equal("hello",                    history[0].Content);
        Assert.Equal(ConversationRole.Assistant, history[1].Role);
        Assert.Equal("hi",                       history[1].Content);
    }

    // -------------------------------------------------------------------------
    // Bounded history
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddMessageAsync_EvictsOldestMessages_WhenBoundExceeded()
    {
        var store = new InMemoryConversationStore();

        // Fill beyond the limit.
        for (var i = 0; i < InMemoryConversationStore.MaxMessages + 5; i++)
        {
            await store.AddMessageAsync("s1", "owner", "repo", UserMessage($"msg-{i}"));
        }

        var history = await store.GetHistoryAsync("s1", "owner", "repo");

        Assert.Equal(InMemoryConversationStore.MaxMessages, history.Count);
        // The earliest messages (0..4) should have been evicted.
        Assert.Equal("msg-5", history[0].Content);
    }

    [Fact]
    public async Task AddMessageAsync_ExactlyAtBound_DoesNotEvict()
    {
        var store = new InMemoryConversationStore();

        for (var i = 0; i < InMemoryConversationStore.MaxMessages; i++)
        {
            await store.AddMessageAsync("s1", "owner", "repo", UserMessage($"msg-{i}"));
        }

        var history = await store.GetHistoryAsync("s1", "owner", "repo");

        Assert.Equal(InMemoryConversationStore.MaxMessages, history.Count);
        Assert.Equal("msg-0", history[0].Content);
    }

    // -------------------------------------------------------------------------
    // Isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetHistoryAsync_IsolatesConversationsBySession()
    {
        var store = new InMemoryConversationStore();

        await store.AddMessageAsync("session-A", "owner", "repo", UserMessage("from A"));
        await store.AddMessageAsync("session-B", "owner", "repo", UserMessage("from B"));

        var histA = await store.GetHistoryAsync("session-A", "owner", "repo");
        var histB = await store.GetHistoryAsync("session-B", "owner", "repo");

        Assert.Single(histA);
        Assert.Equal("from A", histA[0].Content);
        Assert.Single(histB);
        Assert.Equal("from B", histB[0].Content);
    }

    [Fact]
    public async Task GetHistoryAsync_IsolatesConversationsByRepository()
    {
        var store = new InMemoryConversationStore();

        await store.AddMessageAsync("s1", "owner", "repo-a", UserMessage("repo-a message"));
        await store.AddMessageAsync("s1", "owner", "repo-b", UserMessage("repo-b message"));

        var histA = await store.GetHistoryAsync("s1", "owner", "repo-a");
        var histB = await store.GetHistoryAsync("s1", "owner", "repo-b");

        Assert.Single(histA);
        Assert.Equal("repo-a message", histA[0].Content);
        Assert.Single(histB);
        Assert.Equal("repo-b message", histB[0].Content);
    }

    [Fact]
    public async Task GetHistoryAsync_IsCaseInsensitive_ForOwnerAndRepository()
    {
        var store = new InMemoryConversationStore();

        await store.AddMessageAsync("s1", "Owner", "Repo", UserMessage("hello"));

        var history = await store.GetHistoryAsync("s1", "OWNER", "REPO");

        Assert.Single(history);
    }

    // -------------------------------------------------------------------------
    // ClearAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClearAsync_RemovesAllMessages_ForTheGivenKey()
    {
        var store = new InMemoryConversationStore();

        await store.AddMessageAsync("s1", "owner", "repo", UserMessage("hello"));
        await store.AddMessageAsync("s1", "owner", "repo", AssistantMessage("world"));
        await store.ClearAsync("s1", "owner", "repo");

        var history = await store.GetHistoryAsync("s1", "owner", "repo");

        Assert.Empty(history);
    }

    [Fact]
    public async Task ClearAsync_DoesNotAffectOtherSessions()
    {
        var store = new InMemoryConversationStore();

        await store.AddMessageAsync("s1", "owner", "repo", UserMessage("stay"));
        await store.AddMessageAsync("s2", "owner", "repo", UserMessage("gone"));
        await store.ClearAsync("s2", "owner", "repo");

        var histS1 = await store.GetHistoryAsync("s1", "owner", "repo");
        var histS2 = await store.GetHistoryAsync("s2", "owner", "repo");

        Assert.Single(histS1);
        Assert.Empty(histS2);
    }

    [Fact]
    public async Task ClearAsync_OnNonExistentKey_DoesNotThrow()
    {
        var store = new InMemoryConversationStore();

        // Should complete without throwing.
        await store.ClearAsync("s1", "owner", "repo");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ConversationMessage UserMessage(string content)
        => new() { Role = ConversationRole.User, Content = content };

    private static ConversationMessage AssistantMessage(string content)
        => new() { Role = ConversationRole.Assistant, Content = content };
}
