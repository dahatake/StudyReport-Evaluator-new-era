using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using Xunit;
using Control = Avalonia.Controls.Control;

namespace StudyReportEvaluator.App.Tests.UI;

// AC-051 / FR-RV-01..06, SEC-RV-01, NFR-RV-01: the results detail shows the student's answer
// (re-read from the identity-checked input) and how each criterion evaluated it.
// Requirements: FR-050 (AC-051)
public sealed class ResultsAnswerReviewTests
{
    private const string AnswerCanary = "PRIVATE-ANSWER-CANARY";
    private const string ReasonCanary = "PRIVATE-REASON-CANARY";

    [Fact]
    public async Task Detail_formats_primary_and_supporting_cells_with_blank_marker_and_original_line_breaks()
    {
        ScriptedAnswerSource source = new(_ => Loaded(("A", "一行目\n二行目  "), ("B", " \u3000")));
        using ResultsOutputViewModel viewModel = await CreateAsync(source);

        viewModel.ShowDetailCommand.Execute(null);
        await viewModel.PendingStudentAnswerTask;

        ResultsCriterionViewModel criterion = Assert.Single(viewModel.SelectedRowCriteria);
        Assert.Equal(ResultsAnswerState.Loaded, criterion.StudentAnswerState);
        Assert.Equal("[列 A · 主回答]\n一行目\n二行目  \n\n[列 B · 補助]\n（空欄）", criterion.StudentAnswerText);
        ResultsAnswerRequest request = Assert.Single(source.Requests);
        Assert.Equal(2, request.SourceRowNumber);
        Assert.Equal("Original", request.SourceSheet);
        Assert.Equal(["A", "B"], request.Columns);
        Assert.Equal(U01TestSupport.InputSnapshot(), request.ExpectedInput);
    }

    [Fact]
    public async Task Missing_cells_are_blank_and_only_the_question_columns_are_shown()
    {
        ScriptedAnswerSource source = new(_ => Loaded(("A", null)));
        using ResultsOutputViewModel viewModel = await CreateAsync(source);

        viewModel.ShowDetailCommand.Execute(null);
        await viewModel.PendingStudentAnswerTask;

        Assert.Equal(
            "[列 A · 主回答]\n（空欄）\n\n[列 B · 補助]\n（空欄）",
            viewModel.SelectedRowCriteria[0].StudentAnswerText);
    }

    [Fact]
    public async Task Answers_are_read_only_for_the_selected_row_while_the_detail_is_visible_and_cached_per_row()
    {
        ScriptedAnswerSource source = new(request => Loaded(("A", "row " + request.SourceRowNumber), ("B", "support")));
        using ResultsOutputViewModel viewModel = await CreateAsync(source);

        Assert.Empty(source.Requests);
        Assert.Equal(ResultsAnswerState.Loading, viewModel.Results[0].StudentAnswerState);
        viewModel.SelectedRow = viewModel.RowScores[1];
        Assert.Empty(source.Requests);

        viewModel.SelectedRow = viewModel.RowScores[0];
        viewModel.ShowDetailCommand.Execute(null);
        await viewModel.PendingStudentAnswerTask;
        Assert.Single(source.Requests);
        Assert.StartsWith("[列 A · 主回答]\nrow 2", viewModel.SelectedRowCriteria[0].StudentAnswerText, StringComparison.Ordinal);

        viewModel.ShowListCommand.Execute(null);
        viewModel.ShowDetailCommand.Execute(null);
        await viewModel.PendingStudentAnswerTask;
        Assert.Single(source.Requests);

        viewModel.SelectedRow = viewModel.RowScores[1];
        await viewModel.PendingStudentAnswerTask;
        Assert.Equal([2, 3], source.Requests.Select(request => request.SourceRowNumber));
        Assert.StartsWith("[列 A · 主回答]\nrow 3", viewModel.SelectedRowCriteria[0].StudentAnswerText, StringComparison.Ordinal);

        viewModel.SelectedRow = viewModel.RowScores[0];
        await viewModel.PendingStudentAnswerTask;
        Assert.Equal(2, source.Requests.Count);
        Assert.Equal(ResultsAnswerState.Loaded, viewModel.SelectedRowCriteria[0].StudentAnswerState);
    }

