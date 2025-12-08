using Tusk.Application.Diagnostics;
using Tusk.Cli.Exceptions;
using Tusk.Cli.Formatting;

namespace Tusk.Cli;

internal static class GlobalExceptionHandler
{
    private static string? _logPath;

    public static void Configure(IAppLogger logger)
    {
        _logPath = logger.LogFilePath;
    }

    public static void Handle(Exception exception)
    {
        var cliException = exception as TuskCliException
                           ?? new TuskCliException("An unexpected error occurred.", exception);

        var lines = new List<string>
        {
            cliException.Message
        };

        if (cliException.InnerException is not null &&
            cliException.InnerException != cliException &&
            !string.Equals(cliException.InnerException.Message, cliException.Message, StringComparison.Ordinal))
        {
            lines.Add(cliException.InnerException.Message);
        }

        if (cliException.RollbackErrors.Count > 0)
        {
            lines.Add("Rollback attempted but some steps failed:");
            lines.AddRange(cliException.RollbackErrors.Select(e => $"- {e.Message}"));
        }

        if (!string.IsNullOrWhiteSpace(_logPath))
        {
            lines.Add($"Log file: {_logPath}");
        }

        CliConsole.ErrorBlock("Command failed", lines);
    }
}
