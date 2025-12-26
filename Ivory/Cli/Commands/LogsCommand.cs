using System.CommandLine;
using Ivory.Application.Deploy;
using Ivory.Cli.Deploy;
using Ivory.Cli.Execution;
using Ivory.Cli.Exceptions;
using Ivory.Cli.Formatting;

namespace Ivory.Cli.Commands;

internal static class LogsCommand
{
    public static Command Create(IDeployApiClient apiClient, IDeployConfigStore configStore)
    {
        var deploymentOption = new Option<Guid>("--deployment-id")
        {
            Description = "Deployment id to inspect."
        };

        var apiUrlOption = new Option<string>("--api-url")
        {
            Description = "Override API base URL for this command."
        };

        var userEmailOption = new Option<string>("--user-email")
        {
            Description = "Override user email for this command."
        };

        var command = new Command("logs", "Fetch deployment status and log URL.")
        {
            deploymentOption,
            apiUrlOption,
            userEmailOption
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("logs", async _ =>
            {
                var session = await DeploySessionResolver.ResolveAsync(
                    configStore,
                    parseResult.GetValue(apiUrlOption),
                    parseResult.GetValue(userEmailOption)).ConfigureAwait(false);

                var deploymentId = parseResult.GetValue(deploymentOption);
                if (deploymentId == Guid.Empty)
                {
                    throw new IvoryCliException("Deployment id is required.");
                }

                var info = await apiClient.GetLogsAsync(session, deploymentId).ConfigureAwait(false);

                CliConsole.Info($"Status: {info.Status}");
                CliConsole.Info($"Log URL: {info.LogUrl ?? "(not available)"}");
            }).ConfigureAwait(false);
        });

        return command;
    }
}
