using System.Text.RegularExpressions;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Packaging;

public sealed class WindowsInstallerPackageTests
{
    // Keep this contract independent of the scripts and the ZIP test's required list.
    private static readonly string[] ExpectedPublicPayloadPaths =
    [
        "README.md",
        "LICENSE",
        "docs/README.md",
        "docs/getting-started.md",
        "docs/features.md",
        "docs/custom-evaluator-guide.md",
        "docs/technical-guid.md",
        "docs/prompt-launch.md",
        "docs/privacy-and-data-handling.md",
        "docs/troubleshooting.md",
        "docs/settings.md",
        "docs/third-party-notices.md",
        "docs/result-excel-description.md",
        "images/README.md",
        "images/architecture-overview.svg",
        "images/technical-architecture.svg",
        "images/evaluation-message-flow.svg",
        "images/01-input-workbook.png",
        "images/02-input-mapping.png",
        "images/03-design-knowledge.png",
        "images/04-design-custom-prompt.png",
        "images/05-execution-auto.png",
        "images/06-results-review.png",
        "images/07-output-export.png",
        "images/08-settings.png",
    ];

    [Fact]
    public void Zip_package_script_keeps_the_exact_public_payload_and_excludes_user_settings()
    {
        string repositoryRoot = FindRepositoryRoot();
        string zipSource = File.ReadAllText(Path.Combine(repositoryRoot, "scripts", "package-windows.ps1"));

        AssertPublicPayloadList(zipSource, "documentationRelativePaths");
        Assert.Contains("$item.Name -ieq 'setting.txt'", zipSource, StringComparison.Ordinal);
    }

    [Fact]
    public void CI_runs_the_Windows_ZIP_regression_once_and_uploads_only_closed_evidence()
    {
        string repositoryRoot = FindRepositoryRoot();
        string driver = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "scripts",
            "test-windows-zip.ps1"));
        string workflow = File.ReadAllText(Path.Combine(
            repositoryRoot,
            ".github",
            "workflows",
            "ci.yml"));

        Assert.Contains("#Requires -Version 7.4", driver, StringComparison.Ordinal);
        Assert.Contains("#Requires -PSEdition Core", driver, StringComparison.Ordinal);
        Assert.Contains("WindowsPublishPackageTests", driver, StringComparison.Ordinal);
        Assert.Contains("$RequiredTestCount = 3", driver, StringComparison.Ordinal);
        Assert.Contains("must be generated from a clean source checkout", driver, StringComparison.Ordinal);
        Assert.Contains("user-workbook-sentinel.xlsx", driver, StringComparison.Ordinal);
        Assert.Contains("windows-zip-required", driver, StringComparison.Ordinal);
        Assert.Contains("PASS_REQUIRED", driver, StringComparison.Ordinal);
        Assert.Contains("StudyReportEvaluator-win-x64.evidence.json", driver, StringComparison.Ordinal);
        Assert.True(
            driver.IndexOf("[AllowEmptyCollection()]", StringComparison.Ordinal) <
            driver.IndexOf(
                "[System.Collections.Generic.HashSet[string]] $Seen",
                StringComparison.Ordinal));
        Assert.Contains("sourceStatusEntryCount = 0", driver, StringComparison.Ordinal);
        Assert.Contains("safeLayoutVerified = $true", driver, StringComparison.Ordinal);
        Assert.Contains("apphostLaunchVerified = $true", driver, StringComparison.Ordinal);
        Assert.Contains("inputUnchangedVerified = $true", driver, StringComparison.Ordinal);
        Assert.DoesNotContain("PASS_PRODUCTION", driver, StringComparison.Ordinal);
        Assert.True(
            driver.IndexOf(
                "Remove-Item -LiteralPath $evidencePath -Force",
                StringComparison.Ordinal) <
            driver.IndexOf("$statusBefore =", StringComparison.Ordinal));
        Assert.True(
            driver.LastIndexOf(
                "if (Test-Path -LiteralPath $sentinelRoot)",
                StringComparison.Ordinal) <
            driver.IndexOf(
                "Write-AtomicEvidence -Path $evidencePath",
                StringComparison.Ordinal));

        Assert.Contains("FullyQualifiedName!~StudyReportEvaluator.App.Tests.Packaging.WindowsPublishPackageTests", workflow, StringComparison.Ordinal);
        Assert.Contains("Build and validate Windows ZIP regression", workflow, StringComparison.Ordinal);
        Assert.Contains("id: windows-zip", workflow, StringComparison.Ordinal);
        Assert.Contains(".\\scripts\\test-windows-zip.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("steps.windows-zip.outcome == 'success'", workflow, StringComparison.Ordinal);
        Assert.Contains("windows-zip-regression-${{ github.run_id }}", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/package/StudyReportEvaluator-win-x64.zip", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/package/StudyReportEvaluator-win-x64.evidence.json", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@v6", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("actions/setup-dotnet@v4", workflow, StringComparison.Ordinal);
        Assert.Contains("Run product version tool self-tests", workflow, StringComparison.Ordinal);
        Assert.Contains(".\\dev\\version.tests.ps1", workflow, StringComparison.Ordinal);

        // The development MSIX and the macOS foundation were retired on 2026-10-06.
        Assert.DoesNotContain("msix", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("macos", workflow, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertPublicPayloadList(string source, string variableName)
    {
        Match array = Assert.Single(
            Regex.Matches(
                source,
                @"(?m)^[ \t]*\$" + Regex.Escape(variableName) +
                @"[ \t]*=[ \t]*@\(\s*(?:'(?<path>[^'\r\n]+)'\s*,?\s*)+\)[ \t]*\r?$",
                RegexOptions.CultureInvariant)
                .Cast<Match>());
        string[] actualPaths = array.Groups["path"].Captures
            .Cast<Capture>()
            .Select(capture => capture.Value.Replace('\\', '/'))
            .ToArray();

        Assert.Equal(25, ExpectedPublicPayloadPaths.Length);
        Assert.Equal(25, actualPaths.Length);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in actualPaths)
        {
            Assert.True(seen.Add(path), $"Duplicate or case-colliding public payload path: {path}");
        }

        Assert.Equal(
            ExpectedPublicPayloadPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            actualPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "StudyReportEvaluator.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
