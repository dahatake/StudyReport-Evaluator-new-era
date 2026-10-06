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
using StudyReportEvaluator.App.Composition;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Settings;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(StudyReportEvaluator.App.Tests.UI.U02TestAppBuilder))]

namespace StudyReportEvaluator.App.Tests.UI;

public sealed class U02TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });
}

// Requirements: FR-034 (AC-035), NFR-OPS-001 (AC-074)
public sealed class MainWindowTests
{
    [Fact]
    public void Runtime_XAML_loader_has_a_public_parameterless_constructor()
    {
        Assert.NotNull(typeof(MainWindow).GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void Navigator_has_exactly_four_closed_ordered_steps_and_sequential_forward_navigation()
    {
        WorkflowNavigator navigator = new();

        Assert.Equal(4, WorkflowNavigator.StepCount);
        Assert.Equal(
            [WorkflowStep.Input, WorkflowStep.Design, WorkflowStep.Execution, WorkflowStep.Results],
            Enum.GetValues<WorkflowStep>());
        Assert.Equal(
            [WorkflowStep.Input, WorkflowStep.Design, WorkflowStep.Execution, WorkflowStep.Results],
            navigator.Steps.Select(step => step.Step));
        Assert.Equal([1, 2, 3, 4], navigator.Steps.Select(step => step.Number));
        Assert.Equal(WorkflowStep.Input, navigator.CurrentStep);
        Assert.False(navigator.CanNavigateTo(WorkflowStep.Execution));
        Assert.False(navigator.NavigateTo(WorkflowStep.Results));

        Assert.True(navigator.MoveNext());
        Assert.Equal(WorkflowStep.Design, navigator.CurrentStep);
    Assert.Equal(WorkflowStepState.Visited, navigator.GetState(WorkflowStep.Input));
        Assert.Equal(WorkflowStepState.Current, navigator.GetState(WorkflowStep.Design));
        Assert.Equal(WorkflowStepState.Upcoming, navigator.GetState(WorkflowStep.Execution));
        Assert.True(navigator.MoveNext());
        Assert.True(navigator.MoveNext());
        Assert.Equal(WorkflowStep.Results, navigator.CurrentStep);
        Assert.False(navigator.MoveNext());
        Assert.True(navigator.MovePrevious());
        Assert.Equal(WorkflowStep.Execution, navigator.CurrentStep);
        Assert.True(navigator.NavigateTo(WorkflowStep.Input));
        Assert.Equal(WorkflowStepState.Visited, navigator.GetState(WorkflowStep.Results));
        Assert.True(navigator.CanNavigateTo(WorkflowStep.Results));
        Assert.Throws<ArgumentOutOfRangeException>(() => navigator.NavigateTo((WorkflowStep)99));
    }

    [Fact]
    public void Main_window_view_model_opens_shared_settings_without_changing_the_current_workflow_step()
    {
        WorkflowNavigator navigator = new();
        InputViewModel input = new();
        QuantificationDesignViewModel design = new();
        ExecutionViewModel execution = new(
            new RecordingAuthenticationBoundary(
                new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired)),
            new RecordingRunBoundary((_, _, _) => throw new InvalidOperationException("must not run")));
        ResultsOutputViewModel results = new(new RecordingOutputBoundary());
        MainWindowViewModel viewModel = new(navigator, input, design, execution, results);

        try
        {
            Assert.Same(input, viewModel.Settings.Input);
            Assert.Same(design, viewModel.Settings.Design);
            Assert.Same(execution, viewModel.Settings.Execution);
            Assert.False(viewModel.IsSettingsOpen);
            Assert.Same(viewModel.InputViewModel, viewModel.CurrentEditorViewModel);
            Assert.Equal(ExecutionAuthenticationState.NotChecked, execution.AuthenticationState);

            viewModel.OpenSettings(SettingsCategory.Evaluation);

            Assert.True(viewModel.IsSettingsOpen);
            Assert.Equal(WorkflowStep.Input, viewModel.CurrentStep);
            Assert.Equal(SettingsCategory.Evaluation, viewModel.Settings.SelectedCategory);
            Assert.Same(viewModel.Settings, viewModel.CurrentEditorViewModel);
            Assert.Same(design, viewModel.DesignViewModel);

            viewModel.CloseSettings();

            Assert.False(viewModel.IsSettingsOpen);
            Assert.Same(viewModel.InputViewModel, viewModel.CurrentEditorViewModel);
            Assert.Same(design, viewModel.DesignViewModel);
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    [Fact]
    public void Workflow_navigation_while_settings_are_open_closes_settings_and_preserves_navigation_target()
    {
        MainWindowViewModel viewModel = new(
            new WorkflowNavigator(),
            new InputViewModel(),
            new QuantificationDesignViewModel());

        try
        {
            viewModel.OpenSettings(SettingsCategory.Common);

            Assert.True(viewModel.IsSettingsOpen);
            Assert.True(viewModel.NextCommand.CanExecute(null));

            viewModel.NextCommand.Execute(null);

            Assert.False(viewModel.IsSettingsOpen);
            Assert.Equal(WorkflowStep.Design, viewModel.CurrentStep);
            Assert.Equal(WorkflowStepState.Visited, viewModel.Navigator.GetState(WorkflowStep.Input));
        }
        finally
        {
            viewModel.Dispose();
        }
    }

    [Fact]
    public async Task Initial_common_settings_open_preserves_loaded_definition_and_mapping_when_renamed()
    {
        using X02TemporaryWorkbook workbook = CreateShellWorkbook();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        using ShellHarness harness = new(input);
        QuantificationDefinition loaded = input.DefinitionDraft;
        InputQuestionMappingViewModel[] mappings = input.Questions.ToArray();
        var metadata = input.Metadata;
        var snapshot = input.Snapshot;
        Assert.NotEqual(loaded.Id, harness.Design.Draft.Id);
        Assert.Equal(SettingsCategory.Common, harness.ViewModel.Settings.SelectedCategory);

        harness.ViewModel.OpenSettingsCommand.Execute(null);

        Assert.True(harness.ViewModel.IsSettingsOpen);
        Assert.Equal(WorkflowStep.Input, harness.ViewModel.CurrentStep);
        AssertDefinition(loaded, harness.Design.Draft);
        harness.ViewModel.Settings.Design.DefinitionName = "初回の共通設定で変更した名前";
        harness.ViewModel.Settings.RequestCloseCommand.Execute(null);

        QuantificationDefinition expected = loaded with { Name = "初回の共通設定で変更した名前" };
        AssertDefinition(expected, input.DefinitionDraft);
        AssertDefinition(expected, harness.Design.Draft);
        Assert.Equal(mappings, input.Questions);
        Assert.Same(metadata, input.Metadata);
        Assert.Same(snapshot, input.Snapshot);
        Assert.False(harness.ViewModel.IsSettingsOpen);
        Assert.Same(input, harness.ViewModel.CurrentEditorViewModel);
        AssertNoAutomaticActivity(harness);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initial_input_common_settings_preview_does_not_configure_execution(bool loadBeforeShell)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original", 1, 4, 2, new X02Header(1, "Answer"), new X02Header(2, "Rationale"));
        InputViewModel input = new();
        if (loadBeforeShell)
        {
            await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        }

        using ShellHarness harness = new(input);
        ExecutionViewModel execution = harness.Execution;
        ExecutionTechnicalError[] errors = execution.TechnicalErrors.ToArray();
        List<string?> notifications = [];
        execution.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        if (!loadBeforeShell)
        {
            await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        }

        MainWindowViewModel shell = harness.ViewModel;
        string expectedOutput = Path.Combine(workbook.Directory, "result");
        string? outputAtOpen = null;
        shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.IsSettingsOpen) && shell.IsSettingsOpen)
            {
                outputAtOpen = execution.OutputDirectory;
            }
        };
        MainWindow window = new(shell);
        try
        {
            window.Show();
            shell.OpenSettingsCommand.Execute(SettingsCategory.Common);
            Render();

            SettingsView settings = Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());
            Assert.Equal(expectedOutput, outputAtOpen);
            Assert.Equal(expectedOutput, ByAutomationId<TextBox>(settings, "SettingsEffectiveOutputDirectory").Text);
            Assert.Equal(expectedOutput, execution.OutputDirectory);
            Assert.Null(execution.OutputDirectoryOverride);
            Assert.Equal(7, execution.PlannedEvaluationCount);
            Assert.Equal(21, execution.WorstCaseAttemptCount);
            Assert.Contains("7 evaluation units", execution.PlanSummary, StringComparison.Ordinal);
            Assert.Equal(WorkflowStep.Input, shell.CurrentStep);
            AssertDefinition(input.DefinitionDraft, harness.Design.Draft);
            Assert.False(execution.IsConfigured);
            Assert.False(execution.CanStart);
            Assert.False(execution.HasCurrentRun);
            Assert.Null(execution.LastRunContext);
            Assert.Equal(errors, execution.TechnicalErrors.ToArray());
            Assert.DoesNotContain(nameof(ExecutionViewModel.IsConfigured), notifications);
            Assert.DoesNotContain(nameof(ExecutionViewModel.HasTechnicalErrors), notifications);
            Assert.False(Directory.Exists(expectedOutput));
            AssertNoAutomaticActivity(harness);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Common_settings_after_input_replacement_preserves_configuration_and_override(bool explicitOutput)
    {
        using X02TemporaryWorkbook workbookA = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original", 1, 3, 2, new X02Header(1, "Answer"), new X02Header(2, "Rationale"));
        using X02TemporaryWorkbook workbookB = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original", 1, 4, 2, new X02Header(1, "Answer"), new X02Header(2, "Rationale"));
        using ShellHarness harness = new();
        await harness.Input.SetFilePathAsync(workbookA.Path, TestContext.Current.CancellationToken);
        MainWindowViewModel shell = harness.ViewModel;
        shell.NextCommand.Execute(null);
        shell.NextCommand.Execute(null);
        ExecutionViewModel execution = harness.Execution;
        Assert.True(execution.IsConfigured);
        Assert.Equal(5, execution.PlannedEvaluationCount);
        string? explicitDirectory = explicitOutput ? Path.Combine(workbookA.Directory, "chosen-output") : null;
        execution.OutputDirectoryOverride = explicitDirectory;
        string resumeA = Path.Combine(workbookA.Directory, "retained.partial.xlsx");
        execution.ResumePartialPath = resumeA;
        execution.IsResumeMode = true;
        ExecutionTechnicalError[] errors = execution.TechnicalErrors.ToArray();
        List<string?> notifications = [];
        execution.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        string expectedOutput = explicitDirectory ?? Path.Combine(workbookB.Directory, "result");
        string? outputAtOpen = null;
        shell.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.IsSettingsOpen) && shell.IsSettingsOpen)
            {
                outputAtOpen = execution.OutputDirectory;
            }
        };
        MainWindow window = new(shell);
        try
        {
            window.Show();
            shell.NavigateCommand.Execute(WorkflowStep.Input);
            await harness.Input.SetFilePathAsync(workbookB.Path, TestContext.Current.CancellationToken);
            Assert.Equal(expectedOutput, execution.OutputDirectory);
            Assert.Equal(7, execution.PlannedEvaluationCount);
            shell.OpenSettingsCommand.Execute(SettingsCategory.Common);
            Render();

            SettingsView settings = Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());
            Assert.Equal(expectedOutput, outputAtOpen);
            Assert.Equal(expectedOutput, ByAutomationId<TextBox>(settings, "SettingsEffectiveOutputDirectory").Text);
            Assert.Equal(explicitDirectory, execution.OutputDirectoryOverride);
            Assert.Equal(21, execution.WorstCaseAttemptCount);
            Assert.Equal(WorkflowStep.Input, shell.CurrentStep);
            Assert.True(execution.IsConfigured); // Still A: preview does not Configure or ClearConfiguration.
            Assert.True(execution.IsResumeMode);
            Assert.Equal(resumeA, execution.ResumePartialPath);
            Assert.Empty(execution.ResumeResetReason);
            Assert.False(execution.CanStart);
            Assert.False(execution.HasCurrentRun);
            Assert.Equal(errors, execution.TechnicalErrors.ToArray());
            Assert.DoesNotContain(nameof(ExecutionViewModel.IsConfigured), notifications);
            Assert.DoesNotContain(nameof(ExecutionViewModel.HasTechnicalErrors), notifications);
            AssertNoAutomaticActivity(harness);

            shell.NavigateCommand.Execute(WorkflowStep.Execution);
            Assert.True(execution.IsConfigured);
            Assert.False(execution.IsResumeMode);
            Assert.Empty(execution.ResumePartialPath);
            Assert.NotEmpty(execution.ResumeResetReason);
            Assert.Equal(expectedOutput, execution.OutputDirectory);
            Assert.Equal(explicitDirectory, execution.OutputDirectoryOverride);
            Assert.Equal(7, execution.PlannedEvaluationCount);
            Assert.False(Directory.Exists(Path.Combine(workbookA.Directory, "result")));
            Assert.False(Directory.Exists(Path.Combine(workbookB.Directory, "result")));
            Assert.False(Directory.Exists(expectedOutput));
            AssertNoAutomaticActivity(harness);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Common_settings_after_input_unload_clears_only_next_draft_preview(bool explicitOutput)
    {
        using X02TemporaryWorkbook workbook = CreateShellWorkbook();
        using ShellHarness harness = new();
        await harness.Input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        MainWindowViewModel shell = harness.ViewModel;
        shell.NextCommand.Execute(null);
        shell.NextCommand.Execute(null);
        ExecutionViewModel execution = harness.Execution;
        string? explicitDirectory = explicitOutput ? Path.Combine(workbook.Directory, "chosen-output") : null;
        execution.OutputDirectoryOverride = explicitDirectory;
        string resumeA = Path.Combine(workbook.Directory, "retained.partial.xlsx");
        execution.ResumePartialPath = resumeA;
        execution.IsResumeMode = true;
        ExecutionTechnicalError[] errors = execution.TechnicalErrors.ToArray();
        List<string?> notifications = [];
        execution.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        MainWindow window = new(shell);
        try
        {
            window.Show();
            shell.NavigateCommand.Execute(WorkflowStep.Input);
            shell.OpenSettings(SettingsCategory.Common);
            Render();
            SettingsView settings = Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());
            TextBox output = ByAutomationId<TextBox>(settings, "SettingsEffectiveOutputDirectory");
            Assert.Equal(explicitDirectory ?? Path.Combine(workbook.Directory, "result"), output.Text);

            harness.Input.SetFilePath(string.Empty);
            // A later notification from the retained Design cannot revive the unloaded input.
            harness.Design.DefinitionName = "入力解除後に残る設計";
            Render();

            Assert.False(harness.Input.HasLoadedWorkbook);
            Assert.Equal(explicitDirectory ?? string.Empty, output.Text);
            Assert.Equal(explicitDirectory ?? string.Empty, execution.OutputDirectory);
            Assert.Equal(explicitDirectory, execution.OutputDirectoryOverride);
            Assert.Equal(0, execution.PlannedEvaluationCount);
            Assert.Equal(0, execution.WorstCaseAttemptCount);
            Assert.Equal("入力と定量化設計を完了してください。", execution.PlanSummary);
            Assert.True(execution.IsConfigured);
            Assert.True(execution.IsResumeMode);
            Assert.Equal(resumeA, execution.ResumePartialPath);
            Assert.False(execution.CanStart);
            Assert.Equal(errors, execution.TechnicalErrors.ToArray());
            Assert.DoesNotContain(nameof(ExecutionViewModel.IsConfigured), notifications);
            Assert.DoesNotContain(nameof(ExecutionViewModel.HasTechnicalErrors), notifications);
            AssertNoAutomaticActivity(harness);

            shell.NavigateCommand.Execute(WorkflowStep.Execution);
            Assert.False(execution.IsConfigured);
            Assert.False(execution.IsResumeMode);
            Assert.Empty(execution.ResumePartialPath);
            Assert.Equal(explicitDirectory ?? string.Empty, execution.OutputDirectory);
            Assert.Equal(0, execution.PlannedEvaluationCount);
            Assert.False(Directory.Exists(Path.Combine(workbook.Directory, "result")));
            Assert.False(explicitDirectory is not null && Directory.Exists(explicitDirectory));
            AssertNoAutomaticActivity(harness);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task Next_draft_preview_tracks_latest_editor_without_synchronizing_peers()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original", 1, 4, 2, new X02Header(1, "Answer"), new X02Header(2, "Rationale"));
        using ShellHarness harness = new();
        await harness.Input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        MainWindowViewModel shell = harness.ViewModel;
        ExecutionViewModel execution = harness.Execution;
        ExecutionTechnicalError[] errors = execution.TechnicalErrors.ToArray();
        List<string?> notifications = [];
        execution.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        Assert.Equal(7, execution.PlannedEvaluationCount);

        harness.Input.FirstDataRow = 3;
        Assert.Equal(5, execution.PlannedEvaluationCount);
        Assert.Equal(2, harness.Design.Draft.FirstDataRow);
        harness.Design.Questions[0].Evaluators[0].Enabled = false;
        Assert.Equal(4, execution.PlannedEvaluationCount);
        Assert.Equal(12, execution.WorstCaseAttemptCount);
        Assert.True(harness.Input.DefinitionDraft.Questions[0].Evaluators[0].Enabled);
        Assert.Equal(3, harness.Input.DefinitionDraft.FirstDataRow);

        shell.OpenSettings(SettingsCategory.Common);
        AssertDefinition(harness.Design.Draft, harness.Input.DefinitionDraft);
        Assert.Equal(4, execution.PlannedEvaluationCount);
        shell.CloseSettings();
        shell.NextCommand.Execute(null);
        Assert.Equal(WorkflowStep.Design, shell.CurrentStep);
        harness.Design.Questions[0].Evaluators[0].Enabled = true;
        Assert.Equal(7, execution.PlannedEvaluationCount);
        Assert.False(harness.Input.DefinitionDraft.Questions[0].Evaluators[0].Enabled);

        shell.OpenSettings(SettingsCategory.Common);
        harness.Input.Questions[0].Enabled = false;
        Assert.Equal(0, execution.PlannedEvaluationCount);
        Assert.True(harness.Design.Questions[0].Enabled);
        shell.Settings.SelectCategoryCommand.Execute(SettingsCategory.Mapping);
        AssertDefinition(harness.Input.DefinitionDraft, harness.Design.Draft);
        Assert.Equal(0, execution.WorstCaseAttemptCount);
        Assert.Contains("0 evaluation units", execution.PlanSummary, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(workbook.Directory, "result"), execution.OutputDirectory);
        Assert.False(execution.IsConfigured);
        Assert.Equal(errors, execution.TechnicalErrors.ToArray());
        Assert.DoesNotContain(nameof(ExecutionViewModel.IsConfigured), notifications);
        Assert.DoesNotContain(nameof(ExecutionViewModel.HasTechnicalErrors), notifications);
        Assert.False(Directory.Exists(execution.OutputDirectory));
        AssertNoAutomaticActivity(harness);
    }

    [Fact]
    public async Task Settings_initialization_preserves_unsynchronized_drafts_and_saved_output_override()
    {
        using X02TemporaryWorkbook workbook = CreateShellWorkbook();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition loadedInput = input.DefinitionDraft;
        QuantificationDefinition savedDefinition = loadedInput with { Name = "まだ適用しない保存定義" };
        string explicitDirectory = Path.Combine(workbook.Directory, "saved-output");
        SettingsFileStore store = new(Path.Combine(workbook.Directory, "setting.txt"));
        Assert.Equal(SettingsSaveStatus.Saved, (await store.SaveAsync(new ApplicationSettings
        {
            OutputDirectoryOverride = explicitDirectory,
            Definition = savedDefinition,
        }, TestContext.Current.CancellationToken)).Status);
        byte[] savedBytes = File.ReadAllBytes(store.FilePath);
        using ShellHarness harness = new(input, store);
        QuantificationDefinition initialDesign = harness.Design.Draft;
        Assert.NotEqual(loadedInput.Id, initialDesign.Id);

        await harness.ViewModel.Settings.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Same(loadedInput, input.DefinitionDraft);
        Assert.Same(initialDesign, harness.Design.Draft);
        AssertDefinition(savedDefinition, Assert.IsType<QuantificationDefinition>(harness.ViewModel.Settings.StoredDefinition));
        Assert.Equal(explicitDirectory, harness.Execution.OutputDirectoryOverride);
        Assert.False(harness.Execution.IsConfigured);
        harness.ViewModel.OpenSettings(SettingsCategory.Common);
        AssertDefinition(loadedInput, harness.Design.Draft);
        Assert.Equal(explicitDirectory, harness.Execution.OutputDirectory);
        Assert.Equal(explicitDirectory, harness.Execution.OutputDirectoryOverride);
        Assert.Equal(3, harness.Execution.PlannedEvaluationCount);
        Assert.False(harness.Execution.IsConfigured);
        Assert.Equal(0, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
        Assert.Null(harness.Execution.LastLoginTask);
        Assert.Null(harness.Output.LastRequest);
        Assert.Null(harness.ViewModel.Settings.LastSaveTask);
        Assert.False(Directory.Exists(explicitDirectory));
        Assert.Equal(savedBytes, File.ReadAllBytes(store.FilePath));
    }

    [Fact]
    public void Parameterless_settings_reopen_preserves_category_and_only_accepts_defined_enum_parameters()
    {
        using ShellHarness harness = new();
        MainWindowViewModel viewModel = harness.ViewModel;
        foreach (SettingsCategory category in Enum.GetValues<SettingsCategory>())
        {
            Assert.True(viewModel.OpenSettingsCommand.CanExecute(category));
            viewModel.OpenSettingsCommand.Execute(category);
            Assert.Equal(category, viewModel.Settings.SelectedCategory);
            viewModel.CloseSettings();

            viewModel.OpenSettingsCommand.Execute(null);
            Assert.True(viewModel.IsSettingsOpen);
            Assert.Equal(category, viewModel.Settings.SelectedCategory);
            viewModel.Settings.RequestCloseCommand.Execute(null);

            viewModel.OpenSettings();
            Assert.Equal(category, viewModel.Settings.SelectedCategory);
            Assert.Equal(WorkflowStep.Input, viewModel.CurrentStep);
            viewModel.CloseSettings();
        }

        foreach (object invalid in new object[] { "Common", 0, (SettingsCategory)99 })
        {
            Assert.False(viewModel.OpenSettingsCommand.CanExecute(invalid));
            viewModel.OpenSettingsCommand.Execute(invalid);
            Assert.False(viewModel.IsSettingsOpen);
            Assert.Equal(SettingsCategory.ImportedPrompts, viewModel.Settings.SelectedCategory);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => viewModel.OpenSettings((SettingsCategory)99));
        viewModel.OpenSettings(SettingsCategory.Common);
        Assert.Equal(SettingsCategory.Common, viewModel.Settings.SelectedCategory);
        Assert.Equal(ExecutionAuthenticationState.NotChecked, harness.Execution.AuthenticationState);
        AssertNoAutomaticActivity(harness);
    }

    [Fact]
    public void Navigation_labels_name_the_Japanese_destination()
    {
        using ShellHarness harness = new();
        MainWindowViewModel viewModel = harness.ViewModel;
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        Assert.Equal("定量化設計へ  →", viewModel.NextButtonText);

        viewModel.NextCommand.Execute(null);
        Assert.Equal("←  入力へ戻る", viewModel.PreviousButtonText);
        Assert.Equal("実行へ  →", viewModel.NextButtonText);
        viewModel.NextCommand.Execute(null);
        Assert.Equal("←  定量化設計へ戻る", viewModel.PreviousButtonText);
        Assert.Equal("結果・出力へ  →", viewModel.NextButtonText);
        viewModel.NextCommand.Execute(null);
        Assert.Equal("←  実行へ戻る", viewModel.PreviousButtonText);
        Assert.Equal(3, notifications.Count(name => name == nameof(MainWindowViewModel.NextButtonText)));
        Assert.Equal(3, notifications.Count(name => name == nameof(MainWindowViewModel.PreviousButtonText)));
        AssertNoAutomaticActivity(harness);
    }

    [Theory]
    [InlineData(WorkflowStep.Design, SettingsCategory.Mapping)]
    [InlineData(WorkflowStep.Input, SettingsCategory.Evaluation)]
    [InlineData(WorkflowStep.Input, SettingsCategory.Special)]
    [InlineData(WorkflowStep.Input, SettingsCategory.ImportedPrompts)]
    public async Task Targeted_settings_open_synchronizes_the_source_question_ID_after_the_latest_draft(
        WorkflowStep sourceStep, SettingsCategory targetCategory)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        using ShellHarness harness = new(input);
        MainWindowViewModel shell = harness.ViewModel;
        shell.NextCommand.Execute(null);
        if (sourceStep == WorkflowStep.Input)
        {
            shell.PreviousCommand.Execute(null);
        }

        InputQuestionMappingViewModel inputA = input.Questions[0];
        QuestionDesignItemViewModel designB = harness.Design.Questions[1];
        input.SelectedQuestion = inputA;
        harness.Design.SelectedQuestion = designB;
        string expectedId;
        QuantificationDefinition expected;
        if (sourceStep == WorkflowStep.Input)
        {
            inputA.QuestionText = "設定へ渡す最新の入力本文";
            expectedId = inputA.Id;
            expected = input.DefinitionDraft;
        }
        else
        {
            // Equal display names must not confuse the cross-editor ID match.
            designB.DisplayName = harness.Design.Questions[0].DisplayName;
            expectedId = designB.Id;
            expected = harness.Design.Draft;
        }

        shell.OpenSettingsCommand.Execute(targetCategory);

        Assert.True(shell.IsSettingsOpen);
        Assert.Equal(sourceStep, shell.CurrentStep);
        Assert.Equal(targetCategory, shell.Settings.SelectedCategory);
        Assert.Same(input.Questions.Single(question => question.Id == expectedId), input.SelectedQuestion);
        Assert.Same(harness.Design.Questions.Single(question => question.Id == expectedId), harness.Design.SelectedQuestion);
        AssertDefinition(expected, input.DefinitionDraft);
        AssertDefinition(expected, harness.Design.Draft);
        shell.CloseSettings();
        Assert.Equal(expectedId, input.SelectedQuestion?.Id);
        Assert.Equal(expectedId, harness.Design.SelectedQuestion?.Id);
        AssertNoAutomaticActivity(harness);
    }

    [Theory]
    [InlineData(SettingsCategory.Mapping, SettingsCategory.Evaluation, false)]
    [InlineData(SettingsCategory.Mapping, SettingsCategory.Special, false)]
    [InlineData(SettingsCategory.Mapping, SettingsCategory.ImportedPrompts, false)]
    [InlineData(SettingsCategory.Evaluation, SettingsCategory.Mapping, false)]
    [InlineData(SettingsCategory.Special, SettingsCategory.Mapping, false)]
    [InlineData(SettingsCategory.ImportedPrompts, SettingsCategory.Mapping, false)]
    [InlineData(SettingsCategory.Mapping, SettingsCategory.Evaluation, true)]
    [InlineData(SettingsCategory.Mapping, SettingsCategory.Special, true)]
    [InlineData(SettingsCategory.Mapping, SettingsCategory.ImportedPrompts, true)]
    [InlineData(SettingsCategory.Evaluation, SettingsCategory.Mapping, true)]
    [InlineData(SettingsCategory.Special, SettingsCategory.Mapping, true)]
    [InlineData(SettingsCategory.ImportedPrompts, SettingsCategory.Mapping, true)]
    public async Task Settings_cross_category_selection_uses_the_active_category_not_the_underlying_step(
        SettingsCategory sourceCategory, SettingsCategory targetCategory, bool useMainCommand)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        using ShellHarness harness = new(input);
        MainWindowViewModel shell = harness.ViewModel;
        shell.NextCommand.Execute(null);
        if (sourceCategory != SettingsCategory.Mapping)
        {
            shell.PreviousCommand.Execute(null);
        }

        shell.OpenSettings(sourceCategory);
        InputQuestionMappingViewModel inputA = input.Questions[0];
        QuestionDesignItemViewModel designB = harness.Design.Questions[1];
        input.SelectedQuestion = inputA;
        harness.Design.SelectedQuestion = designB;
        string expectedId = sourceCategory == SettingsCategory.Mapping ? inputA.Id : designB.Id;
        QuantificationDefinition inputDraft = input.DefinitionDraft;
        QuantificationDefinition designDraft = harness.Design.Draft;
        WorkflowStep underlyingStep = shell.CurrentStep;

        if (useMainCommand)
        {
            shell.OpenSettingsCommand.Execute(targetCategory);
        }
        else
        {
            // The actual T17 category buttons bypass Main.OpenSettingsCommand.
            shell.Settings.SelectCategoryCommand.Execute(targetCategory);
        }

        Assert.Equal(targetCategory, shell.Settings.SelectedCategory);
        Assert.Equal(underlyingStep, shell.CurrentStep);
        Assert.True(shell.IsSettingsOpen);
        Assert.Same(input.Questions.Single(question => question.Id == expectedId), input.SelectedQuestion);
        Assert.Same(harness.Design.Questions.Single(question => question.Id == expectedId), harness.Design.SelectedQuestion);
        Assert.Same(inputDraft, input.DefinitionDraft);
        Assert.Same(designDraft, harness.Design.Draft);
        AssertNoAutomaticActivity(harness);
    }

    [Theory]
    [InlineData(WorkflowStep.Input)]
    [InlineData(WorkflowStep.Design)]
    public async Task Common_settings_open_and_reopen_do_not_align_selections_or_reset_browsed_pages(WorkflowStep sourceStep)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        using ShellHarness harness = new(input);
        MainWindowViewModel shell = harness.ViewModel;
        shell.NextCommand.Execute(null);
        if (sourceStep == WorkflowStep.Input)
        {
            shell.PreviousCommand.Execute(null);
        }

        InputQuestionMappingViewModel inputA = input.Questions[0];
        QuestionDesignItemViewModel designB = harness.Design.Questions[1];
        input.SelectedQuestion = inputA;
        harness.Design.SelectedQuestion = designB;
        input.PageSize = 1;
        harness.Design.PageSize = 1;
        input.PageIndex = 3;
        harness.Design.PageIndex = 4;
        QuantificationDefinition inputDraft = input.DefinitionDraft;
        QuantificationDefinition designDraft = harness.Design.Draft;
        shell.OpenSettings(SettingsCategory.Common);
        shell.CloseSettings();
        shell.OpenSettingsCommand.Execute(null);

        Assert.True(shell.IsSettingsOpen);
        Assert.Equal(SettingsCategory.Common, shell.Settings.SelectedCategory);
        Assert.Equal(sourceStep, shell.CurrentStep);
        Assert.Same(inputA, input.SelectedQuestion);
        Assert.Same(designB, harness.Design.SelectedQuestion);
        Assert.Equal(3, input.PageIndex);
        Assert.Equal(4, harness.Design.PageIndex);
        Assert.Same(inputDraft, input.DefinitionDraft);
        Assert.Same(designDraft, harness.Design.Draft);
        AssertNoAutomaticActivity(harness);
    }

    [Fact]
    public async Task Workflow_and_settings_sync_follow_the_latest_editor_without_reentrant_configuration()
    {
        using X02TemporaryWorkbook workbook = CreateShellWorkbook();
        using ShellHarness harness = new();
        await harness.Input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        MainWindowViewModel viewModel = harness.ViewModel;
        viewModel.NextCommand.Execute(null);
        InputQuestionMappingViewModel mapping = harness.Input.Questions[0];
        QuestionDesignItemViewModel question = harness.Design.Questions[0];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[0];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[0];
        string definitionId = harness.Input.DefinitionDraft.Id;

        harness.Design.DefinitionName = "主画面の設計編集";
        viewModel.PreviousCommand.Execute(null);
        Assert.Equal("主画面の設計編集", harness.Input.DefinitionDraft.Name);
        mapping.SetSupportingColumn("B", true);
        viewModel.NextCommand.Execute(null);
        Assert.Contains("B", harness.Design.Draft.Questions[0].SupportingSourceColumns);

        viewModel.OpenSettings(SettingsCategory.Mapping);
        harness.Input.FirstDataRow = 3;
        mapping.QuestionText = "設定の入力側で編集した設問";
        viewModel.Settings.SelectedCategory = SettingsCategory.Evaluation;
        Assert.Equal(mapping.QuestionText, question.QuestionText);
        Assert.Equal(3, harness.Design.Draft.FirstDataRow);
        criterion.Description = "設計側の最新の観点";
        viewModel.Settings.SelectedCategory = SettingsCategory.Common;
        harness.Design.DefinitionName = "設定の設計側で編集した名前";
        viewModel.Settings.SelectedCategory = SettingsCategory.Mapping;
        mapping.SetSupportingColumn("C", true);
        viewModel.CloseSettings();

        // Input can also publish a newer edit while the workflow still says Design.
        // The step being left must not override Settings' latest-editor authority.
        harness.Input.FirstDataRow = 2;
        QuantificationDefinition latestInput = harness.Input.DefinitionDraft;
        Assert.NotEqual(latestInput.FirstDataRow, harness.Design.Draft.FirstDataRow);
        int configurationValidations = 0;
        harness.Execution.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExecutionViewModel.ValidationSummary))
            {
                configurationValidations++;
            }
        };

        viewModel.NextCommand.Execute(null);

        Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
        Assert.True(harness.Execution.IsConfigured);
        Assert.Equal(1, configurationValidations);
        AssertDefinition(latestInput, harness.Input.DefinitionDraft);
        AssertDefinition(latestInput, harness.Design.Draft);
        Assert.Equal(definitionId, harness.Design.Draft.Id);
        Assert.Same(mapping, harness.Input.SelectedQuestion);
        Assert.Same(question, harness.Design.SelectedQuestion);
        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.Same(criterion, evaluator.SelectedCriterion);
        Assert.Equal("設計側の最新の観点", criterion.Description);

        configurationValidations = 0;
        harness.Design.DefinitionName = "Execution上の最新の設計編集";
        Assert.Equal(1, configurationValidations);
        AssertDefinition(harness.Design.Draft, harness.Input.DefinitionDraft);
        AssertNoAutomaticActivity(harness);
    }

    [Fact]
    public async Task Equivalent_draft_round_trips_preserve_completed_run_and_selections()
    {
        using X02TemporaryWorkbook workbook = CreateShellWorkbook();
        using ShellHarness harness = new();
        harness.RunHandler = (request, _, _) => U04TestSupport.CreateSummaryAsync(
            request.DraftDefinition, request.WorkbookMetadata);
        await harness.Input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        MainWindowViewModel viewModel = harness.ViewModel;
        viewModel.NextCommand.Execute(null);
        InputQuestionMappingViewModel mapping = harness.Input.Questions[0];
        QuestionDesignItemViewModel question = harness.Design.Questions[0];
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[0];
        CriterionDesignItemViewModel criterion = evaluator.Criteria[0];
        viewModel.NextCommand.Execute(null);
        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        harness.Execution.SelectedModelId = "auto";
        await harness.Execution.StartAsync(TestContext.Current.CancellationToken);
        ExecutionRunContext completed = Assert.IsType<ExecutionRunContext>(harness.Execution.LastRunContext);
        var progress = (harness.Execution.ProgressTotal, harness.Execution.ProgressCompleted, harness.Execution.ProgressInFlight);
        harness.Results.PageSize = 1;
        harness.Results.SelectedRow = harness.Results.RowScores[^1];
        harness.Results.ShowDetailCommand.Execute(null);
        ResultsCriterionViewModel result = harness.Results.Results[^1];
        result.OverrideText = "7";
        ResultsRowScoreViewModel? selectedRow = harness.Results.SelectedRow;
        int resultsPage = harness.Results.PageIndex;
        string resumePath = Path.Combine(Path.GetDirectoryName(workbook.Path)!, "retained.partial.xlsx");
        harness.Execution.ResumePartialPath = resumePath;
        harness.Execution.IsResumeMode = true;

        // A fresh Input clone and a reverted Design edit are semantically unchanged.
        QuantificationDefinition previousDraft = harness.Input.DefinitionDraft;
        string unchangedQuestionText = mapping.QuestionText;
        mapping.QuestionText = unchangedQuestionText;
        Assert.NotSame(previousDraft, harness.Input.DefinitionDraft);
        AssertDefinition(previousDraft, harness.Input.DefinitionDraft);
        viewModel.NavigateCommand.Execute(WorkflowStep.Design);
        string originalName = harness.Design.DefinitionName;
        harness.Design.DefinitionName = "一時変更";
        harness.Design.DefinitionName = originalName;
        viewModel.OpenSettings(SettingsCategory.Common);
        viewModel.Settings.SelectedCategory = SettingsCategory.Mapping;
        viewModel.Settings.SelectedCategory = SettingsCategory.Evaluation;
        viewModel.CloseSettings();
        viewModel.NextCommand.Execute(null);

        Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
        Assert.Same(completed, harness.Execution.LastRunContext);
        Assert.Equal(progress, (harness.Execution.ProgressTotal, harness.Execution.ProgressCompleted, harness.Execution.ProgressInFlight));
        Assert.Equal("auto", harness.Execution.SelectedModelId);
        Assert.Equal(resumePath, harness.Execution.ResumePartialPath);
        Assert.True(harness.Execution.IsResumeMode);
        Assert.Equal(completed.Summary.DefinitionSha256, Canonical.ComputeSha256(harness.Input.DefinitionDraft));
        Assert.Same(mapping, harness.Input.SelectedQuestion);
        Assert.Same(question, harness.Design.SelectedQuestion);
        Assert.Same(evaluator, question.SelectedEvaluator);
        Assert.Same(criterion, evaluator.SelectedCriterion);
        Assert.Same(selectedRow, harness.Results.SelectedRow);
        Assert.Equal(resultsPage, harness.Results.PageIndex);
        Assert.True(harness.Results.IsDetailVisible);
        Assert.Same(result, harness.Results.Results[^1]);
        Assert.Equal("7", result.OverrideText);
        Assert.True(harness.Results.HasUnsavedOverrides);
        AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 1);
    }

    [Fact]
    public async Task Results_settings_edits_reach_the_next_explicit_start()
    {
        using X02TemporaryWorkbook workbook = CreateShellWorkbook();
        using ShellHarness harness = new();
        harness.RunHandler = (request, _, _) => U04TestSupport.CreateSummaryAsync(
            request.DraftDefinition, request.WorkbookMetadata);
        await harness.Input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        MainWindowViewModel viewModel = harness.ViewModel;
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        await harness.Execution.StartAsync(TestContext.Current.CancellationToken);
        ExecutionRunContext previous = Assert.IsType<ExecutionRunContext>(harness.Execution.LastRunContext);
        ResultsRowScoreViewModel? selectedRow = harness.Results.SelectedRow;
        Assert.Equal(WorkflowStep.Results, viewModel.CurrentStep);

        viewModel.OpenSettings(SettingsCategory.Common);
        harness.Design.DefinitionName = "結果確認後の次回定義";
        string nextHash = Canonical.ComputeSha256(harness.Design.Draft);
        viewModel.CloseSettings();
        Assert.Same(previous, harness.Execution.LastRunContext);
        viewModel.PreviousCommand.Execute(null);

        Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
        Assert.Null(harness.Execution.LastRunContext);
        Assert.Same(selectedRow, harness.Results.SelectedRow);
        Assert.True(harness.Results.IsLoaded);
        Assert.True(harness.Execution.CanStart);
        Assert.NotEqual(previous.Summary.DefinitionSha256, nextHash);
        AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 1);

        await harness.Execution.StartAsync(TestContext.Current.CancellationToken);

        QuantificationRunRequest next = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
        Assert.Equal(nextHash, Canonical.ComputeSha256(next.DraftDefinition));
        Assert.Equal(nextHash, harness.Execution.LastRunContext?.Summary.DefinitionSha256);
        Assert.Equal("結果確認後の次回定義", next.DraftDefinition.Name);
        Assert.Equal(WorkflowStep.Results, viewModel.CurrentStep);
        AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_edits_during_run_preserve_snapshot_and_reach_next_start(bool closeBeforeCompletion)
    {
        using X02TemporaryWorkbook workbook = CreateShellWorkbook();
        using ShellHarness harness = new();
        await harness.Input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        MainWindowViewModel viewModel = harness.ViewModel;
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(harness.Input.DefinitionDraft, harness.Input.Metadata!);
        TaskCompletionSource<RunSummary> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.RunHandler = (request, _, token) => harness.Runner.CallCount == 1
            ? release.Task.WaitAsync(token)
            : U04TestSupport.CreateSummaryAsync(request.DraftDefinition, request.WorkbookMetadata);
        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Task run = harness.Execution.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(harness.Execution.IsRunning);
            QuantificationRunRequest active = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
            string activeHash = Canonical.ComputeSha256(active.DraftDefinition);
            viewModel.OpenSettings(SettingsCategory.Common);
            harness.Design.DefinitionName = "進行中に編集した次回定義";
            harness.Execution.MaxConcurrency = 3;
            string nextHash = Canonical.ComputeSha256(harness.Design.Draft);
            if (closeBeforeCompletion)
            {
                viewModel.Settings.RequestCloseCommand.Execute(null);
            }

            Assert.True(harness.Execution.IsRunning);
            Assert.True(harness.Execution.CanCancel);
            Assert.Same(active, harness.Runner.LastRequest);
            Assert.Equal(activeHash, Canonical.ComputeSha256(active.DraftDefinition));
            Assert.Equal(8, active.MaxConcurrency);
            Assert.NotEqual(activeHash, nextHash);
            AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 1);
            release.TrySetResult(summary);
            await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);

            Assert.False(harness.Execution.IsRunning);
            Assert.True(harness.Results.IsLoaded);
            Assert.Same(summary, harness.Execution.LastRunContext?.Summary);
            Assert.Equal(activeHash, summary.DefinitionSha256);
            Assert.Equal(activeHash, Canonical.ComputeSha256(active.DraftDefinition));
            if (closeBeforeCompletion)
            {
                Assert.False(viewModel.IsSettingsOpen);
                Assert.Equal(WorkflowStep.Results, viewModel.CurrentStep);
                viewModel.PreviousCommand.Execute(null);
            }
            else
            {
                Assert.True(viewModel.IsSettingsOpen);
                Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
                Assert.Same(viewModel.Settings, viewModel.CurrentEditorViewModel);
                viewModel.Settings.RequestCloseCommand.Execute(null);
            }

            Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
            Assert.True(harness.Execution.CanStart);
            AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 1);
            await harness.Execution.StartAsync(TestContext.Current.CancellationToken);
            QuantificationRunRequest next = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
            Assert.NotSame(active, next);
            Assert.Equal(nextHash, Canonical.ComputeSha256(next.DraftDefinition));
            Assert.Equal(nextHash, harness.Execution.LastRunContext?.Summary.DefinitionSha256);
            Assert.Equal(3, next.MaxConcurrency);
            Assert.Equal(activeHash, Canonical.ComputeSha256(active.DraftDefinition));
            AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 2);
        }
        finally
        {
            release.TrySetResult(summary);
            await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task Active_run_A_then_input_B_then_execution_shows_current_A_and_next_B_without_revalidation()
    {
        using X02TemporaryWorkbook workbookA = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original", 1, 3, 2, new X02Header(1, "Answer"), new X02Header(2, "Rationale"));
        using X02TemporaryWorkbook workbookB = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original", 1, 4, 2, new X02Header(1, "Answer"), new X02Header(2, "Rationale"));
        using ShellHarness harness = new();
        await harness.Input.SetFilePathAsync(workbookA.Path, TestContext.Current.CancellationToken);
        MainWindowViewModel shell = harness.ViewModel;
        shell.NextCommand.Execute(null);
        shell.NextCommand.Execute(null);
        ExecutionViewModel execution = harness.Execution;
        Assert.Null(execution.OutputDirectoryOverride);
        Assert.Equal(5, execution.PlannedEvaluationCount);
        string outputA = Path.Combine(workbookA.Directory, "result");
        string outputB = Path.Combine(workbookB.Directory, "result");
        string finalA = Path.Combine(outputA, "reserved.xlsx");
        string partialA = Path.Combine(outputA, "reserved.partial.xlsx");
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(harness.Input.DefinitionDraft, harness.Input.Metadata!);
        TaskCompletionSource<RunSummary> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.RunHandler = (_, progress, token) =>
        {
            progress?.Invoke(new EvaluationProgress(5, 3, 1, EvaluationProgressStatus.Running,
                DurableEvaluationStage.EvaluatingRows, referenceCompleted: 1, referenceTotal: 1,
                rowCompleted: 1, rowTotal: 2, finalPath: finalA, partialPath: partialA));
            return release.Task.WaitAsync(token);
        };
        MainWindow window = new(shell);
        Task? run = null;
        try
        {
            window.Show();
            Render();
            await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            run = execution.StartAsync(TestContext.Current.CancellationToken);
            Assert.False(run.IsCompleted);
            QuantificationRunRequest active = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
            QuantificationDefinition activeDefinition = active.DraftDefinition;
            string activeHash = Canonical.ComputeSha256(activeDefinition);
            int validationNotifications = 0;
            int summaryNotifications = 0;
            List<(string OutputDirectory, long Count)> previews = [];
            execution.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ExecutionViewModel.ValidationSummary)) validationNotifications++;
                if (args.PropertyName == nameof(ExecutionViewModel.PlanSummary))
                {
                    summaryNotifications++;
                    previews.Add((execution.OutputDirectory, execution.PlannedEvaluationCount));
                }
            };

            shell.NavigateCommand.Execute(WorkflowStep.Input);
            await harness.Input.SetFilePathAsync(workbookB.Path, TestContext.Current.CancellationToken);
            shell.OpenSettingsCommand.Execute(SettingsCategory.Common);
            Render();

            SettingsView settings = Assert.Single(window.GetVisualDescendants().OfType<SettingsView>());
            Assert.Equal(WorkflowStep.Input, shell.CurrentStep);
            Assert.Equal(outputB, ByAutomationId<TextBox>(settings, "SettingsEffectiveOutputDirectory").Text);
            Assert.Equal(new (string, long)[] { (string.Empty, 0), (outputB, 7) }, previews.ToArray());
            Assert.Equal("今回run 新規出力先: " + outputA, execution.CurrentRunOutputSummary);
            Assert.True(execution.IsRunning);
            Assert.Equal(5, execution.ProgressTotal);
            Assert.Equal(3, execution.ProgressCompleted);
            Assert.Equal(finalA, execution.ReservedFinalPath);
            Assert.Equal(partialA, execution.PartialPath);
            Assert.Same(active, harness.Runner.LastRequest);
            Assert.Equal(workbookA.Path, active.InputPath);
            Assert.NotSame(harness.Input.Metadata, active.WorkbookMetadata);
            Assert.Equal(activeHash, Canonical.ComputeSha256(active.DraftDefinition));
            Assert.Equal(0, validationNotifications);
            Assert.False(Directory.Exists(outputA));
            Assert.False(Directory.Exists(outputB));
            AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 1);

            shell.NavigateCommand.Execute(WorkflowStep.Execution);
            Render();

            ExecutionView view = Assert.Single(window.GetVisualDescendants().OfType<ExecutionView>());
            Assert.True(execution.IsRunning);
            Assert.True(execution.CanCancel);
            Assert.False(execution.CanStart);
            Assert.Null(execution.OutputDirectoryOverride);
            Assert.Equal(7, execution.PlannedEvaluationCount);
            Assert.Equal(21, execution.WorstCaseAttemptCount);
            Assert.Equal(outputB, execution.OutputDirectory);
            Assert.Equal(outputB, Required<TextBox>(view, "EffectiveOutputDirectoryTextBox").Text);
            Assert.Equal("今回run 新規出力先: " + outputA, Required<TextBox>(view, "CurrentRunOutputTextBox").Text);
            Assert.Contains("実行中 model-test 並列8", Required<TextBox>(view, "EffectiveModelTextBox").Text, StringComparison.Ordinal);
            Assert.Contains("次回", Required<TextBlock>(view, "PlanSummaryLabel").Text, StringComparison.Ordinal);
            Assert.Equal(execution.PlanSummary, Required<TextBlock>(view, "PlanSummaryText").Text);
            Assert.Contains("7 evaluation units", execution.PlanSummary, StringComparison.Ordinal);
            Assert.Contains("最大 21 attempts", execution.PlanSummary, StringComparison.Ordinal);
            Assert.Equal(5, execution.ProgressTotal);
            Assert.Equal(3, execution.ProgressCompleted);
            Assert.Equal(1, execution.ProgressInFlight);
            Assert.Equal(DurableEvaluationStage.EvaluatingRows, execution.ProgressStage);
            Assert.Equal("実測: 参照 1 / 1 · 行 1 / 2", Required<TextBlock>(view, "DurableProgressSummary").Text);
            Assert.Equal(finalA, Required<TextBox>(view, "ReservedFinalPathTextBox").Text);
            Assert.Equal(partialA, Required<TextBox>(view, "PartialPathTextBox").Text);
            Assert.Same(active, harness.Runner.LastRequest);
            Assert.Same(activeDefinition, active.DraftDefinition);
            Assert.Equal(activeHash, Canonical.ComputeSha256(active.DraftDefinition));
            Assert.Equal(workbookA.Path, active.InputPath);
            Assert.Equal(outputA, active.OutputDirectory);
            Assert.Null(active.ResumePartialPath);
            Assert.Equal(0, validationNotifications);
            Assert.Equal(2, summaryNotifications); // Clear A at unload, then publish the fully loaded B.

            // Revisiting an unchanged next draft must not repeat execution preflight or summary notifications.
            for (int repeat = 0; repeat < 2; repeat++)
            {
                shell.NavigateCommand.Execute(WorkflowStep.Input);
                shell.NavigateCommand.Execute(WorkflowStep.Execution);
                Render();
            }

            Assert.Equal(0, validationNotifications);
            Assert.Equal(2, summaryNotifications);
            AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 1);
            release.TrySetResult(summary);
            await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
            Assert.Equal(WorkflowStep.Results, shell.CurrentStep);
            Assert.Same(summary, execution.LastRunContext?.Summary);
            Assert.Equal(workbookA.Path, execution.LastRunContext?.InputPath);
            Assert.Equal("前回run 新規出力先: " + outputA, execution.CurrentRunOutputSummary);
            Assert.Equal(outputB, execution.OutputDirectory);
            Assert.Equal(7, execution.PlannedEvaluationCount);
            Assert.False(execution.CanStart); // Presentation alone has not configured B for dispatch.

            shell.PreviousCommand.Execute(null);
            Assert.True(execution.CanStart);
            harness.RunHandler = (request, _, _) => U04TestSupport.CreateSummaryAsync(request.DraftDefinition, request.WorkbookMetadata);
            await execution.StartAsync(TestContext.Current.CancellationToken);
            QuantificationRunRequest next = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
            Assert.NotSame(active, next);
            Assert.Equal(workbookB.Path, next.InputPath);
            Assert.Equal(outputB, next.OutputDirectory);
            Assert.Same(harness.Input.Metadata, next.WorkbookMetadata);
            Assert.Equal(4, next.DraftDefinition.LastDataRow);
            Assert.Equal(activeHash, Canonical.ComputeSha256(active.DraftDefinition));
            AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 2);
        }
        finally
        {
            release.TrySetResult(summary);
            try
            {
                if (run is not null)
                {
                    await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
                }
            }
            finally
            {
                window.Close();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_settings_during_run_refreshes_changed_drafts_when_execution_remains_visible(bool editDefinition)
    {
        using X02TemporaryWorkbook workbook = CreateShellWorkbook();
        using ShellHarness harness = new();
        await harness.Input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        MainWindowViewModel viewModel = harness.ViewModel;
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        TaskCompletionSource<RunSummary> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.RunHandler = (request, _, token) => harness.Runner.CallCount == 1
            ? release.Task.WaitAsync(token)
            : U04TestSupport.CreateSummaryAsync(request.DraftDefinition, request.WorkbookMetadata);
        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Task run = harness.Execution.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            QuantificationRunRequest active = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
            string activeHash = Canonical.ComputeSha256(active.DraftDefinition);
            viewModel.OpenSettings(SettingsCategory.Common);
            if (editDefinition)
            {
                harness.Design.DefinitionName = "同じExecution画面から開始する次回定義";
            }

            string nextHash = Canonical.ComputeSha256(harness.Design.Draft);
            viewModel.CloseSettings();
            Assert.True(harness.Execution.IsRunning);
            Assert.Equal(activeHash, Canonical.ComputeSha256(active.DraftDefinition));
            release.TrySetException(new InvalidOperationException("Synthetic run failure"));
            await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);

            Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
            Assert.False(viewModel.IsSettingsOpen);
            Assert.False(harness.Execution.IsRunning);
            Assert.False(harness.Results.IsLoaded);
            Assert.Equal(editDefinition, harness.Execution.CanStart);
            AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 1);
            if (editDefinition)
            {
                Assert.NotEqual(activeHash, nextHash);
                Assert.DoesNotContain(harness.Execution.TechnicalErrors, error => error.Code == "RUN_FAILED");
                await harness.Execution.StartAsync(TestContext.Current.CancellationToken);
                QuantificationRunRequest next = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
                Assert.Equal(nextHash, Canonical.ComputeSha256(next.DraftDefinition));
                AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 2);
            }
            else
            {
                // An unchanged configuration must not erase the failure to enable a run.
                Assert.Contains(harness.Execution.TechnicalErrors, error => error.Code == "RUN_FAILED");
                await harness.Execution.StartAsync(TestContext.Current.CancellationToken);
                AssertNoAutomaticActivity(harness, authenticationChecks: 1, runs: 1);
            }
        }
        finally
        {
            release.TrySetCanceled(TestContext.Current.CancellationToken);
            await run.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData("missing-sheet", false)]
    [InlineData("missing-sheet", true)]
    [InlineData("read-failure", false)]
    [InlineData("read-failure", true)]
    [InlineData("cancel", false)]
    [InlineData("cancel", true)]
    public async Task Failed_saved_definition_apply_preserves_divergent_drafts_and_latest_preview(
        string failure, bool showExecution)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original", 1, 4, 2, new X02Header(1, "Answer"), new X02Header(2, "Rationale"));
        InputWorkbookLoadResult loaded = await new InputWorkbookLoader().LoadAsync(
            workbook.Path, 1, TestContext.Current.CancellationToken);
        TaskCompletionSource<InputWorkbookLoadResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ApplyingShellInputLoader loader = new(loaded, release.Task);
        SettingsFileStore store = new(Path.Combine(workbook.Directory, "setting.txt"));
        using ShellHarness harness = new(new InputViewModel(loader), store);
        await harness.Input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition saved = harness.Input.DefinitionDraft with
        {
            Name = "適用前の保存定義",
            SourceSheet = failure == "missing-sheet" ? "Missing" : "Original",
        };
        Assert.Equal(SettingsSaveStatus.Saved, (await store.SaveAsync(
            new ApplicationSettings { Definition = saved }, TestContext.Current.CancellationToken)).Status);
        byte[] savedBytes = File.ReadAllBytes(store.FilePath);
        MainWindowViewModel shell = harness.ViewModel;
        Task initialization = shell.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await initialization;
        QuantificationDefinition stored = Assert.IsType<QuantificationDefinition>(shell.Settings.StoredDefinition);
        if (showExecution)
        {
            shell.NextCommand.Execute(null);
            shell.NextCommand.Execute(null);
        }

        shell.OpenSettings(SettingsCategory.Common);
        harness.Design.DefinitionName = "Input に未反映の設計名";
        harness.Design.Questions[0].Evaluators[0].Enabled = false;
        QuantificationDefinition inputDraft = harness.Input.DefinitionDraft;
        QuantificationDefinition designDraft = harness.Design.Draft;
        Assert.NotEqual(inputDraft.Name, designDraft.Name);
        Assert.True(inputDraft.Questions[0].Evaluators[0].Enabled);
        Assert.Equal(4, harness.Execution.PlannedEvaluationCount);
        int inputDraftChanges = 0;
        int designDraftChanges = 0;
        int configurationChanges = 0;
        int busyConfigurations = 0;
        int summaryChanges = 0;
        List<(QuantificationDefinition Input, QuantificationDefinition Design)> completionDrafts = [];
        harness.Input.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(InputViewModel.DefinitionDraft)) inputDraftChanges++;
            if (args.PropertyName == nameof(InputViewModel.IsBusy) && !harness.Input.IsBusy)
                completionDrafts.Add((harness.Input.DefinitionDraft, harness.Design.Draft));
        };
        harness.Design.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(QuantificationDesignViewModel.Draft)) designDraftChanges++;
        };
        shell.Settings.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SettingsViewModel.IsApplying) && !shell.Settings.IsApplying)
                completionDrafts.Add((harness.Input.DefinitionDraft, harness.Design.Draft));
        };
        harness.Execution.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExecutionViewModel.IsConfigured))
            {
                configurationChanges++;
                if (harness.Input.IsBusy || shell.Settings.IsApplying) busyConfigurations++;
            }

            if (args.PropertyName == nameof(ExecutionViewModel.PlanSummary)) summaryChanges++;
        };
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<bool> applying = shell.Settings.ApplySavedDefinitionAsync(cancellation.Token);
        try
        {
            Assert.True(shell.Settings.IsApplying);
            Assert.True(harness.Input.IsBusy);
            Assert.False(applying.IsCompleted);
            Assert.Equal(2, loader.CallCount);
            if (showExecution) shell.CloseSettings();
            Assert.Equal(0, configurationChanges);
            Assert.Empty(completionDrafts);
            switch (failure)
            {
                case "missing-sheet": release.TrySetResult(loaded); break;
                case "read-failure": release.TrySetException(new IOException("Synthetic apply failure.")); break;
                case "cancel": cancellation.Cancel(); break;
                default: throw new ArgumentOutOfRangeException(nameof(failure));
            }

            Assert.False(await applying.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken));

            Assert.Equal(failure switch
            {
                "missing-sheet" => "SOURCE_SHEET_NOT_FOUND",
                "read-failure" => "SAVED_DEFINITION_LOAD_FAILED",
                _ => "SAVED_DEFINITION_CANCELLED",
            }, harness.Input.SavedDefinitionApplicationError?.Code);
            Assert.False(shell.Settings.IsApplying);
            Assert.False(harness.Input.IsBusy);
            Assert.Same(inputDraft, harness.Input.DefinitionDraft);
            Assert.Same(designDraft, harness.Design.Draft);
            Assert.Same(loaded.Metadata, harness.Input.Metadata);
            Assert.Same(loaded.Snapshot, harness.Input.Snapshot);
            Assert.Equal(2, completionDrafts.Count); // Input completion and then Settings completion.
            Assert.All(completionDrafts, drafts =>
            {
                Assert.Same(inputDraft, drafts.Input);
                Assert.Same(designDraft, drafts.Design);
            });
            Assert.Equal(0, inputDraftChanges);
            Assert.Equal(0, designDraftChanges);
            Assert.Equal(0, busyConfigurations);
            Assert.Equal(showExecution ? 1 : 0, configurationChanges);
            Assert.Equal(showExecution ? 1 : 0, summaryChanges);
            Assert.Equal(showExecution, harness.Execution.IsConfigured);
            Assert.Equal(!showExecution, shell.IsSettingsOpen);
            Assert.Equal(showExecution ? WorkflowStep.Execution : WorkflowStep.Input, shell.CurrentStep);
            Assert.Equal(4, harness.Execution.PlannedEvaluationCount); // Not the unchanged Input's 7 units.
            Assert.Equal(12, harness.Execution.WorstCaseAttemptCount);
            Assert.Equal(Path.Combine(workbook.Directory, "result"), harness.Execution.OutputDirectory);
            Assert.Same(stored, shell.Settings.StoredDefinition);
            AssertDefinition(saved, stored);
            Assert.True(shell.Settings.HasUnsavedChanges);
            Assert.Equal(savedBytes, File.ReadAllBytes(store.FilePath));
            Assert.Same(initialization, shell.Settings.LastLoadTask);
            Assert.Same(applying, shell.Settings.LastApplySavedDefinitionTask);
            Assert.Null(shell.Settings.LastSaveTask);
            Assert.Equal(0, harness.Authentication.CallCount);
            Assert.Equal(0, harness.Runner.CallCount);
            Assert.Null(harness.Execution.LastLoginTask);
            Assert.Null(harness.Output.LastRequest);
            Assert.False(Directory.Exists(harness.Execution.OutputDirectory));
        }
        finally
        {
            release.TrySetResult(loaded);
            await applying.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData("same-step", false)]
    [InlineData("same-step", true)]
    [InlineData("direct-close", false)]
    [InlineData("direct-close", true)]
    [InlineData("navigator", false)]
    [InlineData("navigator", true)]
    [InlineData("after-apply", false)]
    [InlineData("after-apply", true)]
    public async Task Saved_definition_apply_during_navigation_reaches_the_next_parent_run_and_preserves_history_on_failure(
        string route, bool failApply)
    {
        using X02TemporaryWorkbook workbook = CreateShellWorkbook();
        InputWorkbookLoadResult loaded = await new InputWorkbookLoader().LoadAsync(
            workbook.Path, 1, TestContext.Current.CancellationToken);
        TaskCompletionSource<InputWorkbookLoadResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ApplyingShellInputLoader loader = new(loaded, release.Task);
        SettingsFileStore store = new(Path.Combine(workbook.Directory, "setting.txt"));
        using ShellHarness harness = new(new InputViewModel(loader), store);
        harness.RunHandler = (request, _, _) => U04TestSupport.CreateSummaryAsync(
            request.DraftDefinition, request.WorkbookMetadata);
        await harness.Input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition sourceA = harness.Input.DefinitionDraft;
        QuantificationDefinition sourceB = sourceA with
        {
            Name = "保存定義 B",
            Questions = sourceA.Questions.SetItem(0, sourceA.Questions[0] with
            {
                PrimarySourceColumn = "B",
                SupportingSourceColumns = ["A"],
            }),
        };
        Assert.Equal(SettingsSaveStatus.Saved, (await store.SaveAsync(
            new ApplicationSettings { Definition = sourceB }, TestContext.Current.CancellationToken)).Status);
        byte[] savedBytes = File.ReadAllBytes(store.FilePath);
        MainWindowViewModel viewModel = harness.ViewModel;
        Task initialization = viewModel.Settings.InitializeAsync(TestContext.Current.CancellationToken);
        await initialization;
        AssertDefinition(sourceB, Assert.IsType<QuantificationDefinition>(viewModel.Settings.StoredDefinition));
        viewModel.NextCommand.Execute(null);
        viewModel.NextCommand.Execute(null);
        Assert.Equal(savedBytes, File.ReadAllBytes(store.FilePath));
        Assert.Equal(0, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
        await harness.Execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        savedBytes = ModelCatalogPersistenceAssert.OnlyCatalogChanged(savedBytes, File.ReadAllBytes(store.FilePath),
            [new("model-test", 64_000, 128_000), new("auto", null, null)]);
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
        await harness.Execution.StartAsync(TestContext.Current.CancellationToken);
        QuantificationRunRequest previousRequest = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
        ExecutionRunContext completed = Assert.IsType<ExecutionRunContext>(harness.Execution.LastRunContext);
        AssertDefinition(sourceA, previousRequest.DraftDefinition);
        Assert.Equal(Canonical.ComputeSha256(sourceA), completed.Summary.DefinitionSha256);
        Assert.Equal(WorkflowStep.Results, viewModel.CurrentStep);
        viewModel.PreviousCommand.Execute(null);
        viewModel.OpenSettings(SettingsCategory.Common);
        QuantificationDefinition designA = harness.Design.Draft;
        ResultsRowScoreViewModel? previousResult = harness.Results.SelectedRow;
        var progress = (harness.Execution.ProgressTotal, harness.Execution.ProgressCompleted, harness.Execution.ProgressInFlight);
        int configurationChanges = 0;
        int busyConfigurations = 0;
        harness.Execution.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExecutionViewModel.IsConfigured))
            {
                configurationChanges++;
                if (viewModel.Settings.IsApplying || harness.Input.IsBusy) busyConfigurations++;
            }
        };

        Task<bool> applying = viewModel.Settings.ApplySavedDefinitionAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(viewModel.Settings.IsApplying);
            Assert.True(harness.Input.IsBusy);
            Assert.False(applying.IsCompleted);
            Assert.Equal(2, loader.CallCount);
            Assert.False(viewModel.Settings.RequestCloseCommand.CanExecute(null));
            viewModel.Settings.RequestCloseCommand.Execute(null);
            Assert.True(viewModel.IsSettingsOpen);
            switch (route)
            {
                case "same-step":
                    Assert.True(viewModel.NavigateCommand.CanExecute(WorkflowStep.Execution));
                    viewModel.NavigateCommand.Execute(WorkflowStep.Execution);
                    break;
                case "direct-close":
                    viewModel.CloseSettings();
                    break;
                case "navigator":
                    Assert.True(viewModel.Navigator.MovePrevious());
                    viewModel.CloseSettings();
                    Assert.True(viewModel.Navigator.MoveNext());
                    break;
                case "after-apply":
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(route));
            }

            Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
            Assert.Equal(route == "after-apply", viewModel.IsSettingsOpen);
            Assert.Same(completed, harness.Execution.LastRunContext);
            Assert.Same(sourceA, harness.Input.DefinitionDraft);
            Assert.Same(designA, harness.Design.Draft);
            Assert.Equal(0, configurationChanges);
            Assert.Equal(1, harness.Runner.CallCount);
            if (failApply)
            {
                release.TrySetException(new IOException("Synthetic saved-definition read failure."));
            }
            else
            {
                release.TrySetResult(loaded);
            }

            Assert.Equal(!failApply, await applying.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken));
            Assert.False(viewModel.Settings.IsApplying);
            Assert.False(harness.Input.IsBusy);
            if (route == "after-apply")
            {
                Assert.True(viewModel.IsSettingsOpen);
                Assert.Same(completed, harness.Execution.LastRunContext);
                Assert.Equal(0, configurationChanges);
                viewModel.Settings.RequestCloseCommand.Execute(null);
            }

            QuantificationDefinition expected = failApply ? sourceA : sourceB;
            Assert.False(viewModel.IsSettingsOpen);
            Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
            AssertDefinition(expected, harness.Input.DefinitionDraft);
            AssertDefinition(expected, harness.Design.Draft);
            Assert.Equal(0, busyConfigurations);
            Assert.Same(previousResult, harness.Results.SelectedRow);
            Assert.True(harness.Results.IsLoaded);
            if (failApply)
            {
                Assert.Same(completed, harness.Execution.LastRunContext);
                Assert.Same(sourceA, harness.Input.DefinitionDraft);
                Assert.Same(designA, harness.Design.Draft);
                Assert.Equal(progress, (harness.Execution.ProgressTotal, harness.Execution.ProgressCompleted, harness.Execution.ProgressInFlight));
                Assert.Equal("SAVED_DEFINITION_LOAD_FAILED", harness.Input.SavedDefinitionApplicationError?.Code);
            }
            else
            {
                Assert.Null(harness.Input.SavedDefinitionApplicationError);
            }

            Assert.True(harness.Execution.CanStart);
            Assert.Equal(1, harness.Runner.CallCount);
            Assert.Equal(savedBytes, File.ReadAllBytes(store.FilePath));
            await harness.Execution.StartAsync(TestContext.Current.CancellationToken);
            QuantificationRunRequest next = Assert.IsType<QuantificationRunRequest>(harness.Runner.LastRequest);
            Assert.NotSame(previousRequest, next);
            Assert.Equal(2, harness.Runner.CallCount);
            AssertDefinition(expected, next.DraftDefinition);
            Assert.Equal(Canonical.ComputeSha256(expected), harness.Execution.LastRunContext?.Summary.DefinitionSha256);
            Assert.Equal(WorkflowStep.Results, viewModel.CurrentStep);
            AssertDefinition(sourceA, previousRequest.DraftDefinition);
            Assert.Equal(Canonical.ComputeSha256(sourceA), completed.Summary.DefinitionSha256);
            Assert.Equal(2, loader.CallCount);
            Assert.Equal(1, harness.Authentication.CallCount);
            Assert.Same(initialization, viewModel.Settings.LastLoadTask);
            Assert.Same(applying, viewModel.Settings.LastApplySavedDefinitionTask);
            Assert.Null(viewModel.Settings.LastSaveTask);
            Assert.Null(harness.Execution.LastLoginTask);
            Assert.Null(harness.Output.LastRequest);
            Assert.Equal(savedBytes, File.ReadAllBytes(store.FilePath));
        }
        finally
        {
            release.TrySetResult(loaded);
            await applying.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Input_load_finishes_before_execution_configuration_or_common_settings_editing(bool keepSettingsOpen)
    {
        using X02TemporaryWorkbook workbook = CreateShellWorkbook();
        InputWorkbookLoadResult loaded = await new InputWorkbookLoader().LoadAsync(
            workbook.Path, 1, TestContext.Current.CancellationToken);
        TaskCompletionSource<InputWorkbookLoadResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using ShellHarness harness = new(new InputViewModel(new DeferredShellInputLoader(release.Task)));
        int busyConfigurations = 0;
        int readyConfigurations = 0;
        harness.Execution.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExecutionViewModel.IsConfigured))
            {
                if (harness.Input.IsBusy) busyConfigurations++;
                if (harness.Execution.IsConfigured) readyConfigurations++;
            }
        };
        Task loading = harness.Input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        try
        {
            Assert.True(harness.Input.IsBusy);
            harness.ViewModel.NextCommand.Execute(null);
            harness.ViewModel.NextCommand.Execute(null);
            harness.ViewModel.OpenSettings();
            if (!keepSettingsOpen)
            {
                harness.ViewModel.CloseSettings();
            }

            Assert.False(harness.Execution.IsConfigured);
            Assert.Equal(0, busyConfigurations);
            Assert.Equal(0, readyConfigurations);
            Assert.Empty(harness.Execution.OutputDirectory);
            Assert.Equal(0, harness.Execution.PlannedEvaluationCount);
            release.TrySetResult(loaded);
            await loading.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);

            Assert.False(harness.Input.IsBusy);
            Assert.True(harness.Input.HasLoadedWorkbook);
            Assert.Equal(0, busyConfigurations);
            AssertDefinition(harness.Input.DefinitionDraft, harness.Design.Draft);
            Assert.Equal(keepSettingsOpen ? 0 : 1, readyConfigurations);
            Assert.Equal(Path.Combine(workbook.Directory, "result"), harness.Execution.OutputDirectory);
            Assert.Equal(3, harness.Execution.PlannedEvaluationCount);
            if (keepSettingsOpen)
            {
                Assert.Equal(SettingsCategory.Common, harness.ViewModel.Settings.SelectedCategory);
                harness.Design.DefinitionName = "共通設定を表示したまま読込後に編集";
                harness.ViewModel.CloseSettings();
                Assert.Equal("共通設定を表示したまま読込後に編集", harness.Input.DefinitionDraft.Name);
            }

            Assert.True(harness.Execution.IsConfigured);
            Assert.Equal(1, readyConfigurations);
            AssertNoAutomaticActivity(harness);
        }
        finally
        {
            release.TrySetResult(loaded);
            await loading.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("empty")]
    [InlineData("null")]
    public async Task Input_replacement_during_reload_publishes_only_coherent_execution_state(string change)
    {
        using X02TemporaryWorkbook workbookA = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original", 1, 3, 2, new X02Header(1, "Answer"), new X02Header(2, "Rationale"));
        using X02TemporaryWorkbook workbookB = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Replacement", 1, 4, 2, new X02Header(1, "Answer"), new X02Header(2, "Rationale"));
        InputWorkbookLoader realLoader = new();
        InputWorkbookLoadResult loadedA = await realLoader.LoadAsync(workbookA.Path, 1, TestContext.Current.CancellationToken);
        InputWorkbookLoadResult loadedB = await realLoader.LoadAsync(workbookB.Path, 1, TestContext.Current.CancellationToken);
        TaskCompletionSource<InputWorkbookLoadResult> reloadRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<InputWorkbookLoadResult> replacementRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SequencedShellInputLoader loader = new(Task.FromResult(loadedA), reloadRelease.Task, replacementRelease.Task);
        using ShellHarness harness = new(new InputViewModel(loader));
        InputViewModel input = harness.Input;
        ExecutionViewModel execution = harness.Execution;
        await input.SetFilePathAsync(workbookA.Path, TestContext.Current.CancellationToken);
        harness.ViewModel.NextCommand.Execute(null);
        harness.ViewModel.NextCommand.Execute(null);
        Assert.Same(execution, harness.ViewModel.CurrentEditorViewModel);
        Assert.True(execution.IsConfigured);
        Assert.Equal(5, execution.PlannedEvaluationCount);
        int busyConfigurations = 0;
        int validations = 0;
        List<bool> configurations = [];
        List<(string OutputDirectory, long Count, WorkbookMetadata? Metadata)> previews = [];
        List<(string Path, WorkbookMetadata? Metadata, QuantificationDefinition Draft)> completions = [];
        execution.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExecutionViewModel.IsConfigured))
            {
                configurations.Add(execution.IsConfigured);
                if (input.IsBusy) busyConfigurations++;
            }

            if (args.PropertyName == nameof(ExecutionViewModel.ValidationSummary)) validations++;
            if (args.PropertyName == nameof(ExecutionViewModel.PlanSummary))
                previews.Add((execution.OutputDirectory, execution.PlannedEvaluationCount, input.Metadata));
        };
        input.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(InputViewModel.IsBusy) && !input.IsBusy)
                completions.Add((input.FilePath, input.Metadata, input.DefinitionDraft));
        };
        Task reload = input.RefreshHeaderAsync(TestContext.Current.CancellationToken);
        Task? replacementLoad = null;
        try
        {
            Assert.True(input.IsBusy);
            Assert.False(reload.IsCompleted);
            Assert.Same(loadedA.Metadata, input.Metadata);
            Assert.Empty(configurations);
            Assert.Empty(previews);
            string? nextPath = change switch
            {
                "replacement" => workbookB.Path,
                "empty" => string.Empty,
                "null" => null,
                _ => throw new ArgumentOutOfRangeException(nameof(change)),
            };

            Assert.Null(Record.Exception(() => input.SetFilePath(nextPath)));

            Assert.False(input.IsBusy);
            Assert.False(input.HasLoadedWorkbook);
            Assert.Null(input.Metadata);
            Assert.Null(input.Snapshot);
            Assert.Empty(input.DefinitionDraft.Questions);
            Assert.False(execution.IsConfigured);
            Assert.False(execution.CanStart);
            Assert.Equal(string.Empty, execution.OutputDirectory);
            Assert.Equal(0, execution.PlannedEvaluationCount);
            Assert.Contains(execution.TechnicalErrors, error => error.Code == "EXECUTION_CONFIGURATION_REQUIRED");
            Assert.False(Assert.Single(configurations));
            Assert.Equal((string.Empty, 0L, (WorkbookMetadata?)null), Assert.Single(previews));
            var cleared = Assert.Single(completions);
            Assert.Equal(nextPath ?? string.Empty, cleared.Path);
            Assert.Null(cleared.Metadata);
            Assert.Same(input.DefinitionDraft, cleared.Draft);
            Assert.Equal(1, validations);
            Assert.False(reload.IsCompleted);

            if (change == "replacement")
            {
                replacementLoad = input.LoadAsync(TestContext.Current.CancellationToken);
                Assert.True(input.IsBusy);
                Assert.False(replacementLoad.IsCompleted);
                Assert.Single(configurations);
                Assert.Single(previews);
                replacementRelease.TrySetResult(loadedB);
                await replacementLoad.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);

                Assert.True(input.HasLoadedWorkbook);
                Assert.Same(loadedB.Metadata, input.Metadata);
                Assert.Same(loadedB.Snapshot, input.Snapshot);
                Assert.Equal("Replacement", input.DefinitionDraft.SourceSheet);
                AssertDefinition(input.DefinitionDraft, harness.Design.Draft);
                Assert.True(execution.IsConfigured);
                Assert.Equal(7, execution.PlannedEvaluationCount);
                Assert.Equal(21, execution.WorstCaseAttemptCount);
                Assert.Equal(Path.Combine(workbookB.Directory, "result"), execution.OutputDirectory);
                Assert.Equal(new[] { false, true }, configurations);
                Assert.Equal(2, previews.Count);
                Assert.Equal((execution.OutputDirectory, 7L, (WorkbookMetadata?)loadedB.Metadata), previews[1]);
                Assert.Equal(2, completions.Count);
                Assert.Equal((workbookB.Path, (WorkbookMetadata?)loadedB.Metadata, input.DefinitionDraft), completions[1]);
            }

            QuantificationDefinition currentInput = input.DefinitionDraft;
            QuantificationDefinition currentDesign = harness.Design.Draft;
            WorkbookMetadata? currentMetadata = input.Metadata;
            var currentSnapshot = input.Snapshot;
            reloadRelease.TrySetResult(loadedA); // The superseded A read deliberately finishes last.
            await reload.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);

            Assert.False(input.IsBusy);
            Assert.Same(currentInput, input.DefinitionDraft);
            Assert.Same(currentDesign, harness.Design.Draft);
            Assert.Same(currentMetadata, input.Metadata);
            Assert.Same(currentSnapshot, input.Snapshot);
            int expectedPublications = change == "replacement" ? 2 : 1;
            Assert.Equal(expectedPublications, configurations.Count);
            Assert.Equal(expectedPublications, previews.Count);
            Assert.Equal(expectedPublications, completions.Count);
            Assert.Equal(expectedPublications, validations);
            Assert.Equal(0, busyConfigurations);
            Assert.DoesNotContain(previews, preview => ReferenceEquals(loadedA.Metadata, preview.Metadata));
            Assert.Equal(change == "replacement"
                ? new[] { workbookA.Path, workbookA.Path, workbookB.Path }
                : new[] { workbookA.Path, workbookA.Path }, loader.Paths);
            Assert.Same(execution, harness.ViewModel.CurrentEditorViewModel);
            Assert.False(execution.HasCurrentRun);
            Assert.Null(execution.LastRunContext);
            Assert.False(Directory.Exists(Path.Combine(workbookA.Directory, "result")));
            Assert.False(Directory.Exists(Path.Combine(workbookB.Directory, "result")));
            AssertNoAutomaticActivity(harness);
        }
        finally
        {
            replacementRelease.TrySetResult(loadedB);
            reloadRelease.TrySetResult(loadedA);
            if (replacementLoad is not null)
                await replacementLoad.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
            await reload.WaitAsync(BoundaryWait, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public void Dispose_detaches_all_shell_and_settings_subscriptions()
    {
        using ShellHarness harness = new();
        MainWindowViewModel viewModel = harness.ViewModel;
        (object Publisher, Type DeclaringType, string EventName)[] subscriptions =
        [
            (viewModel.Navigator, typeof(WorkflowNavigator), nameof(WorkflowNavigator.CurrentStepChanged)),
            (harness.Input, typeof(UiObservableObject), nameof(UiObservableObject.PropertyChanged)),
            (harness.Design, typeof(UiObservableObject), nameof(UiObservableObject.PropertyChanged)),
            (harness.Execution, typeof(UiObservableObject), nameof(UiObservableObject.PropertyChanged)),
            (harness.Execution, typeof(ExecutionViewModel), nameof(ExecutionViewModel.RunCompleted)),
            (viewModel.Settings, typeof(UiObservableObject), nameof(UiObservableObject.PropertyChanged)),
            (viewModel.Settings, typeof(SettingsViewModel), nameof(SettingsViewModel.CloseRequested)),
        ];
        foreach (var subscription in subscriptions)
        {
            Assert.Contains(SubscriptionHandlers(subscription), handler => ReferenceEquals(handler.Target, viewModel));
        }

        int notifications = 0;
        viewModel.PropertyChanged += (_, _) => notifications++;
        viewModel.Dispose();
        int afterDispose = notifications;
        foreach (var subscription in subscriptions)
        {
            Assert.DoesNotContain(SubscriptionHandlers(subscription), handler =>
                ReferenceEquals(handler.Target, viewModel) || ReferenceEquals(handler.Target, viewModel.Settings));
        }

        viewModel.Navigator.MoveNext();
        harness.Input.AddQuestion();
        harness.Design.DefinitionName = "Dispose後の編集";
        harness.Execution.MaxConcurrency = 3;
        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.OpenSettings(SettingsCategory.Mapping);
        viewModel.CloseSettings();
        viewModel.Dispose();
        Assert.Equal(afterDispose, notifications);
        Assert.False(viewModel.OpenSettingsCommand.CanExecute(null));
        Assert.False(viewModel.IsSettingsOpen);
        AssertNoAutomaticActivity(harness);
    }

    [AvaloniaFact]
    public void App_factory_creates_constructor_injected_shell_with_input_as_the_initial_step()
    {
        App app = Assert.IsType<App>(Application.Current);
        MainWindow window = app.CreateMainWindow();

        try
        {
            Assert.Same(window.ViewModel, window.DataContext);
            Assert.Same(app.Services.WorkflowNavigator, window.ViewModel.Navigator);
            Assert.Equal(WorkflowStep.Input, window.ViewModel.CurrentStep);
            Assert.Equal("StudyReport Evaluator", window.Title);
            Assert.Equal(1180d, window.Width);
            Assert.Equal(800d, window.Height);
            Assert.Equal(1024d, window.MinWidth);
            Assert.Equal(720d, window.MinHeight);

            window.Show();
            Dispatcher.UIThread.RunJobs();

            Border warning = Required<Border>(window, "EthicsWarningBanner");
            Button input = Required<Button>(window, "InputStepButton");
            ContentControl content = Required<ContentControl>(window, "CurrentStepContent");
            Assert.True(warning.IsVisible);
            Assert.Same(input, window.FocusManager?.GetFocusedElement());
            InputView inputView = Assert.Single(window.GetVisualDescendants().OfType<InputView>());
            Assert.Same(inputView, content.Content);
            Assert.Same(window.ViewModel.InputViewModel, inputView.DataContext);
            Assert.False(Required<Grid>(window, "CurrentStepPlaceholderHost").IsVisible);
            AssertNoDevelopmentTaskText(window);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Keyboard_navigation_exposes_automation_ids_and_reaches_all_four_states()
    {
        MainWindow window = new ServiceRegistration().CreateMainWindow();

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Button input = Required<Button>(window, "InputStepButton");
            Button design = Required<Button>(window, "DesignStepButton");
            Button execution = Required<Button>(window, "ExecutionStepButton");
            Button results = Required<Button>(window, "ResultsStepButton");
            Button next = Required<Button>(window, "NextStepButton");

            Assert.Equal("WorkflowStepInput", AutomationProperties.GetAutomationId(input));
            Assert.Equal("WorkflowStepDesign", AutomationProperties.GetAutomationId(design));
            Assert.Equal("WorkflowStepExecution", AutomationProperties.GetAutomationId(execution));
            Assert.Equal("WorkflowStepResults", AutomationProperties.GetAutomationId(results));
            Assert.Equal([0, 1, 2, 3], new[] { input, design, execution, results }.Select(button => button.TabIndex));
            Assert.True(input.IsEffectivelyEnabled);
            Assert.True(design.IsEffectivelyEnabled);
            Assert.False(execution.IsEffectivelyEnabled);
            Assert.False(results.IsEffectivelyEnabled);
            Assert.Contains("現在", window.ViewModel.InputStep.StatusText, StringComparison.Ordinal);
            Assert.Equal("●", window.ViewModel.InputStep.StatusIcon);
            Assert.Contains("current", input.Classes);
            Assert.Contains("upcoming", execution.Classes);
            Assert.Single(window.GetVisualDescendants().OfType<InputView>());
            AssertNoDevelopmentTaskText(window);

            Assert.True(input.Focus(NavigationMethod.Tab, KeyModifiers.None));
            Press(window, Key.Tab);
            Assert.True(design.IsFocused);
            Press(window, Key.Enter);
            Assert.Equal(WorkflowStep.Design, window.ViewModel.CurrentStep);
            Assert.Equal("設定済み", window.ViewModel.InputStep.StatusText);
            Assert.Equal("◉", window.ViewModel.InputStep.StatusIcon);
            Assert.Contains("現在", window.ViewModel.DesignStep.StatusText, StringComparison.Ordinal);
            Assert.Contains("visited", input.Classes);
            Assert.DoesNotContain("completed", input.Classes);
            Assert.Contains("current", design.Classes);
            QuantificationDesignView designView = Assert.Single(
                window.GetVisualDescendants().OfType<QuantificationDesignView>());
            Assert.Same(window.ViewModel.DesignViewModel, designView.DataContext);
            Assert.Same(Required<TextBox>(designView, "BasePointsTextBox"), window.FocusManager?.GetFocusedElement());
            AssertNoDevelopmentTaskText(window);

            Assert.True(next.Focus(NavigationMethod.Tab, KeyModifiers.None));
            Press(window, Key.Enter);
            Assert.Equal(WorkflowStep.Execution, window.ViewModel.CurrentStep);
            ExecutionView executionView = Assert.Single(
                window.GetVisualDescendants().OfType<ExecutionView>());
            Assert.Same(window.ViewModel.ExecutionViewModel, executionView.DataContext);
            Assert.Same(Required<Button>(executionView, "ChangeExecutionSettingsButton"), window.FocusManager?.GetFocusedElement());
            AssertNoDevelopmentTaskText(window);
            Assert.True(next.Focus(NavigationMethod.Tab, KeyModifiers.None));
            Press(window, Key.Enter);
            Assert.Equal(WorkflowStep.Results, window.ViewModel.CurrentStep);
            ResultsOutputView resultsView = Assert.Single(
                window.GetVisualDescendants().OfType<ResultsOutputView>());
            Assert.Same(window.ViewModel.ResultsOutputViewModel, resultsView.DataContext);
            Assert.Equal("設定済み", window.ViewModel.ExecutionStep.StatusText);
            Assert.Contains("現在", window.ViewModel.ResultsStep.StatusText, StringComparison.Ordinal);
            Assert.False(next.IsEffectivelyEnabled);
            Assert.False(next.IsEffectivelyVisible); // No disabled "最終ステップ" primary action.
            Button previous = Required<Button>(window, "PreviousStepButton");
            Assert.Equal(window.ViewModel.PreviousButtonText, previous.Content);
            Assert.Equal(window.ViewModel.PreviousButtonText, AutomationProperties.GetName(previous));
            Assert.True(Required<Border>(window, "EthicsWarningBanner").IsVisible);
            Assert.False(Required<Grid>(window, "CurrentStepPlaceholderHost").IsVisible);
            AssertNoDevelopmentTaskText(window);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Shell_is_scrollable_focus_visible_and_usable_at_two_hundred_percent_scale()
    {
        MainWindow window = new ServiceRegistration().CreateMainWindow();

        try
        {
            window.Show();
            window.SetRenderScaling(2d);
            Dispatcher.UIThread.RunJobs();

            ScrollViewer scrollViewer = Required<ScrollViewer>(window, "ShellScrollViewer");
            Button input = Required<Button>(window, "InputStepButton");
            Button next = Required<Button>(window, "NextStepButton");

            Assert.Equal(2d, window.RenderScaling);
            Assert.Equal(ScrollBarVisibility.Disabled, scrollViewer.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility);
            Assert.True(input.MinHeight >= 44d);
            Assert.True(input.MinWidth >= 44d);
            Assert.True(next.MinHeight >= 44d);
            Assert.True(input.Focus(NavigationMethod.Tab, KeyModifiers.None));
            Assert.Same(input, window.FocusManager?.GetFocusedElement());
            Assert.Equal(new Thickness(3d), input.BorderThickness);
            Assert.True(next.Focus(NavigationMethod.Tab, KeyModifiers.None));
            Assert.Same(next, window.FocusManager?.GetFocusedElement());
            Assert.Equal(new Thickness(3d), next.BorderThickness);
            Assert.True(Required<Border>(window, "EthicsWarningBanner").IsVisible);
            Assert.DoesNotContain(input.GetVisualAncestors(), ancestor => ancestor is ScrollViewer);
            Assert.DoesNotContain(next.GetVisualAncestors(), ancestor => ancestor is ScrollViewer);
            Assert.DoesNotContain(Required<Border>(window, "EthicsWarningBanner").GetVisualAncestors(), ancestor => ancestor is ScrollViewer);
            Assert.True(Required<ContentControl>(window, "CurrentStepContent").IsVisible);
            Assert.False(Required<Grid>(window, "CurrentStepPlaceholderHost").IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Design_view_height_tracks_the_available_shell_height()
    {
        MainWindowViewModel viewModel = new(
            new WorkflowNavigator(),
            new InputViewModel(),
            new QuantificationDesignViewModel());
        MainWindow window = new(viewModel)
        {
            Width = 1180,
            Height = 720,
        };

        try
        {
            window.Show();
            viewModel.NextCommand.Execute(null);
            Render();

            QuantificationDesignView designView = Assert.Single(
                window.GetVisualDescendants().OfType<QuantificationDesignView>());
            double compactHeight = designView.Bounds.Height;
            Assert.True(compactHeight > 0d);
            Assert.True(compactHeight < window.ClientSize.Height);

            window.Height = 1000;
            Render();

            Assert.True(designView.Bounds.Height > compactHeight);
            Assert.True(designView.Bounds.Height < window.ClientSize.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Shell_round_trips_loaded_input_mapping_and_quantification_design_without_losing_edits()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        WorkflowNavigator navigator = new();
        InputViewModel inputViewModel = new();
        await inputViewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        MainWindowViewModel viewModel = new(
            navigator,
            inputViewModel,
            new QuantificationDesignViewModel());
        MainWindow window = new(viewModel);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(window.GetVisualDescendants().OfType<InputView>());

            viewModel.NextCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            QuantificationDesignView designView = Assert.Single(
                window.GetVisualDescendants().OfType<QuantificationDesignView>());
            Assert.Same(viewModel.DesignViewModel, designView.DataContext);
            Assert.Equal("Original", viewModel.DesignViewModel.Draft.SourceSheet);
            Assert.Equal(
                inputViewModel.DefinitionDraft.Questions.Select(question => question.PrimarySourceColumn),
                viewModel.DesignViewModel.Draft.Questions.Select(question => question.PrimarySourceColumn));

            viewModel.DesignViewModel.DefinitionName = "往復後も保持する設計";
            viewModel.PreviousCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("往復後も保持する設計", inputViewModel.DefinitionDraft.Name);

            inputViewModel.FirstDataRow = 3;
            viewModel.NextCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("往復後も保持する設計", viewModel.DesignViewModel.DefinitionName);
            Assert.Equal(3, viewModel.DesignViewModel.Draft.FirstDataRow);
            Assert.Single(window.GetVisualDescendants().OfType<QuantificationDesignView>());
            Assert.False(Required<Grid>(window, "CurrentStepPlaceholderHost").IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Design_formula_and_editor_stay_inside_the_shell_after_shrinking_and_scrolling()
    {
        MainWindow window = new ServiceRegistration().CreateMainWindow();
        window.Width = 1024;
        window.Height = 1000;
        try
        {
            window.Show();
            window.ViewModel.NextCommand.Execute(null);
            Render();
            window.Height = 720;
            Render();
            QuantificationDesignView view = Assert.Single(window.GetVisualDescendants().OfType<QuantificationDesignView>());
            ScrollViewer shell = Required<ScrollViewer>(window, "ShellScrollViewer");
            ScrollViewer body = Required<ScrollViewer>(view, "QuantificationDesignScrollViewer");
            Border guide = Required<Border>(view, "FormulaGuide");
            Point before = guide.TranslatePoint(default, window)!.Value;
            body.ScrollToEnd();
            shell.ScrollToEnd();
            Render();

            Assert.True(shell.Extent.Height <= shell.Viewport.Height + 1d);
            Assert.True(body.Extent.Height <= body.Viewport.Height + 1d);
            Assert.Equal(default, shell.Offset);
            Assert.Equal(default, body.Offset);
            Assert.Equal(before.Y, guide.TranslatePoint(default, window)!.Value.Y, 1d);
            Point editorOrigin = body.TranslatePoint(default, window)!.Value;
            Assert.True(body.Viewport.Height >= 100d);
            Assert.True(editorOrigin.Y >= 0d);
            Assert.True(editorOrigin.Y + body.Bounds.Height <= window.ClientSize.Height + 1d);
            Grid footer = Required<Grid>(window, "ShellFooter");
            Assert.True(editorOrigin.Y + body.Bounds.Height <= footer.TranslatePoint(default, window)!.Value.Y);
            Assert.True(guide.TranslatePoint(default, body)!.Value.Y + guide.Bounds.Height <= body.Viewport.Height + 1d);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Shell_configures_execution_and_hands_the_completed_run_context_to_results()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original",
            1,
            2,
            2,
            new X02Header(1, "Answer"),
            new X02Header(2, "Rationale"));
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition definition = input.DefinitionDraft;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(
            definition,
            input.Metadata ?? throw new InvalidOperationException("Workbook metadata is required."));
        RecordingRunBoundary runner = new((_, progress, _) =>
        {
            progress?.Invoke(new EvaluationProgress(1, 1, 0, EvaluationProgressStatus.Completed));
            return Task.FromResult(summary);
        });
        ExecutionViewModel execution = new(
            new RecordingAuthenticationBoundary(
                new ExecutionAuthenticationSnapshot(
                    ExecutionAuthenticationState.Available,
                    [U04TestSupport.Model("model-test"), U04TestSupport.Model("auto")],
                    U04TestSupport.RuntimeIdentity())),
            runner);
        RecordingOutputBoundary output = new();
        ResultsOutputViewModel results = new(output);
        MainWindowViewModel viewModel = new(
            new WorkflowNavigator(),
            input,
            new QuantificationDesignViewModel(definition),
            execution,
            results);
        MainWindow window = new(viewModel);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            viewModel.NextCommand.Execute(null);
            viewModel.NextCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
            Assert.True(execution.IsConfigured);
            Assert.Single(window.GetVisualDescendants().OfType<ExecutionView>());
            await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            await execution.StartAsync(TestContext.Current.CancellationToken);

            QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(runner.LastRequest);
            Assert.Equal(workbook.Path, request.InputPath);
            Assert.Equal(definition.Name, request.DraftDefinition.Name);
            Assert.True(results.IsLoaded);
            Assert.Equal(summary.CompletedEvaluationCount, results.CompletedEvaluationCount);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WorkflowStep.Results, viewModel.CurrentStep);
            ResultsOutputView resultsView = Assert.Single(
                window.GetVisualDescendants().OfType<ResultsOutputView>());
            Assert.Same(results, resultsView.DataContext);
            Assert.True(Required<Border>(window, "EthicsWarningBanner").IsVisible);
            await results.ExportAsync(TestContext.Current.CancellationToken);
            Assert.Same(execution.LastRunContext, output.LastRequest?.Context);

            ExecutionRunContext completedContext = execution.LastRunContext
                ?? throw new InvalidOperationException("Completed run context is required.");
            viewModel.PreviousCommand.Execute(null);

            Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
            Assert.Same(completedContext, execution.LastRunContext);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Run_completion_while_settings_are_open_keeps_settings_open_and_retains_the_execution_step()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original",
            1,
            2,
            2,
            new X02Header(1, "Answer"),
            new X02Header(2, "Rationale"));
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition definition = input.DefinitionDraft;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(
            definition,
            input.Metadata ?? throw new InvalidOperationException("Workbook metadata is required."));
        RecordingRunBoundary runner = new((_, progress, _) =>
        {
            progress?.Invoke(new EvaluationProgress(1, 1, 0, EvaluationProgressStatus.Completed));
            return Task.FromResult(summary);
        });
        ExecutionViewModel execution = new(
            new RecordingAuthenticationBoundary(
                new ExecutionAuthenticationSnapshot(
                    ExecutionAuthenticationState.Available,
                    [U04TestSupport.Model("model-test"), U04TestSupport.Model("auto")],
                    U04TestSupport.RuntimeIdentity())),
            runner);
        ResultsOutputViewModel results = new(new RecordingOutputBoundary());
        MainWindowViewModel viewModel = new(
            new WorkflowNavigator(),
            input,
            new QuantificationDesignViewModel(definition),
            execution,
            results);
        MainWindow window = new(viewModel);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            viewModel.NextCommand.Execute(null);
            viewModel.NextCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            viewModel.OpenSettings(SettingsCategory.Common);
            Assert.True(viewModel.IsSettingsOpen);
            Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);

            await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            await execution.StartAsync(TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();

            Assert.True(viewModel.IsSettingsOpen);
            Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
            Assert.True(results.IsLoaded);
            Assert.Same(viewModel.Settings, viewModel.CurrentEditorViewModel);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Shell_does_not_reuse_previous_execution_configuration_after_input_is_unloaded()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original",
            1,
            2,
            2,
            new X02Header(1, "Answer"));
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        ExecutionViewModel execution = U04TestSupport.ConfiguredExecutionViewModel(
            input.DefinitionDraft,
            input.Metadata ?? throw new InvalidOperationException("Workbook metadata is required."),
            new RecordingRunBoundary((_, _, _) => throw new InvalidOperationException("must not run")));
        MainWindowViewModel viewModel = new(
            new WorkflowNavigator(),
            input,
            new QuantificationDesignViewModel(input.DefinitionDraft),
            execution,
            new ResultsOutputViewModel(new RecordingOutputBoundary()));
        MainWindow window = new(viewModel);

        try
        {
            window.Show();
            viewModel.NextCommand.Execute(null);
            viewModel.NextCommand.Execute(null);
            Assert.True(execution.IsConfigured);

            viewModel.NavigateCommand.Execute(WorkflowStep.Input);
            input.SetFilePath(string.Empty);
            viewModel.NavigateCommand.Execute(WorkflowStep.Execution);

            Assert.False(execution.IsConfigured);
            Assert.False(execution.CanStart);
            Assert.Contains(
                execution.TechnicalErrors,
                error => error.Code == "EXECUTION_CONFIGURATION_REQUIRED");
        }
        finally
        {
            window.Close();
        }
    }

    private static readonly CanonicalDefinitionSerializer Canonical = new();
    private static readonly TimeSpan BoundaryWait = TimeSpan.FromSeconds(10);

    private static X02TemporaryWorkbook CreateShellWorkbook() => X02SyntheticWorkbookFactory.CreateSingleSheet(
        "Original", 1, 2, 3,
        new X02Header(1, "Answer"),
        new X02Header(2, "Rationale"),
        new X02Header(3, "Context"));

    private static void AssertDefinition(QuantificationDefinition expected, QuantificationDefinition actual) =>
        Assert.Equal(Canonical.SerializeToUtf8Bytes(expected), Canonical.SerializeToUtf8Bytes(actual));

    private static void AssertNoAutomaticActivity(ShellHarness harness, int authenticationChecks = 0, int runs = 0)
    {
        Assert.Equal(authenticationChecks, harness.Authentication.CallCount);
        Assert.Equal(runs, harness.Runner.CallCount);
        Assert.Null(harness.Execution.LastLoginTask);
        Assert.False(harness.Execution.IsLoggingIn);
        Assert.Null(harness.Output.LastRequest);
        Assert.Null(harness.ViewModel.Settings.LastLoadTask);
        Assert.Null(harness.ViewModel.Settings.LastSaveTask);
    }

    private static Delegate[] SubscriptionHandlers((object Publisher, Type DeclaringType, string EventName) subscription)
    {
        FieldInfo field = subscription.DeclaringType.GetField(subscription.EventName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The event backing field is required to verify detachment.");
        return (field.GetValue(subscription.Publisher) as Delegate)?.GetInvocationList() ?? [];
    }

    private sealed class DeferredShellInputLoader(Task<InputWorkbookLoadResult> result) : IInputWorkbookLoader
    {
        public Task<InputWorkbookLoadResult> LoadAsync(string filePath, uint headerRow, CancellationToken cancellationToken = default) =>
            result.WaitAsync(cancellationToken);
    }

    private sealed class SequencedShellInputLoader(params Task<InputWorkbookLoadResult>[] results) : IInputWorkbookLoader
    {
        private readonly Queue<Task<InputWorkbookLoadResult>> pending = new(results);
        public List<string> Paths { get; } = [];

        public Task<InputWorkbookLoadResult> LoadAsync(string filePath, uint headerRow, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Paths.Add(filePath);
            return pending.Dequeue().WaitAsync(cancellationToken);
        }
    }

    private sealed class ApplyingShellInputLoader(
        InputWorkbookLoadResult initial,
        Task<InputWorkbookLoadResult> application) : IInputWorkbookLoader
    {
        public int CallCount { get; private set; }

        public Task<InputWorkbookLoadResult> LoadAsync(string filePath, uint headerRow, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return CallCount == 1 ? Task.FromResult(initial) : application.WaitAsync(cancellationToken);
        }
    }

    private sealed class ShellHarness : IDisposable
    {
        public ShellHarness(InputViewModel? input = null, SettingsFileStore? settingsStore = null)
        {
            Input = input ?? new InputViewModel();
            Runner = new RecordingRunBoundary((request, progress, token) => RunHandler(request, progress, token));
            Execution = new ExecutionViewModel(Authentication, Runner);
            Results = new ResultsOutputViewModel(Output);
            ViewModel = new MainWindowViewModel(new WorkflowNavigator(), Input, Design, Execution, Results, settingsStore);
        }

        public InputViewModel Input { get; }
        public QuantificationDesignViewModel Design { get; } = new();
        public RecordingAuthenticationBoundary Authentication { get; } = new(
            new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.Available,
                [U04TestSupport.Model("model-test"), U04TestSupport.Model("auto")], U04TestSupport.RuntimeIdentity()));
        public RecordingRunBoundary Runner { get; }
        public RecordingOutputBoundary Output { get; } = new();
        public ExecutionViewModel Execution { get; }
        public ResultsOutputViewModel Results { get; }
        public MainWindowViewModel ViewModel { get; }
        public Func<QuantificationRunRequest, Action<EvaluationProgress>?, CancellationToken, Task<RunSummary>> RunHandler { get; set; } =
            (_, _, _) => throw new InvalidOperationException("No run was authorized by this test.");

        public void Dispose() => ViewModel.Dispose();
    }

    private static T Required<T>(Control root, string name)
        where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

    private static T ByAutomationId<T>(Control root, string id)
        where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertNoDevelopmentTaskText(Control root) =>
        Assert.DoesNotContain(
            root.GetVisualDescendants().OfType<TextBlock>(),
            textBlock => textBlock.Text?.Contains("U-03", StringComparison.Ordinal) == true
                || textBlock.Text?.Contains("U-04", StringComparison.Ordinal) == true);

    private static void Press(TopLevel window, Key key)
    {
        PhysicalKey physicalKey = key switch
        {
            Key.Tab => PhysicalKey.Tab,
            Key.Enter => PhysicalKey.Enter,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported test key."),
        };
        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
        Dispatcher.UIThread.RunJobs();
    }
}
