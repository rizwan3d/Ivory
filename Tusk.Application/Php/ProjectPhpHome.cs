namespace Tusk.Application.Php;

public sealed record ProjectPhpHome(
    string HomePath,
    string IniPath,
    string ExtensionsPath,
    string? ProjectRoot);
