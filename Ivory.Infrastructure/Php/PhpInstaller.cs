using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ivory.Application.Php;
using Ivory.Domain.Php;
using Ivory.Domain.Runtime;

namespace Ivory.Infrastructure.Php;

public class PhpInstaller : IPhpInstaller, IDisposable
{
    private const string UserAgent = "ivory-cli/1.0 (+https://github.com/)";
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
        var ivoryDir = Path.Combine(home, ".ivory");

        _versionsRoot = Path.Combine(ivoryDir, "versions");
        _cacheRoot = Path.Combine(ivoryDir, "cache", "php");

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
            Console.WriteLine($"[ivory] PHP {resolvedVersion} already installed at {installDir}");
            return;
        }

        Console.WriteLine($"[ivory] Installing PHP {resolvedVersion} for {_platform.Value}...");
        Directory.CreateDirectory(installDir);

        string archivePath = await DownloadArchiveAsync(
            artifact.Url,
            artifact.Sha256,
            resolvedVersion,
            ignoreChecksum,
            cancellationToken).ConfigureAwait(false);
        ExtractArchive(archivePath, installDir);
        await NormalizePhpLayoutAsync(installDir, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"[ivory] Installed PHP {resolvedVersion} at {installDir}");
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
            Console.WriteLine($"[ivory] PHP {version.Value} is not installed; nothing to remove.");
            return;
        }

        if (installed.Count == 1)
        {
            Console.WriteLine($"[ivory] Refusing to remove the only installed version ({version.Value}).");
            return;
        }

        string dir = GetInstallDir(version.Value);
        try
        {
            Directory.Delete(dir, recursive: true);
            Console.WriteLine($"[ivory] Removed PHP {version.Value} at {dir}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ivory] Failed to remove {dir}: {ex.Message}");
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
                Console.WriteLine($"[ivory] Pruned PHP {v.Value} at {dir}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ivory] Failed to prune {dir}: {ex.Message}");
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
                    Console.WriteLine("[ivory] Cleared PHP cache.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ivory] Failed to clear cache: {ex.Message}");
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
                Console.WriteLine($"[ivory] Using cached archive {archivePath}");
                return archivePath;
            }

            Console.WriteLine("[ivory] Cached archive checksum mismatch, deleting (pass --ignore-checksum to skip)...");
            File.Delete(archivePath);
        }

        Console.WriteLine($"[ivory] Downloading {url}...");
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
            Console.WriteLine("[ivory] Skipping checksum verification.");
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

        string targetRoot = Path.Combine(installDir, phpName);
        string targetBin = Path.Combine(binDir, phpName);

        LayoutNormalizer.EnsureExecutableShim(existing, targetRoot);
        LayoutNormalizer.EnsureExecutableShim(existing, targetBin);

        var extDir = Path.Combine(Path.GetDirectoryName(existing)!, "ext");
        if (Directory.Exists(extDir))
        {
            var targetExt = Path.Combine(installDir, "ext");
            LayoutNormalizer.EnsureExtensionLink(extDir, targetExt);
        }

        await Task.CompletedTask;
    }

    internal static class LayoutNormalizer
    {
        public static void EnsureExecutableShim(string sourcePath, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            try
            {
                if (File.Exists(targetPath))
                {
                    try
                    {
                        File.Delete(targetPath);
                    }
                    catch
                    {
                        // If deletion fails, fall back to overwrite via copy below.
                    }
                }

                if (TryCreateSymlink(targetPath, sourcePath))
                {
                    return;
                }

                File.Copy(sourcePath, targetPath, overwrite: true);
            }
            catch
            {
                // Best effort shim; failures are non-fatal because GetInstalledPathAsync can still locate the real binary recursively.
            }
        }

        public static void EnsureExtensionLink(string sourceDir, string targetDir)
        {
            try
            {
                if (Directory.Exists(targetDir))
                {
                    return;
                }

                if (TryCreateDirectorySymlink(targetDir, sourceDir))
                {
                    return;
                }

                Directory.CreateDirectory(targetDir);
            }
            catch
            {
                // Optional; ignore failures.
            }
        }

        private static bool TryCreateSymlink(string linkPath, string targetPath)
        {
            try
            {
                File.CreateSymbolicLink(linkPath, targetPath);
                return true;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static bool TryCreateDirectorySymlink(string linkPath, string targetPath)
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
                return true;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
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
                    Console.Write($"\r[ivory] [{bar}] {percent,3}% ({Bytes(totalRead)}/{Bytes(contentLength)})");
                }
            }
            else
            {
                char frame = spinner[spinIndex++ % spinner.Length];
                Console.Write($"\r[ivory] [{frame}] {Bytes(totalRead)} downloaded");
            }
        }

        var suffix = contentLength > 0
            ? $"\r[ivory] [{new string('#', barWidth)}] 100% ({Bytes(totalRead)}/{Bytes(contentLength)})"
            : $"\r[ivory] [done] {Bytes(totalRead)} downloaded";
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

