using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: NFR-UX-001 (AC-076)
public sealed class ResponsiveLayoutTests(ITestOutputHelper output)
{
    private const double NarrowWidth = 760d;
    private const double NarrowHeight = 600d;

    // Absorbs layout rounding only; anything larger would hide a real overflow.
    private const double OverflowTolerance = 1d;

    internal const string LongJapaneseName =
        "設問1：機械学習とルールベースの違いを、具体例と評価指標を含めて詳細に説明してください（長文の表示名）";

    internal static readonly string LongText = string.Join("\n", Enumerable.Repeat(LongJapaneseName, 80)) + "\n全文終端-T26";
    internal static readonly string LongPath = Path.Combine(Path.GetTempPath(),
        string.Join(Path.DirectorySeparatorChar.ToString(), Enumerable.Repeat("合成の長い出力先 directory", 12)),
        "synthetic-reviewed.xlsx");

    [AvaloniaTheory]
    [InlineData(1024d, 720d)]
    [InlineData(1180d, 800d)]
    public async Task Normal_shell_contains_every_step_without_body_scroll_at_actual_client_size(double width, double height)
    {
        using WorkflowLayoutFixture fixture = await WorkflowLayoutFixture.CreateAsync();
        MainWindow window = fixture.Window;
        Assert.Equal(1180d, window.Width);
        Assert.Equal(800d, window.Height);
        Assert.Equal(1024d, window.MinWidth);
        Assert.Equal(720d, window.MinHeight);
        window.Width = width;
        window.Height = height;
        window.Show();
        Render();
        Assert.Equal(new Size(width, height), window.ClientSize);
        Rect[] fixedRegions = AssertFixedRegions(window);

        foreach (WorkflowStep step in Enum.GetValues<WorkflowStep>())
        {
            Assert.Equal(step, fixture.Shell.CurrentStep);
            AssertNormalBody(window);
            Assert.Equal(fixedRegions, AssertFixedRegions(window));
            RecordLayout(output, step.ToString(), window);
            if (step != WorkflowStep.Results)
            {
                Activate(Required<Button>(window, "NextStepButton"));
            }
        }

        fixture.AssertPassive();
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task Input_view_reaches_the_last_question_page_at_a_narrow_viewport(double scale)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputView view = new(input);
        Window window = Host(view, scale: scale);

        try
        {
            Assert.NotEmpty(input.Questions);
            AssertFitsHorizontally(view, "input view");
            GoToLastPage(view, "NextPageButton", input.Questions.Count);
            Assert.Same(input.VisibleQuestions, Required<ListBox>(view, "InputQuestionList").ItemsSource);
            AssertPageRows(Required<ListBox>(view, "InputQuestionList"), input.Questions, input.PageIndex, input.PageSize);
            Assert.Same(input.Questions[^1], input.VisibleQuestions[^1]);
            SelectLastItem(window, Required<ListBox>(view, "InputQuestionList"));
            Assert.Same(input.Questions[^1], input.SelectedQuestion);
            AssertFullyInside(Required<Border>(view, "InputValidationSummary"), view);
            RecordLayout(output, "standalone-input", window);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(2d)]
    public void Design_view_reaches_the_last_question_page_at_a_narrow_viewport(double scale)
    {
        QuantificationDesignViewModel design = new(ManyItemDefinition());
        design.SelectedQuestion = design.Questions[0];
        design.Questions[0].SelectedEvaluator = design.Questions[0].Evaluators[0];
        QuantificationDesignView view = new(design);
        Window window = Host(view, scale: scale);

        try
        {
            Assert.True(design.Questions.Count > design.PageSize);
            AssertFitsHorizontally(view, "design view");
            GoToLastPage(view, "NextQuestionPageButton", design.Questions.Count);
            Assert.Same(design.VisibleQuestions, Required<ListBox>(view, "QuestionEditorList").ItemsSource);
            AssertPageRows(Required<ListBox>(view, "QuestionEditorList"), design.Questions, design.PageIndex, design.PageSize);
            SelectLastItem(window, Required<ListBox>(view, "QuestionEditorList"));
            Assert.Same(design.Questions[^1], design.SelectedQuestion);
            AssertScrollEndReachable(Required<ScrollViewer>(view, "QuantificationDesignScrollViewer"),
                Required<Button>(view, "ValidateDesignButton"));
            RecordLayout(output, "standalone-design", window);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(2d)]
    public void Execution_view_keeps_actions_reachable_with_local_scroll_at_a_narrow_viewport(double scale)
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 12);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        using ExecutionViewModel execution = U04TestSupport.ConfiguredExecutionViewModel(
            definition,
            metadata,
            new RecordingRunBoundary((_, _, _) => throw new InvalidOperationException("Layout must not start a run.")));
        execution.OutputDirectoryOverride = Path.GetDirectoryName(LongPath);
        execution.IsResumeMode = true;
        execution.ResumePartialPath = LongPath;
        ExecutionView view = new(execution);
        ExecutionPreparationPanel prep = new() { DataContext = execution };
        Window window = Host(view, scale: scale);
        Window prepWindow = Host(prep, scale: scale);

        try
        {
            AssertFitsHorizontally(view, "execution view");
            AssertFitsHorizontally(prep, "preparation panel");
            AssertScrollEndReachable(Required<ScrollViewer>(prep, "ExecutionAuthenticationScroll"),
                Required<TextBlock>(prep, "CopilotLoginInstructions"));
            AssertScrollEndReachable(Required<ScrollViewer>(view, "ExecutionProgressScroll"),
                Required<TextBlock>(view, "DurableProgressSummary"));
            AssertFullyInside(Required<Grid>(view, "ExecutionActions"), view);
            AssertTextEndReachable(window, Required<TextBox>(view, "EffectiveOutputDirectoryTextBox"));
            AssertTextEndReachable(prepWindow, Required<TextBox>(prep, "ResumePartialPathTextBox"));
            Assert.Null(execution.LastLoginTask);
            Assert.Null(execution.LastRunContext);
            RecordLayout(output, "standalone-execution", window);
        }
        finally
        {
            prepWindow.Close();
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1d)]
    [InlineData(2d)]
    public async Task Results_view_reaches_last_row_and_last_criterion_at_a_narrow_viewport(double scale)
    {
        using ResultsOutputViewModel results = await CreateResultsAsync(100, criterionCount: 12);
        ResultsOutputView view = new(results);
        Window window = Host(view, scale: scale);

        try
        {
            Assert.Equal(100, results.RowScores.Count);
            AssertFitsHorizontally(view, "results view");
            ListBox rowScoreList = Required<ListBox>(view, "RowScoreList");
            GoToLastPage(view, "NextPageButton", results.RowScores.Count);
            AssertPageRows(rowScoreList, results.RowScores, results.PageIndex, results.PageSize);
            SelectLastItem(window, rowScoreList);
            Assert.Equal(101, results.SelectedRow?.SourceRowNumber);
            Activate(Required<Button>(view, "ShowDetailButton"), Key.Space);
            Assert.True(results.IsDetailVisible);
            ListBox reviewList = Required<ListBox>(view, "ResultsList");
            SelectLastItem(window, reviewList);
            Assert.Same(results.SelectedRowCriteria[^1], results.SelectedCriterion);
            AssertScrollEndReachable(ListScroll(reviewList),
                Assert.IsType<ListBoxItem>(reviewList.ContainerFromIndex(reviewList.Items.Count - 1)), requireOverflow: true);
            // The old full-row editor scroll is now the selected criterion's local scroll.
            TextBlock lastValue = ById<TextBlock>(view, "ResultsCriterionOverall");
            AssertScrollEndReachable(lastValue.GetVisualAncestors().OfType<ScrollViewer>().First(), lastValue);
            AssertScrollEndReachable(Required<ScrollViewer>(view, "ResultsOutputScrollViewer"), Required<Button>(view, "ExportButton"));
            AssertFitsHorizontally(view, "last result criterion");
            RecordLayout(output, "standalone-results-detail", window);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Long_japanese_names_and_paths_do_not_widen_the_layout()
    {
        QuantificationDesignViewModel design = new(ManyItemDefinition());
        design.Questions[0].DisplayName = LongJapaneseName;
        design.SelectedQuestion = design.Questions[0];
        QuantificationDesignView designView = new(design);
        Window designWindow = Host(designView);

        try
        {
            AssertFitsHorizontally(designView, "design view with a long question name");
            TextBlock[] longNames = designView.GetVisualDescendants()
                .OfType<TextBlock>()
                .Where(text => text.Text == LongJapaneseName)
                .ToArray();
            Assert.NotEmpty(longNames);
            Assert.All(longNames, longName =>
            {
                AssertControlFitsHorizontally(longName, designView, "long design question name");
            });
        }
        finally
        {
            designWindow.Close();
        }

        QuantificationDefinition definition = U04TestSupport.Definition(2, 6);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        ResultsOutputViewModel results = new(
            new RecordingOutputBoundary(),
            U04TestSupport.Context(summary))
        {
            OutputPath = @"C:\とても\長い\日本語の\出力先\ディレクトリ\階層\eval-20260905-1200-reviewed-with-a-very-long-name.xlsx",
        };
        results.Results[0].OverrideText = "この値は範囲外です";
        ResultsOutputView resultsView = new(results);
        Window resultsWindow = Host(resultsView);

        try
        {
            Assert.True(results.HasOverrideErrors);
            AssertFitsHorizontally(resultsView, "results view with a long output path");
            TextBox outputPath = Required<TextBox>(resultsView, "OutputPathTextBox");
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, outputPath.TextWrapping);
            AssertControlFitsHorizontally(outputPath, resultsView, "long results output path");
            outputPath.SetCurrentValue(TextBox.TextProperty, LongPath);
            AssertTextEndReachable(resultsWindow, outputPath);
            Activate(Required<Button>(resultsView, "ShowDetailButton"));
            ResultsCriterionViewModel firstResult = results.Results[0];
            Border resultCard = Assert.Single(
                resultsView.GetVisualDescendants().OfType<Border>(),
                border => AutomationProperties.GetAutomationId(border) == firstResult.AutomationId);
            AssertControlFitsHorizontally(resultCard, resultsView, "result review card");
        }
        finally
        {
            resultsWindow.Close();
            results.Dispose();
        }

        // An unsubmitted direct path is not read; changing it intentionally clears input metadata.
        InputView inputView = new(new InputViewModel { FilePath = LongPath });
        Window inputWindow = Host(inputView);
        try
        {
            AssertFitsHorizontally(inputView, "unsubmitted long input path");
            AssertTextEndReachable(inputWindow, Required<TextBox>(inputView, "FilePathTextBox"));
        }
        finally
        {
            inputWindow.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(100)]
    [InlineData(530)]
    public async Task Results_rows_stay_virtualized_after_the_responsive_reflow(int rowCount)
    {
        using ResultsOutputViewModel results = await CreateResultsAsync(rowCount);
        ResultsOutputView view = new(results);
        Window window = Host(view);

        try
        {
            Assert.Equal(rowCount, results.Results.Count);
            ListBox list = Required<ListBox>(view, "RowScoreList");
            Assert.Same(results.VisibleRowScores, list.ItemsSource);
            Assert.True(results.PageSize < rowCount);
            AssertPageRows(list, results.RowScores, results.PageIndex, results.PageSize);
            RecordLayout(output, $"virtualization-{rowCount}-first-page", window);
            Activate(Required<Button>(view, "NextPageButton"));
            AssertPageRows(list, results.RowScores, results.PageIndex, results.PageSize);
            RecordLayout(output, $"virtualization-{rowCount}-next-page", window);
            Required<TextBox>(view, "GoToRowTextBox").Text = (rowCount + 1).ToString(CultureInfo.InvariantCulture);
            Activate(Required<Button>(view, "GoToRowButton"));
            Assert.Equal(rowCount + 1, results.SelectedRow?.SourceRowNumber);
            AssertPageRows(list, results.RowScores, results.PageIndex, results.PageSize);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(),
                control => AutomationProperties.GetAutomationId(control) == "ResultsRow-2");
            RecordLayout(output, $"virtualization-{rowCount}-last-page", window);
            Activate(Required<Button>(view, "ShowDetailButton"));
            TextBox editor = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(),
                textBox => (AutomationProperties.GetAutomationId(textBox) ?? string.Empty).EndsWith("-Override", StringComparison.Ordinal));
            Assert.Same(results.SelectedCriterion, editor.DataContext);
            AssertFitsHorizontally(view, $"results view with {rowCount} synthetic rows");
            RecordLayout(output, $"virtualization-{rowCount}", window);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Startup_error_window_scrolls_vertically_and_fits_its_minimum_width()
    {
        StartupErrorWindow window = new(
            new LaunchOptionsException(LaunchErrorCodes.PromptLengthInvalid, "--prompt"))
        {
            Width = 420,
            Height = 220,
        };

        try
        {
            window.Show();
            Render();

            ScrollViewer scroll = Assert.Single(
                window.GetVisualDescendants().OfType<ScrollViewer>(),
                item => AutomationProperties.GetAutomationId(item) == "StartupErrorScrollViewer");
            Button close = Assert.Single(
                window.GetVisualDescendants().OfType<Button>(),
                item => AutomationProperties.GetAutomationId(item) == "CloseStartupError");
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Assert.True(close.IsEffectivelyVisible);
            AssertFitsHorizontally(window, "startup error window");

            StackPanel content = Assert.IsType<StackPanel>(scroll.Content);
            for (int index = 0; index < 8; index++)
            {
                content.Children.Insert(
                    content.Children.Count - 1,
                    new TextBlock
                    {
                        Text = $"追加の起動案内 {index + 1}",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    });
            }

            Render();
            AssertScrollEndReachable(scroll, close, requireOverflow: true);
            Point closeOrigin = close.TranslatePoint(default, scroll)
                ?? throw new InvalidOperationException("Unable to locate the close button in the startup error scroll viewer.");
            Assert.True(
                closeOrigin.Y >= -OverflowTolerance
                    && closeOrigin.Y + close.Bounds.Height <= scroll.Viewport.Height + OverflowTolerance,
                "The close button must be reachable at the vertical scroll endpoint.");
        }
        finally
        {
            window.Close();
        }
    }

    internal static QuantificationDefinition ManyItemDefinition(int questionCount = 13, bool longContent = false, int rowCount = 100)
    {
        QuantificationDefinition seed = U04TestSupport.Definition(2, rowCount + 1);
        return seed with
        {
            Name = longContent ? LongJapaneseName : seed.Name,
            BasePoints = 60m,
            Questions = [.. Enumerable.Range(1, questionCount).Select(index => seed.Questions[0] with
            {
                Id = $"Q{index}",
                DisplayName = longContent ? $"{index}: {LongJapaneseName}" : $"合成設問 {index}",
                QuestionText = longContent ? LongText : $"合成の設問文 {index}",
                Points = index == 1 ? 40m : 0m,
                Evaluators = [U01TestSupport.Evaluator($"E{index}", $"C{index}") with
                {
                    DisplayName = longContent ? LongJapaneseName : $"合成評価 {index}",
                    CustomPromptTemplate = longContent ? LongText + "\n{回答} {評価項目}" : "{回答} {評価項目}",
                    Criteria = [seed.Questions[0].Evaluators[0].Criteria[0] with
                    {
                        Id = $"C{index}", DisplayName = longContent ? LongJapaneseName : $"合成観点 {index}",
                        Description = longContent ? LongText : "合成の説明",
                    }],
                }],
                SpecialEvaluations = [new SpecialEvaluationDefinition
                {
                    Id = $"S{index}", DisplayName = longContent ? LongJapaneseName : $"合成固有 {index}",
                    PrimarySourceColumn = "B", SupportingSourceColumns = ["A"],
                    PromptTemplate = longContent ? LongText + "\n{回答}" : "{回答}",
                }],
            })],
        };
    }

    internal static async Task<ResultsOutputViewModel> CreateResultsAsync(int rowCount, int criterionCount = 1, bool longContent = false)
    {
        QuantificationDefinition definition = ManyItemDefinition(1, longContent, rowCount);
        QuestionDefinition question = definition.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        definition = definition with { Questions = [question with { Evaluators = [evaluator with
        {
            Criteria = [.. Enumerable.Range(1, criterionCount).Select(index => evaluator.Criteria[0] with { Id = $"C{index}" })],
        }] }] };
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, U01TestSupport.ValidateMapping(definition).Metadata);
        return new ResultsOutputViewModel(new RecordingOutputBoundary(), U04TestSupport.Context(summary));
    }

    internal static Window Host(Control view, double width = NarrowWidth, double height = NarrowHeight, double scale = 1d)
    {
        Window window = new()
        {
            Width = width,
            Height = height,
            WindowDecorations = WindowDecorations.None,
            Content = view,
        };
        window.Show();
        window.SetRenderScaling(scale);
        Render();
        Assert.Equal(new Size(width, height), window.ClientSize);
        Assert.Equal(scale, window.RenderScaling);
        return window;
    }

    internal static void AssertScrollEndReachable(ScrollViewer scrollViewer, Control last, bool requireOverflow = false)
    {
        Assert.Contains(scrollViewer, last.GetVisualAncestors());
        Assert.True(
            scrollViewer.Extent.Width <= scrollViewer.Viewport.Width + OverflowTolerance,
            $"Content extent {scrollViewer.Extent.Width:F1} exceeds viewport {scrollViewer.Viewport.Width:F1}.");
        if (requireOverflow)
        {
            Assert.True(scrollViewer.Extent.Height > scrollViewer.Viewport.Height,
                "This exception fixture must exercise actual vertical scroll, not merely allow it.");
            Assert.Contains(scrollViewer.GetVisualDescendants().OfType<ScrollBar>(),
                scrollBar => scrollBar.Orientation == Orientation.Vertical && scrollBar.IsEffectivelyVisible);
        }

        // Virtualized descendants can increase Extent while containers are realized.
        // Retry until both Extent and the terminal offset stabilize, with a finite failure bound.
        double previousExtent = -1d;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            scrollViewer.ScrollToEnd();
            Render();
            double currentBottom = Math.Max(0d, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            if (Math.Abs(scrollViewer.Extent.Height - previousExtent) <= OverflowTolerance
                && Math.Abs(scrollViewer.Offset.Y - currentBottom) <= OverflowTolerance)
            {
                break;
            }

            previousExtent = scrollViewer.Extent.Height;
        }

        // Avalonia 12.1.1 can handle BringIntoView at the inner scroll only.
        // Keep its terminal offset, then expose the entire target in each outer viewport.
        foreach (ScrollContentPresenter ancestor in scrollViewer.GetVisualAncestors().OfType<ScrollContentPresenter>())
        {
            ancestor.BringDescendantIntoView(last, new Rect(last.Bounds.Size));
            Render();
        }

        double expectedBottom = Math.Max(0d, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        Assert.Equal(expectedBottom, scrollViewer.Offset.Y, OverflowTolerance);
        AssertFullyInsideAncestors(last);
    }

    private static void AssertControlFitsHorizontally(
        Control control,
        Control root,
        string context)
    {
        Rect bounds = BoundsIn(control, root);
        double width = root is ScrollViewer scroll ? scroll.Viewport.Width : root.Bounds.Width;
        Assert.True(
            bounds.Left >= -OverflowTolerance && bounds.Right <= width + OverflowTolerance,
            $"The {context} extends from {bounds.Left:F1} to {bounds.Right:F1} "
                + $"within a {width:F1} DIP viewport.");
    }

    internal static void AssertFitsHorizontally(Control root, string context)
    {
        double available = root is TopLevel topLevel
            ? topLevel.ClientSize.Width
            : root.Bounds.Width;
        Assert.True(available > 0d, $"The layout pass produced no width for the {context}.");

        List<string> overflowing = [];
        foreach (Visual visual in root.GetVisualDescendants())
        {
            // TextBox's local text scrolling is permitted, but its frame must fit.
            // Inspect application controls and realized rows, not template text presenters.
            if (visual is not Control control
                || control.TemplatedParent is not null
                || !control.IsEffectivelyVisible
                || control.Bounds.Width <= 0d
                || control.GetVisualAncestors().TakeWhile(parent => !ReferenceEquals(parent, root)).Any(parent => parent is TextBox))
            {
                continue;
            }

            Rect bounds = BoundsIn(control, root);
            double left = bounds.Left;
            double right = bounds.Right;
            if (left < -OverflowTolerance || right > available + OverflowTolerance)
            {
                overflowing.Add($"{Describe(control)} left={left:F1} right={right:F1}");
            }

            // Fitting the window is insufficient if an intermediate clip hides a control.
            foreach (Control clip in control.GetVisualAncestors().OfType<Control>()
                .TakeWhile(parent => !ReferenceEquals(parent, root))
                .Where(parent => parent.ClipToBounds || parent is ScrollViewer))
            {
                AssertControlFitsHorizontally(control, clip, $"{context}: {Describe(control)}");
            }
        }

        Assert.True(
            overflowing.Count == 0,
            $"Controls overflow the {available:F0} DIP wide viewport in the {context}: "
                + string.Join("; ", overflowing.Take(8)));
    }

    internal static Control CurrentView(MainWindow window) =>
        Assert.IsAssignableFrom<Control>(Required<ContentControl>(window, "CurrentStepContent").Content);

    internal static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    internal static ScrollViewer ListScroll(ListBox list) => Assert.Single(
        list.GetVisualDescendants().OfType<ScrollViewer>(), scroll => ReferenceEquals(scroll.TemplatedParent, list));

    internal static void AssertFullyInside(Control control, Control parent)
    {
        Assert.True(control.IsEffectivelyVisible, Describe(control));
        Assert.True(double.IsFinite(control.Bounds.Width) && control.Bounds.Width > 0d, Describe(control));
        Assert.True(double.IsFinite(control.Bounds.Height) && control.Bounds.Height > 0d, Describe(control));
        Rect bounds = BoundsIn(control, parent);
        Size viewport = parent switch
        {
            TopLevel top => top.ClientSize,
            ScrollViewer scroll => scroll.Viewport,
            _ => parent.Bounds.Size,
        };
        Assert.True(bounds.Width > 0d && bounds.Height > 0d
            && bounds.Left >= -OverflowTolerance && bounds.Top >= -OverflowTolerance
            && bounds.Right <= viewport.Width + OverflowTolerance && bounds.Bottom <= viewport.Height + OverflowTolerance,
            $"{Describe(control)} bounds={bounds} are not fully inside {Describe(parent)} viewport={viewport} DIP.");
    }

    internal static void AssertFullyInsideAncestors(Control control)
    {
        Assert.True(control.Opacity > 0d, $"{Describe(control)} must not be transparent.");
        AssertFullyInside(control, Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control)));
        foreach (Control ancestor in control.GetVisualAncestors().OfType<Control>())
        {
            Assert.True(ancestor.Opacity > 0d, $"{Describe(control)} has a transparent ancestor: {Describe(ancestor)}.");
            AssertFullyInside(control, ancestor);
        }
    }

