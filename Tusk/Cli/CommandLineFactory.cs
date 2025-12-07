using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Tusk.Cli.Commands;
using Tusk.Application.Composer;
using Tusk.Application.Config;
using Tusk.Application.Environment;
using Tusk.Application.Php;
using Tusk.Application.Scaffolding;
using Tusk.Domain.Php;
using Tusk.Infrastructure.Php;
using System.Diagnostics.CodeAnalysis;

namespace Tusk.Cli;

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

        PhpVersion phpVersion = await resolver.ResolveForCurrentDirectoryAsync().ConfigureAwait(false);

        var phpVersionOption = new Option<string>("--php-version")
        {
            Description = "PHP version to use (e.g. 8.3, 8.2).",
            DefaultValueFactory = _ => phpVersion.ToString()
        };
        phpVersionOption.Aliases.Add("-p");
        phpVersionOption.Recursive = true;

        var rootCommand = new RootCommand("Tusk - PHP runtime and version manager for PHP.");
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
        rootCommand.Subcommands.Add(DoctorCommand.Create(installer, resolver, phpVersionOption, configProvider, composerService, environmentProbe, projectPhpHomeProvider));
        rootCommand.Subcommands.Add(IsolateCommand.Create(projectPhpHomeProvider));

        return rootCommand;
    }
}
