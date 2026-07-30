using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace OnboardMe.Web.Services.RepoIngestion;

/// <summary>
/// Enriches a deterministic repository overview with an AI narrative.
/// Falls back safely to deterministic output when AI is unavailable.
/// </summary>
public sealed class AzureOpenAiRepositoryOverviewService(
    IHttpClientFactory httpClientFactory,
    IOptions<AzureOpenAiEmbeddingsOptions> optionsAccessor,
    ILogger<AzureOpenAiRepositoryOverviewService> logger) : IRepositoryOverviewAiService
{
    private const int MaxAttempts = 3;
    private const int MaxContextFiles = 8;
    private const int MaxSnippetCharacters = 1200;

    public async Task<RepositoryOverviewAiSummary> GenerateAsync(
        RepositoryIndexingStatus status,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        var deterministicOverview = RepositoryOverviewGenerator.Create(status);
        var fallback = BuildDeterministicFallback(deterministicOverview, status, null);

        var options = optionsAccessor.Value;
        if (!options.IsChatConfigured)
        {
            logger.LogWarning("Azure OpenAI chat is not configured; returning deterministic repository overview summary.");
            return WithFallbackReason(fallback, "Azure OpenAI chat is not configured.");
        }

        var snippets = BuildContextSnippets(status);
        if (snippets.Count == 0)
        {
            return WithFallbackReason(fallback, "No indexed source snippets were available for AI enrichment.");
        }

        var chatRequest = new AzureChatRequest
        {
            Messages =
            [
                new AzureChatMessage
                {
                    Role = "system",
                    Content =
                        "You are a repository onboarding analyst. Respond with strict JSON only and no markdown code fences. " +
                        "Use only provided repository facts and snippets. If uncertain, state uncertainty explicitly in risksAndUnknowns."
                },
                new AzureChatMessage
                {
                    Role = "user",
                    Content = BuildUserPrompt(status, deterministicOverview, snippets)
                }
            ],
            MaxTokens = 900,
            Temperature = 0.2f
        };

        try
        {
            var raw = await SendChatRequestAsync(chatRequest, cancellationToken);
            var parsed = TryParseResponse(raw);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.ArchitectureSummary))
            {
                logger.LogWarning("Repository overview AI response was not valid JSON. Falling back to deterministic summary.");
                return WithFallbackReason(fallback, "AI response was not valid structured JSON.");
            }

            var entryPoints = parsed.EntryPoints
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Path) && !string.IsNullOrWhiteSpace(entry.WhyItMatters))
                .Select(entry => new RepositoryOverviewAiEntryPoint
                {
                    Path = entry.Path!.Trim(),
                    WhyItMatters = entry.WhyItMatters!.Trim()
                })
                .DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();

            var sourceFiles = snippets
                .Select(snippet => snippet.Path)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var narrative = parsed.ArchitectureSummary.Trim();
            var workflows = NormalizeList(parsed.MainWorkflows);
            var risks = NormalizeList(parsed.RisksAndUnknowns);

            return new RepositoryOverviewAiSummary
            {
                Narrative = narrative,
                MainWorkflows = workflows,
                RisksAndUnknowns = risks,
                EntryPoints = entryPoints,
                SourceFiles = sourceFiles,
                IsAiGenerated = true,
                FallbackReason = null
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AI repository overview enrichment failed for {Owner}/{Repository}; using deterministic fallback.", status.Owner, status.Repository);
            return WithFallbackReason(fallback, "AI enrichment request failed and deterministic fallback was used.");
        }
    }

    private static RepositoryOverviewAiSummary WithFallbackReason(RepositoryOverviewAiSummary baseSummary, string fallbackReason)
    {
        return new RepositoryOverviewAiSummary
        {
            Narrative = baseSummary.Narrative,
            MainWorkflows = baseSummary.MainWorkflows,
            RisksAndUnknowns = baseSummary.RisksAndUnknowns,
            EntryPoints = baseSummary.EntryPoints,
            SourceFiles = baseSummary.SourceFiles,
            IsAiGenerated = baseSummary.IsAiGenerated,
            FallbackReason = fallbackReason
        };
    }

    private static RepositoryOverviewAiSummary BuildDeterministicFallback(
        RepositoryOverviewSnapshot overview,
        RepositoryIndexingStatus status,
        string? fallbackReason)
    {
        var entryPoints = overview.NotableFiles
            .Take(5)
            .Select(file => new RepositoryOverviewAiEntryPoint
            {
                Path = file.Path,
                WhyItMatters = $"Detected as {file.Category.ToLowerInvariant()}."
            })
            .ToList();

        var risks = new List<string>();
        if (status.FailedCount > 0)
        {
            risks.Add($"{status.FailedCount} files failed during indexing, so coverage may be incomplete.");
        }

        if (status.SkippedCount > 0)
        {
            risks.Add($"{status.SkippedCount} files were skipped (binary, generated, or oversized)." );
        }

        if (status.State is not (RepositoryIndexingState.Completed or RepositoryIndexingState.CompletedWithErrors))
        {
            risks.Add("Indexing is not in a completed state; this snapshot may change.");
        }

        if (risks.Count == 0)
        {
            risks.Add("No major ingestion risks were detected in the latest snapshot.");
        }

        return new RepositoryOverviewAiSummary
        {
            Narrative = overview.Summary,
            MainWorkflows = [],
            RisksAndUnknowns = risks,
            EntryPoints = entryPoints,
            SourceFiles = entryPoints.Select(entry => entry.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            IsAiGenerated = false,
            FallbackReason = fallbackReason
        };
    }

    private async Task<string> SendChatRequestAsync(
        AzureChatRequest chatRequest,
        CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;
        var endpoint = options.Endpoint!.TrimEnd('/');
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
                    .CreateClient(AzureOpenAiChatService.AzureOpenAiChatClientName)
                    .SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    return await ParseResponseContentAsync(response, cancellationToken);
                }

                lastStatusCode = response.StatusCode;
                lastDetails = await response.Content.ReadAsStringAsync(cancellationToken);
                if (IsTransientStatusCode(response.StatusCode) && attempt < MaxAttempts)
                {
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
                    await Task.Delay(GetRetryDelay(attempt), cancellationToken);
                    continue;
                }
            }
        }

        if (lastException is not null)
        {
            throw new InvalidOperationException("Azure OpenAI request failed after retries.", lastException);
        }

        throw new InvalidOperationException($"Azure OpenAI request failed with {(int?)lastStatusCode} {lastStatusCode}: {lastDetails}");
    }

    private static RepositoryOverviewAiResponse? TryParseResponse(string raw)
    {
        foreach (var candidate in EnumerateJsonCandidates(raw))
        {
            var parsed = TryDeserializeResponse(candidate);
            if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.ArchitectureSummary))
            {
                return parsed;
            }
        }

        return null;
    }

    private static RepositoryOverviewAiResponse? TryDeserializeResponse(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var trimmed = candidate.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{' || trimmed[^1] != '}')
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RepositoryOverviewAiResponse>(
                trimmed,
                s_parseOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateJsonCandidates(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var normalized = raw.Trim();

        if (TryAddCandidate(normalized, seen, out var candidate))
        {
            yield return candidate;
        }

        var deFenced = StripMarkdownFence(normalized);
        if (TryAddCandidate(deFenced, seen, out candidate))
        {
            yield return candidate;
        }

        if (TryExtractFirstJsonObject(normalized, out var extracted)
            && TryAddCandidate(extracted, seen, out candidate))
        {
            yield return candidate;
        }

        if (TryExtractFirstJsonObject(deFenced, out extracted)
            && TryAddCandidate(extracted, seen, out candidate))
        {
            yield return candidate;
        }

        if (TryDeserializeJsonString(normalized, out var unescaped)
            && TryAddCandidate(unescaped, seen, out candidate))
        {
            yield return candidate;
        }

        if (TryDeserializeJsonString(normalized, out unescaped)
            && TryExtractFirstJsonObject(unescaped, out extracted)
            && TryAddCandidate(extracted, seen, out candidate))
        {
            yield return candidate;
        }
    }

    private static string StripMarkdownFence(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var lines = trimmed.Split('\n');
        if (lines.Length < 3)
        {
            return trimmed;
        }

        var firstLine = lines[0].Trim();
        var lastLine = lines[^1].Trim();
        if (!firstLine.StartsWith("```", StringComparison.Ordinal)
            || !string.Equals(lastLine, "```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return string.Join('\n', lines.Skip(1).Take(lines.Length - 2)).Trim();
    }

    private static bool TryDeserializeJsonString(string content, out string value)
    {
        value = string.Empty;
        var trimmed = content.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '"' || trimmed[^1] != '"')
        {
            return false;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<string>(trimmed);
            if (string.IsNullOrWhiteSpace(deserialized))
            {
                return false;
            }

            value = deserialized.Trim();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryExtractFirstJsonObject(string content, out string json)
    {
        json = string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var start = content.IndexOf('{');
        if (start < 0)
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < content.Length; i++)
        {
            var ch = content[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    json = content[start..(i + 1)].Trim();
                    return json.Length >= 2;
                }
            }
        }

        return false;
    }

    private static bool TryAddCandidate(string candidate, ISet<string> seen, out string normalized)
    {
        normalized = candidate.Trim();
        if (normalized.Length == 0)
        {
            return false;
        }

        if (!seen.Add(normalized))
        {
            return false;
        }

        return true;
    }

    private static readonly JsonSerializerOptions s_parseOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static IReadOnlyList<string> NormalizeList(List<string>? values)
    {
        return (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    private static string BuildUserPrompt(
        RepositoryIndexingStatus status,
        RepositoryOverviewSnapshot overview,
        IReadOnlyList<SnippetContext> snippets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You will produce JSON with this exact shape:");
        sb.AppendLine("{");
        sb.AppendLine("  \"architectureSummary\": string,");
        sb.AppendLine("  \"mainWorkflows\": string[],");
        sb.AppendLine("  \"entryPoints\": [{ \"path\": string, \"whyItMatters\": string }],");
        sb.AppendLine("  \"risksAndUnknowns\": string[]");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("Facts:");
        sb.AppendLine($"- Repo: {status.Owner}/{status.Repository}");
        sb.AppendLine($"- State: {status.State}");
        sb.AppendLine($"- Tracked files: {overview.TrackedFileCount}");
        sb.AppendLine($"- Indexed: {overview.IndexedFileCount}, Skipped: {overview.SkippedFileCount}, Failed: {overview.FailedFileCount}");
        sb.AppendLine($"- Processed chunk count: {overview.ProcessedChunkCount}");
        sb.AppendLine();
        sb.AppendLine("Representative snippets:");

        for (var i = 0; i < snippets.Count; i++)
        {
            var snippet = snippets[i];
            sb.AppendLine($"[{i + 1}] {snippet.Path}");
            sb.AppendLine("```");
            sb.AppendLine(snippet.Content);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("Important constraints:");
        sb.AppendLine("- Use only the facts and snippets above.");
        sb.AppendLine("- Keep architectureSummary under 110 words.");
        sb.AppendLine("- Return JSON only.");
        return sb.ToString();
    }

    private static IReadOnlyList<SnippetContext> BuildContextSnippets(RepositoryIndexingStatus status)
    {
        var files = status.Files
            .Where(file => file.Status == RepositoryFileIndexStatus.Indexed)
            .Where(file => !string.IsNullOrWhiteSpace(file.Content))
            .Select(file => new
            {
                file.Path,
                file.Content,
                Priority = GetSnippetPriority(file.Path)
            })
            .OrderByDescending(file => file.Priority)
            .ThenBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxContextFiles)
            .Select(file => new SnippetContext
            {
                Path = file.Path,
                Content = TruncateSnippet(file.Content!, MaxSnippetCharacters)
            })
            .ToList();

        return files;
    }

    private static int GetSnippetPriority(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var fileName = normalizedPath.Split('/').Last();

        if (fileName.StartsWith("README", StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        if (fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("package.json", StringComparison.OrdinalIgnoreCase))
        {
            return 9;
        }

        if (normalizedPath.Contains("/services/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/components/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/controllers/", StringComparison.OrdinalIgnoreCase))
        {
            return 8;
        }

        if (normalizedPath.Contains("/docs/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith("docs/", StringComparison.OrdinalIgnoreCase))
        {
            return 7;
        }

        return 1;
    }

    private static string TruncateSnippet(string content, int maxChars)
    {
        if (content.Length <= maxChars)
        {
            return content;
        }

        return content[..maxChars] + "\n// ...truncated for prompt budget";
    }

    private static async Task<string> ParseResponseContentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var parsed = await response.Content.ReadFromJsonAsync<AzureChatResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Azure OpenAI chat response was empty.");

        var content = parsed.Choices.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Azure OpenAI chat response did not include content.");
        }

        return content;
    }

    private static TimeSpan GetRetryDelay(int attempt) => TimeSpan.FromMilliseconds(250 * attempt);

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout
           || statusCode == (HttpStatusCode)429
           || (int)statusCode >= 500;

    private sealed class SnippetContext
    {
        public required string Path { get; init; }

        public required string Content { get; init; }
    }

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
        public AzureChatResponseMessage? Message { get; init; }
    }

    private sealed class AzureChatResponseMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }

    private sealed class RepositoryOverviewAiResponse
    {
        public string? ArchitectureSummary { get; init; }

        public List<string>? MainWorkflows { get; init; }

        public List<RepositoryOverviewAiResponseEntryPoint> EntryPoints { get; init; } = [];

        public List<string>? RisksAndUnknowns { get; init; }
    }

    private sealed class RepositoryOverviewAiResponseEntryPoint
    {
        public string? Path { get; init; }

        public string? WhyItMatters { get; init; }
    }
}
