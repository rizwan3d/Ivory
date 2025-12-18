using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Ivory.Application.Deploy;

namespace Ivory.Infrastructure.Deploy;

public sealed class DeployApiClient : IDeployApiClient
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private readonly JsonSerializerOptions _jsonOptionsNoEnum = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public Task<LoginResult> LoginAsync(DeploySession session, string? tokenName, CancellationToken cancellationToken = default)
    {
        return SendAsync<LoginResult>(
            session,
            HttpMethod.Post,
            "/cli/login",
            new { name = tokenName },
            cancellationToken);
    }

    public Task<IReadOnlyList<OrgSummary>> GetOrgsAsync(DeploySession session, CancellationToken cancellationToken = default)
    {
        return SendAsync<IReadOnlyList<OrgSummary>>(
            session,
            HttpMethod.Get,
            "/orgs",
            null,
            cancellationToken);
    }

    public Task<OrgSummary> CreateOrgAsync(DeploySession session, string name, CancellationToken cancellationToken = default)
    {
        return SendAsync<OrgSummary>(
            session,
            HttpMethod.Post,
            "/orgs",
            new { name },
            cancellationToken);
    }

    public Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(DeploySession session, Guid orgId, CancellationToken cancellationToken = default)
    {
        return SendAsync<IReadOnlyList<ProjectSummary>>(
            session,
            HttpMethod.Get,
            $"/orgs/{orgId}/projects",
            null,
            cancellationToken);
    }

    public Task<ProjectSummary> CreateProjectAsync(DeploySession session, Guid orgId, string name, CancellationToken cancellationToken = default)
    {
        return SendAsync<ProjectSummary>(
            session,
            HttpMethod.Post,
            $"/orgs/{orgId}/projects",
            new { name },
            cancellationToken);
    }

    public Task<DeploymentCreated> CreateDeploymentAsync(
        DeploySession session,
        Guid projectId,
        DeploymentEnvironment environment,
        string? branch,
        string? commitSha,
        string? artifactLocation,
        CancellationToken cancellationToken = default)
    {
        return SendAsync<DeploymentCreated>(
            session,
            HttpMethod.Post,
            "/cli/deploy",
            new
            {
                projectId,
                environment,
                branch,
                commitSha,
                artifactLocation
            },
            cancellationToken);
    }

    public async Task<UploadedArtifact> UploadArtifactAsync(
        DeploySession session,
        Guid projectId,
        string version,
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            throw new FileNotFoundException("Archive not found.", archivePath);
        }

        var baseUri = BuildBaseUri(session.ApiBaseUrl);
        var requestUri = new Uri(baseUri, "/cli/artifacts/upload");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(projectId.ToString()), "projectId");
        content.Add(new StringContent(version), "version");

        await using var stream = File.OpenRead(archivePath);
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", Path.GetFileName(archivePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = content
        };
        request.Headers.Add("X-User-Id", session.UserId.ToString());

        using var client = new HttpClient();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await SafeReadAsync(response, cancellationToken).ConfigureAwait(false);
            var message = $"API request failed ({(int)response.StatusCode} {response.ReasonPhrase}).";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                message += $" {detail}";
            }

            throw new InvalidOperationException(message);
        }

        if (response.Content is null)
        {
            throw new InvalidOperationException("API response was empty.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<UploadedArtifact>(responseStream, _jsonOptions, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            throw new InvalidOperationException("Failed to parse API response.");
        }

        return result;
    }

    public Task<DeploymentLogInfo> GetLogsAsync(DeploySession session, Guid deploymentId, CancellationToken cancellationToken = default)
    {
        return SendAsync<DeploymentLogInfo>(
            session,
            HttpMethod.Get,
            $"/cli/logs/{deploymentId}",
            null,
            cancellationToken);
    }

    public Task<EnvConfigResult> GetEnvironmentAsync(DeploySession session, Guid projectId, ConfigEnvironment environment, CancellationToken cancellationToken = default)
    {
        var envSegment = environment.ToString();
        return SendAsync<EnvConfigResult>(
            session,
            HttpMethod.Get,
            $"/cli/env/{projectId}/{envSegment}",
            null,
            cancellationToken);
    }

    public Task<IReadOnlyList<DomainInfo>> GetDomainsAsync(DeploySession session, Guid projectId, CancellationToken cancellationToken = default)
    {
        return SendDomainsAsync(session, projectId, cancellationToken);
    }

    public async Task UpsertConfigAsync(
        DeploySession session,
        Guid projectId,
        ConfigEnvironment environment,
        ConfigUpsertRequest request,
        CancellationToken cancellationToken = default)
    {
        var envSegment = environment.ToString();
        await SendAsync<object>(
            session,
            HttpMethod.Post,
            $"/projects/{projectId}/config/{envSegment}",
            request,
            cancellationToken,
            useEnumStrings: false).ConfigureAwait(false);
    }

    public Task<RollbackResult> RollbackAsync(DeploySession session, Guid projectId, Guid targetDeploymentId, CancellationToken cancellationToken = default)
    {
        return SendAsync<RollbackResult>(
            session,
            HttpMethod.Post,
            "/cli/rollback",
            new
            {
                projectId,
                targetDeploymentId
            },
            cancellationToken);
    }

    public async Task<RegisterResult> RegisterUserAsync(string apiBaseUrl, string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            throw new InvalidOperationException("API base URL is required.");
        }

        return await SendAsync<RegisterResult>(
            new DeploySession(apiBaseUrl, Guid.Empty), // no auth header needed for register
            HttpMethod.Post,
            "/users/register",
            new { email, password },
            cancellationToken,
            includeUserHeader: false).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<DomainInfo>> SendDomainsAsync(DeploySession session, Guid projectId, CancellationToken cancellationToken)
    {
        var domains = await SendAsync<List<DomainInfo>>(
            session,
            HttpMethod.Get,
            $"/cli/domains/{projectId}",
            null,
            cancellationToken).ConfigureAwait(false);

        return domains;
    }

    private async Task<T> SendAsync<T>(DeploySession session, HttpMethod method, string path, object? payload, CancellationToken cancellationToken, bool includeUserHeader = true, bool useEnumStrings = true)
    {
        var baseUri = BuildBaseUri(session.ApiBaseUrl);
        var requestUri = new Uri(baseUri, path);

        using var request = new HttpRequestMessage(method, requestUri);
        if (includeUserHeader)
        {
            request.Headers.Add("X-User-Id", session.UserId.ToString());
        }

        if (payload is not null)
        {
            var opts = useEnumStrings ? _jsonOptions : _jsonOptionsNoEnum;
            request.Content = JsonContent.Create(payload, options: opts);
        }

        using var client = new HttpClient();
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var detail = await SafeReadAsync(response, cancellationToken).ConfigureAwait(false);
            var message = $"API request failed ({(int)response.StatusCode} {response.ReasonPhrase}).";
            if (!string.IsNullOrWhiteSpace(detail))
            {
                message += $" {detail}";
            }

            throw new InvalidOperationException(message);
        }

        if (response.Content is null)
        {
            throw new InvalidOperationException("API response was empty.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var result = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            throw new InvalidOperationException("Failed to parse API response.");
        }

        return result;
    }

    private static Uri BuildBaseUri(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid API base URL '{baseUrl}'.");
        }

        return uri;
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
