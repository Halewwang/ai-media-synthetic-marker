namespace Emke.AiMarker.Release.Tests;

public sealed class BuildReleaseScriptContractTests
{
    private static readonly string Root = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [Fact]
    public void Build_script_revalidates_publish_components_before_cleanup_and_publish()
    {
        string script = File.ReadAllText(
                System.IO.Path.Combine(Root, "scripts", "build-release.ps1"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        const string assertion =
            "Assert-NoReparsePathComponents `\n        -RepositoryRoot $repoRoot `\n        -CandidatePath $publishDirectory";
        int cleanup = script.LastIndexOf(
            "Remove-OwnedDirectory `",
            StringComparison.Ordinal);
        int publish = script.IndexOf(
            "& $DotNet publish",
            StringComparison.Ordinal);
        int beforeCleanup = script.IndexOf(assertion, StringComparison.Ordinal);
        int beforePublish = script.LastIndexOf(assertion, StringComparison.Ordinal);

        Assert.True(cleanup >= 0, "build script must invoke publish cleanup");
        Assert.True(publish > cleanup, "publish must follow cleanup");
        Assert.InRange(beforeCleanup, 0, cleanup - 1);
        Assert.InRange(beforePublish, cleanup + 1, publish - 1);
    }

    [Fact]
    public void Build_scripts_compile_and_acceptance_test_installer_after_packaging()
    {
        string releaseScript = File.ReadAllText(
            System.IO.Path.Combine(Root, "scripts", "build-release.ps1"));
        string installerBuildScript = File.ReadAllText(
            System.IO.Path.Combine(Root, "scripts", "build-installer.ps1"));
        string installerScript = File.ReadAllText(
            System.IO.Path.Combine(
                Root,
                "packaging",
                "installer",
                "Emke.AiMarker.iss"));

        int package = releaseScript.IndexOf(
            "package --repo-root",
            StringComparison.Ordinal);
        int installer = releaseScript.IndexOf(
            "build-installer.ps1",
            StringComparison.Ordinal);
        Assert.True(package >= 0);
        Assert.True(installer > package);
        Assert.Contains(
            "-InnoCompiler $InnoCompiler",
            releaseScript,
            StringComparison.Ordinal);

        Assert.Contains(
            "PrivilegesRequired=lowest",
            installerScript,
            StringComparison.Ordinal);
        Assert.Contains("/VERYSILENT", installerBuildScript, StringComparison.Ordinal);
        Assert.Contains("/NOICONS", installerBuildScript, StringComparison.Ordinal);
        Assert.Contains(
            "--ui-self-test",
            installerBuildScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "VersionInfo.FileVersion).Trim()",
            installerBuildScript,
            StringComparison.Ordinal);
        Assert.Contains("unins*.exe", installerBuildScript, StringComparison.Ordinal);
        Assert.Contains(
            "SHA256SUMS.txt",
            installerBuildScript,
            StringComparison.Ordinal);
        Assert.Contains(
            "Assert-NoReparsePathComponents",
            installerBuildScript,
            StringComparison.Ordinal);
    }
}
