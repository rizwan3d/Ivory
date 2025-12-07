using System.CommandLine;
using System.CommandLine.Invocation;
using Tusk.Cli.Execution;
using Tusk.Cli.Exceptions;
using Tusk.Cli.Formatting;
using Tusk.Application.Php;
using Tusk.Domain.Php;

namespace Tusk.Cli.Commands;

internal static class UninstallCommand
{
    public static Command Create(IPhpInstaller installer)
    {
        var versionArgument = new Argument<string>("version")
        {
            Description = "PHP version to uninstall."
        };

        var command = new Command("uninstall", "Remove an installed PHP version safely.")
        {
            versionArgument
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync(async context =>
            {
                var versionText = parseResult.GetValue(versionArgument) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(versionText))
                {
                    throw new TuskCliException("Version cannot be empty.");
                }

                var version = new PhpVersion(versionText);
                var installed = await installer.ListInstalledAsync().ConfigureAwait(false);
                bool wasInstalled = installed.Any(v => string.Equals(v.Value, version.Value, StringComparison.OrdinalIgnoreCase));

                if (wasInstalled)
                {
                    context.OnRollback(async () =>
                    {
                        CliConsole.Warning($"Restoring PHP {version} after failed uninstall...");
                        await installer.InstallAsync(version).ConfigureAwait(false);
                    });
                }

                CliConsole.Info($"Removing PHP {version}...");
                await installer.UninstallAsync(version).ConfigureAwait(false);
                CliConsole.Success($"Removal of PHP {version} complete.");
            }).ConfigureAwait(false);
        });

        return command;
    }
}
