using System.Net.Http;
using Ivory.Application.Composer;
using Ivory.Application.Laravel;
using Ivory.Application.Php;
using Ivory.Infrastructure.Http;

namespace Ivory.Infrastructure.Laravel;

public class LaravelService(IPhpRuntimeService runtime, IComposerService composerService, IHttpClientFactory httpClientFactory) : ILaravelService
{
    private const string DownloadUrl = "https://download.herdphp.com/resources/laravel";
    private readonly IPhpRuntimeService _runtime = runtime;
    private readonly IComposerService _composerService = composerService;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    public async Task<int> RunLaravelAsync(string[] args, string phpVersionSpec, CancellationToken cancellationToken = default)
    {
        string laravelPath;
        try
        {
            laravelPath = await EnsureLaravelAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ivory] Failed to prepare Laravel installer: {ex.Message}");
            return 1;
        }

        string? composerPhar = await _composerService.EnsureComposerPharAsync(cancellationToken).ConfigureAwait(false);
        if (composerPhar is null)
        {
            Console.Error.WriteLine("[ivory] Unable to locate or download composer.phar required by Laravel installer.");
            return 1;
        }

        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["IVORY_ENABLE_DEFAULT_EXTENSIONS"] = "1"
        };
        TryEnsureComposerShim(composerPhar, env);

        string[] forwardedArgs = args ?? Array.Empty<string>();
        return await _runtime.RunPhpAsync(laravelPath, forwardedArgs, phpVersionSpec, env, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> EnsureLaravelAsync(CancellationToken cancellationToken)
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        string ivoryDir = Path.Combine(home, ".ivory");
        Directory.CreateDirectory(ivoryDir);

        string targetPath = Path.Combine(ivoryDir, "laravel");
        if (File.Exists(targetPath))
        {
            return targetPath;
        }

        Console.WriteLine($"[ivory] Downloading Laravel installer from {DownloadUrl} ...");

        string tempPath = Path.Combine(ivoryDir, "laravel.tmp");
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        using (var response = await _httpClientFactory
                       .CreateClient(HttpClientNames.Default)
                       .GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                       .ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();

            await using var destination = File.Create(tempPath);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await stream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var info = new FileInfo(tempPath);
        if (info.Length == 0)
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("Downloaded Laravel installer is empty.");
        }

        File.Move(tempPath, targetPath, overwrite: true);
        Console.WriteLine($"[ivory] Saved Laravel installer to {targetPath}");

        return targetPath;
    }

    private static void TryEnsureComposerShim(string composerPhar, IDictionary<string, string?> env)
    {
        try
        {
            var dir = Path.GetDirectoryName(composerPhar);
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                string shimPath = Path.Combine(dir, "composer.bat");
                string content =
                    "@echo off" + System.Environment.NewLine +
                    $"\"%IVORY_PHP%\" \"{composerPhar}\" %*" + System.Environment.NewLine;
                File.WriteAllText(shimPath, content);
            }
            else
            {
                string shimPath = Path.Combine(dir, "composer");
                string content =
                    "#!/usr/bin/env sh" + System.Environment.NewLine +
                    $"\"$IVORY_PHP\" \"{composerPhar}\" \"$@\"" + System.Environment.NewLine;
                File.WriteAllText(shimPath, content);
                try
                {
                    System.Diagnostics.Process.Start("chmod", $"+x \"{shimPath}\"")?.WaitForExit();
                }
                catch
                {
                    // Ignore failure to chmod on non-POSIX.
                }
            }

            var currentPath = System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            env["PATH"] = string.IsNullOrEmpty(currentPath)
                ? dir
                : dir + separator + currentPath;
        }
        catch
        {
            // Best-effort; Laravel may still find system composer if available.
        }
    }
}
