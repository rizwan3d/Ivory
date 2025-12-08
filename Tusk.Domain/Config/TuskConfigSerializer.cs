using System.Text;
using System.Text.Json;

namespace Tusk.Domain.Config;

public static class TuskConfigSerializer
{
    public static bool TryDeserialize(string json, out TuskConfig? config)
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

    public static bool TryDeserialize(JsonElement root, out TuskConfig? config)
    {
        config = null;

        var phpSection = new TuskConfig.PhpSection();
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

        var scripts = new Dictionary<string, TuskConfig.TuskScript>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("scripts", out var scriptsElem) && scriptsElem.ValueKind == JsonValueKind.Object)
        {
            foreach (var scriptProp in scriptsElem.EnumerateObject())
            {
                var name = scriptProp.Name;
                var sElem = scriptProp.Value;

                if (sElem.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? description = null;
                string phpFile = "";
                var phpArgsList = new List<string>();
                var argsList = new List<string>();

                if (sElem.TryGetProperty("description", out var descElem) && descElem.ValueKind == JsonValueKind.String)
                {
                    description = descElem.GetString();
                }

                if (sElem.TryGetProperty("phpFile", out var fileElem) && fileElem.ValueKind == JsonValueKind.String)
                {
                    phpFile = fileElem.GetString() ?? "";
                }

                if (sElem.TryGetProperty("phpArgs", out var phpArgsElem) && phpArgsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in phpArgsElem.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String))
                    {
                        phpArgsList.Add(item.GetString()!);
                    }
                }

                if (sElem.TryGetProperty("args", out var argsElem2) && argsElem2.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in argsElem2.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String))
                    {
                        argsList.Add(item.GetString()!);
                    }
                }

                scripts[name] = new TuskConfig.TuskScript
                {
                    Description = description,
                    PhpFile = phpFile,
                    PhpArgs = phpArgsList,
                    Args = argsList
                };
            }
        }

        config = new TuskConfig
        {
            Php = phpSection,
            Scripts = scripts
        };

        return true;
    }

    public static string Serialize(TuskConfig config)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            Write(writer, config);
            writer.Flush();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Write(Utf8JsonWriter writer, TuskConfig config)
    {
        writer.WriteStartObject();

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

        writer.WriteEndObject();
    }

    private static void WriteScript(Utf8JsonWriter writer, TuskConfig.TuskScript script)
    {
        writer.WriteStartObject();
        if (!string.IsNullOrWhiteSpace(script.Description))
        {
            writer.WriteString("description", script.Description);
        }

        writer.WriteString("phpFile", script.PhpFile);

        if (script.PhpArgs.Count > 0)
        {
            writer.WritePropertyName("phpArgs");
            writer.WriteStartArray();
            foreach (var arg in script.PhpArgs)
            {
                writer.WriteStringValue(arg);
            }
            writer.WriteEndArray();
        }

        if (script.Args.Count > 0)
        {
            writer.WritePropertyName("args");
            writer.WriteStartArray();
            foreach (var arg in script.Args)
            {
                writer.WriteStringValue(arg);
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }
}
