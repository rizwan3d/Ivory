using System.CommandLine;
using Ivory.Application.Deploy;
using Ivory.Cli.Deploy;
using Ivory.Cli.Execution;
using Ivory.Cli.Exceptions;
using Ivory.Cli.Formatting;

namespace Ivory.Cli.Commands;

internal static class OrgsCommand
{
    public static Command Create(IDeployApiClient apiClient, IDeployConfigStore configStore)
    {
        var list = new Command("list", "List orgs you belong to.");
        list.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("orgs:list", async _ =>
            {
                var session = await DeploySessionResolver.ResolveAsync(configStore, parseResult.GetValue(CommonOptions.ApiUrl), parseResult.GetValue(CommonOptions.UserEmail)).ConfigureAwait(false);
                var orgs = await apiClient.GetOrgsAsync(session).ConfigureAwait(false);
                if (orgs.Count == 0)
                {
                    CliConsole.Info("No orgs found.");
                    return;
                }

                foreach (var org in orgs)
                {
                    Console.WriteLine($"- {org.OrgName} role={org.Role}");
                }
            }).ConfigureAwait(false);
        });

        var createName = new Option<string>("--name")
        {
            Description = "Org name."
        };
        var create = new Command("create", "Create an org.")
        {
            createName
        };
        create.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("orgs:create", async _ =>
            {
                var name = parseResult.GetValue(createName) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new IvoryCliException("Org name is required.");
                }

                var session = await DeploySessionResolver.ResolveAsync(configStore, parseResult.GetValue(CommonOptions.ApiUrl), parseResult.GetValue(CommonOptions.UserEmail)).ConfigureAwait(false);
                var org = await apiClient.CreateOrgAsync(session, name).ConfigureAwait(false);
                CliConsole.Success($"Org created: {org.OrgName} ({org.OrgId})");
            }).ConfigureAwait(false);
        });

        var command = new Command("orgs", "Manage orgs.");
        command.Options.Add(CommonOptions.ApiUrl);
        command.Options.Add(CommonOptions.UserEmail);
        command.Subcommands.Add(list);
        command.Subcommands.Add(create);
        return command;
    }
}

internal static class CommonOptions
{
    public static readonly Option<string> ApiUrl = new("--api-url")
    {
        Description = "Override API base URL."
    };

    public static readonly Option<string> UserEmail = new("--user-email")
    {
        Description = "Override user email."
    };

    static CommonOptions()
    {
        ApiUrl.Recursive = true;
        UserEmail.Recursive = true;
    }
}
