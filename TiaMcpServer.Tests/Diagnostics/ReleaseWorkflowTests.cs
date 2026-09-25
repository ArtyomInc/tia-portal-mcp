using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class ReleaseWorkflowTests
{
    [Fact]
    public void StandalonePublish_UsesResolvedPackageVersion()
    {
        var publishStep = ReadWorkflowStep("Publish Standalone Binary", "Zip Standalone Binary");

        Assert.Contains("/p:Version=$env:PACKAGE_VERSION", publishStep, StringComparison.Ordinal);
        Assert.Contains("/p:PackageVersion=$env:PACKAGE_VERSION", publishStep, StringComparison.Ordinal);
        Assert.Contains("/p:InformationalVersion=$env:PACKAGE_VERSION", publishStep, StringComparison.Ordinal);
        Assert.Contains("/p:IncludeSourceRevisionInInformationalVersion=false", publishStep, StringComparison.Ordinal);
    }

    [Fact]
    public void NuGetPack_UsesExactResolvedInformationalVersion()
    {
        var packStep = ReadWorkflowStep("Build and Pack", "Verify tool package contents");

        Assert.Contains("/p:Version=$env:PACKAGE_VERSION", packStep, StringComparison.Ordinal);
        Assert.Contains("/p:PackageVersion=$env:PACKAGE_VERSION", packStep, StringComparison.Ordinal);
        Assert.Contains("/p:InformationalVersion=$env:PACKAGE_VERSION", packStep, StringComparison.Ordinal);
        Assert.Contains("/p:IncludeSourceRevisionInInformationalVersion=false", packStep, StringComparison.Ordinal);
    }

    [Fact]
    public void Packages_AreAlwaysPushedToTheOwnersGitHubFeed_AndToNuGetOrgOnlyWhenConfigured()
    {
        var githubStep = ReadWorkflowStep("Push to GitHub Packages", "NuGet login (OIDC → temp API key)");
        Assert.Contains("https://nuget.pkg.github.com/${{ github.repository_owner }}/index.json", githubStep, StringComparison.Ordinal);
        Assert.Contains("secrets.GITHUB_TOKEN", githubStep, StringComparison.Ordinal);
        Assert.DoesNotContain("if:", githubStep, StringComparison.Ordinal);

        var loginStep = ReadWorkflowStep("NuGet login (OIDC → temp API key)", "NuGet push");
        var pushStep = ReadWorkflowStep("NuGet push", "Publish Standalone Binary");
        Assert.Contains("if: env.PUBLISH_TO_NUGET_ORG == 'true'", loginStep, StringComparison.Ordinal);
        Assert.Contains("if: env.PUBLISH_TO_NUGET_ORG == 'true'", pushStep, StringComparison.Ordinal);
        Assert.Contains("https://api.nuget.org/v3/index.json", pushStep, StringComparison.Ordinal);
    }

    [Fact]
    public void NuGetPack_UsesTheConfigurablePackageId()
    {
        var packStep = ReadWorkflowStep("Build and Pack", "Verify tool package contents");
        Assert.Contains("/p:PackageId=$env:PACKAGE_ID", packStep, StringComparison.Ordinal);
    }

    private static string ReadWorkflowStep(string stepName, string followingStepName)
    {
        var workflowPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".github",
            "workflows",
            "publish.yml"));
        var workflow = File.ReadAllText(workflowPath);
        var stepStart = workflow.IndexOf($"- name: {stepName}", StringComparison.Ordinal);
        var stepEnd = workflow.IndexOf($"- name: {followingStepName}", stepStart, StringComparison.Ordinal);

        Assert.True(stepStart >= 0, $"{stepName} step is missing.");
        Assert.True(stepEnd > stepStart, $"{followingStepName} step must follow {stepName}.");
        return workflow[stepStart..stepEnd];
    }
}
