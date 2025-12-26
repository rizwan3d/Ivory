using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Ivory.Cli.Exceptions;

namespace Ivory.Cli.Deploy;

internal static class DeployPackager
{
    private static readonly string[] DefaultIgnores =
    {
        ".git/",
        ".vs/",
        "bin/",
        "obj/",
        "artifacts/",
        "*.user",
        "*.swp",
        "*.log",
        "*vendor/",
    };

    public static async Task<string> CreateArchiveAsync(string sourceDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new IvoryCliException($"Source directory '{sourceDirectory}' does not exist.");
        }

        var ignore = GitIgnoreFilter.Load(sourceDirectory, DefaultIgnores);
        var archivePath = Path.Combine(Path.GetTempPath(), $"ivory-deploy-{Guid.NewGuid():N}.zip");

        var archive = await ZipFile.OpenAsync(archivePath, ZipArchiveMode.Create, cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var file in EnumerateFiles(sourceDirectory, ignore))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(sourceDirectory, file);
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var entryName = relativePath.Replace("\\", "/");
                await archive.CreateEntryFromFileAsync(file, entryName, CompressionLevel.Optimal, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await archive.DisposeAsync().ConfigureAwait(false);
        }

        return archivePath;
    }

    private static IEnumerable<string> EnumerateFiles(string root, GitIgnoreFilter filter)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var dir in Directory.EnumerateDirectories(current))
            {
                var relDir = Path.GetRelativePath(root, dir);
                var ignoreDir = filter.ShouldIgnoreDirectory(relDir);
                var allowBecauseNegatedChild = filter.HasNegatedChildRule(relDir);
                if (ignoreDir && !allowBecauseNegatedChild)
                {
                    continue;
                }

                pending.Push(dir);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                var relFile = Path.GetRelativePath(root, file);
                if (filter.ShouldIgnoreFile(relFile))
                {
                    continue;
                }

                yield return file;
            }
        }
    }
}

internal sealed class GitIgnoreFilter
{
    private readonly List<GitIgnoreRule> _rules;

    private GitIgnoreFilter(List<GitIgnoreRule> rules)
    {
        _rules = rules;
    }

    public static GitIgnoreFilter Load(string rootDirectory, IEnumerable<string> defaultPatterns)
    {
        var rules = new List<GitIgnoreRule>();
        var gitignorePath = Path.Combine(rootDirectory, ".gitignore");

        if (File.Exists(gitignorePath))
        {
            foreach (var line in File.ReadAllLines(gitignorePath))
            {
                if (GitIgnoreRule.TryParse(line, out var rule) && rule is not null)
                {
                    rules.Add(rule);
                }
            }
        }

        foreach (var pattern in defaultPatterns)
        {
            if (GitIgnoreRule.TryParse(pattern, out var rule) && rule is not null)
            {
                rules.Add(rule);
            }
        }

        return new GitIgnoreFilter(rules);
    }

    public bool ShouldIgnoreFile(string relativePath) => ShouldIgnore(relativePath, isDirectory: false);

    public bool ShouldIgnoreDirectory(string relativePath) => ShouldIgnore(relativePath, isDirectory: true);

    public bool HasNegatedChildRule(string relativePath)
    {
        if (!_rules.Any(r => r.IsNegated))
        {
            return false;
        }

        var normalized = Normalize(relativePath);
        var prefix = string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : normalized.EndsWith('/') ? normalized : $"{normalized}/";

        foreach (var rule in _rules.Where(r => r.IsNegated))
        {
            if (rule.HasWildcard)
            {
                return true;
            }

            if (prefix.Length > 0 && rule.PatternText.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldIgnore(string relativePath, bool isDirectory)
    {
        var normalized = Normalize(relativePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        var ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.IsMatch(normalized, isDirectory))
            {
                ignored = !rule.IsNegated;
            }
        }

        return ignored;
    }

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }
}

internal sealed class GitIgnoreRule
{
    private readonly Regex _regex;

    private GitIgnoreRule(Regex regex, string patternText, bool hasWildcard, bool isNegated)
    {
        _regex = regex;
        PatternText = patternText;
        HasWildcard = hasWildcard;
        IsNegated = isNegated;
    }

    public bool IsNegated { get; }
    public string PatternText { get; }
    public bool HasWildcard { get; }

    public static bool TryParse(string line, out GitIgnoreRule? rule)
    {
        rule = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
        {
            return false;
        }

        var isNegated = trimmed.StartsWith('!');
        if (isNegated)
        {
            trimmed = trimmed[1..];
        }

        var anchored = trimmed.StartsWith('/');
        if (anchored)
        {
            trimmed = trimmed[1..];
        }

        var directoryOnly = trimmed.EndsWith('/');
        if (directoryOnly)
        {
            trimmed = trimmed[..^1];
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var normalizedPattern = trimmed.Replace("\\", "/");
        var trackPattern = directoryOnly ? $"{normalizedPattern}/" : normalizedPattern;
        var hasWildcard = normalizedPattern.Contains('*') || normalizedPattern.Contains('?');

        var regex = BuildRegex(normalizedPattern, anchored, directoryOnly);
        rule = new GitIgnoreRule(regex, trackPattern, hasWildcard, isNegated);
        return true;
    }

    public bool IsMatch(string normalizedPath, bool isDirectory)
    {
        var candidate = normalizedPath.Replace("\\", "/").TrimEnd('/');

        if (_regex.IsMatch(candidate))
        {
            return true;
        }

        if (isDirectory && _regex.IsMatch($"{candidate}/"))
        {
            return true;
        }

        return false;
    }

    private static Regex BuildRegex(string pattern, bool anchored, bool directoryOnly)
    {
        var builder = new StringBuilder();
        builder.Append(anchored ? "^" : "(?:^|.*/)");

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    var isDouble = i + 1 < pattern.Length && pattern[i + 1] == '*';
                    builder.Append(isDouble ? ".*" : "[^/]*");
                    if (isDouble)
                    {
                        i++;
                    }
                    break;
                case '?':
                    builder.Append("[^/]");
                    break;
                case '.':
                    builder.Append("\\.");
                    break;
                case '/':
                    builder.Append('/');
                    break;
                default:
                    if ("+()^$.{}!|[]\\".Contains(c))
                    {
                        builder.Append('\\');
                    }
                    builder.Append(c);
                    break;
            }
        }

        if (directoryOnly)
        {
            builder.Append("(/.*)?");
        }

        builder.Append('$');

        return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
