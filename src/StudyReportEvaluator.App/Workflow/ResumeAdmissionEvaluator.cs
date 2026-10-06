using System.Collections.Immutable;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.Workflow;

public enum ResumeAdmissionItem { PartialPath, InputIdentity, Definition, NormalModel, Runtime, CheckpointShape }

public sealed record ResumeAdmissionFinding(
    ResumeAdmissionItem Item,
    bool IsSatisfied,
    string StatusCode,
    string Description)
{
    public string DisplayText => (IsSatisfied ? "確認済み: " : "要確認: ") + Description;

    public override string ToString() =>
        $"{nameof(ResumeAdmissionFinding)} {{ Item = {Item}, IsSatisfied = {IsSatisfied}, Content = <redacted> }}";
}

public sealed record ResumeAdmissionReport(
    ImmutableArray<ResumeAdmissionFinding> Findings,
    string? BlockingStatusCode)
{
    public bool CanResume => BlockingStatusCode is null && !Findings.IsDefaultOrEmpty
        && Findings.All(finding => finding.IsSatisfied);

    public override string ToString() =>
        $"{nameof(ResumeAdmissionReport)} {{ CanResume = {CanResume}, Content = <redacted> }}";
}

/// <summary>
/// Read-only admission shared by preview and execution. The store validates the v1
/// package first. Source-row evidence/scorability validation still runs in the orchestrator.
/// </summary>
public static class ResumeAdmissionEvaluator
{
    public static ResumeAdmissionReport Evaluate(
        CheckpointEnvelope checkpoint,
        string requestedPartialPath,
        QuantificationSnapshot? snapshot,
        EvaluationPlan? plan,
        InputSnapshot? currentInput,
        string? inputPath,
        string? modelId,
        CheckpointRuntimeIdentity? runtime) =>
        Evaluate(
            checkpoint,
            requestedPartialPath,
            snapshot,
            plan,
            currentInput,
            inputPath,
            modelId,
            reasoningEffort: null,
            runtime);

    public static ResumeAdmissionReport Evaluate(
        CheckpointEnvelope checkpoint,
        string requestedPartialPath,
        QuantificationSnapshot? snapshot,
        EvaluationPlan? plan,
        InputSnapshot? currentInput,
        string? inputPath,
        string? modelId,
        string? reasoningEffort,
        CheckpointRuntimeIdentity? runtime,
        string? contextTier = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        bool partialMatches = PathsEqual(checkpoint.PartialPath, requestedPartialPath);
        bool inputMatches = PathsEqual(checkpoint.InputPath, inputPath)
            && checkpoint.Input.Equals(currentInput);
        bool definitionMatches = snapshot is not null
            && string.Equals(checkpoint.DefinitionSha256, snapshot.Sha256, StringComparison.Ordinal)
            && string.Equals(checkpoint.DefinitionCanonicalJson, snapshot.CanonicalJson, StringComparison.Ordinal);
        bool modelMatches = modelId is not null
            && string.Equals(checkpoint.NormalModelId, modelId, StringComparison.Ordinal)
            && string.Equals(checkpoint.ReferenceModelId, modelId, StringComparison.Ordinal)
            && string.Equals(checkpoint.ContextTier, contextTier, StringComparison.Ordinal)
            && string.Equals(checkpoint.ReasoningEffort, reasoningEffort, StringComparison.Ordinal);
        bool runtimeMatches = runtime is not null && RuntimeCompatible(checkpoint.Runtime, runtime);
        // A different definition cannot be used to interpret saved row structure.
        bool shapeMatches = definitionMatches && plan is not null
            && ValidateShape(checkpoint, snapshot!, plan);
        ImmutableArray<ResumeAdmissionFinding> findings =
        [
            Finding(ResumeAdmissionItem.PartialPath, partialMatches, CheckpointAdmissionStatusCodes.InputMismatch,
                "再開元の場所が一致しています。", "checkpoint は作成時の場所から移動せずに使ってください。"),
            Finding(ResumeAdmissionItem.InputIdentity, inputMatches, CheckpointAdmissionStatusCodes.InputMismatch,
                "入力の場所と内容が一致しています。", "入力が未選択、読み取り不可、または中断時の入力と一致しません。"),
            Finding(ResumeAdmissionItem.Definition, definitionMatches, CheckpointAdmissionStatusCodes.DefinitionMismatch,
                "採点設計が一致しています。", "採点設計が未設定、または checkpoint と異なります。中断時の設計へ戻すか、新規実行を選んでください。"),
            Finding(ResumeAdmissionItem.NormalModel, modelMatches, CheckpointAdmissionStatusCodes.ModelMismatch,
                "モデル・思考レベル・Context Sizeが一致しています。", "モデル・思考レベル・Context Sizeが checkpoint と異なります。共通設定で中断時の選択値を確認してください。"),
            Finding(ResumeAdmissionItem.Runtime, runtimeMatches, CheckpointAdmissionStatusCodes.RuntimeMismatch,
                "アプリと CLI / SDK の版が一致しています。", "認証状態が未確認、またはアプリ／CLI／SDK の版が異なります。状態を再確認し、版が異なる場合は中断時の版を使用してください。"),
            Finding(ResumeAdmissionItem.CheckpointShape, shapeMatches, CheckpointStatusCodes.Invalid,
                "保存済み参照と完了行の構造を確認しました。実行開始時に完了行の内容も検証します。",
                "保存済み参照と完了行の構造を確認できません。入力と採点設計を確認してください。"),
        ];
        // Preserve the original admission priority, even when several items fail.
        return new ResumeAdmissionReport(findings,
            findings.FirstOrDefault(finding => !finding.IsSatisfied)?.StatusCode);
    }

