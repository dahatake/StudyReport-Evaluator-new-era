using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using GitHub.Copilot;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.Copilot;

public enum AuxiliarySubmitStatus
{
    NotInvoked,
    Processing,
    Accepted,
    InvalidPayload,
    DuplicateInvocation,
}

public sealed class SubmitReferenceAnswerTool
{
    public const string AcceptedAcknowledgement = "accepted";
    public const string RejectedAcknowledgement = "rejected";

    private static readonly string[] RootProperties = ["QuestionId", "Answer"];
    private static readonly ImmutableArray<string> DuplicateErrors = ["TOOL_INVOCATION_COUNT_INVALID"];

    private readonly object gate = new();
    private readonly AuxiliaryQuantificationResultValidator validator = new();
    private int invocationCount;
    private AuxiliarySubmitStatus status;
    private ImmutableArray<string> errorCodes = [];
    private ReferenceAnswerResult? acceptedResult;

    public SubmitReferenceAnswerTool(SafeReferenceAnswerPayload expectedPayload)
    {
        ArgumentNullException.ThrowIfNull(expectedPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPayload.QuestionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPayload.RenderedPrompt);
        ExpectedPayload = expectedPayload;
    }

    internal SafeReferenceAnswerPayload ExpectedPayload { get; }

    public int InvocationCount
    {
        get
        {
            lock (gate)
            {
                return invocationCount;
            }
        }
    }

    public AuxiliarySubmitStatus Status
    {
        get
        {
            lock (gate)
            {
                return status;
            }
        }
    }

    public ImmutableArray<string> ErrorCodes
    {
        get
        {
            lock (gate)
            {
                return errorCodes;
            }
        }
    }

    public ValueTask<string> InvokeAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            invocationCount++;
            if (invocationCount != 1)
            {
                InvalidateAsDuplicate();
                return ValueTask.FromResult(RejectedAcknowledgement);
            }

            status = AuxiliarySubmitStatus.Processing;
        }

        ReferenceAnswerResult? accepted = null;
        ImmutableArray<string> errors;
        try
        {
            if (!string.Equals(invocation.ToolName, AuxiliaryEvaluationSchemaFactory.ReferenceToolName, StringComparison.Ordinal))
            {
                errors = ["UNEXPECTED_TOOL_NAME"];
            }
            else if (!TryParse(invocation.Arguments, out ReferenceAnswerResult? submitted, out errors))
            {
                // Strict wire parsing supplied content-free codes.
            }
            else
            {
                AuxiliaryResultValidationOutcome<ReferenceAnswerResult> outcome =
                    validator.ValidateReference(ExpectedPayload, submitted);
                accepted = outcome.AcceptedResult;
                errors = AuxiliaryToolWireParser.NormalizeCodes(outcome.Errors.Select(error => error.Code));
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or FormatException or InvalidOperationException or OverflowException)
        {
            errors = ["TOOL_PAYLOAD_INVALID"];
        }

        lock (gate)
        {
            if (invocationCount != 1 || status == AuxiliarySubmitStatus.DuplicateInvocation)
            {
                InvalidateAsDuplicate();
                return ValueTask.FromResult(RejectedAcknowledgement);
            }

            acceptedResult = accepted;
            errorCodes = errors;
            status = accepted is null
                ? AuxiliarySubmitStatus.InvalidPayload
                : AuxiliarySubmitStatus.Accepted;
            return ValueTask.FromResult(accepted is null ? RejectedAcknowledgement : AcceptedAcknowledgement);
        }
    }

    public bool TryGetAcceptedResult([NotNullWhen(true)] out ReferenceAnswerResult? result)
    {
        lock (gate)
        {
            if (status == AuxiliarySubmitStatus.Accepted
                && invocationCount == 1
                && acceptedResult is not null)
            {
                result = acceptedResult;
                return true;
            }

            result = null;
            return false;
        }
    }

    public override string ToString()
    {
        lock (gate)
        {
            return $"{nameof(SubmitReferenceAnswerTool)} {{ Status = {status}, InvocationCount = {invocationCount}, Content = <redacted> }}";
        }
    }

    private static bool TryParse(
        JsonElement? arguments,
        [NotNullWhen(true)] out ReferenceAnswerResult? result,
        out ImmutableArray<string> errorCodes)
    {
        ImmutableArray<string>.Builder errors = ImmutableArray.CreateBuilder<string>();
        result = null;
        Dictionary<string, JsonElement>? values = AuxiliaryToolWireParser.ReadClosedObject(
            arguments,
            RootProperties,
            errors);
        if (values is null)
        {
            errorCodes = AuxiliaryToolWireParser.NormalizeCodes(errors);
            return false;
        }

        bool idRead = AuxiliaryToolWireParser.TryReadString(
            values,
            "QuestionId",
            requireNonWhitespace: true,
            errors,
            out string questionId);
        bool answerRead = AuxiliaryToolWireParser.TryReadString(
            values,
            "Answer",
            requireNonWhitespace: true,
            errors,
            out string answer);
        errorCodes = AuxiliaryToolWireParser.NormalizeCodes(errors);
        if (!errorCodes.IsEmpty || !idRead || !answerRead)
        {
            return false;
        }

        result = new ReferenceAnswerResult { QuestionId = questionId, Answer = answer };
        return true;
    }

    private void InvalidateAsDuplicate()
    {
        status = AuxiliarySubmitStatus.DuplicateInvocation;
        acceptedResult = null;
        errorCodes = DuplicateErrors;
    }
}
