namespace Ivory.Application.Deploy;

public interface IDeployApiClient
{
    Task<LoginResult> LoginAsync(DeploySession session, string? tokenName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrgSummary>> GetOrgsAsync(DeploySession session, CancellationToken cancellationToken = default);
    Task<OrgSummary> CreateOrgAsync(DeploySession session, string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(DeploySession session, Guid orgId, CancellationToken cancellationToken = default);
    Task<ProjectSummary> CreateProjectAsync(DeploySession session, Guid orgId, string name, CancellationToken cancellationToken = default);

    Task<DeploymentCreated> CreateDeploymentAsync(
        DeploySession session,
        Guid projectId,
        DeploymentEnvironment environment,
        string? branch,
        string? commitSha,
        string? artifactLocation,
        CancellationToken cancellationToken = default);

    Task<UploadedArtifact> UploadArtifactAsync(
        DeploySession session,
        Guid projectId,
        string version,
        string archivePath,
        CancellationToken cancellationToken = default);

    Task<DeploymentLogInfo> GetLogsAsync(DeploySession session, Guid deploymentId, CancellationToken cancellationToken = default);

    Task<EnvConfigResult> GetEnvironmentAsync(DeploySession session, Guid projectId, ConfigEnvironment environment, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DomainInfo>> GetDomainsAsync(DeploySession session, Guid projectId, CancellationToken cancellationToken = default);

    Task<RollbackResult> RollbackAsync(DeploySession session, Guid projectId, Guid targetDeploymentId, CancellationToken cancellationToken = default);

    Task<RegisterResult> RegisterUserAsync(string apiBaseUrl, string email, string password, CancellationToken cancellationToken = default);

    Task UpsertConfigAsync(
        DeploySession session,
        Guid projectId,
        ConfigEnvironment environment,
        ConfigUpsertRequest request,
        CancellationToken cancellationToken = default);
}