    private static ResumeAdmissionFinding Finding(ResumeAdmissionItem item, bool satisfied,
        string failureCode, string successText, string failureText) =>
        new(item, satisfied, satisfied ? CheckpointStatusCodes.Success : failureCode,
            satisfied ? successText : failureText);

    private static bool ValidateShape(CheckpointEnvelope checkpoint, QuantificationSnapshot snapshot, EvaluationPlan plan)
    {
        if (checkpoint.SchemaVersion != CheckpointEnvelope.CurrentSchemaVersion
            || checkpoint.References.IsDefault || checkpoint.CompletedRows.IsDefault)
        {
            return false;
        }

        QuestionDefinition[] enabledQuestions = snapshot.Definition.Questions.Where(question => question.Enabled).ToArray();
        if (checkpoint.References.Length > enabledQuestions.Length) return false;
        for (int index = 0; index < checkpoint.References.Length; index++)
        {
            CheckpointReference reference = checkpoint.References[index];
            if (reference is null
                || !string.Equals(reference.QuestionId, enabledQuestions[index].Id, StringComparison.Ordinal)
                || string.Equals(reference.StatusCode, ResultsStatusCodes.Cancelled, StringComparison.Ordinal)) return false;
        }

        if (checkpoint.CompletedRows.Length > plan.Mapping.SelectedRowCount
            || (checkpoint.CompletedRows.Length > 0 && checkpoint.References.Length != enabledQuestions.Length)) return false;
        for (int index = 0; index < checkpoint.CompletedRows.Length; index++)
        {
            CheckpointCompletedRow row = checkpoint.CompletedRows[index];
            if (row is null || row.SourceRowNumber != plan.Mapping.FirstDataRow + index
                || !ValidateCompletedRowShape(row, plan, enabledQuestions)
                || row.SimilarityResults.Select((similarity, questionIndex) => new
                    { Similarity = similarity, Reference = checkpoint.References[questionIndex] })
                    .Any(pair => !string.Equals(pair.Reference.StatusCode, ResultsStatusCodes.Success, StringComparison.Ordinal)
                        && string.Equals(pair.Similarity.StatusCode, ResultsStatusCodes.Success, StringComparison.Ordinal))) return false;
        }

        return true;
    }

