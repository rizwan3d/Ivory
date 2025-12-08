using System.CommandLine;
using Tusk.Application.Config;
using Tusk.Cli.Execution;
using Tusk.Cli.Exceptions;
using Tusk.Cli.Formatting;
using Tusk.Domain.Config;
using Tusk.Infrastructure.Php;
using Tusk.Domain.Runtime;

namespace Tusk.Cli.Commands;

internal static class CompletionCommand
{
    public static Command Create(
        RootCommand rootCommand,
        IProjectConfigProvider configProvider,
        PhpVersionsManifest manifest)
    {
        var shellArgument = new Argument<string>("shell")
        {
            Description = "Target shell (bash, zsh, fish, powershell)."
        };

        var command = new Command("completion", "Generate a shell completion script for tusk.")
        {
            shellArgument
        };

        command.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("completion", async _ =>
            {
                var shell = (parseResult.GetValue(shellArgument) ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(shell))
                {
                    throw new TuskCliException("Please specify a shell: bash, zsh, fish, powershell.");
                }

                var allCommands = rootCommand.Subcommands
                    .SelectMany(c => new[] { c.Name }.Concat(c.Aliases))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var scripts = await LoadScriptsAsync(configProvider).ConfigureAwait(false);
                var versions = LoadVersions(manifest);

                var words = string.Join(" ",
                    allCommands
                        .Concat(scripts)
                        .Concat(versions)
                        .Select(EscapeWord));

                string script = shell switch
                {
                    "bash" => Bash(words),
                    "zsh" => Zsh(words),
                    "fish" => Fish(words),
                    "powershell" or "pwsh" => PowerShell(words),
                    _ => throw new TuskCliException($"Unknown shell '{shell}'. Use bash|zsh|fish|powershell.")
                };

                Console.WriteLine(script);
            }).ConfigureAwait(false);
        });

        return command;
    }

    private static async Task<IEnumerable<string>> LoadScriptsAsync(IProjectConfigProvider configProvider)
    {
        var result = await configProvider.LoadAsync(Environment.CurrentDirectory).ConfigureAwait(false);
        return result.Config?.Scripts.Keys ?? Enumerable.Empty<string>();
    }

    private static IEnumerable<string> LoadVersions(PhpVersionsManifest manifest)
    {
        var platform = PlatformId.DetectCurrent().Value;
        if (!manifest.Platforms.TryGetValue(platform, out var entry))
        {
            return Array.Empty<string>();
        }

        return entry.Versions.Keys
            .Concat(entry.Aliases.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);
    }

    private static string EscapeWord(string word) => word.Replace("\"", "\\\"");

    private static string Bash(string words) => """
        _tusk_complete() {
            local cur="${COMP_WORDS[COMP_CWORD]}"
            local words_str="REPLACE_WORDS"
            COMPREPLY=( $(compgen -W "${words_str}" -- "${cur}") )
            return 0
        }
        complete -F _tusk_complete tusk
        """ .Replace("REPLACE_WORDS", words);

    private static string Zsh(string words) => """
        #compdef tusk
        _tusk_complete() {
            local -a words
            words=(REPLACE_WORDS)
            _describe 'values' words
        }
        compdef _tusk_complete tusk
        """ .Replace("REPLACE_WORDS", words);

    private static string Fish(string words) => """
        complete -c tusk -f -a "REPLACE_WORDS"
        """ .Replace("REPLACE_WORDS", words);

    private static string PowerShell(string words) => """
        Register-ArgumentCompleter -Native -CommandName tusk -ScriptBlock {
            param($wordToComplete, $commandAst, $cursorPosition)
            $words = "REPLACE_WORDS".Split(" ", [System.StringSplitOptions]::RemoveEmptyEntries)
            foreach ($w in $words) {
                if ($w -like "$wordToComplete*") {
                    [System.Management.Automation.CompletionResult]::new($w, $w, 'ParameterValue', $w)
                }
            }
        }
        """ .Replace("REPLACE_WORDS", words);
}
