using System.Text.Json;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workflow;

public sealed class LivePreviewOrchestratorTests
{
    [Fact]
    public async Task New_run_announces_every_row_as_planned_then_reports_each_row_and_stores_no_preview_text()
    {
        QuantificationDefinition definition = Definition(2, 4);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        FakeCheckpointStore checkpoint = new();
        List<LivePreviewUpdate> updates = [];
        object gate = new();

        RunSummary summary = await Orchestrator(checkpoint).RunAsync(
            Request(definition, metadata),
            livePreview: update =>
            {
                lock (gate)
                {
                    updates.Add(update);
                }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        LivePreviewUpdate planned = updates[0];
        Assert.Null(planned.Row);
        Assert.Equal((2, 4, 3), (planned.FirstDataRow, planned.LastDataRow, planned.ItemsPerRow));
        Assert.All(updates, update =>
        {
            Assert.Equal(2, update.FirstDataRow);
            Assert.Equal(4, update.LastDataRow);
        });
        Assert.Equal(
            [2, 3, 4],
            updates.Where(update => update.Row?.State == LivePreviewRowState.Completed)
                .Select(update => update.Row!.SourceRowNumber)
                .OrderBy(number => number));
        LivePreviewRow row3 = updates.Last(update => update.Row!.SourceRowNumber == 3).Row!;
        Assert.Equal("answer-3", row3.Items[0].Cells[0].Value);
        Assert.Equal("7", row3.Items[0].Measures[0].Value);

        string stored = JsonSerializer.Serialize(checkpoint.Current);
        Assert.DoesNotContain("answer-3", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("special-3", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resume_lists_stored_rows_as_restored_without_text_and_evaluates_only_the_rest()
    {
        QuantificationDefinition definition = Definition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot());
        FakeCheckpointStore checkpoint = new();
        using CancellationTokenSource firstCancellation = new();
        checkpoint.UpdateCallback = envelope =>
        {
            if (envelope.CompletedRows.Length == 1)
            {
                firstCancellation.Cancel();
            }
        };
        RunSummary first = await Orchestrator(checkpoint, input).RunAsync(
            Request(definition, metadata),
            cancellationToken: firstCancellation.Token);
        Assert.Equal(QuantificationRunStatusCodes.Cancelled, first.StatusCode);

        checkpoint.UpdateCallback = null;
        List<LivePreviewUpdate> updates = [];
        RunSummary resumed = await Orchestrator(checkpoint, input).RunAsync(
            Request(definition, metadata) with { ResumePartialPath = checkpoint.Current!.PartialPath },
            livePreview: updates.Add,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Success, resumed.StatusCode);
        Assert.Null(updates[0].Row);
        LivePreviewRow restored = updates[1].Row!;
        Assert.Equal(2, restored.SourceRowNumber);
        Assert.Equal(LivePreviewRowState.RestoredFromCheckpoint, restored.State);
        Assert.Equal(3, restored.SettledCount);
        Assert.All(restored.Items, item => Assert.False(item.TextAvailable));
        Assert.Equal("通常 7 / 固有 0.8 / 類似 0.8571", LivePreviewFormatter.ValueSummary(restored));
        string detail = LivePreviewFormatter.DetailText(restored);
        Assert.Contains(LivePreviewFormatter.TextNotStoredText, detail, StringComparison.Ordinal);
        Assert.DoesNotContain("answer-2", detail, StringComparison.Ordinal);
        Assert.Equal([2, 3], updates.Skip(1).Select(update => update.Row!.SourceRowNumber).Distinct());
        Assert.Equal(LivePreviewRowState.Completed, updates[^1].Row!.State);
        Assert.Equal(3, updates[^1].Row!.SourceRowNumber);
    }

    private static DurableQuantificationOrchestrator Orchestrator(
        FakeCheckpointStore checkpoint,
        ScriptedInputSnapshots? input = null) =>
        new(
            new ScriptedRowSource((request, _) => Task.FromResult(new EvaluationRowData(
                request.SourceRowNumber,
                new Dictionary<string, string?>
                {
                    ["A"] = $"answer-{request.SourceRowNumber}",
                    ["B"] = $"special-{request.SourceRowNumber}",
                }))),
            new ScriptedRunner((payload, _, _) => Task.FromResult(
                EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload, _ => 7m)))),
            new FakeReferenceRunner(),
            new FakeSpecialRunner(),
            input ?? new ScriptedInputSnapshots(U01TestSupport.InputSnapshot()),
            checkpoint,
            new FakePathPlanner(),
            new FakeFinalizer(),
            new FakeCleaner(),
            new FixedTimeProvider());

    private static DurableQuantificationRunRequest Request(
        QuantificationDefinition definition,
        WorkbookMetadata metadata) =>
        new()
        {
            Run = new QuantificationRunRequest
            {
                DraftDefinition = definition,
                WorkbookMetadata = metadata,
                InputPath = "C:\\PRIVATE\\input.xlsx",
                ModelId = "model-test",
                MaximumPromptTokens = 64_000,
                MaximumContextWindowTokens = 128_000,
                MaxConcurrency = 1,
            },
            Runtime = new CheckpointRuntimeIdentity
            {
                ApplicationIdentity = "StudyReportEvaluator.App/4.0.0",
                CliVersion = "1.0.79",
                CliSha256 = new string('A', 64),
                SdkInformationalVersion = "1.0.11",
            },
        };

    private static QuantificationDefinition Definition(int firstRow, int lastRow)
    {
        SpecialEvaluationDefinition special = new()
        {
            Id = "S1",
            DisplayName = "Special",
            PrimarySourceColumn = "B",
            SupportingSourceColumns = ["A"],
            PromptTemplate = "Special {回答} {補助情報}",
        };
        QuantificationDefinition source = U01TestSupport.Definition(
            firstRow,
            lastRow,
            U01TestSupport.Question("Q1", "A", ["B"], true, U01TestSupport.Evaluator("E1", "C1")));
        return source with
        {
            BasePoints = 88m,
            SpecialPoints = 10m,
            Questions = [source.Questions[0] with { SpecialEvaluations = [special] }],
        };
    }

    private sealed class FakeReferenceRunner : IReferenceAnswerOperationRunner
    {
        public Task<AuxiliaryOperationResult<ReferenceAnswerResult>> EvaluateAsync(
            SafeReferenceAnswerPayload payload,
            CancellationToken cancellationToken) =>
            Task.FromResult(AuxiliaryOperationResult<ReferenceAnswerResult>.Succeeded(
                new ReferenceAnswerResult { QuestionId = payload.QuestionId, Answer = "reference answer" }));
    }

    private sealed class FakeSpecialRunner : ISpecialEvaluationOperationRunner
    {
        public Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(
            SafeSpecialEvaluationPayload payload,
            string modelId,
            CancellationToken cancellationToken) =>
            Task.FromResult(AuxiliaryOperationResult<SpecialQuantificationResult>.Succeeded(
                new SpecialQuantificationResult
                {
                    SpecialEvaluationId = payload.SpecialEvaluationId,
                    Score = 0.8m,
                    Reason = "special reason",
                    Evidence = string.Empty,
                    EvidenceSource = EvidenceSourceKind.None,
                    EvidenceSourceColumnId = string.Empty,
                }));
    }

    private sealed class FakeCheckpointStore : ICheckpointStore
    {
        internal CheckpointEnvelope? Current { get; private set; }

        internal Action<CheckpointEnvelope>? UpdateCallback { get; set; }

        public CheckpointSaveResult Create(CheckpointEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Current = envelope;
            return CheckpointSaveResult.Succeeded(createdNew: true);
        }

        public CheckpointSaveResult Update(CheckpointEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Current = envelope;
            UpdateCallback?.Invoke(envelope);
            return CheckpointSaveResult.Succeeded(createdNew: false);
        }

        public CheckpointLoadResult Load(string partialPath, CancellationToken cancellationToken = default) =>
            Current is null
                ? CheckpointLoadResult.Failed(CheckpointStatusCodes.Invalid)
                : CheckpointLoadResult.Succeeded(Current);
    }

    private sealed class FakePathPlanner : IOutputPathPlanner
    {
        private readonly OutputPathReservation reservation = OutputPathReservation.Create(
            "C:\\PRIVATE\\result",
            "C:\\PRIVATE\\result\\eval-20260902-1435.xlsx",
            "C:\\PRIVATE\\result\\eval-20260902-1435.partial.xlsx");

        public OutputPathReservation Reserve(string inputPath, DateTimeOffset localTime, string? outputDirectory = null) =>
            reservation;
    }

    private sealed class FakeFinalizer : IDurableRunFinalizer
    {
        public DurableFinalizationResult Finalize(
            RunSummary summary,
            CheckpointEnvelope checkpoint,
            CancellationToken cancellationToken) =>
            DurableFinalizationResult.Succeeded(checkpoint.FinalPath);
    }

    private sealed class FakeCleaner : IPartialCheckpointCleaner
    {
        public bool TryDelete(string partialPath) => true;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 9, 2, 5, 35, 0, TimeSpan.Zero).UtcTicks;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() =>
            new(Interlocked.Add(ref ticks, TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