    private static bool ValidateCompletedRowShape(CheckpointCompletedRow row, EvaluationPlan plan,
        IReadOnlyList<QuestionDefinition> enabledQuestions)
    {
        if (row.NormalResults.IsDefault || row.SpecialResults.IsDefault || row.SimilarityResults.IsDefault) return false;
        EvaluationPlanItem[] normalItems = plan.Items.Where(item => item.SourceRowNumber == row.SourceRowNumber).ToArray();
        if (row.NormalResults.Length != normalItems.Length) return false;
        for (int index = 0; index < normalItems.Length; index++)
        {
            EvaluationPlanItem expected = normalItems[index];
            CheckpointNormalResult actual = row.NormalResults[index];
            if (actual is null
                || !string.Equals(actual.QuestionId, expected.QuestionId, StringComparison.Ordinal)
                || !string.Equals(actual.EvaluatorId, expected.EvaluatorId, StringComparison.Ordinal)
                || string.Equals(actual.StatusCode, ResultsStatusCodes.Cancelled, StringComparison.Ordinal)
                || (string.Equals(actual.StatusCode, ResultsStatusCodes.Success, StringComparison.Ordinal)
                    != (actual.AcceptedResult is not null))) return false;

            EvaluatorDefinition evaluator = enabledQuestions.Single(question => question.Id == expected.QuestionId)
                .Evaluators.Single(item => item.Enabled && item.Id == expected.EvaluatorId);
            if (actual.AcceptedResult is QuantificationResult accepted)
            {
                CriterionDefinition[] criteria = evaluator.Criteria.Where(criterion => criterion.Enabled).ToArray();
                if (accepted.Criteria.IsDefault || accepted.Criteria.Length != criteria.Length) return false;
                for (int criterionIndex = 0; criterionIndex < criteria.Length; criterionIndex++)
                {
                    CriterionDefinition criterion = criteria[criterionIndex];
                    CriterionQuantificationResult submitted = accepted.Criteria[criterionIndex];
                    ScoreRange range = criterion.Range ?? evaluator.Range;
                    if (submitted is null
                        || !string.Equals(criterion.Id, submitted.CriterionId, StringComparison.Ordinal)
                        || submitted.RawScore < range.Minimum || submitted.RawScore > range.Maximum) return false;
                }
            }
        }

        (string QuestionId, string SpecialId)[] specials = enabledQuestions
            .SelectMany(question => question.SpecialEvaluations.Where(special => special.Enabled)
                .Select(special => (question.Id, special.Id))).ToArray();
        if (row.SpecialResults.Length != specials.Length) return false;
        for (int index = 0; index < specials.Length; index++)
        {
            CheckpointSpecialResult actual = row.SpecialResults[index];
            if (actual is null
                || !string.Equals(actual.QuestionId, specials[index].QuestionId, StringComparison.Ordinal)
                || !string.Equals(actual.SpecialEvaluationId, specials[index].SpecialId, StringComparison.Ordinal)
                || string.Equals(actual.StatusCode, ResultsStatusCodes.Cancelled, StringComparison.Ordinal)
                || (string.Equals(actual.StatusCode, ResultsStatusCodes.Success, StringComparison.Ordinal)
                    != (actual.AcceptedResult is not null))
                || ((plan.Snapshot.Definition.SpecialPoints == 0m)
                    != string.Equals(actual.StatusCode, ResultsStatusCodes.NotRunZeroBudget, StringComparison.Ordinal))) return false;
        }

        if (row.SimilarityResults.Length != enabledQuestions.Count) return false;
        for (int index = 0; index < enabledQuestions.Count; index++)
        {
            CheckpointSimilarityResult actual = row.SimilarityResults[index];
            if (actual is null
                || !string.Equals(actual.QuestionId, enabledQuestions[index].Id, StringComparison.Ordinal)
                || string.Equals(actual.StatusCode, ResultsStatusCodes.Cancelled, StringComparison.Ordinal)
                || (string.Equals(actual.StatusCode, ResultsStatusCodes.Success, StringComparison.Ordinal)
                    != (actual.AcceptedResult is not null))) return false;
        }

        return true;
    }

    private static bool RuntimeCompatible(CheckpointRuntimeIdentity saved, CheckpointRuntimeIdentity current) =>
        SameApplicationMajor(saved.ApplicationIdentity, current.ApplicationIdentity)
        && string.Equals(saved.CliVersion, current.CliVersion, StringComparison.Ordinal)
        && string.Equals(saved.CliSha256, current.CliSha256, StringComparison.Ordinal)
        && string.Equals(saved.SdkInformationalVersion, current.SdkInformationalVersion, StringComparison.Ordinal);

    private static bool SameApplicationMajor(string saved, string current)
    {
        int savedSeparator = saved.LastIndexOf('/');
        int currentSeparator = current.LastIndexOf('/');
        if (savedSeparator <= 0 || currentSeparator <= 0
            || !string.Equals(saved[..savedSeparator], current[..currentSeparator], StringComparison.Ordinal)
            || !Version.TryParse(saved[(savedSeparator + 1)..], out Version? savedVersion)
            || !Version.TryParse(current[(currentSeparator + 1)..], out Version? currentVersion))
        {
            return string.Equals(saved, current, StringComparison.Ordinal);
        }

        return savedVersion.Major == currentVersion.Major;
    }

    internal static bool PathsEqual(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}