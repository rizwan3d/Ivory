using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http;
using Ivory.Application.Composer;
using Ivory.Application.Php;
using Ivory.Domain.Config;
using Ivory.Infrastructure.Http;

namespace Ivory.Infrastructure.Composer;

public class ComposerService(IPhpRuntimeService runtime, IHttpClientFactory httpClientFactory) : IComposerService
{
    private const string ComposerUrl = "https://github.com/composer/composer/releases/latest/download/composer.phar";
    private readonly IPhpRuntimeService _runtime = runtime;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public string? FindComposerConfig(string? configRoot)
    {
        var dir = Path.GetFullPath(string.IsNullOrWhiteSpace(configRoot)
            ? System.Environment.CurrentDirectory
            : configRoot);

        while (!string.IsNullOrWhiteSpace(dir))
        {
            var ivoryJson = Path.Combine(dir, "ivory.json");
            if (File.Exists(ivoryJson))
                return ivoryJson;

            var composerJson = Path.Combine(dir, "composer.json");
            if (File.Exists(composerJson))
                return composerJson;

            var parent = Directory.GetParent(dir);
            if (parent is null)
                break;

            dir = parent.FullName;
        }

        return null;
    }

    public async Task<string?> EnsureComposerPharAsync(CancellationToken cancellationToken = default)
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        string ivoryDir = Path.Combine(home, ".ivory");
        Directory.CreateDirectory(ivoryDir);

        string targetPath = Path.Combine(ivoryDir, "composer.phar");

        if (File.Exists(targetPath))
            return targetPath;

        Console.WriteLine($"[ivory] Downloading composer.phar from {ComposerUrl} ...");

        try
        {
            using var client = _httpClientFactory.CreateClient(HttpClientNames.Default);
            using var response = await client.GetAsync(ComposerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var fs = File.Create(targetPath);
            var contentLength = response.Content.Headers.ContentLength ?? -1;
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await CopyWithProgressAsync(stream, fs, contentLength, cancellationToken).ConfigureAwait(false);

            Console.WriteLine($"[ivory] Saved composer.phar to {targetPath}");
            return targetPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ivory] Failed to download composer.phar: {ex.Message}");
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
        candidates.Add(Path.Combine(home, ".ivory", "composer.phar"));
        candidates.Add(Path.Combine(home, ".ivory", "tools", "composer", "composer.phar"));

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
        IvoryConfig? config,
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
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["IVORY_COMPOSER_PHAR"] = composerPhar,
            ["IVORY_ENABLE_DEFAULT_EXTENSIONS"] = "1"
        };

        if (composerConfigPath is not null)
        {
            env["COMPOSER"] = composerConfigPath;
            Console.WriteLine($"[ivory] Using COMPOSER={composerConfigPath}");
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
        IvoryConfig? config,
        string? configRoot,
        CancellationToken cancellationToken = default)
    {
        var composerConfigPath = FindComposerConfig(configRoot);
        if (composerConfigPath is null)
        {
            Console.Error.WriteLine("[ivory] No ivory.json or composer.json found to read Composer scripts.");
            return 1;
        }

        string? composerPhar = FindComposerPhar(configRoot);
        if (composerPhar is null)
        {
            composerPhar = await EnsureComposerPharAsync(cancellationToken).ConfigureAwait(false);
            if (composerPhar is null)
            {
                Console.Error.WriteLine("[ivory] Unable to locate or download composer.phar.");
                return 1;
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(composerConfigPath));
            var root = doc.RootElement;

            if (!root.TryGetProperty("scripts", out var scriptsElem) ||
                scriptsElem.ValueKind != JsonValueKind.Object ||
                !scriptsElem.EnumerateObject().Any(p => string.Equals(p.Name, scriptName, StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine($"[ivory] Composer script '{scriptName}' not found in {composerConfigPath}.");
                return 1;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ivory] Failed to read Composer scripts from {composerConfigPath}: {ex.Message}");
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
            ["COMPOSER"] = composerConfigPath,
            ["IVORY_COMPOSER_PHAR"] = composerPhar,
            ["IVORY_ENABLE_DEFAULT_EXTENSIONS"] = "1"
        };

        Console.WriteLine($"[ivory] Running Composer script '{scriptName}' (php={phpVersionSpec})");
        Console.WriteLine($"[ivory]   COMPOSER={composerConfigPath}");

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
                    Console.Write($"\r[ivory] [{bar}] {percent,3}% ({FormatBytes(totalRead)}/{FormatBytes(contentLength)})");
                }
            }
            else
            {
                char frame = spinner[spinIndex++ % spinner.Length];
                Console.Write($"\r[ivory] [{frame}] {FormatBytes(totalRead)} downloaded");
            }
        }

        var suffix = contentLength > 0
            ? $"\r[ivory] [{new string('#', barWidth)}] 100% ({FormatBytes(totalRead)}/{FormatBytes(contentLength)})"
            : $"\r[ivory] [done] {FormatBytes(totalRead)} downloaded";
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
