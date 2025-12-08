using System.CommandLine;
using Tusk.Application.Php;
using Tusk.Cli.Execution;
using Tusk.Cli.Exceptions;
using Tusk.Cli.Formatting;

namespace Tusk.Cli.Commands;

internal static class ScaffoldDockerCommand
{
    public static Command Create(IPhpVersionResolver resolver)
    {
        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite existing Dockerfile / docker-compose.yml."
        };

        var command = new Command("scaffold:docker", "Generate Dockerfile and docker-compose wired to the Tusk PHP version.\nExamples:\n  tusk scaffold:docker\n  tusk scaffold:docker --force")
        {
            forceOption
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("scaffold:docker", async _ =>
            {
                bool force = parseResult.GetValue(forceOption);
                string cwd = Environment.CurrentDirectory;
                var resolved = await resolver.ResolveForCurrentDirectoryAsync().ConfigureAwait(false);
                string phpVersion = string.Equals(resolved.Value, "system", StringComparison.OrdinalIgnoreCase)
                    ? "8.3"
                    : resolved.Value;

                string dockerfilePath = Path.Combine(cwd, "Dockerfile");
                string composePath = Path.Combine(cwd, "docker-compose.yml");

                WriteFile(dockerfilePath, force, DockerfileContent(phpVersion));
                WriteFile(composePath, force, ComposeContent());

                CliConsole.Success($"Wrote {dockerfilePath}");
                CliConsole.Success($"Wrote {composePath}");
                CliConsole.Info("Extend the Dockerfile to add required extensions (pecl/install) as needed.");
            }).ConfigureAwait(false);
        });

        return command;
    }

    private static void WriteFile(string path, bool force, string content)
    {
        if (File.Exists(path) && !force)
        {
            throw new TuskCliException($"{path} already exists. Pass --force to overwrite.");
        }

        File.WriteAllText(path, content);
        }

    private static string DockerfileContent(string phpVersion) => $"""
        # syntax=docker/dockerfile:1
        FROM php:{phpVersion}-cli

        WORKDIR /app
        COPY . .

        # Install extensions here if needed, e.g.:
        # RUN pecl install xdebug && docker-php-ext-enable xdebug

        ENV COMPOSER_ALLOW_SUPERUSER=1
        ENV APP_ENV=dev

        CMD ["php", "-S", "0.0.0.0:8000", "-t", "public"]
        """;

    private static string ComposeContent() => """
        version: "3.9"
        services:
          app:
            build: .
            ports:
              - "8000:8000"
            volumes:
              - .:/app
            command: php -S 0.0.0.0:8000 -t public
        """;
}
