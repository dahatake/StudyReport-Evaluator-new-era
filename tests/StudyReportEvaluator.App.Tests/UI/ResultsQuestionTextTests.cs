using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;
using Xunit;
using Control = Avalonia.Controls.Control;

namespace StudyReportEvaluator.App.Tests.UI;

// AC-041 / FR-RS-01..06: the results screen shows the Excel-derived question text, not the question ID,
// and only the detail (not the row list preview) shows per-question scores.
// Requirements: FR-045 (AC-046)
public sealed class ResultsQuestionTextTests
{
    private const string FirstText = "レポート課題1：\r\n機械学習\t とは何か\u3000";
    private const string SecondText = "課題2：活用例";

    [Fact]
    public async Task Row_score_summary_uses_collapsed_question_text_instead_of_id_or_display_name()
    {
        (RunSummary summary, ResultsOutputViewModel viewModel) = await CreateAsync(FirstText, SecondText);
        using (viewModel)
        {
            string text = Assert.Single(viewModel.RowScores, row => row.SourceRowNumber == 2).QuestionEarnedText;

            Assert.Equal("レポート課題1： 機械学習 とは何か: 1 · 課題2：活用例: 1", text);
            Assert.DoesNotContain("Q1", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Question Q", text, StringComparison.Ordinal);
            Assert.False(viewModel.HasUnsavedOverrides);
            Assert.Equal(FirstText, summary.Snapshot.Definition.Questions[0].QuestionText);
        }
    }

    [Fact]
    public async Task Same_question_text_on_two_questions_is_not_merged()
    {
        (_, ResultsOutputViewModel viewModel) = await CreateAsync("同じ設問文", "同じ設問文");
        using (viewModel)
        {
            Assert.Equal(
                "同じ設問文: 1 · 同じ設問文: 1",
                viewModel.RowScores.First(row => row.SourceRowNumber == 2).QuestionEarnedText);
        }
    }

    [Fact]
    public async Task Unfinished_question_keeps_the_dash_after_its_question_text()
    {
        QuantificationDefinition definition = Definition(FirstText, SecondText);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata, cancelAfterFirst: true);
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), U04TestSupport.Context(summary));

        Assert.Contains(
            viewModel.RowScores,
            row => row.QuestionEarnedText.Contains("レポート課題1： 機械学習 とは何か: —", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("  \r\n\t\u3000 ", "表示名", "表示名")]
    [InlineData("", "  \u3000 ", "Q1")]
    [InlineData("設問文", "表示名", "設問文")]
    public void Blank_question_text_falls_back_to_display_name_then_id(string text, string displayName, string expected)
    {
        QuestionDefinition question = U01TestSupport.Question("Q1", "A", [], true) with
        {
            QuestionText = text,
            DisplayName = displayName,
        };

        Assert.Equal(expected, ResultsOutputViewModel.QuestionLabel(question));
    }

    [AvaloniaFact]
    public async Task Views_show_the_same_summary_and_the_original_question_text_in_the_criterion_editor()
    {
        (_, ResultsOutputViewModel viewModel) = await CreateAsync(FirstText, SecondText);
        using (viewModel)
        {
            Window window = new() { Width = 950, Height = 450, Content = new ResultsOutputView(viewModel) };
            window.Show();
            try
            {
                ResultsOutputView view = Assert.IsType<ResultsOutputView>(window.Content);
                Button detail = Assert.IsType<Button>(view.FindControl<Button>("ShowDetailButton"));
                detail.Command!.Execute(detail.CommandParameter);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Equal(
                    viewModel.SelectedRow?.QuestionEarnedText,
                    ById<TextBlock>(view, "ResultsQuestionEarnedFull").Text);
                Assert.DoesNotContain(
                    "Q2",
                    ById<TextBlock>(view, "ResultsQuestionEarnedFull").Text!,
                    StringComparison.Ordinal);
                ResultsCriterionViewModel first = viewModel.SelectedRowCriteria[0];
                Assert.Equal(FirstText, first.QuestionText);
                Assert.Equal(FirstText, ById<TextBlock>(view, "ResultsCriterionQuestionText").Text);
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public async Task Row_list_preview_shows_only_five_columns_without_question_scores()
    {
        (_, ResultsOutputViewModel viewModel) = await CreateAsync(FirstText, SecondText);
        using (viewModel)
        {
            Window window = new() { Width = 950, Height = 450, Content = new ResultsOutputView(viewModel) };
            window.Show();
            try
            {
                ResultsOutputView view = Assert.IsType<ResultsOutputView>(window.Content);
                ListBox rows = Assert.IsType<ListBox>(view.FindControl<ListBox>("RowScoreList"));
                Grid listPanel = Assert.IsType<Grid>(view.FindControl<Grid>("ResultsListPanel"));
                Grid header = Assert.IsType<Grid>(listPanel.Children[0]);

                Assert.Equal(5, header.ColumnDefinitions.Count);
                Assert.Equal(
                    new[] { "元の行", "最終点", "固有点", "類似減点", "状態" },
                    header.Children.OfType<TextBlock>().OrderBy(Grid.GetColumn).Select(text => text.Text ?? string.Empty).ToArray());
                Assert.Equal(5, header.Children.Count);

                Control firstRow = Assert.IsAssignableFrom<Control>(rows.ContainerFromIndex(0));
                Grid rowGrid = Assert.Single(
                    firstRow.GetVisualDescendants().OfType<Grid>(),
                    grid => AutomationProperties.GetAutomationId(grid) == "ResultsRow-2");
                Assert.Equal(5, rowGrid.ColumnDefinitions.Count);
                Assert.Equal(5, rowGrid.Children.Count);

                string questionScores = viewModel.RowScores.First(row => row.SourceRowNumber == 2).QuestionEarnedText;
                foreach (TextBlock text in listPanel.GetVisualDescendants().OfType<TextBlock>())
                {
                    string shown = (text.Text ?? string.Empty) + "\n" + (ToolTip.GetTip(text)?.ToString() ?? string.Empty);
                    Assert.DoesNotContain("設問別得点", shown, StringComparison.Ordinal);
                    Assert.DoesNotContain(questionScores, shown, StringComparison.Ordinal);
                    Assert.DoesNotContain("課題2：活用例", shown, StringComparison.Ordinal);
                    Assert.DoesNotContain("機械学習", shown, StringComparison.Ordinal);
                }
            }
            finally
            {
                window.Close();
            }
        }
    }

    private static QuantificationDefinition Definition(string firstText, string secondText) =>
        U01TestSupport.Definition(
            2,
            3,
            U01TestSupport.Question("Q1", "A", ["B"], true, U01TestSupport.Evaluator("E1", "C1")) with { QuestionText = firstText },
            U01TestSupport.Question("Q2", "A", [], true, U01TestSupport.Evaluator("E2", "C2")) with { QuestionText = secondText });

    private static async Task<(RunSummary Summary, ResultsOutputViewModel ViewModel)> CreateAsync(
        string firstText,
        string secondText)
    {
        QuantificationDefinition definition = Definition(firstText, secondText);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        return (summary, new ResultsOutputViewModel(new RecordingOutputBoundary(), U04TestSupport.Context(summary)));
    }

    private static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);
}
