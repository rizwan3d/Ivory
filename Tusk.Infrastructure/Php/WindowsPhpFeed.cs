using System.Text.RegularExpressions;
using System.Text.Json;

namespace Tusk.Infrastructure.Php;

public sealed class WindowsPhpFeed
{
    private static readonly Uri ReleasesUri = new("https://windows.php.net/downloads/releases/releases.json");
    private static readonly Uri ArchivesUri = new("https://windows.php.net/downloads/releases/archives/");
    private const string DownloadBase = "https://windows.php.net/downloads/releases/";
    private const string ArchivesBase = "https://windows.php.net/downloads/releases/archives/";
    private const string UserAgent = "tusk-cli/1.0 (+https://github.com/)";

    private readonly HttpClient _httpClient;
    private Dictionary<string, FeedEntry>? _cache;
    private readonly object _lock = new();

    public WindowsPhpFeed(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public async Task<IReadOnlyList<(string Version, string File, string Sha)>> ListAsync(CancellationToken ct = default)
    {
        await EnsureCacheAsync(ct).ConfigureAwait(false);
        return _cache!
            .Select(kvp => (kvp.Key, kvp.Value.File, kvp.Value.Sha))
            .OrderByDescending(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<(bool Success, string ResolvedVersion, PhpArtifact Artifact)> ResolveAsync(string spec, CancellationToken ct = default)
    {
        await EnsureCacheAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(spec) || _cache is null || _cache.Count == 0)
        {
            return (false, string.Empty, new PhpArtifact());
        }

        if (_cache.TryGetValue(spec, out var exact))
        {
            return (true, spec, new PhpArtifact { Url = exact.BaseUrl + exact.File, Sha256 = exact.Sha });
        }

        var candidates = _cache.Keys
            .Where(v => v.StartsWith(spec + ".", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count > 0 && _cache.TryGetValue(candidates[0], out var best))
        {
            return (true, candidates[0], new PhpArtifact { Url = best.BaseUrl + best.File, Sha256 = best.Sha });
        }

        return (false, string.Empty, new PhpArtifact());
    }

    private async Task EnsureCacheAsync(CancellationToken ct)
    {
        if (_cache is not null) return;

        var dict = await TryLoadFromReleasesAsync(ct).ConfigureAwait(false)
                   ?? new Dictionary<string, FeedEntry>(StringComparer.OrdinalIgnoreCase);
        var archives = await LoadFromArchivesAsync(ct).ConfigureAwait(false);

        foreach (var archive in archives)
        {
            if (!dict.ContainsKey(archive.Key))
            {
                dict[archive.Key] = archive.Value;
            }
        }

        if (dict.Count == 0)
        {
            throw new InvalidOperationException("Failed to load PHP releases feed.");
        }     

        lock (_lock)
        {
            _cache ??= dict;
        }
    }

    private async Task<Dictionary<string, FeedEntry>?> TryLoadFromReleasesAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUri);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            var dict = new Dictionary<string, FeedEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var release in doc.RootElement.EnumerateObject())
            {
                if (!release.Value.TryGetProperty("version", out var versionProp))
                {
                    continue;
                }

                var fullVersion = versionProp.GetString();
                if (string.IsNullOrWhiteSpace(fullVersion))
                {
                    continue;
                }

                var artifact = SelectPreferredArtifact(release.Value);
                if (artifact is null)
                {
                    continue;
                }

                dict[fullVersion] = artifact.Value with { BaseUrl = DownloadBase };
            }

            return dict;
        }
        catch
        {
            return null;
        }
    }

    private static FeedEntry? SelectPreferredArtifact(JsonElement releaseElement)
    {
        // Prefer NTS x64 builds with the newest toolset identifiers.
        string[] preferredKeys =
        [
            "nts-vs17-x64",
            "nts-vs16-x64",
            "nts-vc15-x64",
            "nts-vc14-x64",
            "nts-vs15-x64",
            "nts-vs14-x64"
        ];

        foreach (var key in preferredKeys)
        {
            if (releaseElement.TryGetProperty(key, out var buildElement))
            {
                var artifact = ReadArtifact(buildElement);
                if (artifact is not null)
                {
                    return artifact;
                }
            }
        }

        // Fallback: any NTS x64 build present.
        foreach (var build in releaseElement.EnumerateObject()
                     .Where(p => p.Name.Contains("nts", StringComparison.OrdinalIgnoreCase) &&
                                 p.Name.Contains("x64", StringComparison.OrdinalIgnoreCase)))
        {
            var artifact = ReadArtifact(build.Value);
            if (artifact is not null)
            {
                return artifact;
            }
        }

        return null;
    }

    private static FeedEntry? ReadArtifact(JsonElement buildElement)
    {
        if (!buildElement.TryGetProperty("zip", out var zipElement))
        {
            return null;
        }

        if (!zipElement.TryGetProperty("path", out var pathProp))
        {
            return null;
        }

        var path = pathProp.GetString();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string sha = string.Empty;
        if (zipElement.TryGetProperty("sha256", out var shaProp))
        {
            sha = shaProp.GetString() ?? string.Empty;
        }

        return new FeedEntry(path, sha, DownloadBase);
    }

    private async Task<Dictionary<string, FeedEntry>> LoadFromArchivesAsync(CancellationToken ct)
    {
        var dict = new Dictionary<string, FeedEntry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ArchivesUri);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var regex = new Regex(@"href=\""/downloads/releases/archives/php-(?<ver>[\d\.]+)-nts-Win32-(?<vs>VC\d+|vs\d+)-x64\.zip\""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            var matches = regex.Matches(html);
            foreach (Match match in matches)
            {
                var ver = match.Groups["ver"].Value;
                var toolset = match.Groups["vs"].Value;
                if (string.IsNullOrWhiteSpace(ver))
                {
                    continue;
                }

                var file = $"php-{ver}-nts-Win32-{toolset}-x64.zip";
                dict[ver] = new FeedEntry(file, string.Empty, ArchivesBase);
            }
        }
        catch
        {
            // Ignore archive errors; they are a fallback source.
        }

        return dict;
    }

    private readonly record struct FeedEntry(string File, string Sha, string BaseUrl);
}
