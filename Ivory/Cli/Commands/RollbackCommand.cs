using System.CommandLine;
using Ivory.Application.Deploy;
using Ivory.Cli.Deploy;
using Ivory.Cli.Execution;
using Ivory.Cli.Exceptions;
using Ivory.Cli.Formatting;
using Ivory.Cli.Helpers;
using Ivory.Application.Config;

namespace Ivory.Cli.Commands;

internal static class RollbackCommand
{
    public static Command Create(IDeployApiClient apiClient, IDeployConfigStore configStore, IProjectConfigProvider configProvider)
    {
        var orgOption = new Option<string>("--org")
        {
            Description = "Org name to rollback."
        };

        var projectOption = new Option<string>("--project")
        {
            Description = "Project name to rollback."
        };

        var targetOption = new Option<Guid>("--target-deployment-id")
        {
            Description = "Deployment id to rollback to."
        };

        var apiUrlOption = new Option<string>("--api-url")
        {
            Description = "Override API base URL for this command."
        };

        var userEmailOption = new Option<string>("--user-email")
        {
            Description = "Override user email for this command."
        };

        var command = new Command("rollback", "Create a rollback deployment that targets a previous deployment.")
        {
            orgOption,
            projectOption,
            targetOption,
            apiUrlOption,
            userEmailOption
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("rollback", async _ =>
            {
                var session = await DeploySessionResolver.ResolveAsync(
                    configStore,
                    parseResult.GetValue(apiUrlOption),
                    parseResult.GetValue(userEmailOption)).ConfigureAwait(false);

                var (orgName, projectName) = await ProjectIdentityResolver.ResolveAsync(
                    configProvider,
                    parseResult.GetValue(orgOption),
                    parseResult.GetValue(projectOption)).ConfigureAwait(false);
                var targetId = parseResult.GetValue(targetOption);

                if (targetId == Guid.Empty)
                {
                    throw new IvoryCliException("--target-deployment-id is required.");
                }

                var result = await apiClient.RollbackAsync(session, orgName, projectName, targetId).ConfigureAwait(false);
                CliConsole.Success($"Rollback deployment created for {orgName}/{projectName}: {result.Id}");
            }).ConfigureAwait(false);
        });

        return command;
    }
}
