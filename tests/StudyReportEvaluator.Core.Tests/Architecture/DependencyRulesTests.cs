using System.Xml.Linq;
using Xunit;

namespace StudyReportEvaluator.Core.Tests.Architecture;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Solution_has_exactly_two_production_and_two_test_projects()
    {
        string root = FindRepositoryRoot();
        string[] production = FindProjects(Path.Combine(root, "src"));
        string[] tests = FindProjects(Path.Combine(root, "tests"));

        Assert.Equal(
            ["StudyReportEvaluator.App.csproj", "StudyReportEvaluator.Core.csproj"],
            production.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(
            ["StudyReportEvaluator.App.Tests.csproj", "StudyReportEvaluator.Core.Tests.csproj"],
            tests.Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Core_is_bcl_only_and_app_depends_one_way_on_core()
    {
        string root = FindRepositoryRoot();
        XDocument core = LoadProject(root, "src", "StudyReportEvaluator.Core", "StudyReportEvaluator.Core.csproj");
        XDocument app = LoadProject(root, "src", "StudyReportEvaluator.App", "StudyReportEvaluator.App.csproj");
        string appDirectory = Path.Combine(root, "src", "StudyReportEvaluator.App");

        Assert.Empty(core.Descendants("PackageReference"));
        Assert.Empty(core.Descendants("ProjectReference"));

        string[] references = app.Descendants("ProjectReference")
            .Select(element => NormalizePath(Path.Combine(
                appDirectory,
                (string?)element.Attribute("Include") ?? string.Empty)))
            .ToArray();
        Assert.Single(references);
        Assert.EndsWith("src/StudyReportEvaluator.Core/StudyReportEvaluator.Core.csproj", references[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Core_source_has_no_adapter_framework_or_office_dependency()
    {
        string root = FindRepositoryRoot();
        string coreDirectory = Path.Combine(root, "src", "StudyReportEvaluator.Core");
        string[] forbidden =
        [
            "Avalonia",
            "DocumentFormat.OpenXml",
            "GitHub.Copilot",
            "Microsoft.Office",
            "Office.Interop",
            "LibreOffice",
        ];

        foreach (string file in Directory.EnumerateFiles(coreDirectory, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !IsBuildOutput(path)))
        {
            string content = File.ReadAllText(file);
            foreach (string token in forbidden)
            {
                Assert.DoesNotContain(token, content, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void No_unplanned_production_project_roots_exist()
    {
        string root = FindRepositoryRoot();
        string[] forbiddenRoots = ["Application", "Infrastructure", "Platform", "Workbooks", "Desktop"];
        foreach (string name in forbiddenRoots)
        {
            Assert.False(Directory.Exists(Path.Combine(root, "src", name)), $"Unplanned project root exists: src/{name}");
        }
    }

    private static XDocument LoadProject(string root, params string[] segments) =>
        XDocument.Load(Path.Combine([root, .. segments]));

    private static string[] FindProjects(string directory) =>
        Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool IsBuildOutput(string path) =>
        NormalizePath(path).Contains("/bin/", StringComparison.OrdinalIgnoreCase)
        || NormalizePath(path).Contains("/obj/", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).Replace('\\', '/');

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
