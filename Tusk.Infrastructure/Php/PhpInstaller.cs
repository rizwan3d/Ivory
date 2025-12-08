using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Tusk.Application.Php;
using Tusk.Domain.Php;
using Tusk.Domain.Runtime;

namespace Tusk.Infrastructure.Php;

public class PhpInstaller : IPhpInstaller, IDisposable
{
    private const string UserAgent = "tusk-cli/1.0 (+https://github.com/)";
    private const string LinuxHerdBase = "https://download.herdphp.com/herd-lite/linux";
    private readonly PhpVersionsManifest _manifest;
    private readonly PlatformId _platform;
    private readonly WindowsPhpFeed _windowsFeed;
    private readonly string _versionsRoot;
    private readonly string _cacheRoot;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public PhpInstaller(PhpVersionsManifest manifest, WindowsPhpFeed windowsFeed, HttpClient? httpClient = null)
    {
        _manifest = manifest;
        _windowsFeed = windowsFeed;
        _platform = PlatformId.DetectCurrent();

        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var tuskDir = Path.Combine(home, ".tusk");

        _versionsRoot = Path.Combine(tuskDir, "versions");
        _cacheRoot = Path.Combine(tuskDir, "cache", "php");

        Directory.CreateDirectory(_versionsRoot);
        Directory.CreateDirectory(_cacheRoot);

        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public async Task InstallAsync(PhpVersion versionSpec, bool ignoreChecksum = false, CancellationToken cancellationToken = default)
    {
        string resolvedVersion;
        PhpArtifact artifact;

        if (!_manifest.TryResolveArtifact(_platform, versionSpec.Value,
                out resolvedVersion, out artifact))
        {
            if (OperatingSystem.IsWindows())
            {
                var result = await _windowsFeed.ResolveAsync(versionSpec.Value, cancellationToken).ConfigureAwait(false);
                if (!result.Success)
                {
                    throw new InvalidOperationException(
                        $"PHP version '{versionSpec.Value}' is not available for platform '{_platform.Value}' (only 64-bit NTS builds are supported on Windows).");
                }

                resolvedVersion = result.ResolvedVersion;
                artifact = result.Artifact;
            }
            else if (OperatingSystem.IsLinux())
            {
                resolvedVersion = versionSpec.Value;
                string archSegment = RuntimeInformation.ProcessArchitecture switch
                {
                    Architecture.X64 => "x64",
                    Architecture.Arm64 => "arm64",
                    _ => throw new PlatformNotSupportedException($"Unsupported Linux architecture: {RuntimeInformation.ProcessArchitecture}")
                };

                var url = $"{LinuxHerdBase}/{archSegment}/{resolvedVersion}/php";
                artifact = new PhpArtifact
                {
                    Url = url,
                    Sha256 = string.Empty
                };
            }
            else
            {
                throw new InvalidOperationException(
                    $"PHP version '{versionSpec.Value}' is not defined for platform '{_platform.Value}' in php-versions.json.");
            }
        }

        string installDir = GetInstallDir(resolvedVersion);
        if (Directory.Exists(installDir))
        {
            Console.WriteLine($"[tusk] PHP {resolvedVersion} already installed at {installDir}");
            return;
        }

        Console.WriteLine($"[tusk] Installing PHP {resolvedVersion} for {_platform.Value}...");
        Directory.CreateDirectory(installDir);

        string archivePath = await DownloadArchiveAsync(
            artifact.Url,
            artifact.Sha256,
            resolvedVersion,
            ignoreChecksum,
            cancellationToken).ConfigureAwait(false);
        ExtractArchive(archivePath, installDir);
        await NormalizePhpLayoutAsync(installDir, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"[tusk] Installed PHP {resolvedVersion} at {installDir}");
    }

    public Task<string> GetInstalledPathAsync(PhpVersion version, CancellationToken cancellationToken = default)
    {
        string installDir = GetInstallDir(version.Value);
        string phpName = OperatingSystem.IsWindows() ? "php.exe" : "php";
        string phpPath = Path.Combine(installDir, phpName);

        if (!File.Exists(phpPath))
        {
            throw new FileNotFoundException($"PHP binary not found at {phpPath}. Is version '{version.Value}' installed?");
        }

        return Task.FromResult(phpPath);
    }

    public Task<IReadOnlyList<PhpVersion>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var platformDir = Path.Combine(_versionsRoot, _platform.Value);
        if (!Directory.Exists(platformDir))
        {
            return Task.FromResult<IReadOnlyList<PhpVersion>>([]);
        }

        var versions = Directory.GetDirectories(platformDir)
            .Select(Path.GetFileName)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => new PhpVersion(v!))
            .ToList();

