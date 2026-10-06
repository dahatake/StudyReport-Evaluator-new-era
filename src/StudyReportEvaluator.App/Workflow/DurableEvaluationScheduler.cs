using System.Collections.Immutable;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Similarity;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.Workflow;

public sealed class DurableRowEvaluationResult
{
    internal DurableRowEvaluationResult(
        int sourceRowNumber,
        CheckpointCompletedRow? completedRow)
    {
        SourceRowNumber = sourceRowNumber;
        CompletedRow = completedRow;
    }

    public int SourceRowNumber { get; }

    public CheckpointCompletedRow? CompletedRow { get; }

    public bool IsComplete => CompletedRow is not null;

    public override string ToString() =>
        $"{nameof(DurableRowEvaluationResult)} {{ SourceRowNumber = {SourceRowNumber}, IsComplete = {IsComplete}, Content = <redacted> }}";
}

public sealed class DurableEvaluationScheduler
{
    private readonly IEvaluationRowSource rowSource;
    private readonly EvaluationScheduler normalScheduler;
    private readonly ISpecialEvaluationOperationRunner specialRunner;
    private readonly SafeEvaluationPayloadBuilder payloadBuilder = new();
    private readonly AuxiliaryQuantificationResultValidator resultValidator = new();
    private readonly EvaluationRequestCapacityValidator requestCapacityValidator = new();
    private readonly SurfaceTextSimilarityCalculator similarityCalculator = new();

    public DurableEvaluationScheduler(
        IEvaluationRowSource rowSource,
        IEvaluationRunner normalRunner,
        ISpecialEvaluationOperationRunner specialRunner)
    {
        this.rowSource = rowSource ?? throw new ArgumentNullException(nameof(rowSource));
        ArgumentNullException.ThrowIfNull(normalRunner);
        this.specialRunner = specialRunner ?? throw new ArgumentNullException(nameof(specialRunner));
        normalScheduler = new EvaluationScheduler(rowSource, normalRunner);
    }

    public Task<DurableRowEvaluationResult> EvaluateRowAsync(
        EvaluationPlan plan,
        int sourceRowNumber,
        IReadOnlyDictionary<string, CheckpointReference> references,
        string modelId,
        int maxConcurrency,
        int? maximumPromptTokens = null,
        int? maximumContextWindowTokens = null,
        Action<int>? inFlightChanged = null,
        CancellationToken cancellationToken = default) =>
        EvaluateRowAsync(
            plan,
            sourceRowNumber,
            references,
            modelId,
            reasoningEffort: null,
            maxConcurrency,
            maximumPromptTokens,
            maximumContextWindowTokens,
            inFlightChanged,
            cancellationToken);

