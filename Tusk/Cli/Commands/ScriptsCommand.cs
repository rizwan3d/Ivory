using System.CommandLine;
using System.CommandLine.Invocation;
using Tusk.Application.Config;
using Tusk.Cli.Execution;
using Tusk.Cli.Formatting;
using Tusk.Domain.Config;

namespace Tusk.Cli.Commands;

internal static class ScriptsCommand
{
    public static Command Create(IProjectConfigProvider configProvider)
    {
        var command = new Command("scripts", "List available scripts from tusk.json");

        command.SetAction(async _ =>
        {
            await CommandExecutor.RunAsync("scripts", async _ =>
            {
                var result = await configProvider.LoadAsync(Environment.CurrentDirectory).ConfigureAwait(false);

                if (!result.Found)
                {
                    CliConsole.Warning("No tusk.json found in this directory or its parents.");
                    return;
                }

                if (result.Config!.Scripts.Count == 0)
                {
                    CliConsole.Warning($"tusk.json found at {result.RootDirectory}, but no scripts are defined.");
                    return;
                }

                CliConsole.Info($"Scripts in tusk.json at {result.RootDirectory}:");
                Console.WriteLine();

                int maxNameLen = result.Config.Scripts.Keys.Max(k => k.Length);

                foreach (var kvp in result.Config.Scripts.OrderBy(k => k.Key))
                {
                    var name   = kvp.Key;
                    var script = kvp.Value;
                    string paddedName = name.PadRight(maxNameLen);

                    Console.WriteLine($"  {paddedName}  {script.Description ?? ""}".TrimEnd());

                    var parts = new List<string> { "php" };

                    foreach (var ini in result.Config.Php.Ini)
                    {
                        parts.Add("-d");
                        parts.Add(ini);
                    }

                    parts.AddRange(result.Config.Php.Args);
                    parts.AddRange(script.PhpArgs);
                    parts.Add(script.PhpFile);
                    if (script.Args.Count > 0)
                    {
                        parts.AddRange(script.Args);
                    }

                    Console.WriteLine("             " + string.Join(' ', parts));
                    Console.WriteLine();
                }
            }).ConfigureAwait(false);
        });
        return command;
    }
}
