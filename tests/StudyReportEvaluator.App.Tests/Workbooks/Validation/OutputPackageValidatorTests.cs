using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Validation;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Formulas;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Validation;

// Requirements: FR-029 (AC-030)
public sealed class OutputPackageValidatorTests
{
    private readonly OutputPackageValidator validator = new();

    [Fact]
    public void Reopens_read_only_and_accepts_exact_bound_preserved_closed_formula_package()
    {
        using ValidationFixture fixture = ValidationFixture.Create();

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Errors.Select(error => error.ToString())));
        Assert.Empty(result.Errors);
        Assert.Contains("<redacted>", result.ToString(), StringComparison.Ordinal);
        Assert.Contains("<redacted>", fixture.Plan.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Input.Path, fixture.Plan.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Package.TemporaryPath, validator.ToString(), StringComparison.Ordinal);
        using SpreadsheetDocument reopened = SpreadsheetDocument.Open(fixture.Package.TemporaryPath, false);
        Assert.Empty(new OpenXmlValidator().Validate(reopened, TestContext.Current.CancellationToken));
        Assert.All(
            Worksheet(reopened, fixture.Names.ResultsSheetName).Descendants<CellFormula>(),
            formula => Assert.False((formula.Text ?? string.Empty).StartsWith("=", StringComparison.Ordinal)));
        fixture.AssertInputUnchanged();
    }

    [Fact]
    public void Formula_must_match_the_exact_closed_plan_and_rejects_external_or_DDE_syntax()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        const string hostileFormula = "'[private-book.xlsx]Sheet1'!A1|'private-topic'";
        using (SpreadsheetDocument document = fixture.Package.OpenForEditing())
        {
            Cell(document, fixture.Names.ResultsSheetName, "E2").CellFormula =
                new CellFormula(hostileFormula);
        }

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        OutputPackageValidationError external = Assert.Single(
            result.Errors,
            error => error.Code == "FORMULA_EXTERNAL_OR_DDE_REFERENCE");
        OutputPackageValidationError mismatch = Assert.Single(
            result.Errors,
            error => error.Code == "FORMULA_SERIALIZATION_MISMATCH"
                && error.Identity.Field == ResultsSheetWriter.EffectiveRawSuffix);
        Assert.Equal("Criterion", external.Identity.NodeKind);
        Assert.Equal("C1", external.Identity.NodeId);
        Assert.False(result.IsValid);
        Assert.DoesNotContain(hostileFormula, external.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(hostileFormula, mismatch.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(hostileFormula, new OutputPackageValidationException(result.Errors).ToString(), StringComparison.Ordinal);
        fixture.AssertInputUnchanged();
    }

    [Fact]
    public void Formula_storage_with_a_leading_equals_is_rejected()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        using (SpreadsheetDocument document = fixture.Package.OpenForEditing())
        {
            Cell formulaCell = Cell(document, fixture.Names.ResultsSheetName, "E2");
            formulaCell.CellFormula = new CellFormula("=" + formulaCell.CellFormula!.Text);
        }

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.Code == "FORMULA_HAS_LEADING_EQUALS"
            && error.Identity.NodeId == "C1");
        Assert.Contains(result.Errors, error => error.Code == "FORMULA_SERIALIZATION_MISMATCH");
    }

    [Fact]
    public void Formula_cache_must_equal_the_expected_numeric_preview()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        using (SpreadsheetDocument document = fixture.Package.OpenForEditing())
        {
            Cell(document, fixture.Names.ResultsSheetName, "F2").CellValue = new CellValue("49.9");
        }

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        OutputPackageValidationError error = Assert.Single(
            result.Errors,
            item => item.Code == "FORMULA_CACHE_MISMATCH"
                && item.Identity.Field == ResultsSheetWriter.NormalizedSuffix);
        Assert.Equal("C1", error.Identity.NodeId);
        Assert.DoesNotContain("49.9", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Existing_worksheets_must_remain_byte_semantically_unchanged_and_in_place()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        using (SpreadsheetDocument document = fixture.Package.OpenForEditing())
        {
            Cell originalCell = Cell(document, "Original", "A2");
            originalCell.CellValue = new CellValue("987654321");
        }

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        OutputPackageValidationError error = Assert.Single(
            result.Errors,
            item => item.Code == "PRESERVED_SHEET_CHANGED");
        Assert.Equal("PreservedSheet", error.Identity.NodeKind);
        Assert.Equal("1", error.Identity.NodeId);
        Assert.DoesNotContain("987654321", error.ToString(), StringComparison.Ordinal);
        fixture.AssertInputUnchanged();
    }

    [Fact]
    public void Existing_sheet_shared_string_parts_must_remain_unchanged()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        const string privateCanary = "PRIVATE-SHARED-STRING-MUTATION-CANARY";
        using (SpreadsheetDocument document = fixture.Package.OpenForEditing())
        {
            SharedStringTablePart sharedStrings = document.WorkbookPart?.SharedStringTablePart
                ?? throw new InvalidDataException("Synthetic shared-string part is missing.");
            SharedStringItem item = sharedStrings.SharedStringTable?.Elements<SharedStringItem>().First()
                ?? throw new InvalidDataException("Synthetic shared-string item is missing.");
            item.Text = new Text(privateCanary);
            sharedStrings.SharedStringTable!.Save();
        }

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        OutputPackageValidationError error = Assert.Single(
            result.Errors,
            item => item.Code == "PRESERVED_PART_CHANGED");
        Assert.Equal("PreservedPart", error.Identity.NodeKind);
        Assert.DoesNotContain(privateCanary, error.ToString(), StringComparison.Ordinal);
        fixture.AssertInputUnchanged();
    }

    [Fact]
    public void Exactly_four_bound_app_sheets_are_required_with_no_extra_sheet()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        using (SpreadsheetDocument document = fixture.Package.OpenForEditing())
        {
            AddWorksheet(document, "Unexpected synthetic sheet");
        }

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.Code == "WORKSHEET_COUNT_MISMATCH");
        Assert.Contains(result.Errors, error => error.Code == "APP_OWNED_SHEET_BINDING_MISMATCH");
    }

    [Fact]
    public void Run_sheet_must_record_the_same_actual_four_sheet_bindings()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        using (SpreadsheetDocument document = fixture.Package.OpenForEditing())
        {
            Worksheet run = Worksheet(document, fixture.Names.RunSheetName);
            Row row = run.Descendants<Row>()
                .Single(candidate => Text(candidate, "A") == "ResultsSheetName");
            Cell value = row.Elements<Cell>()
                .Single(candidate => Column(candidate.CellReference?.Value) == "B");
            value.InlineString = new InlineString(new Text("Private wrong binding canary"));
        }

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        OutputPackageValidationError error = Assert.Single(
            result.Errors,
            item => item.Code == "RUN_SHEET_BINDING_MISMATCH"
                && item.Identity.Field == "ResultsSheetName");
        Assert.Equal("Run", error.Identity.NodeKind);
        Assert.DoesNotContain("Private wrong binding canary", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Config_cannot_acquire_unplanned_formulas_and_Run_remains_formula_free()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        using (SpreadsheetDocument document = fixture.Package.OpenForEditing())
        {
            Cell(document, fixture.Names.ConfigSheetName, "U2").CellFormula = new CellFormula("1+1");
            Cell(document, fixture.Names.RunSheetName, "B2").CellFormula = new CellFormula("1+1");
        }

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.Code == "UNEXPECTED_FORMULA_CELL"
            && error.Identity.NodeKind == "Formula");
        Assert.Contains(result.Errors, error => error.Code == "FORMULA_OUTSIDE_RESULTS"
            && error.Identity.NodeKind == "Run");
    }

    [Fact]
    public void Calculation_properties_must_request_auto_force_and_full_recalculation()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        using (SpreadsheetDocument document = fixture.Package.OpenForEditing())
        {
            Workbook workbook = document.WorkbookPart?.Workbook
                ?? throw new InvalidDataException("Synthetic workbook root is missing.");
            CalculationProperties properties = workbook.CalculationProperties
                ?? throw new InvalidDataException("Synthetic calculation properties are missing.");
            properties.CalculationMode = CalculateModeValues.Manual;
            properties.FullCalculationOnLoad = false;
            properties.ForceFullCalculation = false;
            workbook.Save();
        }

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.Code == "CALCULATION_MODE_INVALID");
        Assert.Contains(result.Errors, error => error.Code == "FULL_CALCULATION_ON_LOAD_REQUIRED");
        Assert.Contains(result.Errors, error => error.Code == "FORCE_FULL_CALCULATION_REQUIRED");
    }

    [Fact]
    public void Defined_name_injection_and_external_relationship_are_rejected()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        using (SpreadsheetDocument document = fixture.Package.OpenForEditing())
        {
            WorkbookPart workbookPart = document.WorkbookPart
                ?? throw new InvalidDataException("Synthetic workbook part is missing.");
            Workbook workbook = workbookPart.Workbook
                ?? throw new InvalidDataException("Synthetic workbook root is missing.");
            DefinedNames names = new(
                new DefinedName("A1")
                {
                    Name = "InjectedName",
                });
            workbook.InsertBefore(names, workbook.CalculationProperties);
            workbookPart.AddExternalRelationship(
                "urn:study-report-evaluator:synthetic-external",
                new Uri("https://example.invalid/private", UriKind.Absolute));
            workbook.Save();
        }

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.Code == "DEFINED_NAMES_CHANGED");
        Assert.Contains(result.Errors, error => error.Code == "EXTERNAL_RELATIONSHIP_PRESENT");
        Assert.DoesNotContain("InjectedName", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Orphan_package_part_outside_the_OpenXml_relationship_graph_is_rejected_fail_closed()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        X01SyntheticWorkbookFactory.AddEntry(
            fixture.Package.TemporaryPath,
            "xl/activeX/private-orphan.bin",
            "PRIVATE-ORPHAN-ACTIVE-CONTENT-CANARY"u8.ToArray());

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            fixture.Plan,
            TestContext.Current.CancellationToken);

        Assert.Contains(result.Errors, error => error.Code == "PACKAGE_CLASSIFICATION_INVALID");
        Assert.DoesNotContain("PRIVATE-ORPHAN-ACTIVE-CONTENT-CANARY", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Package.TemporaryPath, result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Expected_formula_plan_itself_is_preflighted_for_cycles_with_hierarchy_identity()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        FormulaCellAddress target = new(fixture.Names.ResultsSheetName, "E", 2);
        FormulaIdentity identity = new(
            "Criterion",
            "C-CYCLE",
            "Private cycle display canary",
            ResultsSheetWriter.EffectiveRawSuffix);
        ExpectedFormulaCell cyclic = new(
            new FormulaCellDefinition(
                target,
                identity,
                new FormulaCell(new FormulaCellReference(target))),
            null);
        OutputPackageValidationPlan cyclicPlan = OutputPackageValidationPlan.Capture(
            fixture.Input.Path,
            fixture.Names,
            [cyclic]);

        OutputPackageValidationResult result = validator.Validate(
            fixture.Package.TemporaryPath,
            cyclicPlan,
            TestContext.Current.CancellationToken);

        OutputPackageValidationError error = Assert.Single(
            result.Errors,
            item => item.Code == "FORMULA_CYCLE");
        Assert.Equal("C-CYCLE", error.Identity.NodeId);
        Assert.Equal(ResultsSheetWriter.EffectiveRawSuffix, error.Identity.Field);
        Assert.DoesNotContain("Private cycle display canary", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Cancellation_is_propagated_without_opening_or_mutating_content()
    {
        using ValidationFixture fixture = ValidationFixture.Create();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => validator.Validate(fixture.Package.TemporaryPath, fixture.Plan, cancellation.Token));

        fixture.AssertInputUnchanged();
        Assert.False(File.Exists(fixture.Package.RequestedFinalPath));
    }

    private static void AddWorksheet(SpreadsheetDocument document, string name)
    {
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("Synthetic workbook part is missing.");
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("Synthetic workbook root is missing.");
        Sheets sheets = workbook.GetFirstChild<Sheets>()
            ?? throw new InvalidDataException("Synthetic workbook sheets are missing.");
        WorksheetPart part = workbookPart.AddNewPart<WorksheetPart>();
        part.Worksheet = new Worksheet(new SheetData());
        part.Worksheet.Save();
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(part),
            SheetId = sheets.Elements<Sheet>().Max(sheet => sheet.SheetId?.Value ?? 0) + 1,
            Name = name,
        });
        workbook.Save();
    }

    private static Cell Cell(SpreadsheetDocument document, string sheetName, string reference) =>
        Worksheet(document, sheetName).Descendants<Cell>()
            .Single(cell => string.Equals(cell.CellReference?.Value, reference, StringComparison.Ordinal));

    private static Worksheet Worksheet(SpreadsheetDocument document, string sheetName)
    {
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("Synthetic workbook part is missing.");
        Sheet sheet = (workbookPart.Workbook
            ?? throw new InvalidDataException("Synthetic workbook root is missing."))
            .Descendants<Sheet>()
            .Single(candidate => string.Equals(candidate.Name?.Value, sheetName, StringComparison.Ordinal));
        string relationshipId = sheet.Id?.Value
            ?? throw new InvalidDataException("Synthetic worksheet relationship is missing.");
        return ((WorksheetPart)workbookPart.GetPartById(relationshipId)).Worksheet
            ?? throw new InvalidDataException("Synthetic worksheet root is missing.");
    }

    private static string Text(Row row, string column)
    {
        Cell? cell = row.Elements<Cell>()
            .FirstOrDefault(candidate => Column(candidate.CellReference?.Value) == column);
        return cell?.InnerText ?? string.Empty;
    }

    private static string Column(string? reference) =>
        new((reference ?? string.Empty).TakeWhile(char.IsAsciiLetter).ToArray());

    internal sealed class ValidationFixture : IDisposable
    {
        private readonly InputSnapshotService snapshots = new();

        private ValidationFixture(
            TemporaryWorkbook input,
            InputSnapshot inputIdentity,
            WorkingPackage package,
            AppOwnedSheetNames names,
            OutputPackageValidationPlan plan)
        {
            Input = input;
            InputIdentity = inputIdentity;
            Package = package;
            Names = names;
            Plan = plan;
        }

        public TemporaryWorkbook Input { get; }

        public InputSnapshot InputIdentity { get; }

        public WorkingPackage Package { get; }

        public AppOwnedSheetNames Names { get; }

        public OutputPackageValidationPlan Plan { get; }

        public static ValidationFixture Create()
        {
            TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
            InputSnapshotService snapshots = new();
            InputSnapshot inputIdentity = snapshots.Capture(input.Path);
            string targetDirectory = Path.Combine(input.Directory, "target");
            Directory.CreateDirectory(targetDirectory);
            WorkingPackage package = WorkingPackage.Create(
                input.Path,
                Path.Combine(targetDirectory, "quantified.xlsx"));
            QuantificationSnapshot snapshot = QuantificationSnapshot.Create(CreateDefinition());
            AppOwnedSheetNames names;
            ConfigCellAddressMap config;
            try
            {
                using (SpreadsheetDocument document = package.OpenForEditing())
                {
                    names = new AppOwnedSheetNameResolver().Resolve(document);
                    config = new ConfigSheetWriter().Write(document, snapshot, names);
                    new ReferenceAnswersSheetWriter().Write(
                        document,
                        snapshot,
                        names,
                        [
                            new ReferenceAnswerSheetRow
                            {
                                QuestionId = "Q1",
                                ModelId = "auto",
                                Answer = "Private reference canary",
                                StatusCode = ResultsStatusCodes.Success,
                                GeneratedAtUtc = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
                            },
                        ]);
                    new ResultsSheetWriter().Write(
                        document,
                        snapshot,
                        names,
                        config,
                        [CreateRow()]);
                    new RunSheetWriter().Write(
                        document,
                        new RunSheetMetadata
                        {
                            InputIdentity = inputIdentity,
                            DefinitionSha256 = snapshot.Sha256,
                            ApplicationIdentity = "StudyReportEvaluator.App/3.0.0-test",
                            CopilotSdkIdentity = "GitHub.Copilot.SDK/1.0.11",
                            CopilotCliIdentity = "copilot/1.0.82",
                            ModelIdentity = "synthetic-model",
                            StartedAtUtc = new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero),
                            EndedAtUtc = new DateTimeOffset(2026, 9, 1, 10, 1, 0, TimeSpan.Zero),
                            PlannedEvaluationCount = 1,
                            CompletedEvaluationCount = 1,
                            ErrorCount = 0,
                            SheetNames = names,
                        });
                    new CalculationPropertiesWriter().Write(document);
                }

                ExpectedFormulaCell[] formulas = CreateExpectedFormulas(names, config);
                OutputPackageValidationPlan plan = OutputPackageValidationPlan.Capture(
                    input.Path,
                    names,
                    formulas);
                return new ValidationFixture(input, inputIdentity, package, names, plan);
            }
            catch
            {
                package.Dispose();
                input.Dispose();
                throw;
            }
        }

        public void AssertInputUnchanged() =>
            Assert.True(snapshots.Recheck(Input.Path, InputIdentity).IsMatch);

        public void Dispose()
        {
            Package.Dispose();
            Input.Dispose();
        }

        private static QuantificationDefinition CreateDefinition() =>
            new()
            {
                Id = "DEF-X05",
                Name = "X-05 synthetic definition",
                Revision = "1",
                SourceSheet = "Original",
                HeaderRow = 1,
                FirstDataRow = 2,
                LastDataRow = 2,
                BasePoints = 99m,
                RoundingDigits = 1,
                Questions =
                [
                    new QuestionDefinition
                    {
                        Id = "Q1",
                        DisplayName = "Question",
                        QuestionText = "Synthetic question",
                        PrimarySourceColumn = "B",
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
                                        DisplayName = "Private criterion display canary",
                                        Description = "Private criterion body canary",
                                        Weight = 1m,
                                    },
                                ],
                            },
                        ],
                    },
                ],
            };

        private static ResultsSheetRowInput CreateRow() =>
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
                                            Reason = "Private reason canary",
                                            Evidence = string.Empty,
                                            EvidenceSource = EvidenceSourceKind.None,
                                            EvidenceSourceColumnId = string.Empty,
                                        },
                                    ],
                                },
                            },
                        ],
                        Similarity = new SimilarityResultInput
                        {
                            AiRaw = 0.5m,
                            Reason = "Private similarity reason canary",
                            Status = ResultsStatusCodes.Success,
                        },
                    },
                ],
            };

        private static ExpectedFormulaCell[] CreateExpectedFormulas(
            AppOwnedSheetNames names,
            ConfigCellAddressMap config)
        {
            FormulaCellAddress scorable = Address(names.ResultsSheetName, "B", 2);
            FormulaCellAddress aiRaw = Address(names.ResultsSheetName, "C", 2);
            FormulaCellAddress overrideValue = Address(names.ResultsSheetName, "D", 2);
            FormulaCellAddress effective = Address(names.ResultsSheetName, "E", 2);
            FormulaCellAddress normalized = Address(names.ResultsSheetName, "F", 2);
            FormulaCellAddress evaluator = Address(names.ResultsSheetName, "L", 2);
            FormulaCellAddress answerPresent = Address(names.ResultsSheetName, "M", 2);
            FormulaCellAddress questionNormalized = Address(names.ResultsSheetName, "N", 2);
            FormulaCellAddress questionRate = Address(names.ResultsSheetName, "O", 2);
            FormulaCellAddress questionEarned = Address(names.ResultsSheetName, "P", 2);
            FormulaCellAddress similarity = Address(names.ResultsSheetName, "Q", 2);
            FormulaCellAddress similarityPenalty = Address(names.ResultsSheetName, "T", 2);
            FormulaCellAddress basePoints = Address(names.ResultsSheetName, "W", 2);
            FormulaCellAddress specialEarned = Address(names.ResultsSheetName, "X", 2);
            FormulaCellAddress finalRaw = Address(names.ResultsSheetName, "Y", 2);
            FormulaCellAddress finalScore = Address(names.ResultsSheetName, "Z", 2);
            FormulaCellReference minimum = Ref(config.CriterionMinimumCells["C1"], absolute: true);
            FormulaCellReference maximum = Ref(config.CriterionMaximumCells["C1"], absolute: true);
            FormulaCellReference rounding = Ref(config.RoundingDigitsCell, absolute: true);
            FormulaCellReference criterionWeight = Ref(config.CriterionWeightCells["C1"], absolute: true);
            FormulaCellReference evaluatorWeight = Ref(config.EvaluatorWeightCells["E1"], absolute: true);
            return
            [
                Expected(
                    config.AllocationTotalCell,
                    new FormulaIdentity("Definition", "DEF-X05", "X-05 synthetic definition", "AllocationTotal"),
                    FormulaExpressions.AllocationTotal(
                        Ref(config.BasePointsCell, true),
                        Ref(config.SpecialPointsCell, true),
                        [Ref(config.QuestionPointsCells["Q1"], true)]),
                    100m),
                Expected(
                    config.AllocationValidCell,
                    new FormulaIdentity("Definition", "DEF-X05", "X-05 synthetic definition", "AllocationValid"),
                    FormulaExpressions.AllocationValid(Ref(config.AllocationTotalCell, true)),
                    1m),
                Expected(
                    effective,
                    new FormulaIdentity("Criterion", "C1", "Private criterion display canary", ResultsSheetWriter.EffectiveRawSuffix),
                    FormulaExpressions.EffectiveRaw(Ref(scorable), Ref(aiRaw), Ref(overrideValue), minimum, maximum),
                    5m),
                Expected(
                    normalized,
                    new FormulaIdentity("Criterion", "C1", "Private criterion display canary", ResultsSheetWriter.NormalizedSuffix),
                    FormulaExpressions.Normalized(Ref(effective), minimum, maximum, rounding),
                    50m),
                Expected(
                    evaluator,
                    new FormulaIdentity("Evaluator", "E1", "Evaluator", ResultsSheetWriter.EvaluatorScoreSuffix),
                    FormulaExpressions.Aggregate([new WeightedFormulaChild(Ref(normalized), criterionWeight)], rounding),
                    50m),
                Expected(
                    questionNormalized,
                    new FormulaIdentity("Question", "Q1", "Question", ResultsSheetWriter.QuestionNormalizedSuffix),
                    FormulaExpressions.Aggregate([new WeightedFormulaChild(Ref(evaluator), evaluatorWeight)], rounding),
                    50m),
                Expected(
                    questionRate,
                    new FormulaIdentity("Question", "Q1", "Question", ResultsSheetWriter.QuestionRateSuffix),
                    FormulaExpressions.QuestionRate(Ref(answerPresent), Ref(questionNormalized)),
                    0.5m),
                Expected(
                    questionEarned,
                    new FormulaIdentity("Question", "Q1", "Question", ResultsSheetWriter.QuestionEarnedSuffix),
                    FormulaExpressions.QuestionEarned(Ref(questionRate), Ref(config.QuestionPointsCells["Q1"], true), rounding),
                    0.5m),
                Expected(
                    similarityPenalty,
                    new FormulaIdentity("Question", "Q1", "Question", ResultsSheetWriter.SimilarityPenaltySuffix),
                    FormulaExpressions.SimilarityPenalty(
                        Ref(config.QuestionPointsCells["Q1"], true),
                        Ref(similarity),
                        Ref(config.SimilarityPenaltyWeightCell, true),
                        rounding),
                    0.1m),
                Expected(
                    basePoints,
                    new FormulaIdentity("Definition", "DEF-X05", "X-05 synthetic definition", ResultsSheetWriter.BasePointsHeader),
                    new FormulaCell(Ref(config.BasePointsCell, true)),
                    99m),
                Expected(
                    specialEarned,
                    new FormulaIdentity("Definition", "DEF-X05", "X-05 synthetic definition", ResultsSheetWriter.SpecialEarnedHeader),
                    FormulaExpressions.SpecialEarned([], Ref(config.SpecialPointsCell, true), rounding),
                    0m),
                Expected(
                    finalRaw,
                    new FormulaIdentity("Definition", "DEF-X05", "X-05 synthetic definition", ResultsSheetWriter.FinalRawHeader),
                    FormulaExpressions.FinalRaw(
                        Ref(config.AllocationValidCell, true),
                        Ref(config.BasePointsCell, true),
                        [Ref(questionEarned)],
                        Ref(specialEarned),
                        [Ref(similarityPenalty)],
                        rounding),
                    99.4m),
                Expected(
                    finalScore,
                    new FormulaIdentity("Definition", "DEF-X05", "X-05 synthetic definition", ResultsSheetWriter.FinalScoreHeader),
                    FormulaExpressions.FinalScore(Ref(finalRaw)),
                    99.4m),
            ];
        }

        private static ExpectedFormulaCell Expected(
            FormulaCellAddress target,
            FormulaIdentity identity,
            FormulaExpression expression,
            decimal? cache) =>
            new(new FormulaCellDefinition(target, identity, expression), cache);

        private static FormulaCellAddress Address(string sheet, string column, int row) =>
            new(sheet, column, row);

        private static FormulaCellReference Ref(FormulaCellAddress address, bool absolute = false) =>
            new(address, absolute, absolute);
    }
}