    public async Task<DurableRowEvaluationResult> EvaluateRowAsync(
        EvaluationPlan plan,
        int sourceRowNumber,
        IReadOnlyDictionary<string, CheckpointReference> references,
        string modelId,
        string? reasoningEffort,
        int maxConcurrency,
        int? maximumPromptTokens = null,
        int? maximumContextWindowTokens = null,
        Action<int>? inFlightChanged = null,
        CancellationToken cancellationToken = default,
        AdaptiveEvaluationConcurrencyLimiter? concurrencyLimiter = null,
        Action<LivePreviewRow>? livePreview = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        EphemeralEvaluationRunner.ValidateReasoningEffort(reasoningEffort);
        _ = new EvaluationSchedulerOptions(maxConcurrency);
        if (sourceRowNumber < plan.Mapping.FirstDataRow
            || sourceRowNumber > plan.Mapping.LastDataRow)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRowNumber));
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new DurableRowEvaluationResult(sourceRowNumber, null);
        }

        ImmutableArray<EvaluationPlanItem> normalItems = plan.Items
            .Where(item => item.SourceRowNumber == sourceRowNumber)
            .ToImmutableArray();
        ImmutableArray<(QuestionDefinition Question, SpecialEvaluationDefinition Special)> specials =
            plan.Snapshot.Definition.Questions
                .Where(question => question.Enabled)
                .SelectMany(question => question.SpecialEvaluations
                    .Where(special => special.Enabled)
                    .Select(special => (question, special)))
                .ToImmutableArray();
        ImmutableArray<QuestionDefinition> similarityQuestions = plan.Snapshot.Definition.Questions
            .Where(question => question.Enabled)
            .ToImmutableArray();
        string[] selectedColumns = normalItems
            .SelectMany(item => item.SelectedSourceColumns)
            .Concat(specials.SelectMany(item => item.Special.SupportingSourceColumns.Prepend(
                item.Special.PrimarySourceColumn)))
            .Concat(similarityQuestions.Select(question => question.PrimarySourceColumn))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        LivePreviewRowTracker? tracker = livePreview is null
            ? null
            : new LivePreviewRowTracker(
                sourceRowNumber,
                normalItems
                    .Select(item => LivePreviewItems.Waiting(
                        LivePreviewItemKind.Normal,
                        NormalTitle(plan, item)))
                    .Concat(specials.Select(item => LivePreviewItems.Waiting(
                        LivePreviewItemKind.Special,
                        LivePreviewItems.SpecialTitle(item.Question, item.Special))))
                    .Concat(similarityQuestions.Select(question => LivePreviewItems.Waiting(
                        LivePreviewItemKind.Similarity,
                        LivePreviewItems.SimilarityTitle(question)))),
                livePreview);

        DurableRowEvaluationResult FailedRowResult()
        {
            tracker?.UpdateAll((_, item) => LivePreviewItems.Settled(item, ResultsStatusCodes.AiRuntimeFailed));
            tracker?.Complete();
            return FailedRow(plan, sourceRowNumber, normalItems, specials, similarityQuestions);
        }

        EvaluationRowData row;
        try
        {
            row = await rowSource.ReadAsync(
                new EvaluationRowRequest(
                    plan.Snapshot.Definition.SourceSheet,
                    sourceRowNumber,
                    selectedColumns),
                cancellationToken).ConfigureAwait(false);
            if (row.SourceRowNumber != sourceRowNumber)
            {
                return FailedRowResult();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new DurableRowEvaluationResult(sourceRowNumber, null);
        }
        catch
        {
            return FailedRowResult();
        }

        tracker?.UpdateAll((slot, item) =>
        {
            if (slot < normalItems.Length)
            {
                EvaluationPlanItem planItem = normalItems[slot];
                return item with
                {
                    Cells = LivePreviewItems.Cells(
                        planItem.PrimarySourceColumn,
                        planItem.SelectedSourceColumns,
                        column => ReadCell(row, column)),
                };
            }

            slot -= normalItems.Length;
            if (slot < specials.Length)
            {
                SpecialEvaluationDefinition special = specials[slot].Special;
                return item with
                {
                    Cells = LivePreviewItems.Cells(
                        special.PrimarySourceColumn,
                        special.SupportingSourceColumns.Prepend(special.PrimarySourceColumn),
                        column => ReadCell(row, column)),
                };
            }

            QuestionDefinition question = similarityQuestions[slot - specials.Length];
            return item with
            {
                Cells = LivePreviewItems.Cells(
                    question.PrimarySourceColumn,
                    [question.PrimarySourceColumn],
                    column => ReadCell(row, column)),
                ComparisonText = references.TryGetValue(question.Id, out CheckpointReference? reference)
                    && string.Equals(reference.StatusCode, ResultsStatusCodes.Success, StringComparison.Ordinal)
                    ? reference.Answer
                    : null,
            };
        });

        CheckpointNormalResult?[] normalResults = new CheckpointNormalResult?[normalItems.Length];
        CheckpointSpecialResult?[] specialResults = new CheckpointSpecialResult?[specials.Length];
        CheckpointSimilarityResult?[] similarityResults = new CheckpointSimilarityResult?[similarityQuestions.Length];
        List<Func<Task>> operations = [];
        for (int index = 0; index < normalItems.Length; index++)
        {
            int slot = index;
            operations.Add(async () =>
            {
                EvaluationUnitResult result = await normalScheduler.ExecuteItemAsync(
                    plan,
                    normalItems[slot],
                    row,
                    modelId,
                    cancellationToken,
                    maximumPromptTokens,
                    maximumContextWindowTokens,
                    payload => tracker?.Update(slot, item => item with
                    {
                        PromptText = payload.RenderedPrompt,
                        Phase = LivePreviewItemPhase.Running,
                    })).ConfigureAwait(false);
                normalResults[slot] = ToCheckpoint(result);
                tracker?.Update(slot, item => LivePreviewItems.Settled(
                    item,
                    result.StatusCode,
                    LivePreviewItems.NormalMeasures(
                        QuestionOf(plan, result.Item.QuestionId),
                        result.AcceptedResult)));
            });
        }

        for (int index = 0; index < specials.Length; index++)
        {
            int slot = index;
            int previewSlot = normalItems.Length + index;
            operations.Add(async () =>
            {
                (QuestionDefinition question, SpecialEvaluationDefinition special) = specials[slot];
                specialResults[slot] = await EvaluateSpecialAsync(
                    plan.Snapshot,
                    question,
                    special,
                    row,
                    modelId,
                    reasoningEffort,
                    maximumPromptTokens,
                    maximumContextWindowTokens,
                    cancellationToken,
                    payload => tracker?.Update(previewSlot, item => item with
                    {
                        PromptText = payload.RenderedPrompt,
                        Phase = LivePreviewItemPhase.Running,
                    })).ConfigureAwait(false);
                CheckpointSpecialResult settled = specialResults[slot]!;
                tracker?.Update(previewSlot, item => LivePreviewItems.Settled(
                    item,
                    settled.StatusCode,
                    LivePreviewItems.SpecialMeasures(settled.AcceptedResult)));
            });
        }

        for (int index = 0; index < similarityQuestions.Length; index++)
        {
            int slot = index;
            int previewSlot = normalItems.Length + specials.Length + index;
            operations.Add(async () =>
            {
                QuestionDefinition question = similarityQuestions[slot];
                similarityResults[slot] = await EvaluateSimilarityAsync(
                    plan.Snapshot,
                    question,
                    row,
                    references,
                    cancellationToken).ConfigureAwait(false);
                CheckpointSimilarityResult settled = similarityResults[slot]!;
                tracker?.Update(previewSlot, item => LivePreviewItems.Settled(
                    item,
                    settled.StatusCode,
                    LivePreviewItems.SimilarityMeasures(settled.AcceptedResult)));
            });
        }

        using SemaphoreSlim? gate = concurrencyLimiter is null
            ? new SemaphoreSlim(maxConcurrency, maxConcurrency)
            : null;
        int inFlight = 0;
        Task[] tasks = operations.Select(operation => RunBoundedAsync(operation)).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (normalResults.Any(result => result is null || IsCancelled(result.StatusCode))
            || specialResults.Any(result => result is null || IsCancelled(result.StatusCode))
            || similarityResults.Any(result => result is null || IsCancelled(result.StatusCode)))
        {
            return new DurableRowEvaluationResult(sourceRowNumber, null);
        }

        tracker?.Complete();
        return new DurableRowEvaluationResult(
            sourceRowNumber,
            new CheckpointCompletedRow
            {
                SourceRowNumber = sourceRowNumber,
                NormalResults = normalResults.Select(result => result!).ToImmutableArray(),
                SpecialResults = specialResults.Select(result => result!).ToImmutableArray(),
                SimilarityResults = similarityResults.Select(result => result!).ToImmutableArray(),
            });

        async Task RunBoundedAsync(Func<Task> operation)
        {
            IAsyncDisposable? lease = null;
            try
            {
                if (concurrencyLimiter is not null)
                {
                    lease = await concurrencyLimiter.AcquireAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await gate!.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                int current = Interlocked.Increment(ref inFlight);
                ReportSafely(inFlightChanged, current);
                await operation().ConfigureAwait(false);
            }
            catch
            {
                // Each operation maps failures to a closed result. This is a last-resort guard;
                // the null slot causes the entire in-progress row to be discarded.
            }
            finally
            {
                int current = Interlocked.Decrement(ref inFlight);
                ReportSafely(inFlightChanged, current);
                if (lease is not null)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    gate!.Release();
                }
            }
        }
    }

    public override string ToString() =>
        $"{nameof(DurableEvaluationScheduler)} {{ Content = <redacted> }}";

    private async Task<CheckpointSpecialResult> EvaluateSpecialAsync(
        QuantificationSnapshot snapshot,
        QuestionDefinition question,
        SpecialEvaluationDefinition special,
        EvaluationRowData row,
        string modelId,
        string? reasoningEffort,
        int? maximumPromptTokens,
        int? maximumContextWindowTokens,
        CancellationToken cancellationToken,
        Action<SafeSpecialEvaluationPayload>? payloadBuilt = null)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return SpecialFailure(question.Id, special.Id, ResultsStatusCodes.Cancelled);
        }

        if (snapshot.Definition.SpecialPoints == 0m)
        {
            return SpecialFailure(question.Id, special.Id, ResultsStatusCodes.NotRunZeroBudget);
        }

        Dictionary<string, string?> cells = SelectCells(
            row,
            special.SupportingSourceColumns.Prepend(special.PrimarySourceColumn));
        if (string.IsNullOrWhiteSpace(cells[special.PrimarySourceColumn]))
        {
            return SpecialFailure(question.Id, special.Id, ResultsStatusCodes.Empty);
        }

        SafeSpecialEvaluationPayload payload;
        try
        {
            payload = payloadBuilder.BuildSpecialEvaluation(
                snapshot,
                question.Id,
                special.Id,
                cells);
        }
        catch
        {
            return SpecialFailure(question.Id, special.Id, ResultsStatusCodes.AiRuntimeFailed);
        }

        try
        {
            payloadBuilt?.Invoke(payload);
            if (!requestCapacityValidator.Validate(
                    payload,
                    maximumPromptTokens,
                    maximumContextWindowTokens).IsValid)
            {
                return SpecialFailure(question.Id, special.Id, ResultsStatusCodes.AiOutputInvalid);
            }

            AuxiliaryOperationResult<SpecialQuantificationResult> result = await specialRunner
                .EvaluateAsync(payload, modelId, reasoningEffort, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return SpecialFailure(
                    question.Id,
                    special.Id,
                    result.StatusCode,
                    result.AttemptCount,
                    result.TokenUsage);
            }

            AuxiliaryResultValidationOutcome<SpecialQuantificationResult> validation =
                resultValidator.ValidateSpecial(payload, result.AcceptedResult!);
            return validation.IsValid
                ? new CheckpointSpecialResult
                {
                    QuestionId = question.Id,
                    SpecialEvaluationId = special.Id,
                    StatusCode = ResultsStatusCodes.Success,
                    AttemptCount = result.AttemptCount,
                    AcceptedResult = validation.AcceptedResult,
                    TokenUsage = DurableOperationConversions.ToCheckpointUsage(result.TokenUsage),
                }
                : SpecialFailure(
                    question.Id,
                    special.Id,
                    ResultsStatusCodes.AiOutputInvalid,
                    result.AttemptCount,
                    result.TokenUsage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SpecialFailure(question.Id, special.Id, ResultsStatusCodes.Cancelled);
        }
        catch (Exception exception)
        {
            return SpecialFailure(
                question.Id,
                special.Id,
                DurableOperationConversions.ClassifyException(exception));
        }
    }

    private Task<CheckpointSimilarityResult> EvaluateSimilarityAsync(
        QuantificationSnapshot snapshot,
        QuestionDefinition question,
        EvaluationRowData row,
        IReadOnlyDictionary<string, CheckpointReference> references,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(SimilarityFailure(question.Id, ResultsStatusCodes.Cancelled));
        }

        string studentAnswer = ReadCell(row, question.PrimarySourceColumn);
        if (string.IsNullOrWhiteSpace(studentAnswer))
        {
            return Task.FromResult(SimilarityFailure(question.Id, ResultsStatusCodes.Empty));
        }

        if (!references.TryGetValue(question.Id, out CheckpointReference? reference)
            || !string.Equals(reference.StatusCode, ResultsStatusCodes.Success, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(reference.Answer))
        {
            string status = reference is not null && ResultsStatusCodes.IsDefined(reference.StatusCode)
                ? reference.StatusCode
                : ResultsStatusCodes.AiRuntimeFailed;
            return Task.FromResult(SimilarityFailure(question.Id, status));
        }

        try
        {
            SurfaceSimilarityResult result = similarityCalculator.Calculate(studentAnswer, reference.Answer);
            return Task.FromResult(new CheckpointSimilarityResult
            {
                QuestionId = question.Id,
                StatusCode = ResultsStatusCodes.Success,
                AttemptCount = 1,
                AcceptedResult = new SimilarityQuantificationResult
                {
                    QuestionId = question.Id,
                    Similarity = result.Score,
                    Reason = result.ToReason(),
                },
                TokenUsage = DurableOperationConversions.ToCheckpointUsage(EvaluationTokenUsage.Unavailable),
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(SimilarityFailure(question.Id, ResultsStatusCodes.Cancelled));
        }
        catch
        {
            return Task.FromResult(SimilarityFailure(question.Id, ResultsStatusCodes.AiRuntimeFailed));
        }
    }

    private static DurableRowEvaluationResult FailedRow(
        EvaluationPlan plan,
        int sourceRowNumber,
        ImmutableArray<EvaluationPlanItem> normalItems,
        ImmutableArray<(QuestionDefinition Question, SpecialEvaluationDefinition Special)> specials,
        ImmutableArray<QuestionDefinition> similarityQuestions)
    {
        CheckpointTokenUsage unavailable = new();
        return new DurableRowEvaluationResult(
            sourceRowNumber,
            new CheckpointCompletedRow
            {
                SourceRowNumber = sourceRowNumber,
                NormalResults = normalItems.Select(item => new CheckpointNormalResult
                {
                    QuestionId = item.QuestionId,
                    EvaluatorId = item.EvaluatorId,
                    StatusCode = ResultsStatusCodes.AiRuntimeFailed,
                    Scorable = false,
                    ScorableKnown = false,
                    TokenUsage = unavailable,
                }).ToImmutableArray(),
                SpecialResults = specials.Select(item => SpecialFailure(
                    item.Question.Id,
                    item.Special.Id,
                    ResultsStatusCodes.AiRuntimeFailed)).ToImmutableArray(),
                SimilarityResults = similarityQuestions.Select(question => SimilarityFailure(
                    question.Id,
                    ResultsStatusCodes.AiRuntimeFailed)).ToImmutableArray(),
            });
    }

    private static CheckpointNormalResult ToCheckpoint(EvaluationUnitResult result) =>
        new()
        {
            QuestionId = result.Item.QuestionId,
            EvaluatorId = result.Item.EvaluatorId,
            StatusCode = result.StatusCode,
            AttemptCount = result.AttemptCount,
            Scorable = result.Scorable,
            ScorableKnown = result.ScorableKnown,
            AcceptedResult = result.AcceptedResult,
            TokenUsage = DurableOperationConversions.ToCheckpointUsage(result.TokenUsage),
        };

    private static CheckpointSpecialResult SpecialFailure(
        string questionId,
        string specialId,
        string statusCode,
        int attemptCount = 0,
        EvaluationTokenUsage? usage = null) =>
        new()
        {
            QuestionId = questionId,
            SpecialEvaluationId = specialId,
            StatusCode = statusCode,
            AttemptCount = attemptCount,
            TokenUsage = DurableOperationConversions.ToCheckpointUsage(
                usage ?? EvaluationTokenUsage.Unavailable),
        };

    private static CheckpointSimilarityResult SimilarityFailure(
        string questionId,
        string statusCode,
        int attemptCount = 0,
        EvaluationTokenUsage? usage = null) =>
        new()
        {
            QuestionId = questionId,
            StatusCode = statusCode,
            AttemptCount = attemptCount,
            TokenUsage = DurableOperationConversions.ToCheckpointUsage(
                usage ?? EvaluationTokenUsage.Unavailable),
        };

    private static Dictionary<string, string?> SelectCells(
        EvaluationRowData row,
        IEnumerable<string> columns) =>
        columns.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(
            column => column,
            column => row.Cells.TryGetValue(column, out string? value) ? value : string.Empty,
            StringComparer.OrdinalIgnoreCase);

    private static string ReadCell(EvaluationRowData row, string column) =>
        row.Cells.TryGetValue(column, out string? value) ? value ?? string.Empty : string.Empty;

    private static QuestionDefinition? QuestionOf(EvaluationPlan plan, string questionId) =>
        plan.Snapshot.Definition.Questions.FirstOrDefault(
            question => string.Equals(question.Id, questionId, StringComparison.Ordinal));

    private static string NormalTitle(EvaluationPlan plan, EvaluationPlanItem item)
    {
        QuestionDefinition? question = QuestionOf(plan, item.QuestionId);
        EvaluatorDefinition? evaluator = question?.Evaluators.FirstOrDefault(
            candidate => string.Equals(candidate.Id, item.EvaluatorId, StringComparison.Ordinal));
        return question is not null && evaluator is not null
            ? LivePreviewItems.NormalTitle(question, evaluator)
            : $"通常評価 · {item.QuestionId} / {item.EvaluatorId}";
    }

    private static bool IsCancelled(string statusCode) =>
        string.Equals(statusCode, ResultsStatusCodes.Cancelled, StringComparison.Ordinal);

    private static void ReportSafely(Action<int>? callback, int value)
    {
        if (callback is null)
        {
            return;
        }

        try
        {
            callback(value);
        }
        catch
        {
            // Progress observers cannot alter durable row state.
        }
    }
}
