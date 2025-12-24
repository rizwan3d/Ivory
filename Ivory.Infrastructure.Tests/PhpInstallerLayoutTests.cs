using FluentAssertions;
using Ivory.Infrastructure.Php;
using Xunit;

namespace Ivory.Infrastructure.Tests;

public class PhpInstallerLayoutTests
{
    [Fact]
    public void Normalization_creates_root_and_bin_shims_and_ext_directory()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "ivory-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            string installDir = Path.Combine(tempRoot, "install");
            string extractedDir = Path.Combine(installDir, "php-8.3");
            string binDir = Path.Combine(installDir, "bin");
            Directory.CreateDirectory(extractedDir);
            Directory.CreateDirectory(binDir);

            string phpPath = Path.Combine(extractedDir, OperatingSystem.IsWindows() ? "php.exe" : "php");
            File.WriteAllText(phpPath, "#!/usr/bin/env php");

            string extDir = Path.Combine(extractedDir, "ext");
            Directory.CreateDirectory(extDir);
            File.WriteAllText(Path.Combine(extDir, "info.txt"), "extension");

            string targetRoot = Path.Combine(installDir, OperatingSystem.IsWindows() ? "php.exe" : "php");
            string targetBin = Path.Combine(binDir, OperatingSystem.IsWindows() ? "php.exe" : "php");
            string targetExt = Path.Combine(installDir, "ext");

            PhpInstaller.LayoutNormalizer.EnsureExecutableShim(phpPath, targetRoot);
            PhpInstaller.LayoutNormalizer.EnsureExecutableShim(phpPath, targetBin);
            PhpInstaller.LayoutNormalizer.EnsureExtensionLink(extDir, targetExt);

            File.Exists(targetRoot).Should().BeTrue("php binary should be accessible at install root");
            File.Exists(targetBin).Should().BeTrue("php binary should be accessible under bin");
            File.ReadAllText(targetRoot).Should().Contain("php");
            File.ReadAllText(targetBin).Should().Contain("php");

            Directory.Exists(targetExt).Should().BeTrue("extension directory should be linked or created");
            File.Exists(Path.Combine(targetExt, "info.txt")).Should().BeTrue("extension content should be reachable from target ext");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* ignore cleanup errors */ }
        }
    }
}
