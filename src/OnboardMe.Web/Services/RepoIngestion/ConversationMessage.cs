namespace OnboardMe.Web.Services.RepoIngestion;

/// <summary>The role of the author of a conversation message.</summary>
public enum ConversationRole
{
    User,
    Assistant
}

/// <summary>A single message in a multi-turn conversation.</summary>
public sealed class ConversationMessage
{
    /// <summary>Who sent this message.</summary>
    public required ConversationRole Role { get; init; }

    /// <summary>Text content of the message.</summary>
    public required string Content { get; init; }

    /// <summary>When the message was recorded (UTC).</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
