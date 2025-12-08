namespace Tusk.Application.Diagnostics;

public interface ITelemetryService
{
    bool IsEnabled { get; }
    Task RecordCommandAsync(string commandName, bool success, CancellationToken cancellationToken = default);
}
