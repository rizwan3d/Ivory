using System.CommandLine;
using Ivory.Application.Deploy;
using Ivory.Cli.Deploy;
using Ivory.Cli.Execution;
using Ivory.Cli.Exceptions;
using Ivory.Cli.Formatting;
using Ivory.Cli.Helpers;
using Ivory.Application.Config;

namespace Ivory.Cli.Commands;

internal static class ProjectsCommand
{
    public static Command Create(IDeployApiClient apiClient, IDeployConfigStore configStore, IProjectConfigProvider configProvider)
    {
        var orgOption = new Option<string>("--org")
        {
            Description = "Org name.",
        };

        var list = new Command("list", "List projects in an org.")
        {
            orgOption
        };

        list.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("projects:list", async _ =>
            {
                var session = await DeploySessionResolver.ResolveAsync(configStore, parseResult.GetValue(CommonOptions.ApiUrl), parseResult.GetValue(CommonOptions.UserEmail)).ConfigureAwait(false);
                var orgName = await ResolveOrgAsync(apiClient, configProvider, session, parseResult.GetValue(orgOption)).ConfigureAwait(false);

                var projects = await apiClient.GetProjectsAsync(session, orgName).ConfigureAwait(false);
                if (projects.Count == 0)
                {
                    CliConsole.Info("No projects found.");
                    return;
                }

                foreach (var p in projects)
                {
                    Console.WriteLine($"- {p.Name} org={p.OrgName}");
                }
            }).ConfigureAwait(false);
        });

        var nameOption = new Option<string>("--name")
        {
            Description = "Project name."
        };
        var create = new Command("create", "Create a project in an org.")
        {
            orgOption,
            nameOption
        };

        create.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("projects:create", async _ =>
            {
                var session = await DeploySessionResolver.ResolveAsync(configStore, parseResult.GetValue(CommonOptions.ApiUrl), parseResult.GetValue(CommonOptions.UserEmail)).ConfigureAwait(false);
                var orgName = await ResolveOrgAsync(apiClient, configProvider, session, parseResult.GetValue(orgOption)).ConfigureAwait(false);
                var name = parseResult.GetValue(nameOption) ?? string.Empty;

                if (string.IsNullOrWhiteSpace(name)) throw new IvoryCliException("Project name is required.");

                var project = await apiClient.CreateProjectAsync(session, orgName, name).ConfigureAwait(false);
                CliConsole.Success($"Project created: {project.Name} ({project.Id}) in org {project.OrgName}");
            }).ConfigureAwait(false);
        });

        var command = new Command("projects", "Manage projects.");
        command.Options.Add(CommonOptions.ApiUrl);
        command.Options.Add(CommonOptions.UserEmail);
        command.Subcommands.Add(list);
        command.Subcommands.Add(create);
        return command;
    }

    private static async Task<string> ResolveOrgAsync(IDeployApiClient apiClient, IProjectConfigProvider configProvider, DeploySession session, string? provided)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            return provided!.Trim();
        }

        var orgs = await apiClient.GetOrgsAsync(session).ConfigureAwait(false);
        if (orgs.Count == 0)
        {
            throw new IvoryCliException("No orgs found. Create one first with 'iv orgs create --name <name>'.");
        }

        if (orgs.Count > 1)
        {
            try
            {
                return await ProjectIdentityResolver.ResolveOrgAsync(configProvider, null).ConfigureAwait(false);
            }
            catch (IvoryCliException)
            {
                var choices = string.Join(", ", orgs.Select(o => $"{o.OrgName}"));
                throw new IvoryCliException($"Org name is required when you belong to multiple orgs. Available: {choices}");
            }
        }

        return orgs[0].OrgName;
    }
}
