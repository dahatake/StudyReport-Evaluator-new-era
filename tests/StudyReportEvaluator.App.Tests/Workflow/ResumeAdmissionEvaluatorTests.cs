using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workflow;

// Requirements: FR-031 (AC-032)
public sealed class ResumeAdmissionEvaluatorTests
{
    [Theory]
    [InlineData(null, null, true)]
    [InlineData("long-context", "long-context", true)]
    [InlineData(null, "long-context", false)]
    [InlineData("long-context", null, false)]
    public void Resume_requires_the_same_context_tier(string? saved, string? requested, bool expected)
    {
        var definition = Definition();
        var snapshot = QuantificationSnapshot.Create(definition);
        var plan = new EvaluationPlanBuilder().Build(snapshot, U01TestSupport.ValidateMapping(definition).Mapping!);
        InputSnapshot input = new(new string('A', 64), 123, DateTimeOffset.UnixEpoch);
        var checkpoint = Envelope(snapshot, input, Runtime()) with { ContextTier = saved };
        var report = ResumeAdmissionEvaluator.Evaluate(checkpoint, checkpoint.PartialPath, snapshot, plan, input,
            checkpoint.InputPath, checkpoint.NormalModelId, null, Runtime(), requested);
        Assert.Equal(expected, report.CanResume);
        Assert.Equal(expected ? null : CheckpointAdmissionStatusCodes.ModelMismatch, report.BlockingStatusCode);
    }

    [Fact]
    public void Matching_checkpoint_is_admitted()
    {
        QuantificationDefinition definition = Definition();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
        EvaluationPlan plan = new EvaluationPlanBuilder().Build(
            snapshot,
            U01TestSupport.ValidateMapping(definition).Mapping!);
        InputSnapshot input = new(new string('A', 64), 123, DateTimeOffset.UnixEpoch);
        CheckpointRuntimeIdentity runtime = Runtime();
        CheckpointEnvelope checkpoint = Envelope(snapshot, input, runtime);

        ResumeAdmissionReport report = ResumeAdmissionEvaluator.Evaluate(
            checkpoint, checkpoint.PartialPath, snapshot, plan, input,
            checkpoint.InputPath, checkpoint.NormalModelId, runtime);

        Assert.True(report.CanResume);
        Assert.Null(report.BlockingStatusCode);
        Assert.All(report.Findings, finding => Assert.True(finding.IsSatisfied));
        AssertNoContent(report, checkpoint);
    }

    [Fact]
    public void Different_definition_is_rejected_without_reporting_content()
    {
        QuantificationDefinition original = Definition();
        QuantificationSnapshot originalSnapshot = QuantificationSnapshot.Create(original);
        InputSnapshot input = new(new string('A', 64), 123, DateTimeOffset.UnixEpoch);
        CheckpointEnvelope checkpoint = Envelope(originalSnapshot, input, Runtime());
        QuantificationDefinition changed = original with { Revision = "changed" };
        QuantificationSnapshot changedSnapshot = QuantificationSnapshot.Create(changed);
        EvaluationPlan changedPlan = new EvaluationPlanBuilder().Build(
            changedSnapshot,
            U01TestSupport.ValidateMapping(changed).Mapping!);

        ResumeAdmissionReport report = ResumeAdmissionEvaluator.Evaluate(
            checkpoint, checkpoint.PartialPath, changedSnapshot, changedPlan, input,
            checkpoint.InputPath, checkpoint.NormalModelId, Runtime());

        Assert.False(report.CanResume);
        Assert.Equal(CheckpointAdmissionStatusCodes.DefinitionMismatch, report.BlockingStatusCode);
        ResumeAdmissionFinding definition = Assert.Single(report.Findings,
            finding => finding.Item == ResumeAdmissionItem.Definition);
        Assert.False(definition.IsSatisfied);
        Assert.DoesNotContain(checkpoint.DefinitionCanonicalJson, definition.Description, StringComparison.Ordinal);
        AssertNoContent(report, checkpoint);
    }

