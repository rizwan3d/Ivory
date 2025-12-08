namespace Tusk.Application.Diagnostics;

public interface IAppLogger
{
    string LogFilePath { get; }

    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
    void Trace(string message);
}
