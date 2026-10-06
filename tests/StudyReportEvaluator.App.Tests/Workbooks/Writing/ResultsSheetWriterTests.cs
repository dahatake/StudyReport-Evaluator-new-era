using System.Collections.Immutable;
using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Formulas;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Writing;

// Requirements: FR-023 (AC-024), FR-024 (AC-025)
public sealed class ResultsSheetWriterTests
{
    private const string ReasonA = "=REASON-A-CANARY";
    private const string EvidenceA = "+EVIDENCE-A-CANARY";
    private const string ReasonB = "-REASON-B-CANARY";
    private const string EvidenceB = "@EVIDENCE-B-CANARY";

    [Fact]
    public void Reopened_results_use_enabled_stable_id_columns_safe_literals_golden_formulas_and_rounded_cached_oracle()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        AddSheets(
            input.Path,
            AppOwnedSheetNameResolver.ConfigBaseName,
            AppOwnedSheetNameResolver.ResultsBaseName,
            AppOwnedSheetNameResolver.RunBaseName);
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateOracleDefinition(2, 2));
        AppOwnedSheetNames names;
        ConfigCellAddressMap config;
        ResultsSheetWriteResult writeResult;

        using (SpreadsheetDocument document = SpreadsheetDocument.Open(input.Path, true))
        {
            names = new AppOwnedSheetNameResolver().Resolve(document);
            Assert.Equal("Quantification_Config (2)", names.ConfigSheetName);
            Assert.Equal("Quantification_Results (2)", names.ResultsSheetName);
            config = new ConfigSheetWriter().Write(document, snapshot, names);
            writeResult = new ResultsSheetWriter().Write(
                document,
                snapshot,
                names,
                config,
                [OracleRow(2)]);
        }

        using SpreadsheetDocument reopened = SpreadsheetDocument.Open(input.Path, false);
        Assert.Empty(new OpenXmlValidator().Validate(reopened, TestContext.Current.CancellationToken));
        Worksheet worksheet = GetWorksheet(reopened, names.ResultsSheetName);
        Assert.Equal(names.ResultsSheetName, writeResult.SheetName);
        Assert.Equal(1, writeResult.HeaderRow);
        Assert.Equal(1, writeResult.DataRowCount);
        Assert.Equal(36, writeResult.ColumnCount);
        Assert.Equal(13, writeResult.FormulaCells.Length);
        Assert.Equal(
            writeResult.FormulaCells.Length,
            writeResult.FormulaCells.Select(item => item.Definition.Target).Distinct().Count());
        Assert.Equal(99.1m, Assert.Single(
            writeResult.FormulaCells,
            item => item.Definition.Identity.Field == ResultsSheetWriter.FinalScoreHeader).CachedValue);
        Assert.All(
            writeResult.FormulaCells,
            item => Assert.Contains("<redacted>", item.ToString(), StringComparison.Ordinal));
        Assert.Contains("<redacted>", writeResult.ToString(), StringComparison.Ordinal);

        string[] expectedHeaders =
        [
            "SourceRow",
            "Q1.E1.C-A.Scorable",
            "Q1.E1.C-A.AI_Raw",
            "Q1.E1.C-A.Override",
            "Q1.E1.C-A.Effective_Raw",
            "Q1.E1.C-A.Normalized",
            "Q1.E1.C-A.Reason",
            "Q1.E1.C-A.Evidence",
            "Q1.E1.C-A.Evidence_Source",
            "Q1.E1.C-A.Evidence_SourceColumn",
            "Q1.E1.C-A.Status",
            "Q1.E1.C-B.Scorable",
            "Q1.E1.C-B.AI_Raw",
            "Q1.E1.C-B.Override",
            "Q1.E1.C-B.Effective_Raw",
            "Q1.E1.C-B.Normalized",
            "Q1.E1.C-B.Reason",
            "Q1.E1.C-B.Evidence",
            "Q1.E1.C-B.Evidence_Source",
            "Q1.E1.C-B.Evidence_SourceColumn",
            "Q1.E1.C-B.Status",
            "Q1.E1.Evaluator_Score",
            "Q1.Answer_Present",
            "Q1.Question_Normalized",
            "Q1.Question_Rate",
            "Q1.Question_Earned",
            "Q1.Similarity_AI_Raw",
            "Q1.Similarity_Reason",
            "Q1.Similarity_Status",
            "Q1.Similarity_Penalty",
            "Q1.Similarity_Peer_Max",
            "Q1.Similarity_Peer_Row",
            "Base_Points",
            "Special_Earned",
            "Final_Raw",
            "Final_Score",
        ];
        Row header = worksheet.Descendants<Row>().Single(row => row.RowIndex?.Value == 1);
        Assert.Equal(expectedHeaders, header.Elements<Cell>().Select(cell => cell.InnerText));
        Assert.All(header.Elements<Cell>(), cell => Assert.Equal(CellValues.InlineString, cell.DataType?.Value));
        Assert.DoesNotContain("C-DISABLED", header.InnerText, StringComparison.Ordinal);
        Assert.DoesNotContain("E-DISABLED", header.InnerText, StringComparison.Ordinal);
        Assert.DoesNotContain("Q-DISABLED", header.InnerText, StringComparison.Ordinal);

        AssertNumber(CellByHeader(worksheet, "SourceRow", 2), "2");
        AssertNumber(CellByHeader(worksheet, "Q1.E1.C-A.Scorable", 2), "1");
        AssertNumber(CellByHeader(worksheet, "Q1.E1.C-A.AI_Raw", 2), "25");
        AssertTrulyBlank(CellByHeader(worksheet, "Q1.E1.C-A.Override", 2));
        AssertNumber(CellByHeader(worksheet, "Q1.E1.C-B.AI_Raw", 2), "8");
        AssertInline(CellByHeader(worksheet, "Q1.E1.C-A.Reason", 2), ReasonA);
        AssertInline(CellByHeader(worksheet, "Q1.E1.C-A.Evidence", 2), EvidenceA);
        AssertInline(CellByHeader(worksheet, "Q1.E1.C-A.Evidence_Source", 2), "PRIMARY_ANSWER");
        AssertInline(CellByHeader(worksheet, "Q1.E1.C-A.Evidence_SourceColumn", 2), "G");
        AssertInline(CellByHeader(worksheet, "Q1.E1.C-B.Evidence_Source", 2), "SUPPORTING_COLUMN");
        AssertInline(CellByHeader(worksheet, "Q1.E1.C-B.Evidence_SourceColumn", 2), "H");

        string aMin = ConfigReference(config.CriterionMinimumCells["C-A"]);
        string aMax = ConfigReference(config.CriterionMaximumCells["C-A"]);
        string weightA = ConfigReference(config.CriterionWeightCells["C-A"]);
        string weightB = ConfigReference(config.CriterionWeightCells["C-B"]);
        AssertFormulaCache(CellByHeader(worksheet, "Q1.E1.C-A.Effective_Raw", 2), "25");
        AssertFormulaCache(CellByHeader(worksheet, "Q1.E1.C-A.Normalized", 2), "83.3");
        AssertFormulaCache(CellByHeader(worksheet, "Q1.E1.C-B.Normalized", 2), "77.8");
        Cell evaluatorCell = CellByHeader(worksheet, "Q1.E1.Evaluator_Score", 2);
        string evaluatorFormula = evaluatorCell.CellFormula?.Text ?? string.Empty;
        AssertFormulaCache(evaluatorCell, "81.5");
        Assert.DoesNotContain("C-DISABLED", string.Concat(worksheet.Descendants<CellFormula>().Select(formula => formula.Text)), StringComparison.Ordinal);
        Assert.DoesNotContain("83.3", evaluatorFormula, StringComparison.Ordinal);
        Assert.Contains(weightA, evaluatorFormula, StringComparison.Ordinal);
        Assert.Contains(weightB, evaluatorFormula, StringComparison.Ordinal);
        AssertFormulaCache(CellByHeader(worksheet, "Q1.Question_Normalized", 2), "81.5");
        AssertFormulaCache(CellByHeader(worksheet, "Q1.Question_Rate", 2), "0.815");
        AssertFormulaCache(CellByHeader(worksheet, "Q1.Question_Earned", 2), "2.4");
        AssertFormulaCache(CellByHeader(worksheet, "Q1.Similarity_Penalty", 2), "0.3");
        AssertFormulaCache(CellByHeader(worksheet, "Base_Points", 2), "97");
        AssertFormulaCache(CellByHeader(worksheet, "Special_Earned", 2), "0");
        AssertFormulaCache(CellByHeader(worksheet, "Final_Raw", 2), "99.1");
        AssertFormulaCache(CellByHeader(worksheet, "Final_Score", 2), "99.1");
        Assert.All(
            worksheet.Descendants<CellFormula>(),
            formula => Assert.False(formula.Text.StartsWith("=", StringComparison.Ordinal)));

        decimal oracleA = decimal.Round(((25m - 0m) / (30m - 0m)) * 100m, 1, MidpointRounding.AwayFromZero);
        decimal oracleB = decimal.Round(((8m - 1m) / (10m - 1m)) * 100m, 1, MidpointRounding.AwayFromZero);
        decimal oracleEvaluator = decimal.Round(((oracleA * 2m) + (oracleB * 1m)) / 3m, 1, MidpointRounding.AwayFromZero);
        Assert.Equal(83.3m, oracleA);
        Assert.Equal(77.8m, oracleB);
        Assert.Equal(81.5m, oracleEvaluator);
        Assert.Equal(oracleA, CachedDecimal(CellByHeader(worksheet, "Q1.E1.C-A.Normalized", 2)));
        Assert.Equal(oracleB, CachedDecimal(CellByHeader(worksheet, "Q1.E1.C-B.Normalized", 2)));
        Assert.Equal(oracleEvaluator, CachedDecimal(evaluatorCell));

        DataValidation[] validations = worksheet.Descendants<DataValidation>().ToArray();
        Assert.Equal(2, validations.Length);
        AssertDecimalValidation(
            validations.Single(item => item.SequenceOfReferences?.InnerText == CellByHeader(worksheet, "Q1.E1.C-A.Override", 2).CellReference?.Value),
            aMin,
            aMax);
        Assert.NotNull(GetWorksheet(reopened, AppOwnedSheetNameResolver.ResultsBaseName));
    }

    [Fact]
    public void Empty_failed_cancelled_and_valid_override_rows_keep_blank_semantics_and_custom_empty_validation()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateSingleCriterionDefinition(2, 5));
        AppOwnedSheetNames names;
        using (SpreadsheetDocument document = SpreadsheetDocument.Open(input.Path, true))
        {
            names = new AppOwnedSheetNameResolver().Resolve(document);
            ConfigCellAddressMap config = new ConfigSheetWriter().Write(document, snapshot, names);
            new ResultsSheetWriter().Write(
                document,
                snapshot,
                names,
                config,
                [
                    SingleCriterionRow(2, scorable: false, ResultsStatusCodes.Empty),
                    SingleCriterionRow(3, scorable: true, ResultsStatusCodes.Cancelled),
                    SingleCriterionRow(4, scorable: true, ResultsStatusCodes.AiOutputInvalid, overrideValue: "5"),
                    SingleCriterionRow(5, scorable: true, ResultsStatusCodes.Success, overrideValue: "4", aiRaw: 11m),
                ]);
        }

        using SpreadsheetDocument reopened = SpreadsheetDocument.Open(input.Path, false);
        Worksheet worksheet = GetWorksheet(reopened, names.ResultsSheetName);
        Assert.Empty(new OpenXmlValidator().Validate(reopened, TestContext.Current.CancellationToken));

        AssertNumber(CellByHeader(worksheet, "Q1.E1.C1.Scorable", 2), "0");
        AssertTrulyBlank(CellByHeader(worksheet, "Q1.E1.C1.AI_Raw", 2));
        AssertTrulyBlank(CellByHeader(worksheet, "Q1.E1.C1.Override", 2));
        AssertFormulaWithoutCache(CellByHeader(worksheet, "Q1.E1.C1.Effective_Raw", 2));
        AssertFormulaWithoutCache(CellByHeader(worksheet, "Q1.Question_Normalized", 2));
        AssertFormulaCache(CellByHeader(worksheet, "Q1.Question_Rate", 2), "0");
        AssertFormulaCache(CellByHeader(worksheet, "Q1.Question_Earned", 2), "0");
        AssertFormulaCache(CellByHeader(worksheet, "Final_Score", 2), "99");
        AssertInline(CellByHeader(worksheet, "Q1.E1.C1.Status", 2), ResultsStatusCodes.Empty);

        AssertNumber(CellByHeader(worksheet, "Q1.E1.C1.Scorable", 3), "1");
        AssertTrulyBlank(CellByHeader(worksheet, "Q1.E1.C1.AI_Raw", 3));
        AssertTrulyBlank(CellByHeader(worksheet, "Q1.E1.C1.Override", 3));
        AssertFormulaWithoutCache(CellByHeader(worksheet, "Q1.E1.C1.Effective_Raw", 3));
        AssertFormulaWithoutCache(CellByHeader(worksheet, "Final_Score", 3));
        AssertInline(CellByHeader(worksheet, "Q1.E1.C1.Status", 3), ResultsStatusCodes.Cancelled);

        AssertTrulyBlank(CellByHeader(worksheet, "Q1.E1.C1.AI_Raw", 4));
        AssertNumber(CellByHeader(worksheet, "Q1.E1.C1.Override", 4), "5");
        AssertFormulaCache(CellByHeader(worksheet, "Q1.E1.C1.Effective_Raw", 4), "5");
        AssertFormulaCache(CellByHeader(worksheet, "Q1.E1.C1.Normalized", 4), "50");
        AssertFormulaCache(CellByHeader(worksheet, "Final_Score", 4), "99.5");
        AssertInline(CellByHeader(worksheet, "Q1.E1.C1.Status", 4), ResultsStatusCodes.AiOutputInvalid);

        AssertNumber(CellByHeader(worksheet, "Q1.E1.C1.AI_Raw", 5), "11");
        AssertNumber(CellByHeader(worksheet, "Q1.E1.C1.Override", 5), "4");
        Assert.Equal("4", CellByHeader(worksheet, "Q1.E1.C1.Effective_Raw", 5).CellValue?.Text);
        Assert.Equal("40", CellByHeader(worksheet, "Q1.E1.C1.Normalized", 5).CellValue?.Text);
        string effectiveFormula = CellByHeader(worksheet, "Q1.E1.C1.Effective_Raw", 5).CellFormula?.Text ?? string.Empty;
        string overrideReference = CellByHeader(worksheet, "Q1.E1.C1.Override", 5).CellReference?.Value ?? string.Empty;
        Assert.Contains(overrideReference + "=\"\"", effectiveFormula, StringComparison.Ordinal);
        Assert.Contains("ISNUMBER", effectiveFormula, StringComparison.Ordinal);
        Assert.DoesNotContain("MIN(", effectiveFormula, StringComparison.Ordinal);
        Assert.DoesNotContain("MAX(", effectiveFormula, StringComparison.Ordinal);

        DataValidation[] validations = worksheet.Descendants<DataValidation>().ToArray();
        Assert.Equal(4, validations.Length);
        string emptyOverrideReference = CellByHeader(worksheet, "Q1.E1.C1.Override", 2).CellReference?.Value
            ?? throw new InvalidDataException("Override reference is missing.");
        DataValidation emptyValidation = validations.Single(item => item.SequenceOfReferences?.InnerText == emptyOverrideReference);
        Assert.Equal(DataValidationValues.Custom, emptyValidation.Type?.Value);
        Assert.True(emptyValidation.AllowBlank?.Value == true);
        Assert.Contains(emptyOverrideReference + "=\"\"", emptyValidation.Formula1?.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.All(
            validations.Where(item => item.SequenceOfReferences?.InnerText != emptyOverrideReference),
            item => Assert.Equal(DataValidationValues.Decimal, item.Type?.Value));
    }

    [Fact]
    public void Special_and_similarity_results_are_separate_literals_with_excel_owned_final_score()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        QuantificationDefinition source = CreateSingleCriterionDefinition(2, 2);
        SpecialEvaluationDefinition first = new()
        {
            Id = "S1",
            DisplayName = "Student prompt",
            PrimarySourceColumn = "G",
            PromptTemplate = "Evaluate {回答}",
        };
        SpecialEvaluationDefinition second = first with { Id = "S2", DisplayName = "Prompt considerations" };
        QuantificationDefinition definition = source with
        {
            BasePoints = 89m,
            SpecialPoints = 10m,
            Questions = [source.Questions[0] with { SpecialEvaluations = [first, second] }],
        };
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
        AppOwnedSheetNames names;
        ResultsSheetWriteResult result;
        using (SpreadsheetDocument document = SpreadsheetDocument.Open(input.Path, true))
        {
            names = new AppOwnedSheetNameResolver().Resolve(document);
            ConfigCellAddressMap config = new ConfigSheetWriter().Write(document, snapshot, names);
            result = new ResultsSheetWriter().Write(
                document,
                snapshot,
                names,
                config,
                [
                    new ResultsSheetRowInput
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
                                            Criteria = [CriterionResult("C1", 5m, "reason", "", EvidenceSourceKind.None, "")],
                                        },
                                    },
                                ],
                                SpecialResults =
                                [
                                    new SpecialResultInput { SpecialEvaluationId = "S1", AiRaw = 0.8m, Status = ResultsStatusCodes.Success },
                                    new SpecialResultInput { SpecialEvaluationId = "S2", AiRaw = 0.6m, Status = ResultsStatusCodes.Success },
                                ],
                                Similarity = new SimilarityResultInput
                                {
                                    AiRaw = 0.5m,
                                    Reason = "semantic overlap",
                                    Status = ResultsStatusCodes.Success,
                                },
                            },
                        ],
                    },
                ]);
        }

        using SpreadsheetDocument reopened = SpreadsheetDocument.Open(input.Path, false);
        Worksheet worksheet = GetWorksheet(reopened, names.ResultsSheetName);
        Assert.Equal(39, result.ColumnCount);
        AssertNumber(CellByHeader(worksheet, "Q1.S1.Special_AI_Raw", 2), "0.8");
        AssertNumber(CellByHeader(worksheet, "Q1.S2.Special_AI_Raw", 2), "0.6");
        AssertFormulaCache(CellByHeader(worksheet, "Q1.Special_Question_Rate", 2), "0.7");
        AssertNumber(CellByHeader(worksheet, "Q1.Similarity_AI_Raw", 2), "0.5");
        AssertFormulaCache(CellByHeader(worksheet, "Q1.Similarity_Penalty", 2), "0.1");
        AssertFormulaCache(CellByHeader(worksheet, "Base_Points", 2), "89");
        AssertFormulaCache(CellByHeader(worksheet, "Special_Earned", 2), "7");
        AssertFormulaCache(CellByHeader(worksheet, "Final_Raw", 2), "96.4");
        AssertFormulaCache(CellByHeader(worksheet, "Final_Score", 2), "96.4");
        Assert.Empty(new OpenXmlValidator().Validate(reopened, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("PRIVATE-NONNUMERIC-CANARY", "OVERRIDE_NOT_NUMERIC")]
    [InlineData("-0.1", "OVERRIDE_OUT_OF_RANGE")]
    [InlineData("10.1", "OVERRIDE_OUT_OF_RANGE")]
    public void Invalid_app_side_override_returns_a_field_error_and_refuses_before_results_mutation(
        string overrideValue,
        string expectedCode)
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateSingleCriterionDefinition(2, 2));
        using SpreadsheetDocument document = SpreadsheetDocument.Open(input.Path, true);
        AppOwnedSheetNames names = new AppOwnedSheetNameResolver().Resolve(document);
        ConfigCellAddressMap config = new ConfigSheetWriter().Write(document, snapshot, names);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("Synthetic workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("Synthetic workbook root is missing.");
        int sheetCount = workbook.Descendants<Sheet>().Count();
        int worksheetPartCount = workbookPart.WorksheetParts.Count();
        ResultsSheetRowInput row = SingleCriterionRow(
            2,
            scorable: true,
            ResultsStatusCodes.AiOutputInvalid,
            overrideValue: overrideValue);

        ResultsSheetValidationException exception = Assert.Throws<ResultsSheetValidationException>(
            () => new ResultsSheetWriter().Write(document, snapshot, names, config, [row]));

        ResultsSheetValidationError error = Assert.Single(exception.Errors, item => item.Code == expectedCode);
        Assert.Equal("Override", error.Field);
        Assert.Equal("C1", error.NodeId);
        Assert.Equal(sheetCount, workbook.Descendants<Sheet>().Count());
        Assert.Equal(worksheetPartCount, workbookPart.WorksheetParts.Count());
        Assert.DoesNotContain(workbook.Descendants<Sheet>(), sheet => sheet.Name?.Value == names.ResultsSheetName);
        Assert.DoesNotContain(overrideValue, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(overrideValue, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(overrideValue, row.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Override_for_scorable_zero_is_rejected_before_mutation()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateSingleCriterionDefinition(2, 2));
        using SpreadsheetDocument document = SpreadsheetDocument.Open(input.Path, true);
        AppOwnedSheetNames names = new AppOwnedSheetNameResolver().Resolve(document);
        ConfigCellAddressMap config = new ConfigSheetWriter().Write(document, snapshot, names);
        int before = document.WorkbookPart?.WorksheetParts.Count() ?? 0;

        ResultsSheetValidationException exception = Assert.Throws<ResultsSheetValidationException>(
            () => new ResultsSheetWriter().Write(
                document,
                snapshot,
                names,
                config,
                [SingleCriterionRow(2, scorable: false, ResultsStatusCodes.Empty, overrideValue: "5")]));

        Assert.Contains(exception.Errors, error => error.Code == "OVERRIDE_NOT_ALLOWED" && error.Field == "Override");
        Assert.Equal(before, document.WorkbookPart?.WorksheetParts.Count() ?? 0);
    }

    [Fact]
    public void Reserved_results_name_must_still_be_absent_when_write_starts()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateSingleCriterionDefinition(2, 2));
        using SpreadsheetDocument document = SpreadsheetDocument.Open(input.Path, true);
        AppOwnedSheetNames names = new AppOwnedSheetNameResolver().Resolve(document);
        ConfigCellAddressMap config = new ConfigSheetWriter().Write(document, snapshot, names);
        AddSheet(document, names.ResultsSheetName);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("Synthetic workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("Synthetic workbook root is missing.");
        int sheetCount = workbook.Descendants<Sheet>().Count();
        int partCount = workbookPart.WorksheetParts.Count();
        string sentinelXml = GetWorksheet(document, names.ResultsSheetName).OuterXml;

        ResultsSheetValidationException exception = Assert.Throws<ResultsSheetValidationException>(
            () => new ResultsSheetWriter().Write(
                document,
                snapshot,
                names,
                config,
                [SingleCriterionRow(2, true, ResultsStatusCodes.Cancelled)]));

        Assert.Contains(exception.Errors, error => error.Code == "RESULTS_SHEET_ALREADY_EXISTS");
        Assert.Equal(sheetCount, workbook.Descendants<Sheet>().Count());
        Assert.Equal(partCount, workbookPart.WorksheetParts.Count());
        Assert.Equal(sentinelXml, GetWorksheet(document, names.ResultsSheetName).OuterXml);
    }

    [Fact]
    public void Excel_column_limit_is_rejected_before_results_mutation()
    {
        const int criterionCount = 1_639;
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(
            CreateManyCriteriaDefinition(criterionCount));
        using SpreadsheetDocument document = SpreadsheetDocument.Open(input.Path, true);
        AppOwnedSheetNames names = new AppOwnedSheetNameResolver().Resolve(document);
        ConfigCellAddressMap config = new ConfigSheetWriter().Write(document, snapshot, names);
        int before = document.WorkbookPart?.WorksheetParts.Count() ?? 0;

        ResultsSheetValidationException exception = Assert.Throws<ResultsSheetValidationException>(
            () => new ResultsSheetWriter().Write(document, snapshot, names, config, []));

        ResultsSheetValidationError error = Assert.Single(exception.Errors, item => item.Code == "COLUMN_LIMIT_EXCEEDED");
        Assert.Equal((criterionCount * 10 + 16).ToString(CultureInfo.InvariantCulture), error.SafeOffendingValue);
        Assert.Equal(before, document.WorkbookPart?.WorksheetParts.Count() ?? 0);
        Assert.DoesNotContain(Workbook(document).Descendants<Sheet>(), sheet => sheet.Name?.Value == names.ResultsSheetName);
    }

    [Fact]
    public void Formula_function_argument_preflight_rejects_before_results_mutation()
    {
        const int criterionCount = 256;
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(
            CreateManyCriteriaDefinition(criterionCount));
        using SpreadsheetDocument document = SpreadsheetDocument.Open(input.Path, true);
        AppOwnedSheetNames names = new AppOwnedSheetNameResolver().Resolve(document);
        ConfigCellAddressMap config = new ConfigSheetWriter().Write(document, snapshot, names);
        int before = document.WorkbookPart?.WorksheetParts.Count() ?? 0;

        ResultsSheetValidationException exception = Assert.Throws<ResultsSheetValidationException>(
            () => new ResultsSheetWriter().Write(
                document,
                snapshot,
                names,
                config,
                [SingleEvaluatorRow(2, ResultsStatusCodes.Cancelled)]));

        Assert.Contains(
            exception.Errors,
            error => error.Code == "FUNCTION_ARGUMENT_LIMIT_EXCEEDED"
                && error.NodeKind == "Evaluator"
                && error.NodeId == "E1"
                && error.SafeOffendingValue == "256");
        Assert.Equal(before, document.WorkbookPart?.WorksheetParts.Count() ?? 0);
            Assert.DoesNotContain(Workbook(document).Descendants<Sheet>(), sheet => sheet.Name?.Value == names.ResultsSheetName);
    }

    private static QuantificationDefinition CreateOracleDefinition(int firstRow, int lastRow)
    {
        EvaluatorDefinition enabledEvaluator = new()
        {
            Id = "E1",
            DisplayName = "Enabled evaluator",
            Type = EvaluatorType.CustomPrompt,
            Weight = 7m,
            Range = new ScoreRange(1m, 10m),
            CustomPromptTemplate = "{回答} {評価項目}",
            Enabled = true,
            Criteria =
            [
                new CriterionDefinition
                {
                    Id = "C-A",
                    DisplayName = "Criterion A",
                    Description = "Synthetic criterion A",
                    Weight = 2m,
                    Range = new ScoreRange(0m, 30m),
                    Enabled = true,
                },
                new CriterionDefinition
                {
                    Id = "C-DISABLED",
                    DisplayName = "Disabled criterion",
                    Description = "Synthetic disabled criterion",
                    Weight = 999m,
                    Enabled = false,
                },
                new CriterionDefinition
                {
                    Id = "C-B",
                    DisplayName = "Criterion B",
                    Description = "Synthetic criterion B",
                    Weight = 1m,
                    Enabled = true,
                },
            ],
        };
        EvaluatorDefinition disabledEvaluator = new()
        {
            Id = "E-DISABLED",
            DisplayName = "Disabled evaluator",
            Type = EvaluatorType.CustomPrompt,
            Weight = 999m,
            Range = new ScoreRange(0m, 100m),
            CustomPromptTemplate = "{回答} {評価項目}",
            Enabled = false,
            Criteria =
            [
                new CriterionDefinition
                {
                    Id = "C-DESCENDANT",
                    DisplayName = "Disabled descendant",
                    Description = "Synthetic disabled descendant",
                    Weight = 999m,
                    Enabled = true,
                },
            ],
        };
        QuestionDefinition enabledQuestion = new()
        {
            Id = "Q1",
            DisplayName = "Enabled question",
            QuestionText = "Synthetic question",
            PrimarySourceColumn = "G",
            SupportingSourceColumns = ["H"],
            Points = 3m,
            Enabled = true,
            Evaluators = [enabledEvaluator, disabledEvaluator],
        };
        QuestionDefinition disabledQuestion = new()
        {
            Id = "Q-DISABLED",
            DisplayName = "Disabled question",
            QuestionText = "Synthetic disabled question",
            PrimarySourceColumn = "K",
            Points = 999m,
            Enabled = false,
            Evaluators =
            [
                new EvaluatorDefinition
                {
                    Id = "E-DESCENDANT",
                    DisplayName = "Disabled question evaluator",
                    Type = EvaluatorType.CustomPrompt,
                    Weight = 999m,
                    Range = new ScoreRange(0m, 100m),
                    CustomPromptTemplate = "{回答} {評価項目}",
                    Enabled = true,
                    Criteria =
                    [
                        new CriterionDefinition
                        {
                            Id = "C-Q-DESCENDANT",
                            DisplayName = "Disabled question criterion",
                            Description = "Synthetic disabled question criterion",
                            Weight = 999m,
                            Enabled = true,
                        },
                    ],
                },
            ],
        };
        return new QuantificationDefinition
        {
            Id = "DEF-X04-ORACLE",
            Name = "X-04 oracle definition",
            Revision = "1",
            SourceSheet = "Original",
            HeaderRow = 1,
            FirstDataRow = firstRow,
            LastDataRow = lastRow,
            BasePoints = 97m,
            RoundingDigits = 1,
            Questions = [enabledQuestion, disabledQuestion],
        };
    }

    private static QuantificationDefinition CreateSingleCriterionDefinition(int firstRow, int lastRow) =>
        new()
        {
            Id = "DEF-X04-SINGLE",
            Name = "X-04 single criterion definition",
            Revision = "1",
            SourceSheet = "Original",
            HeaderRow = 1,
            FirstDataRow = firstRow,
            LastDataRow = lastRow,
            BasePoints = 99m,
            RoundingDigits = 1,
            Questions =
            [
                new QuestionDefinition
                {
                    Id = "Q1",
                    DisplayName = "Question",
                    QuestionText = "Synthetic question",
                    PrimarySourceColumn = "G",
                    Points = 1m,
                    Evaluators =
                    [
                        new EvaluatorDefinition
                        {
                            Id = "E1",
                            DisplayName = "Evaluator",
                            Type = EvaluatorType.CustomPrompt,
                            Weight = 1m,
                            Range = new ScoreRange(0m, 10m),
                            CustomPromptTemplate = "{回答} {評価項目}",
                            Criteria =
                            [
                                new CriterionDefinition
                                {
                                    Id = "C1",
                                    DisplayName = "Criterion",
                                    Description = "Synthetic criterion",
                                    Weight = 1m,
                                },
                            ],
                        },
                    ],
                },
            ],
        };

    private static QuantificationDefinition CreateManyCriteriaDefinition(int criterionCount)
    {
        ImmutableArray<CriterionDefinition> criteria = Enumerable.Range(1, criterionCount)
            .Select(index => new CriterionDefinition
            {
                Id = $"C{index.ToString(CultureInfo.InvariantCulture)}",
                DisplayName = $"Criterion {index.ToString(CultureInfo.InvariantCulture)}",
                Description = "Synthetic criterion",
                Weight = 1m,
            })
            .ToImmutableArray();
        QuantificationDefinition single = CreateSingleCriterionDefinition(2, 2);
        EvaluatorDefinition evaluator = single.Questions[0].Evaluators[0] with { Criteria = criteria };
        QuestionDefinition question = single.Questions[0] with { Evaluators = [evaluator] };
        return single with
        {
            Id = $"DEF-X04-MANY-{criterionCount.ToString(CultureInfo.InvariantCulture)}",
            Questions = [question],
        };
    }

    private static ResultsSheetRowInput OracleRow(int sourceRow) =>
        new()
        {
            SourceRowNumber = sourceRow,
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
                                    CriterionResult("C-B", 8m, ReasonB, EvidenceB, EvidenceSourceKind.SupportingColumn, "H"),
                                    CriterionResult("C-A", 25m, ReasonA, EvidenceA, EvidenceSourceKind.PrimaryAnswer, "G"),
                                ],
                            },
                        },
                    ],
                    Similarity = new SimilarityResultInput
                    {
                        AiRaw = 0.99m,
                        Reason = "synthetic similarity",
                        Status = ResultsStatusCodes.Success,
                    },
                },
            ],
        };

    private static ResultsSheetRowInput SingleCriterionRow(
        int sourceRow,
        bool scorable,
        string status,
        string? overrideValue = null,
        decimal? aiRaw = null) =>
        new()
        {
            SourceRowNumber = sourceRow,
            Questions =
            [
                new QuestionResultInput
                {
                    QuestionId = "Q1",
                    Scorable = scorable,
                    Evaluators =
                    [
                        new EvaluatorResultInput
                        {
                            EvaluatorId = "E1",
                            Status = status,
                            AiResult = aiRaw is decimal raw
                                ? new QuantificationResult
                                {
                                    EvaluatorId = "E1",
                                    Criteria =
                                    [
                                        CriterionResult("C1", raw, "synthetic reason", "", EvidenceSourceKind.None, ""),
                                    ],
                                }
                                : null,
                            Overrides = overrideValue is null
                                ? []
                                : [new CriterionOverrideInput { CriterionId = "C1", Value = overrideValue }],
                        },
                    ],
                    Similarity = new SimilarityResultInput
                    {
                        AiRaw = 0m,
                        Reason = string.Empty,
                        Status = scorable ? ResultsStatusCodes.Success : ResultsStatusCodes.Empty,
                    },
                },
            ],
        };

    private static ResultsSheetRowInput SingleEvaluatorRow(int sourceRow, string status) =>
        new()
        {
            SourceRowNumber = sourceRow,
            Questions =
            [
                new QuestionResultInput
                {
                    QuestionId = "Q1",
                    Scorable = true,
                    Evaluators = [new EvaluatorResultInput { EvaluatorId = "E1", Status = status }],
                },
            ],
        };

    private static CriterionQuantificationResult CriterionResult(
        string id,
        decimal score,
        string reason,
        string evidence,
        EvidenceSourceKind source,
        string sourceColumn) =>
        new()
        {
            CriterionId = id,
            RawScore = score,
            Reason = reason,
            Evidence = evidence,
            EvidenceSource = source,
            EvidenceSourceColumnId = sourceColumn,
        };

    private static void AssertDecimalValidation(DataValidation validation, string minimum, string maximum)
    {
        Assert.Equal(DataValidationValues.Decimal, validation.Type?.Value);
        Assert.Equal(DataValidationOperatorValues.Between, validation.Operator?.Value);
        Assert.True(validation.AllowBlank?.Value == true);
        Assert.True(validation.ShowErrorMessage?.Value == true);
        Assert.Equal(minimum, validation.Formula1?.Text);
        Assert.Equal(maximum, validation.Formula2?.Text);
    }

    private static void AssertFormula(Cell cell, string expectedFormula, string expectedCache)
    {
        Assert.Equal(expectedFormula, cell.CellFormula?.Text);
        Assert.False((cell.CellFormula?.Text ?? string.Empty).StartsWith("=", StringComparison.Ordinal));
        Assert.Equal(CellValues.Number, cell.DataType?.Value);
        Assert.Equal(expectedCache, cell.CellValue?.Text);
    }

    private static void AssertFormulaCache(Cell cell, string expectedCache)
    {
        Assert.NotNull(cell.CellFormula);
        Assert.False((cell.CellFormula?.Text ?? string.Empty).StartsWith("=", StringComparison.Ordinal));
        Assert.Equal(CellValues.Number, cell.DataType?.Value);
        Assert.Equal(expectedCache, cell.CellValue?.Text);
    }

    private static void AssertFormulaWithoutCache(Cell cell)
    {
        Assert.NotNull(cell.CellFormula);
        Assert.Null(cell.CellValue);
        Assert.Null(cell.DataType);
    }

    private static void AssertNumber(Cell cell, string expected)
    {
        Assert.Equal(CellValues.Number, cell.DataType?.Value);
        Assert.Equal(expected, cell.CellValue?.Text);
        Assert.Null(cell.CellFormula);
    }

    private static void AssertTrulyBlank(Cell cell)
    {
        Assert.Null(cell.DataType);
        Assert.Null(cell.CellValue);
        Assert.Null(cell.CellFormula);
        Assert.Null(cell.InlineString);
        Assert.Equal(string.Empty, cell.InnerText);
    }

    private static void AssertInline(Cell cell, string expected)
    {
        Assert.Equal(CellValues.InlineString, cell.DataType?.Value);
        Assert.Equal(expected, cell.InlineString?.Text?.Text);
        Assert.Null(cell.CellFormula);
    }

    private static decimal CachedDecimal(Cell cell) =>
        decimal.Parse(cell.CellValue?.Text ?? throw new InvalidDataException("Cached value is missing."), CultureInfo.InvariantCulture);

    private static string ConfigReference(FormulaCellAddress address) =>
        $"{Quote(address.SheetName)}!${address.NormalizedColumnName}${address.RowNumber.ToString(CultureInfo.InvariantCulture)}";

    private static string Quote(string sheetName) =>
        $"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'";

    private static Cell Cell(Worksheet worksheet, string reference) =>
        worksheet.Descendants<Cell>().Single(cell => string.Equals(cell.CellReference?.Value, reference, StringComparison.Ordinal));

    private static Cell CellByHeader(Worksheet worksheet, string header, int rowNumber)
    {
        Row headerRow = worksheet.Descendants<Row>().Single(row => row.RowIndex?.Value == 1);
        Cell headerCell = headerRow.Elements<Cell>().Single(cell => cell.InnerText == header);
        string column = new((headerCell.CellReference?.Value ?? string.Empty)
            .TakeWhile(char.IsAsciiLetter)
            .ToArray());
        return Cell(worksheet, column + rowNumber.ToString(CultureInfo.InvariantCulture));
    }

    private static Worksheet GetWorksheet(SpreadsheetDocument document, string name)
    {
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("Synthetic workbook part is missing.");
        Sheet sheet = (workbookPart.Workbook
            ?? throw new InvalidDataException("Synthetic workbook root is missing."))
            .Descendants<Sheet>()
            .Single(candidate => string.Equals(candidate.Name?.Value, name, StringComparison.Ordinal));
        string relationshipId = sheet.Id?.Value
            ?? throw new InvalidDataException("Synthetic worksheet relationship is missing.");
        return ((WorksheetPart)workbookPart.GetPartById(relationshipId)).Worksheet
            ?? throw new InvalidDataException("Synthetic worksheet root is missing.");
    }

    private static void AddSheets(string path, params string[] names)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, true);
        foreach (string name in names)
        {
            AddSheet(document, name);
        }
    }

    private static void AddSheet(SpreadsheetDocument document, string name)
    {
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("Synthetic workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("Synthetic workbook root is missing.");
        Sheets sheets = workbook.GetFirstChild<Sheets>()
            ?? throw new InvalidDataException("Synthetic sheet collection is missing.");
        uint nextId = sheets.Elements<Sheet>().Max(sheet => sheet.SheetId?.Value ?? 0) + 1;
        WorksheetPart part = workbookPart.AddNewPart<WorksheetPart>();
        part.Worksheet = new Worksheet(new SheetData());
        part.Worksheet.Save();
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(part),
            SheetId = nextId,
            Name = name,
        });
        workbook.Save();
    }

    private static Workbook Workbook(SpreadsheetDocument document) =>
        document.WorkbookPart?.Workbook
        ?? throw new InvalidDataException("Synthetic workbook root is missing.");
}
