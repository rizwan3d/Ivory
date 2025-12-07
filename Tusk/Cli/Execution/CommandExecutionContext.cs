using System.Diagnostics.CodeAnalysis;

namespace Tusk.Cli.Execution;

/// <summary>
/// Tracks rollback actions for a command so we can undo work if something fails.
/// </summary>
internal sealed class CommandExecutionContext
{
    private readonly Stack<Func<Task>> _rollbackActions = new();

    public void OnRollback(Func<Task> rollback)
    {
        ArgumentNullException.ThrowIfNull(rollback);
        _rollbackActions.Push(rollback);
    }

    public void OnRollback(Action rollback)
    {
        ArgumentNullException.ThrowIfNull(rollback);
        _rollbackActions.Push(() =>
        {
            rollback();
            return Task.CompletedTask;
        });
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Rollback must keep going to revert as much work as possible.")]
    internal async Task<IReadOnlyList<Exception>> RollbackAsync()
    {
        var errors = new List<Exception>();

        while (_rollbackActions.TryPop(out var rollback))
        {
            try
            {
                await rollback().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        return errors;
    }
}
