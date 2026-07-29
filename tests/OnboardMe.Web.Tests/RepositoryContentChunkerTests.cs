using OnboardMe.Web.Services.RepoIngestion;

namespace OnboardMe.Web.Tests;

public class RepositoryContentChunkerTests
{
    [Fact]
    public void ChunkFile_SplitsMarkdownByHeadings()
    {
        const string content = "# Intro\nThis is section one.\n\n## Details\nThis is section two.";

        var chunks = RepositoryContentChunker.ChunkFile("README.md", "sha-readme", content, "Markdown");

        Assert.Equal(2, chunks.Count);
        Assert.Equal("sha-readme:0", chunks[0].ChunkId);
        Assert.Equal(1, chunks[0].StartLine);
        Assert.Equal(2, chunks[0].EndLine);
        Assert.Equal("sha-readme:1", chunks[1].ChunkId);
        Assert.Equal(4, chunks[1].StartLine);
        Assert.Equal(5, chunks[1].EndLine);
    }

    [Fact]
    public void ChunkFile_SplitsLargeCodeIntoMultipleChunks()
    {
        var repeatedLine = new string('a', 100);
        var lines = Enumerable.Repeat(repeatedLine, 20);
        var content = string.Join('\n', lines);

        var chunks = RepositoryContentChunker.ChunkFile("src/App.cs", "sha-code", content, "C#");

        Assert.True(chunks.Count > 1);
        Assert.Equal("src/App.cs", chunks[0].SourcePath);
        Assert.Equal("sha-code", chunks[0].SourceSha);
        Assert.Equal(1, chunks[0].StartLine);
        Assert.Equal("sha-code:0", chunks[0].ChunkId);
        Assert.True(chunks[^1].EndLine >= chunks[0].EndLine);
    }
}
