using System.Collections.Immutable;
using System.Globalization;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.Tests.E2E;

internal sealed record FakeCopilotCall(
    int SourceRowNumber,
    string QuestionId,
    string EvaluatorId,
    ImmutableArray<string> CriterionIds,
    ImmutableArray<string> SourceColumnIds)
{
    public override string ToString() =>
        $"{nameof(FakeCopilotCall)} {{ SourceRowNumber = {SourceRowNumber.ToString(CultureInfo.InvariantCulture)}, QuestionId = {QuestionId}, EvaluatorId = {EvaluatorId}, CriterionCount = {CriterionIds.Length.ToString(CultureInfo.InvariantCulture)}, SourceColumnIds = [{string.Join(',', SourceColumnIds)}], Content = <redacted> }}";
}

internal sealed class FakeCopilotTransport(SyntheticWorkbook workbook) : IEvaluationRunner
{
    private readonly object gate = new();
    private readonly QuantificationResultValidator validator = new();
    private readonly List<FakeCopilotCall> calls = [];
    private int inFlight;
    private int maximumObservedConcurrency;

    internal ImmutableArray<FakeCopilotCall> Calls
    {
        get
        {
            lock (gate)
            {
                return calls.ToImmutableArray();
            }
        }
    }

    internal int MaximumObservedConcurrency => Volatile.Read(ref maximumObservedConcurrency);

    public async Task<EvaluationRunnerResult> EvaluateAsync(
        SafeEvaluationPayload payload,
        string modelId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        cancellationToken.ThrowIfCancellationRequested();

        int currentConcurrency = Interlocked.Increment(ref inFlight);
        ObserveMaximumConcurrency(currentConcurrency);
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            int sourceRowNumber = workbook.FindUniqueSourceRow(
                payload.PrimarySource.SourceColumnId,
                payload.PrimarySource.Value);
            AssertSourcesAreFromTheSameRow(payload, sourceRowNumber);

            ImmutableArray<CriterionQuantificationResult> criteria = payload.ExpectedCriteria
                .Select((criterion, index) => CreateCriterionResult(
                    payload,
                    sourceRowNumber,
                    criterion,
                    index))
                .ToImmutableArray();
            QuantificationResult result = new()
            {
                EvaluatorId = payload.EvaluatorId,
                Criteria = criteria,
            };
            QuantificationResultValidationOutcome validation = validator.Validate(payload, result);
            if (!validation.IsValid || validation.AcceptedResult is null)
            {
                throw new InvalidOperationException(
                    "The deterministic fake generated an invalid quantification result.");
            }

            lock (gate)
            {
                calls.Add(new FakeCopilotCall(
                    sourceRowNumber,
                    payload.QuestionId,
                    payload.EvaluatorId,
                    criteria.Select(item => item.CriterionId).ToImmutableArray(),
                    payload.Sources.Select(item => item.SourceColumnId).ToImmutableArray()));
            }

            return EvaluationRunnerResult.Succeeded(validation.AcceptedResult);
        }
        finally
        {
            _ = Interlocked.Decrement(ref inFlight);
        }
    }

    public override string ToString() =>
        $"{nameof(FakeCopilotTransport)} {{ CallCount = {Calls.Length.ToString(CultureInfo.InvariantCulture)}, MaximumObservedConcurrency = {MaximumObservedConcurrency.ToString(CultureInfo.InvariantCulture)}, Content = <redacted> }}";

    private CriterionQuantificationResult CreateCriterionResult(
        SafeEvaluationPayload payload,
        int sourceRowNumber,
        ExpectedCriterion criterion,
        int criterionIndex)
    {
        bool useSupportingSource = !payload.SupportingSources.IsEmpty
            && ((sourceRowNumber + criterionIndex) % 2 == 0);
        EvaluationSourceCell source = useSupportingSource
            ? payload.SupportingSources[criterionIndex % payload.SupportingSources.Length]
            : payload.PrimarySource;
        decimal rangeWidth = criterion.Range.Maximum - criterion.Range.Minimum;
        decimal fraction = ((sourceRowNumber + criterionIndex) % 7 + 1m) / 8m;
        decimal rawScore = criterion.Range.Minimum + (rangeWidth * fraction);
        string evidence = string.Concat(
            SyntheticWorkbookFactory.FormulaMarkerForRow(sourceRowNumber),
            SyntheticWorkbookFactory.RowMarker(sourceRowNumber));
        if (!source.Value.Contains(evidence, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The deterministic evidence marker is missing from the selected source.");
        }

        return new CriterionQuantificationResult
        {
            CriterionId = criterion.CriterionId,
            RawScore = rawScore,
            Reason = string.Concat(
                SyntheticWorkbookFactory.FormulaMarkerForRow(sourceRowNumber),
                "SYNTHETIC-DETERMINISTIC-REASON"),
            Evidence = evidence,
            EvidenceSource = useSupportingSource
                ? EvidenceSourceKind.SupportingColumn
                : EvidenceSourceKind.PrimaryAnswer,
            EvidenceSourceColumnId = source.SourceColumnId,
        };
    }

    private void AssertSourcesAreFromTheSameRow(
        SafeEvaluationPayload payload,
        int sourceRowNumber)
    {
        foreach (EvaluationSourceCell source in payload.Sources)
        {
            string expected = workbook.GetCell(sourceRowNumber, source.SourceColumnId);
            if (!string.Equals(expected, source.Value, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The scheduler mixed source cells from different rows.");
            }

            char marker = SyntheticWorkbookFactory.FormulaMarkerForRow(sourceRowNumber);
            if (!source.Value.StartsWith(marker))
            {
                throw new InvalidOperationException(
                    "Formula-injection-like source text was modified before dispatch.");
            }
        }
    }

    private void ObserveMaximumConcurrency(int currentConcurrency)
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref maximumObservedConcurrency);
            if (currentConcurrency <= observed)
            {
                return;
            }
        }
        while (Interlocked.CompareExchange(
                   ref maximumObservedConcurrency,
                   currentConcurrency,
                   observed) != observed);
    }
}