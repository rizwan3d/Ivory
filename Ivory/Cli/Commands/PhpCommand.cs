using System.CommandLine;
using System.CommandLine.Invocation;
using Ivory.Application.Php;
using Ivory.Cli.Execution;
using Ivory.Cli.Formatting;

namespace Ivory.Cli.Commands;

internal static class PhpCommand
{
    public static Command Create(IPhpRuntimeService runtime, Option<string> phpVersionOption)
    {
        var phpArgs = new Argument<string[]>("args")
        {
            Description = "Arguments to pass to `php`.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("php", "Run the resolved PHP binary with the given arguments.\nExamples:\n  ivory php -- -v\n  ivory php --php 8.3 -- -r \"echo 'hi';\"")
        {
            phpArgs
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("php", async _ =>
            {
                string phpVersionSpec = parseResult.GetValue(phpVersionOption) ?? string.Empty;
                string[] argsToPhp = parseResult.GetValue(phpArgs) ?? Array.Empty<string>();
                CliConsole.Info($"php (version={phpVersionSpec}) {string.Join(' ', argsToPhp)}");
                int exitCode = await runtime.RunPhpAsync(null, argsToPhp, phpVersionSpec).ConfigureAwait(false);

                if (exitCode == 0)
                {
                    CliConsole.Success($"PHP exited with code {exitCode}.");
                }
                else
                {
                    CliConsole.Error($"PHP exited with code {exitCode}.");
                }
            }).ConfigureAwait(false);
        });

        return command;
    }
}

