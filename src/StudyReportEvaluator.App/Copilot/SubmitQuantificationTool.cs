using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using GitHub.Copilot;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Validation;

namespace StudyReportEvaluator.App.Copilot;

public enum SubmitQuantificationStatus
{
    NotInvoked,
    Processing,
    Accepted,
    InvalidPayload,
    DuplicateInvocation,
}

public sealed class SubmitQuantificationOutcome
{
    internal SubmitQuantificationOutcome(
        SubmitQuantificationStatus status,
        int invocationCount,
        ImmutableArray<string> errorCodes,
        QuantificationResult? acceptedResult)
    {
        Status = status;
        InvocationCount = invocationCount;
        ErrorCodes = errorCodes;
        AcceptedResult = acceptedResult;
    }

    public SubmitQuantificationStatus Status { get; }

    public int InvocationCount { get; }

    public ImmutableArray<string> ErrorCodes { get; }

    public QuantificationResult? AcceptedResult { get; }

    public bool IsAccepted =>
        Status == SubmitQuantificationStatus.Accepted
        && InvocationCount == 1
        && AcceptedResult is not null;

    public override string ToString() =>
        $"SubmitQuantificationOutcome {{ Status = {Status}, InvocationCount = {InvocationCount}, ErrorCount = {ErrorCodes.Length}, Content = <redacted> }}";
}

public sealed class SubmitQuantificationTool
{
    public const string AcceptedAcknowledgement = "accepted";
    public const string RejectedAcknowledgement = "rejected";

    private const int MaximumCellCharacters = 32_767;

    private static readonly string[] RootProperties =
    [
        "EvaluatorId",
        "Criteria",
    ];

    private static readonly string[] RootRequiredProperties =
    [
        "Criteria",
    ];

    private static readonly string[] CriterionProperties =
    [
        "CriterionId",
        "RawScore",
        "Reason",
        "Evidence",
        "EvidenceSource",
        "EvidenceSourceColumnId",
    ];

    private static readonly ImmutableArray<string> DuplicateInvocationErrors =
        ["TOOL_INVOCATION_COUNT_INVALID"];

    private readonly object _gate = new();
    private readonly QuantificationResultValidator _validator;
    private int _invocationCount;
    private SubmitQuantificationStatus _status = SubmitQuantificationStatus.NotInvoked;
    private ImmutableArray<string> _errorCodes = [];
    private QuantificationResult? _acceptedResult;

    public SubmitQuantificationTool(SafeEvaluationPayload expectedPayload)
        : this(expectedPayload, new QuantificationResultValidator())
    {
    }

    internal SubmitQuantificationTool(
        SafeEvaluationPayload expectedPayload,
        QuantificationResultValidator validator)
    {
        EvaluationSchemaFactory.ValidatePayloadContract(expectedPayload);
        ArgumentNullException.ThrowIfNull(validator);

        ExpectedPayload = expectedPayload;
        _validator = validator;
    }

    internal SafeEvaluationPayload ExpectedPayload { get; }

    public ValueTask<string> InvokeAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _invocationCount++;
            if (_invocationCount != 1)
            {
                InvalidateAsDuplicate();
                return ValueTask.FromResult(RejectedAcknowledgement);
            }

