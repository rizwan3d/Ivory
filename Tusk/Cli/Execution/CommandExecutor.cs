using Tusk.Application.Diagnostics;
using Tusk.Cli.Exceptions;

namespace Tusk.Cli.Execution;

internal static class CommandExecutor
{
    private static IAppLogger? _logger;
    private static ITelemetryService? _telemetry;

    public static void Configure(IAppLogger logger, ITelemetryService telemetry)
    {
        _logger = logger;
        _telemetry = telemetry;
    }

    public static async Task RunAsync(string commandName, Func<CommandExecutionContext, Task> action)
    {
        await RunAsyncInternal(commandName, action).ConfigureAwait(false);
    }

    public static async Task RunAsync(Func<CommandExecutionContext, Task> action)
    {
        await RunAsyncInternal("unknown", action).ConfigureAwait(false);
    }

    private static async Task RunAsyncInternal(string commandName, Func<CommandExecutionContext, Task> action)
    {
        var context = new CommandExecutionContext();

        try
        {
            _logger?.Info($"Command start: {commandName}");
            await action(context).ConfigureAwait(false);
            _logger?.Info($"Command success: {commandName}");
            if (_telemetry?.IsEnabled == true)
            {
                await _telemetry.RecordCommandAsync(commandName, true).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            var rollbackErrors = await context.RollbackAsync().ConfigureAwait(false);

            if (ex is TuskCliException cliEx)
            {
                cliEx.AddRollbackErrors(rollbackErrors);
                throw;
            }

            var wrapped = new TuskCliException(ex.Message, ex);
            wrapped.AddRollbackErrors(rollbackErrors);

            _logger?.Error($"Command failed: {commandName}", ex);
            if (_telemetry?.IsEnabled == true)
            {
                await _telemetry.RecordCommandAsync(commandName, false).ConfigureAwait(false);
            }
            throw wrapped;
        }
    }
}