    [Theory]
    [InlineData("low", "low", "model-test", true)]
    [InlineData("low", "medium", "model-test", false)]
    [InlineData("low", "low", "auto", false)]
    public void Reasoning_effort_and_reference_model_are_part_of_resume_model_identity(
        string? savedEffort,
        string? requestedEffort,
        string referenceModelId,
        bool expectedCanResume)
    {
        QuantificationDefinition definition = Definition();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
        EvaluationPlan plan = new EvaluationPlanBuilder().Build(
            snapshot,
            U01TestSupport.ValidateMapping(definition).Mapping!);
        InputSnapshot input = new(new string('A', 64), 123, DateTimeOffset.UnixEpoch);
        CheckpointRuntimeIdentity runtime = Runtime();
        CheckpointEnvelope checkpoint = Envelope(snapshot, input, runtime) with
        {
            ReferenceModelId = referenceModelId,
            ReasoningEffort = savedEffort,
        };

        ResumeAdmissionReport report = ResumeAdmissionEvaluator.Evaluate(
            checkpoint, checkpoint.PartialPath, snapshot, plan, input,
            checkpoint.InputPath, checkpoint.NormalModelId, requestedEffort, runtime);

        Assert.Equal(expectedCanResume, report.CanResume);
        ResumeAdmissionFinding model = Assert.Single(report.Findings,
            finding => finding.Item == ResumeAdmissionItem.NormalModel);
        Assert.Equal(expectedCanResume, model.IsSatisfied);
        if (!expectedCanResume)
        {
            Assert.Equal(CheckpointAdmissionStatusCodes.ModelMismatch, report.BlockingStatusCode);
        }
    }

    [Theory]
    [InlineData(nameof(ResumeAdmissionItem.PartialPath), CheckpointAdmissionStatusCodes.InputMismatch)]
    [InlineData(nameof(ResumeAdmissionItem.InputIdentity), CheckpointAdmissionStatusCodes.InputMismatch)]
    [InlineData(nameof(ResumeAdmissionItem.NormalModel), CheckpointAdmissionStatusCodes.ModelMismatch)]
    [InlineData(nameof(ResumeAdmissionItem.Runtime), CheckpointAdmissionStatusCodes.RuntimeMismatch)]
    [InlineData(nameof(ResumeAdmissionItem.CheckpointShape), CheckpointStatusCodes.Invalid)]
    public void Each_single_mismatch_blocks_only_its_item_without_reporting_content(
        string itemName, string expectedCode)
    {
        ResumeAdmissionItem item = Enum.Parse<ResumeAdmissionItem>(itemName);
        QuantificationDefinition definition = Definition();
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
        EvaluationPlan plan = new EvaluationPlanBuilder().Build(
            snapshot,
            U01TestSupport.ValidateMapping(definition).Mapping!);
        InputSnapshot input = new(new string('A', 64), 123, DateTimeOffset.UnixEpoch);
        CheckpointEnvelope checkpoint = Envelope(snapshot, input, Runtime()) with
        {
            References =
            [
                new CheckpointReference
                {
                    QuestionId = "Q1",
                    Answer = SavedAnswer,
                    // A cancelled reference cannot be reused, so only the shape case changes the status.
                    StatusCode = item == ResumeAdmissionItem.CheckpointShape
                        ? ResultsStatusCodes.Cancelled
                        : ResultsStatusCodes.Success,
                },
            ],
            CompletedRows =
            [
                new CheckpointCompletedRow
                {
                    SourceRowNumber = plan.Mapping.FirstDataRow,
                    NormalResults =
                    [
                        new CheckpointNormalResult
                        {
                            QuestionId = "Q1",
                            EvaluatorId = "E1",
                            StatusCode = ResultsStatusCodes.Success,
                            AcceptedResult = new QuantificationResult
                            {
                                EvaluatorId = "E1",
                                Criteria =
                                [
                                    new CriterionQuantificationResult
                                    {
                                        CriterionId = "C1",
                                        RawScore = 5m,
                                        Reason = ReasonCanary,
                                        Evidence = EvidenceCanary,
                                        EvidenceSourceColumnId = "A",
                                    },
                                ],
                            },
                        },
                    ],
                    SimilarityResults = [new CheckpointSimilarityResult { QuestionId = "Q1", StatusCode = ResultsStatusCodes.AiTimeout }],
                },
            ],
        };

        ResumeAdmissionReport report = ResumeAdmissionEvaluator.Evaluate(
            checkpoint,
            item == ResumeAdmissionItem.PartialPath ? "C:\\result\\other.partial.xlsx" : checkpoint.PartialPath,
            snapshot,
            plan,
            item == ResumeAdmissionItem.InputIdentity ? new InputSnapshot(new string('B', 64), 123, DateTimeOffset.UnixEpoch) : input,
            checkpoint.InputPath,
            item == ResumeAdmissionItem.NormalModel ? "model-other" : checkpoint.NormalModelId,
            item == ResumeAdmissionItem.Runtime ? Runtime() with { CliVersion = "1.0.80" } : Runtime());

        Assert.False(report.CanResume);
        Assert.Equal(expectedCode, report.BlockingStatusCode);
        Assert.Equal(Enum.GetValues<ResumeAdmissionItem>(), report.Findings.Select(finding => finding.Item));
        Assert.Equal([item], report.Findings.Where(finding => !finding.IsSatisfied).Select(finding => finding.Item));
        AssertNoContent(report, checkpoint);
    }