    internal static void AssertImportantTextFullyVisible(TextBlock text, TextWrapping wrapping = TextWrapping.Wrap,
        bool expectEllipsis = false, int maxLines = 0)
    {
        // Select mandatory state by name/VM state BEFORE this assertion. Never filter it
        // by IsEffectivelyVisible: hiding the required message must fail, not remove coverage.
        Assert.False(string.IsNullOrWhiteSpace(text.Text), $"{Describe(text)} must contain its state text.");
        AssertFullyInsideAncestors(text);
        Assert.Equal(wrapping, text.TextWrapping);
        Assert.Equal(expectEllipsis ? TextTrimming.CharacterEllipsis : TextTrimming.None, text.TextTrimming);
        Assert.Equal(maxLines, text.MaxLines);

        // Verified against pinned Avalonia 12.1.1 TextBlock/TextLayout: Arrange sets
        // MaxHeight and can discard trailing lines even when Text still contains them.
        // Measure at the actual (padding-deflated, layout-rounded) width, without a
        // height limit. Explicit ellipsis/MaxLines are retained only for designed summaries.
        TextLayout actual = text.TextLayout;
        Assert.True(double.IsFinite(actual.MaxWidth) && actual.MaxWidth > 0d, Describe(text));
        TextBlock measurement = new()
        {
            Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize,
            FontWeight = text.FontWeight, FontStyle = text.FontStyle, FontStretch = text.FontStretch,
            FontFeatures = text.FontFeatures, TextDecorations = text.TextDecorations,
            Foreground = text.Foreground, FlowDirection = text.FlowDirection, TextAlignment = text.TextAlignment,
            LineHeight = text.LineHeight, LineSpacing = text.LineSpacing, LetterSpacing = text.LetterSpacing,
            TextWrapping = wrapping, TextTrimming = text.TextTrimming, MaxLines = maxLines,
            UseLayoutRounding = false,
        };
        measurement.Measure(new Size(actual.MaxWidth, double.PositiveInfinity));
        using TextLayout complete = measurement.TextLayout;
        double availableHeight = text.Bounds.Height - text.Padding.Top - text.Padding.Bottom;
        double availableWidth = text.Bounds.Width - text.Padding.Left - text.Padding.Right;
        Assert.True(double.IsFinite(complete.Height) && complete.Height > 0d
            && complete.Height <= availableHeight + OverflowTolerance
            && actual.Height <= availableHeight + OverflowTolerance,
            $"{Describe(text)} needs {complete.Height:F2} DIP for its text, but has {availableHeight:F2} DIP; the last line must fit.");
        Assert.True(actual.WidthIncludingTrailingWhitespace <= availableWidth + OverflowTolerance,
            $"{Describe(text)} text must wrap or use its explicitly expected ellipsis inside {availableWidth:F2} DIP.");
        Assert.True(complete.TextLines.Count == actual.TextLines.Count,
            $"{Describe(text)} renders {actual.TextLines.Count} of {complete.TextLines.Count} required lines.");
        Assert.Equal(complete.TextLines.Select(line => (line.FirstTextSourceIndex, line.Length, line.HasCollapsed)),
            actual.TextLines.Select(line => (line.FirstTextSourceIndex, line.Length, line.HasCollapsed)));
        if (!expectEllipsis)
        {
            Assert.DoesNotContain(actual.TextLines, line => line.HasCollapsed);
            TextLine last = actual.TextLines[^1];
            Assert.True(last.FirstTextSourceIndex + last.Length >= text.Text!.Length,
                $"{Describe(text)} must lay out the complete state string, including its final characters.");
        }
    }

