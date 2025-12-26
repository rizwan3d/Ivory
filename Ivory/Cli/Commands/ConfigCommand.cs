using System.CommandLine;
using System.IO;
using System.Text;
using System.Text.Json;
using Ivory.Application.Config;
using Ivory.Application.Deploy;
using Ivory.Cli.Deploy;
using Ivory.Cli.Execution;
using Ivory.Cli.Exceptions;
using Ivory.Cli.Formatting;
using Ivory.Domain.Config;
using System.Buffers;

namespace Ivory.Cli.Commands;

internal static class ConfigCommand
{
    private static readonly string[] RequiredExtensions = ["pdo", "mbstring", "openssl", "json", "curl", "ctype", "tokenizer", "xml"];

    public static Command Create(IDeployApiClient apiClient, IDeployConfigStore configStore, IProjectConfigProvider configProvider)
    {
        var orgOption = new Option<string>("--org")
        {
            Description = "Org name to sync configuration to."
        };

        var projectOption = new Option<string>("--project")
        {
            Description = "Project name to sync configuration to."
        };

        var envOption = new Option<ConfigEnvironment>("--env")
        {
            Description = "Configuration environment (Production or Preview).",
            DefaultValueFactory = _ => ConfigEnvironment.Production
        };

        var sync = new Command("sync", "Push ivory.json settings to the deploy API.")
        {
            orgOption,
            projectOption,
            envOption
        };

        sync.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("config:sync", async _ =>
            {
                var session = await DeploySessionResolver.ResolveAsync(configStore, parseResult.GetValue(CommonOptions.ApiUrl), parseResult.GetValue(CommonOptions.UserEmail)).ConfigureAwait(false);

                var orgName = (parseResult.GetValue(orgOption) ?? string.Empty).Trim();
                var projectName = (parseResult.GetValue(projectOption) ?? string.Empty).Trim();

                var env = parseResult.GetValue(envOption);

                var projectConfig = await configProvider.LoadAsync(Environment.CurrentDirectory).ConfigureAwait(false);
                if (!projectConfig.Found || projectConfig.Config is null || projectConfig.RootDirectory is null)
                {
                    throw new IvoryCliException("No ivory.json found. Run this command inside a project with ivory.json.");
                }

                if (string.IsNullOrWhiteSpace(orgName))
                {
                    orgName = projectConfig.Config.Org ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(projectName))
                {
                    projectName = projectConfig.Config.Project ?? string.Empty;
                }

                if (string.IsNullOrWhiteSpace(orgName) || string.IsNullOrWhiteSpace(projectName))
                {
                    throw new IvoryCliException($"Org and project names are required. Set them via --org/--project or in {Path.Combine(projectConfig.RootDirectory, "ivory.json")}.");
                }

                var request = BuildRequest(projectConfig.Config, projectConfig.RootDirectory);

                await apiClient.UpsertConfigAsync(session, orgName, projectName, env, request).ConfigureAwait(false);

                CliConsole.Success($"Synced ivory.json to project {orgName}/{projectName} ({env}).");
                Console.WriteLine($"PHP: {request.PhpVersion}");
                Console.WriteLine($"Extensions: {request.RequiredExtensionsCsv}");
                Console.WriteLine($"php.ini: {request.PhpIniOverridesJson}");
            }).ConfigureAwait(false);
        });

        var target = new Command("target", "Set org/project in ivory.json for deploy commands.")
        {
            orgOption,
            projectOption
        };

        target.SetAction(async parseResult =>
        {
            await CommandExecutor.RunAsync("config:target", async _ =>
            {
                var orgName = (parseResult.GetValue(orgOption) ?? string.Empty).Trim();
                var projectName = (parseResult.GetValue(projectOption) ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(orgName) || string.IsNullOrWhiteSpace(projectName))
                {
                    throw new IvoryCliException("Org and project are required. Provide --org and --project.");
                }

                var configResult = await configProvider.LoadAsync(Environment.CurrentDirectory).ConfigureAwait(false);
                if (!configResult.Found || configResult.RootDirectory is null)
                {
                    throw new IvoryCliException("No ivory.json found. Run inside a project that has ivory.json.");
                }

                var path = Path.Combine(configResult.RootDirectory, "ivory.json");
                var original = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                string json;
                try
                {
                    using var doc = JsonDocument.Parse(original);
                    json = WriteOrgProjectPatched(doc.RootElement, orgName, projectName);
                }
                catch (JsonException)
                {
                    throw new IvoryCliException($"Failed to parse {path}; fix the file or recreate it with 'ivory init --force'.");
                }

                await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)).ConfigureAwait(false);
                CliConsole.Success($"Updated org/project in {path} -> {orgName}/{projectName}.");
            }).ConfigureAwait(false);
        });

        var command = new Command("config", "Manage deploy configuration.");
        command.Options.Add(CommonOptions.ApiUrl);
        command.Options.Add(CommonOptions.UserEmail);
        command.Subcommands.Add(sync);
        command.Subcommands.Add(target);
        return command;
    }

    private static string WriteOrgProjectPatched(JsonElement root, string org, string project)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            var hasOrg = false;
            var hasProject = false;

            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("org"))
                {
                    writer.WriteString("org", org);
                    hasOrg = true;
                    continue;
                }

                if (prop.NameEquals("project"))
                {
                    writer.WriteString("project", project);
                    hasProject = true;
                    continue;
                }

                writer.WritePropertyName(prop.Name);
                WriteElement(writer, prop.Value);
            }

            if (!hasOrg)
            {
                writer.WriteString("org", org);
            }

            if (!hasProject)
            {
                writer.WriteString("project", project);
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);
                    WriteElement(writer, prop.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var l))
                {
                    writer.WriteNumberValue(l);
                }
                else if (element.TryGetDouble(out var d))
                {
                    writer.WriteNumberValue(d);
                }
                else
                {
                    writer.WriteRawValue(element.GetRawText());
                }
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                writer.WriteRawValue(element.GetRawText());
                break;
        }
    }

    private static ConfigUpsertRequest BuildRequest(IvoryConfig config, string rootDirectory)
    {
        var phpVersion = (config.Php.Version ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(phpVersion))
        {
            throw new IvoryCliException("php.version is required in ivory.json.");
        }

        var iniJson = BuildPhpIniOverrides(config.Php.Ini);
        var framework = DetectFramework(rootDirectory);

        const string install = "composer install --no-dev --no-interaction --prefer-dist";
        const string build = "composer dump-autoload -o";
        const string composerFlags = "--no-dev --no-interaction --prefer-dist";
        var extensionsCsv = string.Join(",", RequiredExtensions);

        return new ConfigUpsertRequest
        {
            EnvVarsJson = "{}",
            PhpVersion = phpVersion,
            InstallCommand = install,
            BuildCommand = build,
            ComposerInstallFlags = composerFlags,
            ComposerAuthJson = "{}",
            ComposerCacheDir = ".composer/cache",
            OpcacheEnabled = true,
            Framework = framework,
            RequiredExtensionsCsv = extensionsCsv,
            PhpIniOverridesJson = iniJson,
            DeploymentMode = DeploymentMode.ServerlessContainer,
            MinInstances = 0,
            MaxInstances = 5,
            MaxConcurrency = 20,
            CpuLimitCores = 0.25,
            MemoryMb = 512,
            HealthCheckPath = "/health",
            ShutdownGraceSeconds = 20,
            EphemeralFilesystem = true,
            EnableRequestDraining = true,
            AllowInternetEgress = true,
            ExternalDbConnection = string.Empty,
            EnableLogStreaming = true,
            BuildLogRetentionDays = 7,
            RuntimeLogRetentionDays = 14,
            MetricsEnabled = true,
            MetricsRetentionDays = 14,
            TracingEnabled = false,
            OtelEndpoint = null,
            OtelHeaders = null,
            ErrorReportingEnabled = false,
            SentryDsn = null,
            WebRoot = "public",
            Routing = RoutingMode.Server
        };
    }

    private static string BuildPhpIniOverrides(IEnumerable<string> entries)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in entries ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var parts = raw.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                throw new IvoryCliException($"Invalid php.ini override '{raw}'. Expected key=value.");
            }

            map[parts[0]] = parts[1];
        }

        if (map.Count == 0)
        {
            return "{}";
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var kvp in map)
            {
                writer.WriteString(kvp.Key, kvp.Value);
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string DetectFramework(string rootDirectory)
    {
        var artisan = Path.Combine(rootDirectory, "artisan");
        if (File.Exists(artisan)) return "laravel";

        var symfonyConsole = Path.Combine(rootDirectory, "bin", "console");
        if (File.Exists(symfonyConsole)) return "symfony";

        return "generic";
    }
}
