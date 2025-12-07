using System;
using System.Collections.Generic;
using System.Text;

namespace Tusk.Domain.Cli.Doctor;
public sealed class DoctorModel
{
    public required string Cwd { get; init; }
    public required string Platform { get; init; }

    public required string TuskHome { get; init; }
    public required string VersionsRoot { get; init; }
    public required string CacheRoot { get; init; }
    public required string ManifestPath { get; init; }
    public required string GlobalConfigPath { get; init; }

    public required ProjectInfo Project { get; init; }
    public required ResolutionInfo Resolution { get; init; }

    public string? PhpBinaryPath { get; init; }

    public required string[] Installed { get; init; }

    public required ComposerInfo Composer { get; init; }

    public ProjectPhpHomeInfo? ProjectPhpHome { get; init; }

    public sealed class ProjectInfo
    {
        public string? Root { get; init; }
        public string? PhpVersion { get; init; }
    }

    public sealed class ResolutionInfo
    {
        public string? OverrideSpec { get; init; }
        public string? ProjectVersion { get; init; }
        public string? GlobalDefault { get; init; }
        public string? FinalVersion { get; init; }
    }

    public sealed class ComposerInfo
    {
        public string? ComposerPhar { get; init; }
        public string? ComposerExe { get; init; }
    }

    public sealed class ProjectPhpHomeInfo
    {
        public string? Home { get; init; }
        public string? Ini { get; init; }
        public string? Extensions { get; init; }
        public bool Enabled { get; init; }
    }
}
