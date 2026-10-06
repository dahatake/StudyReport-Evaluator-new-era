using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace StudyReportEvaluator.App.Tests.SupplyChain;

public sealed partial class PackageLockTests
{
    private const string CanonicalAppLockPath = "src/StudyReportEvaluator.App/packages.lock.json";
    private const string SingleFileAppLockPath = "src/StudyReportEvaluator.App/packages.win-x64-singlefile.lock.json";

    private static readonly string[] ProjectPaths =
    [
        "src/StudyReportEvaluator.Core/StudyReportEvaluator.Core.csproj",
        "src/StudyReportEvaluator.App/StudyReportEvaluator.App.csproj",
        "tests/StudyReportEvaluator.Core.Tests/StudyReportEvaluator.Core.Tests.csproj",
        "tests/StudyReportEvaluator.App.Tests/StudyReportEvaluator.App.Tests.csproj",
    ];

    [Fact]
    public void Sdk_and_central_package_versions_are_exact()
    {
        string root = FindRepositoryRoot();
        using JsonDocument global = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "global.json")));
        Assert.Equal("10.0.400", global.RootElement.GetProperty("sdk").GetProperty("version").GetString());
        Assert.False(global.RootElement.GetProperty("sdk").GetProperty("allowPrerelease").GetBoolean());

        XDocument central = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        Dictionary<string, string> properties = central.Descendants("PropertyGroup")
            .Elements()
            .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.OrdinalIgnoreCase);
        XElement[] versions = central.Descendants("PackageVersion").ToArray();
        Assert.NotEmpty(versions);
        Assert.Equal(versions.Length, versions.Select(element => (string?)element.Attribute("Include")).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (XElement item in versions)
        {
            string version = ResolveVersion(
                (string?)item.Attribute("Version") ?? string.Empty,
                properties);
            Assert.Matches(ExactVersion(), version);
        }
    }

    [Fact]
    public void Current_projects_opt_in_to_central_management_without_inline_versions()
    {
        string root = FindRepositoryRoot();
        HashSet<string> centralPackages = XDocument.Load(Path.Combine(root, "Directory.Packages.props"))
            .Descendants("PackageVersion")
            .Select(element => (string?)element.Attribute("Include"))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string projectPath in ProjectPaths)
        {
            XDocument project = XDocument.Load(Path.Combine(root, projectPath));
            Assert.Equal("true", project.Descendants("ManagePackageVersionsCentrally").Single().Value);
            Assert.Equal("true", project.Descendants("CentralPackageTransitivePinningEnabled").Single().Value);
            foreach (XElement reference in project.Descendants("PackageReference"))
            {
                string packageName = (string?)reference.Attribute("Include")
                    ?? throw new InvalidDataException($"PackageReference Include is missing in {projectPath}.");
                Assert.Null(reference.Attribute("Version"));
                Assert.Null(reference.Element("Version"));
                Assert.Contains(packageName, centralPackages);
            }
        }
    }

    [Fact]
    public void Every_current_project_has_a_version_two_lock_file_matching_direct_versions()
    {
        string root = FindRepositoryRoot();
        XDocument centralDocument = XDocument.Load(Path.Combine(root, "Directory.Packages.props"));
        Dictionary<string, string> properties = centralDocument.Descendants("PropertyGroup")
            .Elements()
            .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> central = centralDocument
            .Descendants("PackageVersion")
            .ToDictionary(
                element => (string)element.Attribute("Include")!,
                element => ResolveVersion((string)element.Attribute("Version")!, properties),
                StringComparer.OrdinalIgnoreCase);

        foreach (string projectPath in ProjectPaths)
        {
            string projectDirectory = Path.GetDirectoryName(Path.Combine(root, projectPath))!;
            string lockPath = Path.Combine(projectDirectory, "packages.lock.json");
            Assert.True(File.Exists(lockPath), $"Missing lock file: {projectPath}");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(lockPath));
            Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
            JsonElement dependencies = document.RootElement.GetProperty("dependencies").GetProperty("net10.0");
            foreach (JsonProperty package in dependencies.EnumerateObject())
            {
                if (!package.Value.TryGetProperty("type", out JsonElement type)
                    || !string.Equals(type.GetString(), "Direct", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(central.TryGetValue(package.Name, out string? expected), $"Direct package is not centrally pinned: {package.Name}");
                Assert.Equal(expected, package.Value.GetProperty("resolved").GetString());
                Assert.False(string.IsNullOrWhiteSpace(package.Value.GetProperty("contentHash").GetString()));
            }
        }
    }

    [Fact]
    public void Restore_uses_one_declared_https_source_and_lock_files()
    {
        string root = FindRepositoryRoot();
        XDocument config = XDocument.Load(Path.Combine(root, "NuGet.Config"));
        XElement[] sources = config.Descendants("packageSources").Elements("add").ToArray();
        Assert.Single(sources);
        Assert.StartsWith("https://", (string?)sources[0].Attribute("value"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("true", XDocument.Load(Path.Combine(root, "Directory.Build.props"))
            .Descendants("RestorePackagesWithLockFile").Single().Value);
    }

    [Fact]
    public void Single_file_lock_is_version_two_with_only_net10_and_win_x64_targets()
    {
        string root = FindRepositoryRoot();
        string lockPath = Path.Combine(root, SingleFileAppLockPath);
        Assert.True(File.Exists(lockPath), "Missing dedicated App single-file lock file.");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(lockPath));

        Assert.Equal(2, document.RootElement.GetProperty("version").GetInt32());
        string[] expectedTargets = ["net10.0", "net10.0/win-x64"];
        Assert.Equal(expectedTargets, document.RootElement.GetProperty("dependencies")
            .EnumerateObject().Select(target => target.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Single_file_lock_preserves_canonical_net10_package_fields_and_dependencies()
    {
        string root = FindRepositoryRoot();
        using JsonDocument canonical = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, CanonicalAppLockPath)));
        using JsonDocument singleFile = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, SingleFileAppLockPath)));
        JsonElement canonicalPackages = canonical.RootElement.GetProperty("dependencies").GetProperty("net10.0");
        JsonElement singleFilePackages = singleFile.RootElement.GetProperty("dependencies").GetProperty("net10.0");

        foreach (JsonProperty package in canonicalPackages.EnumerateObject())
        {
            Assert.True(singleFilePackages.TryGetProperty(package.Name, out JsonElement actual),
                $"Canonical package is missing from the single-file lock: {package.Name}");
            Assert.True(JsonElement.DeepEquals(package.Value, actual),
                $"Single-file package fields or dependencies differ from the canonical lock: {package.Name}");
        }
    }

    [Fact]
    public void Single_file_lock_adds_only_the_pinned_build_only_ILLink_package()
    {
        const string buildOnlyPackage = "Microsoft.NET.ILLink.Tasks";
        string root = FindRepositoryRoot();
        using JsonDocument canonical = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, CanonicalAppLockPath)));
        using JsonDocument singleFile = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, SingleFileAppLockPath)));
        JsonElement canonicalPackages = canonical.RootElement.GetProperty("dependencies").GetProperty("net10.0");
        JsonElement singleFilePackages = singleFile.RootElement.GetProperty("dependencies").GetProperty("net10.0");

        Assert.False(canonicalPackages.TryGetProperty(buildOnlyPackage, out _),
            "The single-file build-only package must not enter the canonical App lock.");
        string[] expectedPackages = canonicalPackages.EnumerateObject().Select(package => package.Name)
            .Append(buildOnlyPackage).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedPackages, singleFilePackages.EnumerateObject().Select(package => package.Name)
            .Order(StringComparer.Ordinal).ToArray());

        JsonElement buildOnly = singleFilePackages.GetProperty(buildOnlyPackage);
        string[] expectedFields = ["contentHash", "requested", "resolved", "type"];
        Assert.Equal(expectedFields, buildOnly.EnumerateObject().Select(field => field.Name)
            .Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("Direct", buildOnly.GetProperty("type").GetString());
        Assert.Equal("[10.0.11, )", buildOnly.GetProperty("requested").GetString());
        Assert.Equal("10.0.11", buildOnly.GetProperty("resolved").GetString());
        Assert.Equal("IBf7lbovvjGWVWXZX5cJ/cO0WXbId0Zq4BuSeT94mGZuOAP66oMeH9PTBZ9Jpp3Jb6jtK0qm/NyUbPRo1gC/wQ==",
            buildOnly.GetProperty("contentHash").GetString());
    }

    [Fact]
    public void Single_file_win_x64_package_fields_and_dependencies_match_canonical()
    {
        string root = FindRepositoryRoot();
        using JsonDocument canonical = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, CanonicalAppLockPath)));
        using JsonDocument singleFile = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, SingleFileAppLockPath)));
        JsonElement canonicalPackages = canonical.RootElement.GetProperty("dependencies").GetProperty("net10.0");
        JsonElement ridPackages = singleFile.RootElement.GetProperty("dependencies").GetProperty("net10.0/win-x64");
        string[] expectedPackages =
        [
            "Avalonia.Angle.Windows.Natives",
            "Avalonia.Native",
            "HarfBuzzSharp.NativeAssets.Linux",
            "HarfBuzzSharp.NativeAssets.macOS",
            "HarfBuzzSharp.NativeAssets.Win32",
            "SkiaSharp.NativeAssets.Linux",
            "SkiaSharp.NativeAssets.macOS",
            "SkiaSharp.NativeAssets.Win32",
        ];
        Assert.Equal(expectedPackages.Order(StringComparer.Ordinal).ToArray(),
            ridPackages.EnumerateObject().Select(package => package.Name).Order(StringComparer.Ordinal).ToArray());

        foreach (JsonProperty package in ridPackages.EnumerateObject())
        {
            Assert.True(canonicalPackages.TryGetProperty(package.Name, out JsonElement expected),
                $"RID package is absent from the canonical App lock: {package.Name}");
            Assert.True(JsonElement.DeepEquals(expected, package.Value),
                $"RID package fields or dependencies differ from the canonical lock: {package.Name}");
        }
    }

    [Fact]
    public void Core_remains_dependency_free_without_a_dedicated_single_file_lock()
    {
        string coreDirectory = Path.Combine(FindRepositoryRoot(), "src/StudyReportEvaluator.Core");
        XDocument project = XDocument.Load(Path.Combine(coreDirectory, "StudyReportEvaluator.Core.csproj"));
        Assert.Empty(project.Descendants("PackageReference"));

        string lockPath = Assert.Single(Directory.EnumerateFiles(coreDirectory, "packages*.lock.json"));
        Assert.Equal("packages.lock.json", Path.GetFileName(lockPath));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(lockPath));
        JsonProperty target = Assert.Single(document.RootElement.GetProperty("dependencies").EnumerateObject());
        Assert.Equal("net10.0", target.Name);
        Assert.Empty(target.Value.EnumerateObject());
    }

    [GeneratedRegex(@"^\d+(?:\.\d+){1,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExactVersion();

    [GeneratedRegex(@"^\$\((?<name>[A-Za-z_][A-Za-z0-9_.-]*)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex PropertyReference();

    private static string ResolveVersion(
        string value,
        IReadOnlyDictionary<string, string> properties)
    {
        Match match = PropertyReference().Match(value);
        return match.Success
            && properties.TryGetValue(match.Groups["name"].Value, out string? resolved)
                ? resolved
                : value;
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

        throw new DirectoryNotFoundException("Repository root containing StudyReportEvaluator.slnx was not found.");
    }
}
