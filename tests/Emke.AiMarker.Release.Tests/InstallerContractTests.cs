using System.Text.Json;

namespace Emke.AiMarker.Release.Tests;

public sealed class InstallerContractTests
{
    private static readonly string Root = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [Fact]
    public void Installer_is_per_user_x64_and_has_only_the_approved_shortcuts()
    {
        string script = File.ReadAllText(
            Path.Combine(
                Root,
                "packaging",
                "installer",
                "Emke.AiMarker.iss"));

        Assert.Contains(
            "AppId={{9F630913-5706-4142-A1A4-C35B171938C8}",
            script,
            StringComparison.Ordinal);
        Assert.Contains("PrivilegesRequired=lowest", script, StringComparison.Ordinal);
        Assert.Contains(
            @"DefaultDirName={localappdata}\Programs\EMKE AI Marker",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "ArchitecturesAllowed=x64compatible",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            @"Source: ""{#StageDir}\*""",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            @"Name: ""{group}\EMKE AI Marker""",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            @"Name: ""{autodesktop}\EMKE AI Marker""",
            script,
            StringComparison.Ordinal);
        Assert.Contains("Flags: unchecked", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[Registry]", script, StringComparison.Ordinal);
        Assert.DoesNotContain("{autostartup}", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_compiler_lock_is_exact()
    {
        using JsonDocument document = JsonDocument.Parse(
            File.ReadAllBytes(
                Path.Combine(Root, "packaging", "inno-setup.lock.json")));
        JsonElement root = document.RootElement;

        Assert.Equal("6.7.3", root.GetProperty("version").GetString());
        Assert.Equal(
            "windows-x64-build-tool",
            root.GetProperty("platform").GetString());
        Assert.Equal(
            "innosetup-6.7.3.exe",
            root.GetProperty("archive_name").GetString());
        Assert.Equal(10_592_232, root.GetProperty("size").GetInt64());
        Assert.Equal(
            "9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732",
            root.GetProperty("sha256").GetString());
    }
}
