using System.Text;
using SysEnv = System.Environment;
using Tusk.Application.Diagnostics;

namespace Tusk.Infrastructure.Diagnostics;

public sealed class AppLogger : IAppLogger
{
    private readonly object _lock = new();
    public string LogFilePath { get; }

    public AppLogger()
    {
        var home = SysEnv.GetFolderPath(SysEnv.SpecialFolder.UserProfile);
        var tuskHome = Path.Combine(home, ".tusk");
        var logDir = Path.Combine(tuskHome, "logs");
        Directory.CreateDirectory(logDir);
        LogFilePath = Path.Combine(logDir, "tusk.log");
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Trace(string message) => Write("TRACE", message);

    public void Error(string message, Exception? ex = null)
    {
        var sb = new StringBuilder();
        sb.Append(message);
        if (ex is not null)
        {
            sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
        }
        Write("ERROR", sb.ToString());
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTimeOffset.UtcNow:O} [{level}] {message}";
        lock (_lock)
        {
            File.AppendAllText(LogFilePath, line + SysEnv.NewLine);
        }
    }
}