        return Task.FromResult<IReadOnlyList<PhpVersion>>(versions);
    }

    public async Task UninstallAsync(PhpVersion version, CancellationToken cancellationToken = default)
    {
        var installed = await ListInstalledAsync(cancellationToken).ConfigureAwait(false);
        if (!installed.Any(v => string.Equals(v.Value, version.Value, StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine($"[tusk] PHP {version.Value} is not installed; nothing to remove.");
            return;
        }

        if (installed.Count == 1)
        {
            Console.WriteLine($"[tusk] Refusing to remove the only installed version ({version.Value}).");
            return;
        }

        string dir = GetInstallDir(version.Value);
        try
        {
            Directory.Delete(dir, recursive: true);
            Console.WriteLine($"[tusk] Removed PHP {version.Value} at {dir}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[tusk] Failed to remove {dir}: {ex.Message}");
        }
    }

    public async Task<int> PruneAsync(int keepLatest = 1, bool includeCache = true, CancellationToken cancellationToken = default)
    {
        var installed = (await ListInstalledAsync(cancellationToken).ConfigureAwait(false))
            .OrderByDescending(v => v.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var keepCount = Math.Max(keepLatest, 0);
        var toKeep = installed.Take(keepCount).Select(v => v.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int removed = 0;

        foreach (var v in installed.Skip(keepCount))
        {
            string dir = GetInstallDir(v.Value);
            try
            {
                Directory.Delete(dir, recursive: true);
                removed++;
                Console.WriteLine($"[tusk] Pruned PHP {v.Value} at {dir}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[tusk] Failed to prune {dir}: {ex.Message}");
            }
        }

        if (includeCache)
        {
            try
            {
                if (Directory.Exists(_cacheRoot))
                {
                    Directory.Delete(_cacheRoot, recursive: true);
                    Directory.CreateDirectory(_cacheRoot);
                    Console.WriteLine("[tusk] Cleared PHP cache.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[tusk] Failed to clear cache: {ex.Message}");
            }
        }

        return removed;
    }

    private string GetInstallDir(string exactVersion)
        => Path.Combine(_versionsRoot, _platform.Value, exactVersion);

    private async Task<string> DownloadArchiveAsync(
        string url,
        string expectedSha256,
        string version,
        bool ignoreChecksum,
        CancellationToken ct)
    {
        string versionCacheDir = Path.Combine(_cacheRoot, version);
        Directory.CreateDirectory(versionCacheDir);

        var uri = new Uri(url);
        string fileName = Path.GetFileName(uri.LocalPath);
        string archivePath = Path.Combine(versionCacheDir, fileName);

        if (File.Exists(archivePath))
        {
            if (ignoreChecksum || await VerifySha256Async(archivePath, expectedSha256, ct).ConfigureAwait(false))
            {
                Console.WriteLine($"[tusk] Using cached archive {archivePath}");
                return archivePath;
            }

            Console.WriteLine("[tusk] Cached archive checksum mismatch, deleting (pass --ignore-checksum to skip)...");
            File.Delete(archivePath);
        }

        Console.WriteLine($"[tusk] Downloading {url}...");
        var tempPath = archivePath + ".part";
        using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
        {
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var fileStream = File.Create(tempPath);
            var contentLength = response.Content.Headers.ContentLength ?? -1;
            await CopyToWithProgressAsync(responseStream, fileStream, contentLength, ct).ConfigureAwait(false);
        }

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }
        File.Move(tempPath, archivePath);

        if (!ignoreChecksum && !string.IsNullOrWhiteSpace(expectedSha256))
        {
            if (!await VerifySha256Async(archivePath, expectedSha256, ct).ConfigureAwait(false))
            {
                File.Delete(archivePath);
                throw new InvalidOperationException("Checksum mismatch for downloaded PHP archive.");
            }
        }
        else
        {
            Console.WriteLine("[tusk] Skipping checksum verification.");
        }

        return archivePath;
    }

    private static async Task<bool> VerifySha256Async(string path, string expectedHex, CancellationToken ct)
    {
        expectedHex = expectedHex.Trim().ToLowerInvariant();

        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);

        string actualHex = Convert.ToHexStringLower(hash);
        return string.Equals(actualHex, expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    private void ExtractArchive(string archivePath, string targetDir)
    {
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, targetDir, overwriteFiles: true);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
                 archivePath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            ExtractTarGzWithTar(archivePath, targetDir);
        }
        else
        {
            throw new NotSupportedException($"Unsupported archive type: {Path.GetFileName(archivePath)}");
        }
    }

    private static void ExtractTarGzWithTar(string archivePath, string targetDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "tar",
            ArgumentList = { "-xzf", archivePath, "-C", targetDir },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi)
                           ?? throw new InvalidOperationException("Failed to start 'tar' process.");

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"tar failed with code {process.ExitCode}: {error}");
        }
    }

    private async Task NormalizePhpLayoutAsync(string installDir, CancellationToken ct = default)
    {
        string binDir = Path.Combine(installDir, "bin");
        Directory.CreateDirectory(binDir);

        string phpName = OperatingSystem.IsWindows() ? "php.exe" : "php";
        string windowsAltName = "php-win.exe";

        string? existing = Directory
            .EnumerateFiles(installDir, phpName, SearchOption.AllDirectories)
            .FirstOrDefault();

        if (existing is null && OperatingSystem.IsWindows())
        {
            existing = Directory
                .EnumerateFiles(installDir, windowsAltName, SearchOption.AllDirectories)
                .FirstOrDefault();
        }

        if (existing is null)
        {
            throw new InvalidOperationException("Extracted archive does not contain a PHP binary.");
        }

        if (OperatingSystem.IsWindows() && Path.GetFileName(existing).Equals(windowsAltName, StringComparison.OrdinalIgnoreCase))
        {
            var renamed = Path.Combine(Path.GetDirectoryName(existing)!, phpName);
            try
            {
                File.Move(existing, renamed);
                existing = renamed;
            }
            catch (IOException)
            {
            }
        }
        string targetPath = Path.Combine(binDir, phpName);

        await Task.CompletedTask;
    }

    private static async Task CopyToWithProgressAsync(Stream source, Stream destination, long contentLength, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long totalRead = 0;
        int lastPercent = -1;
        var spinner = new[] { '|', '/', '-', '\\' };
        int spinIndex = 0;
        const int barWidth = 28;

        while (true)
        {
            int read = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0) break;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            totalRead += read;

            if (contentLength > 0)
            {
                int percent = (int)(totalRead * 100 / contentLength);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    int filled = (int)Math.Min(barWidth, Math.Max(0, percent * barWidth / 100));
                    string bar = new string('#', filled).PadRight(barWidth, '.');
                    Console.Write($"\r[tusk] [{bar}] {percent,3}% ({Bytes(totalRead)}/{Bytes(contentLength)})");
                }
            }
            else
            {
                char frame = spinner[spinIndex++ % spinner.Length];
                Console.Write($"\r[tusk] [{frame}] {Bytes(totalRead)} downloaded");
            }
        }

        var suffix = contentLength > 0
            ? $"\r[tusk] [{new string('#', barWidth)}] 100% ({Bytes(totalRead)}/{Bytes(contentLength)})"
            : $"\r[tusk] [done] {Bytes(totalRead)} downloaded";
        Console.WriteLine(suffix);
    }

    private static string Bytes(long value)
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }
}