    private static void AssertImportantStateTexts(Control view)
    {
        void State(string id, TextWrapping wrapping = TextWrapping.Wrap, bool ellipsis = false, int maxLines = 0) =>
            AssertImportantTextFullyVisible(ById<TextBlock>(view, id), wrapping, ellipsis, maxLines);

        // Compact state that must be visible without scrolling. Long document/prompt
        // bodies retain their separate local-scroll checks; decorative/hidden alternatives
        // (e.g. PickerStatusMessage and SettingsDefinitionAvailability) are not mandatory.
        switch (view)
        {
            case InputView:
                State("InputLoadStatus");
                State("InputPageSummary");
                State("InputSummary");
                break;
            case QuantificationDesignView:
                State("DesignQuestionsPageSummary", TextWrapping.NoWrap, ellipsis: true);
                State("DesignEvaluatorSummary", ellipsis: true, maxLines: 2);
                State("DesignSpecialSummary", ellipsis: true, maxLines: 2);
                State("DesignStatus", TextWrapping.NoWrap, ellipsis: true);
                break;
            case ExecutionView:
                State("ExecutionReasoningEffort");
                State("ExecutionConcurrencySummary");
                AssertImportantTextFullyVisible(Required<TextBlock>(view, "OutputDirectorySource"));
                State("ExecutionValidationStatus");
                break;
            case ResultsOutputView results:
                State("ResultsRunSummary", TextWrapping.NoWrap, ellipsis: true);
                State("ResultsPageSummary");
                State(results.ViewModel.HasUnsavedOverrides ? "ResultsUnsavedOverrides" : "ResultsNoUnsavedOverrides",
                    TextWrapping.NoWrap);
                if (results.ViewModel.IsLoaded)
                {
                    State("ResultsRunIdentity", TextWrapping.NoWrap, ellipsis: true);
                    State("ResultsRunMetrics", TextWrapping.NoWrap, ellipsis: true);
                    State("ResultsSnapshotCaption", TextWrapping.NoWrap);
                }
                break;
            case SettingsView settings:
                State("SettingsStatus", TextWrapping.NoWrap, ellipsis: true);
                switch (settings.ViewModel.SelectedCategory)
                {
                    case SettingsCategory.Common:
                        int page = ById<TabControl>(settings, "SettingsCommonTabs").SelectedIndex;
                        if (page == 0 && !settings.ViewModel.CanEditDefinition)
                        {
                            State("SettingsCommonDefinitionAvailability");
                        }
                        else if (page == 1)
                        {
                            State("SettingsApplyAvailability");
                            // ApplyStatus is intentionally empty until an explicit apply, not hidden state.
                            if (!string.IsNullOrEmpty(settings.ViewModel.ApplyStatusText))
                            {
                                State("SettingsApplyStatus");
                            }
                        }
                        break;
                    case SettingsCategory.Mapping:
                        State("MappingSettingsValidationSummary", TextWrapping.NoWrap);
                        State(settings.ViewModel.Input.SelectedQuestion is null
                            ? "MappingSettingsNoQuestion" : "MappingSettingsPrimarySummary");
                        break;
                    case SettingsCategory.Special:
                        State("SpecialSettingsPointsStatus", TextWrapping.NoWrap);
                        State("SpecialSettingsValidationSummary", TextWrapping.NoWrap);
                        break;
                    case SettingsCategory.ImportedPrompts:
                        State("ImportedPromptCount", TextWrapping.NoWrap);
                        State("SelectedPromptTargetSummary", TextWrapping.NoWrap, ellipsis: true);
                        State("ImportedPromptApplyStatus");
                        break;
                }
                break;
        }
    }

