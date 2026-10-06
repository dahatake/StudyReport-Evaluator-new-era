using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using StudyReportEvaluator.App.Tests.Evidence;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.E2E;

// Requirements: FR-026 (AC-027)
public sealed partial class ExternalSpreadsheetRecalculationSmokeTests
{
    private const string OptInEnvironmentVariable =
        "STUDY_REPORT_EVALUATOR_EXTERNAL_RECALC_SMOKE";

    [Fact]
    public void Optional_external_spreadsheet_recalculation_records_advisory_status_honestly()
    {
        bool optedIn = string.Equals(
            Environment.GetEnvironmentVariable(OptInEnvironmentVariable),
            "1",
            StringComparison.Ordinal);
        if (!optedIn)
        {
            return;
        }

        AdvisoryResult result = RunOptedInSmoke();
        AdvisoryEvidenceWriter.Write(
            "external-recalculation-smoke.json",
            "external_spreadsheet_recalculation",
            result.Status,
            result.Rationale);

        Assert.Contains(
            result.Status,
            new[] { "PASS", "SKIPPED_NOT_INSTALLED", "FAILED_ADVISORY" });
        Assert.Matches("^[a-z][a-z0-9_]{0,63}$", result.Rationale);
    }

    private static AdvisoryResult RunOptedInSmoke()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new AdvisoryResult("SKIPPED_NOT_INSTALLED", "supported_spreadsheet_unavailable");
        }

        Type? excelType = Type.GetTypeFromProgID("Excel.Application", throwOnError: false);
        if (excelType is null)
        {
            return new AdvisoryResult("SKIPPED_NOT_INSTALLED", "supported_spreadsheet_unavailable");
        }

        try
        {
            using TemporaryWorkbook workbook = CreateSyntheticFormulaWorkbook();
            FileFormatClassificationResult classification = new FileFormatClassifier().Classify(workbook.Path);
            if (!classification.IsAccepted)
            {
                return new AdvisoryResult("FAILED_ADVISORY", "synthetic_workbook_classification_failed");
            }

            using (SpreadsheetDocument validationDocument = SpreadsheetDocument.Open(workbook.Path, false))
            {
                if (new OpenXmlValidator().Validate(validationDocument).Any())
                {
                    return new AdvisoryResult("FAILED_ADVISORY", "synthetic_workbook_schema_invalid");
                }
            }

            RecalculationResult recalculation = RecalculateWithExcel(excelType, workbook.Path);
            if (!recalculation.IsSuccess)
            {
                return new AdvisoryResult("FAILED_ADVISORY", recalculation.Rationale);
            }

            using SpreadsheetDocument reopened = SpreadsheetDocument.Open(workbook.Path, false);
            WorkbookPart workbookPart = reopened.WorkbookPart
                ?? throw new InvalidDataException("The synthetic workbook part is missing.");
            Sheet resultsSheet = (workbookPart.Workbook
                    ?? throw new InvalidDataException("The synthetic workbook root is missing."))
                .Descendants<Sheet>()
                .Single(sheet => (sheet.Name?.Value ?? string.Empty).StartsWith(
                    AppOwnedSheetNameResolver.ResultsBaseName,
                    StringComparison.Ordinal));
            WorksheetPart resultsPart = (WorksheetPart)workbookPart.GetPartById(
                resultsSheet.Id?.Value
                ?? throw new InvalidDataException("The synthetic Results relationship is missing."));
            Cell[] formulas = (resultsPart.Worksheet
                    ?? throw new InvalidDataException("The synthetic Results worksheet is missing."))
                .Descendants<Cell>()
                .Where(cell => cell.CellFormula is not null)
                .ToArray();
            if (formulas.Length != 11)
            {
                return new AdvisoryResult("FAILED_ADVISORY", "formula_count_mismatch");
            }

            decimal[] values = new decimal[formulas.Length];
            for (int index = 0; index < formulas.Length; index++)
            {
                if (!decimal.TryParse(
                    formulas[index].CellValue?.Text,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out values[index]))
                {
                    return new AdvisoryResult("FAILED_ADVISORY", "formula_cache_missing");
                }
            }

            decimal[] expectedValues =
            [
                5m,
                50m,
                50m,
                50m,
                0.5m,
                20m,
                0m,
                60m,
                0m,
                80m,
                80m,
            ];
            if (!values.Order().SequenceEqual(expectedValues.Order()))
            {
                return new AdvisoryResult("FAILED_ADVISORY", "formula_value_mismatch");
            }

            return new AdvisoryResult("PASS", "synthetic_formula_recalculated");
        }
        catch
        {
            return new AdvisoryResult("FAILED_ADVISORY", "external_recalculation_failed");
        }
    }

    private static TemporaryWorkbook CreateSyntheticFormulaWorkbook()
    {
        TemporaryWorkbook workbook = X01SyntheticWorkbookFactory.Create();
        try
        {
            InputSnapshot inputIdentity = new InputSnapshotService().Capture(workbook.Path);
            QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateDefinition());
            using SpreadsheetDocument document = SpreadsheetDocument.Open(workbook.Path, true);
            AppOwnedSheetNames names = new AppOwnedSheetNameResolver().Resolve(document);
            ConfigCellAddressMap config = new ConfigSheetWriter().Write(document, snapshot, names);
            new ReferenceAnswersSheetWriter().Write(
                document,
                snapshot,
                names,
                [
                    new ReferenceAnswerSheetRow
                    {
                        QuestionId = "Q1",
                        ModelId = "auto",
                        Answer = "Synthetic reference answer",
                        StatusCode = ResultsStatusCodes.Success,
                        GeneratedAtUtc = DateTimeOffset.UtcNow,
                    },
                ]);
            _ = new ResultsSheetWriter().Write(
                document,
                snapshot,
                names,
                config,
                [CreateResultRow()]);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            new RunSheetWriter().Write(
                document,
                new RunSheetMetadata
                {
                    InputIdentity = inputIdentity,
                    DefinitionSha256 = snapshot.Sha256,
                    ApplicationIdentity = "StudyReportEvaluator.App/external-smoke",
                    CopilotSdkIdentity = "GitHub.Copilot.SDK/not-invoked",
                    CopilotCliIdentity = "copilot/not-invoked",
                    ModelIdentity = "synthetic-no-ai",
                    StartedAtUtc = now,
                    EndedAtUtc = now,
                    PlannedEvaluationCount = 1,
                    CompletedEvaluationCount = 1,
                    ErrorCount = 0,
                    SheetNames = names,
                });
            new CalculationPropertiesWriter().Write(document);
            return workbook;
        }
        catch
        {
            workbook.Dispose();
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static RecalculationResult RecalculateWithExcel(Type excelType, string workbookPath)
    {
        using ManualResetEventSlim completed = new(false);
        RecalculationResult result = new(false, "excel_start_failed");
        int processId = 0;
        Thread thread = new(() =>
        {
            object? applicationObject = null;
            object? workbooksObject = null;
            object? workbookObject = null;
            try
            {
                applicationObject = Activator.CreateInstance(excelType)
                    ?? throw new InvalidOperationException("Excel automation could not start.");
                result = new RecalculationResult(false, "excel_configuration_failed");
                dynamic application = applicationObject;
                application.Visible = false;
                application.DisplayAlerts = false;
                application.EnableEvents = false;
                application.AskToUpdateLinks = false;
                application.AutomationSecurity = 3;
                nint windowHandle = (nint)(int)application.Hwnd;
                _ = GetWindowThreadProcessId(windowHandle, out uint createdProcessId);
                processId = checked((int)createdProcessId);
                workbooksObject = application.Workbooks;
                dynamic workbooks = workbooksObject;
                result = new RecalculationResult(false, "workbook_open_failed");
                workbookObject = workbooks.Open(
                    workbookPath,
                    UpdateLinks: 0,
                    ReadOnly: false,
                    IgnoreReadOnlyRecommended: true,
                    AddToMru: false,
                    Local: true,
                    CorruptLoad: 0);
                dynamic workbook = workbookObject;
                result = new RecalculationResult(false, "full_recalculation_failed");
                application.CalculateFullRebuild();
                result = new RecalculationResult(false, "workbook_save_failed");
                workbook.Save();
                result = new RecalculationResult(false, "workbook_close_failed");
                workbook.Close(SaveChanges: false);
                workbookObject = null;
                result = new RecalculationResult(false, "excel_quit_failed");
                application.Quit();
                result = new RecalculationResult(true, "excel_recalculation_completed");
            }
            catch (COMException exception)
            {
                result = new RecalculationResult(
                    false,
                    result.Rationale + "_hresult_" + unchecked((uint)exception.HResult).ToString("x8", CultureInfo.InvariantCulture));
            }
            catch
            {
            }
            finally
            {
                ReleaseComObject(workbookObject);
                ReleaseComObject(workbooksObject);
                if (applicationObject is not null)
                {
                    try
                    {
                        ((dynamic)applicationObject).Quit();
                    }
                    catch
                    {
                    }
                }

                ReleaseComObject(applicationObject);
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "StudyReportEvaluator-Excel-Advisory-Smoke",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (completed.Wait(TimeSpan.FromSeconds(90)))
        {
            thread.Join();
            return result;
        }

        if (processId > 0)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
            catch
            {
            }
        }

        return new RecalculationResult(false, "external_recalculation_timed_out");
    }

    private static QuantificationDefinition CreateDefinition() =>
        new()
        {
            Id = "DEF-EXTERNAL-SMOKE",
            Name = "Synthetic external recalculation",
            Revision = "1",
            SourceSheet = "Original",
            HeaderRow = 1,
            FirstDataRow = 2,
            LastDataRow = 2,
            BasePoints = 60m,
            SpecialPoints = 0m,
            SimilarityPenaltyWeight = 0.1m,
            RoundingDigits = 1,
            Questions =
            [
                new QuestionDefinition
                {
                    Id = "Q1",
                    DisplayName = "Synthetic question",
                    QuestionText = "Synthetic question",
                    PrimarySourceColumn = "A",
                    Points = 40m,
                    Evaluators =
                    [
                        new EvaluatorDefinition
                        {
                            Id = "E1",
                            DisplayName = "Synthetic evaluator",
                            Type = EvaluatorType.CustomPrompt,
                            Weight = 1m,
                            Range = new ScoreRange(0m, 10m),
                            CustomPromptTemplate = "{回答} {評価項目}",
                            Criteria =
                            [
                                new CriterionDefinition
                                {
                                    Id = "C1",
                                    DisplayName = "Synthetic criterion",
                                    Description = "Synthetic criterion",
                                    Weight = 1m,
                                },
                            ],
                        },
                    ],
                },
            ],
        };

    private static ResultsSheetRowInput CreateResultRow() =>
        new()
        {
            SourceRowNumber = 2,
            Questions =
            [
                new QuestionResultInput
                {
                    QuestionId = "Q1",
                    Scorable = true,
                    Evaluators =
                    [
                        new EvaluatorResultInput
                        {
                            EvaluatorId = "E1",
                            Status = ResultsStatusCodes.Success,
                            AiResult = new QuantificationResult
                            {
                                EvaluatorId = "E1",
                                Criteria =
                                [
                                    new CriterionQuantificationResult
                                    {
                                        CriterionId = "C1",
                                        RawScore = 5m,
                                        Reason = "synthetic reason",
                                        Evidence = "",
                                        EvidenceSource = EvidenceSourceKind.None,
                                        EvidenceSourceColumnId = "",
                                    },
                                ],
                            },
                        },
                    ],
                    Similarity = new SimilarityResultInput
                    {
                        AiRaw = 0m,
                        Reason = string.Empty,
                        Status = ResultsStatusCodes.Success,
                    },
                },
            ],
        };

    [SupportedOSPlatform("windows")]
    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

#pragma warning disable SYSLIB1054 // Test-only COM process containment requires one stable Win32 lookup.
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);
#pragma warning restore SYSLIB1054

    private sealed record AdvisoryResult(string Status, string Rationale);

    private sealed record RecalculationResult(bool IsSuccess, string Rationale);
}
