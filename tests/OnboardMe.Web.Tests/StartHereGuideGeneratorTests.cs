using OnboardMe.Web.Services.RepoIngestion;

namespace OnboardMe.Web.Tests;

public class StartHereGuideGeneratorTests
{
    [Fact]
    public void CreateSuggestions_ReturnsKeywordMatchedFilesWithReasons()
    {
        var files = new[]
        {
            CreateFile("src/Auth/AuthService.cs"),
            CreateFile("src/Billing/BillingService.cs"),
            CreateFile("src/Program.cs"),
            CreateFile("README.md")
        };

        var suggestions = StartHereGuideGenerator.CreateSuggestions("I need to change auth flow", files, maxSuggestions: 3);

        Assert.NotEmpty(suggestions);
        Assert.Equal("src/Auth/AuthService.cs", suggestions[0].Path);
        Assert.Contains("auth", suggestions[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSuggestions_UsesFallbackWhenTaskKeywordsDoNotMatch()
    {
        var files = new[]
        {
            CreateFile("README.md"),
            CreateFile("src/Program.cs"),
            CreateFile("src/Services/RepoIngestionService.cs")
        };

        var suggestions = StartHereGuideGenerator.CreateSuggestions("totally unrelated phrase", files, maxSuggestions: 3);

        Assert.Equal(3, suggestions.Count);
        Assert.Equal("README.md", suggestions[0].Path);
        Assert.Contains("overview", suggestions[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateSuggestions_ReturnsEmptyForBlankPrompt()
    {
        var suggestions = StartHereGuideGenerator.CreateSuggestions("   ", [CreateFile("README.md")]);

        Assert.Empty(suggestions);
    }

    private static RepositoryFileIngestionRecord CreateFile(string path)
        => new()
        {
            Path = path,
            Sha = "sha",
            SizeBytes = 100,
            Extension = Path.GetExtension(path),
            Language = "C#",
            Status = RepositoryFileIndexStatus.Indexed
        };
}