    [Theory]
    [InlineData(ResultsAnswerState.InputChanged, "入力Excelが実行時から変更されているため、学生の回答を表示できません。元のExcelまたは結果Excelで確認してください。")]
    [InlineData(ResultsAnswerState.Unavailable, "入力Excelを読み取れないため、学生の回答を表示できません（移動・削除・ほかのアプリで使用中など）。元のExcelまたは結果Excelで確認してください。")]
    public async Task Failures_show_the_fixed_message_without_values_and_are_retried(ResultsAnswerState state, string expected)
    {
        int calls = 0;
        ScriptedAnswerSource source = new(_ => ++calls == 1
            ? Task.FromResult(state == ResultsAnswerState.InputChanged
                ? ResultsAnswerReadResult.InputChanged
                : ResultsAnswerReadResult.Unavailable)
            : Loaded(("A", "再読込後"), ("B", "")));
        using ResultsOutputViewModel viewModel = await CreateAsync(source);

        viewModel.ShowDetailCommand.Execute(null);
        await viewModel.PendingStudentAnswerTask;
        ResultsCriterionViewModel criterion = viewModel.SelectedRowCriteria[0];
        Assert.Equal(state, criterion.StudentAnswerState);
        Assert.Equal(expected, criterion.StudentAnswerText);
        Assert.DoesNotContain("PRIVATE", criterion.StudentAnswerText, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath(), criterion.StudentAnswerText, StringComparison.OrdinalIgnoreCase);

        viewModel.ShowListCommand.Execute(null);
        viewModel.ShowDetailCommand.Execute(null);
        await viewModel.PendingStudentAnswerTask;
        Assert.Equal(2, source.Requests.Count);
        Assert.Equal("[列 A · 主回答]\n再読込後\n\n[列 B · 補助]\n（空欄）", criterion.StudentAnswerText);
    }

    [Fact]
    public async Task Source_exceptions_are_contained_as_unavailable()
    {
        ScriptedAnswerSource source = new(_ => throw new IOException(AnswerCanary));
        using ResultsOutputViewModel viewModel = await CreateAsync(source);

        viewModel.ShowDetailCommand.Execute(null);
        await viewModel.PendingStudentAnswerTask;

        Assert.Equal(ResultsAnswerState.Unavailable, viewModel.SelectedRowCriteria[0].StudentAnswerState);
        Assert.Equal(ResultsCriterionViewModel.AnswerUnavailableText, viewModel.SelectedRowCriteria[0].StudentAnswerText);
    }

