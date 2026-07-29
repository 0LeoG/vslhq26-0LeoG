using System.Collections.Frozen;

namespace OnboardMe.Web.Services.RepoIngestion;

public static class RepoIngestionRules
{
    private static readonly FrozenSet<string> GeneratedDirectories = new[]
    {
        ".git",
        ".next",
        ".nuxt",
        ".yarn",
        "bin",
        "build",
        "coverage",
        "dist",
        "node_modules",
        "obj",
        "out",
        "target"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> BinaryExtensions = new[]
    {
        ".7z",
        ".avi",
        ".bmp",
        ".class",
        ".dll",
        ".dylib",
        ".exe",
        ".gif",
        ".gz",
        ".ico",
        ".jar",
        ".jpeg",
        ".jpg",
        ".lockb",
        ".mov",
        ".mp3",
        ".mp4",
        ".otf",
        ".pdf",
        ".png",
        ".so",
        ".svgz",
        ".tar",
        ".ttf",
        ".wav",
        ".webm",
        ".woff",
        ".woff2",
        ".zip"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> ExtensionToLanguage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#",
        [".csproj"] = "MSBuild",
        [".css"] = "CSS",
        [".go"] = "Go",
        [".java"] = "Java",
        [".js"] = "JavaScript",
        [".json"] = "JSON",
        [".md"] = "Markdown",
        [".py"] = "Python",
        [".razor"] = "Razor",
        [".rs"] = "Rust",
        [".sql"] = "SQL",
        [".ts"] = "TypeScript",
        [".tsx"] = "TSX",
        [".txt"] = "Text",
        [".xml"] = "XML",
        [".yml"] = "YAML",
        [".yaml"] = "YAML"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static bool IsGeneratedPath(string path)
    {
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (GeneratedDirectories.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsBinaryPath(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && BinaryExtensions.Contains(extension);
    }

    public static string DetectLanguage(string path)
    {
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "Unknown";
        }

        return ExtensionToLanguage.GetValueOrDefault(extension, "Unknown");
    }
}
