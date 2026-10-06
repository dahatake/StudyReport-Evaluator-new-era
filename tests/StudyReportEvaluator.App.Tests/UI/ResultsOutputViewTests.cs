using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using Xunit;
using Border = Avalonia.Controls.Border;
using Control = Avalonia.Controls.Control;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-052 (AC-053)
public sealed class ResultsOutputViewTests
{
    [Fact]
    public async Task Override_uses_snapshot_range_and_formula_equivalent_preview_then_blocks_invalid_export()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        RecordingOutputBoundary output = new();
        ResultsOutputViewModel viewModel = new(output, U04TestSupport.Context(summary));
        ResultsCriterionViewModel item = Assert.Single(viewModel.Results);

        Assert.Equal(5m, item.AiRawScore);
        Assert.Equal(5m, item.EffectiveRaw);
        Assert.Equal(50m, item.NormalizedScore);
        Assert.Equal(50m, item.EvaluatorScore);
        Assert.Equal(50m, item.QuestionScore);
        Assert.Equal(50m, item.OverallScore);
        Assert.True(item.CanOverride);
        Assert.True(viewModel.CanExport);

        item.OverrideText = "8";

        Assert.Null(item.OverrideError);
        Assert.Equal(8m, item.EffectiveRaw);
        Assert.Equal(80m, item.NormalizedScore);
        Assert.Equal(80m, item.EvaluatorScore);
        Assert.Equal(80m, item.QuestionScore);
        Assert.Equal(80m, item.OverallScore);
        Assert.True(viewModel.CanExport);

        item.OverrideText = "11";