    private static Rect BoundsIn(Control control, Control parent)
    {
        // Bounds.Size is untransformed. Translate all corners so Viewbox icons
        // are measured correctly without exempting a scaled/clipped application view.
        Point[] corners = [default, new(control.Bounds.Width, 0), new(0, control.Bounds.Height),
            new(control.Bounds.Width, control.Bounds.Height)];
        Point[] translated = corners.Select(point => control.TranslatePoint(point, parent)
            ?? throw new InvalidOperationException("Controls must share a visual root.")).ToArray();
        double left = translated.Min(point => point.X);
        double top = translated.Min(point => point.Y);
        return new Rect(left, top, translated.Max(point => point.X) - left, translated.Max(point => point.Y) - top);
    }

    internal static void AssertInteractiveFrames(Control view)
    {
        foreach (Control control in view.GetVisualDescendants().OfType<Control>().Where(control =>
            control.IsEffectivelyVisible && control.TemplatedParent is null
            && control is Button or TextBox or ComboBox or CheckBox or TabItem))
        {
            Rect target = BoundsIn(control, Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control)));
            Assert.True(control.MinHeight >= 44d && target.Height >= 44d && target.Width >= 44d,
                $"{Describe(control)} must retain a 44 DIP target, not just an offscreen minimum.");
            Assert.Equal(14d, Assert.IsAssignableFrom<TemplatedControl>(control).FontSize);
            AssertFullyInside(control, view);
            foreach (Control clip in control.GetVisualAncestors().OfType<Control>()
                .TakeWhile(parent => !ReferenceEquals(parent, view))
                .Where(parent => parent.ClipToBounds || parent is ScrollViewer))
            {
                AssertFullyInside(control, clip);
            }
        }
    }

    internal static void AssertNoBodyScroll(ScrollViewer scroll)
    {
        Assert.True(scroll.Viewport.Width > 0d && scroll.Viewport.Height > 0d);
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + OverflowTolerance
            && scroll.Extent.Height <= scroll.Viewport.Height + OverflowTolerance,
            $"{Describe(scroll)} extent={scroll.Extent}, viewport={scroll.Viewport} DIP.");
        Assert.Equal(default, scroll.Offset);
        scroll.ScrollToEnd();
        Render();
        Assert.Equal(default, scroll.Offset);
    }

    internal static void AssertNormalBody(MainWindow window)
    {
        ScrollViewer body = Required<ScrollViewer>(window, "ShellScrollViewer");
        Control view = CurrentView(window);
        AssertNoBodyScroll(body);
        AssertFullyInside(view, body);
        ScrollViewer? innerBody = view switch
        {
            QuantificationDesignView => Required<ScrollViewer>(view, "QuantificationDesignScrollViewer"),
            ResultsOutputView => Required<ScrollViewer>(view, "ResultsOutputScrollViewer"),
            SettingsView => Required<ScrollViewer>(view, "SettingsBodyScroll"),
            _ => null,
        };
        if (innerBody is not null)
        {
            AssertNoBodyScroll(innerBody);
        }

        AssertFitsHorizontally(window, "normal shell");
        AssertInteractiveFrames(view);
        AssertImportantStateTexts(view);
    }

    internal static Rect[] AssertSettingsFixedRegions(SettingsView settings)
    {
        ScrollViewer body = Required<ScrollViewer>(settings, "SettingsBodyScroll");
        Control[] fixedControls = [Required<Grid>(settings, "SettingsCategoryBar"), Required<Grid>(settings, "SettingsFooter")];
        foreach (Control control in fixedControls)
        {
            AssertFullyInside(control, settings);
            Assert.DoesNotContain(body, control.GetVisualAncestors());
        }

        // Relative to Settings: the narrow shell may scroll the whole settings view,
        // but scrolling a category/tab body must not reflow its category bar or footer.
        return fixedControls.Select(control => BoundsIn(control, settings)).ToArray();
    }

    internal static Rect[] AssertFixedRegions(MainWindow window)
    {
        string[] names = ["ShellHeader", "EthicsWarningBanner", "ShellFooter", "InputStepButton",
            "DesignStepButton", "ExecutionStepButton", "ResultsStepButton", "SettingsOpenButton"];
        Control[] fixedControls = names.Select(name => window.FindControl<Control>(name)!).ToArray();
        foreach (Control control in fixedControls)
        {
            Assert.NotNull(control);
            AssertFullyInside(control, window);
            Assert.DoesNotContain(control.GetVisualAncestors(), ancestor => ancestor is ScrollViewer);
        }

        foreach (Button button in fixedControls.OfType<Button>().Concat(new[]
            { Required<Button>(window, "PreviousStepButton"), Required<Button>(window, "NextStepButton") })
            .Where(button => button.IsEffectivelyVisible))
        {
            AssertFullyInside(button, window);
            Rect target = BoundsIn(button, window);
            Assert.True(target.Height >= 44d && target.Width >= 44d);
            Assert.Equal(14d, button.FontSize);
        }

        TextBlock warning = Required<TextBlock>(window, "EthicsWarningMessage");
        Assert.Equal("生成AIが行う評価には正確性が欠ける可能性があるため、必ず自分で責任をもって評点を行ってください。このツールや生成AIは評価結果に対しては一切の責任を負えません", warning.Text);
        Assert.Equal(TextWrapping.Wrap, warning.TextWrapping);
        Assert.Equal(TextTrimming.None, warning.TextTrimming);
        Assert.Equal(14d, warning.FontSize);
        AssertFullyInside(warning, fixedControls[1]);
        AssertImportantTextFullyVisible(warning);
        Rect body = BoundsIn(Required<ScrollViewer>(window, "ShellScrollViewer"), window);
        Assert.True(BoundsIn(fixedControls[0], window).Bottom <= body.Top + OverflowTolerance);
        Assert.True(body.Bottom <= BoundsIn(fixedControls[2], window).Top + OverflowTolerance);
        return fixedControls.Select(control => BoundsIn(control, window)).ToArray();
    }

    internal static void AssertPageRows<T>(ListBox list, IReadOnlyList<T> source, int pageIndex, int pageSize) where T : class
    {
        Assert.True(pageSize > 0 && double.IsFinite(list.Bounds.Height) && list.Bounds.Height > 0d);
        Assert.Contains(list.GetVisualDescendants(), control => control is VirtualizingStackPanel);
        T[] expected = source.Skip(pageIndex * pageSize).Take(pageSize).ToArray();
        Assert.Equal(expected.Length, list.Items.Count);
        Assert.Equal(expected.Length, list.GetVisualDescendants().OfType<ListBoxItem>().Count(item => item.IsEffectivelyVisible));
        ScrollViewer scroll = ListScroll(list);
        AssertNoBodyScroll(scroll);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.Same(expected[index], list.Items[index]);
            ListBoxItem container = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(index));
            Assert.Same(expected[index], container.DataContext);
            Assert.True(BoundsIn(container, scroll).Height >= 44d);
            AssertFullyInside(container, scroll);
        }

        AssertFitsHorizontally(list, "realized page rows");
    }

    internal static void GoToLastPage(Control view, string nextButtonName, int totalCount)
    {
        Button next = Required<Button>(view, nextButtonName);
        int steps = 0;
        while (next.IsEffectivelyEnabled)
        {
            Assert.True(steps++ < totalCount, "Page navigation must terminate within the fixture's item count.");
            Activate(next);
        }
    }

    internal static void SelectLastItem(Window window, ListBox list)
    {
        Assert.NotEmpty(list.Items);
        int start = Math.Max(0, list.SelectedIndex);
        list.ScrollIntoView(list.Items[start]!);
        Render();
        // Avalonia ListBox itself need not be focusable: use its real row container.
        ListBoxItem first = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(start));
        Assert.True(first.Focus(NavigationMethod.Tab));
        Press(window, Key.End);
        Assert.Same(list.Items[list.Items.Count - 1], list.SelectedItem);
        AssertFullyInside(Assert.IsType<ListBoxItem>(list.ContainerFromIndex(list.Items.Count - 1)), ListScroll(list));
    }

    internal static void AssertTextEndReachable(Window window, TextBox editor)
    {
        string text = Assert.IsType<string>(editor.Text);
        Assert.True(editor.Focus(NavigationMethod.Tab));
        Press(window, Key.Home, RawInputModifiers.Control);
        Assert.Equal(0, editor.CaretIndex);
        Press(window, Key.End, RawInputModifiers.Control);
        Assert.Equal(text.Length, editor.CaretIndex);
        Assert.Equal(text, editor.Text);
        ScrollViewer local = Assert.Single(editor.GetVisualDescendants().OfType<ScrollViewer>());
        bool overflows = local.Extent.Width > local.Viewport.Width + OverflowTolerance
            || local.Extent.Height > local.Viewport.Height + OverflowTolerance;
        if (text.Contains(LongText, StringComparison.Ordinal) || text == LongPath || text == Path.GetDirectoryName(LongPath))
        {
            Assert.True(overflows, "The multi-line/path fixture must exercise local scroll.");
        }

        if (overflows)
        {
            Assert.True(local.Offset.X > 0d || local.Offset.Y > 0d, "Keyboard must expose the end of the text.");
        }

        if (editor.TextWrapping == TextWrapping.Wrap)
        {
            Assert.True(local.Extent.Width <= local.Viewport.Width + OverflowTolerance);
        }

        AssertFullyInside(editor, window);
    }

    internal static void Activate(Button button, Key key = Key.Enter)
    {
        Assert.True(button.IsEffectivelyVisible && button.IsEffectivelyEnabled, Describe(button));
        Assert.True(button.Focus(NavigationMethod.Tab));
        Press(Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(button)), key);
    }

    internal static void Press(TopLevel window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physical = key switch
        {
            Key.Tab => PhysicalKey.Tab, Key.Enter => PhysicalKey.Enter, Key.Space => PhysicalKey.Space,
            Key.Home => PhysicalKey.Home, Key.End => PhysicalKey.End, Key.F4 => PhysicalKey.F4,
            Key.Escape => PhysicalKey.Escape,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Render();
    }

    internal static void RecordLayout(ITestOutputHelper output, string scenario, Window window)
    {
        // Numeric observations only. No workbook paths, prompt text, names, screenshots or output files.
        output.WriteLine(FormattableString.Invariant(
            $"T26 {scenario}: requested={window.Width}x{window.Height} DIP; client={window.ClientSize.Width}x{window.ClientSize.Height} DIP; scale={window.RenderScaling}; derivedPixels={window.ClientSize.Width * window.RenderScaling}x{window.ClientSize.Height * window.RenderScaling}"));
        Control view = window is MainWindow main ? CurrentView(main) : Assert.IsAssignableFrom<Control>(window.Content);
        Grid? layout = view switch
        {
            InputView => Required<Grid>(view, "InputContentRoot"),
            QuantificationDesignView => Required<Grid>(view, "DesignBody"),
            ExecutionView => Required<Grid>(view, "ExecutionLayout"),
            ResultsOutputView => Required<Grid>(view, "ResultsLayout"),
            SettingsView settings => Assert.IsType<Grid>(settings.Content),
            _ => null,
        };
        if (layout is not null)
        {
            output.WriteLine(FormattableString.Invariant(
                $"  {view.GetType().Name}: body={layout.Bounds.Width:F2}x{layout.Bounds.Height:F2}; margin={layout.Margin}; rowSpacing={layout.RowSpacing}; columnSpacing={layout.ColumnSpacing}"));
        }

        (int Index, int Size, int Total)? page = view.DataContext switch
        {
            InputViewModel input => (input.PageIndex, input.PageSize, input.Questions.Count),
            QuantificationDesignViewModel design => (design.PageIndex, design.PageSize, design.Questions.Count),
            ResultsOutputViewModel results => (results.PageIndex, results.PageSize, results.RowScores.Count),
            _ => null,
        };
        if (page is { } current)
        {
            output.WriteLine($"  pageIndex={current.Index}; capacity={current.Size}; total={current.Total}");
        }

        foreach (ScrollViewer scroll in window.GetVisualDescendants().OfType<ScrollViewer>()
            .Where(scroll => scroll.IsEffectivelyVisible && !string.IsNullOrEmpty(scroll.Name)))
        {
            output.WriteLine(FormattableString.Invariant(
                $"  {scroll.Name}: extent={scroll.Extent.Width:F2}x{scroll.Extent.Height:F2}; viewport={scroll.Viewport.Width:F2}x{scroll.Viewport.Height:F2}; offset={scroll.Offset.X:F2},{scroll.Offset.Y:F2}"));
        }

        foreach (ListBox list in window.GetVisualDescendants().OfType<ListBox>().Where(list => list.IsEffectivelyVisible))
        {
            ListBoxItem[] realized = list.GetVisualDescendants().OfType<ListBoxItem>().Where(item => item.IsEffectivelyVisible).ToArray();
            output.WriteLine(FormattableString.Invariant(
                $"  {list.Name}: slot={list.Bounds.Width:F2}x{list.Bounds.Height:F2}; pageItems={list.Items.Count}; realized={realized.Length}; rowHeights={string.Join(",", realized.Select(item => item.Bounds.Height.ToString("F2", CultureInfo.InvariantCulture)))}"));
        }
    }

    private static string Describe(Control control)
    {
        string? id = string.IsNullOrWhiteSpace(control.Name) ? AutomationProperties.GetAutomationId(control) : control.Name;
        return string.IsNullOrWhiteSpace(id) ? control.GetType().Name : $"{control.GetType().Name}#{id}";
    }

    internal static T Required<T>(Control root, string name)
        where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

    internal static void Render()
    {
        // Capacity changes can post another arrange; bounded frames, no sleeps or native processes.
        for (int frame = 0; frame < 3; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }
}

// Shared T26 fixture; U04 supplies fake summaries, X02 supplies temporary synthetic metadata.
// A store path is injected for display only. No load/save, real sample, authentication or run is requested.
internal sealed class WorkflowLayoutFixture : IDisposable
{
    private WorkflowLayoutFixture(X02TemporaryWorkbook workbook, InputViewModel input,
        QuantificationDesignViewModel design, ResultsOutputViewModel results)
    {
        Workbook = workbook;
        Authentication = new RecordingAuthenticationBoundary(new ExecutionAuthenticationSnapshot(
            ExecutionAuthenticationState.Available, [U04TestSupport.Model("model-test"), U04TestSupport.Model("auto")],
            U04TestSupport.RuntimeIdentity()));
        Runner = new RecordingRunBoundary((_, _, _) => throw new InvalidOperationException("Layout must not start a run."));
        ExecutionViewModel execution = new(Authentication, Runner)
        {
            OutputDirectoryOverride = Path.GetDirectoryName(ResponsiveLayoutTests.LongPath),
        };
        Shell = new MainWindowViewModel(new WorkflowNavigator(), input, design, execution, results,
            new SettingsFileStore(Path.Combine(workbook.Directory, ResponsiveLayoutTests.LongJapaneseName, "setting.txt")));
        Window = new MainWindow(Shell);
    }

    private X02TemporaryWorkbook Workbook { get; }
    private RecordingAuthenticationBoundary Authentication { get; }
    private RecordingRunBoundary Runner { get; }
    internal MainWindowViewModel Shell { get; }
    internal MainWindow Window { get; }

    internal static async Task<WorkflowLayoutFixture> CreateAsync(bool longContent = false)
    {
        X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet("Original", 1, 101, 3,
            new X02Header(1, "Report answer"), new X02Header(2, "Rationale"), new X02Header(3, "Supporting"));
        try
        {
            InputViewModel input = new();
            await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
            Assert.True(await input.ApplySavedDefinitionAsync(ResponsiveLayoutTests.ManyItemDefinition(longContent: longContent),
                TestContext.Current.CancellationToken));
            ImportedPrompt[] prompts = [.. Enumerable.Range(1, 12).Select(index => new ImportedPrompt
            {
                Path = Path.Combine(workbook.Directory, $"prompt-{index}.txt"),
                DisplayName = $"{index}: {ResponsiveLayoutTests.LongJapaneseName}.txt",
                Content = ResponsiveLayoutTests.LongText + "\n{回答} {評価項目}",
            })];
            QuantificationDesignViewModel design = new(input.DefinitionDraft, ["A", "B", "C"], prompts);
            return new WorkflowLayoutFixture(workbook, input, design,
                await ResponsiveLayoutTests.CreateResultsAsync(100, longContent: longContent));
        }
        catch
        {
            workbook.Dispose();
            throw;
        }
    }

    internal void AssertPassive()
    {
        Assert.Equal(0, Authentication.CallCount);
        Assert.Equal(0, Runner.CallCount);
        Assert.Null(Shell.ExecutionViewModel.LastLoginTask);
        Assert.Null(Shell.Settings.LastLoadTask);
        Assert.Null(Shell.Settings.LastSaveTask);
        Assert.Null(Shell.Settings.LastApplySavedDefinitionTask);
    }

    public void Dispose()
    {
        Window.Close();
        Shell.Dispose();
        Workbook.Dispose();
    }
}
