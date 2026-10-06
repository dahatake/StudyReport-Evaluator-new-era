using System.Text.RegularExpressions;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Content;

// Requirements: FR-064 (AC-065)
public sealed class RequirementTraceabilityTests
{
    private const string RequirementsPath = "docs/requirements-definition.md";
    private const string CatalogPath = "docs/catalog.md";
    private const string ManualTestsPath = "docs/manual-tests.md";
    private const string ThisFilePath = "tests/StudyReportEvaluator.App.Tests/Content/RequirementTraceabilityTests.cs";

    private const string RequirementIdPattern = @"(?:FR|NFR-[A-Z0-9]+)-[0-9]{3}";

    private static readonly Regex RequirementHeading = new(
        @"^#### (?<id>" + RequirementIdPattern + @") (?<title>\S.*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex AnyHeading = new(@"^#{1,4} ", RegexOptions.CultureInvariant);

    private static readonly Regex Priority = new(
        @"｜\s*(?:優先度:\s*)?(?<priority>MUST|SHOULD|MAY)(?![A-Z])",
        RegexOptions.CultureInvariant);

    private static readonly Regex AcceptanceCriterion = new(
        @"受入基準 (?<id>AC-[0-9]{3})(?![0-9])",
        RegexOptions.CultureInvariant);

    private static readonly Regex RequirementIdInText = new(
        @"(?<![A-Za-z0-9-])(?<id>" + RequirementIdPattern + @")(?![0-9])",
        RegexOptions.CultureInvariant);

    private static readonly Regex CatalogRow = new(
        @"^\| (?<id>" + RequirementIdPattern + @") \| (?<title>[^|]+) \| (?<status>[^|]+) \|",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

    [Fact]
    public void Requirement_and_acceptance_criterion_ids_are_unique_and_every_requirement_has_a_status_and_priority()
    {
        IReadOnlyList<Requirement> requirements = ParseRequirements();

        Assert.True(requirements.Count >= 50, $"Expected the requirement headings to be parsed, found {requirements.Count}.");
        Assert.Empty(requirements.GroupBy(requirement => requirement.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key));
        Assert.All(requirements, requirement =>
        {
            Assert.True(requirement.Status is not null, $"{requirement.Id} has no recognised '- 状態:' decision.");
            Assert.True(requirement.Priority is not null, $"{requirement.Id} has no MUST/SHOULD/MAY priority.");
        });
        Assert.Contains(requirements, requirement => requirement.Id == "FR-064"
            && requirement.Status == "承認済み" && requirement.Priority == "MUST");

        string[] acceptanceCriteria = AcceptanceCriterion.Matches(Read(RequirementsPath))
            .Select(match => match.Groups["id"].Value)
            .ToArray();
        Assert.Contains("AC-065", acceptanceCriteria);
        Assert.Empty(acceptanceCriteria.GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key));
    }

    [Fact]
    public void Every_approved_must_requirement_is_found_in_a_test_or_the_manual_tests()
    {
        string[] approvedMust = ApprovedMustRequirements().Select(requirement => requirement.Id).ToArray();
        Assert.NotEmpty(approvedMust);
        HashSet<string> traced = new(StringComparer.Ordinal);
        foreach (string content in TestSources(excludeThisFile: true).Select(File.ReadAllText).Append(Read(ManualTestsPath)))
        {
            traced.UnionWith(RequirementIdInText.Matches(content).Select(match => match.Groups["id"].Value));
        }

        string[] missing = approvedMust.Where(id => !traced.Contains(id)).ToArray();
        Assert.True(
            missing.Length == 0,
            "Approved MUST requirements without a test or manual test: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_requirement_id_in_test_code_exists_in_the_requirements_document()
    {
        HashSet<string> defined = ParseRequirements().Select(requirement => requirement.Id).ToHashSet(StringComparer.Ordinal);
        List<string> unknown = [];
        int references = 0;
        foreach (string path in TestSources(excludeThisFile: true))
        {
            foreach (Match match in RequirementIdInText.Matches(File.ReadAllText(path)))
            {
                references++;
                if (!defined.Contains(match.Groups["id"].Value))
                {
                    unknown.Add($"{Path.GetRelativePath(FindRepositoryRoot(), path)}: {match.Groups["id"].Value}");
                }
            }
        }

        Assert.True(references > 0, "No requirement IDs were found in the test code.");
        Assert.True(unknown.Count == 0, "Unknown requirement IDs in tests: " + string.Join(Environment.NewLine, unknown.Distinct()));
    }

    [Fact]
    public void Catalog_lists_every_approved_must_requirement_with_the_same_status()
    {
        Dictionary<string, string> catalog = new(StringComparer.Ordinal);
        foreach (Match row in CatalogRow.Matches(Read(CatalogPath)))
        {
            Assert.True(
                catalog.TryAdd(row.Groups["id"].Value, row.Groups["status"].Value.Trim()),
                $"Duplicate catalog row: {row.Groups["id"].Value}");
        }

        Assert.NotEmpty(catalog);
        List<string> problems = [];
        foreach (Requirement requirement in ApprovedMustRequirements())
        {
            if (!catalog.TryGetValue(requirement.Id, out string? status))
            {
                problems.Add($"{requirement.Id}: missing from the catalog");
            }
            else if (!string.Equals(StatusCategory(status), requirement.Status, StringComparison.Ordinal))
            {
                problems.Add($"{requirement.Id}: catalog '{status}' != requirements '{requirement.Status}'");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [InlineData("承認済み（依頼 2026-10-06）／依頼原文｜MUST｜上位: G-001", "承認済み", "MUST")]
    [InlineData("承認済み（Q-002 2026-10-06）／利用者決定｜優先度: MUST（理由）｜上位: G-004", "承認済み", "MUST")]
    [InlineData("承認済み／依頼原文｜MUST（担当者と AI エージェントが入れ替わるため）｜上位: G-004", "承認済み", "MUST")]
    [InlineData("保留（Q-007）／利用者決定｜優先度: SHOULD｜上位: G-001", "保留", "SHOULD")]
    [InlineData("廃止（2026-10-06）｜MAY｜", "廃止", "MAY")]
    [InlineData("提案（承認待ち）｜SHOULD｜", "承認待ち", "SHOULD")]
    [InlineData("未定｜MUSTANG｜", null, null)]
    public void Status_line_parser_recognises_decision_and_priority(string line, string? status, string? priority)
    {
        Assert.Equal(status, StatusCategory(line));
        Match match = Priority.Match(line);
        Assert.Equal(priority, match.Success ? match.Groups["priority"].Value : null);
    }

    private static IEnumerable<Requirement> ApprovedMustRequirements() =>
        ParseRequirements().Where(requirement => requirement is { Status: "承認済み", Priority: "MUST" });

    private static IReadOnlyList<Requirement> ParseRequirements()
    {
        string[] lines = Read(RequirementsPath).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        List<Requirement> requirements = [];
        for (int index = 0; index < lines.Length; index++)
        {
            Match heading = RequirementHeading.Match(lines[index]);
            if (!heading.Success)
            {
                continue;
            }

            string? status = null;
            string? priority = null;
            for (int next = index + 1; next < lines.Length && !AnyHeading.IsMatch(lines[next]); next++)
            {
                if (lines[next].StartsWith("- 状態:", StringComparison.Ordinal))
                {
                    string value = lines[next]["- 状態:".Length..].Trim();
                    status = StatusCategory(value);
                    Match match = Priority.Match(value);
                    priority = match.Success ? match.Groups["priority"].Value : null;
                    break;
                }
            }

            requirements.Add(new Requirement(heading.Groups["id"].Value, status, priority));
        }

        return requirements;
    }

    private static string? StatusCategory(string status)
    {
        foreach (string decision in new[] { "承認済み", "保留", "廃止" })
        {
            if (status.StartsWith(decision, StringComparison.Ordinal))
            {
                return decision;
            }
        }

        return status.Contains("承認待ち", StringComparison.Ordinal) ? "承認待ち" : null;
    }

    private static IEnumerable<string> TestSources(bool excludeThisFile)
    {
        string root = FindRepositoryRoot();
        string self = Path.GetFullPath(Resolve(root, ThisFilePath));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Directory.EnumerateFiles(Resolve(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar)
                .Any(segment => segment.Equals("bin", comparison) || segment.Equals("obj", comparison)))
            .Where(path => !excludeThisFile || !string.Equals(Path.GetFullPath(path), self, comparison));
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Resolve(FindRepositoryRoot(), relativePath));

    private static string Resolve(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

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

        throw new DirectoryNotFoundException(
            "The repository root containing StudyReportEvaluator.slnx was not found.");
    }

    private sealed record Requirement(string Id, string? Status, string? Priority);
}
