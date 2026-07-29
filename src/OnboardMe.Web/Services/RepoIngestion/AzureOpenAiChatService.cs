using System.Net;
using System.Net.Http.Json;
using System.Text;
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

    private const int MaxAttempts = 3;

    /// <inheritdoc/>
    public async Task<ChatAnswer> AnswerAsync(
        string owner,
        string repository,
        string question,
        IReadOnlyList<VectorSearchResult> contextChunks,
        CancellationToken cancellationToken = default)
    {
        var options = optionsAccessor.Value;
        if (!options.IsChatConfigured)
        {
            const string message = "Azure OpenAI chat is not configured. Provide Endpoint, ApiKey, and ChatDeployment.";
            logger.LogWarning(message);
            throw new InvalidOperationException(message);
        }

        var endpoint = options.Endpoint!.TrimEnd('/');
        var deployment = Uri.EscapeDataString(options.ChatDeployment!);
        var apiVersion = string.IsNullOrWhiteSpace(options.ApiVersion) ? "2024-02-01" : options.ApiVersion.Trim();
        var requestUri = $"{endpoint}/openai/deployments/{deployment}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}";

        // Build a deduplicated, ordered list of citations from the provided chunks.
        var citations = BuildCitations(contextChunks);

        var systemPrompt = BuildSystemPrompt(owner, repository);
        var userPrompt = BuildUserPrompt(question, contextChunks);

        var chatRequest = new AzureChatRequest
        {
            Messages =
            [
                new AzureChatMessage { Role = "system", Content = systemPrompt },
                new AzureChatMessage { Role = "user",   Content = userPrompt   }
            ],
            MaxTokens = 1024,
            Temperature = 0.2f
        };

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
                    var answerText = await ParseAnswerAsync(response, cancellationToken);
                    logger.LogInformation(
                        "Chat answer generated for {Owner}/{Repository} ({CitationCount} citations).",
                        SanitizeLogValue(owner), SanitizeLogValue(repository), citations.Count);
                    return new ChatAnswer { Answer = answerText, Citations = citations };
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
            """;

    private static string BuildUserPrompt(string question, IReadOnlyList<VectorSearchResult> chunks)
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

    // -------------------------------------------------------------------------
    // Citation builder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Produces a deduplicated, ordered list of <see cref="FileCitation"/> objects
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
                    Path = result.Chunk.SourcePath,
                    StartLine = result.Chunk.StartLine,
                    EndLine = result.Chunk.EndLine
                });
            }
        }

        return citations;
    }

    // -------------------------------------------------------------------------
    // Response parsing
    // -------------------------------------------------------------------------

    private static async Task<string> ParseAnswerAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var parsed = await response.Content.ReadFromJsonAsync<AzureChatResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Azure OpenAI chat response was empty.");

        var content = parsed.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Azure OpenAI chat response contained no message content.");
        }

        return content.Trim();
    }

    // -------------------------------------------------------------------------
    // Retry helpers
    // -------------------------------------------------------------------------

    private static TimeSpan GetRetryDelay(int attempt)
        => TimeSpan.FromMilliseconds(250 * attempt);

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout
           || statusCode == (HttpStatusCode)429
           || (int)statusCode >= 500;

    /// <summary>
    /// Strips CR and LF characters from a log value to prevent log-forging attacks.
    /// </summary>
    private static string SanitizeLogValue(string value)
        => value.Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);

    // -------------------------------------------------------------------------
    // Private API DTOs
    // -------------------------------------------------------------------------

    private sealed class AzureChatRequest
    {
        public required IReadOnlyList<AzureChatMessage> Messages { get; init; }

        public int MaxTokens { get; init; } = 1024;

        public float Temperature { get; init; } = 0.2f;
    }

    private sealed class AzureChatMessage
    {
        public required string Role { get; init; }

        public required string Content { get; init; }
    }

    private sealed class AzureChatResponse
    {
        public List<AzureChatChoice>? Choices { get; init; }
    }

    private sealed class AzureChatChoice
    {
        public AzureChatMessageResponse? Message { get; init; }
    }

    private sealed class AzureChatMessageResponse
    {
        public string? Content { get; init; }
    }
}
