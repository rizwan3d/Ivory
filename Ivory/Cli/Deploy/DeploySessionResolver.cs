using Ivory.Application.Deploy;
using Ivory.Cli.Exceptions;

namespace Ivory.Cli.Deploy;

internal static class DeploySessionResolver
{
    public static async Task<DeploySession> ResolveAsync(
        IDeployConfigStore configStore,
        string? apiBaseOverride,
        string? userEmailOverride,
        CancellationToken cancellationToken = default)
    {
        var config = await configStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        var apiBase = apiBaseOverride
                      ?? Environment.GetEnvironmentVariable("IVORY_DEPLOY_API")
                      ?? config.ApiBaseUrl;

        if (string.IsNullOrWhiteSpace(apiBase))
        {
            throw new IvoryCliException("Missing API base URL. Pass --api-url or run 'iv login --api-url <url> --user-email <email>'.");
        }

        var userEmail = userEmailOverride
                        ?? Environment.GetEnvironmentVariable("IVORY_DEPLOY_USER_EMAIL")
                        ?? config.UserEmail;

        if (string.IsNullOrWhiteSpace(userEmail))
        {
            throw new IvoryCliException("Missing user email. Pass --user-email or run 'iv login --user-email <email>'.");
        }

        return new DeploySession(apiBase.Trim(), userEmail.Trim());
    }
}
