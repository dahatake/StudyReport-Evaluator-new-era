using System.Xml.Linq;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Packaging;

// Structural contracts only: these tests do not evaluate MSBuild or publish/run an application.
// Requirements: FR-055 (AC-056)
public sealed class WindowsSingleFileProfileTests
{
    private const string AppCondition = "'$(MSBuildProjectName)' == 'StudyReportEvaluator.App'";
    private const string RepositoryIncludePrefix = "$(MSBuildProjectDirectory)\\..\\..\\";

    private static readonly string[] RequiredDocumentationFiles =
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
    public void Windows_single_file_profile_scopes_all_declarations_to_the_app()
    {
        XElement profile = LoadProfile();

        Assert.Equal("Project", profile.Name.ToString());
        Assert.Empty(profile.Attributes());
        // One ItemGroup pins the ILLink pack; the other is the public Content allowlist.
        Assert.Equal(
            ["PropertyGroup", "ItemGroup", "ItemGroup"],
            profile.Elements().Select(group => group.Name.ToString()).ToArray());

        foreach (XElement group in profile.Elements())
        {
            XAttribute condition = Assert.Single(group.Attributes());
            Assert.Equal("Condition", condition.Name.ToString());
            Assert.Equal(AppCondition, condition.Value);
        }

        // Pin values are asserted by WindowsSingleFilePublishTests; this keeps the pin group closed.
        XElement[] itemGroups = profile.Elements("ItemGroup").ToArray();
        XElement pinGroup = Assert.Single(itemGroups, group => group.Elements("KnownILLinkPack").Any());
        Assert.Equal(
            ["KnownILLinkPack"],
            pinGroup.Elements().Select(item => item.Name.ToString()).ToArray());
        Assert.Same(LoadContentItemGroup(profile), Assert.Single(itemGroups, group => group != pinGroup));

        Assert.Empty(profile.Descendants("Target"));
        Assert.Empty(profile.Descendants("Import"));
        Assert.Empty(profile.Descendants("PackageReference"));
        Assert.Empty(profile.Descendants("ProjectReference"));
    }

    [Fact]
    public void Windows_single_file_profile_declares_standard_self_contained_settings_once()
    {
        XElement propertyGroup = Assert.Single(LoadProfile().Elements("PropertyGroup"));
        XElement[] properties = propertyGroup.Elements().ToArray();
        Dictionary<string, string> expected = new(StringComparer.Ordinal)
        {
            ["TargetFramework"] = "net10.0",
            ["RuntimeIdentifier"] = "win-x64",
            ["RuntimeFrameworkVersion"] = "10.0.11",
            ["SelfContained"] = "true",
            ["UseAppHost"] = "true",
            ["PublishSingleFile"] = "true",
            ["IncludeNativeLibrariesForSelfExtract"] = "true",
            ["IncludeAllContentForSelfExtract"] = "true",
            ["PublishTrimmed"] = "false",
            ["PublishReadyToRun"] = "false",
            ["EnableCompressionInSingleFile"] = "false",
            ["DebugSymbols"] = "false",
            ["DebugType"] = "None",
        };

        // The closed set also prevents product-version overrides or analyzer suppression.
        Assert.Equal(expected.Count, properties.Length);
        foreach ((string name, string value) in expected)
        {
            XElement property = Assert.Single(properties, item => item.Name == name);
            Assert.Empty(property.Attributes());
            Assert.Empty(property.Elements());
            Assert.Equal(value, property.Value);
        }
    }

    [Fact]
    public void Windows_single_file_profile_registers_only_the_exact_safe_public_allowlist()
    {
        string repositoryRoot = FindRepositoryRoot();
        XElement[] contentItems = LoadContentItemGroup(LoadProfile()).Elements().ToArray();

        Assert.Equal(25, RequiredDocumentationFiles.Length);
        Assert.Equal(25, contentItems.Length);
        Assert.All(contentItems, item => Assert.Equal("Content", item.Name.ToString()));

        foreach (string requiredFile in RequiredDocumentationFiles)
        {
            string windowsRelativePath = requiredFile.Replace('/', '\\');
            XElement content = Assert.Single(
                contentItems,
                item => (string?)item.Attribute("Link") == windowsRelativePath);

            // Metadata has one representation, with no child overrides, item conditions or transforms.
            Assert.Equal(
                ["CopyToPublishDirectory", "ExcludeFromSingleFile", "Include", "Link"],
                content.Attributes()
                    .Select(attribute => attribute.Name.ToString())
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray());
            Assert.Empty(content.Elements());
            Assert.Empty(content.Value);
            Assert.Equal("PreserveNewest", (string?)content.Attribute("CopyToPublishDirectory"));
            Assert.Equal("false", (string?)content.Attribute("ExcludeFromSingleFile"));

            string include = Assert.IsType<string>((string?)content.Attribute("Include"));
            string link = Assert.IsType<string>((string?)content.Attribute("Link"));
            Assert.Equal(RepositoryIncludePrefix + windowsRelativePath, include);
            Assert.DoesNotContain('*', include);
            Assert.DoesNotContain('?', include);
            Assert.DoesNotContain(';', include);
            AssertSafeRelativePath(link.Replace('\\', '/'));
            Assert.True(
                File.Exists(Path.Combine(repositoryRoot, requiredFile.Replace('/', Path.DirectorySeparatorChar))),
                $"Missing public documentation file: {requiredFile}");
        }
    }

    [Fact]
    public void Windows_single_file_profile_allowlist_matches_the_zip_packager()
    {
        string[] lines = File.ReadAllLines(
            Path.Combine(FindRepositoryRoot(), "scripts", "package-windows.ps1"));
        const string declaration = "$documentationRelativePaths = @(";
        Assert.Single(lines, line => line.Trim() == declaration);
        int start = Array.FindIndex(lines, line => line.Trim() == declaration);
        int end = Array.FindIndex(lines, start + 1, line => line.Trim() == ")");
        Assert.True(end > start, "The ZIP documentation allowlist must have a closing delimiter.");

        string[] entries = lines[(start + 1)..end].Select(line => line.Trim()).ToArray();
        Assert.All(entries, entry => Assert.Matches("^'[^']+',?$", entry));
        string[] paths = entries
            .Select(entry => entry.TrimEnd(',').Trim('\'').Replace('\\', '/'))
            .ToArray();

        Assert.Equal(25, paths.Length);
        Assert.Equal(
            RequiredDocumentationFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            paths.OrderBy(path => path, StringComparer.Ordinal).ToArray());
    }

    private static void AssertSafeRelativePath(string relativePath)
    {
        Assert.False(string.IsNullOrWhiteSpace(relativePath));
        Assert.False(Path.IsPathRooted(relativePath));
        Assert.DoesNotContain('\\', relativePath);
        Assert.DoesNotContain('\0', relativePath);
        Assert.DoesNotContain('*', relativePath);
        Assert.DoesNotContain('?', relativePath);
        Assert.DoesNotContain(';', relativePath);
        Assert.DoesNotContain(':', relativePath);
        Assert.DoesNotContain(
            relativePath.Split('/'),
            segment => string.IsNullOrEmpty(segment) || segment is "." or "..");
    }

    // Selecting by any Content child keeps a stray Content in another group from being ignored.
    private static XElement LoadContentItemGroup(XElement profile) =>
        Assert.Single(profile.Elements("ItemGroup"), group => group.Elements("Content").Any());

    private static XElement LoadProfile()
    {
        string path = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "StudyReportEvaluator.App",
            "Properties",
            "PublishProfiles",
            "WindowsSingleFile.pubxml");
        return Assert.IsType<XElement>(XDocument.Load(path).Root);
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