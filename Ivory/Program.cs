using Ivory.Application.Composer;
using Ivory.Application.Config;
using Ivory.Application.Deploy;
using Ivory.Application.Diagnostics;
using Ivory.Application.Environment;
using Ivory.Application.Laravel;
using Ivory.Application.Php;
using Ivory.Application.Runtime;
using Ivory.Application.Scaffolding;
using Ivory.Cli;
using Ivory.Infrastructure.Composer;
using Ivory.Infrastructure.Config;
using Ivory.Infrastructure.Deploy;
using Ivory.Infrastructure.Diagnostics;
using Ivory.Infrastructure.Environment;
using Ivory.Infrastructure.Http;
using Ivory.Infrastructure.Laravel;
using Ivory.Infrastructure.Php;
using Ivory.Infrastructure.Runtime;
using Ivory.Infrastructure.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;

namespace Ivory;

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
            builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
            builder.Logging.AddFilter("System.Net.Http.SocketsHttpHandler", LogLevel.None);
            builder.Logging.AddFilter("Microsoft.Extensions.Http.DefaultHttpClientFactory", LogLevel.None);
            ConfigureServices(builder.Services);

            using IHost host = builder.Build();
            GlobalExceptionHandler.Configure(host.Services.GetRequiredService<IAppLogger>());

            

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
        var ivoryHome = Path.Combine(home, ".ivory");
        Directory.CreateDirectory(ivoryHome);
        var manifestPath = Path.Combine(ivoryHome, "php-versions.json");

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
        services.AddSingleton<ILaravelService, LaravelService>();
        services.AddSingleton<IEnvironmentProbe, EnvironmentProbe>();
        services.AddSingleton<IPublicIndexScaffolder, PublicIndexScaffolder>();
        services.AddSingleton<IProjectPhpHomeProvider, ProjectPhpHomeProvider>();
        services.AddSingleton<IAppLogger, AppLogger>();
        services.AddSingleton<ITelemetryService, TelemetryService>();
        services.AddSingleton<IDeployApiClient, DeployApiClient>();
        services.AddSingleton<IDeployConfigStore, DeployConfigStore>();

        services.AddHttpClient(HttpClientNames.Default, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ivory-cli/1.0 (+https://github.com/)");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseCookies = false
        });
    }
}
