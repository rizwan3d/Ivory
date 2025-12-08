using System.CommandLine;
using Tusk.Application.Php;
using Tusk.Cli.Execution;
using Tusk.Cli.Exceptions;
using Tusk.Cli.Formatting;

namespace Tusk.Cli.Commands;

internal static class ScaffoldCiCommand
{
    public static Command Create(IPhpVersionResolver resolver)
    {
        var targetOption = new Option<string>("--target")
        {
            Description = "CI target: github, gitlab, or both.",
            DefaultValueFactory = _ => "github"
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "Overwrite existing CI files."
        };

        var command = new Command("scaffold:ci", "Generate CI config wired to Tusk.\nExamples:\n  tusk scaffold:ci\n  tusk scaffold:ci --target gitlab --force")
        {
            targetOption,
            forceOption
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("scaffold:ci", async _ =>
            {
                string target = (parseResult.GetValue(targetOption) ?? "github").Trim().ToLowerInvariant();
                bool force = parseResult.GetValue(forceOption);

                var version = await resolver.ResolveForCurrentDirectoryAsync().ConfigureAwait(false);
                string versionText = string.Equals(version.Value, "system", StringComparison.OrdinalIgnoreCase)
                    ? "8.3"
                    : version.Value;

                bool wroteAny = false;

                if (target is "github" or "both")
                {
                    string path = Path.Combine(Environment.CurrentDirectory, ".github", "workflows", "tusk-ci.yml");
                    WriteFile(path, force, GitHubCi(versionText));
                    wroteAny = true;
                    CliConsole.Success($"Wrote {path}");
                }

                if (target is "gitlab" or "both")
                {
                    string path = Path.Combine(Environment.CurrentDirectory, ".gitlab-ci.yml");
                    WriteFile(path, force, GitLabCi(versionText));
                    wroteAny = true;
                    CliConsole.Success($"Wrote {path}");
                }

                if (!wroteAny)
                {
                    throw new TuskCliException("Unknown target. Use github, gitlab, or both.");
                }
            }).ConfigureAwait(false);
        });

        return command;
    }

    private static void WriteFile(string path, bool force, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(path) && !force)
        {
            throw new TuskCliException($"{path} already exists. Pass --force to overwrite.");
        }

        File.WriteAllText(path, content);
    }

    private static string GitHubCi(string version) => $"""
        name: Tusk CI

        on:
          push:
          pull_request:

        jobs:
          build:
            runs-on: ubuntu-latest
            steps:
              - uses: actions/checkout@v4
              - name: Setup .NET
                uses: actions/setup-dotnet@v4
                with:
                  dotnet-version: '10.0.x'
              - name: Restore
                run: dotnet restore
              - name: Build
                run: dotnet build --configuration Release
              - name: Tusk doctor
                run: dotnet run --project Tusk/Tusk.csproj -- doctor --json
              - name: PHP version check
                run: dotnet run --project Tusk/Tusk.csproj -- php --php {version} -- -v
        """;

    private static string GitLabCi(string version) => $"""
        stages:
          - build

        variables:
          DOTNET_VERSION: "10.0"

        build:
          stage: build
          image: mcr.microsoft.com/dotnet/sdk:10.0
          script:
            - dotnet restore
            - dotnet build --configuration Release
            - dotnet run --project Tusk/Tusk.csproj -- doctor --json
            - dotnet run --project Tusk/Tusk.csproj -- php --php {version} -- -v
        """;
}
