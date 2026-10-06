using System.Reflection;
using StudyReportEvaluator.App.Logging;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Logging;

// Requirements: NFR-SEC-002 (AC-067)
public sealed class SafeLoggerCanaryTests
{
    private const string PromptCanary = "PRIVATE-PROMPT-CANARY";
    private const string ResponseCanary = "PRIVATE-RESPONSE-CANARY";
    private const string ReasonCanary = "PRIVATE-REASON-CANARY";
    private const string EvidenceCanary = "PRIVATE-EVIDENCE-CANARY";
    private const string TokenCanary = "ghp_PRIVATE_TOKEN_CANARY";
    private const string PathCanary = "C:\\private\\student.xlsx";

    [Fact]
    public void Every_severity_emits_only_closed_codes_safe_numbers_and_generated_identity()
    {
        CapturingSink sink = new();
        SafeLogger logger = new(sink);
        Assert.True(SafeLogIdentity.TryCreateSession(
            "a03-0123456789abcdef0123456789abcdef",
            out SafeLogIdentity? identity));
        SafeLogDimensions dimensions = new(
            attemptNumber: 1,
            attemptLimit: 3,
            maxConcurrency: 1,
            SafeLogFailureCategory.None);

        logger.Trace(SafeLogEventCode.EvaluationAttemptStarted, dimensions, identity);
        logger.Debug(SafeLogEventCode.EvaluationRetryScheduled, dimensions, identity);
        logger.Information(SafeLogEventCode.EvaluationAttemptSucceeded, dimensions, identity);
        logger.Warning(SafeLogEventCode.EvaluationAttemptFailed, dimensions, identity);
        logger.Error(SafeLogEventCode.EvaluationCleanupFailed, dimensions, identity);
        logger.Critical(SafeLogEventCode.EvaluationCancelled, dimensions, identity);

        Assert.Equal(Enum.GetValues<SafeLogSeverity>(), sink.Entries.Select(entry => entry.Severity));
        Assert.All(sink.Entries, entry =>
        {
            string rendered = entry.ToString();
            Assert.DoesNotContain(PromptCanary, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(ResponseCanary, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(ReasonCanary, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(EvidenceCanary, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(TokenCanary, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(PathCanary, rendered, StringComparison.OrdinalIgnoreCase);
            Assert.Same(dimensions, entry.Dimensions);
            Assert.Same(identity, entry.Identity);
        });
    }

    [Theory]
    [InlineData(PromptCanary)]
    [InlineData(ResponseCanary)]
    [InlineData(ReasonCanary)]
    [InlineData(EvidenceCanary)]
    [InlineData(TokenCanary)]
    [InlineData(PathCanary)]
    [InlineData("a03-0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("a03-0123456789abcdef0123456789abcdeg")]
    public void Content_token_path_and_noncanonical_values_cannot_become_log_identities(string unsafeValue)
    {
        Assert.False(SafeLogIdentity.TryCreateSession(unsafeValue, out SafeLogIdentity? identity));
        Assert.Null(identity);
    }

    [Fact]
    public void Logger_public_write_surface_has_no_free_text_or_exception_parameter()
    {
        MethodInfo[] writeMethods = typeof(SafeLogger)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.ReturnType == typeof(void))
            .ToArray();

        Assert.NotEmpty(writeMethods);
        Assert.All(writeMethods, method =>
        {
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(string));
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => typeof(Exception).IsAssignableFrom(parameter.ParameterType));
            Assert.DoesNotContain(method.GetParameters(), parameter =>
            {
                string name = parameter.Name ?? string.Empty;
                return name.Contains("prompt", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("response", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("reason", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("evidence", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("token", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("path", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("message", StringComparison.OrdinalIgnoreCase);
            });
        });
    }

    [Fact]
    public void Sink_failure_never_changes_control_flow_or_echoes_its_content()
    {
        SafeLogger logger = new(new ThrowingSink());
        SafeLogDimensions dimensions = new(
            attemptNumber: 1,
            attemptLimit: 2,
            maxConcurrency: 1,
            SafeLogFailureCategory.SchemaInvalid);

        Exception? exception = Record.Exception(() =>
            logger.Error(SafeLogEventCode.EvaluationAttemptFailed, dimensions));

        Assert.Null(exception);
    }

    private sealed class CapturingSink : ISafeLogSink
    {
        public List<SafeLogEntry> Entries { get; } = [];

        public void Write(SafeLogEntry entry) => Entries.Add(entry);
    }

    private sealed class ThrowingSink : ISafeLogSink
    {
        public void Write(SafeLogEntry entry) =>
            throw new InvalidOperationException(
                $"{PromptCanary}|{ResponseCanary}|{ReasonCanary}|{EvidenceCanary}|{TokenCanary}|{PathCanary}");
    }
}