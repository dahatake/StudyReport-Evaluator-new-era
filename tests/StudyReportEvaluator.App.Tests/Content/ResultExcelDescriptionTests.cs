using System.Reflection;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Resources;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Content;

// Requirements: FR-062 (AC-063)
public sealed class ResultExcelDescriptionTests
{
    private const string DescriptionPath = "docs/result-excel-description.md";

    private static readonly string[] SheetNames =
    [
        "Quantification_Config",
        "Quantification_References",
        "Quantification_Results",
        "Quantification_Run",
    ];

    [Fact]
    public void Description_is_linked_from_the_readme_and_the_user_guide_index_and_shows_the_notices()
    {
        string description = Read(DescriptionPath);
        string readme = Read("README.md");
        string userIndex = Read("docs/README.md");

        Assert.Contains("(docs/result-excel-description.md)", readme, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"\[[^\]]*\]\(docs/result-excel-description\.md\)[^\r\n]*"),
            readme);
        Assert.Contains("(result-excel-description.md)", userIndex, StringComparison.Ordinal);
        Assert.Contains(EthicsWarningText.Message, description, StringComparison.Ordinal);
        Assert.DoesNotContain("UNRELEASED", description, StringComparison.Ordinal);
        // FR-062 (Q-002): this repository's product version, distinguished from the predecessor's public releases.
        string productVersion = ProductVersionPrefix();
        Assert.Equal("0.9.0", productVersion);
        Assert.Contains($"本書は、本アプリの製品版`{productVersion}`の出力に基づきます", description, StringComparison.Ordinal);
        Assert.Contains($"本書は、製品版`{productVersion}`が作るExcelに基づきます", description, StringComparison.Ordinal);
        Assert.Contains("前版（`dahatake/StudyReport-Evaluator`）の公開`v0.8.6`の後継", description, StringComparison.Ordinal);
        Assert.Contains("前版の公開`v0.8.6`・旧`v0.8.1`とは別の版です", description, StringComparison.Ordinal);
        Assert.Contains($"`{productVersion}`のclean-host試験CH-01〜06と本人loginはまだ実施していません", description, StringComparison.Ordinal);
        Assert.Contains("公開の条件で、公開前に実施します", description, StringComparison.Ordinal);
        Assert.Contains("前版の公開`v0.8.6`は、clean-host試験CH-01〜06と本人loginを公開前に実施しないまま公開されました", description, StringComparison.Ordinal);
        Assert.DoesNotContain($"公開`v{productVersion}`", description, StringComparison.Ordinal);
        Assert.DoesNotContain($"`{productVersion}`は公開済み", description, StringComparison.Ordinal);
        Assert.DoesNotContain("公開`v0.8.6`の出力", description, StringComparison.Ordinal);
        Assert.Contains("大学・高校の先生", description, StringComparison.Ordinal);
        foreach (string sheet in SheetNames)
        {
            Assert.Contains($"`{sheet}`", description, StringComparison.Ordinal);
        }

