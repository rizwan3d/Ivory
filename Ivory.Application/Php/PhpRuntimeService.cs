using Ivory.Application.Runtime;
using Ivory.Domain.Php;

namespace Ivory.Application.Php;

public class PhpRuntimeService(
    IPhpInstaller installer,
    IPhpVersionResolver resolver,
    IProcessRunner processRunner,
    IProjectPhpHomeProvider projectPhpHomeProvider) : IPhpRuntimeService
{
    private readonly IPhpInstaller _installer = installer;
    private readonly IPhpVersionResolver _resolver = resolver;
    private readonly IProcessRunner _processRunner = processRunner;
    private readonly IProjectPhpHomeProvider _projectPhpHomeProvider = projectPhpHomeProvider;

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

        string phpPath;
        if (string.Equals(version.Value, "system", StringComparison.OrdinalIgnoreCase))
        {
            phpPath = OperatingSystem.IsWindows() ? "php.exe" : "php";
        }
        else
        {
            phpPath = await EnsureInstalledPhpAsync(version, cancellationToken).ConfigureAwait(false);
        }

        var finalArgs = new List<string>();

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

        try
        {
            var phpDir = Path.GetDirectoryName(phpPath);
            if (!string.IsNullOrWhiteSpace(phpDir))
            {
                var currentPath = System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                var separator = OperatingSystem.IsWindows() ? ';' : ':';

                bool alreadyPresent = currentPath
                    .Split([separator], StringSplitOptions.RemoveEmptyEntries)
                    .Any(p => string.Equals(p.Trim(), phpDir, StringComparison.OrdinalIgnoreCase));

                if (!alreadyPresent)
                {
                    var newPath = string.IsNullOrEmpty(currentPath)
                        ? phpDir
                        : phpDir + separator + currentPath;

                    env["PATH"] = newPath;
                }
            }
        }
        catch
        {
        }

        var projectHome = await _projectPhpHomeProvider
            .TryGetExistingAsync(System.Environment.CurrentDirectory, cancellationToken)
            .ConfigureAwait(false);

        if (projectHome is not null)
        {
            env["PHPRC"] = projectHome.IniPath;
            env["PHP_INI_SCAN_DIR"] = projectHome.ExtensionsPath;
            env["IVORY_PHP_HOME"] = projectHome.HomePath;
            if (projectHome.ProjectRoot is not null)
            {
                env["IVORY_PROJECT_ROOT"] = projectHome.ProjectRoot;
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
}
