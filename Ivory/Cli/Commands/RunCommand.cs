using System.CommandLine;
using System.CommandLine.Invocation;
using Ivory.Application.Config;
using Ivory.Application.Php;
using Ivory.Cli.Execution;
using Ivory.Cli.Exceptions;
using Ivory.Cli.Formatting;
using Ivory.Domain.Config;
using Ivory.Application.Composer;

namespace Ivory.Cli.Commands;

internal static class RunCommand
{
    public static Command Create(
        IPhpRuntimeService runtime,
        Option<string> phpVersionOption,
        IProjectConfigProvider configProvider,
        IComposerService composerService)
    {
        var scriptArgument = new Argument<string>("script-or-file")
        {
            Description = "Composer script name or path to a PHP file."
        };

        var scriptArgsArgument = new Argument<string[]>("args")
        {
            Description = "Arguments to pass to the script.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("run", "Run a Composer script or a PHP file.\nExamples:\n  ivory run serve\n  ivory run public/index.php -- --flag=value")
        {
            scriptArgument,
            scriptArgsArgument
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("run", async _ =>
            {
                var phpVersionSpec = parseResult.GetValue(phpVersionOption) ?? string.Empty;
                var scriptOrFile = parseResult.GetValue(scriptArgument) ?? string.Empty;
                var extraArgs = parseResult.GetValue(scriptArgsArgument) ?? Array.Empty<string>();

                if (string.IsNullOrWhiteSpace(scriptOrFile))
                {
                    throw new IvoryCliException("You must provide a script name from ivory.json or a PHP file path.");
                }

                var configResult = await configProvider.LoadAsync(Environment.CurrentDirectory).ConfigureAwait(false);

                var filePath = scriptOrFile;
                if (!Path.IsPathRooted(filePath))
                {
                    filePath = Path.Combine(Environment.CurrentDirectory, filePath);
                }

                if (File.Exists(filePath))
                {
                    CliConsole.Info($"Running PHP file '{filePath}' (php={phpVersionSpec})");
                    await runtime.RunPhpAsync(filePath, extraArgs, phpVersionSpec).ConfigureAwait(false);
                    return;
                }

                CliConsole.Info($"Running Composer script '{scriptOrFile}' (php={phpVersionSpec})");

                var exitCode = await composerService.RunComposerScriptAsync(
                    scriptOrFile,
                    extraArgs,
                    phpVersionSpec,
                    configResult.Config,
                    configResult.RootDirectory,
                    CancellationToken.None).ConfigureAwait(false);

                if (exitCode != 0)
                {
                    throw new IvoryCliException($"Script '{scriptOrFile}' failed with exit code {exitCode}.");
                }
            }).ConfigureAwait(false);
        });

        return command;
    }
}
