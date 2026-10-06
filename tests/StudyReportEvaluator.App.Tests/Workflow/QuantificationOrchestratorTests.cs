using System.Collections.Immutable;
using System.Globalization;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Validation;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workflow;

public sealed class QuantificationOrchestratorTests
{
    [Fact]
    public async Task Run_validates_then_captures_input_and_rechecks_after_ordered_snapshot_bound_execution()
    {
        QuantificationDefinition definition = OneQuestionDefinition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        List<string> events = [];
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot(), unchanged: true, events);
        ScriptedRowSource rows = new((request, _) =>
        {
            events.Add($"row:{request.SourceRowNumber}");
            return Task.FromResult(Row(request, $"answer-{request.SourceRowNumber}", "support"));
        });
        ScriptedRunner runner = new((payload, _, _) =>
        {
            events.Add("runner:" + payload.EvaluatorId);
            return Task.FromResult(EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, _ => 4m),
                tokenUsage: new EvaluationTokenUsage(
                    true,
                    inputTokens: 10,
                    outputTokens: 2,
                    reasoningTokens: 1,
                    cacheReadTokens: 3,
                    cacheWriteTokens: 4)));
        });

        RunSummary summary = await new QuantificationOrchestrator(
            rows,
            runner,
            input).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            ["capture", "row:2", "row:3", "recheck", "row:2", "runner:E1", "row:3", "runner:E1", "recheck"],
            events);
        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        Assert.True(summary.IsExportReady);
        Assert.Same(summary.Snapshot, summary.Plan.Snapshot);
        Assert.Same(summary.Plan, summary.Schedule.Plan);
        Assert.Same(input.Snapshot, summary.InputSnapshot);
        Assert.Equal(summary.Snapshot.Sha256, summary.DefinitionSha256);
        Assert.True(summary.Snapshot.HasValidHash());
        Assert.Equal(2, summary.PlannedEvaluationCount);
        Assert.Equal(2, summary.CompletedEvaluationCount);
        Assert.Equal(2, summary.SucceededCount);
        Assert.Equal(0, summary.FailureCount);
        Assert.Equal(2, summary.UsageObservedUnitCount);
        Assert.Equal(20, summary.TokenUsage.InputTokens);
        Assert.Equal(4, summary.TokenUsage.OutputTokens);
        Assert.Equal(2, summary.TokenUsage.ReasoningTokens);
        Assert.Equal(6, summary.TokenUsage.CacheReadTokens);
        Assert.Equal(8, summary.TokenUsage.CacheWriteTokens);
        Assert.Equal(TimeSpan.Zero, summary.StartedAtUtc.Offset);
        Assert.Equal(TimeSpan.Zero, summary.EndedAtUtc.Offset);
        Assert.True(summary.EndedAtUtc >= summary.StartedAtUtc);

        RunOutputPreparation output = summary.PrepareOutput();
        Assert.True(output.IsExportReady);
        Assert.False(output.IsPartial);
        Assert.Same(summary.Snapshot, output.Snapshot);
        Assert.Same(summary.InputSnapshot, output.InputSnapshot);
        Assert.Equal([2, 3], output.Rows.Select(row => row.SourceRowNumber));
        Assert.All(output.Rows, row => Assert.True(Assert.Single(row.Questions).Scorable));
        Assert.All(
            output.Rows,
            row => Assert.Equal(
                ResultsStatusCodes.Success,
                Assert.Single(Assert.Single(row.Questions).Evaluators).Status));
        IList<ResultsSheetRowInput> rowsList = Assert.IsAssignableFrom<IList<ResultsSheetRowInput>>(output.Rows);
        Assert.True(rowsList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => rowsList.Add(output.Rows[0]));
    }

    [Fact]
    public async Task Invalid_definition_or_mapping_fails_before_input_capture_or_AI_dispatch()
    {
        QuantificationDefinition valid = OneQuestionDefinition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(valid).Metadata;
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot());
        ScriptedRowSource rows = new((_, _) => throw new InvalidOperationException("must not read"));
        ScriptedRunner runner = new((_, _, _) => throw new InvalidOperationException("must not dispatch"));
        QuantificationOrchestrator orchestrator = new(rows, runner, input);
        QuantificationDefinition invalidDefinition = valid with
        {
            Questions = [valid.Questions[0] with { Enabled = false }],
        };

        await Assert.ThrowsAsync<QuantificationDefinitionValidationException>(() =>
            orchestrator.RunAsync(
                Request(invalidDefinition, metadata),
                cancellationToken: TestContext.Current.CancellationToken));
        QuantificationDefinition invalidMapping = valid with { SourceSheet = "Missing" };
        await Assert.ThrowsAsync<QuantificationMappingValidationException>(() =>
            orchestrator.RunAsync(
                Request(invalidMapping, metadata),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(0, input.CaptureCount);
        Assert.Equal(0, input.RecheckCount);
        Assert.Empty(rows.Requests);
        Assert.Empty(runner.Payloads);
    }

    [Fact]
    public async Task Invalid_custom_prompt_fails_before_input_capture_row_read_or_AI_dispatch()
    {
        const string promptCanary = "PRIVATE-PROMPT-CANARY {回答} {未知}";
        QuantificationDefinition valid = OneQuestionDefinition(2, 2);
        QuantificationDefinition invalidPrompt = valid with
        {
            Questions =
            [
                valid.Questions[0] with
                {
                    Evaluators =
                    [
                        valid.Questions[0].Evaluators[0] with
                        {
                            CustomPromptTemplate = promptCanary,
                        },
                    ],
                },
            ],
        };
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(valid).Metadata;
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot());
        ScriptedRowSource rows = new((_, _) => throw new InvalidOperationException("must not read"));
        ScriptedRunner runner = new((_, _, _) => throw new InvalidOperationException("must not dispatch"));

        QuantificationDefinitionValidationException exception = await Assert.ThrowsAsync<QuantificationDefinitionValidationException>(() =>
            new QuantificationOrchestrator(rows, runner, input).RunAsync(
                Request(invalidPrompt, metadata),
                cancellationToken: TestContext.Current.CancellationToken));

        DefinitionValidationError error = Assert.Single(
            exception.Errors,
            item => item.Code == "UNKNOWN_PLACEHOLDER");
        Assert.Equal("CustomPromptTemplate", error.Field);
        Assert.Equal(0, input.CaptureCount);
        Assert.Equal(0, input.RecheckCount);
        Assert.Empty(rows.Requests);
        Assert.Empty(runner.Payloads);
        Assert.DoesNotContain(promptCanary, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(promptCanary, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workbook_capacity_failure_stops_before_input_capture_row_read_and_AI_dispatch()
    {
        const int criterionCount = 256;
        QuantificationDefinition single = OneQuestionDefinition(2, 2);
        ImmutableArray<CriterionDefinition> criteria = Enumerable.Range(1, criterionCount)
            .Select(index => new CriterionDefinition
            {
                Id = $"C{index.ToString(CultureInfo.InvariantCulture)}",
                DisplayName = $"Criterion {index.ToString(CultureInfo.InvariantCulture)}",
                Description = "Synthetic capacity criterion",
                Weight = 1m,
                Enabled = true,
            })
            .ToImmutableArray();
        QuantificationDefinition definition = single with
        {
            Questions =
            [
                single.Questions[0] with
                {
                    Evaluators =
                    [
                        single.Questions[0].Evaluators[0] with { Criteria = criteria },
                    ],
                },
            ],
        };
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot());
        ScriptedRowSource rows = new((_, _) => throw new InvalidOperationException("must not read"));
        ScriptedRunner runner = new((_, _, _) => throw new InvalidOperationException("must not dispatch"));

        QuantificationRunPreflightException exception = await Assert.ThrowsAsync<QuantificationRunPreflightException>(() =>
            new QuantificationOrchestrator(rows, runner, input).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(exception.Errors, error => error.Code == "FUNCTION_ARGUMENT_LIMIT_EXCEEDED");
        Assert.Equal(0, input.CaptureCount);
        Assert.Equal(0, input.RecheckCount);
        Assert.Empty(rows.Requests);
        Assert.Empty(runner.Payloads);
        Assert.Contains("<redacted>", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exact_input_drift_or_recheck_failure_marks_INPUT_CHANGED_and_blocks_export_ready_data()
    {
        QuantificationDefinition definition = OneQuestionDefinition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedRowSource rows = new((request, _) => Task.FromResult(Row(request, "answer", "support")));
        ScriptedRunner runner = new((payload, _, _) => Task.FromResult(
            EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload))));
        ScriptedInputSnapshots changed = new(U01TestSupport.InputSnapshot())
        {
            RecheckOutcomes = new Queue<bool>([true, false]),
        };

        RunSummary drift = await new QuantificationOrchestrator(rows, runner, changed).RunAsync(
            Request(definition, metadata),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.InputChanged, drift.StatusCode);
        Assert.False(drift.IsExportReady);
        RunOutputPreparation driftOutput = drift.PrepareOutput();
        Assert.False(driftOutput.IsExportReady);
        Assert.False(driftOutput.InputUnchanged);
        Assert.Empty(driftOutput.Rows);

        ScriptedInputSnapshots failedRecheck = new(U01TestSupport.InputSnapshot(), unchanged: true)
        {
            ThrowOnRecheck = true,
        };
        ScriptedRunner blockedRunner = new((_, _, _) => throw new InvalidOperationException("must not dispatch"));
        QuantificationRunPreflightException failure = await Assert.ThrowsAsync<QuantificationRunPreflightException>(() => new QuantificationOrchestrator(
            rows,
            blockedRunner,
            failedRecheck).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains(failure.Errors, error => error.Code == "INPUT_CHANGED_BEFORE_DISPATCH");
        Assert.Empty(blockedRunner.Payloads);
    }

    [Fact]
    public async Task Request_capacity_excess_is_measured_for_actual_rows_and_blocks_all_AI_dispatch()
    {
        const string privateAnswer = "PRIVATE-LONG-ANSWER-CANARY-日本語-😀";
        QuantificationDefinition definition = OneQuestionDefinition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot());
        ScriptedRowSource rows = new((request, _) => Task.FromResult(Row(request, privateAnswer, "support")));
        ScriptedRunner runner = new((_, _, _) => throw new InvalidOperationException("must not dispatch"));
        QuantificationRunRequest request = Request(definition, metadata) with
        {
            MaximumPromptTokens = 100,
            MaximumContextWindowTokens = 200,
        };

        QuantificationRunPreflightException exception = await Assert.ThrowsAsync<QuantificationRunPreflightException>(() =>
            new QuantificationOrchestrator(rows, runner, input).RunAsync(
                request,
                cancellationToken: TestContext.Current.CancellationToken));

        ExecutionCapacityError error = Assert.Single(
            exception.Errors,
            item => item.Code == "REQUEST_CONTEXT_BUDGET_EXCEEDED");
        Assert.Equal("AppOwnedRequestUtf8Bytes", error.Field);
        Assert.True(long.Parse(error.ActualDimension, CultureInfo.InvariantCulture) > 100);
        Assert.Equal("80", error.Limit);
        Assert.Equal(1, input.CaptureCount);
        Assert.Equal(0, input.RecheckCount);
        Assert.Single(rows.Requests);
        Assert.Empty(runner.Payloads);
        Assert.DoesNotContain(privateAnswer, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(privateAnswer, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Worst_case_retry_budget_over_twenty_thousand_stops_before_input_access()
    {
        QuantificationDefinition definition = OneQuestionDefinition(2, 6_668);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot());
        ScriptedRowSource rows = new((_, _) => throw new InvalidOperationException("must not read"));
        ScriptedRunner runner = new((_, _, _) => throw new InvalidOperationException("must not dispatch"));

        QuantificationRunPreflightException exception = await Assert.ThrowsAsync<QuantificationRunPreflightException>(() =>
            new QuantificationOrchestrator(rows, runner, input).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken));

        ExecutionCapacityError error = Assert.Single(
            exception.Errors,
            item => item.Code == "ATTEMPT_BUDGET_TOO_LARGE");
        Assert.Equal("20001", error.ActualDimension);
        Assert.Equal("20000", error.Limit);
        Assert.Equal(0, input.CaptureCount);
        Assert.Empty(rows.Requests);
        Assert.Empty(runner.Payloads);
    }

    [Fact]
    public async Task Missing_model_prompt_limit_fails_before_input_or_row_access()
    {
        QuantificationDefinition definition = OneQuestionDefinition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot());
        ScriptedRowSource rows = new((_, _) => throw new InvalidOperationException("must not read"));
        ScriptedRunner runner = new((_, _, _) => throw new InvalidOperationException("must not dispatch"));
        QuantificationRunRequest request = Request(definition, metadata) with
        {
            MaximumPromptTokens = 0,
        };

        QuantificationRunPreflightException exception = await Assert.ThrowsAsync<QuantificationRunPreflightException>(() =>
            new QuantificationOrchestrator(rows, runner, input).RunAsync(
                request,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(exception.Errors, error => error.Code == "MODEL_PROMPT_LIMIT_UNAVAILABLE");
        Assert.Equal(0, input.CaptureCount);
        Assert.Empty(rows.Requests);
        Assert.Empty(runner.Payloads);
    }

    [Fact]
    public async Task Unpublished_model_limits_pass_the_static_gate_and_reach_dispatch()
    {
        QuantificationDefinition definition = OneQuestionDefinition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot(), unchanged: true);
        ScriptedRowSource rows = new((request, _) =>
            Task.FromResult(Row(request, $"answer-{request.SourceRowNumber}", "support")));
        ScriptedRunner runner = new((payload, _, _) =>
            Task.FromResult(EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, _ => 4m))));
        QuantificationRunRequest request = Request(definition, metadata) with
        {
            MaximumPromptTokens = null,
            MaximumContextWindowTokens = null,
        };

        RunSummary summary = await new QuantificationOrchestrator(rows, runner, input).RunAsync(
            request,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        Assert.Equal(1, input.CaptureCount);
        Assert.NotEmpty(rows.Requests);
        Assert.NotEmpty(runner.Payloads);
    }

    [Fact]
    public async Task Cancelled_run_allows_partial_output_with_only_completed_AI_payloads_retained()
    {
        QuantificationDefinition definition = OneQuestionDefinition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedRowSource rows = new((request, _) => Task.FromResult(Row(request, "answer", "support")));
        using CancellationTokenSource cancellation = new();
        ScriptedRunner runner = new((payload, _, _) =>
        {
            EvaluationRunnerResult completed = EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, _ => 7m));
            cancellation.Cancel();
            return Task.FromResult(completed);
        });

        RunSummary summary = await new QuantificationOrchestrator(
            rows,
            runner,
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot())).RunAsync(
                Request(definition, metadata),
                cancellationToken: cancellation.Token);

        Assert.Equal(QuantificationRunStatusCodes.Cancelled, summary.StatusCode);
        Assert.True(summary.IsPartial);
        Assert.True(summary.IsExportReady);
        Assert.Equal(1, summary.SucceededCount);
        Assert.Equal(1, summary.CancelledCount);
        Assert.Single(runner.Payloads);

        RunOutputPreparation output = summary.PrepareOutput();
        Assert.True(output.IsExportReady);
        Assert.True(output.IsPartial);
        Assert.Equal(2, output.Rows.Length);
        EvaluatorResultInput completed = Assert.Single(Assert.Single(output.Rows[0].Questions).Evaluators);
        EvaluatorResultInput cancelled = Assert.Single(Assert.Single(output.Rows[1].Questions).Evaluators);
        Assert.Equal(ResultsStatusCodes.Success, completed.Status);
        Assert.Equal(7m, Assert.Single(completed.AiResult!.Criteria).RawScore);
        Assert.Equal(ResultsStatusCodes.Cancelled, cancelled.Status);
        Assert.Null(cancelled.AiResult);
    }

    [Fact]
    public async Task Mid_run_draft_replacement_cannot_change_current_payload_validation_override_or_formula_layout_contract()
    {
        CriterionDefinition originalCriterion = new()
        {
            Id = "C1",
            DisplayName = "Original criterion",
            Description = "ORIGINAL CRITERION TEXT",
            Weight = 4m,
            Range = new ScoreRange(1m, 5m),
            Enabled = true,
        };
        CriterionDefinition nextCriterion = new()
        {
            Id = "C2",
            DisplayName = "Next criterion",
            Description = "NEXT CRITERION TEXT",
            Weight = 8m,
            Range = new ScoreRange(70m, 80m),
            Enabled = false,
        };
        EvaluatorDefinition originalEvaluator = new()
        {
            Id = "E1",
            DisplayName = "Evaluator",
            Type = EvaluatorType.CustomPrompt,
            Weight = 3m,
            Range = new ScoreRange(0m, 10m),
            CustomPromptTemplate = "ORIGINAL PROMPT {回答} {補助情報} {評価項目}",
            Criteria = [originalCriterion, nextCriterion],
        };
        QuestionDefinition originalQuestion = new()
        {
            Id = "Q1",
            DisplayName = "Question",
            QuestionText = "ORIGINAL QUESTION",
            PrimarySourceColumn = "A",
            SupportingSourceColumns = ["B"],
            Points = 2m,
            Evaluators = [originalEvaluator],
        };
        QuantificationDefinition draft = U01TestSupport.Definition(2, 2, originalQuestion);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(draft).Metadata;
        ScriptedRowSource rows = new((request, _) =>
        {
            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase)
            {
                ["A"] = "original primary",
                ["B"] = "original support",
                ["C"] = "next primary",
            };
            return Task.FromResult(new EvaluationRowData(
                request.SourceRowNumber,
                request.SelectedColumns.Select(column =>
                    new KeyValuePair<string, string?>(column, values[column]))));
        });
        TaskCompletionSource firstPayloadBuilt = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int invocation = 0;
        ScriptedRunner runner = new(async (payload, _, token) =>
        {
            if (Interlocked.Increment(ref invocation) == 1)
            {
                firstPayloadBuilt.TrySetResult();
                await releaseFirst.Task.WaitAsync(token);
            }

            return EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, criterion => criterion.Range.Maximum));
        });
        ScriptedInputSnapshots input = new(U01TestSupport.InputSnapshot());
        QuantificationOrchestrator orchestrator = new(rows, runner, input);
        QuantificationRunRequest currentRequest = Request(draft, metadata);

        Task<RunSummary> currentRun = orchestrator.RunAsync(
            currentRequest,
            cancellationToken: TestContext.Current.CancellationToken);
        await firstPayloadBuilt.Task.WaitAsync(TestContext.Current.CancellationToken);

        CriterionDefinition editedOriginal = originalCriterion with
        {
            Description = "EDITED DISABLED CRITERION",
            Weight = 40m,
            Range = new ScoreRange(50m, 55m),
            Enabled = false,
        };
        CriterionDefinition editedNext = nextCriterion with { Enabled = true };
        EvaluatorDefinition editedEvaluator = originalEvaluator with
        {
            Weight = 30m,
            Range = new ScoreRange(50m, 60m),
            CustomPromptTemplate = "EDITED PROMPT {回答} {補助情報} {評価項目}",
            Criteria = [editedOriginal, editedNext],
        };
        QuestionDefinition editedQuestion = originalQuestion with
        {
            QuestionText = "EDITED QUESTION",
            PrimarySourceColumn = "C",
            SupportingSourceColumns = ["A"],
            Points = 20m,
            Evaluators = [editedEvaluator],
        };
        draft = draft with { Revision = "2", BasePoints = 80m, Questions = [editedQuestion] };
        releaseFirst.TrySetResult();
        RunSummary current = await currentRun;

        SafeEvaluationPayload currentPayload = runner.Payloads[0];
        Assert.Equal("A", currentPayload.PrimarySource.SourceColumnId);
        Assert.Equal("original primary", currentPayload.PrimarySource.Value);
        Assert.Equal(["B"], currentPayload.SupportingSources.Select(source => source.SourceColumnId));
        Assert.Contains("ORIGINAL PROMPT", currentPayload.RenderedPrompt, StringComparison.Ordinal);
        Assert.Contains("ORIGINAL CRITERION TEXT", currentPayload.RenderedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("EDITED", currentPayload.RenderedPrompt, StringComparison.Ordinal);
        ExpectedCriterion expectedCurrent = Assert.Single(currentPayload.ExpectedCriteria);
        Assert.Equal("C1", expectedCurrent.CriterionId);
        Assert.Equal(new ScoreRange(1m, 5m), expectedCurrent.Range);
        Assert.Equal(5m, Assert.Single(Assert.Single(current.Units).AcceptedResult!.Criteria).RawScore);
        Assert.Equal(2m, current.Snapshot.Definition.Questions[0].Points);
        Assert.Equal(3m, current.Snapshot.Definition.Questions[0].Evaluators[0].Weight);
        Assert.Equal(4m, current.Snapshot.Definition.Questions[0].Evaluators[0].Criteria[0].Weight);
        RunOutputPreparation currentOutput = current.PrepareOutput(
            [Override("C1", "5")]);
        Assert.True(currentOutput.IsExportReady);
        Assert.Same(current.Snapshot, currentOutput.Snapshot);
        QuantificationResult currentOutputResult = Assert.Single(
            Assert.Single(Assert.Single(currentOutput.Rows).Questions).Evaluators).AiResult!;
        Assert.Equal(["C1"], currentOutputResult.Criteria.Select(criterion => criterion.CriterionId));
        RunOverrideValidationResult editedOnlyOverride = current.ValidateOverrides(
            [Override("C2", "80")]);
        Assert.False(editedOnlyOverride.IsValid);
        Assert.Contains(editedOnlyOverride.Errors, error => error.Code == "OVERRIDE_CRITERION_NOT_ENABLED");

        RunSummary next = await orchestrator.RunAsync(
            Request(draft, metadata),
            cancellationToken: TestContext.Current.CancellationToken);
        SafeEvaluationPayload nextPayload = runner.Payloads[1];
        Assert.NotEqual(current.DefinitionSha256, next.DefinitionSha256);
        Assert.Equal("C", nextPayload.PrimarySource.SourceColumnId);
        Assert.Equal("next primary", nextPayload.PrimarySource.Value);
        Assert.Equal(["A"], nextPayload.SupportingSources.Select(source => source.SourceColumnId));
        Assert.Contains("EDITED PROMPT", nextPayload.RenderedPrompt, StringComparison.Ordinal);
        Assert.Contains("NEXT CRITERION TEXT", nextPayload.RenderedPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("ORIGINAL CRITERION TEXT", nextPayload.RenderedPrompt, StringComparison.Ordinal);
        ExpectedCriterion expectedNext = Assert.Single(nextPayload.ExpectedCriteria);
        Assert.Equal("C2", expectedNext.CriterionId);
        Assert.Equal(new ScoreRange(70m, 80m), expectedNext.Range);
        Assert.Equal(20m, next.Snapshot.Definition.Questions[0].Points);
        Assert.Equal(30m, next.Snapshot.Definition.Questions[0].Evaluators[0].Weight);
        Assert.True(next.PrepareOutput([Override("C2", "80")]).IsExportReady);
    }

    [Fact]
    public async Task Override_validation_uses_effective_snapshot_range_and_invalid_technical_fields_block_output()
    {
        QuantificationDefinition definition = U01TestSupport.Definition(
            2,
            2,
            U01TestSupport.Question(
                "Q1",
                "A",
                ["B"],
                true,
                U01TestSupport.Evaluator(
                    "E1",
                    "C1",
                    evaluatorRange: new ScoreRange(0m, 10m),
                    criterionRange: new ScoreRange(2m, 5m))));
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await RunOnce(
            definition,
            metadata,
            primary: "answer");

        RunOutputPreparation valid = summary.PrepareOutput([Override("C1", "5")]);
        RunOutputPreparation invalidRange = summary.PrepareOutput([Override("C1", "5.1")]);
        RunOutputPreparation invalidText = summary.PrepareOutput([Override("C1", "PRIVATE-NONNUMERIC-CANARY")]);

        Assert.True(valid.IsExportReady);
        CriterionOverrideInput writtenOverride = Assert.Single(
            Assert.Single(Assert.Single(valid.Rows).Questions).Evaluators).Overrides[0];
        Assert.Equal("5", writtenOverride.Value);
        Assert.False(invalidRange.IsExportReady);
        Assert.Empty(invalidRange.Rows);
        Assert.Contains(invalidRange.OverrideErrors, error => error.Code == "OVERRIDE_OUT_OF_RANGE");
        Assert.False(invalidText.IsExportReady);
        Assert.Contains(invalidText.OverrideErrors, error => error.Code == "OVERRIDE_NOT_NUMERIC");
        Assert.DoesNotContain(
            "PRIVATE-NONNUMERIC-CANARY",
            invalidText.ToString() + string.Concat(invalidText.OverrideErrors),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(RunSummary).GetMethods().Where(method => method.Name == nameof(RunSummary.PrepareOutput))
                .SelectMany(method => method.GetParameters()),
            parameter => parameter.Name?.Contains("confirm", StringComparison.OrdinalIgnoreCase) == true
                || parameter.Name?.Contains("warning", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Empty_primary_has_EMPTY_blank_contract_and_cannot_receive_an_override()
    {
        QuantificationDefinition definition = OneQuestionDefinition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await RunOnce(definition, metadata, primary: "   ");

        EvaluationUnitResult unit = Assert.Single(summary.Units);
        Assert.Equal(ResultsStatusCodes.Empty, unit.StatusCode);
        Assert.Null(unit.AcceptedResult);
        RunOutputPreparation withoutOverride = summary.PrepareOutput();
        QuestionResultInput question = Assert.Single(Assert.Single(withoutOverride.Rows).Questions);
        Assert.False(question.Scorable);
        Assert.Equal(ResultsStatusCodes.Empty, Assert.Single(question.Evaluators).Status);
        Assert.Null(Assert.Single(question.Evaluators).AiResult);

        RunOutputPreparation withOverride = summary.PrepareOutput([Override("C1", "5")]);
        Assert.False(withOverride.IsExportReady);
        Assert.Empty(withOverride.Rows);
        Assert.Contains(withOverride.OverrideErrors, error => error.Code == "OVERRIDE_NOT_ALLOWED");
    }

    [Fact]
    public async Task Progress_callback_failure_is_nonfatal_through_the_orchestrator_and_summary_is_redacted()
    {
        const string pathCanary = "C:\\PRIVATE\\student-input.xlsx";
        QuantificationDefinition definition = OneQuestionDefinition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ScriptedRowSource rows = new((request, _) => Task.FromResult(Row(request, "PRIVATE-ANSWER-CANARY", "support")));
        ScriptedRunner runner = new((payload, _, _) => Task.FromResult(
            EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload))));
        QuantificationRunRequest request = Request(definition, metadata) with { InputPath = pathCanary };

        RunSummary summary = await new QuantificationOrchestrator(
            rows,
            runner,
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot())).RunAsync(
                request,
                _ => throw new InvalidOperationException("observer failure"),
                TestContext.Current.CancellationToken);

        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        string rendered = summary + summary.Plan.ToString() + summary.PrepareOutput();
        Assert.DoesNotContain(pathCanary, rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE-ANSWER-CANARY", rendered, StringComparison.Ordinal);
        Assert.Contains("<redacted>", rendered, StringComparison.Ordinal);
    }

    private static async Task<RunSummary> RunOnce(
        QuantificationDefinition definition,
        WorkbookMetadata metadata,
        string primary)
    {
        ScriptedRowSource rows = new((request, _) => Task.FromResult(Row(request, primary, string.Empty)));
        ScriptedRunner runner = new((payload, _, _) => Task.FromResult(
            EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload))));
        return await new QuantificationOrchestrator(
            rows,
            runner,
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot())).RunAsync(
                Request(definition, metadata),
                cancellationToken: TestContext.Current.CancellationToken);
    }

    private static QuantificationDefinition OneQuestionDefinition(int firstRow, int lastRow) =>
        U01TestSupport.Definition(
            firstRow,
            lastRow,
            U01TestSupport.Question(
                "Q1",
                "A",
                ["B"],
                true,
                U01TestSupport.Evaluator("E1", "C1")));

    private static QuantificationRunRequest Request(
        QuantificationDefinition definition,
        WorkbookMetadata metadata) =>
        new()
        {
            DraftDefinition = definition,
            WorkbookMetadata = metadata,
            InputPath = "C:\\PRIVATE\\input.xlsx",
            ModelId = "model-test",
            MaximumPromptTokens = 64_000,
            MaximumContextWindowTokens = 128_000,
            MaxConcurrency = 1,
        };

    private static EvaluationRowData Row(
        EvaluationRowRequest request,
        string primary,
        string support) =>
        new(
            request.SourceRowNumber,
            new Dictionary<string, string?>
            {
                ["A"] = primary,
                ["B"] = support,
            });

    private static RunCriterionOverride Override(string criterionId, string? value) =>
        new()
        {
            SourceRowNumber = 2,
            QuestionId = "Q1",
            EvaluatorId = "E1",
            CriterionId = criterionId,
            Value = value,
        };
}

internal sealed class ScriptedInputSnapshots(
    InputSnapshot snapshot,
    bool unchanged = true,
    IList<string>? events = null) : IInputSnapshotBoundary
{
    internal InputSnapshot Snapshot { get; } = snapshot;

    internal bool Unchanged { get; set; } = unchanged;

    internal bool ThrowOnCapture { get; init; }

    internal bool ThrowOnRecheck { get; init; }

    internal Queue<bool> RecheckOutcomes { get; init; } = new();

    internal int CaptureCount { get; private set; }

    internal int RecheckCount { get; private set; }

    public InputSnapshot Capture(string inputPath)
    {
        CaptureCount++;
        events?.Add("capture");
        if (ThrowOnCapture)
        {
            throw new IOException("PRIVATE-CAPTURE-CANARY");
        }

        return Snapshot;
    }

    public bool IsUnchanged(string inputPath, InputSnapshot expected)
    {
        RecheckCount++;
        events?.Add("recheck");
        Assert.Same(Snapshot, expected);
        if (ThrowOnRecheck)
        {
            throw new IOException("PRIVATE-RECHECK-CANARY");
        }

        return RecheckOutcomes.Count > 0
            ? RecheckOutcomes.Dequeue()
            : Unchanged;
    }
}
