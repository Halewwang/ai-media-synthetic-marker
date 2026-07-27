namespace Emke.AiMarker.Release.Tests;

public sealed class BuildReleaseScriptContractTests
{
    [Fact]
    public void Build_script_revalidates_publish_components_before_cleanup_and_publish()
    {
        string root = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        string script = File.ReadAllText(
                System.IO.Path.Combine(root, "scripts", "build-release.ps1"))
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
}
