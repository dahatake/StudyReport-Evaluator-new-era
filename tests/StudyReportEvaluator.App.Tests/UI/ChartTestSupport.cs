using System.Collections.Immutable;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Visualization;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

/// <summary>Synthetic runs and data for the chart tests. No AI runtime, no files written.</summary>
internal static class ChartTestSupport
{
    /// <summary>A student name in a column that no question selects (and echoed by the synthetic AI reason).</summary>
    internal const string NameCanary = "CANARY-氏名-合成太郎-7F3A";

    internal const string FirstQuestionText = "設問文A：読解";
    internal const string SecondQuestionText = "設問文B：考察";

    /// <summary>
    /// A completed durable-style run of two 50-point questions (base 0, similarity weight 0) over rows 2..rowCount+1.
    /// The raw score (0〜10) of each row and question comes from <paramref name="scoreOf"/>; null is an AI failure (blank).
    /// </summary>
    internal static async Task<RunSummary> CreateRunAsync(int rowCount, Func<int, string, decimal?> scoreOf)
    {
        QuantificationDefinition definition = U01TestSupport.Definition(
            2,
            rowCount + 1,
            U01TestSupport.Question("Q1", "A", ["B"], true, U01TestSupport.Evaluator("E1", "C1")) with
            {
                Points = 50m,
                QuestionText = FirstQuestionText,
            },
            U01TestSupport.Question("Q2", "C", [], true, U01TestSupport.Evaluator("E2", "C2")) with
            {
                Points = 50m,
                QuestionText = SecondQuestionText,
            }) with { SimilarityPenaltyWeight = 0m };
        ScriptedRowSource rows = new((request, _) => Task.FromResult(new EvaluationRowData(
            request.SourceRowNumber,
            new Dictionary<string, string?>
            {
                ["A"] = "row:" + request.SourceRowNumber.ToString(CultureInfo.InvariantCulture),
                ["B"] = "synthetic support",
                ["C"] = "row:" + request.SourceRowNumber.ToString(CultureInfo.InvariantCulture),
                ["D"] = NameCanary,
            })));
        ScriptedRunner runner = new((payload, _, _) =>
        {
            int row = int.Parse(payload.PrimarySource.Value["row:".Length..], CultureInfo.InvariantCulture);
            if (scoreOf(row, payload.QuestionId) is not decimal score)
            {
                return Task.FromResult(EvaluationRunnerResult.Failed(ResultsStatusCodes.AiRuntimeFailed));
            }

            return Task.FromResult(EvaluationRunnerResult.Succeeded(new QuantificationResult
            {
                EvaluatorId = payload.EvaluatorId,
                Criteria = [.. payload.ExpectedCriteria.Select(criterion => new CriterionQuantificationResult
                {
                    CriterionId = criterion.CriterionId,
                    RawScore = score,
                    Reason = "合成の理由 " + NameCanary,
                    Evidence = string.Empty,
                    EvidenceSource = EvidenceSourceKind.None,
                    EvidenceSourceColumnId = string.Empty,
                })],
            }));
        });
        RunSummary legacy = await new QuantificationOrchestrator(
            rows,
            runner,
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot())).RunAsync(
                new QuantificationRunRequest
                {
                    DraftDefinition = definition,
                    WorkbookMetadata = U01TestSupport.ValidateMapping(definition).Metadata,
                    InputPath = Path.Combine(Path.GetTempPath(), "chart-tests-input.xlsx"),
                    ModelId = "model-test",
                    MaximumPromptTokens = 64_000,
                    MaximumContextWindowTokens = 128_000,
                    MaxConcurrency = 1,
                },
                cancellationToken: TestContext.Current.CancellationToken);
        return ToDurable(legacy);
    }

    /// <summary>The synthetic similarity of a row's answer to the question's reference answer (0〜0.99).</summary>
    internal static decimal SimilarityOf(int row, string questionId) =>
        ((row * 7) + (questionId == "Q1" ? 0 : 3)) % 100 / 100m;

    // Durable presentation of the same units: completed rows with similarity results and peers, as the
    // durable orchestrator records them, so that Final_Score and the similarity columns exist.
    private static RunSummary ToDurable(RunSummary source)
    {
        string[] questions = [.. source.Snapshot.Definition.Questions.Where(question => question.Enabled).Select(question => question.Id)];
        ImmutableArray<CheckpointReference> references = [.. questions.Select(question => new CheckpointReference
        {
            QuestionId = question,
            Answer = "合成参照回答",
            StatusCode = ResultsStatusCodes.Success,
            GeneratedAtUtc = source.StartedAtUtc,
            AttemptCount = 1,
        })];
        int[] sourceRows = [.. source.Units.Select(unit => unit.Item.SourceRowNumber).Distinct().Order()];
        ImmutableArray<CheckpointCompletedRow> completedRows = [.. sourceRows.Select(row => new CheckpointCompletedRow
        {
            SourceRowNumber = row,
            NormalResults = [.. source.Units.Where(unit => unit.Item.SourceRowNumber == row).Select(unit => new CheckpointNormalResult
            {
                QuestionId = unit.Item.QuestionId,
                EvaluatorId = unit.Item.EvaluatorId,
                StatusCode = unit.StatusCode,
                AttemptCount = 1,
                Scorable = unit.Scorable,
                ScorableKnown = unit.ScorableKnown,
                AcceptedResult = unit.AcceptedResult,
            })],
            SimilarityResults = [.. questions.Select(question => new CheckpointSimilarityResult
            {
                QuestionId = question,
                StatusCode = ResultsStatusCodes.Success,
                AttemptCount = 1,
                AcceptedResult = new SimilarityQuantificationResult
                {
                    QuestionId = question,
                    Similarity = SimilarityOf(row, question),
                    Reason = "合成の類似度の理由 " + NameCanary,
                },
            })],
        })];
        ImmutableArray<RunPeerSimilarityResult> peers = [.. sourceRows.SelectMany(row => questions.Select(question =>
            new RunPeerSimilarityResult
            {
                SourceRowNumber = row,
                QuestionId = question,
                PeerMax = SimilarityOf(row + 1, question),
                PeerRow = row == sourceRows[0] ? sourceRows[^1] : sourceRows[0],
            }))];
        return new RunSummary(
            source.Schedule,
            source.InputSnapshot,
            QuantificationRunStatusCodes.Success,
            source.StartedAtUtc,
            source.EndedAtUtc,
            isDurable: true,
            references,
            completedRows,
            Path.Combine(Path.GetTempPath(), "chart-tests-final.xlsx"),
            Path.Combine(Path.GetTempPath(), "chart-tests-final.partial.xlsx"),
            wasResumed: false,
            ResultsOutputStatusCodes.Success,
            partialCleanupFailed: false,
            peers);
    }

    /// <summary>Row r: both questions score r mod 11 (Final_Score 0, 10, …, 100); every 53rd row fails on Q2.</summary>
    internal static decimal? SpreadScore(int row, string questionId) =>
        questionId == "Q2" && row % 53 == 0 ? null : row % 11;

    internal static async Task<ResultsOutputViewModel> CreateChartResultsAsync(int rowCount, Func<int, string, decimal?>? scoreOf = null)
    {
        RunSummary summary = await CreateRunAsync(rowCount, scoreOf ?? SpreadScore);
        return new ResultsOutputViewModel(new RecordingOutputBoundary(), U04TestSupport.Context(summary));
    }

    /// <summary>Synthetic chart data: questions with the given points; values from <paramref name="valueOf"/>.</summary>
    internal static ResultChartDataSet DataSet(
        int rowCount,
        int questionCount,
        Func<int, int, ResultChartQuestionValue> valueOf,
        Func<int, decimal?>? finalScoreOf = null,
        bool isProvisional = false)
    {
        ResultChartQuestion[] questions = [.. Enumerable.Range(0, questionCount).Select(index =>
            new ResultChartQuestion($"Q{index + 1}", $"設問文{index + 1}", 20m))];
        IEnumerable<ResultChartRow> rows = Enumerable.Range(2, rowCount).Select(row => new ResultChartRow(
            row,
            finalScoreOf?.Invoke(row) ?? row % 101,
            Enumerable.Range(0, questionCount).Select(column => valueOf(row, column))));
        return new ResultChartDataSet(questions, rows, isProvisional);
    }

    internal static void Key(TopLevel window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physical = key switch
        {
            Avalonia.Input.Key.Left => PhysicalKey.ArrowLeft,
            Avalonia.Input.Key.Right => PhysicalKey.ArrowRight,
            Avalonia.Input.Key.Up => PhysicalKey.ArrowUp,
            Avalonia.Input.Key.Down => PhysicalKey.ArrowDown,
            Avalonia.Input.Key.Home => PhysicalKey.Home,
            Avalonia.Input.Key.End => PhysicalKey.End,
            Avalonia.Input.Key.Enter => PhysicalKey.Enter,
            Avalonia.Input.Key.Space => PhysicalKey.Space,
            Avalonia.Input.Key.PageDown => PhysicalKey.PageDown,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        ResponsiveLayoutTests.Render();
    }

    internal static Control? Focused(TopLevel window) => window.FocusManager?.GetFocusedElement() as Control;

    /// <summary>Every text a reader can see or hear in a subtree: texts, runs, tooltips and accessible names.</summary>
    internal static ImmutableArray<string> ExposedTexts(Control root)
    {
        List<string> texts = [];
        foreach (Control control in root.GetVisualDescendants().OfType<Control>().Prepend(root))
        {
            Add(AutomationProperties.GetName(control));
            Add(AutomationProperties.GetHelpText(control));
            Add(ToolTip.GetTip(control)?.ToString());
            switch (control)
            {
                case TextBlock text:
                    Add(text.Text);
                    foreach (Inline inline in text.Inlines ?? [])
                    {
                        Add((inline as Run)?.Text);
                    }

                    break;
                case HeaderedContentControl headered:
                    Add(headered.Header?.ToString());
                    break;
                case ContentControl content when content.Content is string value:
                    Add(value);
                    break;
                case Views.Charts.HeatmapCell cell:
                    Add(cell.DisplayText);
                    break;
            }
        }

        return [.. texts];

        void Add(string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                texts.Add(value);
            }
        }
    }
}

/// <summary>Records what the charts ask of the result screen.</summary>
internal sealed class RecordingChartActions : IResultsChartActions
{
    internal List<ResultsRowFilter> Filters { get; } = [];

    internal int ClearCount { get; private set; }

    internal List<(int Row, string? QuestionId)> Opened { get; } = [];

    internal ResultsChartsViewModel? Charts { get; set; }

    internal ResultChartDataSet? Data { get; set; }

    public void ApplyRowFilter(ResultsRowFilter filter)
    {
        Filters.Add(filter);
        Charts?.Update(Data, filter, Charts.SelectedSourceRow);
    }

    public void ClearRowFilter()
    {
        ClearCount++;
        Charts?.Update(Data, null, Charts.SelectedSourceRow);
    }

    public void OpenRow(int sourceRow, string? questionId)
    {
        Opened.Add((sourceRow, questionId));
        Charts?.SetSelection(sourceRow);
    }
}
