using OnboardMe.Web.Services.RepoIngestion;

namespace OnboardMe.Web.Tests;

public class RepoIngestionRulesTests
{
    [Fact]
    public void IsGeneratedPath_ReturnsTrue_ForKnownGeneratedDirectory()
    {
        var isGenerated = RepoIngestionRules.IsGeneratedPath("src/app/node_modules/package/index.js");

        Assert.True(isGenerated);
    }

    [Fact]
    public void IsBinaryPath_ReturnsTrue_ForBinaryExtensions()
    {
        var isBinary = RepoIngestionRules.IsBinaryPath("assets/logo.png");

        Assert.True(isBinary);
    }

    [Fact]
    public void DetectLanguage_ReturnsMappedLanguage()
    {
        var language = RepoIngestionRules.DetectLanguage("Components/Pages/RepoSetup.razor");

        Assert.Equal("Razor", language);
    }
}
