namespace OnboardMe.Web.Services.RepoIngestion;

public static class StartHereGuideGenerator
{
    private static readonly string[] StopWords =
    [
        "the", "and", "for", "with", "this", "that", "need", "want",
        "have", "from", "into", "about", "where", "should", "start",
        "change", "add", "feature"
    ];

    public static IReadOnlyList<StartHereSuggestion> CreateSuggestions(
        string taskPrompt,
        IReadOnlyList<RepositoryFileIngestionRecord> files,
        int maxSuggestions = 5)
    {
        if (string.IsNullOrWhiteSpace(taskPrompt) || files.Count == 0 || maxSuggestions <= 0)
        {
            return [];
        }

        var keywords = ExtractKeywords(taskPrompt);
        var scored = files
            .Where(file => file.Status != RepositoryFileIndexStatus.Failed)
            .Select(file => ScoreFile(file.Path, keywords))
            .Where(result => result.Score > 0)
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Path, StringComparer.OrdinalIgnoreCase)
            .Take(maxSuggestions)
            .Select(result => new StartHereSuggestion
            {
                Path = result.Path,
                Reason = BuildReason(result)
            })
            .ToList();

        if (scored.Count > 0)
        {
            return scored;
        }

        return BuildFallbackSuggestions(files, maxSuggestions);
    }

    private static IReadOnlyList<StartHereSuggestion> BuildFallbackSuggestions(
        IReadOnlyList<RepositoryFileIngestionRecord> files,
        int maxSuggestions)
    {
        return files
            .Where(file => file.Status != RepositoryFileIndexStatus.Failed)
            .Select(file => file.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new
            {
                Path = path,
                Priority = GetFallbackPriority(path)
            })
            .Where(item => item.Priority > 0)
            .OrderByDescending(item => item.Priority)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Take(maxSuggestions)
            .Select(item => new StartHereSuggestion
            {
                Path = item.Path,
                Reason = BuildFallbackReason(item.Path)
            })
            .ToList();
    }

    private static IReadOnlyList<string> ExtractKeywords(string taskPrompt)
    {
        return taskPrompt
            .ToLowerInvariant()
            .Split([' ', '\t', '\r', '\n', ',', '.', ':', ';', '/', '-', '_'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .Where(token => !StopWords.Contains(token, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
    }

    private static ScoredFileResult ScoreFile(string path, IReadOnlyList<string> keywords)
    {
        var normalizedPath = path.Replace('\\', '/');
        var fileName = normalizedPath.Split('/').Last();
        var score = 0;
        var matchedKeywords = new List<string>();

        foreach (var keyword in keywords)
        {
            if (normalizedPath.Contains($"/{keyword}/", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith($"{keyword}/", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.EndsWith($"/{keyword}", StringComparison.OrdinalIgnoreCase))
            {
                score += 6;
                matchedKeywords.Add(keyword);
                continue;
            }

            if (fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 5;
                matchedKeywords.Add(keyword);
                continue;
            }

            if (normalizedPath.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
                matchedKeywords.Add(keyword);
            }
        }

        if (score == 0)
        {
            score = GetFallbackPriority(normalizedPath);
        }

        return new ScoredFileResult
        {
            Path = path,
            Score = score,
            MatchedKeywords = matchedKeywords
        };
    }

    private static int GetFallbackPriority(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var fileName = normalizedPath.Split('/').Last();

        if (fileName.StartsWith("README", StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (normalizedPath.Contains("/Services/", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Contains("/Components/", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 0;
    }

    private static string BuildReason(ScoredFileResult result)
    {
        if (result.MatchedKeywords.Count > 0)
        {
            var keywordList = string.Join(", ", result.MatchedKeywords.Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
            return $"This file path matches task keywords ({keywordList}).";
        }

        return BuildFallbackReason(result.Path);
    }

    private static string BuildFallbackReason(string path)
    {
        var normalizedPath = path.Replace('\\', '/');
        var fileName = normalizedPath.Split('/').Last();

        if (fileName.StartsWith("README", StringComparison.OrdinalIgnoreCase))
        {
            return "This usually gives the fastest high-level project overview.";
        }

        if (fileName.Equals("Program.cs", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Startup.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "This is the application startup and wiring entry point.";
        }

        if (normalizedPath.Contains("/Services/", StringComparison.OrdinalIgnoreCase))
        {
            return "This likely contains core business logic for the app.";
        }

        if (normalizedPath.Contains("/Components/", StringComparison.OrdinalIgnoreCase))
        {
            return "This likely contains user-facing UI behavior for the app.";
        }

        return "This appears to be a relevant place to start for this repo.";
    }

    private sealed class ScoredFileResult
    {
        public required string Path { get; init; }

        public required int Score { get; init; }

        public required IReadOnlyList<string> MatchedKeywords { get; init; }
    }
}
