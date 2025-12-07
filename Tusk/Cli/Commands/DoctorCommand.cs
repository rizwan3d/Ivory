using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Tusk.Application.Composer;
using Tusk.Application.Config;
using Tusk.Application.Environment;
using Tusk.Application.Php;
using Tusk.Cli.Execution;
using Tusk.Cli.Formatting;
using Tusk.Domain.Cli.Doctor;
using Tusk.Domain.Config;
using Tusk.Domain.Php;
using Tusk.Domain.Runtime;

namespace Tusk.Cli.Commands;

internal static class DoctorCommand
{
    public static Command Create(
        IPhpInstaller installer,
        IPhpVersionResolver resolver,
        Option<string> phpVersionOption,
        IProjectConfigProvider configProvider,
        IComposerService composerService,
        IEnvironmentProbe environmentProbe,
        IProjectPhpHomeProvider projectPhpHomeProvider)
    {
        var jsonOption = new Option<bool>("--json")
        {
            Description = "Output machine-readable JSON."
        };
        var command = new Command("doctor", "Show Tusk environment, paths, and PHP version resolution.\nExamples:\n  tusk doctor\n  tusk doctor --json")
        {
            jsonOption
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync(async _ =>
            {
                string cwd = Environment.CurrentDirectory;
                var platform = PlatformId.DetectCurrent();

                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string tuskHome = Path.Combine(home, ".tusk");
                string versionsRoot = Path.Combine(tuskHome, "versions");
                string cacheRoot = Path.Combine(tuskHome, "cache", "php");
                string manifestPath = Path.Combine(tuskHome, "php-versions.json");
                string globalConfigPath = Path.Combine(tuskHome, "config.json");

                var configResult = await configProvider.LoadAsync(cwd).ConfigureAwait(false);
                string? projectPhpVersion = configResult.Config?.Php?.Version;
                var projectHome = await projectPhpHomeProvider.TryGetExistingAsync(cwd).ConfigureAwait(false);

                string? globalDefaultVersion = null;
                if (File.Exists(globalConfigPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(globalConfigPath, default).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("defaultPhpVersion", out var prop))
                        {
                            globalDefaultVersion = prop.GetString();
                        }
                    }
                    catch (JsonException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                string overrideSpec = parseResult.GetValue(phpVersionOption) ?? string.Empty;
                bool overrideProvided =
                    !string.IsNullOrWhiteSpace(overrideSpec) &&
                    !string.Equals(overrideSpec, "system", StringComparison.OrdinalIgnoreCase);

                PhpVersion resolvedByResolver = await resolver.ResolveForCurrentDirectoryAsync().ConfigureAwait(false);

                string finalVersion;
                if (!string.IsNullOrWhiteSpace(overrideSpec) &&
                    !string.Equals(overrideSpec, "system", StringComparison.OrdinalIgnoreCase))
                {
                    finalVersion = overrideSpec.Trim();
                }
                else
                {
                    finalVersion = resolvedByResolver.Value;
                }

                string? phpBinaryPath = null;
                if (string.Equals(finalVersion, "system", StringComparison.OrdinalIgnoreCase))
                {
                    phpBinaryPath = environmentProbe.FindSystemPhpExecutablePath();
                }
                else
                {
                    try
                    {
                        phpBinaryPath = await installer.GetInstalledPathAsync(new PhpVersion(finalVersion)).ConfigureAwait(false);
                    }
                    catch (FileNotFoundException)
                    {
                        phpBinaryPath = null;
                    }
                    catch (InvalidOperationException)
                    {
                        phpBinaryPath = null;
                    }
                }

                var installed = await installer.ListInstalledAsync().ConfigureAwait(false);

                string? composerPhar = composerService.FindComposerPhar(configResult.RootDirectory);
                string? composerExe = environmentProbe.FindSystemComposerExecutablePath();

                bool asJson = parseResult.GetValue(jsonOption);
                if (asJson)
                {
                    var payload = new DoctorModel()
                    {
                        Cwd = cwd,
                        Platform = platform.Value, // or platform.ToString()

                        TuskHome = tuskHome,
                        VersionsRoot = versionsRoot,
                        CacheRoot = cacheRoot,
                        ManifestPath = manifestPath,
                        GlobalConfigPath = globalConfigPath,

                        Project = new DoctorModel.ProjectInfo
                        {
                            Root = configResult.RootDirectory,
                            PhpVersion = projectPhpVersion
                        },
                        Resolution = new DoctorModel.ResolutionInfo
                        {
                            OverrideSpec = overrideSpec,
                            ProjectVersion = projectPhpVersion,
                            GlobalDefault = globalDefaultVersion,
                            FinalVersion = finalVersion
                        },

                        PhpBinaryPath = phpBinaryPath,
                        Installed = installed.Select(v => v.Value).ToArray(),

                        Composer = new DoctorModel.ComposerInfo
                        {
                            ComposerPhar = composerPhar,
                            ComposerExe = composerExe
                        },

                        ProjectPhpHome = projectHome is null
                            ? null
                            : new DoctorModel.ProjectPhpHomeInfo
                            {
                                Home = projectHome.HomePath,
                                Ini = projectHome.IniPath,
                                Extensions = projectHome.ExtensionsPath,
                                Enabled = true
                            }
                    };

                    ConsoleFormatter.PrintDoctor(payload, true);
                    return;
                }

                CliConsole.Info("Tusk Doctor");
                Console.WriteLine("-----------");
                Console.WriteLine($"Current directory: {cwd}");
                Console.WriteLine($"Platform:          {platform.Value}");
                Console.WriteLine();
                Console.WriteLine("Paths:");
                Console.WriteLine($"  Tusk home:       {tuskHome}");
                Console.WriteLine($"  Versions root:   {versionsRoot}");
                Console.WriteLine($"  Cache root:      {cacheRoot}");
                Console.WriteLine($"  Manifest:        {manifestPath} {(File.Exists(manifestPath) ? "" : "(missing)") }");
                Console.WriteLine($"  Global config:   {globalConfigPath} {(File.Exists(globalConfigPath) ? "" : "(missing)") }");
                Console.WriteLine();
                Console.WriteLine("Project:");
                if (configResult.RootDirectory is not null)
                {
                    Console.WriteLine($"  tusk.json:       {Path.Combine(configResult.RootDirectory, "tusk.json")}");
                    Console.WriteLine($"  PHP version:     {(string.IsNullOrWhiteSpace(projectPhpVersion) ? "(none)" : projectPhpVersion)}");
                    Console.WriteLine($"  Isolation:       {(projectHome is null ? "disabled (run `tusk isolate` to enable)" : "enabled")}");
                    if (projectHome is not null)
                    {
                        Console.WriteLine($"    Home:          {projectHome.HomePath}");
                        Console.WriteLine($"    php.ini:       {projectHome.IniPath}");
                        Console.WriteLine($"    conf.d:        {projectHome.ExtensionsPath}");
                    }
                }
                else
                {
                    Console.WriteLine("  tusk.json:       (none found in this directory or parents)");
                }
                Console.WriteLine();
                Console.WriteLine("PHP version resolution chain:");
                Console.WriteLine($"  Override (--php-version): {(overrideProvided ? overrideSpec : "(none)") }");
                Console.WriteLine($"  Project tusk.json:        {(string.IsNullOrWhiteSpace(projectPhpVersion) ? "(none)" : projectPhpVersion)}");
                Console.WriteLine($"  Global default:           {(string.IsNullOrWhiteSpace(globalDefaultVersion) ? "(none)" : globalDefaultVersion)}");
                Console.WriteLine("  Fallback:                 system");
                Console.WriteLine($"  => Final resolved:        {finalVersion}");
                Console.WriteLine();
                Console.WriteLine("PHP binary:");
                if (phpBinaryPath is not null)
                {
                    Console.WriteLine($"  Path:            {phpBinaryPath}");
                }
                else
                {
                    Console.WriteLine("  Path:            (not found)");
                    if (!string.Equals(finalVersion, "system", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("  Hint:            run `tusk install " + finalVersion + "`");
                    }
                    else
                    {
                        Console.WriteLine("  Hint:            ensure `php` is on your PATH.");
                    }
                }
                Console.WriteLine();
                Console.WriteLine("Installed PHP versions (for this platform):");
                if (installed.Count == 0)
                {
                    Console.WriteLine("  (none)");
                }
                else
                {
                    foreach (var v in installed.OrderBy(v => v.Value))
                    {
                        string marker = string.Equals(v.Value, finalVersion, StringComparison.OrdinalIgnoreCase)
                            ? " (active)"
                            : "";
                        Console.WriteLine($"  {v.Value}{marker}");
                    }
                }
                Console.WriteLine();
                Console.WriteLine("Composer:");
                Console.WriteLine($"  composer.phar:   {(composerPhar ?? "(not found)")}");
                Console.WriteLine($"  system composer: {(composerExe ?? "(not found)")}");
                Console.WriteLine();
            }).ConfigureAwait(false);
        });

        return command;
    }
}
