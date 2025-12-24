using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Ivory.Application.Composer;
using Ivory.Application.Config;
using Ivory.Cli.Execution;
using Ivory.Cli.Formatting;
using Ivory.Domain.Config;

namespace Ivory.Cli.Commands;

internal static class ScriptsCommand
{
    public static Command Create(IProjectConfigProvider configProvider, IComposerService composerService)
    {
        var command = new Command("scripts", "List available scripts from ivory.json");

        command.SetAction(async _ =>
        {
            await CommandExecutor.RunAsync("scripts", async _ =>
            {
                var result = await configProvider.LoadAsync(Environment.CurrentDirectory).ConfigureAwait(false);
                var composerConfig = composerService.FindComposerConfig(result.RootDirectory);

                if (composerConfig is null)
                {
                    CliConsole.Warning("No composer.json (or ivory.json) found in this directory or its parents.");
                    return;
                }

                var scripts = ComposerScriptsReader.Read(composerConfig);
                if (scripts.Count == 0)
                {
                    CliConsole.Warning($"composer.json found at {composerConfig}, but no scripts are defined.");
                    return;
                }

                CliConsole.Info($"Scripts in composer.json at {composerConfig}:");
                Console.WriteLine();

                int maxNameLen = scripts.Keys.Max(k => k.Length);

                foreach (var kvp in scripts.OrderBy(k => k.Key))
                {
                    var name   = kvp.Key;
                    var commands = kvp.Value;
                    string paddedName = name.PadRight(maxNameLen);

                    Console.WriteLine($"  {paddedName}");

                    if (commands.Count == 0)
                    {
                        Console.WriteLine("             (no commands)");
                        Console.WriteLine();
                        continue;
                    }

                    foreach (var cmd in commands)
                    {
                        Console.WriteLine("             " + cmd);
                    }
                    Console.WriteLine();
                }
            }).ConfigureAwait(false);
        });
        return command;
    }
}

internal static class ComposerScriptsReader
{
    public static Dictionary<string, List<string>> Read(string composerJsonPath)
    {
        var scripts = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(composerJsonPath));
            if (doc.RootElement.TryGetProperty("scripts", out var scriptsElem) && scriptsElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in scriptsElem.EnumerateObject())
                {
                    var list = new List<string>();
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var v = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(v))
                        {
                            list.Add(v);
                        }
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in prop.Value.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String))
                        {
                            var v = item.GetString();
                            if (!string.IsNullOrWhiteSpace(v))
                            {
                                list.Add(v);
                            }
                        }
                    }
                    scripts[prop.Name] = list;
                }
            }
        }
        catch(JsonException e)
        {
            throw new InvalidDataException($"Failed to parse composer.json at {composerJsonPath}: {e.Message}", e);
        }



        return scripts;
    }
}

