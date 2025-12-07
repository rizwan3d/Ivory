using System.CommandLine;
using System.CommandLine.Invocation;
using Tusk.Cli.Execution;
using Tusk.Cli.Exceptions;
using Tusk.Cli.Formatting;
using Tusk.Application.Php;
using Tusk.Domain.Php;

namespace Tusk.Cli.Commands;

internal static class InstallCommand
{
    public static Command Create(IPhpInstaller installer)
    {
        var versionArgument = new Argument<string>("version")
        {
            Description = "PHP version (e.g. 8.3, 8.2, latest)."
        };
        var ignoreChecksumOption = new Option<bool>("--ignore-checksum")
        {
            Description = "Skip archive checksum validation (use with caution).",
            DefaultValueFactory = (e) => true
        };
        var command = new Command("install", "Install a PHP runtime version.")
        {
            versionArgument,
            ignoreChecksumOption
        };
        command.Aliases.Add("i");

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync(async context =>
            {
                var versionText = parseResult.GetValue(versionArgument);
                if (string.IsNullOrWhiteSpace(versionText))
                {
                    throw new TuskCliException("Version cannot be empty.");
                }
                bool ignoreChecksum = parseResult.GetValue(ignoreChecksumOption);
                var version = new PhpVersion(versionText);

                context.OnRollback(async () =>
                {
                    CliConsole.Warning($"Rolling back installation of PHP {version}...");
                    await installer.UninstallAsync(version).ConfigureAwait(false);
                });

                CliConsole.Info($"Installing PHP {version}...");
                await installer.InstallAsync(version, ignoreChecksum).ConfigureAwait(false);
                CliConsole.Success($"PHP {version} installed.");
            }).ConfigureAwait(false);
        });

        return command;
    }
}