    [Fact]
    public void Multiple_mismatches_report_every_item_and_block_with_the_original_priority()
    {
        QuantificationDefinition original = Definition();
        QuantificationSnapshot originalSnapshot = QuantificationSnapshot.Create(original);
        InputSnapshot input = new(new string('A', 64), 123, DateTimeOffset.UnixEpoch);
        CheckpointEnvelope checkpoint = Envelope(originalSnapshot, input, Runtime());
        QuantificationDefinition changed = original with { Revision = "changed" };
        QuantificationSnapshot changedSnapshot = QuantificationSnapshot.Create(changed);
        EvaluationPlan changedPlan = new EvaluationPlanBuilder().Build(
            changedSnapshot,
            U01TestSupport.ValidateMapping(changed).Mapping!);
        CheckpointRuntimeIdentity otherRuntime = Runtime() with { SdkInformationalVersion = "1.0.12" };

        // Same order as the pre-refactoring DurableQuantificationOrchestrator.ValidateResumeAdmission:
        // input, definition, model, runtime, then saved shape.
        ResumeAdmissionReport all = ResumeAdmissionEvaluator.Evaluate(
            checkpoint, checkpoint.PartialPath, changedSnapshot, changedPlan,
            new InputSnapshot(new string('A', 64), 124, DateTimeOffset.UnixEpoch), checkpoint.InputPath, "model-other", otherRuntime);
        ResumeAdmissionReport withoutInput = ResumeAdmissionEvaluator.Evaluate(
            checkpoint, checkpoint.PartialPath, changedSnapshot, changedPlan,
            input, checkpoint.InputPath, "model-other", otherRuntime);
        ResumeAdmissionReport modelAndRuntime = ResumeAdmissionEvaluator.Evaluate(
            checkpoint, checkpoint.PartialPath, originalSnapshot, new EvaluationPlanBuilder().Build(
                originalSnapshot, U01TestSupport.ValidateMapping(original).Mapping!),
            input, checkpoint.InputPath, "model-other", otherRuntime);

        Assert.Equal(CheckpointAdmissionStatusCodes.InputMismatch, all.BlockingStatusCode);
        Assert.Equal(
            [
                ResumeAdmissionItem.InputIdentity, ResumeAdmissionItem.Definition, ResumeAdmissionItem.NormalModel,
                ResumeAdmissionItem.Runtime, ResumeAdmissionItem.CheckpointShape,
            ],
            all.Findings.Where(finding => !finding.IsSatisfied).Select(finding => finding.Item));
        Assert.Equal(CheckpointAdmissionStatusCodes.DefinitionMismatch, withoutInput.BlockingStatusCode);
        Assert.Equal(CheckpointAdmissionStatusCodes.ModelMismatch, modelAndRuntime.BlockingStatusCode);
        Assert.Equal(
            [ResumeAdmissionItem.NormalModel, ResumeAdmissionItem.Runtime],
            modelAndRuntime.Findings.Where(finding => !finding.IsSatisfied).Select(finding => finding.Item));
        CheckpointEnvelope cancelledReference = checkpoint with
        {
            References = [new CheckpointReference { QuestionId = "Q1", StatusCode = ResultsStatusCodes.Cancelled }],
        };
        ResumeAdmissionReport runtimeAndShape = ResumeAdmissionEvaluator.Evaluate(
            cancelledReference, checkpoint.PartialPath, originalSnapshot, new EvaluationPlanBuilder().Build(
                originalSnapshot, U01TestSupport.ValidateMapping(original).Mapping!),
            input, checkpoint.InputPath, checkpoint.NormalModelId, otherRuntime);
        Assert.Equal(CheckpointAdmissionStatusCodes.RuntimeMismatch, runtimeAndShape.BlockingStatusCode);
        Assert.Equal(
            [ResumeAdmissionItem.Runtime, ResumeAdmissionItem.CheckpointShape],
            runtimeAndShape.Findings.Where(finding => !finding.IsSatisfied).Select(finding => finding.Item));
        Assert.All(new[] { all, withoutInput, modelAndRuntime, runtimeAndShape },
            report => AssertNoContent(report, checkpoint));
    }

