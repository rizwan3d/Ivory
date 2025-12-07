using Tusk.Cli.Exceptions;

namespace Tusk.Cli.Execution;

/// <summary>
/// Executes a command with rollback support and wraps exceptions so they can be shown consistently.
/// </summary>
internal static class CommandExecutor
{
    public static async Task RunAsync(Func<CommandExecutionContext, Task> action)
    {
        var context = new CommandExecutionContext();

        try
        {
            await action(context).ConfigureAwait(false);
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
            throw wrapped;
        }
    }
}
