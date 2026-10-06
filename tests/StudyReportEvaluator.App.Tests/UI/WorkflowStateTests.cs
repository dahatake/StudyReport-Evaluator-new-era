using System.Globalization;
using System.Security.Cryptography;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Settings;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

/// <summary>
/// T24: real cached shell/editor controls, temporary settings persistence, and synthetic U04 runs.
/// These are transition contracts, not native layout, real AI, checkpoint admission, or XLSX export tests.
/// </summary>
// Requirements: FR-034 (AC-035), FR-048 (AC-049)
public sealed class WorkflowStateTests
{
    private static readonly CanonicalDefinitionSerializer Canonical = new();
    private static readonly TimeSpan BoundaryWait = TimeSpan.FromSeconds(10);
    private static readonly CachedCopilotModel[] ExpectedCatalog =
        [new("model-test", 64_000, 128_000), new("auto", null, null)];

    [AvaloniaTheory]
    [InlineData(SettingsCategory.Mapping)]
    [InlineData(SettingsCategory.Evaluation)]
    public async Task Shell_round_trip_saves_the_latest_mapping_or_evaluator_edit(SettingsCategory lastEditor)
    {
        using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
        QuantificationDefinition original = fixture.Input.DefinitionDraft;
        string originalHash = Canonical.ComputeSha256(original);
        QuestionDefinition originalQuestion = Assert.Single(original.Questions);
        EvaluatorDefinition originalEvaluator = Assert.Single(originalQuestion.Evaluators);
        InputView inputView = Current<InputView>(fixture);
        InputQuestionMappingViewModel mapping = fixture.Input.SelectedQuestion!;
        Edit(Required<TextBox>(inputView, "QuestionTextEditor"), "T24 手動の設問文");
        Next(fixture);
        QuantificationDesignView designView = Current<QuantificationDesignView>(fixture);
        Edit(Required<TextBox>(designView, "BasePointsTextBox"), "62.5");
        Edit(Required<TextBox>(designView, "SelectedQuestionPointsTextBox"), "37.5");
        QuestionDesignItemViewModel question = fixture.Design.SelectedQuestion!;
        EvaluatorDesignItemViewModel evaluator = question.SelectedEvaluator!;
        CriterionDesignItemViewModel criterion = evaluator.SelectedCriterion!;
        ImportedPromptViewModel[] prompts = fixture.Design.ImportedPrompts.ToArray();

        Activate(Required<Button>(designView, "OpenEvaluatorSettingsButton"));
        SettingsView settings = Current<SettingsView>(fixture);
        Edit(ById<TextBox>(settings, $"DesignEvaluator-{evaluator.Id}-Name"), "T24 evaluator");
        Edit(ById<TextBox>(settings, $"DesignEvaluator-{evaluator.Id}-Weight"), "2.5");
        Edit(ById<TextBox>(settings, $"DesignCriterion-{criterion.Id}-Description"), "中間の観点");
        Open(fixture, SettingsCategory.Mapping);
        MappingSettingsView mappingView = Assert.IsType<MappingSettingsView>(CategoryContent(settings));
        Edit(Required<TextBox>(mappingView, "QuestionNameEditor"), "中間の設問名");
        Required<ListBox>(mappingView, "SupportingColumnList").SetCurrentValue(
            ListBox.SelectedItemProperty, mapping.SupportingColumns.Single(column => column.ColumnName == "B"));
        Render();
        CheckBox support = Required<CheckBox>(mappingView, "IncludeSupportingColumnCheckBox");
        Assert.True(support.IsEffectivelyEnabled);
        support.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        Render();
        Assert.Contains("B", fixture.Input.DefinitionDraft.Questions[0].SupportingSourceColumns);

        Open(fixture, SettingsCategory.Common);
        Edit(ById<TextBox>(settings, "DesignDefinitionName"), "T24 保存定義");
        Edit(ById<TextBox>(settings, "DesignRevision"), "t24");
        Select(ById<ComboBox>(settings, "ExecutionConcurrency"), 2);
        string outputDirectory = Path.Combine(fixture.Workbook.Directory, "明示出力先");
        Edit(ById<TextBox>(settings, "ExecutionOutputDirectory"), outputDirectory);
        Open(fixture, SettingsCategory.Special);
        Assert.IsType<SpecialEvaluationSettingsView>(CategoryContent(settings));
        Open(fixture, SettingsCategory.ImportedPrompts);
        Assert.IsType<ImportedPromptSettingsView>(CategoryContent(settings));
        Assert.Equal(prompts, fixture.Design.ImportedPrompts);

        // Save directly from either final editing surface, not after a manual SynchronizeDrafts call.
        foreach (SettingsCategory category in lastEditor == SettingsCategory.Mapping
            ? new[] { SettingsCategory.Evaluation, SettingsCategory.Mapping }
            : new[] { SettingsCategory.Mapping, SettingsCategory.Evaluation })
        {
            Open(fixture, category);
            Edit(category == SettingsCategory.Mapping
                ? Required<TextBox>(Assert.IsType<MappingSettingsView>(CategoryContent(settings)), "QuestionNameEditor")
                : ById<TextBox>(settings, $"DesignCriterion-{criterion.Id}-Description"),
                category == SettingsCategory.Mapping ? "最終の設問名" : "最新の評価説明");
        }

        QuantificationDefinition expected = original with
        {
            Name = "T24 保存定義",
            Revision = "t24",
            BasePoints = 62.5m,
            Questions = [originalQuestion with
            {
                DisplayName = "最終の設問名",
                QuestionText = "T24 手動の設問文",
                Points = 37.5m,
                SupportingSourceColumns = originalQuestion.SupportingSourceColumns.Contains("B")
                    ? originalQuestion.SupportingSourceColumns : originalQuestion.SupportingSourceColumns.Add("B"),
                Evaluators = [originalEvaluator with
                {
                    DisplayName = "T24 evaluator",
                    Weight = 2.5m,
                    Criteria = [originalEvaluator.Criteria[0] with { Description = "最新の評価説明" }],
                }],
            }],
        };
        Assert.Equal(WorkflowStep.Design, fixture.Shell.CurrentStep);
        Assert.Equal(lastEditor, fixture.Shell.Settings.SelectedCategory);
        Assert.False(File.Exists(fixture.Store.FilePath));
        AssertPassive(fixture);
        ApplicationSettings saved = await SaveAsync(fixture, expected);
        Assert.Equal(2, saved.MaxConcurrency);
        Assert.Equal(outputDirectory, saved.OutputDirectoryOverride);
        Assert.Null(saved.PreferredModelId);
        Close(fixture);
        Assert.Same(designView, Current<QuantificationDesignView>(fixture));
        Go(fixture, WorkflowStep.Input);
        Assert.Same(inputView, Current<InputView>(fixture));
        Assert.Same(mapping, fixture.Input.SelectedQuestion);
        Next(fixture);
        Next(fixture);
        Assert.Same(question, fixture.Design.SelectedQuestion);
        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.Same(criterion, evaluator.SelectedCriterion);
        AssertDefinition(expected, fixture.Input.DefinitionDraft);
        AssertDefinition(expected, fixture.Design.Draft);
        AssertPassive(fixture);

        await AuthenticateAsync(fixture);
        ExecutionRunContext context = await RunAsync(fixture);
        QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(fixture.Runner.LastRequest);
        AssertDefinition(expected, request.DraftDefinition);
        Assert.Equal(Canonical.ComputeSha256(expected), context.Summary.DefinitionSha256);
        Assert.Equal(outputDirectory, request.OutputDirectory);
        Assert.Equal(2, request.MaxConcurrency);
        Assert.Equal(originalHash, Canonical.ComputeSha256(original));
        Assert.False(Directory.Exists(outputDirectory)); // The fake run did not create output.
        AssertPassive(fixture, authenticationChecks: 1, runs: 1);
    }

