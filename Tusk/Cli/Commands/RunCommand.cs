using System.CommandLine;
using System.CommandLine.Invocation;
using Tusk.Application.Config;
using Tusk.Application.Php;
using Tusk.Cli.Execution;
using Tusk.Cli.Exceptions;
using Tusk.Cli.Formatting;
using Tusk.Domain.Config;

namespace Tusk.Cli.Commands;

internal static class RunCommand
{
    public static Command Create(IPhpRuntimeService runtime, Option<string> phpVersionOption, IProjectConfigProvider configProvider)
    {
        var scriptArgument = new Argument<string>("script-or-file")
        {
            Description = "Script name (from tusk.json) or path to a PHP file."
        };

        var scriptArgsArgument = new Argument<string[]>("args")
        {
            Description = "Arguments to pass to the script.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("run", "Run a named script from tusk.json or a PHP file.")
        {
            scriptArgument,
            scriptArgsArgument
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync(async _ =>
            {
                var phpVersionSpec = parseResult.GetValue(phpVersionOption) ?? string.Empty;
                var scriptOrFile = parseResult.GetValue(scriptArgument) ?? string.Empty;
                var extraArgs = parseResult.GetValue(scriptArgsArgument) ?? Array.Empty<string>();

                if (string.IsNullOrWhiteSpace(scriptOrFile))
                {
                    throw new TuskCliException("You must provide a script name from tusk.json or a PHP file path.");
                }

                var configResult = await configProvider.LoadAsync(Environment.CurrentDirectory).ConfigureAwait(false);

                if (configResult.Config is not null &&
                    configResult.Config.Scripts.TryGetValue(scriptOrFile, out var script))
                {
                    CliConsole.Info($"Running script {scriptOrFile} from tusk.json (php={phpVersionSpec})");

                    var finalArgs = new List<string>();

                    foreach (var ini in configResult.Config.Php.Ini)
                    {
                        finalArgs.Add("-d");
                        finalArgs.Add(ini);
                    }

                    finalArgs.AddRange(configResult.Config.Php.Args);
                    finalArgs.AddRange(script.PhpArgs);

                    if (string.IsNullOrWhiteSpace(script.PhpFile))
                    {
                        throw new TuskCliException($"Script '{scriptOrFile}' in tusk.json is missing 'phpFile'.");
                    }

                    var phpFilePath = configResult.RootDirectory is not null
                        ? Path.Combine(configResult.RootDirectory, script.PhpFile)
                        : script.PhpFile;

                    finalArgs.Add(phpFilePath);
                    finalArgs.AddRange(script.Args);
                    finalArgs.AddRange(extraArgs);

                    await runtime.RunPhpAsync(null, finalArgs.ToArray(), phpVersionSpec).ConfigureAwait(false);
                }
                else
                {
                    CliConsole.Info($"Running PHP file '{scriptOrFile}' (php={phpVersionSpec})");
                    var filePath = scriptOrFile;
                    if (!Path.IsPathRooted(filePath))
                    {
                        filePath = Path.Combine(Environment.CurrentDirectory, filePath);
                    }

                    await runtime.RunPhpAsync(filePath, extraArgs, phpVersionSpec).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        });

        return command;
    }
}
