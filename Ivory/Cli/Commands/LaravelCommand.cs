using System.CommandLine;
using System.CommandLine.Invocation;
using Ivory.Application.Laravel;
using Ivory.Cli.Execution;

namespace Ivory.Cli.Commands;

internal static class LaravelCommand
{
    public static Command Create(ILaravelService laravelService, Option<string> phpVersionOption)
    {
        var laravelArgs = new Argument<string[]>("args")
        {
            Description = "Arguments to pass to the Laravel installer.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("laravel", "Run the Laravel installer using the resolved PHP version.\nExamples:\n  ivory laravel new blog\n  ivory laravel --php 8.3 -- --version")
        {
            laravelArgs
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("laravel", async _ =>
            {
                string phpVersionSpec = parseResult.GetValue(phpVersionOption) ?? string.Empty;
                string[] argsToLaravel = parseResult.GetValue(laravelArgs) ?? Array.Empty<string>();
                await laravelService.RunLaravelAsync(argsToLaravel, phpVersionSpec, CancellationToken.None).ConfigureAwait(false);
            }).ConfigureAwait(false);
        });

        return command;
    }
}
