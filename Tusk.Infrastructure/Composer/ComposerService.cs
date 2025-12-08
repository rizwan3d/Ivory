using System.Diagnostics;
using System.Text.Json;
using Tusk.Application.Composer;
using Tusk.Application.Php;
using Tusk.Domain.Config;

namespace Tusk.Infrastructure.Composer;

public class ComposerService(IPhpRuntimeService runtime) : IComposerService
{
    private const string _composerUrl = "https://github.com/composer/composer/releases/latest/download/composer.phar";
    private readonly IPhpRuntimeService _runtime = runtime;

    public string? FindComposerConfig(string? configRoot)
    {
        var searchDirs = new List<string>();

        if (!string.IsNullOrEmpty(configRoot))
            searchDirs.Add(configRoot);

        searchDirs.Add(System.Environment.CurrentDirectory);

        foreach (var dir in searchDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var tuskJson = Path.Combine(dir, "tusk.json");
            if (File.Exists(tuskJson))
                return tuskJson;

            var composerJson = Path.Combine(dir, "composer.json");
            if (File.Exists(composerJson))
                return composerJson;
        }

        return null;
    }

    public async Task<string?> EnsureComposerPharAsync(CancellationToken cancellationToken = default)
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        string tuskDir = Path.Combine(home, ".tusk");
        Directory.CreateDirectory(tuskDir);

        string targetPath = Path.Combine(tuskDir, "composer.phar");

        if (File.Exists(targetPath))
            return targetPath;

        Console.WriteLine($"[tusk] Downloading composer.phar from {_composerUrl} ...");

