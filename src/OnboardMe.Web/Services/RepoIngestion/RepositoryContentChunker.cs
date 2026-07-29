using System.Text;
using System.Text.RegularExpressions;

namespace OnboardMe.Web.Services.RepoIngestion;

public static class RepositoryContentChunker
{
    // Keep these knobs centralized so chunk sizing can be tuned later.
    private const int MarkdownMaxChunkChars = 1200;
    private const int MarkdownMinChunkChars = 400;
    private const int CodeMaxChunkChars = 1000;
    private const int CodeMinChunkChars = 350;
    private const int TextMaxChunkChars = 1000;
    private const int TextMinChunkChars = 350;

    private static readonly Regex MarkdownHeadingRegex = new(@"^\s{0,3}#{1,6}\s+\S", RegexOptions.Compiled);

    public static IReadOnlyList<RepositoryContentChunk> ChunkFile(string path, string sha, string content, string language)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var normalizedContent = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalizedContent.Split('\n');
        var mode = ResolveChunkingMode(path, language);

        var logicalSegments = mode switch
        {
            ChunkingMode.Markdown => BuildMarkdownSegments(lines),
            _ => [new LineRange(1, lines.Length)]
        };

        var chunks = new List<RepositoryContentChunk>();
        var chunkIndex = 0;
        var strategyName = mode switch
        {
            ChunkingMode.Markdown => "markdown-section",
            ChunkingMode.Code => "code-block",
            _ => "text-block"
        };

        foreach (var segment in logicalSegments)
        {
            foreach (var range in SplitBySize(lines, segment, mode))
            {
                var chunkText = BuildContent(lines, range.StartLine, range.EndLine);
                if (string.IsNullOrWhiteSpace(chunkText))
                {
                    continue;
                }

                chunks.Add(new RepositoryContentChunk
                {
                    ChunkId = $"{sha}:{chunkIndex}",
                    SourcePath = path,
                    SourceSha = sha,
                    ChunkIndex = chunkIndex,
                    Strategy = strategyName,
                    StartLine = range.StartLine,
                    EndLine = range.EndLine,
                    Content = chunkText
                });

                chunkIndex++;
            }
        }

        return chunks;
    }

    private static List<LineRange> BuildMarkdownSegments(string[] lines)
    {
        var segments = new List<LineRange>();
        var currentStart = 1;

        for (var index = 0; index < lines.Length; index++)
        {
            if (!IsMarkdownHeading(lines[index]))
            {
                continue;
            }

            var headingLine = index + 1;
            if (headingLine <= currentStart)
            {
                continue;
            }

            segments.Add(new LineRange(currentStart, headingLine - 1));
            currentStart = headingLine;
        }

        if (currentStart <= lines.Length)
        {
            segments.Add(new LineRange(currentStart, lines.Length));
        }

        return segments;
    }

    private static bool IsMarkdownHeading(string line)
        => MarkdownHeadingRegex.IsMatch(line);

    private static IEnumerable<LineRange> SplitBySize(string[] lines, LineRange segment, ChunkingMode mode)
    {
        var (maxChars, minChars) = mode switch
        {
            ChunkingMode.Markdown => (MarkdownMaxChunkChars, MarkdownMinChunkChars),
            ChunkingMode.Code => (CodeMaxChunkChars, CodeMinChunkChars),
            _ => (TextMaxChunkChars, TextMinChunkChars)
        };

        var currentStart = segment.StartLine;
        while (currentStart <= segment.EndLine)
        {
            var currentLength = 0;
            var lastBreakableLine = -1;
            var endLine = currentStart;

            for (var line = currentStart; line <= segment.EndLine; line++)
            {
                currentLength += lines[line - 1].Length + 1;

                if (string.IsNullOrWhiteSpace(lines[line - 1]))
                {
                    lastBreakableLine = line;
                }

                if (currentLength <= maxChars)
                {
                    endLine = line;
                    continue;
                }

                endLine = lastBreakableLine >= currentStart && currentLength >= minChars
                    ? lastBreakableLine
                    : Math.Max(currentStart, line - 1);
                break;
            }

            if (endLine < currentStart)
            {
                endLine = currentStart;
            }

            var trimmedEndLine = TrimTrailingBlankLines(lines, currentStart, endLine);
            if (trimmedEndLine >= currentStart)
            {
                yield return new LineRange(currentStart, trimmedEndLine);
            }

            currentStart = endLine + 1;
        }
    }

    private static string BuildContent(string[] lines, int startLine, int endLine)
    {
        var builder = new StringBuilder();
        for (var line = startLine; line <= endLine; line++)
        {
            builder.Append(lines[line - 1]);
            if (line < endLine)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static int TrimTrailingBlankLines(string[] lines, int startLine, int endLine)
    {
        while (endLine >= startLine && string.IsNullOrWhiteSpace(lines[endLine - 1]))
        {
            endLine--;
        }

        return endLine;
    }

    private static ChunkingMode ResolveChunkingMode(string path, string language)
    {
        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase))
        {
            return ChunkingMode.Markdown;
        }

        return string.Equals(language, "Unknown", StringComparison.OrdinalIgnoreCase)
            ? ChunkingMode.Text
            : ChunkingMode.Code;
    }

    private readonly record struct LineRange(int StartLine, int EndLine);

    private enum ChunkingMode
    {
        Markdown,
        Code,
        Text
    }
}
