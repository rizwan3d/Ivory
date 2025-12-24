using Ivory.Application.Composer;
using Ivory.Application.Php;
using Ivory.Application.Scaffolding;
using Ivory.Cli.Exceptions;
using Ivory.Cli.Execution;
using Ivory.Cli.Formatting;
using Ivory.Cli.Helpers;
using Ivory.Domain.Config;
using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ivory.Cli.Commands;

internal static class InitCommand
{
    public static Command Create(
        IPhpVersionResolver resolver,
        Option<string> phpVersionOption,
        IPublicIndexScaffolder publicIndexScaffolder,
        IComposerService composerService)
    {
        var frameworkOption = new Option<FrameworkKind>("--framework")
        {
            Description = "Framework preset to use when generating ivory.json (Generic, Laravel, Symfony).",
            Arity = ArgumentArity.ZeroOrOne
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite existing ivory.json if it already exists."
        };

        var command = new Command("init", "Create a starter ivory.json for this project.")
        {
            frameworkOption,
            forceOption
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("init", async context =>
            {
                FrameworkKind framework = parseResult.GetValue(frameworkOption);
                if (!Enum.IsDefined(framework))
                {
                    throw new IvoryCliException($"Unknown framework '{framework}'.");
                }

                bool force = parseResult.GetValue(forceOption);

                string cwd = Environment.CurrentDirectory;
                string ivoryPath = Path.Combine(cwd, "ivory.json");
                string ivoryLockPath = Path.Combine(cwd, "ivory.lock");
                string composerPath = Path.Combine(cwd, "composer.json");
                string composerLockPath = Path.Combine(cwd, "composer.lock");
                string publicIndexPath = Path.Combine(cwd, "public", "index.php");
                bool composerWasPresent = File.Exists(composerPath);
                bool composerCreated = false;

                if (File.Exists(ivoryPath) && !force)
                {
                    throw new IvoryCliException($"ivory.json already exists at {ivoryPath}. Use --force to overwrite.");
                }

                RegisterFileRollback(ivoryPath, context);

                bool willCopyLock = File.Exists(composerLockPath) && (!File.Exists(ivoryLockPath) || force);
                if (willCopyLock || File.Exists(ivoryLockPath))
                {
                    RegisterFileRollback(ivoryLockPath, context);
                }

                if (!File.Exists(publicIndexPath))
                {
                    context.OnRollback(() =>
                    {
                        if (File.Exists(publicIndexPath))
                        {
                            File.Delete(publicIndexPath);
                            var publicDir = Path.GetDirectoryName(publicIndexPath);
                            if (publicDir is not null &&
                                Directory.Exists(publicDir) &&
                                !Directory.EnumerateFileSystemEntries(publicDir).Any())
                            {
                                Directory.Delete(publicDir);
                            }
                        }
                    });
                }

                var config = IvoryConfigFactory.CreateFor(framework);

                string phpVersionSpec = parseResult.GetValue(phpVersionOption) ?? string.Empty;
                var detectedVersion = await VersionHelpers.DetectPhpVersionFromPhpVAsync(resolver, phpVersionSpec).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(detectedVersion) && composerWasPresent)
                {
                    detectedVersion = TryReadPhpVersionFromComposer(composerPath);
                }
                if (!string.IsNullOrWhiteSpace(detectedVersion))
                {
                    config.Php.Version = detectedVersion;
                }

                if (!composerWasPresent)
                {
                    await EnsureComposerJsonAsync(composerService, cwd, config.Php.Version ?? string.Empty).ConfigureAwait(false);
                    composerCreated = true;
                    RegisterFileRollback(composerPath, context);
                    RegisterFileRollback(composerLockPath, context);
                }

                string ivoryJson = File.Exists(composerPath)
                    ? await CreateIvoryJsonFromComposerAsync(config, composerService, cwd).ConfigureAwait(false)
                    : IvoryConfigSerialization.SerializeIvoryConfig(config);

                await File.WriteAllTextAsync(ivoryPath, ivoryJson).ConfigureAwait(false);

                if (willCopyLock)
                {
                    File.Copy(composerLockPath, ivoryLockPath, overwrite: true);
                }

                publicIndexScaffolder.EnsureDefaultPublicIndex(cwd);

                if (composerCreated)
                {
                    if (File.Exists(composerPath))
                    {
                        File.Delete(composerPath);
                    }

                    if (File.Exists(composerLockPath))
                    {
                        File.Delete(composerLockPath);
                    }
                }

                CliConsole.Success($"Created ivory.json for framework '{framework}' in {cwd}");
            }).ConfigureAwait(false);
        });

        return command;
    }

    private static async Task<string> CreateIvoryJsonFromComposerAsync(IvoryConfig config, IComposerService composerService, string cwd)
    {

        using var composerDoc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(cwd, "composer.json")).ConfigureAwait(false));
        var root = composerDoc.RootElement;

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        writer.WriteStartObject();

        foreach (var prop in root.EnumerateObject())
        {
           
            writer.WritePropertyName(prop.Name);
            prop.Value.WriteTo(writer);
        }
    
        writer.WriteEndObject();
        await writer.FlushAsync().ConfigureAwait(false);

        // If composer.json exists, install dependencies via Composer after writing ivory.json.
        var exit = await composerService.RunComposerAsync(
            ["install"],
            phpVersionSpec: config.Php.Version ?? string.Empty,
            config,
            cwd,
            CancellationToken.None).ConfigureAwait(false);
        if (exit != 0)
        {
            Console.WriteLine("[ivory] Composer install failed; ivory.json still generated.");
        }

        return Encoding.UTF8.GetString(stream.ToArray()); ;
    }

    private static async Task EnsureComposerJsonAsync(IComposerService composerService, string cwd, string phpVersionSpec)
    {
        string dirName = new DirectoryInfo(cwd).Name;
        string packageName = $"app/{SanitizeName(dirName)}";

        var exit = await composerService.RunComposerAsync(
            ["init",
             "--no-interaction",
             $"--name={packageName}",
             "--description=",
             "--license=proprietary"],
            phpVersionSpec,
            null,
            cwd,
            CancellationToken.None).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new IvoryCliException("composer init failed; cannot create ivory.json without composer.json");
        }
    }

    private static string? TryReadPhpVersionFromComposer(string composerPath)
    {
#pragma warning disable CA1031 // Do not catch general exception types
        try
        {
            using var composerDoc = JsonDocument.Parse(File.ReadAllText(composerPath));
            var root = composerDoc.RootElement;

            if (root.TryGetProperty("config", out var configElem) &&
                configElem.ValueKind == JsonValueKind.Object &&
                configElem.TryGetProperty("platform", out var platformElem) &&
                platformElem.ValueKind == JsonValueKind.Object &&
                platformElem.TryGetProperty("php", out var platformPhp) &&
                platformPhp.ValueKind == JsonValueKind.String)
            {
                var spec = platformPhp.GetString();
                if (TryParseComposerPhpSpec(spec, out var version))
                {
                    return version;
                }
            }

            if (root.TryGetProperty("require", out var requireElem) &&
                requireElem.ValueKind == JsonValueKind.Object &&
                requireElem.TryGetProperty("php", out var requirePhp) &&
                requirePhp.ValueKind == JsonValueKind.String)
            {
                var spec = requirePhp.GetString();
                if (TryParseComposerPhpSpec(spec, out var version))
                {
                    return version;
                }
            }
        }
        catch
        {
            // Best-effort composer fallback.
        }
#pragma warning restore CA1031 // Do not catch general exception types

        return null;
    }

    private static bool TryParseComposerPhpSpec(string? spec, out string version)
    {
        version = string.Empty;
        if (string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        var match = Regex.Match(spec, @"(?<v>\d+(\.\d+){0,2})");
        if (!match.Success)
        {
            return false;
        }

        version = match.Groups["v"].Value;
        return !string.IsNullOrWhiteSpace(version);
    }

    private static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "project";

        var sanitized = new string(name
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());

        while (sanitized.Contains("--"))
            sanitized = sanitized.Replace("--", "-");

        return sanitized.Trim('-');
    }

    private static void RegisterFileRollback(string path, CommandExecutionContext context)
    {
        bool existed = File.Exists(path);
        byte[]? originalBytes = existed ? File.ReadAllBytes(path) : null;

        context.OnRollback(async () =>
        {
            if (existed && originalBytes is not null)
            {
                await File.WriteAllBytesAsync(path, originalBytes).ConfigureAwait(false);
            }
            else if (!existed && File.Exists(path))
            {
                File.Delete(path);
            }
        });
    }
}

