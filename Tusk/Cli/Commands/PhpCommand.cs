using System.CommandLine;
using System.CommandLine.Invocation;
using Tusk.Application.Php;
using Tusk.Cli.Execution;
using Tusk.Cli.Formatting;

namespace Tusk.Cli.Commands;

internal static class PhpCommand
{
    public static Command Create(IPhpRuntimeService runtime, Option<string> phpVersionOption)
    {
        var phpArgs = new Argument<string[]>("args")
        {
            Description = "Arguments to pass to `php`.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("php", "Run the resolved PHP binary with the given arguments.")
        {
            phpArgs
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync(async _ =>
            {
                string phpVersionSpec = parseResult.GetValue(phpVersionOption) ?? string.Empty;
                string[] argsToPhp = parseResult.GetValue(phpArgs) ?? Array.Empty<string>();
                CliConsole.Info($"php (version={phpVersionSpec}) {string.Join(' ', argsToPhp)}");
                int exitCode = await runtime.RunPhpAsync(null, argsToPhp, phpVersionSpec).ConfigureAwait(false);
                CliConsole.Success($"PHP exited with code {exitCode}.");
            }).ConfigureAwait(false);
        });

        return command;
    }
}