        try
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(_composerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var fs = File.Create(targetPath);
            var contentLength = response.Content.Headers.ContentLength ?? -1;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await CopyWithProgressAsync(stream, fs, contentLength, cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"[tusk] Saved composer.phar to {targetPath}");
            return targetPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[tusk] Failed to download composer.phar: {ex.Message}");
            return null;
        }
    }

    public string? FindComposerPhar(string? configRoot)
    {
        var candidates = new List<string>();

        var env = System.Environment.GetEnvironmentVariable("COMPOSER_PHAR");
        if (!string.IsNullOrWhiteSpace(env))
            candidates.Add(env);

        if (configRoot is not null)
            candidates.Add(Path.Combine(configRoot, "composer.phar"));

        candidates.Add(Path.Combine(System.Environment.CurrentDirectory, "composer.phar"));

        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        candidates.Add(Path.Combine(home, ".tusk", "composer.phar"));
        candidates.Add(Path.Combine(home, ".tusk", "tools", "composer", "composer.phar"));

        foreach (var path in candidates)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        return null;
    }

    public async Task<int> RunComposerAsync(
        string[] args,
        string phpVersionSpec,
        TuskConfig? config,
        string? configRoot,
        CancellationToken cancellationToken = default)
    {
        string? composerPhar = FindComposerPhar(configRoot);

        if (composerPhar is null)
        {
            composerPhar = await EnsureComposerPharAsync(cancellationToken).ConfigureAwait(false);
            if (composerPhar is null)
            {
                return 1;
            }
        }

        var finalArgs = new List<string>();

        if (config is not null)
        {
            foreach (var ini in config.Php.Ini)
            {
                finalArgs.Add("-d");
                finalArgs.Add(ini);
            }

            finalArgs.AddRange(config.Php.Args);
        }

        finalArgs.Add(composerPhar);
        finalArgs.AddRange(args);

        var composerConfigPath = FindComposerConfig(configRoot);
        Dictionary<string, string?>? env = new(StringComparer.OrdinalIgnoreCase);
        if (composerConfigPath is not null)
        {
            env.Add("COMPOSER", composerConfigPath);
            env.Add("TUSK_COMPOSER_PHAR", composerPhar);
            Console.WriteLine($"[tusk] Using COMPOSER={composerConfigPath}");
        }
        else
        {
            env.Add("TUSK_COMPOSER_PHAR", composerPhar);
        }

        return await _runtime.RunPhpAsync(
            scriptOrCommand: null,
            args: [.. finalArgs],
            overrideVersionSpec: phpVersionSpec,
            environment: env,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> RunComposerScriptAsync(
        string scriptName,
        string[] extraArgs,
        string phpVersionSpec,
        TuskConfig? config,
        string? configRoot,
        CancellationToken cancellationToken = default)
    {
        var composerConfigPath = FindComposerConfig(configRoot);
        if (composerConfigPath is null)
        {
            Console.Error.WriteLine("[tusk] No tusk.json or composer.json found to read Composer scripts.");
            return 1;
        }

        string? composerPhar = FindComposerPhar(configRoot);
        if (composerPhar is null)
        {
            composerPhar = await EnsureComposerPharAsync(cancellationToken).ConfigureAwait(false);
            if (composerPhar is null)
            {
                Console.Error.WriteLine("[tusk] Unable to locate or download composer.phar.");
                return 1;
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(composerConfigPath));
            var root = doc.RootElement;

            if (!root.TryGetProperty("scripts", out var scriptsElem) ||
                scriptsElem.ValueKind != JsonValueKind.Object ||
                !scriptsElem.EnumerateObject()
                            .Any(p => string.Equals(p.Name, scriptName, StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine($"[tusk] Composer script '{scriptName}' not found in {composerConfigPath}.");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[tusk] Failed to read Composer scripts from {composerConfigPath}: {ex.Message}");
            return 1;
        }

        var finalArgs = new List<string>();

        if (config is not null)
        {
            foreach (var ini in config.Php.Ini)
            {
                finalArgs.Add("-d");
                finalArgs.Add(ini);
            }

            finalArgs.AddRange(config.Php.Args);
        }

        finalArgs.Add(composerPhar);
        finalArgs.Add("run-script");
        finalArgs.Add(scriptName);

        if (extraArgs.Length > 0)
        {
            finalArgs.Add("--");
            finalArgs.AddRange(extraArgs);
        }

        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["COMPOSER"] = composerConfigPath
        };

        Console.WriteLine($"[tusk] Running Composer script '{scriptName}' (php={phpVersionSpec})");
        Console.WriteLine($"[tusk]   COMPOSER={composerConfigPath}");

        return await _runtime.RunPhpAsync(
            scriptOrCommand: null,
            args: [.. finalArgs],
            overrideVersionSpec: phpVersionSpec,
            environment: env,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyWithProgressAsync(Stream source, Stream destination, long contentLength, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long totalRead = 0;
        int lastPercent = -1;
        var spinner = new[] { '|', '/', '-', '\\' };
        int spinIndex = 0;
        const int barWidth = 28;

        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            totalRead += read;

            if (contentLength > 0)
            {
                int percent = (int)(totalRead * 100 / contentLength);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    int filled = (int)Math.Min(barWidth, Math.Max(0, percent * barWidth / 100));
                    string bar = new string('#', filled).PadRight(barWidth, '.');
                    Console.Write($"\r[tusk] [{bar}] {percent,3}% ({FormatBytes(totalRead)}/{FormatBytes(contentLength)})");
                }
            }
            else
            {
                char frame = spinner[spinIndex++ % spinner.Length];
                Console.Write($"\r[tusk] [{frame}] {FormatBytes(totalRead)} downloaded");
            }
        }

        var suffix = contentLength > 0
            ? $"\r[tusk] [{new string('#', barWidth)}] 100% ({FormatBytes(totalRead)}/{FormatBytes(contentLength)})"
            : $"\r[tusk] [done] {FormatBytes(totalRead)} downloaded";
        Console.WriteLine(suffix);
    }

    private static string FormatBytes(long value)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        const long GB = MB * 1024;

        return value switch
        {
            >= GB => $"{value / (double)GB:0.0} GB",
            >= MB => $"{value / (double)MB:0.0} MB",
            >= KB => $"{value / (double)KB:0.0} KB",
            _ => $"{value} B"
        };
    }
}
