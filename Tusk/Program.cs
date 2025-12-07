using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using Tusk.Application.Composer;
using Tusk.Application.Config;
using Tusk.Application.Environment;
using Tusk.Application.Php;
using Tusk.Application.Runtime;
using Tusk.Application.Scaffolding;
using Tusk.Cli;
using Tusk.Infrastructure.Composer;
using Tusk.Infrastructure.Config;
using Tusk.Infrastructure.Environment;
using Tusk.Infrastructure.Php;
using Tusk.Infrastructure.Runtime;
using Tusk.Infrastructure.Scaffolding;

namespace Tusk;

internal static class Program
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Global handler should capture and display every CLI failure uniformly.")]
    public static async Task<int> Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception ex)
            {
                GlobalExceptionHandler.Handle(ex);
            }
        };

        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            ConfigureServices(builder.Services);

            using IHost host = builder.Build();

            RootCommand rootCommand = await CommandLineFactory.CreateAsync(host.Services).ConfigureAwait(false);

            return await rootCommand.Parse(args).InvokeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GlobalExceptionHandler.Handle(ex);
            return 1;
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var tuskHome = Path.Combine(home, ".tusk");
        Directory.CreateDirectory(tuskHome);
        var manifestPath = Path.Combine(tuskHome, "php-versions.json");

        PhpVersionsManifest manifest;
        if (File.Exists(manifestPath))
        {
            manifest = PhpVersionsManifest.LoadFromFile(manifestPath);
        }
        else
        {
            manifest = new PhpVersionsManifest();
        }

        services.AddSingleton(manifest);
        services.AddSingleton<WindowsPhpFeed>();
        services.AddSingleton<IPhpInstaller, PhpInstaller>();
        services.AddSingleton<IPhpVersionResolver, PhpVersionResolver>();
        services.AddSingleton<IPhpRuntimeService, PhpRuntimeService>();
        services.AddSingleton<IProcessRunner, ProcessExecution>();
        services.AddSingleton<IProjectConfigProvider, ProjectConfigProvider>();
        services.AddSingleton<IComposerService, ComposerService>();
        services.AddSingleton<IEnvironmentProbe, EnvironmentProbe>();
        services.AddSingleton<IPublicIndexScaffolder, PublicIndexScaffolder>();
    }
}
