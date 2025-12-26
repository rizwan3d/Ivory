using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace Ivory.Domain.Config;

public static class IvoryConfigSerializer
{
    public static bool TryDeserialize(string json, out IvoryConfig? config)
    {
        config = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return TryDeserialize(doc.RootElement, out config);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDeserialize(JsonElement root, out IvoryConfig? config)
    {
        config = null;

        string? org = null;
        string? project = null;
        if (root.TryGetProperty("org", out var orgElem) && orgElem.ValueKind == JsonValueKind.String)
        {
            org = orgElem.GetString();
        }

        if (root.TryGetProperty("project", out var projectElem) && projectElem.ValueKind == JsonValueKind.String)
        {
            project = projectElem.GetString();
        }

        var phpSection = new IvoryConfig.PhpSection();
        if (root.TryGetProperty("php", out var phpElem) && phpElem.ValueKind == JsonValueKind.Object)
        {
            if (phpElem.TryGetProperty("version", out var verElem) && verElem.ValueKind == JsonValueKind.String)
            {
                phpSection.Version = verElem.GetString();
            }

            if (phpElem.TryGetProperty("ini", out var iniElem) && iniElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in iniElem.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String))
                {
                    phpSection.Ini.Add(item.GetString()!);
                }
            }

            if (phpElem.TryGetProperty("args", out var argsElem) && argsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in argsElem.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String))
                {
                    phpSection.Args.Add(item.GetString()!);
                }
            }
        }

        var scripts = new Dictionary<string, IvoryConfig.IvoryScript>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("scripts", out var scriptsElem) && scriptsElem.ValueKind == JsonValueKind.Object)
        {
            foreach (var scriptProp in scriptsElem.EnumerateObject())
            {
                var name = scriptProp.Name;
                var sElem = scriptProp.Value;

                var commands = new List<string>();
                if (sElem.ValueKind == JsonValueKind.String)
                {
                    commands.Add(sElem.GetString() ?? string.Empty);
                }
                else if (sElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in sElem.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String))
                    {
                        var cmd = item.GetString();
                        if (!string.IsNullOrWhiteSpace(cmd))
                        {
                            commands.Add(cmd);
                        }
                    }
                }

                if (commands.Count > 0)
                {
                    scripts[name] = new IvoryConfig.IvoryScript
                    {
                        Commands = commands
                    };
                }
            }
        }

        config = new IvoryConfig
        {
            Org = org,
            Project = project,
            Php = phpSection,
            Scripts = scripts
        };

        return true;
    }

    public static string Serialize(IvoryConfig config)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            Write(writer, config);
            writer.Flush();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Write(Utf8JsonWriter writer, IvoryConfig config)
    {
        writer.WriteStartObject();

        if (!string.IsNullOrWhiteSpace(config.Org))
        {
            writer.WriteString("org", config.Org);
        }

        if (!string.IsNullOrWhiteSpace(config.Project))
        {
            writer.WriteString("project", config.Project);
        }

        writer.WritePropertyName("php");
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(config.Php.Version))
        {
            writer.WriteString("version", config.Php.Version);
        }

        writer.WritePropertyName("ini");
        writer.WriteStartArray();
        foreach (var ini in config.Php.Ini)
        {
            writer.WriteStringValue(ini);
        }
        writer.WriteEndArray();

        writer.WritePropertyName("args");
        writer.WriteStartArray();
        foreach (var arg in config.Php.Args)
        {
            writer.WriteStringValue(arg);
        }
        writer.WriteEndArray();

        writer.WriteEndObject();

        WriteScripts(writer, config);

        writer.WriteEndObject();
    }

    private static void WriteScripts(Utf8JsonWriter writer, IvoryConfig config)
    {
        if (config.Scripts.Count > 0)
        {
            writer.WritePropertyName("scripts");
            writer.WriteStartObject();
            foreach (var script in config.Scripts.OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                writer.WritePropertyName(script.Key);
                WriteScript(writer, script.Value);
            }
            writer.WriteEndObject();
        }

    }

    private static void WriteScript(Utf8JsonWriter writer, IvoryConfig.IvoryScript script)
    {
        if (script.Commands.Count == 1)
        {
            writer.WriteStringValue(script.Commands[0]);
            return;
        }

        writer.WriteStartArray();
        foreach (var cmd in script.Commands)
        {
            writer.WriteStringValue(cmd);
        }
        writer.WriteEndArray();
    }
}