            _status = SubmitQuantificationStatus.Processing;
        }

        QuantificationResult? acceptedResult = null;
        ImmutableArray<string> errorCodes;
        try
        {
            if (!string.Equals(
                    invocation.ToolName,
                    EvaluationSchemaFactory.ToolName,
                    StringComparison.Ordinal))
            {
                errorCodes = ["UNEXPECTED_TOOL_NAME"];
            }
            else if (!TryParseArguments(invocation.Arguments, ExpectedPayload.EvaluatorId, out QuantificationResult? submitted, out errorCodes))
            {
                // The strict wire parser already produced fixed, content-free error codes.
            }
            else
            {
                QuantificationResultValidationOutcome validation = _validator.Validate(ExpectedPayload, submitted);
                if (validation.IsValid)
                {
                    acceptedResult = validation.AcceptedResult;
                    errorCodes = [];
                }
                else
                {
                    errorCodes = NormalizeCodes(validation.Errors.Select(error => error.Code));
                }
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or FormatException
                or InvalidOperationException
                or OverflowException)
        {
            errorCodes = ["TOOL_PAYLOAD_INVALID"];
        }

        lock (_gate)
        {
            if (_invocationCount != 1 || _status == SubmitQuantificationStatus.DuplicateInvocation)
            {
                InvalidateAsDuplicate();
                return ValueTask.FromResult(RejectedAcknowledgement);
            }

            _acceptedResult = acceptedResult;
            _errorCodes = errorCodes;
            _status = acceptedResult is null
                ? SubmitQuantificationStatus.InvalidPayload
                : SubmitQuantificationStatus.Accepted;

            return ValueTask.FromResult(
                acceptedResult is null ? RejectedAcknowledgement : AcceptedAcknowledgement);
        }
    }

    public SubmitQuantificationOutcome GetOutcome()
    {
        lock (_gate)
        {
            return new SubmitQuantificationOutcome(
                _status,
                _invocationCount,
                _errorCodes,
                _acceptedResult);
        }
    }

    public bool TryGetAcceptedResult(
        [NotNullWhen(true)] out QuantificationResult? acceptedResult)
    {
        lock (_gate)
        {
            if (_status == SubmitQuantificationStatus.Accepted
                && _invocationCount == 1
                && _acceptedResult is not null)
            {
                acceptedResult = _acceptedResult;
                return true;
            }

            acceptedResult = null;
            return false;
        }
    }

    public override string ToString()
    {
        lock (_gate)
        {
            return $"SubmitQuantificationTool {{ Status = {_status}, InvocationCount = {_invocationCount}, Content = <redacted> }}";
        }
    }

    private static bool TryParseArguments(
        JsonElement? arguments,
        string expectedEvaluatorId,
        [NotNullWhen(true)] out QuantificationResult? result,
        out ImmutableArray<string> errorCodes)
    {
        ImmutableArray<string>.Builder errors = ImmutableArray.CreateBuilder<string>();
        result = null;

        if (arguments is not JsonElement root)
        {
            errorCodes = ["ROOT_ARGUMENTS_REQUIRED"];
            return false;
        }

        Dictionary<string, JsonElement>? rootValues = ReadClosedObject(
            root,
            RootProperties,
            RootRequiredProperties,
            "ROOT",
            errors);
        if (rootValues is null)
        {
            errorCodes = NormalizeCodes(errors);
            return false;
        }

        // Models omit this app-known constant (observed with claude-sonnet-5); a returned value is still validated.
        string evaluatorId = expectedEvaluatorId;
        bool evaluatorRead = !rootValues.ContainsKey("EvaluatorId")
            || TryReadString(
                rootValues,
                "EvaluatorId",
                MaximumCellCharacters,
                errors,
                out evaluatorId);

        ImmutableArray<CriterionQuantificationResult>.Builder criteria =
            ImmutableArray.CreateBuilder<CriterionQuantificationResult>();
        bool criteriaRead = false;
        if (rootValues.TryGetValue("Criteria", out JsonElement criteriaElement))
        {
            if (criteriaElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add("CRITERIA_ARRAY_REQUIRED");
            }
            else
            {
                criteriaRead = true;
                foreach (JsonElement criterionElement in criteriaElement.EnumerateArray())
                {
                    if (TryParseCriterion(criterionElement, errors, out CriterionQuantificationResult? criterion))
                    {
                        criteria.Add(criterion);
                    }
                }
            }
        }

        errorCodes = NormalizeCodes(errors);
        if (!errorCodes.IsEmpty || !evaluatorRead || !criteriaRead)
        {
            return false;
        }

        result = new QuantificationResult
        {
            EvaluatorId = evaluatorId,
            Criteria = criteria.ToImmutable(),
        };
        return true;
    }

    private static bool TryParseCriterion(
        JsonElement element,
        ImmutableArray<string>.Builder errors,
        [NotNullWhen(true)] out CriterionQuantificationResult? criterion)
    {
        criterion = null;
        Dictionary<string, JsonElement>? values = ReadClosedObject(
            element,
            CriterionProperties,
            CriterionProperties,
            "CRITERION",
            errors);
        if (values is null)
        {
            return false;
        }

        bool criterionIdRead = TryReadString(
            values,
            "CriterionId",
            MaximumCellCharacters,
            errors,
            out string criterionId);
        bool rawScoreRead = TryReadDecimal(values, "RawScore", errors, out decimal rawScore);
        bool reasonRead = TryReadString(
            values,
            "Reason",
            MaximumCellCharacters,
            errors,
            out string reason);
        bool evidenceRead = TryReadString(
            values,
            "Evidence",
            MaximumCellCharacters,
            errors,
            out string evidence);
        bool evidenceSourceRead = TryReadEvidenceSource(
            values,
            errors,
            out EvidenceSourceKind evidenceSource);
        bool sourceColumnRead = TryReadString(
            values,
            "EvidenceSourceColumnId",
            MaximumCellCharacters,
            errors,
            out string evidenceSourceColumnId);

        if (!criterionIdRead
            || !rawScoreRead
            || !reasonRead
            || !evidenceRead
            || !evidenceSourceRead
            || !sourceColumnRead)
        {
            return false;
        }

        criterion = new CriterionQuantificationResult
        {
            CriterionId = criterionId,
            RawScore = rawScore,
            Reason = reason,
            Evidence = evidence,
            EvidenceSource = evidenceSource,
            EvidenceSourceColumnId = evidenceSourceColumnId,
        };
        return true;
    }

    private static Dictionary<string, JsonElement>? ReadClosedObject(
        JsonElement element,
        string[] allowedProperties,
        string[] requiredProperties,
        string scope,
        ImmutableArray<string>.Builder errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{scope}_OBJECT_REQUIRED");
            return null;
        }

        Dictionary<string, JsonElement> values = new(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name, StringComparer.Ordinal))
            {
                errors.Add($"{scope}_UNKNOWN_PROPERTY");
                continue;
            }

            if (!values.TryAdd(property.Name, property.Value))
            {
                errors.Add($"{scope}_DUPLICATE_PROPERTY");
            }
        }

        if (requiredProperties.Any(property => !values.ContainsKey(property)))
        {
            errors.Add($"{scope}_MISSING_PROPERTY");
        }

        return values;
    }

    private static bool TryReadString(
        IReadOnlyDictionary<string, JsonElement> values,
        string propertyName,
        int maximumLength,
        ImmutableArray<string>.Builder errors,
        out string value)
    {
        value = string.Empty;
        if (!values.TryGetValue(propertyName, out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add("PROPERTY_TYPE_INVALID");
            return false;
        }

        value = element.GetString() ?? string.Empty;
        if (value.Length > maximumLength)
        {
            errors.Add("TEXT_LENGTH_INVALID");
            return false;
        }

        return true;
    }

    private static bool TryReadDecimal(
        IReadOnlyDictionary<string, JsonElement> values,
        string propertyName,
        ImmutableArray<string>.Builder errors,
        out decimal value)
    {
        value = default;
        if (!values.TryGetValue(propertyName, out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDecimal(out value))
        {
            errors.Add("RAW_SCORE_INVALID");
            return false;
        }

        return true;
    }

    private static bool TryReadEvidenceSource(
        IReadOnlyDictionary<string, JsonElement> values,
        ImmutableArray<string>.Builder errors,
        out EvidenceSourceKind value)
    {
        value = default;
        if (!values.TryGetValue("EvidenceSource", out JsonElement element))
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add("PROPERTY_TYPE_INVALID");
            return false;
        }

        value = element.GetString() switch
        {
            "PRIMARY_ANSWER" => EvidenceSourceKind.PrimaryAnswer,
            "SUPPORTING_COLUMN" => EvidenceSourceKind.SupportingColumn,
            "NONE" => EvidenceSourceKind.None,
            _ => (EvidenceSourceKind)(-1),
        };
        if (!Enum.IsDefined(value))
        {
            errors.Add("EVIDENCE_SOURCE_INVALID");
            return false;
        }

        return true;
    }

    private static ImmutableArray<string> NormalizeCodes(IEnumerable<string> codes) =>
        codes.Distinct(StringComparer.Ordinal).ToImmutableArray();

    private void InvalidateAsDuplicate()
    {
        _status = SubmitQuantificationStatus.DuplicateInvocation;
        _acceptedResult = null;
        _errorCodes = DuplicateInvocationErrors;
    }
}