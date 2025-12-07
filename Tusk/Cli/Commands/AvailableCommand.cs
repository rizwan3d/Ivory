using System.CommandLine;
using System.CommandLine.Invocation;
using System.Diagnostics.CodeAnalysis;
using Tusk.Cli.Formatting;
using Tusk.Cli.Execution;
using Tusk.Infrastructure.Php;

namespace Tusk.Cli.Commands;

internal static class AvailableCommand
{
    public static Command Create(WindowsPhpFeed feed)
    {
        var jsonOption = new Option<bool>(name: "--json")
        {
            Description = "Output machine-readable JSON."
        };
        var command = new Command("available", "List available PHP versions (Windows 64-bit NTS feed).")
        {
            jsonOption
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync(async _ =>
            {
                bool asJson = parseResult.GetValue(jsonOption);
                var list = await feed.ListAsync().ConfigureAwait(false);

                if (asJson)
                {
                    var payload = list.Select(x => new { version = x.Version, file = x.File, sha256 = x.Sha }).ToArray();
                    ConsoleFormatter.PrintDoctor(payload, true);
                    return;
                }

                CliConsole.Info("Available PHP (Windows x64 NTS):");
                foreach (var item in list)
                {
                    CliConsole.Success($"  {item.Version} ({item.File})");
                }
            }).ConfigureAwait(false);
        });

        return command;
    }
}