        // 利用者向けの解説は、開発者向け資料や実装の型名へ誘導しない。
        Assert.DoesNotContain("dev/docs", description, StringComparison.Ordinal);
        Assert.DoesNotContain("ResultsSheetWriter", description, StringComparison.Ordinal);
        Assert.DoesNotContain("TODO", description, StringComparison.Ordinal);
        Assert.DoesNotContain("TBD", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Description_covers_every_results_column_kind_status_code_and_total_column_of_the_writer()
    {
        string description = Read(DescriptionPath);
        Type writer = typeof(ResultsSheetWriter);
        List<(string Name, string Value)> constants = writer
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (field.Name, Value: (string)field.GetRawConstantValue()!))
            .ToList();

        List<string> suffixes = constants
            .Where(item => item.Name.EndsWith("Suffix", StringComparison.Ordinal))
            .Select(item => item.Value)
            .ToList();
        List<string> headers = constants
            .Where(item => item.Name.EndsWith("Header", StringComparison.Ordinal)
                && item.Name != nameof(ResultsSheetWriter.OverallScoreHeader))
            .Select(item => item.Value)
            .ToList();
        Assert.True(suffixes.Count >= 28, "The Results column suffix constants were not found.");
        Assert.Equal(5, headers.Count);

        foreach (string suffix in suffixes.Distinct(StringComparer.Ordinal))
        {
            Assert.Contains($".{suffix}`", description, StringComparison.Ordinal);
        }

        foreach (string header in headers)
        {
            Assert.Contains($"`{header}`", description, StringComparison.Ordinal);
        }

        List<string> statuses = typeof(ResultsStatusCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();
        Assert.Equal(12, statuses.Count);
        foreach (string status in statuses)
        {
            Assert.Matches(new Regex($@"^\| `{Regex.Escape(status)}` \|", RegexOptions.Multiline), description);
        }

        foreach (string evidenceSource in new[] { "PRIMARY_ANSWER", "SUPPORTING_COLUMN", "NONE" })
        {
            Assert.Contains($"`{evidenceSource}`", description, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Description_lists_every_config_references_and_run_field_written_by_the_app()
    {
        string description = Read(DescriptionPath);
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        InputSnapshot identity = new InputSnapshotService().Capture(input.Path);
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateDefinition());
        string targetDirectory = Path.Combine(input.Directory, "target");
        Directory.CreateDirectory(targetDirectory);

        using WorkingPackage package = WorkingPackage.Create(input.Path, Path.Combine(targetDirectory, "result.xlsx"));
        AppOwnedSheetNames names;
        using (SpreadsheetDocument document = package.OpenForEditing())
        {
            names = new AppOwnedSheetNameResolver().Resolve(document);
            _ = new ConfigSheetWriter().Write(document, snapshot, names);
            _ = new ReferenceAnswersSheetWriter().Write(
                document,
                snapshot,
                names,
                [
                    new ReferenceAnswerSheetRow
                    {
                        QuestionId = "Q1",
                        ModelId = "synthetic-model",
                        Answer = "synthetic reference",
                        StatusCode = ResultsStatusCodes.Success,
                        GeneratedAtUtc = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
                    },
                ]);
            new RunSheetWriter().Write(
                document,
                new RunSheetMetadata
                {
                    InputIdentity = identity,
                    DefinitionSha256 = snapshot.Sha256,
                    ApplicationIdentity = "StudyReportEvaluator.App/test",
                    CopilotSdkIdentity = "GitHub.Copilot.SDK/test",
                    CopilotCliIdentity = "copilot/test",
                    ModelIdentity = "synthetic-model",
                    StartedAtUtc = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
                    EndedAtUtc = new DateTimeOffset(2026, 9, 1, 10, 1, 0, TimeSpan.Zero),
                    SheetNames = names,
                });
        }

        using SpreadsheetDocument reopened = SpreadsheetDocument.Open(package.TemporaryPath, false);

        List<(string Column, string Header)> configHeaders = ReadRow(reopened, names.ConfigSheetName, 1);
        Assert.Equal(33, configHeaders.Count);
        foreach ((string column, string header) in configHeaders)
        {
            Assert.Contains($"| {column} | `{header}` |", description, StringComparison.Ordinal);
        }

        foreach (string recordType in ReadColumn(reopened, names.ConfigSheetName, "A").Skip(1).Distinct(StringComparer.Ordinal))
        {
            Assert.Matches(new Regex($@"^\| `{Regex.Escape(recordType)}` \|", RegexOptions.Multiline), description);
        }

        List<(string Column, string Header)> referenceHeaders = ReadRow(reopened, names.ReferencesSheetName, 1);
        Assert.Equal(8, referenceHeaders.Count);
        foreach ((string column, string header) in referenceHeaders)
        {
            Assert.Contains($"| {column} | `{header}` |", description, StringComparison.Ordinal);
        }

        List<string> runFields = ReadColumn(reopened, names.RunSheetName, "A");
        Assert.Equal("Field", runFields[0]);
        Assert.Equal(27, runFields.Count);
        foreach (string field in runFields.Skip(1))
        {
            Assert.Matches(new Regex($@"^\| `{Regex.Escape(field)}` \|", RegexOptions.Multiline), description);
        }
    }

    private static List<(string Column, string Text)> ReadRow(SpreadsheetDocument document, string sheetName, uint rowNumber) =>
        GetWorksheet(document, sheetName).Descendants<Row>()
            .Single(row => row.RowIndex?.Value == rowNumber)
            .Elements<Cell>()
            .Select(cell => (Regex.Replace(cell.CellReference!.Value!, @"\d+", string.Empty), CellText(cell)))
            .ToList();

    private static List<string> ReadColumn(SpreadsheetDocument document, string sheetName, string column) =>
        GetWorksheet(document, sheetName).Descendants<Row>()
            .SelectMany(row => row.Elements<Cell>())
            .Where(cell => Regex.Replace(cell.CellReference!.Value!, @"\d+", string.Empty) == column)
            .Select(CellText)
            .ToList();

    private static string CellText(Cell cell) =>
        cell.InlineString?.Text?.Text ?? cell.CellValue?.Text ?? string.Empty;

    private static Worksheet GetWorksheet(SpreadsheetDocument document, string name)
    {
        WorkbookPart workbookPart = document.WorkbookPart!;
        Sheet sheet = workbookPart.Workbook!.Descendants<Sheet>().Single(item => item.Name?.Value == name);
        return ((WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!)).Worksheet!;
    }

    private static QuantificationDefinition CreateDefinition() => new()
    {
        Id = "DEF-DOC",
        Name = "Synthetic documentation definition",
        Revision = "1",
        SourceSheet = "Original",
        HeaderRow = 1,
        FirstDataRow = 2,
        LastDataRow = 3,
        BasePoints = 60m,
        SpecialPoints = 10m,
        SimilarityPenaltyWeight = 0.1m,
        RoundingDigits = 1,
        Questions =
        [
            new QuestionDefinition
            {
                Id = "Q1",
                DisplayName = "Synthetic question",
                QuestionText = "Synthetic question text",
                PrimarySourceColumn = "G",
                SupportingSourceColumns = ["H"],
                Points = 30m,
                Enabled = true,
                Evaluators =
                [
                    new EvaluatorDefinition
                    {
                        Id = "E1",
                        DisplayName = "Synthetic custom evaluator",
                        Type = EvaluatorType.CustomPrompt,
                        Weight = 1m,
                        Range = new ScoreRange(0m, 10m),
                        CustomPromptTemplate = "{回答} {評価項目}",
                        Enabled = true,
                        Criteria =
                        [
                            new CriterionDefinition
                            {
                                Id = "C1",
                                DisplayName = "Synthetic criterion",
                                Description = "Synthetic criterion description",
                                Weight = 1m,
                                Enabled = true,
                            },
                        ],
                    },
                ],
                SpecialEvaluations =
                [
                    new SpecialEvaluationDefinition
                    {
                        Id = "S1",
                        DisplayName = "Synthetic special evaluation",
                        PrimarySourceColumn = "G",
                        SupportingSourceColumns = ["H"],
                        PromptTemplate = "Evaluate {回答}",
                    },
                ],
            },
        ],
    };

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string ProductVersionPrefix() =>
        Regex.Match(Read("Directory.Build.props"), @"<VersionPrefix>(?<value>[^<]+)</VersionPrefix>").Groups["value"].Value;

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
}
