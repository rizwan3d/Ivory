using System.Text.Json;
using System.Text.RegularExpressions;
using Ivory.Application.Config;
using Ivory.Application.Php;
using Ivory.Domain.Php;

namespace Ivory.Infrastructure.Php;

public class PhpVersionResolver(IProjectConfigProvider configProvider) : IPhpVersionResolver
{
    private readonly string _configPath = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            ".ivory",
            "config.json");
    private readonly IProjectConfigProvider _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));

    public async Task<PhpVersion> ResolveForCurrentDirectoryAsync(CancellationToken cancellationToken = default)
    {
        var configResult = await _configProvider.LoadAsync(System.Environment.CurrentDirectory, cancellationToken).ConfigureAwait(false);
        if (configResult.Config?.Php?.Version is { } projectVersion &&
            !string.IsNullOrWhiteSpace(projectVersion))
        {
            return new PhpVersion(projectVersion.Trim());
        }

        if (TryResolveFromComposer(out var composerVersion))
        {
            return composerVersion;
        }

        if (File.Exists(_configPath))
        {
            var json = await File.ReadAllTextAsync(_configPath, cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("defaultPhpVersion", out var prop))
            {
                var defaultVersion = prop.GetString();
                if (!string.IsNullOrWhiteSpace(defaultVersion))
                {
                    return new PhpVersion(defaultVersion.Trim());
                }
            }
        }

        return new PhpVersion("system");
    }

    public async Task SetDefaultAsync(PhpVersion version, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);

        string json = "{\n  \"defaultPhpVersion\": \"" + version.Value + "\"\n}\n";
        await File.WriteAllTextAsync(_configPath, json, cancellationToken).ConfigureAwait(false);
    }

    private static bool TryResolveFromComposer(out PhpVersion version)
    {
        version = default;
        try
        {
            var composerPath = FindComposerJson(System.Environment.CurrentDirectory);
            if (composerPath is null)
            {
                return false;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(composerPath));
            var root = doc.RootElement;

            // Prefer config.platform.php, fall back to require.php
            if (root.TryGetProperty("config", out var configElem) &&
                configElem.ValueKind == JsonValueKind.Object &&
                configElem.TryGetProperty("platform", out var platformElem) &&
                platformElem.ValueKind == JsonValueKind.Object &&
                platformElem.TryGetProperty("php", out var platformPhp) &&
                platformPhp.ValueKind == JsonValueKind.String)
            {
                var spec = platformPhp.GetString();
                if (TryParseComposerPhpSpec(spec, out version))
                {
                    return true;
                }
            }

            if (root.TryGetProperty("require", out var requireElem) &&
                requireElem.ValueKind == JsonValueKind.Object &&
                requireElem.TryGetProperty("php", out var requirePhp) &&
                requirePhp.ValueKind == JsonValueKind.String)
            {
                var spec = requirePhp.GetString();
                if (TryParseComposerPhpSpec(spec, out version))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Composer detection is best-effort; ignore failures.
        }

        return false;
    }

    private static bool TryParseComposerPhpSpec(string? spec, out PhpVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        // Heuristic: grab first numeric version fragment (e.g., "^8.2" -> "8.2").
        var match = Regex.Match(spec, @"(?<v>\d+(\.\d+){0,2})");
        if (!match.Success)
        {
            return false;
        }

        var extracted = match.Groups["v"].Value;
        try
        {
            version = new PhpVersion(extracted);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindComposerJson(string startDirectory)
    {
        var dir = Path.GetFullPath(startDirectory);
        while (!string.IsNullOrWhiteSpace(dir))
        {
            var candidate = Path.Combine(dir, "composer.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return null;
    }
}

