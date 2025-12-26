namespace Ivory.Application.Deploy;

public interface IDeployApiClient
{
    Task<LoginResult> LoginAsync(DeploySession session, string? tokenName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrgSummary>> GetOrgsAsync(DeploySession session, CancellationToken cancellationToken = default);
    Task<OrgSummary> CreateOrgAsync(DeploySession session, string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(DeploySession session, string orgName, CancellationToken cancellationToken = default);
    Task<ProjectSummary> CreateProjectAsync(DeploySession session, string orgName, string name, CancellationToken cancellationToken = default);

    Task<DeploymentCreated> CreateDeploymentAsync(
        DeploySession session,
        string orgName,
        string projectName,
        DeploymentEnvironment environment,
        string? branch,
        string? commitSha,
        string? artifactLocation,
        CancellationToken cancellationToken = default);

    Task<UploadedArtifact> UploadArtifactAsync(
        DeploySession session,
        string orgName,
        string projectName,
        string version,
        string archivePath,
        CancellationToken cancellationToken = default);

    Task<DeploymentLogInfo> GetLogsAsync(DeploySession session, Guid deploymentId, CancellationToken cancellationToken = default);

    Task<EnvConfigResult> GetEnvironmentAsync(DeploySession session, string orgName, string projectName, ConfigEnvironment environment, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DomainInfo>> GetDomainsAsync(DeploySession session, string orgName, string projectName, CancellationToken cancellationToken = default);

    Task<RollbackResult> RollbackAsync(DeploySession session, string orgName, string projectName, Guid targetDeploymentId, CancellationToken cancellationToken = default);

    Task<RegisterResult> RegisterUserAsync(string apiBaseUrl, string email, string password, CancellationToken cancellationToken = default);

    Task UpsertConfigAsync(
        DeploySession session,
        string orgName,
        string projectName,
        ConfigEnvironment environment,
        ConfigUpsertRequest request,
        CancellationToken cancellationToken = default);
}
