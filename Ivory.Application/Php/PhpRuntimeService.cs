using Ivory.Application.Config;
using Ivory.Application.Runtime;
using Ivory.Domain.Php;
using Ivory.Domain.Config;

namespace Ivory.Application.Php;

public class PhpRuntimeService(
    IPhpInstaller installer,
    IPhpVersionResolver resolver,
    IProcessRunner processRunner,
    IProjectPhpHomeProvider projectPhpHomeProvider,
    IProjectConfigProvider configProvider) : IPhpRuntimeService
{
    private readonly IPhpInstaller _installer = installer;
    private readonly IPhpVersionResolver _resolver = resolver;
    private readonly IProcessRunner _processRunner = processRunner;
    private readonly IProjectPhpHomeProvider _projectPhpHomeProvider = projectPhpHomeProvider;
    private readonly IProjectConfigProvider _configProvider = configProvider;

    public Task<int> RunPhpAsync(
        string? scriptOrCommand,
        string[] args,
        string? overrideVersionSpec,
        CancellationToken cancellationToken = default)
        => RunPhpInternalAsync(scriptOrCommand, args, overrideVersionSpec, null, cancellationToken);

    public Task<int> RunPhpAsync(
        string? scriptOrCommand,
        string[] args,
        string? overrideVersionSpec,
        IDictionary<string, string?>? environment,
        CancellationToken cancellationToken = default)
        => RunPhpInternalAsync(scriptOrCommand, args, overrideVersionSpec, environment, cancellationToken);

    private async Task<int> RunPhpInternalAsync(
        string? scriptOrCommand,
        string[] args,
        string? overrideVersionSpec,
        IDictionary<string, string?>? environment,
        CancellationToken cancellationToken)
    {
        PhpVersion version;
        if (!string.IsNullOrWhiteSpace(overrideVersionSpec))
        {
            if (string.Equals(overrideVersionSpec.Trim(), "system", StringComparison.OrdinalIgnoreCase))
            {
                version = new PhpVersion("system");
            }
            else
            {
                version = new PhpVersion(overrideVersionSpec.Trim());
            }
        }
        else
        {
            version = await _resolver.ResolveForCurrentDirectoryAsync(cancellationToken).ConfigureAwait(false);
        }

        var configResult = await _configProvider.LoadAsync(System.Environment.CurrentDirectory, cancellationToken).ConfigureAwait(false);

        string phpPath;
        if (string.Equals(version.Value, "system", StringComparison.OrdinalIgnoreCase))
        {
            phpPath = OperatingSystem.IsWindows() ? "php.exe" : "php";
        }
        else
        {
            phpPath = await EnsureInstalledPhpAsync(version, cancellationToken).ConfigureAwait(false);
        }

        var iniOverride = ResolveIniOverride(phpPath);
        var defaultScanDir = GetDefaultScanDir(phpPath);
        string? extensionDirForIni = null;
        try
        {
            var phpDir = Path.GetDirectoryName(phpPath);
            if (!string.IsNullOrWhiteSpace(phpDir))
            {
                var extCandidate = Path.Combine(phpDir, "ext");
                if (Directory.Exists(extCandidate))
                {
                    extensionDirForIni = extCandidate;
                }
            }
        }
        catch
        {
        }

        var finalArgs = new List<string>();
        if (!string.IsNullOrWhiteSpace(iniOverride))
        {
            finalArgs.Add("-c");
            finalArgs.Add(iniOverride!);
        }
        TryAddExtensionDirArg(phpPath, args, finalArgs);

        if (!string.IsNullOrEmpty(scriptOrCommand))
        {
            finalArgs.Add(scriptOrCommand);
        }

        if (args is not null && args.Length > 0)
        {
            finalArgs.AddRange(args);
        }

        var env = environment is not null
            ? new Dictionary<string, string?>(environment, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        env["IVORY_PHP"] = phpPath;
        ConfigurePhpEnvironment(phpPath, env);

        bool enableDefaultExtensions = env.TryGetValue("IVORY_ENABLE_DEFAULT_EXTENSIONS", out var flag)
            && !string.IsNullOrWhiteSpace(flag)
            && !string.Equals(flag, "0", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(flag, "false", StringComparison.OrdinalIgnoreCase);

        var projectHome = await _projectPhpHomeProvider
            .TryGetExistingAsync(System.Environment.CurrentDirectory, cancellationToken)
            .ConfigureAwait(false);

        bool canInspectExisting = !string.IsNullOrWhiteSpace(iniOverride) || !string.IsNullOrWhiteSpace(defaultScanDir);
        var existingExtensions = canInspectExisting
            ? ReadConfiguredExtensions(iniOverride, defaultScanDir)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extensionDirectives = GetExtensionDirectives(
            configResult.Config,
            extensionDirForIni,
            enableDefaultExtensions && canInspectExisting,
            existingExtensions);

        if (projectHome is not null)
        {
            env["PHPRC"] = Path.GetDirectoryName(projectHome.IniPath) ?? projectHome.IniPath;
            env["PHP_INI_SCAN_DIR"] = projectHome.ExtensionsPath;
            env["IVORY_PHP_HOME"] = projectHome.HomePath;
            if (projectHome.ProjectRoot is not null)
            {
                env["IVORY_PROJECT_ROOT"] = projectHome.ProjectRoot;
            }

            if (extensionDirForIni is not null)
            {
                WriteExtensionDirIni(projectHome.ExtensionsPath, extensionDirForIni);
            }

            WriteExtensionsIni(projectHome.ExtensionsPath, extensionDirectives);
        }
        else
        {
            env.TryAdd("IVORY_PROJECT_ROOT", System.Environment.CurrentDirectory);
            if (env.TryGetValue("PHP_INI_SCAN_DIR", out var scanDir) && !string.IsNullOrWhiteSpace(scanDir))
            {
                WriteExtensionsIni(scanDir!, extensionDirectives);
            }
        }

        string workingDir = System.Environment.CurrentDirectory;

        return await _processRunner.RunAsync(
            executable: phpPath,
            arguments: finalArgs,
            workingDirectory: workingDir,
            environment: env,
            redirectOutput: false,
            redirectError: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static void TryAddExtensionDirArg(string phpPath, string[]? args, List<string> targetArgs)
    {
        try
        {
            var phpDir = Path.GetDirectoryName(phpPath);
            if (string.IsNullOrWhiteSpace(phpDir))
            {
                return;
            }

            var extDir = Path.Combine(phpDir, "ext");
            if (!Directory.Exists(extDir))
            {
                return;
            }

            if (args is not null && args.Any(a => a.Contains("extension_dir", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            string extensionArg = $"extension_dir=\"{extDir}\"";
            targetArgs.Add("-d");
            targetArgs.Add(extensionArg);
        }
        catch
        {
            // Ignore optional extension_dir auto-injection failures.
        }
    }

    private async Task<string> EnsureInstalledPhpAsync(PhpVersion version, CancellationToken cancellationToken)
    {
        try
        {
            return await _installer.GetInstalledPathAsync(version, cancellationToken).ConfigureAwait(false);
        }
        catch (System.IO.FileNotFoundException)
        {
            Console.WriteLine($"[ivory] PHP {version.Value} is not installed. Installing now...");
            await _installer.InstallAsync(version, cancellationToken: cancellationToken).ConfigureAwait(false);
            return await _installer.GetInstalledPathAsync(version, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void ConfigurePhpEnvironment(string phpPath, IDictionary<string, string?> env)
    {
        try
        {
            var phpDir = Path.GetDirectoryName(phpPath);
            if (string.IsNullOrWhiteSpace(phpDir))
            {
                return;
            }

            var extensionDir = Path.Combine(phpDir, "ext");
            if (Directory.Exists(extensionDir))
            {
                env["PHP_EXTENSION_DIR"] = extensionDir;
            }

            // Ensure a PHP ini scan directory exists with an extension_dir override so child PHP processes inherit it.
            var defaultScanDir = Path.Combine(phpDir, "conf.d");
            Directory.CreateDirectory(defaultScanDir);
            WriteExtensionDirIni(defaultScanDir, extensionDir);

            env.TryAdd("PHP_INI_SCAN_DIR", defaultScanDir);

            var phpIniPath = Path.Combine(phpDir, "php.ini");
            var phpIniDir = Path.GetDirectoryName(phpIniPath);
            if (!string.IsNullOrWhiteSpace(phpIniDir))
            {
                env.TryAdd("PHPRC", phpIniDir);
            }

            var separator = OperatingSystem.IsWindows() ? ';' : ':';
            var currentPath = env.TryGetValue("PATH", out var envPath) && !string.IsNullOrWhiteSpace(envPath)
                ? envPath!
                : System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            bool alreadyPresent = currentPath
                .Split([separator], StringSplitOptions.RemoveEmptyEntries)
                .Any(p => string.Equals(p.Trim(), phpDir, StringComparison.OrdinalIgnoreCase));

            var newPath = alreadyPresent
                ? currentPath
                : (string.IsNullOrEmpty(currentPath) ? phpDir : phpDir + separator + currentPath);

            env["PATH"] = newPath;
        }
        catch
        {
            // Best-effort environment setup.
        }
    }

    private static string? ResolveIniOverride(string phpPath)
    {
        try
        {
            var phpDir = Path.GetDirectoryName(phpPath);
            if (string.IsNullOrWhiteSpace(phpDir))
            {
                return null;
            }

            string primary = Path.Combine(phpDir, "php.ini");
            if (File.Exists(primary))
            {
                return primary;
            }

            string dev = Path.Combine(phpDir, "php.ini-development");
            if (File.Exists(dev))
            {
                return dev;
            }

            string prod = Path.Combine(phpDir, "php.ini-production");
            if (File.Exists(prod))
            {
                return prod;
            }
        }
        catch
        {
        }

        return null;
    }

    private static void WriteExtensionDirIni(string scanDir, string extensionDir)
    {
        if (!Directory.Exists(scanDir) || string.IsNullOrWhiteSpace(extensionDir))
        {
            return;
        }

        try
        {
            var iniPath = Path.Combine(scanDir, "99-ivory-extension-dir.ini");
            var desired = $"extension_dir=\"{extensionDir}\"{System.Environment.NewLine}";
            if (!File.Exists(iniPath) || File.ReadAllText(iniPath) != desired)
            {
                File.WriteAllText(iniPath, desired);
            }
        }
        catch
        {
            // Ignore failures; main process still adds -d extension_dir when possible.
        }
    }

    private static IReadOnlyList<string> GetExtensionDirectives(IvoryConfig? config, string? extensionDir, bool enableDefaults, HashSet<string> existingExtensions)
    {
        IEnumerable<string> configured = Enumerable.Empty<string>();
        if (config?.Php is not null && config.Php.Ini.Count > 0)
        {
            configured = config.Php.Ini
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Where(i =>
                    i.TrimStart().StartsWith("extension", StringComparison.OrdinalIgnoreCase) ||
                    i.TrimStart().StartsWith("zend_extension", StringComparison.OrdinalIgnoreCase));
        }

        if (configured.Any())
        {
            return FilterExistingExtensions(configured, extensionDir);
        }

        if (!enableDefaults)
        {
            return Array.Empty<string>();
        }

        // Default to a minimal set required by Composer/Laravel if enabled.
        var defaults = new[]
        {
            "extension=curl",
            "extension=openssl",
            "extension=mbstring",
            "extension=sodium",
            "extension=fileinfo",
        };

        var filteredDefaults = defaults
            .Where(d =>
            {
                var name = ExtractExtensionName(d);
                return string.IsNullOrWhiteSpace(name) || !existingExtensions.Contains(name);
            });

        return FilterExistingExtensions(filteredDefaults, extensionDir);
    }

    private static IReadOnlyList<string> FilterExistingExtensions(IEnumerable<string> directives, string? extensionDir)
    {
        if (string.IsNullOrWhiteSpace(extensionDir) || !Directory.Exists(extensionDir))
        {
            return directives.ToArray();
        }

        var list = new List<string>();
        foreach (var directive in directives)
        {
            if (!directive.TrimStart().StartsWith("extension", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(directive);
                continue;
            }

            var parts = directive.Split('=', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                list.Add(directive);
                continue;
            }

            var name = parts[1];
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            bool exists = OperatingSystem.IsWindows()
                ? File.Exists(Path.Combine(extensionDir, name)) ||
                  File.Exists(Path.Combine(extensionDir, $"php_{name}.dll")) ||
                  File.Exists(Path.Combine(extensionDir, $"{name}.dll"))
                : File.Exists(Path.Combine(extensionDir, name)) ||
                  File.Exists(Path.Combine(extensionDir, $"php_{name}.so")) ||
                  File.Exists(Path.Combine(extensionDir, $"{name}.so"));

            if (exists)
            {
                list.Add($"extension={name}");
            }
        }

        return list;
    }

    private static void WriteExtensionsIni(string scanDir, IReadOnlyList<string> directives)
    {
        if (!Directory.Exists(scanDir) || directives.Count == 0)
        {
            return;
        }

        try
        {
            var iniPath = Path.Combine(scanDir, "20-ivory-extensions.ini");
            var content = string.Join(System.Environment.NewLine, directives) + System.Environment.NewLine;
            if (!File.Exists(iniPath) || File.ReadAllText(iniPath) != content)
            {
                File.WriteAllText(iniPath, content);
            }
        }
        catch
        {
            // Best-effort; failing to write extension list should not crash the CLI.
        }
    }

    private static HashSet<string> ReadConfiguredExtensions(string? iniPath, string? scanDir)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!string.IsNullOrWhiteSpace(iniPath) && File.Exists(iniPath))
            {
                foreach (var line in File.ReadLines(iniPath))
                {
                    var name = ExtractExtensionName(line);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        set.Add(name);
                    }
                }
            }
        }
        catch
        {
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(scanDir) && Directory.Exists(scanDir))
            {
                foreach (var file in Directory.EnumerateFiles(scanDir, "*.ini", SearchOption.TopDirectoryOnly))
                {
                    var fname = Path.GetFileName(file);
                    if (fname is "20-ivory-extensions.ini" or "99-ivory-extension-dir.ini")
                    {
                        continue;
                    }

                    foreach (var line in File.ReadLines(file))
                    {
                        var name = ExtractExtensionName(line);
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            set.Add(name);
                        }
                    }
                }
            }
        }
        catch
        {
        }

        return set;
    }

    private static string ExtractExtensionName(string? directive)
    {
        if (string.IsNullOrWhiteSpace(directive))
        {
            return string.Empty;
        }

        var trimmed = directive.Trim();
        if (!trimmed.StartsWith("extension", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("zend_extension", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        var parts = trimmed.Split('=', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return string.Empty;
        }

        var value = parts[1].Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var basename = Path.GetFileName(value);
        var withoutExt = Path.GetFileNameWithoutExtension(basename);
        if (withoutExt.StartsWith("php_", StringComparison.OrdinalIgnoreCase))
        {
            withoutExt = withoutExt["php_".Length..];
        }

        return withoutExt;
    }

    private static string? GetDefaultScanDir(string phpPath)
    {
        try
        {
            var phpDir = Path.GetDirectoryName(phpPath);
            if (string.IsNullOrWhiteSpace(phpDir))
            {
                return null;
            }

            var scanDir = Path.Combine(phpDir, "conf.d");
            Directory.CreateDirectory(scanDir);
            return scanDir;
        }
        catch
        {
            return null;
        }
    }
}
