using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Ivory.Cli.Commands;
using Ivory.Application.Composer;
using Ivory.Application.Config;
using Ivory.Application.Environment;
using Ivory.Application.Diagnostics;
using Ivory.Application.Php;
using Ivory.Application.Scaffolding;
using Ivory.Application.Deploy;
using Ivory.Application.Laravel;
using Ivory.Domain.Php;
using Ivory.Infrastructure.Php;
using Ivory.Cli.Execution;
using System.Diagnostics.CodeAnalysis;

namespace Ivory.Cli;

internal static class CommandLineFactory
{
    public static async Task<RootCommand> CreateAsync(IServiceProvider services)
    {
        var installer = services.GetRequiredService<IPhpInstaller>();
        var resolver  = services.GetRequiredService<IPhpVersionResolver>();
        var runtime   = services.GetRequiredService<IPhpRuntimeService>();
        var configProvider = services.GetRequiredService<IProjectConfigProvider>();
        var composerService = services.GetRequiredService<IComposerService>();
        var environmentProbe = services.GetRequiredService<IEnvironmentProbe>();
        var publicIndexScaffolder = services.GetRequiredService<IPublicIndexScaffolder>();
        var windowsFeed = services.GetRequiredService<WindowsPhpFeed>();
        var projectPhpHomeProvider = services.GetRequiredService<IProjectPhpHomeProvider>();
        var manifest = services.GetRequiredService<PhpVersionsManifest>();
        var logger = services.GetRequiredService<IAppLogger>();
        var telemetry = services.GetRequiredService<ITelemetryService>();
        var deployClient = services.GetRequiredService<IDeployApiClient>();
        var deployConfigStore = services.GetRequiredService<IDeployConfigStore>();
        var laravelService = services.GetRequiredService<ILaravelService>();
        CommandExecutor.Configure(logger, telemetry);

        PhpVersion phpVersion = await resolver.ResolveForCurrentDirectoryAsync().ConfigureAwait(false);

        var phpVersionOption = new Option<string>("--php-version")
        {
            Description = "PHP version to use (e.g. 8.3, 8.2).",
            DefaultValueFactory = _ => phpVersion.ToString()
        };
        phpVersionOption.Aliases.Add("-p");
        phpVersionOption.Recursive = true;

        var rootCommand = new RootCommand("Ivory - PHP runtime and version manager for PHP.\nExamples:\n  ivory install 8.3\n  ivory run serve\n  ivory composer install\n  ivory doctor");
        rootCommand.Options.Add(phpVersionOption);

        rootCommand.Subcommands.Add(RunCommand.Create(runtime, phpVersionOption, configProvider));
        rootCommand.Subcommands.Add(ScriptsCommand.Create(configProvider));
        rootCommand.Subcommands.Add(ListCommand.Create(installer));
        rootCommand.Subcommands.Add(InstallCommand.Create(installer));
        rootCommand.Subcommands.Add(UninstallCommand.Create(installer));
        rootCommand.Subcommands.Add(PruneCommand.Create(installer));
        rootCommand.Subcommands.Add(AvailableCommand.Create(windowsFeed));
        rootCommand.Subcommands.Add(UseCommand.Create());
        rootCommand.Subcommands.Add(DefaultCommand.Create(resolver));
        rootCommand.Subcommands.Add(PhpCommand.Create(runtime, phpVersionOption));
        rootCommand.Subcommands.Add(InitCommand.Create(resolver, phpVersionOption, publicIndexScaffolder));
        rootCommand.Subcommands.Add(ComposerCommand.Create(composerService, phpVersionOption, configProvider));
        rootCommand.Subcommands.Add(LaravelCommand.Create(laravelService, phpVersionOption));
        rootCommand.Subcommands.Add(DoctorCommand.Create(installer, resolver, phpVersionOption, configProvider, composerService, environmentProbe, projectPhpHomeProvider));
        rootCommand.Subcommands.Add(IsolateCommand.Create(projectPhpHomeProvider));
        rootCommand.Subcommands.Add(CompletionCommand.Create(rootCommand, configProvider, manifest));
        rootCommand.Subcommands.Add(ScaffoldCiCommand.Create(resolver));
        rootCommand.Subcommands.Add(ScaffoldDockerCommand.Create(resolver));
        rootCommand.Subcommands.Add(RegisterCommand.Create(deployClient));
        rootCommand.Subcommands.Add(LoginCommand.Create(deployClient, deployConfigStore));
        rootCommand.Subcommands.Add(DeployCommand.Create(deployClient, deployConfigStore));
        rootCommand.Subcommands.Add(LogsCommand.Create(deployClient, deployConfigStore));
        rootCommand.Subcommands.Add(EnvCommand.Create(deployClient, deployConfigStore));
        rootCommand.Subcommands.Add(DomainsCommand.Create(deployClient, deployConfigStore));
        rootCommand.Subcommands.Add(ConfigCommand.Create(deployClient, deployConfigStore, configProvider));
        rootCommand.Subcommands.Add(RollbackCommand.Create(deployClient, deployConfigStore));
        rootCommand.Subcommands.Add(OrgsCommand.Create(deployClient, deployConfigStore));
        rootCommand.Subcommands.Add(ProjectsCommand.Create(deployClient, deployConfigStore));

        return rootCommand;
    }
}