    [AvaloniaFact]
    public async Task Pending_numeric_controls_survive_round_trips_then_corrected_values_reach_save_and_run()
    {
        using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
        QuantificationDefinition original = fixture.Input.DefinitionDraft;
        InputView inputView = Current<InputView>(fixture);
        TextBox row = Required<TextBox>(inputView, "FirstDataRowTextBox");
        BindingExpressionBase rowBinding = Binding(row);
        Edit(row, "-");
        Next(fixture);
        QuantificationDesignView designView = Current<QuantificationDesignView>(fixture);
        TextBox points = Required<TextBox>(designView, "BasePointsTextBox");
        BindingExpressionBase pointsBinding = Binding(points);
        Edit(points, "未確定");
        SettingsView settings = Open(fixture, SettingsCategory.Common);
        TextBox rounding = ById<TextBox>(settings, "DesignRoundingDigits");
        BindingExpressionBase roundingBinding = Binding(rounding);
        Edit(rounding, "-");
        Open(fixture, SettingsCategory.Evaluation);
        Control evaluation = CategoryContent(settings);
        string evaluatorId = original.Questions[0].Evaluators[0].Id;
        TextBox weight = ById<TextBox>(settings, $"DesignEvaluator-{evaluatorId}-Weight");
        BindingExpressionBase weightBinding = Binding(weight);
        Edit(weight, "-");
        Open(fixture, SettingsCategory.Special);
        Close(fixture);
        Go(fixture, WorkflowStep.Input);
        Assert.Same(inputView, Current<InputView>(fixture));
        AssertPending(row, "-", rowBinding);
        Next(fixture);
        Assert.Same(designView, Current<QuantificationDesignView>(fixture));
        AssertPending(points, "未確定", pointsBinding);
        Next(fixture);
        Assert.True(fixture.Execution.IsConfigured);
        Assert.Same(settings, Open(fixture, SettingsCategory.Evaluation));
        Assert.Same(evaluation, CategoryContent(settings));
        Assert.Same(weight, ById<TextBox>(settings, $"DesignEvaluator-{evaluatorId}-Weight"));
        AssertPending(weight, "-", weightBinding);
        Open(fixture, SettingsCategory.Common);
        Assert.Same(rounding, ById<TextBox>(settings, "DesignRoundingDigits"));
        AssertPending(rounding, "-", roundingBinding);
        AssertDefinition(original, fixture.Input.DefinitionDraft);
        AssertDefinition(original, fixture.Design.Draft);
        Assert.False(File.Exists(fixture.Store.FilePath));
        AssertPassive(fixture);

        // Correct through the same controls. Valid commits may legitimately publish fresh peer values.
        Close(fixture);
        Go(fixture, WorkflowStep.Input);
        Edit(row, "3");
        Next(fixture);
        Edit(points, "55.5");
        Edit(Required<TextBox>(designView, "SelectedQuestionPointsTextBox"), "44.5");
        Open(fixture, SettingsCategory.Common);
        Edit(rounding, "2");
        Open(fixture, SettingsCategory.Evaluation);
        Edit(weight, "2.5");
        QuantificationDefinition expected = original with
        {
            FirstDataRow = 3,
            BasePoints = 55.5m,
            RoundingDigits = 2,
            Questions = [original.Questions[0] with
            {
                Points = 44.5m,
                Evaluators = [original.Questions[0].Evaluators[0] with { Weight = 2.5m }],
            }],
        };
        await SaveAsync(fixture, expected);
        Assert.All(new[] { row, points, rounding, weight }, editor => Assert.False(DataValidationErrors.GetHasErrors(editor)));
        Close(fixture);
        Next(fixture);
        await AuthenticateAsync(fixture);
        ExecutionRunContext context = await RunAsync(fixture);
        AssertDefinition(expected, fixture.Runner.LastRequest!.DraftDefinition);
        Assert.Equal(Canonical.ComputeSha256(expected), context.Summary.DefinitionSha256);
        AssertPassive(fixture, authenticationChecks: 1, runs: 1);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unedited_same_input_round_trip_retains_new_or_resume_and_completed_context(bool resume)
    {
        using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
        Next(fixture);
        Next(fixture);
        Assert.False(File.Exists(fixture.Store.FilePath));
        byte[] savedBytes = await AuthenticateAsync(fixture);
        SettingsView settings = Open(fixture, SettingsCategory.Common);
        Select(ById<ComboBox>(settings, "ExecutionModel"), "auto");
        Select(ById<ComboBox>(settings, "ExecutionConcurrency"), 2);
        string outputDirectory = Path.Combine(fixture.Workbook.Directory, "chosen-output");
        Edit(ById<TextBox>(settings, "ExecutionOutputDirectory"), outputDirectory);
        Close(fixture);
        ExecutionRunContext completed = await RunAsync(fixture);
        QuantificationRunRequest request = fixture.Runner.LastRequest!;
        Go(fixture, WorkflowStep.Execution);
        ExecutionView executionView = Current<ExecutionView>(fixture);
        string partial = fixture.CreateCheckpointPartial();
        SetResume(fixture, partial, resume);
        if (resume)
        {
            await fixture.Execution.PrepareResumeAsync(TestContext.Current.CancellationToken)
                .WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
            Assert.True(fixture.Execution.ResumeReport?.CanResume);
        }
        var progress = (fixture.Execution.ProgressTotal, fixture.Execution.ProgressCompleted, fixture.Execution.ProgressStage);
        var metadata = fixture.Input.Metadata;
        var snapshot = fixture.Input.Snapshot;
        ResultsCriterionViewModel[] previousResults = fixture.Results.Results.ToArray();
        string hash = Canonical.ComputeSha256(fixture.Input.DefinitionDraft);

        Go(fixture, WorkflowStep.Input);
        Next(fixture);
        Open(fixture, SettingsCategory.Common);
        foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>()) Open(fixture, category);
        Close(fixture);
        Go(fixture, WorkflowStep.Input);
        Next(fixture);
        Next(fixture);

        Assert.Same(executionView, Current<ExecutionView>(fixture));
        Assert.Same(completed, fixture.Execution.LastRunContext);
        Assert.Equal(fixture.Workbook.Path, completed.InputPath);
        Assert.Same(metadata, fixture.Input.Metadata);
        Assert.Same(snapshot, fixture.Input.Snapshot);
        Assert.Equal(progress, (fixture.Execution.ProgressTotal, fixture.Execution.ProgressCompleted, fixture.Execution.ProgressStage));
        Assert.Equal(resume, fixture.Execution.IsResumeMode);
        WithPreparationPanel(fixture, panel =>
        {
            Assert.Equal(resume, Required<CheckBox>(panel, "ResumeModeCheckBox").IsChecked);
            Assert.Equal(partial, Required<TextBox>(panel, "ResumePartialPathTextBox").Text);
        });
        Assert.Equal(partial, fixture.Execution.ResumePartialPath);
        Assert.Equal(string.Empty, fixture.Execution.ResumeResetReason);
        Assert.Equal(outputDirectory, Required<TextBox>(executionView, "EffectiveOutputDirectoryTextBox").Text);
        Assert.Equal("auto", fixture.Execution.SelectedModelId);
        Assert.Equal(2, fixture.Execution.MaxConcurrency);
        Assert.True(fixture.Execution.CanStart);
        Assert.Equal(previousResults, fixture.Results.Results);
        Assert.Equal(hash, completed.Summary.DefinitionSha256);
        AssertDefinition(completed.Summary.Snapshot.Definition, request.DraftDefinition);
        AssertDefinition(request.DraftDefinition, fixture.Input.DefinitionDraft);
        AssertDefinition(request.DraftDefinition, fixture.Design.Draft);
        Assert.Equal(1, fixture.Loader.CallCount);
        Assert.Equal(savedBytes, await File.ReadAllBytesAsync(fixture.Store.FilePath, TestContext.Current.CancellationToken));
        Assert.Null(fixture.Shell.Settings.LastSaveTask); // Authentication created only cache + defaults.
        AssertPassive(fixture, authenticationChecks: 1, runs: 1);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_input_invalidates_resume_and_context_but_resolves_default_or_explicit_output(bool explicitOutput)
    {
        using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
        using X02TemporaryWorkbook replacement = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Replacement", 1, 5, 3, new X02Header(1, "Different answer"), new X02Header(2, "Rationale"));
        Next(fixture);
        Next(fixture);
        SettingsView settings = Open(fixture, SettingsCategory.Common);
        string chosenOutput = Path.Combine(fixture.Workbook.Directory, "persisted-output");
        if (explicitOutput) Edit(ById<TextBox>(settings, "ExecutionOutputDirectory"), chosenOutput);
        QuantificationDefinition original = fixture.Input.DefinitionDraft;
        ApplicationSettings saved = await SaveAsync(fixture, original);
        Assert.Equal(explicitOutput ? chosenOutput : null, saved.OutputDirectoryOverride);
        byte[] savedBytes = await File.ReadAllBytesAsync(fixture.Store.FilePath, TestContext.Current.CancellationToken);
        Close(fixture);
        savedBytes = await AuthenticateAsync(fixture, savedBytes);
        ExecutionRunContext completed = await RunAsync(fixture);
        QuantificationRunRequest previous = fixture.Runner.LastRequest!;
        ResultsCriterionViewModel[] results = fixture.Results.Results.ToArray();
        Go(fixture, WorkflowStep.Execution);
        SetResume(fixture, fixture.CreateExistenceOnlyPartial(), true);
        Go(fixture, WorkflowStep.Input);
        await fixture.Input.SetFilePathAsync(replacement.Path, TestContext.Current.CancellationToken)
            .WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        Assert.Equal(replacement.Path, Required<TextBox>(Current<InputView>(fixture), "FilePathTextBox").Text);
        Next(fixture);
        Next(fixture);

        string expectedOutput = explicitOutput ? chosenOutput : Path.Combine(replacement.Directory, "result");
        QuantificationDefinition next = fixture.Input.DefinitionDraft;
        Assert.NotEqual(original.Id, next.Id);
        Assert.Equal("Replacement", next.SourceSheet);
        AssertDefinition(next, fixture.Design.Draft);
        AssertDefinition(original, fixture.Shell.Settings.StoredDefinition!); // Stored does not mean applied.
        Assert.False(fixture.Execution.IsResumeMode);
        Assert.Equal(string.Empty, fixture.Execution.ResumePartialPath);
        Assert.Contains("解除", fixture.Execution.ResumeResetReason, StringComparison.Ordinal);
        Assert.Null(fixture.Execution.LastRunContext);
        Assert.Equal(0, fixture.Execution.ProgressCompleted);
        Assert.Equal(0, fixture.Execution.ProgressTotal);
        Assert.Equal(expectedOutput, Required<TextBox>(Current<ExecutionView>(fixture), "EffectiveOutputDirectoryTextBox").Text);
        Assert.Equal(explicitOutput ? chosenOutput : null, fixture.Execution.OutputDirectoryOverride);
        Assert.False(Directory.Exists(expectedOutput));
        Assert.Equal(results, fixture.Results.Results);
        AssertDefinition(original, completed.Summary.Snapshot.Definition);
        Assert.Equal(savedBytes, await File.ReadAllBytesAsync(fixture.Store.FilePath, TestContext.Current.CancellationToken));
        AssertPassive(fixture, authenticationChecks: 1, runs: 1);

        ExecutionRunContext newContext = await RunAsync(fixture);
        QuantificationRunRequest request = fixture.Runner.LastRequest!;
        Assert.NotSame(previous, request);
        Assert.Equal(replacement.Path, request.InputPath);
        Assert.Same(fixture.Input.Metadata, request.WorkbookMetadata);
        Assert.Equal(expectedOutput, request.OutputDirectory);
        Assert.Null(request.ResumePartialPath);
        AssertDefinition(next, request.DraftDefinition);
        Assert.Equal(Canonical.ComputeSha256(next), newContext.Summary.DefinitionSha256);
        AssertDefinition(original, previous.DraftDefinition);
        Assert.Equal(savedBytes, await File.ReadAllBytesAsync(fixture.Store.FilePath, TestContext.Current.CancellationToken));
        AssertPassive(fixture, authenticationChecks: 1, runs: 2);
    }

    [AvaloniaFact]
    public async Task Deferred_run_uses_captured_request_and_delivers_results_without_closing_edited_settings()
    {
        using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
        Next(fixture);
        Next(fixture);
        await AuthenticateAsync(fixture);
        QuantificationDefinition original = fixture.Input.DefinitionDraft;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(original, fixture.Input.Metadata!)
            .WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        TaskCompletionSource<RunSummary> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        string reserved = Path.Combine(fixture.Workbook.Directory, "result", "reserved.xlsx");
        string partial = Path.Combine(fixture.Workbook.Directory, "result", "reserved.partial.xlsx");
        fixture.RunHandler = (_, progress, token) =>
        {
            // Synthetic progress identities; no checkpoint or output file is written.
            progress?.Invoke(new EvaluationProgress(7, 1, 1, EvaluationProgressStatus.Running,
                DurableEvaluationStage.SavingCheckpoint, finalPath: reserved, partialPath: partial));
            return release.Task.WaitAsync(token);
        };
        Task run = fixture.Execution.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.False(run.IsCompleted);
            QuantificationRunRequest active = Assert.IsType<QuantificationRunRequest>(fixture.Runner.LastRequest);
            SettingsView settings = Open(fixture, SettingsCategory.Evaluation);
            string criterionId = original.Questions[0].Evaluators[0].Criteria[0].Id;
            Edit(ById<TextBox>(settings, $"DesignCriterion-{criterionId}-Description"), "次回の評価説明");
            Open(fixture, SettingsCategory.Common);
            Edit(ById<TextBox>(settings, "DesignDefinitionName"), "進行中に編集した次回定義");
            Select(ById<ComboBox>(settings, "ExecutionModel"), "auto");
            Select(ById<ComboBox>(settings, "ExecutionConcurrency"), 3);
            string nextOutput = Path.Combine(fixture.Workbook.Directory, "next-output");
            Edit(ById<TextBox>(settings, "ExecutionOutputDirectory"), nextOutput);
            QuantificationDefinition next = fixture.Design.Draft;
            await SaveAsync(fixture, next);
            Assert.NotEqual(Canonical.ComputeSha256(original), Canonical.ComputeSha256(next));
            Assert.Same(active, fixture.Runner.LastRequest);
            AssertDefinition(original, active.DraftDefinition);
            Assert.Equal(fixture.Workbook.Path, active.InputPath);
            Assert.Same(fixture.Input.Metadata, active.WorkbookMetadata);
            Assert.Equal("model-test", active.ModelId);
            Assert.Equal(8, active.MaxConcurrency);
            Assert.Equal(Path.Combine(fixture.Workbook.Directory, "result"), active.OutputDirectory);
            Assert.Null(active.ResumePartialPath);
            Assert.Equal((7, 1, 1), (fixture.Execution.ProgressTotal, fixture.Execution.ProgressCompleted, fixture.Execution.ProgressInFlight));
            Assert.Equal(reserved, fixture.Execution.ReservedFinalPath);
            Assert.Equal(partial, fixture.Execution.PartialPath);
            Button stop = Required<Button>(fixture.Window, "ShellCancelRunButton");
            Assert.Same(fixture.Execution.CancelCommand, stop.Command);
            Assert.True(stop.IsEffectivelyVisible && stop.IsEffectivelyEnabled);
            Assert.False(fixture.Shell.Settings.CanApplySavedDefinition);
            AssertPassive(fixture, authenticationChecks: 1, runs: 1);

            release.TrySetResult(summary);
            await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
            Render();
            Assert.Same(settings, Current<SettingsView>(fixture));
            Assert.True(fixture.Shell.IsSettingsOpen);
            Assert.Equal(WorkflowStep.Execution, fixture.Shell.CurrentStep);
            Assert.Same(summary, fixture.Execution.LastRunContext?.Summary);
            Assert.True(fixture.Results.IsLoaded);
            Assert.Equal(summary.CompletedEvaluationCount, fixture.Results.CompletedEvaluationCount);
            Assert.Equal(Canonical.ComputeSha256(original), summary.DefinitionSha256);
            AssertDefinition(next, fixture.Shell.Settings.StoredDefinition!);
            Close(fixture);
            Assert.Null(fixture.Execution.LastRunContext); // Only the next configuration is refreshed.
            Assert.True(fixture.Results.IsLoaded);
            AssertPassive(fixture, authenticationChecks: 1, runs: 1);
            fixture.RunHandler = WorkflowFixture.SyntheticRun;
            ExecutionRunContext nextContext = await RunAsync(fixture);
            QuantificationRunRequest nextRequest = fixture.Runner.LastRequest!;
            AssertDefinition(next, nextRequest.DraftDefinition);
            Assert.Equal(Canonical.ComputeSha256(next), nextContext.Summary.DefinitionSha256);
            Assert.Equal(("auto", 3, nextOutput), (nextRequest.ModelId, nextRequest.MaxConcurrency, nextRequest.OutputDirectory));
            AssertDefinition(original, active.DraftDefinition);
            AssertPassive(fixture, authenticationChecks: 1, runs: 2);
        }
        finally
        {
            release.TrySetResult(summary);
            await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task Shared_stop_cancels_captured_run_without_discarding_next_settings_or_partial_results()
    {
        using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
        Next(fixture);
        Next(fixture);
        await AuthenticateAsync(fixture);
        QuantificationDefinition original = fixture.Input.DefinitionDraft;
        RunSummary cancelled = await U04TestSupport.CreateSummaryAsync(original, fixture.Input.Metadata!, cancelAfterFirst: true)
            .WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        Assert.True(cancelled.IsPartial);
        TaskCompletionSource<RunSummary> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedToken = default;
        fixture.RunHandler = (_, progress, token) =>
        {
            capturedToken = token;
            progress?.Invoke(new EvaluationProgress(7, 1, 1, EvaluationProgressStatus.Running));
            // A stop request is not completion: allow the fake to return its partial summary afterwards.
            return release.Task.WaitAsync(TestContext.Current.CancellationToken);
        };
        Task run = fixture.Execution.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            QuantificationRunRequest active = Assert.IsType<QuantificationRunRequest>(fixture.Runner.LastRequest);
            SettingsView settings = Open(fixture, SettingsCategory.Common);
            Edit(ById<TextBox>(settings, "DesignDefinitionName"), "停止とは独立した次回draft");
            Select(ById<ComboBox>(settings, "ExecutionConcurrency"), 2);
            QuantificationDefinition next = fixture.Design.Draft;
            await SaveAsync(fixture, next);
            Button stop = Required<Button>(fixture.Window, "ShellCancelRunButton");
            Assert.Same(fixture.Execution.CancelCommand, stop.Command);
            Assert.False(capturedToken.IsCancellationRequested);
            Activate(stop);
            Assert.True(capturedToken.IsCancellationRequested);
            Assert.False(run.IsCompleted);
            Assert.True(fixture.Execution.IsRunning && fixture.Execution.IsCancelling);
            Assert.False(stop.IsEffectivelyEnabled);
            Assert.Same(active, fixture.Runner.LastRequest);
            AssertDefinition(original, active.DraftDefinition);
            Assert.Equal(8, active.MaxConcurrency);
            AssertPassive(fixture, authenticationChecks: 1, runs: 1);

            release.TrySetResult(cancelled);
            await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
            Render();
            Assert.Same(settings, Current<SettingsView>(fixture));
            Assert.Equal(WorkflowStep.Execution, fixture.Shell.CurrentStep);
            Assert.False(fixture.Execution.IsRunning);
            Assert.False(fixture.Execution.CanCancel);
            Assert.True(fixture.Results.IsPartial);
            Assert.True(fixture.Results.CancelledCount > 0);
            Assert.Same(cancelled, fixture.Execution.LastRunContext?.Summary);
            Assert.Equal(Canonical.ComputeSha256(original), cancelled.DefinitionSha256);
            AssertDefinition(original, active.DraftDefinition);
            AssertDefinition(next, fixture.Input.DefinitionDraft);
            AssertDefinition(next, fixture.Shell.Settings.StoredDefinition!);
            Assert.False(fixture.Shell.Settings.HasUnsavedChanges);
            AssertPassive(fixture, authenticationChecks: 1, runs: 1);
        }
        finally
        {
            release.TrySetResult(cancelled);
            await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_definition_apply_across_shell_transitions_is_atomic_on_success_or_failure(bool failApply)
    {
        using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
        QuantificationDefinition original = fixture.Input.DefinitionDraft;
        QuantificationDefinition stored = original with
        {
            Id = "saved-T24-definition",
            Name = "保存定義 B",
            FirstDataRow = 3,
            Questions = [original.Questions[0] with
            {
                QuestionText = "保存した設問文・見出しによる再生成は禁止",
                PrimarySourceColumn = "B",
                SupportingSourceColumns = ["A"],
                Evaluators = [original.Questions[0].Evaluators[0] with
                {
                    Type = EvaluatorType.CustomPrompt,
                    BuiltInTemplateVersion = null,
                    CustomPromptTemplate = "保存済みの観点: {評価項目}\n{回答}",
                }],
            }],
        };
        Assert.Equal(SettingsSaveStatus.Saved, (await fixture.Store.SaveAsync(
            new ApplicationSettings { Definition = stored }, TestContext.Current.CancellationToken)).Status);
        byte[] savedBytes = await File.ReadAllBytesAsync(fixture.Store.FilePath, TestContext.Current.CancellationToken);
        Task reload = fixture.Shell.Settings.LoadAsync(TestContext.Current.CancellationToken);
        await reload.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        AssertDefinition(original, fixture.Input.DefinitionDraft);
        AssertDefinition(stored, fixture.Shell.Settings.StoredDefinition!);
        AssertPassive(fixture);
        Next(fixture);
        Next(fixture);
        savedBytes = await AuthenticateAsync(fixture, savedBytes);
        ExecutionRunContext completed = await RunAsync(fixture);
        QuantificationRunRequest previous = fixture.Runner.LastRequest!;
        ResultsCriterionViewModel[] previousResults = fixture.Results.Results.ToArray();
        ImportedPromptViewModel[] prompts = fixture.Design.ImportedPrompts.ToArray();
        Go(fixture, WorkflowStep.Execution);
        string partial = fixture.CreateCheckpointPartial();
        SetResume(fixture, partial, true);
        await fixture.Execution.PrepareResumeAsync(TestContext.Current.CancellationToken)
            .WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        Assert.True(fixture.Execution.ResumeReport?.CanResume);
        QuantificationDefinition inputBefore = fixture.Input.DefinitionDraft;
        QuantificationDefinition designBefore = fixture.Design.Draft;
        var metadataBefore = fixture.Input.Metadata;
        var snapshotBefore = fixture.Input.Snapshot;
        SettingsView settings = Open(fixture, SettingsCategory.Common);
        ById<TabControl>(settings, "SettingsCommonTabs").SetCurrentValue(TabControl.SelectedIndexProperty, 1);
        Render();
        TaskCompletionSource<InputWorkbookLoadResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Loader.Pending = release;
        Activate(ById<Button>(settings, "SettingsApplySavedDefinition"));
        Task<bool> applying = Assert.IsAssignableFrom<Task<bool>>(fixture.Shell.Settings.LastApplySavedDefinitionTask);
        try
        {
            Assert.False(applying.IsCompleted);
            Assert.True(fixture.Shell.Settings.IsApplying && fixture.Input.IsBusy);
            Assert.False(Required<Button>(settings, "SettingsRequestClose").IsEffectivelyEnabled);
            // The shared shell remains navigable while the category's close action is gated.
            Go(fixture, WorkflowStep.Input);
            Go(fixture, WorkflowStep.Execution);
            Assert.Same(completed, fixture.Execution.LastRunContext);
            Assert.Same(inputBefore, fixture.Input.DefinitionDraft);
            Assert.Same(designBefore, fixture.Design.Draft);
            AssertPassive(fixture, authenticationChecks: 1, runs: 1);
            if (failApply) release.TrySetException(new IOException("Synthetic T24 metadata read failure."));
            else release.TrySetResult(fixture.Loader.Loaded!);
            Assert.Equal(!failApply, await applying.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken));
            Render();
            QuantificationDefinition expected = failApply ? original : stored;
            AssertDefinition(expected, fixture.Input.DefinitionDraft);
            AssertDefinition(expected, fixture.Design.Draft);
            Assert.Equal(failApply, fixture.Execution.IsResumeMode);
            Assert.Equal(failApply ? partial : string.Empty, fixture.Execution.ResumePartialPath);
            Assert.Equal(previousResults, fixture.Results.Results);
            Assert.Equal(prompts, fixture.Design.ImportedPrompts);
            Assert.Same(reload, fixture.Shell.Settings.LastLoadTask);
            Assert.Null(fixture.Shell.Settings.LastSaveTask);
            Assert.Equal(2, fixture.Loader.CallCount);
            if (failApply)
            {
                Assert.Same(inputBefore, fixture.Input.DefinitionDraft);
                Assert.Same(designBefore, fixture.Design.Draft);
                Assert.Same(metadataBefore, fixture.Input.Metadata);
                Assert.Same(snapshotBefore, fixture.Input.Snapshot);
                Assert.Same(completed, fixture.Execution.LastRunContext);
                Assert.Equal("SAVED_DEFINITION_LOAD_FAILED", fixture.Input.SavedDefinitionApplicationError?.Code);
            }
            else
            {
                Assert.Null(fixture.Input.SavedDefinitionApplicationError);
                Assert.Null(fixture.Execution.LastRunContext);
                Assert.Contains("解除", fixture.Execution.ResumeResetReason, StringComparison.Ordinal);
                Go(fixture, WorkflowStep.Input);
                Assert.Equal("3", Required<TextBox>(Current<InputView>(fixture), "FirstDataRowTextBox").Text);
                Assert.Equal(stored.Questions[0].QuestionText, Required<TextBox>(Current<InputView>(fixture), "QuestionTextEditor").Text);
                Go(fixture, WorkflowStep.Execution);
            }

            AssertPassive(fixture, authenticationChecks: 1, runs: 1);
            if (fixture.Execution.IsResumeMode)
            {
                await fixture.Execution.PrepareResumeAsync(TestContext.Current.CancellationToken)
                    .WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
                Assert.True(fixture.Execution.ResumeReport?.CanResume);
            }
            ExecutionRunContext next = await RunAsync(fixture);
            AssertDefinition(expected, fixture.Runner.LastRequest!.DraftDefinition);
            Assert.Equal(failApply ? partial : null, fixture.Runner.LastRequest.ResumePartialPath);
            Assert.Equal(Canonical.ComputeSha256(expected), next.Summary.DefinitionSha256);
            AssertDefinition(original, previous.DraftDefinition);
            AssertDefinition(original, completed.Summary.Snapshot.Definition);
            Assert.Equal(savedBytes, await File.ReadAllBytesAsync(fixture.Store.FilePath, TestContext.Current.CancellationToken));
            AssertPassive(fixture, authenticationChecks: 1, runs: 2);
        }
        finally
        {
            release.TrySetResult(fixture.Loader.Loaded!);
            await applying.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task Next_draft_changes_neither_previous_result_values_nor_the_last_export_request()
    {
        using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
        Next(fixture);
        Next(fixture);
        await AuthenticateAsync(fixture);
        ExecutionRunContext completed = await RunAsync(fixture);
        QuantificationRunRequest previous = fixture.Runner.LastRequest!;
        ResultsOutputView resultsView = Current<ResultsOutputView>(fixture);
        // P-01, the first-run persona, shows the detail beside the list (NFR-UX-009, AS-009).
        Assert.True(fixture.Results.IsDetailVisible);
        Assert.False(Required<Button>(resultsView, "ShowDetailButton").IsEffectivelyVisible);
        ResultsCriterionViewModel criterion = fixture.Results.SelectedCriterion!;
        string overrideId = criterion.AutomationId + "-Override";
        Edit(ById<TextBox>(resultsView, overrideId), "7");
        string exportedPath = Path.Combine(fixture.Workbook.Directory, "fake-export.xlsx");
        Edit(Required<TextBox>(resultsView, "OutputPathTextBox"), exportedPath);
        await fixture.Results.ExportAsync(TestContext.Current.CancellationToken)
            .WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        Render();
        ResultsOutputRequest exported = Assert.IsType<ResultsOutputRequest>(fixture.Output.LastRequest);
        Assert.Same(completed, exported.Context);
        Assert.Equal("7", Assert.Single(exported.Overrides).Value);
        Edit(ById<TextBox>(resultsView, overrideId), "8");
        ResultsCriterionViewModel[] resultItems = fixture.Results.Results.ToArray();
        ResultsRowScoreViewModel[] rows = fixture.Results.RowScores.ToArray();
        var values = resultItems.Select(item => (item.AutomationId, item.AiRawScore, item.OverrideText,
            item.EffectiveRaw, item.NormalizedScore, item.EvaluatorScore, item.QuestionScore, item.OverallScore)).ToArray();
        string originalHash = completed.Summary.DefinitionSha256;
        SettingsView settings = Open(fixture, SettingsCategory.Common);
        Edit(ById<TextBox>(settings, "DesignDefinitionName"), "過去の結果とは異なる次回定義");
        Open(fixture, SettingsCategory.Evaluation);
        Edit(ById<TextBox>(settings, $"DesignEvaluator-{criterion.EvaluatorId}-Maximum"), "20");
        QuantificationDefinition next = fixture.Design.Draft;
        await SaveAsync(fixture, next);
        Assert.NotEqual(originalHash, Canonical.ComputeSha256(next));
        Close(fixture);
        Go(fixture, WorkflowStep.Execution);
        Assert.Null(fixture.Execution.LastRunContext);
        Go(fixture, WorkflowStep.Results);

        Assert.Same(resultsView, Current<ResultsOutputView>(fixture));
        Assert.Equal(resultItems, fixture.Results.Results);
        Assert.Equal(rows, fixture.Results.RowScores);
        Assert.Equal(values, fixture.Results.Results.Select(item => (item.AutomationId, item.AiRawScore, item.OverrideText,
            item.EffectiveRaw, item.NormalizedScore, item.EvaluatorScore, item.QuestionScore, item.OverallScore)));
        Assert.Same(criterion, fixture.Results.SelectedCriterion);
        Assert.Equal(10m, criterion.Range.Maximum);
        Assert.Equal(20m, fixture.Design.SelectedQuestion!.SelectedEvaluator!.Maximum);
        Assert.Equal("8", ById<TextBox>(resultsView, overrideId).Text);
        Assert.Equal("前回の実行結果", ById<TextBlock>(resultsView, "ResultsRunHeading").Text);
        Assert.True(fixture.Results.HasUnsavedOverrides);
        Assert.Equal(exportedPath, fixture.Results.LastSuccessfulExportPath);
        Assert.Same(exported, fixture.Output.LastRequest);
        Assert.Equal("7", Assert.Single(exported.Overrides).Value);
        Assert.Equal(originalHash, Canonical.ComputeSha256(exported.Context.Summary.Snapshot.Definition));
        AssertDefinition(completed.Summary.Snapshot.Definition, previous.DraftDefinition);
        Assert.False(File.Exists(exportedPath)); // Receipt-only fake, not an export durability claim.
        AssertPassive(fixture, authenticationChecks: 1, runs: 1, exports: 1);
    }

    private static void AssertDefinition(QuantificationDefinition expected, QuantificationDefinition actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(Canonical.ComputeSha256(expected), Canonical.ComputeSha256(actual));
        Assert.Equal(Canonical.SerializeToUtf8Bytes(expected), Canonical.SerializeToUtf8Bytes(actual));
    }

    private static void AssertPassive(WorkflowFixture fixture, int authenticationChecks = 0, int runs = 0, int exports = 0)
    {
        Assert.Equal(authenticationChecks, fixture.Authentication.CallCount);
        Assert.Equal(runs, fixture.Runner.CallCount);
        Assert.Equal(0, fixture.Login.CallCount);
        Assert.Null(fixture.Execution.LastLoginTask);
        Assert.False(fixture.Execution.IsLoggingIn);
        Assert.Equal(exports, fixture.Output.ExportCount);
        if (exports == 0) Assert.Null(fixture.Output.LastRequest);
        Assert.Equal(fixture.WorkbookHash, SHA256.HashData(File.ReadAllBytes(fixture.Workbook.Path)));
    }

    private static async Task<ApplicationSettings> SaveAsync(WorkflowFixture fixture, QuantificationDefinition expected)
    {
        Task? previous = fixture.Shell.Settings.LastSaveTask;
        Activate(Required<Button>(Current<SettingsView>(fixture), "SettingsSaveButton"));
        Task save = Assert.IsAssignableFrom<Task>(fixture.Shell.Settings.LastSaveTask);
        Assert.NotSame(previous, save);
        await save.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        Render();
        Assert.Equal(SettingsSaveStatus.Saved, fixture.Shell.Settings.SaveStatus);
        Assert.False(fixture.Shell.Settings.HasUnsavedChanges);
        AssertDefinition(expected, fixture.Shell.Settings.StoredDefinition!);
        SettingsLoadResult loaded = await new SettingsFileStore(fixture.Store.FilePath)
            .LoadAsync(TestContext.Current.CancellationToken).WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        Assert.Equal(SettingsLoadStatus.Loaded, loaded.Status);
        ApplicationSettings saved = Assert.IsType<ApplicationSettings>(loaded.Settings);
        AssertDefinition(expected, Assert.IsType<QuantificationDefinition>(saved.Definition));
        return saved;
    }

    private static async Task<byte[]> AuthenticateAsync(WorkflowFixture fixture, byte[]? savedBytes = null)
    {
        byte[]? before = File.Exists(fixture.Store.FilePath)
            ? await File.ReadAllBytesAsync(fixture.Store.FilePath, TestContext.Current.CancellationToken) : null;
        if (savedBytes is not null) Assert.Equal(savedBytes, before);
        int authenticationChecks = fixture.Authentication.CallCount;
        int runs = fixture.Runner.CallCount;
        WithPreparationPanel(fixture, panel => Assert.Same(fixture.Execution.CheckAuthenticationCommand,
            Required<Button>(panel, "CheckAuthenticationButton").Command));
        await fixture.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken)
            .WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        Render();
        Assert.Equal(ExecutionAuthenticationState.Available, fixture.Execution.AuthenticationState);
        AssertPassive(fixture, authenticationChecks: authenticationChecks + 1, runs: runs);
        return ModelCatalogPersistenceAssert.OnlyCatalogChanged(before,
            await File.ReadAllBytesAsync(fixture.Store.FilePath, TestContext.Current.CancellationToken), ExpectedCatalog);
    }

    private static async Task<ExecutionRunContext> RunAsync(WorkflowFixture fixture)
    {
        Button start = Required<Button>(Current<ExecutionView>(fixture), "StartRunButton");
        Assert.Same(fixture.Execution.StartCommand, start.Command);
        Assert.True(start.IsEffectivelyEnabled, fixture.Execution.ValidationSummary);
        // Await the same command entry point with the test lifetime, instead of fire-and-forget dispatch.
        await fixture.Execution.StartAsync(TestContext.Current.CancellationToken)
            .WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        Render();
        Assert.Equal(WorkflowStep.Results, fixture.Shell.CurrentStep);
        Current<ResultsOutputView>(fixture);
        return Assert.IsType<ExecutionRunContext>(fixture.Execution.LastRunContext);
    }

    private static SettingsView Open(WorkflowFixture fixture, SettingsCategory category)
    {
        if (!fixture.Shell.IsSettingsOpen) Activate(Required<Button>(fixture.Window, "SettingsOpenButton"));
        SettingsView settings = Current<SettingsView>(fixture);
        if (fixture.Shell.Settings.SelectedCategory != category)
            Activate(ById<Button>(settings, "SettingsCategory" + category));
        Assert.Equal(category, fixture.Shell.Settings.SelectedCategory);
        return settings;
    }

    private static void Close(WorkflowFixture fixture) =>
        Activate(Required<Button>(Current<SettingsView>(fixture), "SettingsRequestClose"));

    private static void Next(WorkflowFixture fixture) => Activate(Required<Button>(fixture.Window, "NextStepButton"));

    private static void Go(WorkflowFixture fixture, WorkflowStep step)
    {
        Activate(Required<Button>(fixture.Window, step + "StepButton"));
        Assert.Equal(step, fixture.Shell.CurrentStep);
        Assert.False(fixture.Shell.IsSettingsOpen);
    }

    private static void SetResume(WorkflowFixture fixture, string path, bool resume)
    {
        WithPreparationPanel(fixture, view =>
        {
            CheckBox mode = Required<CheckBox>(view, "ResumeModeCheckBox");
            mode.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Render();
            Edit(Required<TextBox>(view, "ResumePartialPathTextBox"), path);
            mode.SetCurrentValue(ToggleButton.IsCheckedProperty, resume);
            Render();
        });
    }

    // The preparation parts live in the Input step; visit it, act, then return to the original step.
    private static void WithPreparationPanel(WorkflowFixture fixture, Action<ExecutionPreparationPanel> action)
    {
        WorkflowStep step = fixture.Shell.CurrentStep;
        if (step != WorkflowStep.Input) Go(fixture, WorkflowStep.Input);
        InputView input = Current<InputView>(fixture);
        input.SetPreparationOpen(true);
        Render();
        try { action(input.PreparationPanel); }
        finally
        {
            input.SetPreparationOpen(false);
            Render();
            if (step != WorkflowStep.Input) Go(fixture, step);
        }
    }

    private static T Current<T>(WorkflowFixture fixture) where T : Control
    {
        T view = Assert.IsType<T>(Required<ContentControl>(fixture.Window, "CurrentStepContent").Content);
        Assert.Same(fixture.Shell.CurrentEditorViewModel, view.DataContext);
        return view;
    }

    private static Control CategoryContent(SettingsView view) =>
        Assert.IsAssignableFrom<Control>(Required<ContentControl>(view, "CurrentSettingsContent").Content);

    private static T Required<T>(Control root, string name) where T : Control => Assert.IsType<T>(root.FindControl<T>(name));

    private static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static BindingExpressionBase Binding(TextBox editor) => Assert.IsAssignableFrom<BindingExpressionBase>(
        BindingOperations.GetBindingExpressionBase(editor, TextBox.TextProperty));

    private static void AssertPending(TextBox editor, string text, BindingExpressionBase binding)
    {
        Assert.Equal(text, editor.Text);
        Assert.Same(binding, Binding(editor));
        Assert.True(DataValidationErrors.GetHasErrors(editor));
    }

    private static void Edit(TextBox editor, string text)
    {
        Assert.True(editor.IsEffectivelyVisible && editor.IsEffectivelyEnabled && !editor.IsReadOnly);
        Binding(editor);
        editor.SetCurrentValue(TextBox.TextProperty, text);
        Render();
    }

    private static void Select(ComboBox editor, object value)
    {
        Assert.True(editor.IsEffectivelyVisible && editor.IsEffectivelyEnabled);
        editor.SetCurrentValue(ComboBox.SelectedItemProperty, value);
        Render();
        Assert.Equal(value, editor.SelectedItem);
    }

    private static void Activate(Button button)
    {
        Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled, button.Name);
        Assert.True(button.Focus(NavigationMethod.Tab));
        TopLevel top = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(button));
        top.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        top.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Render();
    }

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class WorkflowFixture : IDisposable
    {
        private WorkflowFixture(X02TemporaryWorkbook workbook, InputViewModel input, GatedInputLoader loader)
        {
            Workbook = workbook;
            WorkbookHash = SHA256.HashData(File.ReadAllBytes(workbook.Path));
            Input = input;
            Loader = loader;
            Design = new QuantificationDesignViewModel(input.DefinitionDraft, input.AvailableColumnNames,
                Enumerable.Range(1, 2).Select(index => new ImportedPrompt
                {
                    Path = Path.Combine(workbook.Directory, $"unapplied-{index}.txt"),
                    DisplayName = $"unapplied-{index}.txt",
                    Content = $"未適用 {index.ToString(CultureInfo.InvariantCulture)}: {{回答}} {{評価項目}}",
                })); // In-memory synthetic launch inputs, never read through PromptFileLoader.
            Runner = new RecordingRunBoundary((request, progress, token) => RunHandler(request, progress, token));
            Execution = new ExecutionViewModel(Authentication, Runner, new BundledCopilotLoginService(
                Login, _ => throw new InvalidOperationException("T24 must not create a login process.")),
                ResumeInspection);
            Results = new ResultsOutputViewModel(Output);
            Store = new SettingsFileStore(Path.Combine(workbook.Directory, "preferences", "setting.txt"));
            Shell = new MainWindowViewModel(new WorkflowNavigator(), Input, Design, Execution, Results, Store);
            Window = new MainWindow(Shell);
        }

        public X02TemporaryWorkbook Workbook { get; }
        public byte[] WorkbookHash { get; }
        public InputViewModel Input { get; }
        public GatedInputLoader Loader { get; }
        public QuantificationDesignViewModel Design { get; }
        public RecordingAuthenticationBoundary Authentication { get; } = new(new ExecutionAuthenticationSnapshot(
            ExecutionAuthenticationState.Available,
            [U04TestSupport.Model("model-test"), U04TestSupport.Model("auto")], U04TestSupport.RuntimeIdentity()));
        public AdmittingResumeInspectionBoundary ResumeInspection { get; } = new();
        public DeniedLoginResolver Login { get; } = new();
        public RecordingRunBoundary Runner { get; }
        public RecordingOutputBoundary Output { get; } = new();
        public ExecutionViewModel Execution { get; }
        public ResultsOutputViewModel Results { get; }
        public SettingsFileStore Store { get; }
        public MainWindowViewModel Shell { get; }
        public MainWindow Window { get; }
        public Func<QuantificationRunRequest, Action<EvaluationProgress>?, CancellationToken, Task<RunSummary>> RunHandler { get; set; } = SyntheticRun;

        public static Task<RunSummary> SyntheticRun(QuantificationRunRequest request, Action<EvaluationProgress>? _, CancellationToken token) =>
            U04TestSupport.CreateSummaryAsync(request.DraftDefinition, request.WorkbookMetadata).WaitAsync(BoundaryWait, token);

        public static async Task<WorkflowFixture> CreateAsync()
        {
            X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
                "Original", 1, 4, 3, new X02Header(1, "Answer"), new X02Header(2, "Rationale"), new X02Header(3, "Context"));
            WorkflowFixture? fixture = null;
            try
            {
                GatedInputLoader loader = new();
                InputViewModel input = new(loader);
                await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken)
                    .WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
                Assert.True(input.CanContinue, input.ValidationSummary);
                fixture = new WorkflowFixture(workbook, input, loader);
                await fixture.Shell.Settings.InitializeAsync(TestContext.Current.CancellationToken)
                    .WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
                Assert.Equal(SettingsLoadStatus.Missing, fixture.Shell.Settings.LoadStatus);
                Assert.Null(fixture.Shell.Settings.LastSaveTask);
                Assert.Null(fixture.Shell.Settings.LastApplySavedDefinitionTask);
                Assert.False(File.Exists(fixture.Store.FilePath));
                fixture.Window.Show();
                Render();
                return fixture;
            }
            catch
            {
                if (fixture is null) workbook.Dispose();
                else fixture.Dispose();
                throw;
            }
        }

