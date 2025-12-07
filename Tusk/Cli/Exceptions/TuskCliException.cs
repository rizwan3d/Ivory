using System.Collections.ObjectModel;

namespace Tusk.Cli.Exceptions;

/// <summary>
/// Represents an error that should be surfaced to the CLI with a friendly message.
/// Rollback errors can be attached so the global exception handler can report them.
/// </summary>
internal sealed class TuskCliException : Exception
{
    private readonly List<Exception> _rollbackErrors = [];

    public TuskCliException()
        : base("An error occurred while executing the command.")
    {
    }

    public TuskCliException(string message)
        : base(message)
    {
    }

    public TuskCliException(string message, Exception? inner)
        : base(message, inner)
    {
    }

    public IReadOnlyCollection<Exception> RollbackErrors => new ReadOnlyCollection<Exception>(_rollbackErrors);

    public void AddRollbackErrors(IEnumerable<Exception> errors)
    {
        foreach (var error in errors)
        {
            _rollbackErrors.Add(error);
        }
    }
}
