using System;
using System.IO;
using Ivory.Application.Config;
using Ivory.Cli.Exceptions;

namespace Ivory.Cli.Helpers;

internal static class ProjectIdentityResolver
{
    public static async Task<(string Org, string Project)> ResolveAsync(
        IProjectConfigProvider configProvider,
        string? orgOption,
        string? projectOption,
        CancellationToken cancellationToken = default)
    {
        var org = orgOption?.Trim();
        var project = projectOption?.Trim();
        if (!string.IsNullOrWhiteSpace(org) && !string.IsNullOrWhiteSpace(project))
        {
            return (org!, project!);
        }

        var config = await configProvider.LoadAsync(Environment.CurrentDirectory, cancellationToken).ConfigureAwait(false);
        if (config.Found && config.Config is not null)
        {
            org ??= config.Config.Org?.Trim();
            project ??= config.Config.Project?.Trim();
        }

        if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(project))
        {
            var source = config.Found && config.RootDirectory is not null
                ? $"Check {Path.Combine(config.RootDirectory, "ivory.json")} for org/project settings or provide --org and --project."
                : "Provide --org and --project or add them to ivory.json.";
            throw new IvoryCliException(source);
        }

        return (org!, project!);
    }

    public static async Task<string> ResolveOrgAsync(
        IProjectConfigProvider configProvider,
        string? orgOption,
        CancellationToken cancellationToken = default)
    {
        var org = orgOption?.Trim();
        if (!string.IsNullOrWhiteSpace(org))
        {
            return org!;
        }

        var config = await configProvider.LoadAsync(Environment.CurrentDirectory, cancellationToken).ConfigureAwait(false);
        if (config.Found && !string.IsNullOrWhiteSpace(config.Config?.Org))
        {
            return config.Config!.Org!.Trim();
        }

        var source = config.Found && config.RootDirectory is not null
            ? $"Provide --org or set org in {Path.Combine(config.RootDirectory, "ivory.json")}."
            : "Provide --org or set org in ivory.json.";
        throw new IvoryCliException(source);
    }
}
