using System.CommandLine;
using Ivory.Application.Deploy;
using Ivory.Cli.Deploy;
using Ivory.Cli.Execution;
using Ivory.Cli.Exceptions;
using Ivory.Cli.Formatting;
using Ivory.Cli.Helpers;
using Ivory.Application.Config;

namespace Ivory.Cli.Commands;

internal static class DeployCommand
{
    public static Command Create(IDeployApiClient apiClient, IDeployConfigStore configStore, IProjectConfigProvider configProvider)
    {
        var orgOption = new Option<string>("--org")
        {
            Description = "Target org name."
        };

        var projectOption = new Option<string>("--project")
        {
            Description = "Target project name."
        };

        var environmentOption = new Option<DeploymentEnvironment>("--env")
        {
            Description = "Deployment environment (Production or Preview).",
            DefaultValueFactory = _ => DeploymentEnvironment.Production
        };

        var branchOption = new Option<string>("--branch")
        {
            Description = "Git branch to deploy."
        };

        var commitOption = new Option<string>("--commit")
        {
            Description = "Commit SHA to deploy."
        };

        var artifactOption = new Option<string>("--artifact")
        {
            Description = "Source artifact location."
        };

        var apiUrlOption = new Option<string>("--api-url")
        {
            Description = "Override API base URL for this command."
        };

        var userEmailOption = new Option<string>("--user-email")
        {
            Description = "Override user email for this command."
        };

        var command = new Command("deploy", "Create a deployment for a project.")
        {
            orgOption,
            projectOption,
            environmentOption,
            branchOption,
            commitOption,
            artifactOption,
            apiUrlOption,
            userEmailOption
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("deploy", async _ =>
            {
                var session = await DeploySessionResolver.ResolveAsync(
                    configStore,
                    parseResult.GetValue(apiUrlOption),
                    parseResult.GetValue(userEmailOption)).ConfigureAwait(false);

                var (orgName, projectName) = await ProjectIdentityResolver.ResolveAsync(
                    configProvider,
                    parseResult.GetValue(orgOption),
                    parseResult.GetValue(projectOption)).ConfigureAwait(false);

                var env = parseResult.GetValue(environmentOption);
                var branch = (parseResult.GetValue(branchOption) ?? string.Empty).Trim();
                var commit = (parseResult.GetValue(commitOption) ?? string.Empty).Trim();
                var artifact = (parseResult.GetValue(artifactOption) ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(branch) && string.IsNullOrWhiteSpace(commit))
                {
                    throw new IvoryCliException("Provide --branch or --commit for the deployment.");
                }

                string? artifactLocation = string.IsNullOrWhiteSpace(artifact) ? null : artifact;
                string? tempArchive = null;

                try
                {
                    if (artifactLocation is null)
                    {
                        CliConsole.Info("Creating deployment package (ignoring .gitignore entries when present)...");
                        tempArchive = await DeployPackager.CreateArchiveAsync(Directory.GetCurrentDirectory()).ConfigureAwait(false);

                        CliConsole.Info("Uploading artifact to deploy API...");
                        var uploaded = await apiClient.UploadArtifactAsync(session, orgName, projectName, ResolveVersion(branch, commit), tempArchive).ConfigureAwait(false);
                        artifactLocation = uploaded.Location;
                        CliConsole.Success($"Uploaded artifact {uploaded.Version}.");
                    }

                    if (string.IsNullOrWhiteSpace(artifactLocation))
                    {
                        throw new IvoryCliException("Failed to determine artifact location for deployment.");
                    }

                    var created = await apiClient.CreateDeploymentAsync(
                        session,
                        orgName,
                        projectName,
                        env,
                        string.IsNullOrWhiteSpace(branch) ? null : branch,
                        string.IsNullOrWhiteSpace(commit) ? null : commit,
                        artifactLocation).ConfigureAwait(false);

                    CliConsole.Success($"Deployment {created.Id} created for {orgName}/{projectName} ({created.Environment}, status {created.Status}).");
                }
                finally
                {
                    if (!string.IsNullOrWhiteSpace(tempArchive) && File.Exists(tempArchive))
                    {
                        try
                        {
                            File.Delete(tempArchive);
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                            // ignore cleanup failures
                        }
                    }
                }
            }).ConfigureAwait(false);
        });

        return command;
    }

    private static string ResolveVersion(string branch, string commit)
    {
        if (!string.IsNullOrWhiteSpace(commit))
        {
            return commit;
        }

        if (!string.IsNullOrWhiteSpace(branch))
        {
            return branch;
        }

        return $"deploy-{DateTime.UtcNow:yyyyMMddHHmmss}";
    }
}