        Assert.True(item.HasOverrideError);
        Assert.Contains("range", item.OverrideError, StringComparison.OrdinalIgnoreCase);
        Assert.Null(item.EffectiveRaw);
        Assert.Null(item.NormalizedScore);
        Assert.Null(item.EvaluatorScore);
        Assert.Null(item.QuestionScore);
        Assert.Null(item.OverallScore);
        Assert.True(viewModel.HasOverrideErrors);
        Assert.False(viewModel.CanExport);
        await viewModel.ExportAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, output.ExportCount);

        item.OverrideText = "8";
        await viewModel.ExportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, output.ExportCount);
        RunCriterionOverride written = Assert.Single(output.LastRequest?.Overrides ?? []);
        Assert.Equal("8", written.Value);
        Assert.Equal(ResultsOutputStatusCodes.Success, viewModel.LastExportCode);
        Assert.DoesNotContain("8", item.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_primary_disables_override_and_keeps_raw_effective_and_ancestors_blank_not_zero()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(
            definition,
            metadata,
            primary: "   ");
        ResultsOutputViewModel viewModel = new(
            new RecordingOutputBoundary(),
            U04TestSupport.Context(summary));
        ResultsCriterionViewModel item = Assert.Single(viewModel.Results);

        Assert.Equal(ResultsStatusCodes.Empty, item.StatusCode);
        Assert.False(item.CanOverride);
        Assert.Null(item.AiRawScore);
        Assert.Null(item.EffectiveRaw);
        Assert.Null(item.NormalizedScore);
        Assert.Null(item.EvaluatorScore);
        Assert.Null(item.QuestionScore);
        Assert.Null(item.OverallScore);
        Assert.Equal("—", item.AiRawText);
        Assert.Equal("—", item.OverallScoreText);

        item.OverrideText = "5";

        Assert.Equal(string.Empty, item.OverrideText);
        Assert.False(viewModel.HasOverrideErrors);
        Assert.True(viewModel.CanExport);
        RunOutputPreparation preparation = summary.PrepareOutput();
        QuestionResultInput question = Assert.Single(Assert.Single(preparation.Rows).Questions);
        Assert.False(question.Scorable);
        Assert.Null(Assert.Single(question.Evaluators).AiResult);
    }

    [Fact]
    public async Task Cancelled_partial_run_retains_completed_raw_and_exports_unfinished_units_as_blank_cancelled()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(
            definition,
            metadata,
            cancelAfterFirst: true);
        RecordingOutputBoundary output = new();
        ExecutionRunContext context = U04TestSupport.Context(summary);
        ResultsOutputViewModel viewModel = new(output, context);

        Assert.True(viewModel.IsPartial);
        Assert.True(viewModel.IsInputUnchanged);
        Assert.Equal(2, viewModel.PlannedEvaluationCount);
        Assert.Equal(1, viewModel.CompletedEvaluationCount);
        Assert.Equal(1, viewModel.CancelledCount);
        Assert.Equal(2, viewModel.Results.Count);
        ResultsCriterionViewModel completed = viewModel.Results.Single(item => item.SourceRowNumber == 2);
        ResultsCriterionViewModel cancelled = viewModel.Results.Single(item => item.SourceRowNumber == 3);
        Assert.Equal(5m, completed.AiRawScore);
        Assert.Equal(ResultsStatusCodes.Cancelled, cancelled.StatusCode);
        Assert.Null(cancelled.AiRawScore);
        Assert.Null(cancelled.EffectiveRaw);
        Assert.Null(cancelled.NormalizedScore);
        Assert.Null(cancelled.OverallScore);
        Assert.Equal("Question text Q1: 1", viewModel.RowScores[0].QuestionEarnedText);
        Assert.Equal("Question text Q1: —", viewModel.RowScores[1].QuestionEarnedText);
        Assert.True(viewModel.CanExport);

        await viewModel.ExportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, output.ExportCount);
        Assert.Same(context, output.LastRequest?.Context);
        RunOutputPreparation preparation = summary.PrepareOutput(output.LastRequest?.Overrides);
        EvaluatorResultInput cancelledOutput = Assert.Single(
            Assert.Single(preparation.Rows, row => row.SourceRowNumber == 3).Questions[0].Evaluators);
        Assert.Equal(ResultsStatusCodes.Cancelled, cancelledOutput.Status);
        Assert.Null(cancelledOutput.AiResult);
        Assert.Equal(ResultsOutputStatusCodes.Success, viewModel.LastExportCode);
    }

    [Fact]
    public async Task Output_collision_is_visible_blocks_export_and_never_requests_overwrite()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        RecordingOutputBoundary output = new()
        {
            Assess = (_, path) => path.EndsWith("occupied.xlsx", StringComparison.OrdinalIgnoreCase)
                ? new ResultsOutputPathAssessment(
                    ResultsOutputStatusCodes.TargetExists,
                    isValid: false,
                    targetExists: true)
                : ResultsOutputPathAssessment.Valid,
        };
        ResultsOutputViewModel viewModel = new(output, U04TestSupport.Context(summary));

        viewModel.OutputPath = Path.Combine(Path.GetTempPath(), "occupied.xlsx");

        Assert.False(viewModel.IsOutputPathValid);
        Assert.True(viewModel.OutputTargetExists);
        Assert.False(viewModel.CanExport);
        Assert.Contains("上書き", viewModel.OutputPathStatusText, StringComparison.Ordinal);
        await viewModel.ExportAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, output.ExportCount);
    }

    [Fact]
    public async Task Loading_a_new_run_suppresses_the_late_result_of_a_cancelled_previous_export()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        TaskCompletionSource exportStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<ResultsOutputResult> releaseExport = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingOutputBoundary output = new()
        {
            Export = (_, _) =>
            {
                exportStarted.TrySetResult();
                return releaseExport.Task;
            },
        };
        ResultsOutputViewModel viewModel = new(
            output,
            U04TestSupport.Context(
                summary,
                Path.Combine(Path.GetTempPath(), "first-input.xlsx")));

        Task firstExport = viewModel.ExportAsync(TestContext.Current.CancellationToken);
        await exportStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(viewModel.IsExporting);
        ExecutionRunContext nextContext = U04TestSupport.Context(
            summary,
            Path.Combine(Path.GetTempPath(), "second-input.xlsx"));

        viewModel.Load(nextContext);

        Assert.False(viewModel.IsExporting);
        Assert.Equal(ResultsOutputStatusCodes.Ready, viewModel.LastExportCode);
        Assert.Contains("second-input_quantified_", viewModel.OutputPath, StringComparison.Ordinal);
        releaseExport.TrySetResult(new ResultsOutputResult(ResultsOutputStatusCodes.TargetExists));
        await firstExport;

        Assert.False(viewModel.IsExporting);
        Assert.Equal(ResultsOutputStatusCodes.Ready, viewModel.LastExportCode);
        Assert.Contains("second-input_quantified_", viewModel.OutputPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_output_boundary_writes_valid_separate_workbook_and_preserves_exact_input_identity()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original",
            1,
            2,
            2,
            new X02Header(1, "Primary A"),
            new X02Header(2, "Support B"));
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = new WorkbookMetadataReader().Read(workbook.Path);
        ScriptedRunner runner = new((payload, _, _) => Task.FromResult(
            EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, _ => 5m))));
        RunSummary summary = await new QuantificationOrchestrator(
            new OpenXmlEvaluationRowSource(workbook.Path),
            runner).RunAsync(
                new QuantificationRunRequest
                {
                    DraftDefinition = definition,
                    WorkbookMetadata = metadata,
                    InputPath = workbook.Path,
                    ModelId = "model-test",
                    MaximumPromptTokens = 64_000,
                    MaximumContextWindowTokens = 128_000,
                    MaxConcurrency = 1,
                },
                cancellationToken: TestContext.Current.CancellationToken);
        string finalPath = Path.Combine(workbook.Directory, "quantified.xlsx");
        ResultsOutputBoundary output = new();
        ExecutionRunContext context = new(
            summary,
            workbook.Path,
            "model-test",
            U04TestSupport.RuntimeIdentity(),
            reasoningEffort: "low",
            contextTier: "long-context");

        Assert.True(output.AssessPath(workbook.Path, finalPath).IsValid);
        ResultsOutputResult result = await output.ExportAsync(
            new ResultsOutputRequest(context, finalPath),
            TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess, result.ToString());
        Assert.Equal(Path.GetFullPath(finalPath), result.FinalPath);
        Assert.True(File.Exists(finalPath));
        Assert.True(new InputSnapshotService().Recheck(workbook.Path, summary.InputSnapshot).IsMatch);
        using (SpreadsheetDocument original = SpreadsheetDocument.Open(workbook.Path, false))
        {
            WorkbookPart originalPart = original.WorkbookPart
                ?? throw new InvalidDataException("The input workbook part is missing.");
            Workbook originalWorkbook = originalPart.Workbook
                ?? throw new InvalidDataException("The input workbook root is missing.");
            Assert.Single(originalWorkbook.Descendants<Sheet>());
        }

        using SpreadsheetDocument outputDocument = SpreadsheetDocument.Open(finalPath, false);
        WorkbookPart outputPart = outputDocument.WorkbookPart
            ?? throw new InvalidDataException("The output workbook part is missing.");
        Workbook outputWorkbook = outputPart.Workbook
            ?? throw new InvalidDataException("The output workbook root is missing.");
        Sheet[] sheets = outputWorkbook.Descendants<Sheet>().ToArray();
        Assert.Equal(5, sheets.Length);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.ConfigBaseName);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.ReferencesBaseName);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.ResultsBaseName);
        Assert.Contains(sheets, sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.RunBaseName);
        Assert.NotEmpty(outputPart.WorksheetParts.SelectMany(part =>
            (part.Worksheet ?? throw new InvalidDataException("An output worksheet is missing."))
                .Descendants<CellFormula>()));
        // References and Run sheets record the run model and run-level effort, not a fixed auto.
        Assert.Equal("ModelId", InlineText(outputPart, sheets, AppOwnedSheetNameResolver.ReferencesBaseName, "D1"));
        Assert.Equal("model-test", InlineText(outputPart, sheets, AppOwnedSheetNameResolver.ReferencesBaseName, "D2"));
        Assert.Equal("ReasoningEffort", InlineText(outputPart, sheets, AppOwnedSheetNameResolver.ReferencesBaseName, "H1"));
        Assert.Equal("low", InlineText(outputPart, sheets, AppOwnedSheetNameResolver.ReferencesBaseName, "H2"));
        Worksheet runSheet = ((WorksheetPart)outputPart.GetPartById(sheets
                .Single(sheet => sheet.Name?.Value == AppOwnedSheetNameResolver.RunBaseName).Id!.Value!))
            .Worksheet!;
        Row effortRow = runSheet.Descendants<Row>().Single(row => row.Elements<Cell>()
            .Any(cell => cell.InlineString?.InnerText == "ReasoningEffort"));
        Assert.Contains(effortRow.Elements<Cell>(), cell => cell.InlineString?.InnerText == "low");
        Row contextRow = runSheet.Descendants<Row>().Single(row => row.Elements<Cell>()
            .Any(cell => cell.InlineString?.InnerText == "ContextTier"));
        Assert.Contains(contextRow.Elements<Cell>(), cell => cell.InlineString?.InnerText == "long-context");
        Assert.DoesNotContain(workbook.Path, result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? InlineText(WorkbookPart part, Sheet[] sheets, string sheetName, string reference)
    {
        Sheet sheet = sheets.Single(item => item.Name?.Value == sheetName);
        Worksheet worksheet = ((WorksheetPart)part.GetPartById(sheet.Id!.Value!)).Worksheet!;
        return worksheet.Descendants<Cell>()
            .SingleOrDefault(cell => cell.CellReference?.Value == reference)?
            .InlineString?.InnerText;
    }

    [Fact]
    public async Task Ethics_warning_acknowledgement_is_absent_from_snapshot_and_export_gate_state()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        ResultsOutputViewModel viewModel = new(
            new RecordingOutputBoundary(),
            U04TestSupport.Context(summary));
        string[] prohibited = ["Acknowledge", "Consent", "Dismiss", "WarningAccepted", "EthicsAccepted"];
        MemberInfo[] members = typeof(ResultsOutputViewModel).GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(
            members,
            member => prohibited.Any(term => member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            prohibited,
            term => summary.Snapshot.CanonicalJson.Contains(term, StringComparison.OrdinalIgnoreCase));
        Assert.True(viewModel.CanExport);
    }

    [AvaloniaFact]
    public void Empty_results_disable_navigation_and_never_claim_completion_or_input_change()
    {
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary());
        using ResultsViewHost host = new(viewModel);

        Assert.Empty(viewModel.RowScores);
        Assert.Empty(viewModel.Results);
        Assert.Equal("結果はありません（0 行）", ById<TextBlock>(host.View, "ResultsPageSummary").Text);
        Assert.Equal(viewModel.RunSummaryText, ById<TextBlock>(host.View, "ResultsRunSummary").Text);
        Assert.Equal("run結果はありません。", ById<TextBlock>(host.View, "ResultsDurableOutput").Text);
        Assert.False(ById<TextBlock>(host.View, "ResultsInputState").IsEffectivelyVisible);
        Assert.False(ById<TextBlock>(host.View, "ResultsRunHeading").IsEffectivelyVisible);
        Assert.False(ById<TextBlock>(host.View, "ResultsRunIdentity").IsEffectivelyVisible);
        Assert.Equal(string.Empty, ById<TextBlock>(host.View, "ResultsRunIdentity").Text);
        Assert.False(ById<TextBlock>(host.View, "ResultsSnapshotCaption").IsEffectivelyVisible);
        Assert.False(ById<TextBlock>(host.View, "ResultsLastSuccessfulExport").IsEffectivelyVisible);
        Assert.Empty(ActiveOverrideEditors(host.View));
        foreach (string name in new[] { "PreviousPageButton", "NextPageButton", "GoToRowButton", "NextOverrideErrorButton", "ShowDetailButton", "ExportButton", "CancelExportButton" })
        {
            Assert.False(Required<Button>(host.View, name).IsEffectivelyEnabled);
        }

        AssertUniqueAutomationIds(host.View);
    }

    [AvaloniaFact]
    public async Task List_and_detail_keep_output_in_results_with_one_active_view_and_stable_ids()
    {
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), await CreateUiContextAsync(rowCount: 2));
        using ResultsViewHost host = new(viewModel);
        ListBox rows = Required<ListBox>(host.View, "RowScoreList");
        ListBox criteria = Required<ListBox>(host.View, "ResultsList");
        TextBox outputPath = Required<TextBox>(host.View, "OutputPathTextBox");

        Assert.Same(viewModel.VisibleRowScores, rows.ItemsSource);
        Assert.Same(viewModel.SelectedRowCriteria, criteria.ItemsSource);
        Assert.Equal("ResultsRowScores", AutomationProperties.GetAutomationId(rows));
        Assert.Equal("ResultsReviewList", AutomationProperties.GetAutomationId(criteria));
        Assert.True(rows.IsEffectivelyVisible);
        Assert.False(criteria.IsEffectivelyVisible);
        Assert.Empty(ActiveOverrideEditors(host.View));
        Assert.Same(viewModel.ShowDetailCommand, Required<Button>(host.View, "ShowDetailButton").Command);
        Assert.Same(viewModel.ShowListCommand, Required<Button>(host.View, "ShowListButton").Command);
        Assert.Same(viewModel.ExportCommand, Required<Button>(host.View, "ExportButton").Command);
        Assert.Same(viewModel.CancelExportCommand, Required<Button>(host.View, "CancelExportButton").Command);
        Assert.DoesNotContain(host.View.GetVisualDescendants(), visual =>
            visual.GetType().Name.Contains("Settings", StringComparison.Ordinal));

        rows.SelectedItem = viewModel.RowScores[1];
        RenderUi();
        Assert.Same(viewModel.RowScores[1], viewModel.SelectedRow);
        Control selectedContainer = Assert.IsAssignableFrom<Control>(rows.ContainerFromIndex(rows.SelectedIndex));
        Assert.True(selectedContainer.Focus(NavigationMethod.Tab, KeyModifiers.None));
        PressEnter(host.Window);

        Assert.True(viewModel.IsDetailVisible);
        Assert.False(rows.IsEffectivelyVisible);
        Assert.True(criteria.IsEffectivelyVisible);
        ResultsCriterionViewModel criterion = viewModel.SelectedRowCriteria[0];
        Assert.Same(criterion, Required<ContentControl>(host.View, "CriterionEditor").Content);
        TextBox editor = Assert.Single(ActiveOverrideEditors(host.View));
        Assert.Equal(criterion.AutomationId + "-Override", AutomationProperties.GetAutomationId(editor));
        Assert.Same(criterion, editor.DataContext);
        Assert.True(editor.Focus(NavigationMethod.Tab, KeyModifiers.None));
        Assert.Same(editor, host.Window.FocusManager?.GetFocusedElement());
        Assert.True(outputPath.IsEffectivelyVisible);
        Assert.True(outputPath.IsEffectivelyEnabled);
        AssertUniqueAutomationIds(host.View);

        Execute(Required<Button>(host.View, "ShowListButton"));

        Assert.False(viewModel.IsDetailVisible);
        Assert.Equal(11, viewModel.SelectedRow?.SourceRowNumber);
        Assert.Empty(ActiveOverrideEditors(host.View));
        Assert.Same(outputPath, Required<TextBox>(host.View, "OutputPathTextBox"));
        Assert.Same(rows.ContainerFromIndex(rows.SelectedIndex), host.Window.FocusManager?.GetFocusedElement());
        AssertUniqueAutomationIds(host.View);
    }

    [AvaloniaFact]
    public async Task Paging_uses_actual_viewport_and_row_height_without_dropping_the_total_dataset()
    {
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), await CreateUiContextAsync(rowCount: 530));
        ResultsCriterionViewModel[] originalCriteria = viewModel.Results.ToArray();
        ResultsRowScoreViewModel[] originalRows = viewModel.RowScores.ToArray();
        using ResultsViewHost host = new(viewModel);
        ListBox rows = Required<ListBox>(host.View, "RowScoreList");
        AssertMeasuredPage(host.View, viewModel);
        Assert.Contains(rows.GetVisualDescendants(), visual => visual is VirtualizingStackPanel);
        int smallPageSize = viewModel.PageSize;

        Required<TextBox>(host.View, "GoToRowTextBox").Text = "539";
        Assert.Equal(10, viewModel.SelectedRow?.SourceRowNumber);
        Execute(Required<Button>(host.View, "GoToRowButton"));

        Assert.Equal((530 - 1) / smallPageSize, viewModel.PageIndex);
        Assert.Same(originalRows[^1], viewModel.SelectedRow);
        Assert.False(Required<Button>(host.View, "NextPageButton").IsEffectivelyEnabled);
        Assert.True(Required<Button>(host.View, "PreviousPageButton").IsEffectivelyEnabled);
        Assert.Equal(viewModel.PageSummary, ById<TextBlock>(host.View, "ResultsPageSummary").Text);
        host.Window.Height += 176d;
        RenderUi();

        Assert.True(viewModel.PageSize > smallPageSize);
        AssertMeasuredPage(host.View, viewModel);
        Assert.Same(originalRows[^1], viewModel.SelectedRow);
        Assert.Equal(530, viewModel.RowScores.Count);
        Assert.Equal(1060, viewModel.Results.Count);
        for (int index = 0; index < originalRows.Length; index++)
        {
            Assert.Same(originalRows[index], viewModel.RowScores[index]);
        }

        for (int index = 0; index < originalCriteria.Length; index++)
        {
            Assert.Same(originalCriteria[index], viewModel.Results[index]);
        }

        Assert.False(viewModel.HasUnsavedOverrides);
    }

    [AvaloniaFact]
    public async Task Last_page_override_survives_recomputation_list_return_resize_and_row_jump()
    {
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), await CreateUiContextAsync());
        using ResultsViewHost host = new(viewModel);
        Required<TextBox>(host.View, "GoToRowTextBox").Text = "18";
        Execute(Required<Button>(host.View, "GoToRowButton"));
        Execute(Required<Button>(host.View, "ShowDetailButton"));
        ListBox criteria = Required<ListBox>(host.View, "ResultsList");
        ResultsCriterionViewModel criterion = viewModel.SelectedRowCriteria[1];
        criteria.SelectedItem = criterion;
        RenderUi();
        TextBox editor = ById<TextBox>(host.View, criterion.AutomationId + "-Override");
        ResultsRowScoreViewModel? previousScore = viewModel.SelectedRow;
        Assert.True(editor.Focus(NavigationMethod.Tab, KeyModifiers.None));

        editor.Text = "0";
        RenderUi();

        Assert.Equal("0", criterion.OverrideText);
        Assert.Equal(5m, criterion.AiRawScore);
        Assert.Equal(0m, criterion.EffectiveRaw);
        Assert.Equal(0m, criterion.NormalizedScore);
        Assert.Equal(25m, criterion.EvaluatorScore);
        Assert.Equal(25m, criterion.QuestionScore);
        Assert.Equal(25m, criterion.OverallScore);
        AssertCriterionTexts(host.View, criterion);
        Assert.NotSame(previousScore, viewModel.SelectedRow);
        Assert.Same(viewModel.RowScores[^1], viewModel.SelectedRow);
        Assert.Same(viewModel.SelectedRow, Required<ListBox>(host.View, "RowScoreList").SelectedItem);
        Assert.Same(editor, ById<TextBox>(host.View, criterion.AutomationId + "-Override"));
        Assert.Same(editor, host.Window.FocusManager?.GetFocusedElement());
        Assert.True(ById<TextBlock>(host.View, "ResultsUnsavedOverrides").IsEffectivelyVisible);

        Execute(Required<Button>(host.View, "ShowListButton"));
        host.Window.Height += 88d;
        RenderUi();
        Required<TextBox>(host.View, "GoToRowTextBox").Text = "10";
        Execute(Required<Button>(host.View, "GoToRowButton"));
        Required<TextBox>(host.View, "GoToRowTextBox").Text = "18";
        Execute(Required<Button>(host.View, "GoToRowButton"));
        Execute(Required<Button>(host.View, "ShowDetailButton"));
        criteria.SelectedItem = criterion;
        RenderUi();

        Assert.Same(criterion, viewModel.SelectedRowCriteria[1]);
        Assert.Same(criterion, ById<TextBox>(host.View, criterion.AutomationId + "-Override").DataContext);
        Assert.Equal("0", ById<TextBox>(host.View, criterion.AutomationId + "-Override").Text);
        Assert.Equal(9, viewModel.RowScores.Count);
        Assert.Equal(18, viewModel.Results.Count);
        Assert.True(viewModel.HasUnsavedOverrides);
        AssertUniqueAutomationIds(host.View);
    }

    [AvaloniaTheory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("not a row", null)]
    [InlineData("18.0", null)]
    [InlineData("2147483648", null)]
    [InlineData("0", 0)]
    [InlineData("-1", -1)]
    [InlineData("999", 999)]
    public async Task Invalid_go_to_row_never_reuses_a_previous_valid_target(string text, int? parsed)
    {
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel viewModel = new(output, await CreateUiContextAsync());
        using ResultsViewHost host = new(viewModel);
        TextBox target = Required<TextBox>(host.View, "GoToRowTextBox");
        ResultsRowScoreViewModel? selected = viewModel.SelectedRow;
        int page = viewModel.PageIndex;
        target.Text = "18";
        Assert.Equal(18, viewModel.GoToRowNumber);
        Assert.Same(selected, viewModel.SelectedRow);
        Assert.True(viewModel.GoToRowCommand.CanExecute(null));

        target.Text = text;

        // Check before pumping the dispatcher: an invalid edit must disarm the old command immediately.
        Assert.Equal(parsed, viewModel.GoToRowNumber);
        Assert.False(viewModel.GoToRowCommand.CanExecute(null));
        Assert.True(target.Focus(NavigationMethod.Tab, KeyModifiers.None));
        PressEnter(host.Window);
        Assert.Equal(text, target.Text);
        Assert.False(Required<Button>(host.View, "GoToRowButton").IsEffectivelyEnabled);
        Assert.Equal(page, viewModel.PageIndex);
        Assert.Same(selected, viewModel.SelectedRow);
        Assert.False(viewModel.IsDetailVisible);
        Assert.False(viewModel.HasUnsavedOverrides);
        Assert.Equal(0, output.ExportCount);
    }

    [AvaloniaFact]
    public async Task Empty_answer_detail_keeps_blank_preview_and_disabled_override_without_dirty_state()
    {
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), await CreateUiContextAsync(rowCount: 1, primary: "   "));
        using ResultsViewHost host = new(viewModel);
        Assert.Equal("回答空欄", ById<TextBlock>(host.View, "ResultsRowStatus-10").Text);
        Execute(Required<Button>(host.View, "ShowDetailButton"));
        ResultsCriterionViewModel criterion = viewModel.SelectedRowCriteria[0];

        Assert.Equal(ResultsStatusCodes.Empty, criterion.StatusCode);
        Assert.Equal("回答空欄", ById<TextBlock>(host.View, "ResultsSelectedRowStatus").Text);
        Assert.False(Assert.Single(ActiveOverrideEditors(host.View)).IsEffectivelyEnabled);
        Assert.Equal("—", ById<TextBlock>(host.View, "ResultsCriterionAiRaw").Text);
        Assert.Equal("—", ById<TextBlock>(host.View, "ResultsCriterionEffective").Text);
        Assert.Equal("—", ById<TextBlock>(host.View, "ResultsCriterionOverall").Text);
        AssertCriterionTexts(host.View, criterion);
        Assert.False(viewModel.HasUnsavedOverrides);
        Assert.False(ById<TextBlock>(host.View, "ResultsUnsavedOverrides").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Cancelled_detail_remains_blank_after_a_completed_row_receives_zero_override()
    {
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), await CreateUiContextAsync(rowCount: 2, cancelAfterFirst: true));
        using ResultsViewHost host = new(viewModel);
        Execute(Required<Button>(host.View, "ShowDetailButton"));
        Assert.Single(ActiveOverrideEditors(host.View)).Text = "0";
        RenderUi();
        Assert.Equal("0", ById<TextBlock>(host.View, "ResultsCriterionEffective").Text);
        Assert.Equal("0", ById<TextBlock>(host.View, "ResultsCriterionNormalized").Text);
        Required<TextBox>(host.View, "GoToRowTextBox").Text = "11";
        Execute(Required<Button>(host.View, "GoToRowButton"));

        ResultsCriterionViewModel criterion = viewModel.SelectedRowCriteria[0];
        Assert.True(viewModel.IsDetailVisible);
        Assert.Equal(ResultsStatusCodes.Cancelled, criterion.StatusCode);
        Assert.False(Assert.Single(ActiveOverrideEditors(host.View)).IsEffectivelyEnabled);
        AssertCriterionTexts(host.View, criterion);
        Assert.Equal("—", ById<TextBlock>(host.View, "ResultsCriterionEffective").Text);
        Assert.Equal("—", ById<TextBlock>(host.View, "ResultsCriterionOverall").Text);
        Assert.Equal("1 / 2 完了 · error 0 · cancelled 1", ById<TextBlock>(host.View, "ResultsRunSummary").Text);
        Assert.True(ById<TextBlock>(host.View, "ResultsPartialStatus").IsEffectivelyVisible);
        Assert.True(viewModel.HasUnsavedOverrides);
    }

    [AvaloniaFact]
    public async Task Many_nested_criteria_use_a_finite_virtualized_selector_and_one_full_editor()
    {
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), await CreateUiContextAsync(
            rowCount: 2, questionCount: 10, evaluatorCount: 3, criterionCount: 12));
        using ResultsViewHost host = new(viewModel);
        ListBox rows = Required<ListBox>(host.View, "RowScoreList");
        Control firstRow = Assert.IsAssignableFrom<Control>(rows.ContainerFromIndex(0));
        TextBlock status = Assert.Single(firstRow.GetVisualDescendants().OfType<TextBlock>(), text => text.Classes.Contains("row-status"));
        Grid columns = Assert.IsType<Grid>(status.Parent);
        Assert.Equal(5, columns.ColumnDefinitions.Count);
        Assert.Equal(5, columns.Children.Count);
        Assert.Equal(4, Grid.GetColumn(status));
        Assert.Equal("成功", status.Text);
        Assert.Equal("—", Assert.Single(columns.Children.OfType<TextBlock>(), text => Grid.GetColumn(text) == 1).Text);
        Assert.DoesNotContain(
            rows.GetVisualDescendants().OfType<TextBlock>(),
            text => (text.Text ?? string.Empty).Contains("Q10:", StringComparison.Ordinal)
                || text.Text == viewModel.RowScores[0].QuestionEarnedText);
        AssertMeasuredPage(host.View, viewModel);

        Execute(Required<Button>(host.View, "ShowDetailButton"));
        ListBox criteria = Required<ListBox>(host.View, "ResultsList");
        Assert.Equal(360, viewModel.SelectedRowCriteria.Count);
        Assert.Equal(720, viewModel.Results.Count);
        Assert.Same(viewModel.SelectedRowCriteria, criteria.ItemsSource);
        Assert.Contains(criteria.GetVisualDescendants(), visual => visual is VirtualizingStackPanel);
        Assert.True(double.IsFinite(criteria.Bounds.Height));
        Assert.InRange(criteria.Bounds.Height, 44d, host.View.Bounds.Height);
        Assert.InRange(criteria.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 16);
        Assert.Single(ActiveOverrideEditors(host.View));
        Assert.Equal(viewModel.SelectedRow?.QuestionEarnedText, ById<TextBlock>(host.View, "ResultsQuestionEarnedFull").Text);
        Assert.Contains("Q10:", ById<TextBlock>(host.View, "ResultsQuestionEarnedFull").Text, StringComparison.Ordinal);

        ResultsCriterionViewModel last = viewModel.SelectedRowCriteria[^1];
        criteria.SelectedItem = last;
        criteria.ScrollIntoView(last);
        RenderUi();

        Assert.Same(last, Assert.Single(ActiveOverrideEditors(host.View)).DataContext);
        Assert.Equal(last.AutomationId + "-Override", AutomationProperties.GetAutomationId(Assert.Single(ActiveOverrideEditors(host.View))));
        AssertCriterionTexts(host.View, last);
        AssertUniqueAutomationIds(host.View);
    }

    [AvaloniaFact]
    public async Task Invalid_override_and_output_collision_remain_nonmodal_and_correctable()
    {
        RecordingOutputBoundary output = new()
        {
            Assess = (_, path) => path.EndsWith("occupied.xlsx", StringComparison.Ordinal)
                ? new ResultsOutputPathAssessment(ResultsOutputStatusCodes.TargetExists, false, true)
                : ResultsOutputPathAssessment.Valid,
        };
        using ResultsOutputViewModel viewModel = new(output, await CreateUiContextAsync(rowCount: 1, criterionCount: 1));
        using ResultsViewHost host = new(viewModel);
        Execute(Required<Button>(host.View, "ShowDetailButton"));
        TextBox editor = Assert.Single(ActiveOverrideEditors(host.View));
        ResultsCriterionViewModel criterion = viewModel.SelectedRowCriteria[0];
        editor.Text = "11";
        RenderUi();

        Assert.True(viewModel.HasOverrideErrors);
        Assert.False(Required<Button>(host.View, "ExportButton").IsEffectivelyEnabled);
        Assert.True(ById<TextBlock>(host.View, "ResultsCriterionOverrideError").IsEffectivelyVisible);
        Assert.Equal(criterion.OverrideError, ById<TextBlock>(host.View, "ResultsCriterionOverrideError").Text);
        Assert.Equal(viewModel.OverrideValidationText, Assert.IsType<TextBlock>(ById<Border>(host.View, "OverrideValidationSummary").Child).Text);
        Assert.Equal("—", ById<TextBlock>(host.View, "ResultsSelectedFinalScore").Text);
        AssertCriterionTexts(host.View, criterion);
        editor.Text = "8";
        RenderUi();
        Assert.Equal(8m, criterion.EffectiveRaw);
        Assert.Equal(80m, criterion.OverallScore);
        Assert.Same(editor, Assert.Single(ActiveOverrideEditors(host.View)));

        TextBox path = Required<TextBox>(host.View, "OutputPathTextBox");
        path.Text = Path.Combine(Path.GetTempPath(), "occupied.xlsx");
        RenderUi();
        Assert.True(viewModel.OutputTargetExists);
        Assert.False(Required<Button>(host.View, "ExportButton").IsEffectivelyEnabled);
        Assert.Equal(viewModel.OutputPathStatusText, Assert.IsType<TextBlock>(ById<Border>(host.View, "OutputPathValidationSummary").Child).Text);
        path.Text = Path.Combine(Path.GetTempPath(), "synthetic-review-T21.xlsx");
        RenderUi();

        Assert.Equal(path.Text, viewModel.OutputPath);
        Assert.True(Required<Button>(host.View, "ExportButton").IsEffectivelyEnabled);
        Assert.True(viewModel.HasUnsavedOverrides);
        Assert.Equal(0, output.ExportCount);
        Assert.True(Required<Button>(host.View, "ShowListButton").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task Override_error_navigation_reaches_off_page_and_offscreen_criteria_and_stays_correctable()
    {
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel viewModel = new(output, await CreateUiContextAsync(
            questionCount: 2, evaluatorCount: 2, criterionCount: 12));
        using ResultsViewHost host = new(viewModel);
        ResultsCriterionViewModel[] errors =
        [
            viewModel.Results.Single(item => item.SourceRowNumber == 10 && item.CriterionId == "Q1E1C2"),
            viewModel.Results.Single(item => item.SourceRowNumber == 18 && item.CriterionId == "Q2E2C11"),
            viewModel.Results.Single(item => item.SourceRowNumber == 18 && item.CriterionId == "Q2E2C12"),
        ];
        Button nextError = Required<Button>(host.View, "NextOverrideErrorButton");
        Assert.False(nextError.IsEffectivelyVisible);
        Assert.Same(viewModel.NextOverrideErrorCommand, nextError.Command);
        foreach (ResultsCriterionViewModel item in errors)
        {
            item.OverrideText = "11";
        }

        RenderUi();
        Assert.Equal(3, viewModel.OverrideErrorCount);
        Assert.Equal("エラー 3 件・次へ", nextError.Content);
        Assert.Contains("3 件", AutomationProperties.GetName(nextError), StringComparison.Ordinal);
        Assert.True(nextError.IsEffectivelyVisible);
        Assert.False(Required<Button>(host.View, "ExportButton").IsEffectivelyEnabled);
        AssertMeasuredPage(host.View, viewModel);
        for (int index = 0; index < errors.Length; index++)
        {
            ResultsCriterionViewModel target = errors[index];
            Execute(nextError);

            Assert.True(viewModel.IsDetailVisible);
            Assert.Equal((target.SourceRowNumber - 10) / viewModel.PageSize, viewModel.PageIndex);
            Assert.Equal(target.SourceRowNumber, viewModel.SelectedRow?.SourceRowNumber);
            Assert.Same(viewModel.SelectedRow, Required<ListBox>(host.View, "RowScoreList").SelectedItem);
            ListBox criteria = Required<ListBox>(host.View, "ResultsList");
            Assert.Same(target, viewModel.SelectedCriterion);
            Assert.Same(target, criteria.SelectedItem);
            Assert.IsAssignableFrom<Control>(criteria.ContainerFromIndex(criteria.SelectedIndex));
            if (index == errors.Length - 1)
            {
                ScrollViewer scroll = Assert.Single(criteria.GetVisualDescendants().OfType<ScrollViewer>(),
                    item => ReferenceEquals(item.TemplatedParent, criteria));
                criteria.ScrollIntoView(viewModel.SelectedRowCriteria[0]);
                RenderUi();
                Assert.InRange(scroll.Offset.Y, 0d, 1d);
                Assert.Same(target, criteria.SelectedItem);
                Execute(nextError);
                Assert.Same(target, criteria.SelectedItem);
                Assert.True(scroll.Offset.Y > 0d);
                Assert.Equal(1, viewModel.OverrideErrorCount);
            }

            TextBox editor = Assert.Single(ActiveOverrideEditors(host.View));
            Assert.Same(target, editor.DataContext);
            Assert.Equal(target.AutomationId + "-Override", AutomationProperties.GetAutomationId(editor));
            Assert.Equal(target.OverrideError, ById<TextBlock>(host.View, "ResultsCriterionOverrideError").Text);
            Assert.True(editor.Focus(NavigationMethod.Tab, KeyModifiers.None));

            editor.Text = index == 0 ? "8" : index == 1 ? "0" : string.Empty;
            RenderUi();

            Assert.Equal(errors.Length - index - 1, viewModel.OverrideErrorCount);
            Assert.Equal(viewModel.OverrideErrorNavigationText, nextError.Content);
            Assert.False(target.HasOverrideError);
            Assert.Same(editor, Assert.Single(ActiveOverrideEditors(host.View)));
            Assert.Same(editor, host.Window.FocusManager?.GetFocusedElement());
        }

        Assert.False(nextError.IsEffectivelyVisible);
        Assert.False(nextError.IsEffectivelyEnabled);
        Assert.True(Required<Button>(host.View, "ExportButton").IsEffectivelyEnabled);
        Assert.False(viewModel.HasOverrideErrors);
        Assert.True(viewModel.HasUnsavedOverrides);
        Assert.Equal(0, output.ExportCount);
        AssertUniqueAutomationIds(host.View);
    }

    [AvaloniaFact]
    public async Task Dirty_export_cancel_and_success_states_follow_the_original_output_boundary()
    {
        TaskCompletionSource<ResultsOutputResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        CancellationToken capturedToken = default;
        RecordingOutputBoundary output = new()
        {
            Export = (request, token) =>
            {
                capturedToken = token;
                return ++calls == 1 ? release.Task : Task.FromResult(new ResultsOutputResult(ResultsOutputStatusCodes.Success, request.OutputPath));
            },
        };
        using ResultsOutputViewModel viewModel = new(output, await CreateUiContextAsync(rowCount: 1));
        using ResultsViewHost host = new(viewModel);
        Execute(Required<Button>(host.View, "ShowDetailButton"));
        TextBox editor = Assert.Single(ActiveOverrideEditors(host.View));
        editor.Text = "8";
        RenderUi();
        Assert.True(ById<TextBlock>(host.View, "ResultsUnsavedOverrides").IsEffectivelyVisible);
        Task exporting = viewModel.ExportAsync(TestContext.Current.CancellationToken);
        try
        {
            RenderUi();
            Assert.True(viewModel.IsExporting);
            Assert.False(Required<Button>(host.View, "ExportButton").IsEffectivelyEnabled);
            Assert.False(Required<TextBox>(host.View, "OutputPathTextBox").IsEffectivelyEnabled);
            Assert.True(Required<Button>(host.View, "CancelExportButton").IsEffectivelyEnabled);
            Assert.Equal(viewModel.ExportStatusText, ById<TextBlock>(host.View, "ExportStatus").Text);
            Assert.Equal("8", Assert.Single(Assert.IsType<ResultsOutputRequest>(output.LastRequest).Overrides).Value);
            editor.Text = "7";
            RenderUi();
            Execute(Required<Button>(host.View, "CancelExportButton"));
            Assert.True(capturedToken.IsCancellationRequested);
            Assert.True(viewModel.IsExportCancelling);
            Assert.False(Required<Button>(host.View, "CancelExportButton").IsEffectivelyEnabled);
            Assert.Equal(viewModel.ExportStatusText, ById<TextBlock>(host.View, "ExportStatus").Text);
        }
        finally
        {
            release.TrySetResult(new ResultsOutputResult(ResultsOutputStatusCodes.Cancelled));
        }

        await exporting;
        RenderUi();
        Assert.Equal(ResultsOutputStatusCodes.Cancelled, viewModel.LastExportCode);
        Assert.True(viewModel.HasUnsavedOverrides);
        Assert.Equal("final commit 前に取り消しました。完成名は作成していません。", ById<TextBlock>(host.View, "ExportStatus").Text);
        Assert.True(Required<TextBox>(host.View, "OutputPathTextBox").IsEffectivelyEnabled);
        Assert.True(Required<Button>(host.View, "ExportButton").IsEffectivelyEnabled);

        await viewModel.ExportAsync(TestContext.Current.CancellationToken);
        RenderUi();

        Assert.Equal(2, output.ExportCount);
        Assert.Equal("7", Assert.Single(Assert.IsType<ResultsOutputRequest>(output.LastRequest).Overrides).Value);
        Assert.False(viewModel.HasUnsavedOverrides);
        Assert.False(ById<TextBlock>(host.View, "ResultsUnsavedOverrides").IsEffectivelyVisible);
        Assert.Equal(viewModel.ExportStatusText, ById<TextBlock>(host.View, "ExportStatus").Text);
        Assert.Same(editor, Assert.Single(ActiveOverrideEditors(host.View)));
        // The fake returned SUCCESS; this checks presentation, not the existence of a workbook.
    }

    [AvaloniaFact]
    public async Task Legacy_completed_run_does_not_claim_a_final_workbook_before_manual_export()
    {
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel viewModel = new(output, await CreateUiContextAsync(rowCount: 1));
        using ResultsViewHost host = new(viewModel);

        Assert.False(viewModel.IsPartial);
        Assert.False(viewModel.IsAutomaticOutput);
        Assert.Equal(string.Empty, viewModel.FinalPath);
        Assert.Equal(string.Empty, viewModel.PartialPath);
        Assert.Equal("legacy manual export modeです。", ById<TextBlock>(host.View, "ResultsDurableOutput").Text);
        Assert.Equal("出力準備を確認してください。", ById<TextBlock>(host.View, "ExportStatus").Text);
        Assert.Equal(viewModel.InputStateText, ById<TextBlock>(host.View, "ResultsInputState").Text);
        string text = string.Join("\n", host.View.GetVisualDescendants().OfType<TextBlock>()
            .Where(item => item.IsEffectivelyVisible).Select(item => item.Text));
        Assert.DoesNotContain("全operationの実行とfinalizationが終了", text, StringComparison.Ordinal);
        Assert.DoesNotContain("自動commitしました", text, StringComparison.Ordinal);
        Assert.Equal(0, output.ExportCount);
    }

    [AvaloniaFact]
    public async Task Failed_run_shows_exact_failure_count_and_input_changed_export_block()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(10, 10);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await new QuantificationOrchestrator(
            new ScriptedRowSource((request, _) => Task.FromResult(new EvaluationRowData(
                request.SourceRowNumber,
                new Dictionary<string, string?> { ["A"] = "synthetic answer", ["B"] = "synthetic support" }))),
            new ScriptedRunner((_, _, _) => Task.FromResult(EvaluationRunnerResult.Failed(ResultsStatusCodes.NetworkFailed))),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot())
            {
                RecheckOutcomes = new Queue<bool>([true, false]),
            }).RunAsync(
                new QuantificationRunRequest
                {
                    DraftDefinition = definition,
                    WorkbookMetadata = metadata,
                    InputPath = Path.Combine(Path.GetTempPath(), "synthetic-T21-changed-input.xlsx"),
                    ModelId = "model-test",
                    MaximumPromptTokens = 64_000,
                    MaximumContextWindowTokens = 128_000,
                    MaxConcurrency = 1,
                }, cancellationToken: TestContext.Current.CancellationToken);
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel viewModel = new(output, U04TestSupport.Context(summary));
        using ResultsViewHost host = new(viewModel);

        Assert.Equal(QuantificationRunStatusCodes.InputChanged, summary.StatusCode);
        Assert.False(viewModel.IsInputUnchanged);
        Assert.Equal(1, viewModel.FailureCount);
        Assert.Equal("1 / 1 完了 · error 1 · cancelled 0", ById<TextBlock>(host.View, "ResultsRunSummary").Text);
        Assert.Equal(viewModel.InputStateText, ById<TextBlock>(host.View, "ResultsInputState").Text);
        Assert.True(ById<TextBlock>(host.View, "ResultsInputState").IsEffectivelyVisible);
        Assert.False(Required<Button>(host.View, "ExportButton").IsEffectivelyEnabled);
        Assert.Equal(string.Empty, viewModel.FinalPath);
        Assert.Equal(0, output.ExportCount);
        Assert.Equal("技術エラー", ById<TextBlock>(host.View, "ResultsRowStatus-10").Text);
        Execute(Required<Button>(host.View, "ShowDetailButton"));
        Assert.Equal("技術エラー", ById<TextBlock>(host.View, "ResultsSelectedRowStatus").Text);
        Assert.Equal(ResultsStatusCodes.NetworkFailed, ById<TextBlock>(host.View, "ResultsCriterionStatus").Text);
        Assert.Equal("—", ById<TextBlock>(host.View, "ResultsSelectedFinalScore").Text);
        AssertCriterionTexts(host.View, viewModel.SelectedRowCriteria[0]);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Durable_final_partial_and_cleanup_warning_bind_only_the_supplied_run_state(bool hasFinal, bool cleanupFailed)
    {
        ExecutionRunContext source = await CreateUiContextAsync(rowCount: 1, criterionCount: 1);
        RunSummary summary = CreateDurablePresentationSummary(source.Summary, hasFinal, cleanupFailed);
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), U04TestSupport.Context(summary));
        using ResultsViewHost host = new(viewModel);

        Assert.True(viewModel.IsAutomaticOutput);
        Assert.Equal(!hasFinal, viewModel.IsPartial);
        Assert.Equal(cleanupFailed, viewModel.PartialCleanupFailed);
        Assert.Equal(3, viewModel.CompletedEvaluationCount);
        Assert.Equal(3, viewModel.PlannedEvaluationCount);
        Assert.Equal(0, viewModel.FailureCount);
        Assert.Equal(0, viewModel.CancelledCount);
        Assert.Equal(viewModel.RunSummaryText, ById<TextBlock>(host.View, "ResultsRunSummary").Text);
        Assert.Equal(viewModel.DurableOutputText, ById<TextBlock>(host.View, "ResultsDurableOutput").Text);
        Assert.Equal(viewModel.ExportStatusText, ById<TextBlock>(host.View, "ExportStatus").Text);
        Assert.Equal(viewModel.InputStateText, ById<TextBlock>(host.View, "ResultsInputState").Text);
        Assert.Equal(hasFinal, ById<TextBlock>(host.View, "ResultsDurableOutput").Text!.StartsWith("final:", StringComparison.Ordinal));
        Assert.Equal(cleanupFailed, ById<TextBlock>(host.View, "ResultsDurableOutput").Text!.Contains("partial cleanup warning:", StringComparison.Ordinal));
        Assert.True(Required<TextBox>(host.View, "OutputPathTextBox").IsEffectivelyEnabled);
        Execute(Required<Button>(host.View, "ShowDetailButton"));
        Assert.Equal("99", ById<TextBlock>(host.View, "ResultsSelectedFinalScore").Text);
        Assert.Equal("Question text Q1: 1", ById<TextBlock>(host.View, "ResultsQuestionEarnedFull").Text);
        Assert.Equal(99m, viewModel.SelectedRowCriteria[0].OverallScore);
        AssertCriterionTexts(host.View, viewModel.SelectedRowCriteria[0]);
        AssertUniqueAutomationIds(host.View);
    }

    [AvaloniaFact]
    public async Task Cached_view_reattachment_context_replacement_and_load_ignore_old_callbacks()
    {
        using ResultsOutputViewModel first = new(new RecordingOutputBoundary(), await CreateUiContextAsync());
        using ResultsOutputViewModel second = new(new RecordingOutputBoundary(), await CreateUiContextAsync(firstRow: 30));
        ExecutionRunContext nextRun = await CreateUiContextAsync(firstRow: 50, rowCount: 2);
        using ResultsViewHost host = new(first);
        Execute(Required<Button>(host.View, "ShowDetailButton"));
        ListBox criteria = Required<ListBox>(host.View, "ResultsList");
        ResultsCriterionViewModel retained = first.SelectedRowCriteria[1];
        criteria.SelectedItem = retained;
        RenderUi();
        Assert.Single(ActiveOverrideEditors(host.View)).Text = "8";
        RenderUi();

        host.Window.Content = new Border();
        RenderUi();
        Assert.Null(Required<ContentControl>(host.View, "CriterionEditor").Content);
        retained.OverrideText = "7";
        host.Window.Content = host.View;
        RenderUi();

        Assert.True(first.IsDetailVisible);
        Assert.Same(retained, Assert.Single(ActiveOverrideEditors(host.View)).DataContext);
        Assert.Equal("7", Assert.Single(ActiveOverrideEditors(host.View)).Text);
        first.ShowListCommand.Execute(null); // Leave an old-VM refresh queued intentionally.
        host.View.DataContext = second;
        second.ShowDetailCommand.Execute(null);
        RenderUi();
        ResultsCriterionViewModel secondCriterion = second.SelectedRowCriteria[0];
        Assert.Same(secondCriterion, Assert.Single(ActiveOverrideEditors(host.View)).DataContext);
        retained.OverrideText = "9";
        first.GoToRowNumber = 18;
        first.GoToRowCommand.Execute(null);
        RenderUi();
        Assert.Equal(30, second.SelectedRow?.SourceRowNumber);
        Assert.Same(secondCriterion, Assert.Single(ActiveOverrideEditors(host.View)).DataContext);
        Assert.False(second.HasUnsavedOverrides);

        second.ShowListCommand.Execute(null); // The same VM also invalidates queued work across Load.
        second.Load(nextRun);
        RenderUi();
        Assert.False(second.IsDetailVisible);
        Assert.Equal(50, second.SelectedRow?.SourceRowNumber);
        Assert.Equal(string.Empty, Required<TextBox>(host.View, "GoToRowTextBox").Text);
        Assert.Empty(ActiveOverrideEditors(host.View));
        secondCriterion.OverrideText = "6";
        Execute(Required<Button>(host.View, "ShowDetailButton"));
        Assert.False(second.HasUnsavedOverrides);
        Assert.Same(second.SelectedRowCriteria[0], Assert.Single(ActiveOverrideEditors(host.View)).DataContext);
        Assert.Equal(string.Empty, Assert.Single(ActiveOverrideEditors(host.View)).Text);
        AssertUniqueAutomationIds(host.View);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Durable_preview_final_levels_match_written_formula_cache_for_success_unprocessed_and_empty(
        bool cancelAfterFirst,
        bool emptyPrimary)
    {
        ExecutionRunContext context = await new ResultsDurableFixture().RunAsync(
            rowCount: 2,
            primary: emptyPrimary ? "   " : "synthetic answer",
            cancelAfterRows: cancelAfterFirst ? 1 : null);
        Assert.Equal(ResultsStatusCodes.Success, Assert.Single(context.Summary.References).StatusCode);
        RunOutputPreparation preparation = context.Summary.PrepareOutput();
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), context);
        using MemoryStream stream = new();
        using SpreadsheetDocument document = SpreadsheetDocument.Create(stream, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        WorkbookPart workbookPart = document.AddWorkbookPart();
        WorksheetPart original = workbookPart.AddNewPart<WorksheetPart>();
        original.Worksheet = new Worksheet(new SheetData());
        workbookPart.Workbook = new Workbook(new Sheets(new Sheet
        {
            Id = workbookPart.GetIdOfPart(original),
            SheetId = 1U,
            Name = context.Summary.Snapshot.Definition.SourceSheet,
        }));
        AppOwnedSheetNames names = new AppOwnedSheetNameResolver().Resolve(document);
        ConfigCellAddressMap config = new ConfigSheetWriter().Write(document, preparation.Snapshot, names);
        ResultsSheetWriteResult written = new ResultsSheetWriter().Write(document, preparation.Snapshot, names, config, preparation.Rows);

        foreach (ResultsRowScoreViewModel row in viewModel.RowScores)
        {
            Assert.Equal(row.FinalRaw, Cached(row.SourceRowNumber, ResultsSheetWriter.FinalRawHeader));
            Assert.Equal(row.FinalScore, Cached(row.SourceRowNumber, ResultsSheetWriter.FinalScoreHeader));
            Assert.Equal(row.SpecialEarned, Cached(row.SourceRowNumber, ResultsSheetWriter.SpecialEarnedHeader));
            Assert.Equal(row.SimilarityPenalty, Cached(row.SourceRowNumber, ResultsSheetWriter.SimilarityPenaltySuffix));
            ResultsCriterionViewModel criterion = viewModel.Results.Single(item => item.SourceRowNumber == row.SourceRowNumber);
            Assert.Equal(criterion.EffectiveRaw, Cached(row.SourceRowNumber, ResultsSheetWriter.EffectiveRawSuffix));
            Assert.Equal(criterion.NormalizedScore, Cached(row.SourceRowNumber, ResultsSheetWriter.NormalizedSuffix));
            Assert.Equal(criterion.EvaluatorScore, Cached(row.SourceRowNumber, ResultsSheetWriter.EvaluatorScoreSuffix));
            Assert.Equal(criterion.QuestionScore, Cached(row.SourceRowNumber, ResultsSheetWriter.QuestionNormalizedSuffix));
            Assert.Equal(row.FinalScore, criterion.OverallScore);
        }

        if (cancelAfterFirst)
        {
            Assert.NotNull(preparation.Rows[1].Questions[0].Similarity);
            Assert.Null(preparation.Rows[1].Questions[0].Similarity!.AiRaw);
            // Presentation distinguishes unknown input without changing workbook formulas.
            Assert.Equal(0m, Cached(11, ResultsSheetWriter.QuestionEarnedSuffix));
            Assert.Equal("Question text Q1: 40", viewModel.RowScores[0].QuestionEarnedText);
            Assert.Equal("Question text Q1: —", viewModel.RowScores[1].QuestionEarnedText);
            Assert.Null(Cached(11, ResultsSheetWriter.SimilarityPenaltySuffix));
            Assert.Null(Cached(11, ResultsSheetWriter.FinalRawHeader));
            Assert.Null(Cached(11, ResultsSheetWriter.FinalScoreHeader));
        }
        else
        {
            Assert.Equal(emptyPrimary ? 20m : 60m, Cached(11, ResultsSheetWriter.FinalRawHeader));
            Assert.Equal(emptyPrimary ? 20m : 60m, Cached(11, ResultsSheetWriter.FinalScoreHeader));
            Assert.Equal(emptyPrimary ? 0m : 40m, Cached(11, ResultsSheetWriter.QuestionEarnedSuffix));
            Assert.Equal(emptyPrimary ? "Question text Q1: 0" : "Question text Q1: 40", viewModel.RowScores[1].QuestionEarnedText);
            Assert.Equal(0m, Cached(11, ResultsSheetWriter.SimilarityPenaltySuffix));
        }

        decimal? Cached(int row, string field) => Assert.Single(written.FormulaCells, cell =>
            cell.Definition.Target.RowNumber == row && cell.Definition.Identity.Field == field).CachedValue;
    }

    [AvaloniaFact]
    public async Task Actual_durable_cancel_shows_unprocessed_blank_final_and_snapshot_only_input_state()
    {
        ExecutionRunContext context = await new ResultsDurableFixture().RunAsync(rowCount: 2, cancelAfterRows: 1);
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), context);
        using ResultsViewHost host = new(viewModel);
        AssertMeasuredPage(host.View, viewModel);
        Assert.True(viewModel.IsInputUnchanged);
        Assert.Contains("不変性確認は未実施", ById<TextBlock>(host.View, "ResultsInputState").Text, StringComparison.Ordinal);
        Assert.DoesNotContain("入力変更を検出", ById<TextBlock>(host.View, "ResultsInputState").Text, StringComparison.Ordinal);
        Assert.Equal("成功", ById<TextBlock>(host.View, "ResultsRowStatus-10").Text);
        viewModel.Results[0].OverrideText = "8";
        RenderUi();
        Assert.Equal(84m, viewModel.RowScores[0].FinalScore);
        Assert.Equal("Question text Q1: 64", viewModel.RowScores[0].QuestionEarnedText);
        Required<TextBox>(host.View, "GoToRowTextBox").Text = "11";
        Execute(Required<Button>(host.View, "GoToRowButton"));
        Assert.Equal("未処理・未確定", ById<TextBlock>(host.View, "ResultsRowStatus-11").Text);
        Execute(Required<Button>(host.View, "ShowDetailButton"));

        Assert.Equal("未処理・未確定", ById<TextBlock>(host.View, "ResultsSelectedRowStatus").Text);
        Assert.Equal("—", ById<TextBlock>(host.View, "ResultsSelectedFinalRaw").Text);
        Assert.Equal("—", ById<TextBlock>(host.View, "ResultsSelectedFinalScore").Text);
        Assert.Equal("Question text Q1: —", ById<TextBlock>(host.View, "ResultsQuestionEarnedFull").Text);
        Assert.Equal(ResultsStatusCodes.Cancelled, ById<TextBlock>(host.View, "ResultsCriterionStatus").Text);
        Assert.False(Assert.Single(ActiveOverrideEditors(host.View)).IsEffectivelyEnabled);
        foreach (string id in new[] { "ResultsCriterionAiRaw", "ResultsCriterionEffective", "ResultsCriterionNormalized", "ResultsCriterionEvaluator", "ResultsCriterionQuestion", "ResultsCriterionOverall" })
        {
            Assert.Equal("—", ById<TextBlock>(host.View, id).Text);
        }

        AssertUniqueAutomationIds(host.View);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_durable_output_keeps_completed_scores_visible_and_export_disabled(bool checkpointFailure)
    {
        ResultsDurableFixture fixture = new()
        {
            FinalizationCode = ResultsOutputStatusCodes.OutputInvalid,
            CheckpointFailureCode = checkpointFailure ? CheckpointStatusCodes.SaveFailed : null,
            CheckpointFailureAtRowCount = 2,
        };
        ExecutionRunContext context = await fixture.RunAsync(rowCount: 2, specialPoints: 10m);
        Assert.Equal(checkpointFailure ? QuantificationRunStatusCodes.CheckpointFailed : QuantificationRunStatusCodes.OutputInvalid,
            context.Summary.StatusCode);
        Assert.Empty(context.Summary.PrepareOutput().Rows);
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), context);
        using ResultsViewHost host = new(viewModel);
        Execute(Required<Button>(host.View, "ShowDetailButton"));

        Assert.Equal("63", ById<TextBlock>(host.View, "ResultsSelectedFinalRaw").Text);
        Assert.Equal("63", ById<TextBlock>(host.View, "ResultsSelectedFinalScore").Text);
        Assert.Equal("Question text Q1: 35", ById<TextBlock>(host.View, "ResultsQuestionEarnedFull").Text);
        Assert.Equal("成功", ById<TextBlock>(host.View, "ResultsSelectedRowStatus").Text);
        Assert.Equal("final workbookは作成されていません。partial checkpointを確認してください。",
            ById<TextBlock>(host.View, "ExportStatus").Text);
        Assert.False(Required<Button>(host.View, "ExportButton").IsEffectivelyEnabled);
        Assert.Single(ActiveOverrideEditors(host.View)).Text = "8";
        RenderUi();
        Assert.Equal("84", ById<TextBlock>(host.View, "ResultsSelectedFinalScore").Text);
        Assert.False(Required<Button>(host.View, "ExportButton").IsEffectivelyEnabled);

        Required<TextBox>(host.View, "GoToRowTextBox").Text = "11";
        Execute(Required<Button>(host.View, "GoToRowButton"));
        Assert.Equal(checkpointFailure ? "—" : "63", ById<TextBlock>(host.View, "ResultsSelectedFinalScore").Text);
        Assert.Equal(checkpointFailure ? "Question text Q1: —" : "Question text Q1: 35", ById<TextBlock>(host.View, "ResultsQuestionEarnedFull").Text);
        Assert.Equal(checkpointFailure ? "未処理・未確定" : "成功", ById<TextBlock>(host.View, "ResultsSelectedRowStatus").Text);
        Assert.False(Required<Button>(host.View, "ExportButton").IsEffectivelyEnabled);
        Assert.Equal(string.Empty, viewModel.LastSuccessfulExportPath);
    }

    [AvaloniaFact]
    public async Task Header_and_saved_revision_follow_the_loaded_run_and_success_receipt_not_the_candidate_path()
    {
        ExecutionRunContext context = await new ResultsDurableFixture().RunAsync();
        string returnedPath = Path.Combine(Path.GetTempPath(), "synthetic-T21-actual-revision.xlsx");
        RecordingOutputBoundary output = new()
        {
            Export = (_, _) => Task.FromResult(new ResultsOutputResult(ResultsOutputStatusCodes.Success, returnedPath)),
        };
        using ResultsOutputViewModel viewModel = new(output, context);
        using ResultsViewHost host = new(viewModel);
        Assert.Equal("前回の実行結果", ById<TextBlock>(host.View, "ResultsRunHeading").Text);
        Assert.True(ById<TextBlock>(host.View, "ResultsRunHeading").IsEffectivelyVisible);
        string identity = Assert.IsType<string>(ById<TextBlock>(host.View, "ResultsRunIdentity").Text);
        Assert.Equal(viewModel.RunIdentityText, identity);
        Assert.Contains(Path.GetFileName(context.InputPath), identity, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetDirectoryName(context.InputPath)!, identity, StringComparison.Ordinal);
        Assert.Contains(context.Summary.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture), identity, StringComparison.Ordinal);
        Assert.Contains(context.Summary.EndedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture), identity, StringComparison.Ordinal);
        Assert.Equal("この結果は表示時の採点draftから自動再評価しない", ById<TextBlock>(host.View, "ResultsSnapshotCaption").Text);
        Assert.True(ById<TextBlock>(host.View, "ResultsSnapshotCaption").IsEffectivelyVisible);
        Assert.True(ById<TextBlock>(host.View, "ResultsOriginalOutputLabel").IsEffectivelyVisible);
        Assert.False(ById<TextBlock>(host.View, "ResultsLastSuccessfulExport").IsEffectivelyVisible);
        Assert.Equal(string.Empty, ById<TextBlock>(host.View, "ResultsLastSuccessfulExport").Text);
        Assert.False(viewModel.HasUnsavedOverrides);
        Assert.Equal("未保存の override なし", ById<TextBlock>(host.View, "ResultsNoUnsavedOverrides").Text);
        Assert.True(ById<TextBlock>(host.View, "ResultsNoUnsavedOverrides").IsEffectivelyVisible);
        string originalOutput = viewModel.DurableOutputText;
        viewModel.OutputPath = Path.Combine(Path.GetTempPath(), "synthetic-T21-request-revision.xlsx");
        viewModel.Results[0].OverrideText = "8";

        await viewModel.ExportAsync(TestContext.Current.CancellationToken);
        viewModel.OutputPath = Path.Combine(Path.GetTempPath(), "synthetic-T21-future-revision.xlsx");
        RenderUi();

        Assert.Equal(context.Summary.FinalPath, viewModel.FinalPath);
        Assert.Equal(originalOutput, ById<TextBlock>(host.View, "ResultsDurableOutput").Text);
        Assert.Equal("保存済み修正版: " + returnedPath, ById<TextBlock>(host.View, "ResultsLastSuccessfulExport").Text);
        Assert.True(ById<TextBlock>(host.View, "ResultsLastSuccessfulExport").IsEffectivelyVisible);
        Assert.Equal(returnedPath, viewModel.LastSuccessfulExportPath);
        Assert.Equal(identity, ById<TextBlock>(host.View, "ResultsRunIdentity").Text);
        Assert.False(viewModel.HasUnsavedOverrides);
        Assert.True(ById<TextBlock>(host.View, "ResultsNoUnsavedOverrides").IsEffectivelyVisible);
        AssertMeasuredPage(host.View, viewModel);

        viewModel.Results[0].OverrideText = "7";
        RenderUi();
        Assert.True(ById<TextBlock>(host.View, "ResultsUnsavedOverrides").IsEffectivelyVisible);
        Assert.False(ById<TextBlock>(host.View, "ResultsNoUnsavedOverrides").IsEffectivelyVisible);
        Assert.Equal("保存済み修正版: " + returnedPath, ById<TextBlock>(host.View, "ResultsLastSuccessfulExport").Text);
        Assert.True(ById<TextBlock>(host.View, "ResultsLastSuccessfulExport").IsEffectivelyVisible);
        Assert.Equal(originalOutput, ById<TextBlock>(host.View, "ResultsDurableOutput").Text);

        ExecutionRunContext next = await new ResultsDurableFixture
        {
            InputPath = Path.Combine(Path.GetTempPath(), "synthetic-T21-next-run-input.xlsx"),
        }.RunAsync();
        viewModel.Load(next);
        RenderUi();

        Assert.Equal(viewModel.RunIdentityText, ById<TextBlock>(host.View, "ResultsRunIdentity").Text);
        Assert.Contains(Path.GetFileName(next.InputPath), viewModel.RunIdentityText, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFileName(context.InputPath), viewModel.RunIdentityText, StringComparison.Ordinal);
        Assert.False(ById<TextBlock>(host.View, "ResultsLastSuccessfulExport").IsEffectivelyVisible);
        Assert.Equal(string.Empty, ById<TextBlock>(host.View, "ResultsLastSuccessfulExport").Text);
        Assert.Equal(string.Empty, viewModel.LastSuccessfulExportPath);
        AssertUniqueAutomationIds(host.View);
        // These are synthetic boundary receipts; this test makes no claim about files on disk.
    }

    private static async Task<ExecutionRunContext> CreateUiContextAsync(
        int firstRow = 10,
        int rowCount = 9,
        int questionCount = 1,
        int evaluatorCount = 1,
        int criterionCount = 2,
        string primary = "synthetic answer",
        bool cancelAfterFirst = false)
    {
        QuestionDefinition[] questions = Enumerable.Range(1, questionCount).Select(question =>
        {
            EvaluatorDefinition[] evaluators = Enumerable.Range(1, evaluatorCount).Select(evaluator =>
            {
                string id = $"Q{question.ToString(CultureInfo.InvariantCulture)}E{evaluator.ToString(CultureInfo.InvariantCulture)}";
                EvaluatorDefinition template = U01TestSupport.Evaluator(id, id + "C1");
                return template with
                {
                    Criteria = Enumerable.Range(1, criterionCount).Select(criterion => template.Criteria[0] with
                    {
                        Id = id + "C" + criterion.ToString(CultureInfo.InvariantCulture),
                        DisplayName = "Criterion " + criterion.ToString(CultureInfo.InvariantCulture),
                    }).ToImmutableArray(),
                };
            }).ToArray();
            return U01TestSupport.Question("Q" + question.ToString(CultureInfo.InvariantCulture), "A", ["B"], true, evaluators);
        }).ToArray();
        QuantificationDefinition definition = U01TestSupport.Definition(firstRow, firstRow + rowCount - 1, questions);
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(
            definition, U01TestSupport.ValidateMapping(definition).Metadata, primary, cancelAfterFirst);
        return U04TestSupport.Context(summary);
    }

    private static RunSummary CreateDurablePresentationSummary(RunSummary source, bool hasFinal, bool cleanupFailed)
    {
        // A synthetic receipt for binding assertions, not a claim that these paths exist.
        // Use the internal summary constructor without widening the production API for a UI fixture.
        ImmutableArray<CheckpointReference> references =
        [
            new()
            {
                QuestionId = "Q1",
                Answer = "synthetic reference",
                StatusCode = ResultsStatusCodes.Success,
                GeneratedAtUtc = source.StartedAtUtc,
                AttemptCount = 1,
            },
        ];
        EvaluationUnitResult unit = Assert.Single(source.Units);
        ImmutableArray<CheckpointCompletedRow> completedRows =
        [
            new()
            {
                SourceRowNumber = unit.Item.SourceRowNumber,
                NormalResults =
                [
                    new()
                    {
                        QuestionId = unit.Item.QuestionId,
                        EvaluatorId = unit.Item.EvaluatorId,
                        StatusCode = unit.StatusCode,
                        AttemptCount = 1,
                        Scorable = unit.Scorable,
                        ScorableKnown = unit.ScorableKnown,
                        AcceptedResult = unit.AcceptedResult,
                    },
                ],
                SimilarityResults =
                [
                    new()
                    {
                        QuestionId = "Q1",
                        StatusCode = ResultsStatusCodes.Success,
                        AttemptCount = 1,
                        AcceptedResult = new SimilarityQuantificationResult
                        {
                            QuestionId = "Q1",
                            Similarity = 0m,
                            Reason = "synthetic reason",
                        },
                    },
                ],
            },
        ];
        string finalPath = Path.Combine(Path.GetTempPath(), "synthetic-T21-final.xlsx");
        string partialPath = Path.Combine(Path.GetTempPath(), "synthetic-T21-final.partial.xlsx");
        return Assert.IsType<RunSummary>(Activator.CreateInstance(
            typeof(RunSummary), BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
            args:
            [
                source.Schedule, source.InputSnapshot,
                hasFinal ? QuantificationRunStatusCodes.Success : QuantificationRunStatusCodes.Cancelled,
                source.StartedAtUtc, source.EndedAtUtc, true, references, completedRows,
                hasFinal ? finalPath : null, partialPath, false,
                hasFinal ? ResultsOutputStatusCodes.Success : ResultsOutputStatusCodes.Cancelled, cleanupFailed,
            ], culture: CultureInfo.InvariantCulture));
    }

    private static void AssertCriterionTexts(ResultsOutputView view, ResultsCriterionViewModel criterion)
    {
        Assert.Equal(criterion.AiRawText, ById<TextBlock>(view, "ResultsCriterionAiRaw").Text);
        Assert.Equal(criterion.EffectiveRawText, ById<TextBlock>(view, "ResultsCriterionEffective").Text);
        Assert.Equal(criterion.NormalizedScoreText, ById<TextBlock>(view, "ResultsCriterionNormalized").Text);
        Assert.Equal(criterion.EvaluatorScoreText, ById<TextBlock>(view, "ResultsCriterionEvaluator").Text);
        Assert.Equal(criterion.QuestionScoreText, ById<TextBlock>(view, "ResultsCriterionQuestion").Text);
        Assert.Equal(criterion.OverallScoreText, ById<TextBlock>(view, "ResultsCriterionOverall").Text);
        Assert.Equal(criterion.RangeText, ById<TextBlock>(view, "ResultsCriterionRange").Text);
        Assert.Equal(criterion.StatusCode, ById<TextBlock>(view, "ResultsCriterionStatus").Text);
    }

    private static void AssertMeasuredPage(ResultsOutputView view, ResultsOutputViewModel viewModel)
    {
        ListBox rows = Required<ListBox>(view, "RowScoreList");
        ScrollViewer scroll = Assert.Single(rows.GetVisualDescendants().OfType<ScrollViewer>(), item => ReferenceEquals(item.TemplatedParent, rows));
        Control firstRow = Assert.IsAssignableFrom<Control>(rows.ContainerFromIndex(0));
        Assert.True(double.IsFinite(scroll.Viewport.Height));
        Assert.True(firstRow.Bounds.Height >= 44d);
        Assert.Equal(Math.Max(1, (int)Math.Floor(scroll.Viewport.Height / firstRow.Bounds.Height)), viewModel.PageSize);
        Assert.InRange(viewModel.VisibleRowScores.Count, 1, viewModel.PageSize);
        Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1d);
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1d);
        ScrollViewer outer = Required<ScrollViewer>(view, "ResultsOutputScrollViewer");
        Assert.True(outer.Extent.Height <= outer.Viewport.Height + 1d);
        Assert.True(outer.Extent.Width <= outer.Viewport.Width + 1d);
        Assert.Equal(ScrollBarVisibility.Disabled, outer.HorizontalScrollBarVisibility);
    }

    private static T Required<T>(Control root, string name) where T : Control => Assert.IsType<T>(root.FindControl<T>(name));

    private static T ById<T>(Control root, string id) where T : Control => Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static TextBox[] ActiveOverrideEditors(Control root) => root.GetVisualDescendants().OfType<TextBox>()
        .Where(editor => editor.IsEffectivelyVisible && (AutomationProperties.GetAutomationId(editor) ?? string.Empty).EndsWith("-Override", StringComparison.Ordinal)).ToArray();

    private static void AssertUniqueAutomationIds(Control root)
    {
        string[] ids = root.GetVisualDescendants().OfType<Control>().Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static void Execute(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        Assert.NotNull(button.Command);
        Assert.True(button.Command.CanExecute(button.CommandParameter));
        button.Command.Execute(button.CommandParameter);
        RenderUi();
    }

    private static void PressEnter(Window window)
    {
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        RenderUi();
    }

    private static void RenderUi()
    {
        // Selection/content templates and viewport-sized pages may each schedule one layout pass.
        for (int pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }

    private sealed class ResultsViewHost : IDisposable
    {
        public ResultsViewHost(ResultsOutputViewModel viewModel)
        {
            View = new ResultsOutputView(viewModel);
            // T21's content-area fixture; the 1024x720 parent-shell measurement belongs to T26.
            Window = new Window { Width = 950, Height = 450, Content = View };
            Window.Show();
            RenderUi();
        }

        public ResultsOutputView View { get; }

        public Window Window { get; }

        public void Dispose() => Window.Close();
    }
}

internal sealed class RecordingOutputBoundary : IResultsOutputBoundary
{
    public Func<string, string, ResultsOutputPathAssessment> Assess { get; init; } =
        (_, _) => ResultsOutputPathAssessment.Valid;

    public Func<ResultsOutputRequest, CancellationToken, Task<ResultsOutputResult>> Export { get; init; } =
        (request, _) => Task.FromResult(new ResultsOutputResult(
            ResultsOutputStatusCodes.Success,
            request.OutputPath));

    public int ExportCount { get; private set; }

    public ResultsOutputRequest? LastRequest { get; private set; }

    public ResultsOutputPathAssessment AssessPath(string inputPath, string outputPath) =>
        Assess(inputPath, outputPath);

    public Task<ResultsOutputResult> ExportAsync(
        ResultsOutputRequest request,
        CancellationToken cancellationToken)
    {
        ExportCount++;
        LastRequest = request;
        return Export(request, cancellationToken);
    }
}