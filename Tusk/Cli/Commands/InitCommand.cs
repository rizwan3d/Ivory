using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text;
using System.Text.Json;
using Tusk.Application.Php;
using Tusk.Application.Scaffolding;
using Tusk.Cli.Execution;
using Tusk.Cli.Exceptions;
using Tusk.Cli.Formatting;
using Tusk.Cli.Helpers;
using Tusk.Domain.Config;

namespace Tusk.Cli.Commands;

internal static class InitCommand
{
    public static Command Create(IPhpVersionResolver resolver, Option<string> phpVersionOption, IPublicIndexScaffolder publicIndexScaffolder)
    {
        var frameworkOption = new Option<FrameworkKind>("--framework")
        {
            Description = "Framework preset to use when generating tusk.json (Generic, Laravel, Symfony).",
            Arity = ArgumentArity.ZeroOrOne
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite existing tusk.json if it already exists."
        };

        var command = new Command("init", "Create a starter tusk.json for this project.")
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
                    throw new TuskCliException($"Unknown framework '{framework}'.");
                }

                bool force = parseResult.GetValue(forceOption);

                string cwd = Environment.CurrentDirectory;
                string tuskPath = Path.Combine(cwd, "tusk.json");
                string tuskLockPath = Path.Combine(cwd, "tusk.lock");
                string composerPath = Path.Combine(cwd, "composer.json");
                string composerLockPath = Path.Combine(cwd, "composer.lock");
                string publicIndexPath = Path.Combine(cwd, "public", "index.php");

                if (File.Exists(tuskPath) && !force)
                {
                    throw new TuskCliException($"tusk.json already exists at {tuskPath}. Use --force to overwrite.");
                }

                RegisterFileRollback(tuskPath, context);

                bool willCopyLock = File.Exists(composerLockPath) && (!File.Exists(tuskLockPath) || force);
                if (willCopyLock || File.Exists(tuskLockPath))
                {
                    RegisterFileRollback(tuskLockPath, context);
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

                var config = TuskConfigFactory.CreateFor(framework);

                string phpVersionSpec = parseResult.GetValue(phpVersionOption) ?? string.Empty;
                var detectedVersion = await VersionHelpers.DetectPhpVersionFromPhpVAsync(resolver, phpVersionSpec).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(detectedVersion))
                {
                    config.Php.Version = detectedVersion;
                }

                string tuskJson = File.Exists(composerPath)
                    ? await CreateTuskJsonFromComposerAsync(composerPath, config).ConfigureAwait(false)
                    : TuskConfigSerialization.SerializeTuskConfig(config);

                await File.WriteAllTextAsync(tuskPath, tuskJson).ConfigureAwait(false);

                if (willCopyLock)
                {
                    File.Copy(composerLockPath, tuskLockPath, overwrite: true);
                }

                publicIndexScaffolder.EnsureDefaultPublicIndex(cwd);

                CliConsole.Success($"Created tusk.json for framework '{framework}' in {cwd}");
            }).ConfigureAwait(false);
        });

        return command;
    }

    private static async Task<string> CreateTuskJsonFromComposerAsync(string composerPath, TuskConfig config)
    {
        using var composerDoc = JsonDocument.Parse(await File.ReadAllTextAsync(composerPath).ConfigureAwait(false));
        var root = composerDoc.RootElement;

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
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
                    if (extraProp.NameEquals("tusk"))
                        continue;

                    writer.WritePropertyName(extraProp.Name);
                    extraProp.Value.WriteTo(writer);
                }

                writer.WritePropertyName("tusk");
                TuskConfigSerialization.WriteTuskConfigObject(writer, config);

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
            writer.WritePropertyName("tusk");
            TuskConfigSerialization.WriteTuskConfigObject(writer, config);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        await writer.FlushAsync().ConfigureAwait(false);

        return Encoding.UTF8.GetString(stream.ToArray());
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
