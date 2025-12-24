using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using System.Text.Json;
using Ivory.Application.Composer;
using Ivory.Application.Php;
using Ivory.Application.Scaffolding;
using Ivory.Cli.Execution;
using Ivory.Cli.Exceptions;
using Ivory.Cli.Formatting;
using Ivory.Cli.Helpers;
using Ivory.Domain.Config;

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
                bool composerExists = File.Exists(composerPath);

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
                if (!string.IsNullOrWhiteSpace(detectedVersion))
                {
                    config.Php.Version = detectedVersion;
                }

                if (!composerExists)
                {
                    await EnsureComposerJsonAsync(composerService, cwd, config.Php.Version ?? string.Empty).ConfigureAwait(false);
                }

                string ivoryJson = File.Exists(composerPath)
                    ? await CreateIvoryJsonFromComposerAsync(composerPath, config, composerService, cwd).ConfigureAwait(false)
                    : IvoryConfigSerialization.SerializeIvoryConfig(config);

                await File.WriteAllTextAsync(ivoryPath, ivoryJson).ConfigureAwait(false);

                if (willCopyLock)
                {
                    File.Copy(composerLockPath, ivoryLockPath, overwrite: true);
                }

                publicIndexScaffolder.EnsureDefaultPublicIndex(cwd);

                File.Delete(composerPath);
                File.Delete(composerLockPath);

                CliConsole.Success($"Created ivory.json for framework '{framework}' in {cwd}");
            }).ConfigureAwait(false);
        });

        return command;
    }

    private static async Task<string> CreateIvoryJsonFromComposerAsync(string composerPath, IvoryConfig config, IComposerService composerService, string cwd)
    {
        using var composerDoc = JsonDocument.Parse(await File.ReadAllTextAsync(composerPath).ConfigureAwait(false));
        var root = composerDoc.RootElement;

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        writer.WriteStartObject();

        bool hasExtra = false;
        JsonElement extraElement = default;

        if (root.TryGetProperty("extra", out var extraElem) &&
            extraElem.ValueKind == JsonValueKind.Object)
        {
            hasExtra = true;
            extraElement = extraElem;
        }

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.NameEquals("extra") && hasExtra)
            {
                writer.WritePropertyName("extra");
                writer.WriteStartObject();

                foreach (var extraProp in extraElement.EnumerateObject())
                {
                    if (extraProp.NameEquals("ivory"))
                        continue;

                    writer.WritePropertyName(extraProp.Name);
                    extraProp.Value.WriteTo(writer);
                }

                writer.WritePropertyName("ivory");
                IvoryConfigSerialization.WriteIvoryConfigObject(writer, config);

                writer.WriteEndObject();
            }
            else
            {
                writer.WritePropertyName(prop.Name);
                prop.Value.WriteTo(writer);
            }
        }

        if (!hasExtra)
        {
            writer.WritePropertyName("extra");
            writer.WriteStartObject();
            writer.WritePropertyName("ivory");
            IvoryConfigSerialization.WriteIvoryConfigObject(writer, config);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        await writer.FlushAsync().ConfigureAwait(false);

        var json = Encoding.UTF8.GetString(stream.ToArray());

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

        return json;
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

