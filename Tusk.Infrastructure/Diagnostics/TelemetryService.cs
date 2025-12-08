using System.Text.Json;
using Tusk.Application.Diagnostics;
using SysEnv = System.Environment;

namespace Tusk.Infrastructure.Diagnostics;

public sealed class TelemetryService : ITelemetryService
{
    private readonly string _logPath;
    public bool IsEnabled { get; }

    public TelemetryService()
    {
        var home = SysEnv.GetFolderPath(SysEnv.SpecialFolder.UserProfile);
        var tuskHome = Path.Combine(home, ".tusk");
        var logDir = Path.Combine(tuskHome, "logs");
        Directory.CreateDirectory(logDir);
        _logPath = Path.Combine(logDir, "telemetry.log");

        bool optInFile = File.Exists(Path.Combine(tuskHome, "telemetry.optin"));
        bool optInEnv = string.Equals(SysEnv.GetEnvironmentVariable("TUSK_TELEMETRY"), "1", StringComparison.Ordinal);
        IsEnabled = optInFile || optInEnv;
    }

    public Task RecordCommandAsync(string commandName, bool success, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        var payload = new
        {
            ts = DateTimeOffset.UtcNow,
            command = commandName,
            success
        };

        var json = JsonSerializer.Serialize(payload);
        File.AppendAllText(_logPath, json + SysEnv.NewLine);
        return Task.CompletedTask;
    }
}