        public string CreateExistenceOnlyPartial()
        {
            // Only Execution's file-existence gate is exercised. The fake never admits/resumes a real checkpoint.
            string path = Path.Combine(Workbook.Directory, "existence-only.partial.xlsx");
            File.Copy(Workbook.Path, path);
            return path;
        }

        public string CreateCheckpointPartial()
        {
            QuantificationSnapshot snapshot = QuantificationSnapshot.Create(Input.DefinitionDraft);
            CopilotRuntimeIdentity runtime = U04TestSupport.RuntimeIdentity();
            string path = Path.Combine(Workbook.Directory, Guid.NewGuid().ToString("N") + ".partial.xlsx");
            CheckpointEnvelope envelope = new()
            {
                InputPath = Workbook.Path,
                Input = Input.Snapshot!,
                DefinitionCanonicalJson = snapshot.CanonicalJson,
                DefinitionSha256 = snapshot.Sha256,
                NormalModelId = Execution.SelectedModelId ?? "model-test",
                ReferenceModelId = Execution.SelectedModelId ?? "model-test",
                Runtime = new CheckpointRuntimeIdentity
                {
                    ApplicationIdentity = QuantificationRunBoundary.ApplicationIdentity(),
                    CliVersion = runtime.CliVersion,
                    CliSha256 = runtime.CliSha256,
                    SdkInformationalVersion = runtime.SdkInformationalVersion,
                },
                FinalPath = Path.Combine(Workbook.Directory, "eval.xlsx"),
                PartialPath = path,
                StartedAtUtc = DateTimeOffset.UnixEpoch,
                SavedAtUtc = DateTimeOffset.UnixEpoch,
            };
            File.Copy(Workbook.Path, path);
            ResumeInspection.Admit(envelope);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Window.Close();
                Shell.Dispose();
            }
            finally
            {
                Workbook.Dispose();
            }
        }
    }

    private sealed class AdmittingResumeInspectionBoundary : IResumeInspectionBoundary
    {
        private CheckpointEnvelope? checkpoint;

        public void Admit(CheckpointEnvelope envelope) => checkpoint = envelope;

        public Task<CheckpointLoadResult> LoadAsync(string partialPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return checkpoint is { } envelope && string.Equals(envelope.PartialPath, partialPath, StringComparison.Ordinal)
                ? Task.FromResult(CheckpointLoadResult.Succeeded(envelope))
                : Task.FromResult(CheckpointLoadResult.Failed(CheckpointStatusCodes.Invalid));
        }

        public Task<InputSnapshot> CaptureInputAsync(string inputPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return checkpoint is { } envelope
                && string.Equals(envelope.InputPath, inputPath, StringComparison.OrdinalIgnoreCase)
                ? Task.FromResult(envelope.Input)
                : Task.FromResult(new InputSnapshot(new string('0', 64), 0, DateTimeOffset.UnixEpoch));
        }
    }

    private sealed class GatedInputLoader : IInputWorkbookLoader
    {
        public int CallCount { get; private set; }
        public InputWorkbookLoadResult? Loaded { get; private set; }
        public TaskCompletionSource<InputWorkbookLoadResult>? Pending { get; set; }

        public async Task<InputWorkbookLoadResult> LoadAsync(string filePath, uint headerRow, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (Pending is { } pending) return await pending.Task.WaitAsync(cancellationToken);
            Loaded = await new InputWorkbookLoader().LoadAsync(filePath, headerRow, cancellationToken);
            return Loaded;
        }
    }

    private sealed class DeniedLoginResolver : ICopilotCliPathResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult<string?>(null);
        }
    }
}
