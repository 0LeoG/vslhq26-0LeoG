using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace OnboardMe.Web.Services.RepoIngestion;

/// <summary>
/// Calls the Azure OpenAI Chat Completions API to answer a question grounded in
/// retrieved repository chunks. Implements the Strategy pattern: prompt assembly
/// and model invocation are isolated here so callers are decoupled from the
/// underlying AI provider.
/// </summary>
public sealed class AzureOpenAiChatService(
    IHttpClientFactory httpClientFactory,
    IOptions<AzureOpenAiEmbeddingsOptions> optionsAccessor,
    ILogger<AzureOpenAiChatService> logger) : IAzureOpenAiChatService
{
    /// <summary>Named HTTP client used for chat completion requests.</summary>
    public const string AzureOpenAiChatClientName = "AzureOpenAiChat";

    /// <summary>
    /// Prefix the model uses to signal that a clarification is needed rather than a direct answer.
    /// </summary>
    public const string ClarificationPrefix = "CLARIFICATION:";

    private const int MaxAttempts = 3;

    /// <inheritdoc/>
    public async Task<string> RewriteQueryAsync(
        string question,
        IReadOnlyList<ConversationMessage> recentHistory,
        CancellationToken cancellationToken = default)
    {
        if (recentHistory.Count == 0)
        {
            return question;
        }

        var options = optionsAccessor.Value;
        if (!options.IsChatConfigured)
        {
            logger.LogWarning("Azure OpenAI chat is not configured; skipping query rewrite.");
            return question;
        }

        var systemPrompt =
            """
            You are a query-rewriting assistant.
            Given a conversation history and a follow-up question, rewrite the question into a
            fully self-contained search query that can be understood without the prior context.
            Preserve the original intent and keep the rewrite concise.
            Respond with ONLY the rewritten query — no explanation, no punctuation changes.
            If the question is already self-contained, return it unchanged.
            """;

        var historyText = BuildHistoryText(recentHistory);
        var userPrompt =
            $"""
            ## Conversation history
            {historyText}

            ## Follow-up question
            {question}
            """;

        var chatRequest = new AzureChatRequest
        {
            Messages =
            [
                new AzureChatMessage { Role = "system", Content = systemPrompt },
                new AzureChatMessage { Role = "user",   Content = userPrompt   }
            ],
            MaxTokens = 256,
            Temperature = 0.0f
        };

        try
        {
            var rewritten = await SendChatRequestAsync(chatRequest, cancellationToken);
            var trimmed = rewritten.Trim();
            logger.LogInformation("Query rewritten from '{Original}' to '{Rewritten}'.", question, trimmed);
            return string.IsNullOrWhiteSpace(trimmed) ? question : trimmed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Query rewrite failed; using original question.");
            return question;
        }
    }

    /// <inheritdoc/>
    public async Task<ChatAnswer> AnswerAsync(
        string owner,
        string repository,
        string question,
        IReadOnlyList<VectorSearchResult> contextChunks,
        IReadOnlyList<ConversationMessage>? conversationHistory = null,
        CancellationToken cancellationToken = default)
    {
        var options = optionsAccessor.Value;
        if (!options.IsChatConfigured)
        {
            const string message = "Azure OpenAI chat is not configured. Provide Endpoint, ApiKey, and ChatDeployment.";
            logger.LogWarning(message);
            throw new InvalidOperationException(message);
        }

        // Build a deduplicated, ordered list of citations from the provided chunks.
        var citations = BuildCitations(contextChunks);

        var systemPrompt = BuildSystemPrompt(owner, repository);
        var userPrompt = BuildUserPrompt(question, contextChunks, conversationHistory);

        var messages = new List<AzureChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };

        // Inject prior turns as alternating user/assistant messages so the model
        // has multi-turn context; retrieval chunks (injected via the user prompt)
        // remain the authoritative source for citations.
        if (conversationHistory is { Count: > 0 })
        {
            foreach (var msg in conversationHistory)
            {
                messages.Add(new AzureChatMessage
                {
                    Role    = msg.Role == ConversationRole.User ? "user" : "assistant",
                    Content = msg.Content
                });
            }
        }

        messages.Add(new AzureChatMessage { Role = "user", Content = userPrompt });

        var chatRequest = new AzureChatRequest
        {
            Messages    = messages,
            MaxTokens   = 1024,
            Temperature = 0.2f
        };

        var answerText = await SendChatRequestAsync(chatRequest, cancellationToken);

        var isClarification = answerText.StartsWith(ClarificationPrefix, StringComparison.OrdinalIgnoreCase);
        if (isClarification)
        {
            answerText = answerText[ClarificationPrefix.Length..].TrimStart();
        }

        logger.LogInformation(
            "Chat answer generated for {Owner}/{Repository} (clarification={IsClarification}, {CitationCount} citations).",
            SanitizeLogValue(owner), SanitizeLogValue(repository), isClarification, citations.Count);

        return new ChatAnswer
        {
            Answer          = answerText,
            Citations       = isClarification ? [] : citations,
            IsClarification = isClarification
        };
    }

    // -------------------------------------------------------------------------
    // Prompt builders
    // -------------------------------------------------------------------------

    private static string BuildSystemPrompt(string owner, string repository)
        => $"""
            You are a helpful onboarding assistant for the GitHub repository {owner}/{repository}.
            Answer the developer's question using ONLY the code and documentation excerpts provided below.
            Keep the answer concise and factual.
            If the provided context does not contain enough information to answer, say so honestly.
            Do not invent file names, line numbers, or behaviours that are not shown in the context.
            If the question is ambiguous or has multiple plausible meanings that require different
            repository evidence, ask a single focused clarification question instead of guessing.
            When asking for clarification, begin your response with exactly "{ClarificationPrefix} ".
            """;

    private static string BuildUserPrompt(
        string question,
        IReadOnlyList<VectorSearchResult> chunks,
        IReadOnlyList<ConversationMessage>? history)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Repository context");
        sb.AppendLine();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i].Chunk;
            sb.AppendLine($"### [{i + 1}] {chunk.SourcePath} (lines {chunk.StartLine}–{chunk.EndLine})");
            sb.AppendLine("```");
            sb.AppendLine(chunk.Content);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## Question");
        sb.AppendLine(question);
        return sb.ToString();
    }

    private static string BuildHistoryText(IReadOnlyList<ConversationMessage> history)
    {
        var sb = new StringBuilder();
        foreach (var msg in history)
        {
            var label = msg.Role == ConversationRole.User ? "User" : "Assistant";
            sb.AppendLine($"{label}: {msg.Content}");
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // Citation builder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Produces a deduplicated, ordered list of <see cref="FileCitation" /> objects
    /// from the retrieved chunks, preserving relevance order.
    /// </summary>
    private static IReadOnlyList<FileCitation> BuildCitations(IReadOnlyList<VectorSearchResult> chunks)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var citations = new List<FileCitation>(chunks.Count);

        foreach (var result in chunks)
        {
            var key = $"{result.Chunk.SourcePath}:{result.Chunk.StartLine}:{result.Chunk.EndLine}";
            if (seen.Add(key))
            {
                citations.Add(new FileCitation
                {
                    Path      = result.Chunk.SourcePath,
                    StartLine = result.Chunk.StartLine,
                    EndLine   = result.Chunk.EndLine
                });
            }
        }

        return citations;
    }

    // -------------------------------------------------------------------------
    // HTTP request helper
    // -------------------------------------------------------------------------

    private async Task<string> SendChatRequestAsync(
        AzureChatRequest chatRequest,
        CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;
        var endpoint   = options.Endpoint!.TrimEnd('/');
        var deployment = Uri.EscapeDataString(options.ChatDeployment!);
        var apiVersion = string.IsNullOrWhiteSpace(options.ApiVersion) ? "2024-02-01" : options.ApiVersion.Trim();
        var requestUri = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}";

        HttpStatusCode? lastStatusCode = null;
        string? lastDetails = null;
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                {
                    Content = JsonContent.Create(chatRequest)
                };
                request.Headers.Add("api-key", options.ApiKey);

                using var response = await httpClientFactory
                    .CreateClient(AzureOpenAiChatClientName)
                    .SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await ParseAnswerAsync(response, cancellationToken);
                }

                lastStatusCode = response.StatusCode;
                lastDetails = await response.Content.ReadAsStringAsync(cancellationToken);

                if (IsTransientStatusCode(response.StatusCode) && attempt < MaxAttempts)
                {
                    logger.LogWarning(
                        "Azure OpenAI chat request attempt {Attempt} failed with transient status {StatusCode}. Retrying.",
                        attempt, (int)response.StatusCode);
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                    continue;
                }

                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastException = ex;
                if (attempt < MaxAttempts)
                {
                    logger.LogWarning(ex, "Azure OpenAI chat request attempt {Attempt} failed. Retrying.", attempt);
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                    continue;
                }
            }
        }

        if (lastException is not null)
        {
            throw new InvalidOperationException("Azure OpenAI chat request failed after retries.", lastException);
        }

        throw new InvalidOperationException(
            $"Azure OpenAI chat request failed with {(int?)lastStatusCode} {lastStatusCode}: {lastDetails}");
    }

    private static async Task<string> ParseAnswerAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var parsed = await response.Content.ReadFromJsonAsync<AzureChatResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Azure OpenAI chat response was empty.");

        var choice = parsed.Choices.FirstOrDefault();
        var content = choice?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Azure OpenAI chat response did not include answer content.");
        }

        return content;
    }

    private static TimeSpan GetRetryDelay(int attempt)
        => TimeSpan.FromMilliseconds(250 * attempt);

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout
           || statusCode == (HttpStatusCode)429
           || (int)statusCode >= 500;

    private static string SanitizeLogValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "<empty>" : value;

    private sealed class AzureChatRequest
    {
        public required List<AzureChatMessage> Messages { get; init; }

        [JsonPropertyName("max_completion_tokens")]
        public int MaxTokens { get; init; }

        public float Temperature { get; init; }
    }

    private sealed class AzureChatMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private sealed class AzureChatResponse
    {
        [JsonPropertyName("choices")]
        public List<AzureChatChoice> Choices { get; init; } = [];
    }

    private sealed class AzureChatChoice
    {
        [JsonPropertyName("message")]
        public AzureChatMessage? Message { get; init; }
    }
}