    [Fact]
    public async Task Stale_reads_are_discarded_after_row_change_and_reload()
    {
        List<TaskCompletionSource<ResultsAnswerReadResult>> pending = [];
        List<CancellationToken> tokens = [];
        ScriptedAnswerSource source = new((_, token) =>
        {
            TaskCompletionSource<ResultsAnswerReadResult> completion = new();
            pending.Add(completion);
            tokens.Add(token);
            return completion.Task;
        });
        QuantificationDefinition definition = U04TestSupport.Definition(2, 4);
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, U01TestSupport.ValidateMapping(definition).Metadata);
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), U04TestSupport.Context(summary), source);

        viewModel.ShowDetailCommand.Execute(null);
        Task firstTask = viewModel.PendingStudentAnswerTask;
        ResultsCriterionViewModel rowTwo = viewModel.SelectedRowCriteria[0];
        viewModel.SelectedRow = viewModel.RowScores[1];
        ResultsCriterionViewModel rowThree = viewModel.SelectedRowCriteria[0];
        Assert.Equal(2, pending.Count);
        Assert.True(tokens[0].IsCancellationRequested);

        pending[0].SetResult(ResultsAnswerReadResult.Loaded([new("A", "stale"), new("B", "stale")]));
        await firstTask;
        Assert.Equal(ResultsAnswerState.Loading, rowTwo.StudentAnswerState);
        Assert.Equal(ResultsAnswerState.Loading, rowThree.StudentAnswerState);

        pending[1].SetResult(ResultsAnswerReadResult.Loaded([new("A", "current"), new("B", "")]));
        await viewModel.PendingStudentAnswerTask;
        Assert.Equal("[列 A · 主回答]\ncurrent\n\n[列 B · 補助]\n（空欄）", rowThree.StudentAnswerText);
        Assert.Equal(ResultsAnswerState.Loading, rowTwo.StudentAnswerState);

        viewModel.SelectedRow = viewModel.RowScores[2];
        Task thirdTask = viewModel.PendingStudentAnswerTask;
        ResultsCriterionViewModel rowFour = viewModel.SelectedRowCriteria[0];
        viewModel.Load(U04TestSupport.Context(summary));
        Assert.True(tokens[2].IsCancellationRequested);
        pending[2].SetResult(ResultsAnswerReadResult.Loaded([new("A", "after reload"), new("B", "")]));
        await thirdTask;
        Assert.Equal(ResultsAnswerState.Loading, rowFour.StudentAnswerState);
        Assert.False(viewModel.IsDetailVisible);
        Assert.All(viewModel.Results, item => Assert.Equal(ResultsAnswerState.Loading, item.StudentAnswerState));
        Assert.Equal(3, source.Requests.Count);
    }

    [Theory]
    [InlineData("用語の説明が正確", "データから学ぶ", EvidenceSourceKind.PrimaryAnswer, "A", "用語の説明が正確", "データから学ぶ", "主回答（列 A）")]
    [InlineData(" \n ", "", EvidenceSourceKind.None, "", "（理由は空欄です）", "（引用なし）", "なし")]
    [InlineData("補助列を参照", "補足", EvidenceSourceKind.SupportingColumn, "B", "補助列を参照", "補足", "補助（列 B）")]
    [InlineData("列なし", "引用", EvidenceSourceKind.SupportingColumn, "", "列なし", "引用", "補助")]
    [InlineData("理由\n2行目", "引用", EvidenceSourceKind.PrimaryAnswer, "", "理由\n2行目", "引用", "主回答")]
    public void Accepted_results_show_reason_evidence_and_source(
        string reason,
        string evidence,
        EvidenceSourceKind source,
        string column,
        string expectedReason,
        string expectedEvidence,
        string expectedSource)
    {
        ResultsCriterionViewModel criterion = Criterion(ResultsStatusCodes.Success, new CriterionQuantificationResult
        {
            CriterionId = "C1",
            RawScore = 3m,
            Reason = reason,
            Evidence = evidence,
            EvidenceSource = source,
            EvidenceSourceColumnId = column,
        });

        Assert.Equal(expectedReason, criterion.ReasonText);
        Assert.Equal(expectedEvidence, criterion.EvidenceText);
        Assert.Equal(expectedSource, criterion.EvidenceSourceText);
        Assert.Equal("Description C1", criterion.CriterionDescriptionText);
    }

    [Theory]
    [InlineData(ResultsStatusCodes.Empty, "回答が空欄のため、AIで評価していません。")]
    [InlineData(ResultsStatusCodes.Cancelled, "取消または未処理のため、評価していません。")]
    [InlineData(ResultsStatusCodes.AiTimeout, "技術的な失敗（AI_TIMEOUT）のため、評価結果はありません。")]
    [InlineData(ResultsStatusCodes.AiOutputInvalid, "技術的な失敗（AI_OUTPUT_INVALID）のため、評価結果はありません。")]
    public void Missing_results_explain_the_status_without_inventing_values(string status, string expectedReason)
    {
        ResultsCriterionViewModel criterion = Criterion(status, null, description: " \u3000");

        Assert.Equal(expectedReason, criterion.ReasonText);
        Assert.Equal("—", criterion.EvidenceText);
        Assert.Equal("—", criterion.EvidenceSourceText);
        Assert.Equal("（説明は未設定です）", criterion.CriterionDescriptionText);
        Assert.Equal("—", criterion.AiRawText);
    }

    [Fact]
    public async Task Run_results_supply_the_same_reason_and_evidence_as_the_results_sheet()
    {
        RunSummary summary = await CreateCanarySummaryAsync(U01TestSupport.InputSnapshot(), SyntheticInputPath());
        using ResultsOutputViewModel viewModel = new(
            new RecordingOutputBoundary(),
            U04TestSupport.Context(summary),
            new ScriptedAnswerSource(_ => Loaded(("A", AnswerCanary), ("B", ""))));

        ResultsCriterionViewModel criterion = viewModel.Results[0];
        Assert.Equal(7m, criterion.AiRawScore);
        Assert.Equal(ReasonCanary, criterion.ReasonText);
        Assert.Equal(AnswerCanary, criterion.EvidenceText);
        Assert.Equal("主回答（列 A）", criterion.EvidenceSourceText);
        Assert.Equal("Description C1", criterion.CriterionDescriptionText);
    }

    [Fact]
    public async Task Viewing_answers_changes_no_scores_overrides_or_exports_and_leaks_no_content()
    {
        RecordingOutputBoundary output = new();
        RunSummary summary = await CreateCanarySummaryAsync(U01TestSupport.InputSnapshot(), SyntheticInputPath());
        ExecutionRunContext context = U04TestSupport.Context(summary);
        using ResultsOutputViewModel viewModel = new(output, context,
            new ScriptedAnswerSource(_ => Loaded(("A", AnswerCanary + " 本文"), ("B", "補助"))));
        decimal?[] before = viewModel.RowScores.Select(row => row.FinalScore).ToArray();

        viewModel.ShowDetailCommand.Execute(null);
        await viewModel.PendingStudentAnswerTask;
        viewModel.SelectedRow = viewModel.RowScores[1];
        await viewModel.PendingStudentAnswerTask;

        Assert.Equal(before, viewModel.RowScores.Select(row => row.FinalScore).ToArray());
        Assert.False(viewModel.HasUnsavedOverrides);
        Assert.All(viewModel.Results, item => Assert.Equal(string.Empty, item.OverrideText));
        Assert.Equal(0, output.ExportCount);
        foreach (ResultsCriterionViewModel item in viewModel.Results)
        {
            Assert.Contains(AnswerCanary, item.StudentAnswerText, StringComparison.Ordinal);
            foreach (string text in new[] { item.ToString(), item.AccessibleSummary, viewModel.ToString() })
            {
                Assert.DoesNotContain(AnswerCanary, text, StringComparison.Ordinal);
                Assert.DoesNotContain(ReasonCanary, text, StringComparison.Ordinal);
            }
        }

        ResultsAnswerRequest request = new(context.InputPath, summary.InputSnapshot, "Original", 2, ["A"]);
        Assert.DoesNotContain(context.InputPath, request.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AnswerCanary, ResultsAnswerReadResult.Loaded([new("A", AnswerCanary)]).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answer_source_checks_the_input_identity_before_and_after_reading_one_row()
    {
        List<string> events = [];
        ScriptedInputSnapshots snapshots = new(U01TestSupport.InputSnapshot(), events: events);
        ScriptedRowSource rows = new((request, _) =>
        {
            events.Add("read");
            return Task.FromResult(new EvaluationRowData(request.SourceRowNumber,
                new Dictionary<string, string?> { ["A"] = "主", ["B"] = null }));
        });
        ResultsAnswerSource source = new(snapshots, _ => rows);

        ResultsAnswerReadResult result = await source.ReadAsync(Request(snapshots.Snapshot), CancellationToken.None);

        Assert.Equal(ResultsAnswerState.Loaded, result.State);
        Assert.Equal("主", result.Cells["a"]);
        Assert.Null(result.Cells["B"]);
        Assert.Equal(["recheck", "read", "recheck"], events);
        EvaluationRowRequest read = Assert.Single(rows.Requests);
        Assert.Equal(("Original", 2), (read.SourceSheet, read.SourceRowNumber));
        Assert.Equal(["A", "B"], read.SelectedColumns);
    }

    [Theory]
    [InlineData(false, true, false, false, ResultsAnswerState.InputChanged, 0)]
    [InlineData(true, false, false, false, ResultsAnswerState.InputChanged, 1)]
    [InlineData(true, true, true, false, ResultsAnswerState.Unavailable, 1)]
    [InlineData(true, true, false, true, ResultsAnswerState.Unavailable, 0)]
    public async Task Answer_source_reports_changed_or_unreadable_input(
        bool firstCheck,
        bool secondCheck,
        bool readThrows,
        bool checkThrows,
        ResultsAnswerState expected,
        int expectedReads)
    {
        ScriptedInputSnapshots snapshots = new(U01TestSupport.InputSnapshot())
        {
            RecheckOutcomes = new Queue<bool>([firstCheck, secondCheck]),
            ThrowOnRecheck = checkThrows,
        };
        ScriptedRowSource rows = new((request, _) => readThrows
            ? throw new EvaluationRowSourceException("ROW_SOURCE_FAILED")
            : Task.FromResult(new EvaluationRowData(request.SourceRowNumber, new Dictionary<string, string?> { ["A"] = AnswerCanary })));

        ResultsAnswerReadResult result = await new ResultsAnswerSource(snapshots, _ => rows)
            .ReadAsync(Request(snapshots.Snapshot), CancellationToken.None);

        Assert.Equal(expected, result.State);
        Assert.Empty(result.Cells);
        Assert.Equal(expectedReads, rows.Requests.Count);
    }

    [Fact]
    public async Task Physical_source_reads_the_real_input_row_without_changing_its_bytes_or_timestamp()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-RV-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "synthetic.xlsx");
            WriteWorkbook(path, ("A2", "実ファイルの回答\n2行目"), ("B2", "補助の値"), ("A3", "3行目の回答"));
            InputSnapshot snapshot = new InputSnapshotService().Capture(path);
            byte[] bytes = File.ReadAllBytes(path);
            DateTime lastWrite = File.GetLastWriteTimeUtc(path);
            RunSummary summary = await CreateCanarySummaryAsync(snapshot, path);
            using (ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), U04TestSupport.Context(summary, path)))
            {
                viewModel.ShowDetailCommand.Execute(null);
                await viewModel.PendingStudentAnswerTask;
                Assert.Equal(
                    "[列 A · 主回答]\n実ファイルの回答\n2行目\n\n[列 B · 補助]\n補助の値",
                    viewModel.SelectedRowCriteria[0].StudentAnswerText);
                viewModel.SelectedRow = viewModel.RowScores[1];
                await viewModel.PendingStudentAnswerTask;
                Assert.Equal(
                    "[列 A · 主回答]\n3行目の回答\n\n[列 B · 補助]\n（空欄）",
                    viewModel.SelectedRowCriteria[0].StudentAnswerText);
            }

            ResultsAnswerSource physical = new();
            Assert.Equal(ResultsAnswerState.InputChanged, (await physical.ReadAsync(
                new ResultsAnswerRequest(path, U01TestSupport.InputSnapshot(), "Original", 2, ["A"]),
                CancellationToken.None)).State);
            Assert.Equal(ResultsAnswerState.Unavailable, (await physical.ReadAsync(
                new ResultsAnswerRequest(Path.Combine(directory, "missing.xlsx"), snapshot, "Original", 2, ["A"]),
                CancellationToken.None)).State);
            Assert.Equal(ResultsAnswerState.Unavailable, (await physical.ReadAsync(
                new ResultsAnswerRequest(path, snapshot, "NoSuchSheet", 2, ["A"]),
                CancellationToken.None)).State);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Detail_view_shows_the_answer_pane_beside_the_evaluation_pane_in_the_minimum_window()
    {
        RunSummary summary = await CreateCanarySummaryAsync(U01TestSupport.InputSnapshot(), SyntheticInputPath());
        using ResultsOutputViewModel viewModel = new(
            new RecordingOutputBoundary(),
            U04TestSupport.Context(summary),
            new ScriptedAnswerSource(_ => Loaded(("A", AnswerCanary + "\n本文"), ("B", ""))));
        ResultsOutputView view = new(viewModel);
        Window window = new() { Width = 1024, Height = 720, Content = view };
        window.Show();
        try
        {
            RenderUi();
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(),
                control => AutomationProperties.GetAutomationId(control) == "ResultsCriterionStudentAnswer");
            Button detail = Assert.IsType<Button>(view.FindControl<Button>("ShowDetailButton"));
            detail.Command!.Execute(detail.CommandParameter);
            await viewModel.PendingStudentAnswerTask;
            RenderUi();

            ResultsCriterionViewModel criterion = Assert.IsType<ResultsCriterionViewModel>(viewModel.SelectedCriterion);
            TextBlock answer = ById<TextBlock>(view, "ResultsCriterionStudentAnswer");
            Assert.Equal($"[列 A · 主回答]\n{AnswerCanary}\n本文\n\n[列 B · 補助]\n（空欄）", answer.Text);
            Assert.Equal(criterion.QuestionText, ById<TextBlock>(view, "ResultsCriterionQuestionText").Text);
            Assert.Equal(ReasonCanary, ById<TextBlock>(view, "ResultsCriterionReason").Text);
            Assert.Equal(AnswerCanary, ById<TextBlock>(view, "ResultsCriterionEvidence").Text);
            Assert.Equal("主回答（列 A）", ById<TextBlock>(view, "ResultsCriterionEvidenceSource").Text);
            Assert.Equal("Description C1", ById<TextBlock>(view, "ResultsCriterionDescription").Text);

            ScrollViewer answerPane = ById<ScrollViewer>(view, "ResultsCriterionAnswerPane");
            ScrollViewer evaluationPane = ById<ScrollViewer>(view, "ResultsCriterionEvaluationPane");
            Assert.Contains(answer, answerPane.GetVisualDescendants());
            Assert.Contains(ById<TextBlock>(view, "ResultsCriterionQuestionText"), answerPane.GetVisualDescendants());
            Assert.Contains(ById<TextBlock>(view, "ResultsCriterionReason"), evaluationPane.GetVisualDescendants());
            Point questionOrigin = Origin(ById<TextBlock>(view, "ResultsCriterionQuestionText"), answerPane);
            Point answerOrigin = Origin(answer, answerPane);
            Assert.True(questionOrigin.Y < answerOrigin.Y);
            Point answerPaneOrigin = Origin(answerPane, view);
            Point evaluationPaneOrigin = Origin(evaluationPane, view);
            Assert.True(answerPane.Bounds.Height >= 44d);
            Assert.True(evaluationPane.Bounds.Height >= 44d);
            Assert.True(answerPaneOrigin.X + answerPane.Bounds.Width <= evaluationPaneOrigin.X + 0.5d);
            Assert.True(evaluationPaneOrigin.X + evaluationPane.Bounds.Width <= view.Bounds.Width + 0.5d);

            TextBox overrideEditor = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(),
                editor => (AutomationProperties.GetAutomationId(editor) ?? string.Empty).EndsWith("-Override", StringComparison.Ordinal));
            Point editorOrigin = Origin(overrideEditor, window);
            Assert.InRange(editorOrigin.X, 0d, window.ClientSize.Width);
            Assert.InRange(editorOrigin.X + overrideEditor.Bounds.Width, 0d, window.ClientSize.Width);
            Assert.InRange(editorOrigin.Y, 0d, window.ClientSize.Height);
            Assert.InRange(editorOrigin.Y + overrideEditor.Bounds.Height, 0d, window.ClientSize.Height);

            Control[] controls = view.GetVisualDescendants().OfType<Control>().ToArray();
            Assert.All(controls, control =>
            {
                string name = AutomationProperties.GetName(control) ?? string.Empty;
                Assert.DoesNotContain(AnswerCanary, name, StringComparison.Ordinal);
                Assert.DoesNotContain(ReasonCanary, name, StringComparison.Ordinal);
            });
            string[] ids = controls.Select(AutomationProperties.GetAutomationId)
                .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray();
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            window.Close();
        }
    }

    private static ResultsCriterionViewModel Criterion(
        string status,
        CriterionQuantificationResult? result,
        string description = "Description C1")
    {
        QuestionDefinition question = U01TestSupport.Question("Q1", "A", ["B"], true, U01TestSupport.Evaluator("E1", "C1"));
        EvaluatorDefinition evaluator = question.Evaluators[0];
        CriterionDefinition criterion = evaluator.Criteria[0] with { Description = description };
        return new ResultsCriterionViewModel(2, question, evaluator, criterion, true, result?.RawScore, status, _ => { }, result);
    }

    private static Task<ResultsAnswerReadResult> Loaded(params (string Column, string? Value)[] cells) =>
        Task.FromResult(ResultsAnswerReadResult.Loaded(
            cells.Select(cell => new KeyValuePair<string, string?>(cell.Column, cell.Value))));

    private static async Task<ResultsOutputViewModel> CreateAsync(IResultsAnswerSource source)
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 3);
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(
            definition,
            U01TestSupport.ValidateMapping(definition).Metadata);
        return new ResultsOutputViewModel(new RecordingOutputBoundary(), U04TestSupport.Context(summary), source);
    }

    private static async Task<RunSummary> CreateCanarySummaryAsync(InputSnapshot snapshot, string inputPath)
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedRowSource rows = new((request, _) => Task.FromResult(new EvaluationRowData(
            request.SourceRowNumber,
            new Dictionary<string, string?>
            {
                ["A"] = AnswerCanary + " 本文",
                ["B"] = "補助",
            })));
        ScriptedRunner runner = new((payload, _, _) => Task.FromResult(EvaluationRunnerResult.Succeeded(
            new QuantificationResult
            {
                EvaluatorId = payload.EvaluatorId,
                Criteria = payload.ExpectedCriteria
                    .Select(criterion => new CriterionQuantificationResult
                    {
                        CriterionId = criterion.CriterionId,
                        RawScore = 7m,
                        Reason = ReasonCanary,
                        Evidence = AnswerCanary,
                        EvidenceSource = EvidenceSourceKind.PrimaryAnswer,
                        EvidenceSourceColumnId = payload.PrimarySource.SourceColumnId,
                    })
                    .ToImmutableArray(),
            })));
        return await new QuantificationOrchestrator(rows, runner, new ScriptedInputSnapshots(snapshot)).RunAsync(
            new QuantificationRunRequest
            {
                DraftDefinition = definition,
                WorkbookMetadata = metadata,
                InputPath = inputPath,
                ModelId = "model-test",
                MaximumPromptTokens = 64_000,
                MaximumContextWindowTokens = 128_000,
                MaxConcurrency = 1,
            });
    }

    private static string SyntheticInputPath() =>
        Path.Combine(Path.GetTempPath(), "PRIVATE-RV-INPUT-CANARY.xlsx");

    private static ResultsAnswerRequest Request(InputSnapshot snapshot) =>
        new(SyntheticInputPath(), snapshot, "Original", 2, ["A", "b"]);

    private static void WriteWorkbook(string path, params (string Reference, string Value)[] cells)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        WorkbookPart workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        SheetData data = new(new Row(
            InlineCell("A1", "Primary A"),
            InlineCell("B1", "Support B"),
            InlineCell("C1", "Primary C"))
        { RowIndex = 1 });
        foreach (IGrouping<uint, (string Reference, string Value)> row in cells
                     .GroupBy(cell => uint.Parse(cell.Reference[1..], System.Globalization.CultureInfo.InvariantCulture))
                     .OrderBy(group => group.Key))
        {
            data.Append(new Row(row.OrderBy(cell => cell.Reference, StringComparer.Ordinal)
                .Select(cell => InlineCell(cell.Reference, cell.Value)))
            { RowIndex = row.Key });
        }

        worksheetPart.Worksheet = new Worksheet(data);
        workbookPart.Workbook.AppendChild(new Sheets(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Original",
        }));
        workbookPart.Workbook.Save();
    }

    private static Cell InlineCell(string reference, string value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(value) { Space = SpaceProcessingModeValues.Preserve }),
        };

    private static Point Origin(Visual visual, Visual relativeTo) =>
        visual.TranslatePoint(new Point(0d, 0d), relativeTo) ?? throw new InvalidOperationException("The control is not in the visual tree.");

    private static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static void RenderUi()
    {
        for (int pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        Dispatcher.UIThread.RunJobs();
    }

    private sealed class ScriptedAnswerSource : IResultsAnswerSource
    {
        private readonly Func<ResultsAnswerRequest, CancellationToken, Task<ResultsAnswerReadResult>> read;
        private readonly List<ResultsAnswerRequest> requests = [];

        public ScriptedAnswerSource(Func<ResultsAnswerRequest, Task<ResultsAnswerReadResult>> read)
            : this((request, _) => read(request))
        {
        }

        public ScriptedAnswerSource(Func<ResultsAnswerRequest, CancellationToken, Task<ResultsAnswerReadResult>> read)
        {
            this.read = read;
        }

        public IReadOnlyList<ResultsAnswerRequest> Requests => requests;

        public Task<ResultsAnswerReadResult> ReadAsync(ResultsAnswerRequest request, CancellationToken cancellationToken)
        {
            requests.Add(request);
            return read(request, cancellationToken);
        }
    }
}
