using System.Text;

namespace Tusk.Cli.Formatting;

internal static class CliConsole
{
    private static readonly object _lock = new();

    public static void Info(string message) => WriteStyled(message, ConsoleColor.Cyan);

    public static void Success(string message) => WriteStyled(message, ConsoleColor.Green);

    public static void Warning(string message) => WriteStyled(message, ConsoleColor.Yellow, isError: false);

    public static void Error(string message) => WriteStyled(message, ConsoleColor.Red, isError: true);

    public static void ErrorBlock(string title, IEnumerable<string> details)
    {
        var builder = new StringBuilder();
        string separator = new string('-', Math.Max(24, title.Length + 6));
        builder.AppendLine(separator);
        builder.AppendLine($"! {title}");
        foreach (var detail in details.Where(d => !string.IsNullOrWhiteSpace(d)))
        {
            builder.Append("> ");
            builder.AppendLine(detail.Trim());
        }

        builder.Append(separator);
        WriteStyled(builder.ToString().TrimEnd('\r', '\n'), ConsoleColor.Red, isError: true);
    }

    private static void WriteStyled(string message, ConsoleColor color, bool isError = false)
    {
        lock (_lock)
        {
            var previous = Console.ForegroundColor;
            Console.ForegroundColor = color;
            var writer = isError ? Console.Error : Console.Out;
            writer.WriteLine($"[tusk] {message}");
            Console.ForegroundColor = previous;
        }
    }
}
