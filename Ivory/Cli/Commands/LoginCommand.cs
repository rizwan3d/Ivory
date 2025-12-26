using System.CommandLine;
using Ivory.Application.Deploy;
using Ivory.Cli.Deploy;
using Ivory.Cli.Execution;
using Ivory.Cli.Formatting;

namespace Ivory.Cli.Commands;

internal static class LoginCommand
{
    public static Command Create(IDeployApiClient apiClient, IDeployConfigStore configStore)
    {
        var apiUrlOption = new Option<string>("--api-url")
        {
            Description = "Base URL of the Ivory deploy API (e.g. http://localhost:5000)."
        };

        var userEmailOption = new Option<string>("--user-email")
        {
            Description = "User email to authenticate as."
        };

        var nameOption = new Option<string>("--name")
        {
            Description = "Optional token name to create during login.",
            DefaultValueFactory = _ => "ivory-cli-token"
        };

        var command = new Command("login", "Authenticate Ivory CLI against the deploy API and store settings locally.")
        {
            apiUrlOption,
            userEmailOption,
            nameOption
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("login", async _ =>
            {
                var apiBase = parseResult.GetValue(apiUrlOption);
                var userEmail = parseResult.GetValue(userEmailOption);
                var tokenName = parseResult.GetValue(nameOption);

                var session = await DeploySessionResolver.ResolveAsync(configStore, apiBase, userEmail).ConfigureAwait(false);
                var login = await apiClient.LoginAsync(session, tokenName).ConfigureAwait(false);

                var config = await configStore.LoadAsync().ConfigureAwait(false);
                config.ApiBaseUrl = session.ApiBaseUrl;
                config.UserEmail = session.UserEmail;
                config.LastTokenId = login.Id;
                config.LastTokenPrefix = login.Prefix;
                config.LastTokenSecret = login.Secret;
                await configStore.SaveAsync(config).ConfigureAwait(false);

                CliConsole.Success($"Authenticated against {session.ApiBaseUrl} as user {session.UserEmail}.");
                CliConsole.Info($"Token prefix: {login.Prefix}");
                CliConsole.Warning("Token secret (store securely): " + login.Secret);
            }).ConfigureAwait(false);
        });

        return command;
    }
}
