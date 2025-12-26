namespace Ivory.Application.Deploy;

public enum DeploymentEnvironment
{
    Preview,
    Production
}

public enum DeploymentStatus
{
    Pending,
    Running,
    Succeeded,
    Failed,
    RolledBack
}

public enum ConfigEnvironment
{
    Preview,
    Production
}

public enum DeploymentMode
{
    ServerlessContainer,
    Custom
}

public enum RoutingMode
{
    Static,
    Spa,
    Server
}

public sealed record DeploySession(string ApiBaseUrl, string UserEmail);

public sealed class DeployCliConfig
{
    public string? ApiBaseUrl { get; set; }
    public string? UserEmail { get; set; }
    public Guid? LastTokenId { get; set; }
    public string? LastTokenPrefix { get; set; }
    public string? LastTokenSecret { get; set; }
}

public sealed class LoginResult
{
    public Guid Id { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}

public sealed class DeploymentCreated
{
    public Guid Id { get; set; }
    public DeploymentEnvironment Environment { get; set; }
    public DeploymentStatus Status { get; set; }
}

public sealed class DeploymentLogInfo
{
    public DeploymentStatus Status { get; set; }
    public string? LogUrl { get; set; }
}

public sealed class EnvConfigResult
{
    public string EnvVarsJson { get; set; } = "{}";
    public string PhpVersion { get; set; } = string.Empty;
    public string InstallCommand { get; set; } = string.Empty;
    public string BuildCommand { get; set; } = string.Empty;
    public string ComposerInstallFlags { get; set; } = string.Empty;
    public string ComposerAuthJson { get; set; } = string.Empty;
    public string ComposerCacheDir { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public string RequiredExtensionsCsv { get; set; } = string.Empty;
    public string PhpIniOverridesJson { get; set; } = string.Empty;
    public DeploymentMode DeploymentMode { get; set; }
    public int MinInstances { get; set; }
    public int MaxInstances { get; set; }
    public int MaxConcurrency { get; set; }
    public double CpuLimitCores { get; set; }
    public int MemoryMb { get; set; }
    public string HealthCheckPath { get; set; } = string.Empty;
    public int ShutdownGraceSeconds { get; set; }
    public bool EphemeralFilesystem { get; set; }
    public bool EnableRequestDraining { get; set; }
    public bool AllowInternetEgress { get; set; }
    public string ExternalDbConnection { get; set; } = string.Empty;
    public bool EnableLogStreaming { get; set; }
    public int BuildLogRetentionDays { get; set; }
    public int RuntimeLogRetentionDays { get; set; }
    public bool MetricsEnabled { get; set; }
    public int MetricsRetentionDays { get; set; }
    public bool TracingEnabled { get; set; }
    public string? OtelEndpoint { get; set; }
    public string? OtelHeaders { get; set; }
    public bool ErrorReportingEnabled { get; set; }
    public string? SentryDsn { get; set; }
    public string WebRoot { get; set; } = string.Empty;
    public RoutingMode Routing { get; set; }
    public List<SecretKey> Secrets { get; set; } = [];
}

public sealed class SecretKey
{
    public string Key { get; set; } = string.Empty;
}

public sealed class DomainInfo
{
    public string Hostname { get; set; } = string.Empty;
    public bool IsWildcard { get; set; }
    public bool ManagedCertificate { get; set; }
}

public sealed class RollbackResult
{
    public Guid Id { get; set; }
}

public sealed class UploadedArtifact
{
    public Guid Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}
