using System.Text.RegularExpressions;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Tests.Evidence;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Copilot;

public sealed partial class AuthenticatedSyntheticSmokeTests
{
    private const string OptInEnvironmentVariable = "STUDY_REPORT_EVALUATOR_COPILOT_LIVE_SMOKE";

    [Fact]
    public void Advisory_status_set_is_exact()
    {
        Assert.Equal(
            ["PASS", "SKIPPED_NOT_AUTHENTICATED", "NOT_RUN", "FAILED_ADVISORY"],
            Enum.GetNames<AdvisoryStatus>());
    }

    [Fact]
    public async Task Default_policy_does_not_probe_login_or_start_network_even_if_login_would_be_available()
    {
        bool authenticationProbeCalled = false;
        bool liveEvaluationCalled = false;

        AdvisoryResult result = await ExecuteAdvisoryPolicyAsync(
            explicitlyOptedIn: false,
            () =>
            {
                authenticationProbeCalled = true;
                return Task.FromResult(CopilotAuthenticationStatus.Available);
            },
            () =>
            {
                liveEvaluationCalled = true;
                return Task.FromResult(true);
            });

        Assert.Equal(AdvisoryStatus.NOT_RUN, result.Status);
        Assert.Equal("explicit_opt_in_absent", result.Rationale);
        Assert.False(authenticationProbeCalled);
        Assert.False(liveEvaluationCalled);
    }

    [Fact]
    public async Task Optional_authenticated_synthetic_smoke_reports_advisory_status_honestly()
    {
        bool optedIn = string.Equals(
            Environment.GetEnvironmentVariable(OptInEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

        AdvisoryResult result;
        if (!optedIn)
        {
            result = AdvisoryResult.NotRun();
        }
        else
        {
            result = await RunOptedInSmokeAsync(TestContext.Current.CancellationToken);
            AdvisoryEvidenceWriter.Write(
                "live-copilot-smoke.json",
                "authenticated_live_copilot_smoke",
                result.Status.ToString(),
                result.Rationale);
        }

        Assert.Contains(result.Status, Enum.GetValues<AdvisoryStatus>());
        Assert.Matches(SafeRationalePattern(), result.Rationale);
        Assert.DoesNotContain("synthetic primary", result.Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("synthetic reason", result.Rationale, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("model", result.Rationale, StringComparison.OrdinalIgnoreCase);
        if (!optedIn)
        {
            Assert.Equal(AdvisoryStatus.NOT_RUN, result.Status);
        }
    }

    private static async Task<AdvisoryResult> RunOptedInSmokeAsync(CancellationToken cancellationToken)
    {
        try
        {
            CopilotAuthenticationService authentication = new();
            CopilotAuthenticationResult authResult = await authentication
                .CheckAsync(cancellationToken)
                .ConfigureAwait(false);
            if (authResult.Status != CopilotAuthenticationStatus.Available)
            {
                return AdvisoryResult.NotAuthenticated();
            }

            string? modelId = authResult.AvailableModelIds.FirstOrDefault();
            if (modelId is null)
            {
                return AdvisoryResult.Failed();
            }

            EphemeralEvaluationRunner runner = new();
            EphemeralEvaluationResult evaluation = await runner
                .EvaluateAsync(CreateFixedSyntheticPayload(), modelId, cancellationToken)
                .ConfigureAwait(false);
            return evaluation.IsSuccess
                ? AdvisoryResult.Passed()
                : AdvisoryResult.Failed();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AdvisoryResult.Failed();
        }
        catch
        {
            return AdvisoryResult.Failed();
        }
    }

    private static async Task<AdvisoryResult> ExecuteAdvisoryPolicyAsync(
        bool explicitlyOptedIn,
        Func<Task<CopilotAuthenticationStatus>> authenticationProbe,
        Func<Task<bool>> liveEvaluation)
    {
        ArgumentNullException.ThrowIfNull(authenticationProbe);
        ArgumentNullException.ThrowIfNull(liveEvaluation);

        if (!explicitlyOptedIn)
        {
            return AdvisoryResult.NotRun();
        }

        CopilotAuthenticationStatus authenticationStatus = await authenticationProbe().ConfigureAwait(false);
        if (authenticationStatus != CopilotAuthenticationStatus.Available)
        {
            return AdvisoryResult.NotAuthenticated();
        }

        return await liveEvaluation().ConfigureAwait(false)
            ? AdvisoryResult.Passed()
            : AdvisoryResult.Failed();
    }

    private static SafeEvaluationPayload CreateFixedSyntheticPayload() => new(
        "synthetic-question",
        "synthetic-evaluator",
        "Evaluate the fixed synthetic primary text. Call submit_quantification exactly once with criterion synthetic-criterion, score 1, a short reason, evidence synthetic primary, PRIMARY_ANSWER, and source column SYNTHETIC.",
        new EvaluationSourceCell(
            EvaluationSourceKind.PrimaryAnswer,
            "SYNTHETIC",
            "synthetic primary"),
        [],
        [new ExpectedCriterion(
            "synthetic-criterion",
            "Synthetic criterion",
            new ScoreRange(0m, 1m))]);

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeRationalePattern();

    public enum AdvisoryStatus
    {
        PASS,
        SKIPPED_NOT_AUTHENTICATED,
        NOT_RUN,
        FAILED_ADVISORY,
    }

    public sealed record AdvisoryResult(AdvisoryStatus Status, string Rationale)
    {
        public static AdvisoryResult Passed() => new(AdvisoryStatus.PASS, "synthetic_result_accepted");

        public static AdvisoryResult NotAuthenticated() =>
            new(AdvisoryStatus.SKIPPED_NOT_AUTHENTICATED, "logged_in_user_unavailable");

        public static AdvisoryResult NotRun() => new(AdvisoryStatus.NOT_RUN, "explicit_opt_in_absent");

        public static AdvisoryResult Failed() =>
            new(AdvisoryStatus.FAILED_ADVISORY, "synthetic_evaluation_failed");
    }
}