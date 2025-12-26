using System.CommandLine;
using Ivory.Application.Deploy;
using Ivory.Cli.Deploy;
using Ivory.Cli.Execution;
using Ivory.Cli.Exceptions;
using Ivory.Cli.Formatting;
using Ivory.Cli.Helpers;
using Ivory.Application.Config;

namespace Ivory.Cli.Commands;

internal static class DomainsCommand
{
    public static Command Create(IDeployApiClient apiClient, IDeployConfigStore configStore, IProjectConfigProvider configProvider)
    {
        var orgOption = new Option<string>("--org")
        {
            Description = "Org name to list domains for."
        };

        var projectOption = new Option<string>("--project")
        {
            Description = "Project name to list domains for."
        };

        var apiUrlOption = new Option<string>("--api-url")
        {
            Description = "Override API base URL for this command."
        };

        var userEmailOption = new Option<string>("--user-email")
        {
            Description = "Override user email for this command."
        };

        var command = new Command("domains", "List domains bound to a project.")
        {
            orgOption,
            projectOption,
            apiUrlOption,
            userEmailOption
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("domains", async _ =>
            {
                var session = await DeploySessionResolver.ResolveAsync(
                    configStore,
                    parseResult.GetValue(apiUrlOption),
                    parseResult.GetValue(userEmailOption)).ConfigureAwait(false);

                var (orgName, projectName) = await ProjectIdentityResolver.ResolveAsync(
                    configProvider,
                    parseResult.GetValue(orgOption),
                    parseResult.GetValue(projectOption)).ConfigureAwait(false);

                var domains = await apiClient.GetDomainsAsync(session, orgName, projectName).ConfigureAwait(false);

                if (domains.Count == 0)
                {
                    CliConsole.Info("No domains found.");
                    return;
                }

                CliConsole.Success($"Domains for project {orgName}/{projectName}:");
                foreach (var domain in domains)
                {
                    var flags = new List<string>();
                    if (domain.IsWildcard) flags.Add("wildcard");
                    if (domain.ManagedCertificate) flags.Add("managed-cert");

                    var suffix = flags.Count > 0 ? $" ({string.Join(", ", flags)})" : string.Empty;
                    Console.WriteLine($"- {domain.Hostname}{suffix}");
                }
            }).ConfigureAwait(false);
        });

        return command;
    }
}
