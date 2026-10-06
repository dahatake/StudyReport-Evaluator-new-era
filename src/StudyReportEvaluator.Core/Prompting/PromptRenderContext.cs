using System.Collections.Immutable;
using StudyReportEvaluator.Core.Domain;

namespace StudyReportEvaluator.Core.Prompting;

public sealed record PromptRenderContext(
    string Question,
    string Answer,
    string SupportingInformation,
    string EvaluationCriteria,
    string MinimumScore,
    string MaximumScore)
{
    public override string ToString() => "PromptRenderContext { content = <redacted> }";
}

public enum EvaluationSourceKind
{
    PrimaryAnswer,
    SupportingColumn,
}

public sealed record EvaluationSourceCell(
    EvaluationSourceKind Kind,
    string SourceColumnId,
    string Value)
{
    public override string ToString() => $"EvaluationSourceCell {{ Kind = {Kind}, SourceColumnId = {SourceColumnId}, Value = <redacted> }}";
}

public sealed record ExpectedCriterion(
    string CriterionId,
    string DisplayName,
    ScoreRange Range)
{
    public override string ToString() => $"ExpectedCriterion {{ CriterionId = {CriterionId}, DisplayName = {DisplayName}, Range = {Range.Minimum}..{Range.Maximum} }}";
}

public sealed class SafeEvaluationPayload
{
    public SafeEvaluationPayload(
        string questionId,
        string evaluatorId,
        string renderedPrompt,
        EvaluationSourceCell primarySource,
        ImmutableArray<EvaluationSourceCell> supportingSources,
        ImmutableArray<ExpectedCriterion> expectedCriteria)
    {
        QuestionId = questionId;
        EvaluatorId = evaluatorId;
        RenderedPrompt = renderedPrompt;
        PrimarySource = primarySource;
        SupportingSources = supportingSources;
        ExpectedCriteria = expectedCriteria;
    }

    public string QuestionId { get; }

    public string EvaluatorId { get; }

    public string RenderedPrompt { get; }

    public EvaluationSourceCell PrimarySource { get; }

    public ImmutableArray<EvaluationSourceCell> SupportingSources { get; }

    public ImmutableArray<ExpectedCriterion> ExpectedCriteria { get; }

    public IEnumerable<EvaluationSourceCell> Sources => SupportingSources.Prepend(PrimarySource);

    public override string ToString() =>
        $"SafeEvaluationPayload {{ QuestionId = {QuestionId}, EvaluatorId = {EvaluatorId}, Content = <redacted>, Sources = {1 + SupportingSources.Length}, Criteria = {ExpectedCriteria.Length} }}";
}

public sealed record SafeReferenceAnswerPayload(
    string QuestionId,
    string RenderedPrompt)
{
    public override string ToString() =>
        $"{nameof(SafeReferenceAnswerPayload)} {{ QuestionId = {QuestionId}, Content = <redacted> }}";
}

public sealed class SafeSpecialEvaluationPayload
{
    public SafeSpecialEvaluationPayload(
        string questionId,
        string specialEvaluationId,
        string renderedPrompt,
        EvaluationSourceCell primarySource,
        ImmutableArray<EvaluationSourceCell> supportingSources)
    {
        QuestionId = questionId;
        SpecialEvaluationId = specialEvaluationId;
        RenderedPrompt = renderedPrompt;
        PrimarySource = primarySource;
        SupportingSources = supportingSources;
    }

    public string QuestionId { get; }

    public string SpecialEvaluationId { get; }

    public string RenderedPrompt { get; }

    public EvaluationSourceCell PrimarySource { get; }

    public ImmutableArray<EvaluationSourceCell> SupportingSources { get; }

    public IEnumerable<EvaluationSourceCell> Sources => SupportingSources.Prepend(PrimarySource);

    public override string ToString() =>
        $"{nameof(SafeSpecialEvaluationPayload)} {{ QuestionId = {QuestionId}, SpecialEvaluationId = {SpecialEvaluationId}, Content = <redacted>, Sources = {1 + SupportingSources.Length} }}";
}

public class PromptConfigurationException : Exception
{
    public PromptConfigurationException(string code, string message, int? position = null)
        : base(message)
    {
        Code = code;
        Position = position;
    }

    public string Code { get; }

    public int? Position { get; }
}
