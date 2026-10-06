using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Navigation;
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
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

public sealed class DocumentationScreenshotTests
{
    private const string GenerateEnvironmentVariable =
        "STUDY_REPORT_EVALUATOR_GENERATE_DOC_IMAGES";
    private const string GenerateExecutionEnvironmentVariable =
        "STUDY_REPORT_EVALUATOR_GENERATE_EXECUTION_DOC_IMAGE";
    private const string SafeInputPath = @"C:\Synthetic\StudyReport-100x2.xlsx";
    private const string SafeOutputDirectory = @"C:\Synthetic\result";
    private const string SafeFinalPath = @"C:\Synthetic\result\eval-20260902-1200.xlsx";
    private const string SafeReviewedPath = @"C:\Synthetic\result\eval-20260902-1200-reviewed.xlsx";
    private const int ScreenshotWidth = 1440;
    private const int ScreenshotHeight = 1050;
    private static readonly DateTimeOffset ScreenshotUtc = new(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly string[] QuestionIds = ["documentation-question-1", "documentation-question-2"];

    // T28 caption contract for T29: these are real views with synthetic content,
    // not native/live acceptance evidence. Only 06/07 consume fake authentication,
    // AI, input-snapshot and finalizer receipts; no final/partial workbook is written.
    private static readonly string[] ScreenshotFileNames =
    [
        "01-input-workbook.png",       // Input: synthetic workbook, 100 rows / 2 questions.
        "02-input-mapping.png",        // Settings / Mapping: question 2, primary C, supporting D.
        "03-design-knowledge.png",     // Design: question 1, Knowledge summary (not its Prompt editor).
        "04-design-custom-prompt.png", // Settings / Evaluation / Prompt: actual Custom editor + preview.
        "05-execution-auto.png",       // Execution: NotChecked, login not started, effort unselected.
        "06-results-review.png",       // Results list: completed FAKE run/finalization, no overrides.
        "07-output-export.png",        // Results detail: raw 8 -> override 9, separate UNSAVED candidate.
        "08-settings.png",             // Settings / Common: no store; no file load/save or success claim.
    ];

    [AvaloniaFact]
    public async Task Documentation_screenshots_use_only_synthetic_state_and_have_expected_dimensions()
    {
        string repositoryRoot = FindRepositoryRoot();
        string imageDirectory = Path.Combine(repositoryRoot, "images");
        bool updateDocumentation = string.Equals(
                Environment.GetEnvironmentVariable(GenerateEnvironmentVariable),
                "1",
                StringComparison.Ordinal);
        string renderedDirectory = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-Captures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(renderedDirectory);
        try
        {
            await GenerateAsync(renderedDirectory);
            AssertCaptureSet(renderedDirectory, ScreenshotWidth, ScreenshotHeight);
            if (updateDocumentation)
            {
                // Publish only the complete, validated normal-size set. The minimum
                // and repeatability tests never copy into images, even under this opt-in.
                foreach (string fileName in ScreenshotFileNames)
                {
                    string destination = Path.Combine(imageDirectory, fileName);
                    File.Copy(Path.Combine(renderedDirectory, fileName), destination, overwrite: true);
                    AssertSavedCapture(destination, ScreenshotWidth, ScreenshotHeight);
                }
            }
        }
        finally
        {
            Directory.Delete(renderedDirectory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Synthetic_screenshot_renders_are_repeatable()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-CaptureRepeat-" + Guid.NewGuid().ToString("N"));
        string first = Directory.CreateDirectory(Path.Combine(directory, "first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(directory, "second")).FullName;
        try
        {
            await GenerateAsync(first);
            await GenerateAsync(second);
            AssertCaptureSet(first, ScreenshotWidth, ScreenshotHeight);
            AssertCaptureSet(second, ScreenshotWidth, ScreenshotHeight);
            foreach (string fileName in ScreenshotFileNames)
            {
                string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(first, fileName))));
                string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(second, fileName))));
                Assert.True(before == after, $"Synthetic screenshot is not repeatable: {fileName}");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Minimum_client_screenshots_verify_all_eight_actual_pixel_frames_without_publishing()
    {
        string directory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(), "StudyReportEvaluator-MinimumCaptures-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            // Requested size alone is not evidence: GenerateAsync checks actual
            // ClientSize, each captured framebuffer, and its non-uniform pixel data.
            // Reopen the saved PNGs as well. No opt-in can publish these smaller files.
            await GenerateAsync(directory, width: 1024, height: 720);
            AssertCaptureSet(directory, 1024, 720);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Execution_documentation_screenshot_renders_are_repeatable()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-ExecutionCaptureRepeat-" + Guid.NewGuid().ToString("N"));
        string first = Directory.CreateDirectory(Path.Combine(directory, "first")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(directory, "second")).FullName;
        string fileName = ScreenshotFileNames[4];
        try
        {
            await GenerateAsync(first, executionOnly: true);
            await GenerateAsync(second, executionOnly: true);
            foreach (string renderedDirectory in new[] { first, second })
            {
                string path = Assert.Single(Directory.GetFiles(renderedDirectory));
                Assert.Equal(fileName, Path.GetFileName(path));
                Assert.True(
                    new FileInfo(path).Length > 10_000,
                    $"Documentation screenshot is unexpectedly small: {fileName}");
                using Bitmap bitmap = new(path);
                Assert.Equal(new PixelSize(ScreenshotWidth, ScreenshotHeight), bitmap.PixelSize);
            }

            string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(first, fileName))));
            string after = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(second, fileName))));
            Assert.True(before == after, $"Synthetic screenshot is not repeatable: {fileName}");

            // Retain only validated 05 renders for review; never publish to images here,
            // even when the separate eight-image generation opt-in is also set.
            if (string.Equals(
                    Environment.GetEnvironmentVariable(GenerateExecutionEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                string artifactDirectory = Path.Combine(
                    FindRepositoryRoot(), "artifacts", "test", "documentation-execution-05");
                foreach (string render in new[] { "first", "second" })
                {
                    string destination = Directory.CreateDirectory(Path.Combine(artifactDirectory, render)).FullName;
                    File.Copy(
                        Path.Combine(directory, render, fileName),
                        Path.Combine(destination, fileName),
                        overwrite: true);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task GenerateAsync(
        string imageDirectory,
        bool executionOnly = false,
        int width = ScreenshotWidth,
        int height = ScreenshotHeight)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original",
            headerRow: 1,
            lastRow: 101,
            lastColumn: 4,
            new X02Header(1, "回答 ID"),
            new X02Header(2, "設問 1 のレポート回答"),
            new X02Header(3, "設問 2 のレポート回答"),
            new X02Header(4, "補助情報"));
        InputWorkbookLoadResult loaded = await new InputWorkbookLoader().LoadAsync(
            workbook.Path,
            headerRow: 1,
            TestContext.Current.CancellationToken);
        InputViewModel input = new(new FixedInputWorkbookLoader(loaded));
        // Consume the launch-input load before mounting the view. A later Loaded
        // event must not regenerate random question/evaluator/criterion identities.
        input.ApplyLaunchInput(SafeInputPath);
        await input.LoadLaunchInputIfRequestedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, input.Questions.Count);
        input.Questions[0].DisplayName = "設問1：機械学習の基礎";
        input.Questions[1].DisplayName = "設問2：活用方法の説明";
        input.Questions[1].SetSupportingColumn("D", selected: true);
        QuantificationDefinition screenshotDefinition = input.DefinitionDraft with
        {
            Id = "documentation-definition",
            Questions = [.. input.DefinitionDraft.Questions.Select((question, index) => question with
            {
                Id = QuestionIds[index],
                Evaluators = [question.Evaluators[0] with
                {
                    Id = $"documentation-evaluator-{index + 1}",
                    Criteria = [question.Evaluators[0].Criteria[0] with { Id = $"documentation-criterion-{index + 1}" }],
                }],
            })],
        };
        // Settings initially treats Input as the latest editor. Seed that owner
        // through its public validated admission path, not only the Design copy.
        Assert.True(await input.ApplySavedDefinitionAsync(
            screenshotDefinition, TestContext.Current.CancellationToken));
        AssertStableDefinition(input.DefinitionDraft);

        CopilotRuntimeIdentity runtimeIdentity = new(
            @"C:\Synthetic\StudyReportEvaluator-win-x64\runtimes\win-x64\native\copilot.exe",
            "1.0.79",
            new string('B', 64),
            "1.0.11+synthetic");
        ExecutionAuthenticationSnapshot authenticationSnapshot = new(
            ExecutionAuthenticationState.Available,
            [U04TestSupport.Model("gpt-5"), U04TestSupport.Model("auto")],
            runtimeIdentity);
        ScreenshotFinalizer finalizer = new();
        RecordingRunBoundary runBoundary = new(async (request, progress, cancellationToken) =>
        {
            Assert.Equal(SafeInputPath, request.InputPath);
            Assert.Equal(SafeOutputDirectory, request.OutputDirectory);
            Assert.True(request.UseDurableWorkflow);
            Assert.Null(request.ResumePartialPath);
            AssertStableDefinition(request.DraftDefinition);
            // The real orchestrator consumes the request made by Execution, but
            // every AI and file-output boundary below is explicitly synthetic.
            RunSummary summary = await CreateDurableSummaryAsync(
                request, runtimeIdentity, finalizer, cancellationToken);
            progress?.Invoke(new EvaluationProgress(
                summary.PlannedOperationCount,
                summary.CompletedOperationCount,
                0,
                EvaluationProgressStatus.Completed));
            return summary;
        });
        RecordingAuthenticationBoundary authentication = new(authenticationSnapshot);
        ScreenshotLoginResolver loginResolver = new();
        ExecutionViewModel execution = new(
            authentication,
            runBoundary,
            new BundledCopilotLoginService(loginResolver,
                _ => throw new InvalidOperationException("Documentation captures must never start a CLI process.")));
        RecordingOutputBoundary output = new()
        {
            // A fake finalizer receipt owns the original final name. Only the
            // separate synthetic candidate is ready; do not blindly accept every path.
            Assess = (inputPath, outputPath) => inputPath == SafeInputPath && outputPath == SafeFinalPath
                ? new ResultsOutputPathAssessment(ResultsOutputStatusCodes.TargetExists, isValid: false, targetExists: true)
                : inputPath == SafeInputPath && outputPath == SafeReviewedPath
                    ? ResultsOutputPathAssessment.Valid
                    : new ResultsOutputPathAssessment(ResultsOutputStatusCodes.OutputPathInvalid, isValid: false, targetExists: false),
            Export = (_, _) => throw new InvalidOperationException("Documentation captures must not export a workbook."),
        };
        // Synthetic student answers: documentation captures never read a real input workbook.
        ResultsOutputViewModel results = new(output, answerSource: new ScreenshotAnswerSource());
        MainWindowViewModel viewModel = new(
            new WorkflowNavigator(),
            input,
            new QuantificationDesignViewModel(input.DefinitionDraft, input.AvailableColumnNames),
            execution,
            results,
            settingsStore: null); // No user-data path resolution, initialization, load or save.
        MainWindow window = new(viewModel)
        {
            Width = width,
            Height = height,
        };

        try
        {
            window.Show();
            Render();
            Assert.Equal(new Size(width, height), window.ClientSize);
            Assert.Equal(1d, window.RenderScaling);
            Assert.Equal(WorkflowStep.Input, viewModel.CurrentStep);
            InputView inputView = CurrentView<InputView>(window);
            Assert.Equal(SafeInputPath, Required<TextBox>(inputView, "FilePathTextBox").Text);
            Assert.Contains("100 行", Required<TextBlock>(inputView, "InputCountSummary").Text, StringComparison.Ordinal);
            Assert.Equal(2, Required<ListBox>(inputView, "InputQuestionList").Items.Count);
            if (!executionOnly)
            {
                Capture(window, imageDirectory, ScreenshotFileNames[0], Required<Button>(inputView, "PickFileButton"));
            }

            Required<ComboBox>(inputView, "QuestionSelector").SelectedItem = input.Questions[1];
            Activate(Required<Button>(inputView, "MappingDetailsButton"));
            SettingsView settingsView = CurrentView<SettingsView>(window);
            Assert.Equal(SettingsCategory.Mapping, viewModel.Settings.SelectedCategory);
            Assert.Equal(WorkflowStep.Input, viewModel.CurrentStep);
            MappingSettingsView mappingView = CurrentSettingsView<MappingSettingsView>(settingsView);
            Assert.Same(input, mappingView.DataContext);
            Assert.Equal(QuestionIds[1], input.SelectedQuestion?.Id);
            Assert.Equal(QuestionIds[1], viewModel.DesignViewModel.SelectedQuestion?.Id);
            ListBox supportingColumns = Required<ListBox>(mappingView, "SupportingColumnList");
            supportingColumns.SelectedItem = input.Questions[1].SupportingColumns.Single(column => column.ColumnName == "D");
            Required<ComboBox>(mappingView, "CandidateSelector").SelectedItem =
                input.MappingSuggestions.Single(candidate => candidate.ColumnName == "C");
            Render();
            Assert.True(Required<TextBox>(mappingView, "QuestionTextEditor").IsReadOnly);
            Assert.Equal("C", input.SelectedQuestion?.PrimarySourceColumn);
            Assert.Equal("D", input.SelectedQuestion?.SupportingSummary);
            Assert.True(Required<CheckBox>(mappingView, "IncludeSupportingColumnCheckBox").IsChecked);
            AssertContainedInViewport(Required<TextBox>(mappingView, "QuestionNameEditor"), window);
            AssertContainedInViewport(Required<TextBox>(mappingView, "CandidateOverview"), window);
            if (!executionOnly)
            {
                Capture(window, imageDirectory, ScreenshotFileNames[1], Required<Button>(settingsView, "SettingsRequestClose"));
            }

            Activate(Required<Button>(settingsView, "SettingsRequestClose"));
            Assert.Same(inputView, CurrentView<InputView>(window));
            Activate(Required<Button>(window, "NextStepButton"));
            Assert.Equal(WorkflowStep.Design, viewModel.CurrentStep);
            QuantificationDesignViewModel design = viewModel.DesignViewModel;
            design.DefinitionName = "100名・2設問 レポート定量化";
            design.Revision = "2026-09";
            design.RoundingDigits = 1;
            design.BasePoints = 60m;
            design.SpecialPoints = 0m;
            design.SimilarityPenaltyWeight = 0.1m;
            ConfigureKnowledgeQuestion(design.Questions[0]);
            ConfigureCustomQuestion(design.Questions[1]);
            Assert.True(design.IsAllocationValid);
            Assert.False(design.HasTechnicalErrors);

            QuestionDesignItemViewModel knowledgeQuestion = design.Questions[0];
            EvaluatorDesignItemViewModel knowledgeEvaluator = knowledgeQuestion.Evaluators[0];
            knowledgeQuestion.SelectedEvaluator = knowledgeEvaluator;
            QuantificationDesignView designView = CurrentView<QuantificationDesignView>(window);
            Border formulaGuide = Required<Border>(designView, "FormulaGuide");
            ListBox questionCards = Required<ListBox>(designView, "QuestionEditorList");
            questionCards.SelectedItem = knowledgeQuestion;
            Render();
            Assert.Same(knowledgeQuestion, design.SelectedQuestion);
            Assert.True(knowledgeEvaluator.IsKnowledge);
            TextBlock knowledgeSummary = Required<TextBlock>(designView, "EvaluatorSummaryText");
            Assert.Contains("Knowledge", knowledgeSummary.Text, StringComparison.Ordinal);
            AssertContainedInViewport(knowledgeSummary, window);
            // Prompt editors moved to Settings; do not search for an off-tree editor.
            Assert.DoesNotContain(designView.GetVisualDescendants().OfType<TextBox>(), control =>
                AutomationProperties.GetAutomationId(control) == knowledgeEvaluator.KnowledgePromptPreviewAutomationId);
            Assert.Equal(
                2,
                designView.GetVisualDescendants().OfType<Border>().Count(border =>
                    (AutomationProperties.GetAutomationId(border) ?? string.Empty).StartsWith(
                        "DesignQuestion-",
                        StringComparison.Ordinal)));
            AssertContainedInViewport(formulaGuide, window);
            foreach (Border card in designView.GetVisualDescendants().OfType<Border>().Where(border =>
                         (AutomationProperties.GetAutomationId(border) ?? string.Empty).StartsWith(
                             "DesignQuestion-",
                             StringComparison.Ordinal)))
            {
                AssertContainedInViewport(card, window);
            }
            if (!executionOnly)
            {
                Capture(window, imageDirectory, ScreenshotFileNames[2], Required<Button>(designView, "OpenEvaluatorSettingsButton"));
            }

            QuestionDesignItemViewModel customQuestion = design.Questions[1];
            EvaluatorDesignItemViewModel customEvaluator = customQuestion.Evaluators[0];
            questionCards.SelectedItem = customQuestion;
            customQuestion.SelectedEvaluator = customEvaluator;
            Activate(Required<Button>(designView, "OpenEvaluatorSettingsButton"));
            settingsView = CurrentView<SettingsView>(window);
            Assert.Equal(SettingsCategory.Evaluation, viewModel.Settings.SelectedCategory);
            Assert.Equal(WorkflowStep.Design, viewModel.CurrentStep);
            EvaluatorSettingsView evaluatorView = CurrentSettingsView<EvaluatorSettingsView>(settingsView);
            Assert.Same(design, evaluatorView.DataContext);
            Assert.Equal(QuestionIds[1], design.SelectedQuestion?.Id);
            Assert.Equal(QuestionIds[1], input.SelectedQuestion?.Id);
            TabControl promptTabs = Required<TabControl>(evaluatorView, "EditorTabs");
            promptTabs.SelectedItem = ById<TabItem>(evaluatorView, "EvaluatorSettingsPromptTab");
            Render();
            Assert.Equal(1, promptTabs.SelectedIndex);
            TextBox customPrompt = ById<TextBox>(evaluatorView, customEvaluator.PromptAutomationId);
            TextBox customPreview = ById<TextBox>(evaluatorView, customEvaluator.CustomPromptPreviewAutomationId);
            Assert.False(customPrompt.IsReadOnly);
            Assert.True(customPreview.IsReadOnly);
            Assert.Equal(customEvaluator.CustomPromptTemplate, customPrompt.Text);
            Assert.Equal(customEvaluator.PromptPreview, customPreview.Text);
            AssertContainedInViewport(customPrompt, window);
            AssertContainedInViewport(customPreview, window);
            if (!executionOnly)
            {
                Capture(window, imageDirectory, ScreenshotFileNames[3], Required<Button>(settingsView, "SettingsRequestClose"));
            }

            Activate(Required<Button>(settingsView, "SettingsRequestClose"));
            Assert.Same(designView, CurrentView<QuantificationDesignView>(window));
            Activate(Required<Button>(window, "NextStepButton"));
            Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
            execution.MaxConcurrency = 2;
            Assert.Equal(402, execution.PlannedEvaluationCount);
            ExecutionView executionView = CurrentView<ExecutionView>(window);
            Assert.Null(executionView.FindControl<CheckBox>("ResumeModeCheckBox"));
            TextBox effectiveOutput = ById<TextBox>(executionView, "ExecutionEffectiveOutputDirectory");
            Assert.Same(Required<TextBox>(executionView, "EffectiveOutputDirectoryTextBox"), effectiveOutput);
            Assert.True(effectiveOutput.IsReadOnly);
            Assert.Equal(SafeOutputDirectory, effectiveOutput.Text);
            ResetScroll(Required<ScrollViewer>(window, "ShellScrollViewer"));
            ResetScroll(Required<ScrollViewer>(executionView, "ExecutionProgressScroll"));
            Render();

            // This is the synthetic, not-yet-checked state, not evidence of live login.
            // The login parts now live in the Input step; inspect them on a detached panel.
            ExecutionPreparationPanel preparation = new() { DataContext = execution };
            Window preparationWindow = new() { Width = 540, Height = 400, Content = preparation };
            preparationWindow.Show();
            Render();
            Button checkAuthentication = Required<Button>(preparation, "CheckAuthenticationButton");
            Button login = Required<Button>(preparation, "StartCopilotLogin");
            Button cancelLogin = Required<Button>(preparation, "CancelCopilotLogin");
            TextBlock loginStatus = Required<TextBlock>(preparation, "CopilotLoginStatus");
            Assert.Equal(ExecutionAuthenticationState.NotChecked, execution.AuthenticationState);
            Assert.False(execution.IsAuthenticationAvailable);
            TextBlock effortStatus = ById<TextBlock>(executionView, "ExecutionReasoningEffort");
            Assert.Equal("effort\n未選択", effortStatus.Text);
            Assert.Empty(execution.AvailableModelIds);
            Assert.Null(execution.SelectedModelId);
            Assert.False(execution.IsLoggingIn);
            Assert.Null(execution.LastLoginTask);
            Assert.Equal(0, authentication.CallCount);
            Assert.Equal(0, runBoundary.CallCount);
            Assert.Equal(0, loginResolver.CallCount);
            Assert.Equal(0, finalizer.CallCount);
            Assert.Equal(0, output.ExportCount);
            Assert.Same(execution.LoginCommand, login.Command);
            Assert.Equal("GitHubにログイン", login.Content);
            Assert.True(checkAuthentication.IsEffectivelyEnabled);
            Assert.True(login.IsEffectivelyEnabled);
            Assert.False(cancelLogin.IsEffectivelyEnabled);
            Assert.False(Required<Button>(executionView, "StartRunButton").IsEffectivelyEnabled);
            Assert.Equal("GitHub へのログインは開始していません。", execution.LoginStatusText);
            Assert.Equal(execution.LoginStatusText, loginStatus.Text);
            Assert.Equal(loginStatus.Text, AutomationProperties.GetName(loginStatus));
            Assert.Empty(Required<StackPanel>(preparation, "CopilotLoginPanel")
                .GetVisualDescendants().OfType<TextBox>());
            foreach (Control control in new Control[] { checkAuthentication, login, cancelLogin, loginStatus })
            {
                Assert.True(control.IsEffectivelyVisible);
            }

            preparationWindow.Close();
            foreach (Control control in new Control[] { effortStatus, effectiveOutput })
            {
                Assert.True(control.IsEffectivelyVisible);
                AssertContainedInViewport(control, window);
            }

            Capture(window, imageDirectory, ScreenshotFileNames[4], Required<Button>(executionView, "ChangeExecutionSettingsButton"));
            if (executionOnly)
            {
                return;
            }

            // Capture 08 before any fake authentication/run, using the real Common
            // navigation. No store is injected: the visible disabled Save and status
            // explain that editing has NOT created or saved a setting.txt file.
            Activate(Required<Button>(executionView, "ChangeExecutionSettingsButton"));
            settingsView = CurrentView<SettingsView>(window);
            Assert.Equal(SettingsCategory.Common, viewModel.Settings.SelectedCategory);
            Assert.Equal(WorkflowStep.Execution, viewModel.CurrentStep);
            TabControl commonTabs = CurrentSettingsView<TabControl>(settingsView);
            Assert.Equal("SettingsCommonTabs", AutomationProperties.GetAutomationId(commonTabs));
            Assert.Equal(0, commonTabs.SelectedIndex);
            Assert.True(viewModel.Settings.HasUnsavedChanges);
            Assert.False(viewModel.Settings.CanSave);
            Assert.False(Required<Button>(settingsView, "SettingsSaveButton").IsEffectivelyEnabled);
            Assert.False(Required<Button>(settingsView, "SettingsLoadRetry").IsEffectivelyEnabled);
            Assert.Equal("保存先が構成されていません。ファイルへの読込・保存は行いません。",
                Required<TextBlock>(settingsView, "SettingsStatus").Text);
            TextBox commonOutput = ById<TextBox>(settingsView, "SettingsEffectiveOutputDirectory");
            Assert.True(commonOutput.IsReadOnly);
            Assert.Equal(SafeOutputDirectory, commonOutput.Text);
            Assert.True(string.IsNullOrEmpty(ById<TextBox>(settingsView, "ExecutionOutputDirectory").Text));
            Assert.Equal(design.DefinitionName, ById<TextBox>(settingsView, "DesignDefinitionName").Text);
            Assert.Equal(design.Revision, ById<TextBox>(settingsView, "DesignRevision").Text);
            AssertContainedInViewport(ById<TextBox>(settingsView, "DesignRoundingDigits"), window);
            Assert.Equal(ExecutionAuthenticationState.NotChecked, execution.AuthenticationState);
            Assert.Equal(0, authentication.CallCount);
            Assert.Equal(0, runBoundary.CallCount);
            Capture(window, imageDirectory, ScreenshotFileNames[7], Required<Button>(settingsView, "SettingsRequestClose"));
            Activate(Required<Button>(settingsView, "SettingsRequestClose"));
            Assert.Same(executionView, CurrentView<ExecutionView>(window));

            // Only these explicit actions request fake authentication and a fake
            // run/finalizer receipt. Captions for 06/07 must never claim live login,
            // AI quality, input-file verification, or actual final workbook creation.
            await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            Assert.Equal("gpt-5", execution.SelectedModelId);
            Assert.True(execution.CanStart);
            await execution.StartAsync(TestContext.Current.CancellationToken);
            Render();
            Assert.Equal(1, authentication.CallCount);
            Assert.Equal(1, runBoundary.CallCount);
            Assert.Equal(1, finalizer.CallCount);
            Assert.Equal(0, loginResolver.CallCount);
            Assert.Null(execution.LastLoginTask);
            ExecutionRunContext context = Assert.IsType<ExecutionRunContext>(execution.LastRunContext);
            Assert.Equal(ScreenshotUtc, context.Summary.StartedAtUtc);
            Assert.Equal(ScreenshotUtc, context.Summary.EndedAtUtc);
            Assert.Equal(QuantificationRunStatusCodes.Success, context.Summary.StatusCode);
            Assert.Equal(AtomicOutputStatusCodes.Success, context.Summary.FinalizationCode);
            Assert.Equal(100, context.Summary.CompletedRows.Length);
            Assert.Equal(2, context.Summary.References.Length);
            Assert.True(results.IsLoaded);
            // MainWindow's actual RunCompleted handler moves Execution to Results.
            Assert.Equal(WorkflowStep.Results, viewModel.CurrentStep);
            ResultsOutputView resultsView = CurrentView<ResultsOutputView>(window);
            Assert.Equal(402, results.PlannedEvaluationCount);
            Assert.Equal(402, results.CompletedEvaluationCount);
            Assert.Equal(0, results.FailureCount);
            Assert.Equal(0, results.CancelledCount);
            Assert.Equal(200, results.Results.Count);
            Assert.Equal(100, results.RowScores.Count);
            Assert.False(results.IsPartial);
            Assert.True(results.IsAutomaticOutput);
            Assert.Equal(SafeFinalPath, results.FinalPath);
            Assert.Equal(SafeFinalPath, results.OutputPath);
            string timestamp = ScreenshotUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            Assert.Equal($"入力: StudyReport-100x2.xlsx · 開始: {timestamp} · 終了: {timestamp}",
                ById<TextBlock>(resultsView, "ResultsRunIdentity").Text);
            // P-01, the first-run persona, shows the detail beside the list (NFR-UX-009, AS-009).
            Assert.True(results.IsDetailVisible);
            Assert.True(Required<ListBox>(resultsView, "RowScoreList").IsEffectivelyVisible);
            Assert.True(Required<ListBox>(resultsView, "ResultsList").IsEffectivelyVisible);
            Assert.False(results.HasUnsavedOverrides);
            Assert.True(results.OutputTargetExists); // Synthetic original final cannot be overwritten.
            Assert.False(results.CanExport);
            Assert.All(results.RowScores, row =>
            {
                Assert.Equal(ResultsRowStatus.Success, row.Status);
                // Real preview arithmetic over fake raw 8: 60 + 16 + 16 - .4 - .4.
                Assert.Equal(92.0m, row.FinalScore);
            });
            ResetScroll(Required<ScrollViewer>(window, "ShellScrollViewer"));
            ResetScroll(Required<ScrollViewer>(resultsView, "ResultsOutputScrollViewer"));
            Render();
            AssertContainedInViewport(Required<ListBox>(resultsView, "RowScoreList"), window);
            Capture(window, imageDirectory, ScreenshotFileNames[5], Required<Button>(resultsView, "NextPageButton"));

            Assert.False(Required<Button>(resultsView, "ShowDetailButton").IsEffectivelyVisible);
            Assert.True(results.IsDetailVisible);
            ResultsCriterionViewModel firstResult = Assert.Single(results.SelectedRowCriteria,
                item => item.SourceRowNumber == 2 && item.QuestionId == QuestionIds[0]);
            Required<ListBox>(resultsView, "ResultsList").SelectedItem = firstResult;
            Render();
            TextBox overrideEditor = ById<TextBox>(resultsView, firstResult.AutomationId + "-Override");
            overrideEditor.Text = "9";
            TextBox outputCandidate = Required<TextBox>(resultsView, "OutputPathTextBox");
            outputCandidate.Text = SafeReviewedPath;
            Render();
            Assert.Same(firstResult, results.SelectedCriterion);
            Assert.Equal(ResultsAnswerState.Loaded, firstResult.StudentAnswerState);
            Assert.Equal("Synthetic reason", firstResult.ReasonText);
            Assert.Equal("9", firstResult.OverrideText);
            Assert.Equal(8m, firstResult.AiRawScore);
            Assert.Equal(9m, firstResult.EffectiveRaw);
            Assert.Equal(94.0m, results.SelectedRow?.FinalScore);
            Assert.True(results.HasUnsavedOverrides);
            Assert.False(results.HasOverrideErrors);
            Assert.Equal(SafeReviewedPath, results.OutputPath);
            Assert.Equal(SafeFinalPath, results.FinalPath);
            Assert.NotEqual(results.FinalPath, results.OutputPath);
            Assert.False(results.HasSuccessfulExport);
            Assert.Equal(string.Empty, results.LastSuccessfulExportPath);
            Assert.Equal(0, output.ExportCount);
            Assert.Null(output.LastRequest);
            Assert.True(results.CanExport); // Fake candidate assessment, not a file-creation receipt.
            AssertContainedInViewport(overrideEditor, window);
            AssertContainedInViewport(outputCandidate, window);
            AssertContainedInViewport(ById<TextBlock>(resultsView, "ResultsUnsavedOverrides"), window);
            Capture(window, imageDirectory, ScreenshotFileNames[6], Required<Button>(resultsView, "ShowListButton"));
        }
        finally
        {
            window.Close();
        }
    }

    private static void ConfigureKnowledgeQuestion(QuestionDesignItemViewModel question)
    {
        question.DisplayName = "設問1：機械学習の基礎";
        question.QuestionText = "機械学習とルールベースの違いを説明してください。";
        question.Points = 20m;
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[0];
        evaluator.DisplayName = "Knowledge coverage";
        evaluator.Minimum = 0m;
        evaluator.Maximum = 10m;
        CriterionDesignItemViewModel criterion = evaluator.Criteria[0];
        criterion.DisplayName = "概念の説明・関係・適用";
        criterion.Description = "主要概念を説明し、相互関係と具体的な適用に触れているか。";
        criterion.Weight = 1m;
    }

    private static void ConfigureCustomQuestion(QuestionDesignItemViewModel question)
    {
        question.DisplayName = "設問2：活用方法の説明";
        question.QuestionText = "学習した手法の活用例を説明してください。";
        question.Points = 20m;
        EvaluatorDesignItemViewModel evaluator = question.Evaluators[0];
        evaluator.Type = EvaluatorType.CustomPrompt;
        evaluator.DisplayName = "説明品質";
        evaluator.Minimum = 0m;
        evaluator.Maximum = 10m;
        evaluator.CustomPromptTemplate = """
            次の回答を、具体性と論理性の観点から評価してください。

            ### 回答
            {回答}

            ### 補助情報
            {補助情報}

            ### 評価項目
            {評価項目}
            """;
        CriterionDesignItemViewModel criterion = evaluator.Criteria[0];
        criterion.DisplayName = "具体性と論理性";
        criterion.Description = "活用例が具体的で、説明の流れが論理的か。";
        criterion.Weight = 1m;
    }

    private static async Task<RunSummary> CreateDurableSummaryAsync(
        QuantificationRunRequest request,
        CopilotRuntimeIdentity runtimeIdentity,
        ScreenshotFinalizer finalizer,
        CancellationToken cancellationToken)
    {
        ScriptedRowSource rows = new((rowRequest, _) =>
        {
            IEnumerable<KeyValuePair<string, string?>> cells = rowRequest.SelectedColumns.Select(
                column => new KeyValuePair<string, string?>(
                    column,
                    column switch
                    {
                        "B" => "合成回答：教師あり学習は正解例から規則性を学びます。",
                        "C" => "合成回答：需要予測へ適用し、誤差を検証して改善します。",
                        "D" => "合成補助情報：前提、手順、評価指標を明示します。",
                        _ => "合成値",
                    }));
            return Task.FromResult(new EvaluationRowData(
                rowRequest.SourceRowNumber,
                cells));
        });
        ScriptedRunner normalRunner = new((payload, _, _) => Task.FromResult(
            EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, _ => 8m))));
        DurableQuantificationOrchestrator orchestrator = new(
            rows,
            normalRunner,
            new ScreenshotReferenceRunner(),
            new ScreenshotSpecialRunner(),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot()),
            new ScreenshotCheckpointStore(),
            new ScreenshotPathPlanner(),
            finalizer,
            new ScreenshotCleaner(),
            new ScreenshotTimeProvider());
        return await orchestrator.RunAsync(
            new DurableQuantificationRunRequest
            {
                Run = request,
                Runtime = new CheckpointRuntimeIdentity
                {
                    ApplicationIdentity = "StudyReportEvaluator.App/4.1-synthetic",
                    CliVersion = runtimeIdentity.CliVersion,
                    CliSha256 = runtimeIdentity.CliSha256,
                    SdkInformationalVersion = runtimeIdentity.SdkInformationalVersion,
                },
                OutputDirectory = request.OutputDirectory,
            },
            cancellationToken: cancellationToken);
    }

    private sealed class ScreenshotReferenceRunner : IReferenceAnswerOperationRunner
    {
        public Task<AuxiliaryOperationResult<ReferenceAnswerResult>> EvaluateAsync(
            SafeReferenceAnswerPayload payload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(AuxiliaryOperationResult<ReferenceAnswerResult>.Succeeded(
                new ReferenceAnswerResult
                {
                    QuestionId = payload.QuestionId,
                    Answer = "合成参照回答",
                }));
        }
    }

    private sealed class ScreenshotSpecialRunner : ISpecialEvaluationOperationRunner
    {
        public Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
            SafeSpecialEvaluationPayload payload,
            string modelId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(AuxiliaryOperationResult<SpecialQuantificationResult>.Succeeded(
                new SpecialQuantificationResult
                {
                    SpecialEvaluationId = payload.SpecialEvaluationId,
                    Score = 0.8m,
                    Reason = "合成固有評価",
                    Evidence = string.Empty,
                    EvidenceSource = EvidenceSourceKind.None,
                    EvidenceSourceColumnId = string.Empty,
                }));
        }
    }
    private sealed class ScreenshotAnswerSource : IResultsAnswerSource
    {
        public Task<ResultsAnswerReadResult> ReadAsync(
            ResultsAnswerRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ResultsAnswerReadResult.Loaded(request.Columns.Select(column =>
                new KeyValuePair<string, string?>(
                    column,
                    FormattableString.Invariant($"合成回答（Excel {request.SourceRowNumber} 行・列 {column}）。\n機械学習はデータから規則を学ぶ手法です。")))));
        }
    }

    private sealed class ScreenshotCheckpointStore : ICheckpointStore
    {
        private CheckpointEnvelope? current;

        public CheckpointSaveResult Create(
            CheckpointEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = envelope;
            return CheckpointSaveResult.Succeeded(createdNew: true);
        }

        public CheckpointSaveResult Update(
            CheckpointEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = envelope;
            return CheckpointSaveResult.Succeeded(createdNew: false);
        }

        public CheckpointLoadResult Load(
            string partialPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return current is null
                ? CheckpointLoadResult.Failed(CheckpointStatusCodes.Invalid)
                : CheckpointLoadResult.Succeeded(current);
        }
    }

    private sealed class ScreenshotPathPlanner : IOutputPathPlanner
    {
        public OutputPathReservation Reserve(
            string inputPath,
            DateTimeOffset localTime,
            string? outputDirectory = null)
        {
            Assert.Equal(SafeInputPath, inputPath);
            Assert.Equal(SafeOutputDirectory, outputDirectory);
            Assert.Equal(ScreenshotUtc, localTime);
            return OutputPathReservation.Create(
                SafeOutputDirectory, SafeFinalPath,
                @"C:\Synthetic\result\eval-20260902-1200.partial.xlsx");
        }
    }

    // Deliberately fake: SUCCESS is a synthetic UI receipt, never evidence of IO,
    // hash verification, package validation, atomic commit, or a published file.
    private sealed class ScreenshotFinalizer : IDurableRunFinalizer
    {
        public int CallCount { get; private set; }

        public DurableFinalizationResult Finalize(
            RunSummary summary,
            CheckpointEnvelope checkpoint,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Assert.Equal(SafeFinalPath, checkpoint.FinalPath);
            return DurableFinalizationResult.Succeeded(checkpoint.FinalPath);
        }
    }

    private sealed class ScreenshotCleaner : IPartialCheckpointCleaner
    {
        public bool TryDelete(string partialPath) => true;
    }

    private sealed class ScreenshotTimeProvider : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        // A fixture timestamp, not a measured duration. Scheduling cannot change it.
        public override DateTimeOffset GetUtcNow() => ScreenshotUtc;
    }

    private sealed class ScreenshotLoginResolver : ICopilotCliPathResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult<string?>(null);
        }
    }

    private static void Capture(
        MainWindow window,
        string imageDirectory,
        string fileName,
        Button focusTarget)
    {
        // Drain posted category/step focus first, then use a real non-editing
        // control. Never disable caret rendering or replace/control the view tree.
        Render();
        Assert.True(focusTarget.IsEffectivelyVisible && focusTarget.IsEffectivelyEnabled);
        Assert.True(focusTarget.Focus(NavigationMethod.Tab));
        Render();
        Assert.Same(focusTarget, window.FocusManager?.GetFocusedElement());
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBox>(), editor => editor.IsFocused);
        Assert.Equal(new Size(window.Width, window.Height), window.ClientSize);
        Assert.Equal(1d, window.RenderScaling);
        AssertContainedInViewport(focusTarget, window);
        AssertContainedInViewport(Required<Grid>(window, "ShellHeader"), window);
        AssertContainedInViewport(Required<Grid>(window, "ShellFooter"), window);
        Control current = Assert.IsAssignableFrom<Control>(Required<ContentControl>(window, "CurrentStepContent").Content);
        Assert.Same(window.ViewModel.CurrentEditorViewModel, current.DataContext);
        AssertContainedInViewport(current, window);
        AssertStableDefinition(window.ViewModel.InputViewModel.DefinitionDraft);
        AssertStableDefinition(window.ViewModel.DesignViewModel.Draft);
        Assert.Equal(SafeInputPath, window.ViewModel.InputViewModel.FilePath);
        Assert.Equal(string.Empty, window.ViewModel.Settings.FilePath);
        Assert.Null(window.ViewModel.Settings.LoadStatus);
        Assert.Null(window.ViewModel.Settings.SaveStatus);
        Assert.Null(window.ViewModel.Settings.LastLoadTask);
        Assert.Null(window.ViewModel.Settings.LastSaveTask);
        Assert.Null(window.ViewModel.Settings.LastApplySavedDefinitionTask);
        foreach (Control control in window.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible))
        {
            string text = control switch
            {
                TextBox editor => editor.Text ?? string.Empty,
                TextBlock label => label.Text ?? string.Empty,
                _ => string.Empty,
            };
            Assert.DoesNotContain(Path.GetTempPath(), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(imageDirectory, text, StringComparison.OrdinalIgnoreCase);
        }

        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("Avalonia did not render a screenshot frame.");
        // Verify actual raster dimensions, not only requested window dimensions
        // or a derived pixel estimate. This also runs for the 1024 x 720 test.
        Assert.Equal(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height), frame.PixelSize);
        AssertNonUniformPixels(frame);
        string path = Path.Combine(imageDirectory, fileName);
        using FileStream stream = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Flush(flushToDisk: true);
    }

    private static void Render()
    {
        // Bounded layout/render frames for cached views and measured list capacity.
        for (int frame = 0; frame < 3; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Activate(Button button)
    {
        Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled);
        Assert.True(button.Focus(NavigationMethod.Tab));
        TopLevel window = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(button));
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Render();
    }

    private static T CurrentView<T>(MainWindow window) where T : Control
    {
        T view = Assert.IsType<T>(Required<ContentControl>(window, "CurrentStepContent").Content);
        Assert.Same(window.ViewModel.CurrentEditorViewModel, view.DataContext);
        return view;
    }

    private static T CurrentSettingsView<T>(SettingsView settings) where T : Control =>
        Assert.IsType<T>(Required<ContentControl>(settings, "CurrentSettingsContent").Content);

    private static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control =>
            control.IsEffectivelyVisible && AutomationProperties.GetAutomationId(control) == id);

    private static void AssertStableDefinition(QuantificationDefinition definition)
    {
        Assert.Equal("documentation-definition", definition.Id);
        Assert.Equal(1, definition.HeaderRow);
        Assert.Equal(2, definition.FirstDataRow);
        Assert.Equal(101, definition.LastDataRow);
        Assert.Equal(QuestionIds, definition.Questions.Select(question => question.Id));
        for (int index = 0; index < QuestionIds.Length; index++)
        {
            QuestionDefinition question = definition.Questions[index];
            EvaluatorDefinition evaluator = Assert.Single(question.Evaluators);
            Assert.Equal($"documentation-evaluator-{index + 1}", evaluator.Id);
            Assert.Equal($"documentation-criterion-{index + 1}", Assert.Single(evaluator.Criteria).Id);
            Assert.Empty(question.SpecialEvaluations);
        }
    }

    private static void AssertCaptureSet(string directory, int width, int height)
    {
        Assert.Equal(ScreenshotFileNames, Directory.GetFiles(directory)
            .Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));
        foreach (string fileName in ScreenshotFileNames)
        {
            AssertSavedCapture(Path.Combine(directory, fileName), width, height);
        }
    }

    private static void AssertSavedCapture(string path, int width, int height)
    {
        Assert.True(File.Exists(path), $"Missing documentation screenshot: {Path.GetFileName(path)}");
        Assert.True(new FileInfo(path).Length > 10_000,
            $"Documentation screenshot is unexpectedly small: {Path.GetFileName(path)}");
        using Bitmap bitmap = new(path);
        Assert.Equal(new PixelSize(width, height), bitmap.PixelSize);
    }

    private static void AssertNonUniformPixels(WriteableBitmap frame)
    {
        using var pixels = frame.Lock();
        Assert.Equal(frame.PixelSize, pixels.Size);
        Assert.Equal(32, pixels.Format.BitsPerPixel);
        Assert.True(pixels.RowBytes >= checked(pixels.Size.Width * sizeof(int)));
        int[] row = new int[pixels.Size.Width];
        HashSet<int> colors = [];
        // Ignore stride padding and color channel order; a blank frame must not
        // pass merely because its PNG metadata reports the expected dimensions.
        for (int y = 0; y < pixels.Size.Height && colors.Count < 16; y++)
        {
            Marshal.Copy(IntPtr.Add(pixels.Address, checked(y * pixels.RowBytes)), row, 0, row.Length);
            foreach (int pixel in row)
            {
                colors.Add(pixel);
            }
        }

        Assert.True(colors.Count >= 16, "The captured framebuffer must contain rendered UI, not a uniform placeholder.");
    }

    private static void ResetScroll(ScrollViewer scrollViewer) =>
        scrollViewer.Offset = default;

    private static void AssertContainedInViewport(Control control, TopLevel viewport)
    {
        Assert.True(control.IsEffectivelyVisible && control.Bounds.Width > 0d && control.Bounds.Height > 0d);
        Point origin = control.TranslatePoint(default, viewport)
            ?? throw new InvalidOperationException("The screenshot target position is unavailable.");
        Assert.True(
            origin.X >= 0d
                && origin.Y >= 0d
                && origin.X + control.Bounds.Width <= viewport.ClientSize.Width
                && origin.Y + control.Bounds.Height <= viewport.ClientSize.Height,
            $"{control.GetType().Name} is not fully contained in the screenshot viewport.");
    }

    private static T Required<T>(Control root, string name)
        where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

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

    private sealed class FixedInputWorkbookLoader(InputWorkbookLoadResult result)
        : IInputWorkbookLoader
    {
        public Task<InputWorkbookLoadResult> LoadAsync(
            string filePath,
            uint headerRow,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(SafeInputPath, filePath);
            Assert.Equal(1U, headerRow);
            return Task.FromResult(result);
        }
    }
}