    private const string SavedAnswer = "SAVED-REFERENCE-ANSWER-CANARY";
    private const string PromptCanary = "PROMPT-CANARY";
    private const string ReasonCanary = "REASON-CANARY";
    private const string EvidenceCanary = "EVIDENCE-CANARY";

    private static void AssertNoContent(ResumeAdmissionReport report, CheckpointEnvelope checkpoint)
    {
        Assert.Contains("<redacted>", report.ToString(), StringComparison.Ordinal);
        Assert.All(report.Findings, finding =>
        {
            Assert.Contains("<redacted>", finding.ToString(), StringComparison.Ordinal);
            foreach (string text in new[] { finding.Description, finding.DisplayText })
            {
                Assert.DoesNotContain(SavedAnswer, text, StringComparison.Ordinal);
                Assert.DoesNotContain(PromptCanary, text, StringComparison.Ordinal);
                Assert.DoesNotContain(ReasonCanary, text, StringComparison.Ordinal);
                Assert.DoesNotContain(EvidenceCanary, text, StringComparison.Ordinal);
                Assert.DoesNotContain(checkpoint.DefinitionCanonicalJson, text, StringComparison.Ordinal);
                Assert.DoesNotContain(checkpoint.PartialPath, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(checkpoint.InputPath, text, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    private static QuantificationDefinition Definition() => U01TestSupport.Definition(
        2,
        2,
        U01TestSupport.Question("Q1", "A", ["B"], true, U01TestSupport.Evaluator("E1", "C1", customPrompt: PromptCanary + " {回答} {評価項目}")));

    private static CheckpointRuntimeIdentity Runtime() => new()
    {
        ApplicationIdentity = "StudyReportEvaluator.App/0.8.6",
        CliVersion = "1.0.79",
        CliSha256 = new string('A', 64),
        SdkInformationalVersion = "1.0.11",
    };

    private static CheckpointEnvelope Envelope(
        QuantificationSnapshot snapshot,
        InputSnapshot input,
        CheckpointRuntimeIdentity runtime) => new()
    {
        InputPath = "C:\\resume-input.xlsx",
        Input = input,
        DefinitionCanonicalJson = snapshot.CanonicalJson,
        DefinitionSha256 = snapshot.Sha256,
        NormalModelId = "model-test",
        ReferenceModelId = "model-test",
        Runtime = runtime,
        FinalPath = "C:\\result\\eval.xlsx",
        PartialPath = "C:\\result\\eval.partial.xlsx",
        StartedAtUtc = DateTimeOffset.UnixEpoch,
        SavedAtUtc = DateTimeOffset.UnixEpoch,
    };
}
