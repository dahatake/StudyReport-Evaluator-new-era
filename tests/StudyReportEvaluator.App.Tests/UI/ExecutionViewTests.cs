using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-042 (AC-043)
public sealed class ExecutionViewTests
{
    [Fact]
    public async Task Available_authentication_and_run_bind_model_runtime_snapshot_progress_and_partial_result()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary partial = await U04TestSupport.CreateSummaryAsync(
            definition,
            metadata,
            cancelAfterFirst: true);
        CopilotRuntimeIdentity identity = U04TestSupport.RuntimeIdentity();
        RecordingAuthenticationBoundary authentication = new(
            new ExecutionAuthenticationSnapshot(
                ExecutionAuthenticationState.Available,
                [U04TestSupport.Model("model-a"), U04TestSupport.Model("model-b"), U04TestSupport.Model("model-a"), U04TestSupport.Model("auto")],
                identity));
        RecordingRunBoundary runner = new((request, progress, _) =>
        {
            progress?.Invoke(new EvaluationProgress(
                5,
                3,
                1,
                EvaluationProgressStatus.Running,
                DurableEvaluationStage.EvaluatingRows,
                referenceCompleted: 1,
                referenceTotal: 1,
                rowCompleted: 1,
                rowTotal: 2,
                detailStatusCode: "EVALUATING_ROW",
                finalPath: "C:\\PRIVATE\\result\\eval.xlsx",
                partialPath: "C:\\PRIVATE\\result\\eval.partial.xlsx"));
            return Task.FromResult(partial);
        });
        ExecutionViewModel viewModel = new(authentication, runner);
        string inputPath = Path.Combine(Path.GetTempPath(), "PRIVATE-U04-INPUT-CANARY.xlsx");
        viewModel.Configure(definition, metadata, inputPath);

        Assert.Equal(5, viewModel.PlannedEvaluationCount);
        Assert.Equal(15, viewModel.WorstCaseAttemptCount);
        Assert.Contains("最大 15 attempts", viewModel.PlanSummary, StringComparison.Ordinal);
        Assert.False(viewModel.CanStart);
        Assert.Contains(viewModel.TechnicalErrors, error => error.Code == "AUTH_CHECK_REQUIRED");

        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionAuthenticationState.Available, viewModel.AuthenticationState);
        Assert.Equal(["model-a", "model-b", "auto"], viewModel.AvailableModelIds);
        Assert.Equal("model-a", viewModel.SelectedModelId);
        Assert.Contains("1.0.82", viewModel.RuntimeIdentityText, StringComparison.Ordinal);
        Assert.Contains(identity.CliSha256[..12], viewModel.RuntimeIdentityText, StringComparison.Ordinal);
        Assert.DoesNotContain(identity.CliPath, viewModel.RuntimeIdentityText, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.CanStart);
        viewModel.SelectedModelId = "model-b";
        viewModel.MaxConcurrency = 3;
        ExecutionRunContext? notifiedContext = null;
        viewModel.RunCompleted += (_, eventArgs) => notifiedContext = eventArgs.Context;
        viewModel.RunCompleted += (_, _) => throw new InvalidOperationException("PRIVATE-OBSERVER-CANARY");

        await viewModel.StartAsync(TestContext.Current.CancellationToken);

        QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(runner.LastRequest);
        Assert.Equal("model-b", request.ModelId);
        Assert.Equal(64_000, request.MaximumPromptTokens);
        Assert.Equal(128_000, request.MaximumContextWindowTokens);
        Assert.Equal(3, request.MaxConcurrency);
        Assert.True(request.UseDurableWorkflow);
        Assert.Same(identity, request.RuntimeIdentity);
        Assert.EndsWith("result", request.OutputDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Null(request.ResumePartialPath);
        Assert.NotSame(definition, request.DraftDefinition);
        Assert.Equal(partial.DefinitionSha256, viewModel.LastRunContext?.Summary.DefinitionSha256);
        Assert.Same(viewModel.LastRunContext, notifiedContext);
        Assert.Same(identity, viewModel.LastRunContext?.RuntimeIdentity);
        Assert.Equal("model-b", viewModel.LastRunContext?.ModelId);
        Assert.Equal(2, viewModel.ProgressTotal);
        Assert.Equal(1, viewModel.ProgressCompleted);
        Assert.Equal(0, viewModel.ProgressInFlight);
        Assert.Equal(1, viewModel.ReferenceCompleted);
        Assert.Equal(1, viewModel.ReferenceTotal);
        Assert.Equal(1, viewModel.RowCompleted);
        Assert.Equal(2, viewModel.RowTotal);
        Assert.Contains("eval.partial.xlsx", viewModel.OutputIdentityText, StringComparison.Ordinal);
        Assert.True(viewModel.LastRunContext?.Summary.IsPartial);
        Assert.Contains("中断しました", viewModel.RunStatusText, StringComparison.Ordinal);
        Assert.DoesNotContain(viewModel.TechnicalErrors, error => error.Code == "RUN_FAILED");
        Assert.DoesNotContain(inputPath, viewModel.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PRIVATE-OBSERVER-CANARY", viewModel.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancel_routes_the_token_once_and_retains_the_boundary_partial_summary()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary partial = await U04TestSupport.CreateSummaryAsync(
            definition,
            metadata,
            cancelAfterFirst: true);
        ControlledRunBoundary runner = new(partial);
        ExecutionViewModel viewModel = U04TestSupport.ConfiguredExecutionViewModel(
            definition,
            metadata,
            runner);
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Task run = viewModel.StartAsync(TestContext.Current.CancellationToken);
        await runner.Started.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(viewModel.IsRunning);
        Assert.True(viewModel.CanCancel);

        viewModel.Cancel();
        await runner.CancellationObserved.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(viewModel.IsCancelling);
        Assert.False(viewModel.CanCancel);
        Assert.Contains("中断", viewModel.RunStatusText, StringComparison.Ordinal);
        runner.Release();
        await run;

        Assert.Equal(1, runner.CallCount);
        Assert.False(viewModel.IsRunning);
        Assert.False(viewModel.IsCancelling);
        Assert.Same(partial, viewModel.LastRunContext?.Summary);
        Assert.True(viewModel.LastRunContext?.Summary.IsExportReady);
        Assert.Equal(1, viewModel.ProgressCompleted);
        Assert.Contains("中断しました", viewModel.RunStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task New_mode_binds_output_directory_and_resume_mode_requires_preflight()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        RecordingRunBoundary runner = new((_, _, _) => Task.FromResult(summary));
        ExecutionViewModel viewModel = U04TestSupport.ConfiguredExecutionViewModel(
            definition,
            metadata,
            runner);
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.IsResumeMode);
        Assert.EndsWith("result", viewModel.OutputDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.CanStart);

        viewModel.IsResumeMode = true;
        viewModel.ResumePartialPath = Path.Combine(Path.GetTempPath(), "missing.partial.xlsx");
        Assert.False(viewModel.CanStart);
        Assert.Contains(viewModel.TechnicalErrors, error => error.Code == "RESUME_PARTIAL_REQUIRED");

        string directory = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-U03-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string partialPath = Path.Combine(directory, "eval.partial.xlsx");
        await File.WriteAllBytesAsync(partialPath, [1], TestContext.Current.CancellationToken);
        try
        {
            viewModel.ResumePartialPath = partialPath;
            Assert.False(viewModel.CanStart);
            Assert.Contains("再開元", viewModel.ValidationSummary, StringComparison.Ordinal);
            Assert.Equal(0, runner.CallCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(ExecutionAuthenticationState.AuthRequired, ResultsStatusCodes.AuthRequired)]
    [InlineData(ExecutionAuthenticationState.CliUnavailable, "COPILOT_CLI_UNAVAILABLE")]
    [InlineData(ExecutionAuthenticationState.RuntimeFailed, "COPILOT_RUNTIME_FAILED")]
    [InlineData(ExecutionAuthenticationState.Cancelled, null)]
    public async Task Finite_authentication_states_never_enable_run(
        ExecutionAuthenticationState state,
        string? expectedErrorCode)
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        ExecutionViewModel viewModel = new(
            new RecordingAuthenticationBoundary(new ExecutionAuthenticationSnapshot(state)),
            new RecordingRunBoundary((_, _, _) => throw new InvalidOperationException("must not run")));
        viewModel.Configure(
            definition,
            metadata,
            Path.Combine(Path.GetTempPath(), "PRIVATE-AUTH-INPUT-CANARY.xlsx"));

        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(state, viewModel.AuthenticationState);
        Assert.False(viewModel.CanStart);
        Assert.DoesNotContain("開始できます", viewModel.ValidationSummary, StringComparison.Ordinal);
        Assert.False(viewModel.HasCurrentRun);
        Assert.Empty(viewModel.AvailableModelIds);
        if (expectedErrorCode is not null)
        {
            Assert.Contains(viewModel.TechnicalErrors, error => error.Code == expectedErrorCode);
        }
    }

    [Fact]
    public async Task Invalid_concurrency_and_runtime_failure_are_blocking_and_content_redacted()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        const string exceptionCanary = "PRIVATE-ANSWER-AND-PATH-CANARY";
        ExecutionViewModel viewModel = U04TestSupport.ConfiguredExecutionViewModel(
            definition,
            metadata,
            new RecordingRunBoundary((_, _, _) => throw new IOException(exceptionCanary)));
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        viewModel.MaxConcurrency = 17;

        Assert.False(viewModel.CanStart);
        Assert.Contains(viewModel.TechnicalErrors, error => error.Code == "CONCURRENCY_OUT_OF_RANGE");
        viewModel.MaxConcurrency = 4;
        Assert.True(viewModel.CanStart);

        await viewModel.StartAsync(TestContext.Current.CancellationToken);

        ExecutionTechnicalError failure = Assert.Single(
            viewModel.TechnicalErrors,
            error => error.Code == "RUN_FAILED");
        Assert.DoesNotContain(exceptionCanary, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(exceptionCanary, failure.ToString(), StringComparison.Ordinal);
        Assert.Null(viewModel.LastRunContext);
        Assert.False(viewModel.CanStart);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Definition_weight_errors_preserve_node_ids_and_paths_without_exposing_contents(bool duplicateIds)
    {
        QuantificationDefinition definition = DefinitionWithTwoWeightErrors(duplicateIds);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RecordingRunBoundary runner = new((_, _, _) => throw new InvalidOperationException("must not run"));
        using ExecutionViewModel viewModel = U04TestSupport.ConfiguredExecutionViewModel(definition, metadata, runner);
        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        ExecutionTechnicalError[] errors = viewModel.TechnicalErrors
            .Where(error => error.Code == "WEIGHT_MUST_BE_POSITIVE").ToArray();
        Assert.Equal(2, errors.Length);
        Assert.Equal(new[] { "C1", duplicateIds ? "C1" : "C2" }, errors.Select(error => error.NodeId));
        Assert.Equal(new[]
        {
            "$.questions[0].evaluators[0].criteria[0]",
            "$.questions[0].evaluators[0].criteria[1]",
        }, errors.Select(error => error.Path));
        Assert.All(errors, error =>
        {
            Assert.Equal("Weight", error.Field);
            Assert.Contains(error.NodeId!, error.TargetText, StringComparison.Ordinal);
            Assert.Contains(error.Path!, error.TargetText, StringComparison.Ordinal);
            Assert.Contains(error.TargetText, error.AccessibleText, StringComparison.Ordinal);
            Assert.DoesNotContain(error.NodeId!, error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(error.Path!, error.ToString(), StringComparison.Ordinal);
        });
        Assert.Equal(duplicateIds ? 3 : 2, viewModel.TechnicalErrors.Count);
        Assert.All(viewModel.TechnicalErrors, error =>
        {
            string presentation = string.Join('\n', error.Message, error.TargetText, error.AccessibleText, error.ToString());
            Assert.DoesNotContain(TechnicalPromptCanary, presentation, StringComparison.Ordinal);
            Assert.DoesNotContain(TechnicalBodyCanary, presentation, StringComparison.Ordinal);
            Assert.DoesNotContain(TechnicalNameCanary, presentation, StringComparison.Ordinal);
        });
        Assert.False(viewModel.CanStart);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        viewModel.StartCommand.Execute(null);
        await viewModel.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, runner.CallCount);
        Assert.False(viewModel.HasCurrentRun);
        Assert.Equal(2, viewModel.TechnicalErrors.Count(error => error.Code == "WEIGHT_MUST_BE_POSITIVE"));
    }

    [Fact]
    public void Workbook_capacity_error_blocks_start_before_authentication_or_run_boundary()
    {
        const int criterionCount = 256;
        QuantificationDefinition single = U04TestSupport.Definition(2, 2);
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
        RecordingRunBoundary runner = new((_, _, _) => throw new InvalidOperationException("must not run"));
        ExecutionViewModel viewModel = new(
            new RecordingAuthenticationBoundary(
                new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired)),
            runner);

        viewModel.Configure(
            definition,
            metadata,
            Path.Combine(Path.GetTempPath(), "PRIVATE-CAPACITY-INPUT-CANARY.xlsx"));

        ExecutionTechnicalError error = Assert.Single(
            viewModel.TechnicalErrors,
            item => item.Code == "FUNCTION_ARGUMENT_LIMIT_EXCEEDED");
        Assert.Equal("Evaluator_Score", error.Field);
        Assert.Contains("256", error.Message, StringComparison.Ordinal);
        Assert.Contains("255", error.Message, StringComparison.Ordinal);
        Assert.False(viewModel.CanStart);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task Model_without_sdk_prompt_limit_is_selectable_and_starts_without_model_relative_preflight()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        RecordingRunBoundary runner = new((_, _, _) => Task.FromResult(summary));
        ExecutionViewModel viewModel = new(
            new RecordingAuthenticationBoundary(
                new ExecutionAuthenticationSnapshot(
                    ExecutionAuthenticationState.Available,
                    [U04TestSupport.AutoModel(), U04TestSupport.Model("model-known")],
                    U04TestSupport.RuntimeIdentity())),
            runner);
        viewModel.Configure(
            definition,
            metadata,
            Path.Combine(Path.GetTempPath(), "PRIVATE-MODEL-LIMIT-CANARY.xlsx"));

        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["auto", "model-known"], viewModel.AvailableModelIds);
        Assert.Equal("auto", viewModel.SelectedModelId);
        Assert.Null(viewModel.SelectedModelPromptTokenLimit);
        Assert.Equal("SDK未公開・事前検証なし", viewModel.SelectedModelLimitText);
        Assert.DoesNotContain(
            viewModel.TechnicalErrors,
            error => error.Code == "MODEL_PROMPT_LIMIT_UNAVAILABLE");
        Assert.True(viewModel.CanStart);

        await viewModel.StartAsync(TestContext.Current.CancellationToken);

        QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(runner.LastRequest);
        Assert.Equal("auto", request.ModelId);
        Assert.Null(request.MaximumPromptTokens);
        Assert.Null(request.MaximumContextWindowTokens);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal("SDK未公開・事前検証なし", viewModel.CurrentRunLimitText);
    }

    [Fact]
    public async Task Known_model_limit_is_surfaced_and_dispatched_as_positive_values()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata);
        RecordingRunBoundary runner = new((_, _, _) => Task.FromResult(summary));
        ExecutionViewModel viewModel = new(
            new RecordingAuthenticationBoundary(
                new ExecutionAuthenticationSnapshot(
                    ExecutionAuthenticationState.Available,
                    [U04TestSupport.Model("model-known"), U04TestSupport.AutoModel()],
                    U04TestSupport.RuntimeIdentity())),
            runner);
        viewModel.Configure(
            definition,
            metadata,
            Path.Combine(Path.GetTempPath(), "PRIVATE-KNOWN-LIMIT-CANARY.xlsx"));

        await viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);

        Assert.Equal("model-known", viewModel.SelectedModelId);
        Assert.Equal(64_000, viewModel.SelectedModelPromptTokenLimit);
        Assert.Equal("64,000 tokens", viewModel.SelectedModelLimitText);

        await viewModel.StartAsync(TestContext.Current.CancellationToken);

        QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(runner.LastRequest);
        Assert.Equal(64_000, request.MaximumPromptTokens);
        Assert.Equal(128_000, request.MaximumContextWindowTokens);
        Assert.Equal("64,000 tokens", viewModel.CurrentRunLimitText);
    }

    [Fact]
    public void Worst_case_retry_budget_over_twenty_thousand_blocks_start()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 3_334);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RecordingRunBoundary runner = new((_, _, _) => throw new InvalidOperationException("must not run"));
        ExecutionViewModel viewModel = new(
            new RecordingAuthenticationBoundary(
                new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired)),
            runner);

        viewModel.Configure(
            definition,
            metadata,
            Path.Combine(Path.GetTempPath(), "PRIVATE-ATTEMPT-BUDGET-CANARY.xlsx"));

        Assert.Equal(6_667, viewModel.PlannedEvaluationCount);
        Assert.Equal(20_001, viewModel.WorstCaseAttemptCount);
        ExecutionTechnicalError error = Assert.Single(
            viewModel.TechnicalErrors,
            item => item.Code == "ATTEMPT_BUDGET_TOO_LARGE");
        Assert.Contains("20001", error.Message, StringComparison.Ordinal);
        Assert.Contains("20000", error.Message, StringComparison.Ordinal);
        Assert.False(viewModel.CanStart);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public void Public_execution_contract_has_no_secret_or_warning_gate_input()
    {
        string[] prohibited =
        [
            "Password",
            "PersonalAccessToken",
            "ClientSecret",
            "ApiKey",
            "Acknowledge",
            "Consent",
            "Dismiss",
        ];

        Assert.DoesNotContain(
            typeof(ExecutionViewModel).GetMembers(),
            member => prohibited.Any(term => member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            typeof(ExecutionRunContext).GetMembers(),
            member => prohibited.Any(term => member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    [AvaloniaFact]
    public async Task Checking_without_technical_errors_never_claims_start_and_notifies_the_bound_status()
    {
        TaskCompletionSource<ExecutionAuthenticationSnapshot> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ExecutionAuthenticationSnapshot available = new(ExecutionAuthenticationState.Available,
            [U04TestSupport.Model("model-a"), U04TestSupport.Model("auto")], U04TestSupport.RuntimeIdentity());
        RecordingRunBoundary runner = new((_, _, _) => throw new InvalidOperationException("must not run"));
        using ExecutionViewModel viewModel = new(new DeferredAuthenticationBoundary(completion.Task), runner);
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        viewModel.Configure(definition, U01TestSupport.ValidateMapping(definition).Metadata,
            Path.Combine(Path.GetTempPath(), "T20-checking-input.xlsx"));
        List<(bool CanStart, string Summary)> notifications = [];
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ExecutionViewModel.ValidationSummary))
            {
                notifications.Add((viewModel.CanStart, viewModel.ValidationSummary));
            }
        };
        ExecutionView view = new(viewModel);
        Window window = new() { Width = 950, Height = 450, Content = view };
        Task? checking = null;
        try
        {
            window.Show();
            Render();
            checking = viewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
            Render();

            Assert.False(checking.IsCompleted);
            Assert.True(viewModel.IsCheckingAuthentication);
            Assert.Equal(ExecutionAuthenticationState.Checking, viewModel.AuthenticationState);
            Assert.Empty(viewModel.TechnicalErrors);
            Assert.False(viewModel.HasTechnicalErrors);
            Assert.True(viewModel.IsTechnicallyValid);
            Assert.False(viewModel.CanStart);
            AssertValidationStatus(view, viewModel, "確認中");
            Assert.NotEmpty(notifications);
            Assert.Equal(viewModel.ValidationSummary, notifications[^1].Summary);
            await viewModel.StartAsync(TestContext.Current.CancellationToken);
            Assert.Equal(0, runner.CallCount);
            Assert.False(viewModel.HasCurrentRun);
            Assert.False(Required<TextBox>(view, "CurrentRunOutputTextBox").IsEffectivelyVisible);
            Assert.StartsWith("次回 ", Required<TextBox>(view, "EffectiveModelTextBox").Text, StringComparison.Ordinal);

            completion.TrySetResult(available);
            await checking.WaitAsync(LoginTestWait, TestContext.Current.CancellationToken);
            Render();

            Assert.False(viewModel.IsCheckingAuthentication);
            Assert.Empty(viewModel.TechnicalErrors);
            Assert.True(viewModel.CanStart);
            AssertValidationStatus(view, viewModel, "run を開始できます");
            Assert.Equal(viewModel.ValidationSummary, notifications[^1].Summary);
            Assert.All(notifications, state => Assert.Equal(state.CanStart,
                state.Summary.Contains("開始できます", StringComparison.Ordinal)));
            Assert.False(viewModel.HasCurrentRun); // Readiness is not a started run.
            Assert.Null(viewModel.CurrentRunModelId);
            Assert.Null(viewModel.CurrentRunMaxConcurrency);
            Assert.Equal(string.Empty, viewModel.CurrentRunOutputSummary);
            Assert.Equal(0, runner.CallCount);
        }
        finally
        {
            completion.TrySetResult(available);
            try
            {
                if (checking is not null)
                {
                    await checking.WaitAsync(LoginTestWait, TestContext.Current.CancellationToken);
                }
            }
            finally
            {
                window.Close();
            }
        }
    }

    [AvaloniaFact]
    public void Login_controls_bind_accessible_commands_without_automatic_activity()
    {
        using LoginViewHarness harness = new();
        ExecutionViewModel viewModel = harness.ViewModel;
        Button check = Required<Button>(harness.Panel, "CheckAuthenticationButton");
        Button login = Required<Button>(harness.Panel, "StartCopilotLogin");
        Button cancelLogin = Required<Button>(harness.Panel, "CancelCopilotLogin");
        Button start = Required<Button>(harness.View, "StartRunButton");
        Button cancelRun = Required<Button>(harness.View, "CancelRunButton");
        StackPanel panel = Required<StackPanel>(harness.Panel, "CopilotLoginPanel");
        TextBlock instructions = Required<TextBlock>(harness.Panel, "CopilotLoginInstructions");

        Assert.Same(viewModel.LoginCommand, login.Command);
        Assert.Same(viewModel.CancelLoginCommand, cancelLogin.Command);
        Assert.Equal("StartCopilotLogin", AutomationProperties.GetAutomationId(login));
        Assert.Equal("CancelCopilotLogin", AutomationProperties.GetAutomationId(cancelLogin));
        Assert.Equal("GitHubにログイン", login.Content);
        Assert.Equal("ログインを取り消す", cancelLogin.Content);
        Assert.Equal("GitHubにログイン", AutomationProperties.GetName(login));
        Assert.Equal("ログインを取り消す", AutomationProperties.GetName(cancelLogin));
        Assert.All(new[] { login, cancelLogin }, button =>
        {
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(button)));
            Assert.True(button.Focusable);
            Assert.True(button.MinHeight >= 44d);
            Assert.True(button.IsEffectivelyVisible);
        });
        Assert.Same(viewModel.CheckAuthenticationCommand, check.Command);
        Assert.Same(viewModel.StartCommand, start.Command);
        Assert.Same(viewModel.CancelCommand, cancelRun.Command);
        Assert.Equal("CheckCopilotAuthentication", AutomationProperties.GetAutomationId(check));
        Assert.Equal("StartQuantification", AutomationProperties.GetAutomationId(start));
        Assert.Equal("CancelQuantification", AutomationProperties.GetAutomationId(cancelRun));
        Assert.Equal("Copilot 状態を確認", check.Content);
        Assert.Equal("定量化を開始", start.Content);
        Assert.Equal("中断", cancelRun.Content);
        Assert.NotSame(check, harness.PanelWindow.FocusManager?.GetFocusedElement());
        Assert.Contains("別のブラウザー", instructions.Text, StringComparison.Ordinal);
        Assert.Contains("パスワードやトークンを取得・保存しません", ToolTip.GetTip(login)?.ToString(), StringComparison.Ordinal);
        Assert.Equal("起動時にこのPCの既存ログインを自動確認し、なければ自動ログインを試みます。別のブラウザーで認証後、モデル一覧を自動更新・保存します。定量化は自動で開始しません。", instructions.Text);
        Assert.Contains("自動で開始しません", instructions.Text, StringComparison.Ordinal);
        Assert.All(new[] { check, login, cancelLogin, start, cancelRun }, button =>
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(button)?.ToString())));
        Assert.Empty(panel.GetVisualDescendants().OfType<TextBox>());
        Assert.Null(harness.View.FindControl<Border>("EthicsWarningBanner"));
        AssertLoginStatus(harness);

        string[] ids = harness.View.GetVisualDescendants().OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.True(check.IsEffectivelyEnabled);
        Assert.True(login.IsEffectivelyEnabled);
        Assert.False(cancelLogin.IsEffectivelyEnabled);
        Assert.False(start.IsEffectivelyEnabled);
        Assert.False(cancelRun.IsEffectivelyEnabled);
        Assert.True(viewModel.CanLogin);
        Assert.False(viewModel.CanCancelLogin);
        Assert.False(viewModel.IsLoggingIn);
        Assert.Null(viewModel.LastLoginTask);
        Assert.Equal(0, harness.Resolver.CallCount);
        Assert.Equal(0, harness.FactoryCallCount);
        Assert.Equal(0, harness.Process.StartCount);
        Assert.Equal(0, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_button_click_or_keyboard_automatically_rechecks_authentication(bool useKeyboard)
    {
        using LoginViewHarness harness = new();
        ExecutionViewModel viewModel = harness.ViewModel;
        Button check = Required<Button>(harness.Panel, "CheckAuthenticationButton");
        Button login = Required<Button>(harness.Panel, "StartCopilotLogin");
        Button cancel = Required<Button>(harness.Panel, "CancelCopilotLogin");
        Button start = Required<Button>(harness.View, "StartRunButton");
        Activate(harness.PanelWindow, check, useKeyboard);
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.True(start.IsEffectivelyEnabled);
        AssertValidationStatus(harness.View, viewModel, "run を開始できます");

        int clicks = 0;
        login.Click += (_, _) => clicks++;
        Activate(harness.PanelWindow, login, useKeyboard);
        Task task = Assert.IsAssignableFrom<Task>(viewModel.LastLoginTask);

        Assert.Equal(1, clicks);
        Assert.Equal(1, harness.Resolver.CallCount);
        Assert.Equal(1, harness.FactoryCallCount);
        Assert.Equal(1, harness.Process.StartCount);
        Assert.False(task.IsCompleted);
        Assert.True(viewModel.IsLoggingIn);
        Assert.False(viewModel.CanLogin);
        Assert.True(viewModel.CanCancelLogin);
        Assert.False(login.IsEffectivelyEnabled);
        Assert.True(cancel.IsEffectivelyEnabled);
        Assert.False(check.IsEffectivelyEnabled);
        Assert.False(start.IsEffectivelyEnabled);
        Assert.Equal(ExecutionAuthenticationState.NotChecked, viewModel.AuthenticationState);
        Assert.Equal(["model-test", "auto"], viewModel.AvailableModelIds);
        Assert.Null(viewModel.SelectedModelId);
        AssertLoginStatus(harness);
        AssertValidationStatus(harness.View, viewModel, "ログイン中");

        harness.Process.Complete(0);
        await task.WaitAsync(LoginTestWait, TestContext.Current.CancellationToken);
        Render();

        Assert.False(viewModel.IsLoggingIn);
        Assert.True(login.IsEffectivelyEnabled);
        Assert.True(check.IsEffectivelyEnabled);
        Assert.False(cancel.IsEffectivelyEnabled);
        Assert.True(start.IsEffectivelyEnabled);
        Assert.Equal(ExecutionAuthenticationState.Available, viewModel.AuthenticationState);
        Assert.True(viewModel.IsAuthenticationAvailable);
        Assert.Equal(["model-test", "auto"], viewModel.AvailableModelIds);
        Assert.Equal("model-test", viewModel.SelectedModelId);
        Assert.Contains("モデル一覧の更新が完了", viewModel.LoginStatusText, StringComparison.Ordinal);
        Assert.Equal(2, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
        Assert.Null(viewModel.LastRunContext);
        AssertLoginStatus(harness);
        AssertValidationStatus(harness.View, viewModel, "run を開始できます");

        Activate(harness.PanelWindow, check, useKeyboard);

        Assert.Equal(3, harness.Authentication.CallCount);
        Assert.Equal(ExecutionAuthenticationState.Available, viewModel.AuthenticationState);
        Assert.Equal("model-test", viewModel.SelectedModelId);
        Assert.True(start.IsEffectivelyEnabled);
        Assert.Equal(0, harness.Runner.CallCount);
        Assert.Equal(1, harness.FactoryCallCount);
        Assert.Same(task, viewModel.LastLoginTask);
        Assert.Empty(harness.Process.KillTreeArguments);
        Assert.Equal(1, harness.Process.DisposeCount);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_login_button_click_or_keyboard_stops_once_and_retry_is_explicit(bool useKeyboard)
    {
        using LoginViewHarness harness = new();
        Button login = Required<Button>(harness.Panel, "StartCopilotLogin");
        Button cancel = Required<Button>(harness.Panel, "CancelCopilotLogin");
        Button check = Required<Button>(harness.Panel, "CheckAuthenticationButton");
        Button start = Required<Button>(harness.View, "StartRunButton");
        Activate(harness.PanelWindow, login, useKeyboard);
        Task first = Assert.IsAssignableFrom<Task>(harness.ViewModel.LastLoginTask);
        LoginViewProcess cancelled = harness.Process;
        Assert.True(cancel.IsEffectivelyEnabled);

        Activate(harness.PanelWindow, cancel, useKeyboard, Key.Space);
        await first.WaitAsync(LoginTestWait, TestContext.Current.CancellationToken);
        Render();

        Assert.True(cancelled.WaitToken.IsCancellationRequested);
        Assert.Equal([false], cancelled.KillTreeArguments);
        Assert.Equal(1, cancelled.DisposeCount);
        Assert.False(harness.ViewModel.IsLoggingIn);
        Assert.True(login.IsEffectivelyEnabled);
        Assert.True(check.IsEffectivelyEnabled);
        Assert.False(cancel.IsEffectivelyEnabled);
        Assert.False(start.IsEffectivelyEnabled);
        Assert.Contains("取り消しました", harness.ViewModel.LoginStatusText, StringComparison.Ordinal);
        AssertLoginStatus(harness);
        Assert.Equal(0, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
        Assert.Equal(1, harness.FactoryCallCount);
        Assert.Same(first, harness.ViewModel.LastLoginTask);

        harness.Process = new LoginViewProcess();
        Render();
        Assert.Equal(0, harness.Process.StartCount);
        Assert.Equal(1, harness.FactoryCallCount);
        Activate(harness.PanelWindow, login, useKeyboard);
        Task retry = Assert.IsAssignableFrom<Task>(harness.ViewModel.LastLoginTask);
        Assert.NotSame(first, retry);
        Assert.Equal(2, harness.FactoryCallCount);
        Assert.Equal(1, harness.Process.StartCount);
        harness.Process.Complete(0);
        await retry.WaitAsync(LoginTestWait, TestContext.Current.CancellationToken);
        Render();

        AssertLoginStatus(harness);
        Assert.True(start.IsEffectivelyEnabled);
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
        Assert.Equal([false], cancelled.KillTreeArguments);
        Assert.Empty(harness.Process.KillTreeArguments);
    }

    [AvaloniaFact]
    public async Task Login_status_callout_never_displays_credentials_paths_or_exception_details()
    {
        using LoginViewHarness harness = new();
        harness.Process.StartFailureMessage =
            $"{LoginTokenCanary} device-code-CANARY {harness.Resolver.CliPath} {harness.ViewModel.OutputDirectory}";
        Button login = Required<Button>(harness.Panel, "StartCopilotLogin");

        Activate(harness.PanelWindow, login, useKeyboard: false);
        Task task = Assert.IsAssignableFrom<Task>(harness.ViewModel.LastLoginTask);
        await task.WaitAsync(LoginTestWait, TestContext.Current.CancellationToken);
        Render();

        AssertLoginStatus(harness);
        Assert.Contains("失敗", harness.ViewModel.LoginStatusText, StringComparison.Ordinal);
        Assert.Contains("再試行", harness.ViewModel.LoginStatusText, StringComparison.Ordinal);
        Assert.True(login.IsEffectivelyEnabled);
        Assert.False(Required<Button>(harness.Panel, "CancelCopilotLogin").IsEffectivelyEnabled);
        Assert.False(Required<Button>(harness.View, "StartRunButton").IsEffectivelyEnabled);
        Assert.True(harness.ViewModel.IsConfigured);
        Assert.Equal(ExecutionAuthenticationState.NotChecked, harness.ViewModel.AuthenticationState);
        Assert.Equal(1, harness.FactoryCallCount);
        Assert.Equal(1, harness.Process.DisposeCount);
        Assert.Equal(0, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
    }

    [AvaloniaFact]
    public async Task Login_controls_reflow_and_follow_tab_order_at_two_hundred_percent()
    {
        using LoginViewHarness harness = new();
        harness.PanelWindow.Width = 300;
        harness.PanelWindow.Height = 200;
        harness.PanelWindow.SetRenderScaling(2d);
        Render();
        StackPanel panel = Required<StackPanel>(harness.Panel, "CopilotLoginPanel");
        WrapPanel actions = Required<WrapPanel>(harness.Panel, "CopilotLoginActions");
        ScrollViewer scroll = Required<ScrollViewer>(harness.Panel, "ExecutionAuthenticationScroll");
        Button check = Required<Button>(harness.Panel, "CheckAuthenticationButton");
        Button login = Required<Button>(harness.Panel, "StartCopilotLogin");
        Button cancel = Required<Button>(harness.Panel, "CancelCopilotLogin");
        TextBlock instructions = Required<TextBlock>(harness.Panel, "CopilotLoginInstructions");
        TextBlock status = Required<TextBlock>(harness.Panel, "CopilotLoginStatus");

        Assert.Equal(2d, harness.PanelWindow.RenderScaling);
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1d);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        Assert.Equal(new Control[] { check, login, cancel }, actions.Children);
        Assert.Equal([100, 101, 102], new[] { check.TabIndex, login.TabIndex, cancel.TabIndex });
        Assert.True(cancel.Bounds.Y > check.Bounds.Y, "The narrow viewport must wrap the login actions.");
        Assert.Equal(TextWrapping.Wrap, instructions.TextWrapping);
        foreach (Control control in new Control[] { actions, check, login, cancel, instructions, status })
        {
            AssertFitsLoginPanel(control, panel);
        }

        Assert.True(check.Focus(NavigationMethod.Tab, KeyModifiers.None));
        Press(harness.PanelWindow, Key.Tab);
        Assert.Same(login, harness.PanelWindow.FocusManager?.GetFocusedElement());
        Press(harness.PanelWindow, Key.Tab, RawInputModifiers.Shift);
        Assert.Same(check, harness.PanelWindow.FocusManager?.GetFocusedElement());
        Assert.Equal(0, harness.FactoryCallCount);

        Activate(harness.PanelWindow, login, useKeyboard: true);
        Task task = Assert.IsAssignableFrom<Task>(harness.ViewModel.LastLoginTask);
        Render();
        Assert.True(cancel.IsEffectivelyEnabled);
        Assert.True(cancel.Focus(NavigationMethod.Tab, KeyModifiers.None));
        Assert.Same(cancel, harness.PanelWindow.FocusManager?.GetFocusedElement());
        Press(harness.PanelWindow, Key.Space);
        await task.WaitAsync(LoginTestWait, TestContext.Current.CancellationToken);
        Render();

        AssertLoginStatus(harness);
        AssertFitsLoginPanel(status, panel);
        status.BringIntoView();
        Render();
        Point origin = status.TranslatePoint(default, scroll)
            ?? throw new InvalidOperationException("The login status must be attached to its scroll viewer.");
        Assert.True(origin.Y >= -1d && origin.Y + status.Bounds.Height <= scroll.Viewport.Height + 1d);
        Assert.False(cancel.IsEffectivelyEnabled);
        Assert.Equal(0, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
    }

    [AvaloniaFact]
    public void Compact_execution_fits_950_by_450_with_fixed_actions_and_local_scrolling_only()
    {
        using LoginViewHarness harness = new();
        Assert.Equal(new Size(950, 450), harness.Window.ClientSize);
        Assert.IsType<Grid>(harness.View.Content);
        Assert.Equal(44d, Required<Grid>(harness.View, "ExecutionSettingsSummary").Bounds.Height);
        Assert.Equal(346d, Required<Grid>(harness.View, "ExecutionBody").Bounds.Height);
        Assert.Equal(44d, Required<Grid>(harness.View, "ExecutionActions").Bounds.Height);
        Assert.False(Required<TextBox>(harness.View, "CurrentRunOutputTextBox").IsEffectivelyVisible);
        Assert.False(harness.ViewModel.HasCurrentRun);
        Assert.Null(harness.View.FindControl<ScrollViewer>("ExecutionScrollViewer"));
        Assert.Null(harness.View.FindControl<ComboBox>("ModelComboBox"));
        Assert.Null(harness.View.FindControl<ComboBox>("ConcurrencyComboBox"));
        Assert.Null(harness.View.FindControl<TextBox>("OutputDirectoryTextBox"));
        Assert.Empty(harness.View.GetVisualDescendants().OfType<ComboBox>());
        Assert.DoesNotContain(harness.View.GetVisualDescendants().OfType<Control>(), control =>
            AutomationProperties.GetAutomationId(control) is "ExecutionModel" or "ExecutionConcurrency"
                or "ExecutionOutputDirectory" or "SettingsRuntimeIdentity"
            || (control.Name?.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ?? false));
        Assert.DoesNotContain(harness.View.GetVisualDescendants().OfType<TextBlock>(), text =>
            string.Equals(text.Text, harness.ViewModel.RuntimeIdentityText, StringComparison.Ordinal));

        TemplatedControl[] interactive = harness.View.GetVisualDescendants().OfType<TemplatedControl>()
            .Where(control => control is Button or CheckBox or TextBox or ListBox)
            .Where(control => control.IsEffectivelyVisible && !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(control)))
            .ToArray();
        Assert.NotEmpty(interactive);
        foreach (TemplatedControl control in interactive)
        {
            Assert.True(control.Bounds.Height >= 44d, control.Name);
            Assert.True(control.Bounds.Width >= 44d, control.Name);
            Assert.Equal(14d, control.FontSize);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)), control.Name);
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(control)?.ToString()), control.Name);
            AssertFullyInside(control, harness.View);
        }

        Assert.Null(harness.View.FindControl<ScrollViewer>("ExecutionAuthenticationScroll"));
        Assert.Null(harness.View.FindControl<Border>("ExecutionValidationSummary"));
        Assert.Null(harness.View.FindControl<Button>("CheckAuthenticationButton"));
        Assert.True(Required<ScrollViewer>(harness.Panel, "ExecutionAuthenticationScroll").Extent.Height
            <= Required<ScrollViewer>(harness.Panel, "ExecutionAuthenticationScroll").Viewport.Height + 1d);
        foreach (string name in new[] { "ExecutionProgressScroll" })
        {
            ScrollViewer scroll = Required<ScrollViewer>(harness.View, name);
            Assert.True(double.IsFinite(scroll.Bounds.Height));
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1d, name);
            // 950x450 is below the 1024x720 minimum: the progress block shares its column with the
            // speed-report list (FR-LP-01), so it may scroll locally here. The 1024x720 fit is asserted
            // in LivePreviewViewTests.
            if (name != "ExecutionProgressScroll")
            {
                Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1d, name);
            }
        }

        foreach (string name in new[] { "ChangeExecutionSettingsButton", "StartRunButton", "CancelRunButton" })
        {
            Assert.DoesNotContain(Required<Button>(harness.View, name).GetVisualAncestors(), ancestor => ancestor is ScrollViewer);
        }

        AssertNoAutomaticActivity(harness);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Effective_model_preserves_unavailable_preference_and_reports_reasoning_effort_separately(bool autoAvailable)
    {
        CopilotModelAvailability[] models = autoAvailable
            ? [U04TestSupport.Model("model-test"), U04TestSupport.Model("auto")]
            : [U04TestSupport.Model("model-test")];
        using LoginViewHarness harness = new(new ExecutionAuthenticationSnapshot(
            ExecutionAuthenticationState.Available, models, U04TestSupport.RuntimeIdentity()));
        harness.ViewModel.ApplySettings(new ApplicationSettings { PreferredModelId = "not-listed", MaxConcurrency = 3 });
        Render();
        TextBox model = Required<TextBox>(harness.View, "EffectiveModelTextBox");
        TextBlock effort = Required<TextBlock>(harness.View, "ReasoningEffortStatus");
        Assert.Equal("次回 未選択 並列3 · effort: 未選択 · Context Size: 未選択 · 希望: not-listed · 上限: 未選択", model.Text);
        Assert.Equal("effort\n未選択", effort.Text);
        Assert.Equal("次回並列\n3 件", Required<TextBlock>(harness.View, "ConcurrencySummary").Text);
        AssertNoAutomaticActivity(harness);

        Activate(harness.PanelWindow, Required<Button>(harness.Panel, "CheckAuthenticationButton"), useKeyboard: true);
        Render();
        Assert.Null(harness.ViewModel.SelectedModelId);
        Assert.Equal("not-listed", harness.ViewModel.PreferredModelId);
        Assert.Equal("次回 未選択 並列3 · effort: 未選択 · Context Size: 未選択 · 希望: not-listed · 上限: 未選択", model.Text);
        Assert.Equal("effort\n未選択", effort.Text);
        Assert.False(Required<Button>(harness.View, "StartRunButton").IsEffectivelyEnabled);

        harness.ViewModel.SelectedModelId = "model-test"; // An explicit edit, not an availability fallback.
        Render();
        Assert.Equal("次回 model-test 並列3 · effort: 未指定（model非対応またはauto） · Context Size: 64K (Default) · 希望: model-test · 上限: 64,000 tokens", model.Text);
        Assert.Equal("effort\n未指定", effort.Text);
        Assert.True(Required<Button>(harness.View, "StartRunButton").IsEffectivelyEnabled);
        Assert.True(model.IsReadOnly);
        Assert.Equal(TextWrapping.NoWrap, model.TextWrapping);
        AssertNoAutomaticActivity(harness, authenticationChecks: 1);
    }

    [AvaloniaFact]
    public async Task Common_settings_owns_editors_and_runtime_while_execution_keeps_effective_values_and_commands()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Responses", 1, 2, 1, new X02Header(1, "Report answer"));
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        using LoginViewHarness harness = new(new ExecutionAuthenticationSnapshot(
            ExecutionAuthenticationState.Available,
            [U04TestSupport.Model("model-test"), U04TestSupport.Model("model-other"), U04TestSupport.Model("auto")],
            U04TestSupport.RuntimeIdentity()));
        using MainWindowViewModel main = new(new WorkflowNavigator(), input,
            new QuantificationDesignViewModel(input.DefinitionDraft, input.AvailableColumnNames),
            harness.ViewModel, new ResultsOutputViewModel());
        main.NextCommand.Execute(null);
        main.NextCommand.Execute(null);
        harness.Window.DataContext = main;
        SettingsView settings = new(main.Settings);
        int fallbackRequests = 0;
        harness.View.CommonSettingsRequested += (_, _) => fallbackRequests++;
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.IsSettingsOpen))
            {
                harness.Window.Content = main.IsSettingsOpen ? settings : harness.View;
            }
        };
        main.Settings.SelectedCategory = SettingsCategory.ImportedPrompts;
        await harness.ViewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Render();
        Assert.True(input.HasLoadedWorkbook);
        Assert.Equal(WorkflowStep.Execution, main.CurrentStep);
        Assert.Equal(100m, main.DesignViewModel.AllocationTotal);
        Assert.True(harness.ViewModel.IsConfigured);
        Assert.True(harness.ViewModel.CanStart);
        string derivedOutput = harness.ViewModel.OutputDirectory;
        Button start = Required<Button>(harness.View, "StartRunButton");
        Button cancel = Required<Button>(harness.View, "CancelRunButton");
        Button change = Required<Button>(harness.View, "ChangeExecutionSettingsButton");

        Activate(harness.Window, change, useKeyboard: true);

        Assert.True(main.IsSettingsOpen);
        Assert.Equal(SettingsCategory.Common, main.Settings.SelectedCategory);
        Assert.Same(settings, harness.Window.Content);
        Assert.Null(TopLevel.GetTopLevel(harness.View));
        Assert.Equal(0, fallbackRequests);
        ComboBox model = ById<ComboBox>(settings, "ExecutionModel");
        ComboBox concurrency = ById<ComboBox>(settings, "ExecutionConcurrency");
        TextBox output = ById<TextBox>(settings, "ExecutionOutputDirectory");
        Assert.Same(harness.ViewModel.AvailableModelIds, model.ItemsSource);
        Assert.Same(harness.ViewModel.ConcurrencyOptions, concurrency.ItemsSource);
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(output, TextBox.TextProperty));
        Assert.False(output.IsReadOnly);
        model.SetCurrentValue(ComboBox.SelectedItemProperty, "model-other");
        concurrency.SetCurrentValue(ComboBox.SelectedItemProperty, 3);
        string explicitOutput = Path.Combine(Path.GetTempPath(), "T20-explicit-output");
        output.SetCurrentValue(TextBox.TextProperty, explicitOutput);
        Render();
        Assert.Equal("model-other", harness.ViewModel.SelectedModelId);
        Assert.Equal("model-other", harness.ViewModel.PreferredModelId);
        Assert.Equal(3, harness.ViewModel.MaxConcurrency);
        Assert.Equal(explicitOutput, harness.ViewModel.OutputDirectoryOverride);
        Assert.Equal(explicitOutput, ById<TextBox>(settings, "SettingsEffectiveOutputDirectory").Text);

        ById<TabControl>(settings, "SettingsCommonTabs").SelectedIndex = 2;
        Render();
        TextBox runtime = ById<TextBox>(settings, "SettingsRuntimeIdentity");
        Assert.True(runtime.IsReadOnly);
        Assert.Equal(harness.ViewModel.RuntimeIdentityText, runtime.Text);
        Assert.Equal(harness.ViewModel.AuthenticationStatusText, ById<TextBox>(settings, "SettingsAuthenticationStatus").Text);
        Activate(harness.Window, ById<Button>(settings, "SettingsRequestClose"), useKeyboard: true);

        Assert.False(main.IsSettingsOpen);
        Assert.Same(harness.View, harness.Window.Content);
        Assert.Equal("次回 model-other 並列3 · effort: 未指定（model非対応またはauto） · Context Size: 64K (Default) · 希望: model-other · 上限: 64,000 tokens", Required<TextBox>(harness.View, "EffectiveModelTextBox").Text);
        Assert.Equal("次回並列\n3 件", Required<TextBlock>(harness.View, "ConcurrencySummary").Text);
        Assert.Equal(explicitOutput, Required<TextBox>(harness.View, "EffectiveOutputDirectoryTextBox").Text);
        Assert.Contains("明示指定", Required<TextBlock>(harness.View, "OutputDirectorySource").Text, StringComparison.Ordinal);
        Assert.Same(start, Required<Button>(harness.View, "StartRunButton"));
        Assert.Same(cancel, Required<Button>(harness.View, "CancelRunButton"));
        Assert.Same(harness.ViewModel.StartCommand, start.Command);
        Assert.Same(harness.ViewModel.CancelCommand, cancel.Command);
        Assert.True(start.IsEffectivelyEnabled);
        Assert.DoesNotContain(harness.View.GetVisualDescendants().OfType<TextBlock>(), text =>
            string.Equals(text.Text, runtime.Text, StringComparison.Ordinal));

        Activate(harness.Window, change, useKeyboard: false);
        ById<TabControl>(settings, "SettingsCommonTabs").SelectedIndex = 0;
        Render();
        ById<TextBox>(settings, "ExecutionOutputDirectory").SetCurrentValue(TextBox.TextProperty, string.Empty);
        Render();
        Activate(harness.Window, ById<Button>(settings, "SettingsRequestClose"), useKeyboard: false);
        Assert.Null(harness.ViewModel.OutputDirectoryOverride);
        Assert.Equal(derivedOutput, Required<TextBox>(harness.View, "EffectiveOutputDirectoryTextBox").Text);
        Assert.Contains("入力隣接", Required<TextBlock>(harness.View, "OutputDirectorySource").Text, StringComparison.Ordinal);
        Assert.Null(main.Settings.LastLoadTask);
        Assert.Null(main.Settings.LastSaveTask);
        Assert.Equal(0, fallbackRequests);
        AssertNoAutomaticActivity(harness, authenticationChecks: 1);
    }

    [AvaloniaFact]
    public void Long_paths_are_single_line_read_only_for_outputs_and_editable_only_for_resume()
    {
        using LoginViewHarness harness = new();
        string directory = Path.Combine(Path.GetTempPath(), "T20-" + new string('長', 200), new string('値', 100));
        harness.ViewModel.OutputDirectoryOverride = directory;
        Render();
        TextBox output = Required<TextBox>(harness.View, "EffectiveOutputDirectoryTextBox");
        AssertReadOnlySingleLine(output, directory, harness.Window);
        Assert.Equal(directory, harness.ViewModel.OutputDirectoryOverride);
        AssertFullyInside(output, harness.View);

        CheckBox resume = Required<CheckBox>(harness.Panel, "ResumeModeCheckBox");
        TextBox partial = Required<TextBox>(harness.Panel, "ResumePartialPathTextBox");
        Assert.False(partial.IsEffectivelyVisible);
        resume.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        Render();
        Assert.True(harness.ViewModel.IsResumeMode);
        Assert.True(partial.IsEffectivelyVisible);
        Assert.False(partial.IsReadOnly);
        Assert.Equal(TextWrapping.NoWrap, partial.TextWrapping);
        string resumePath = Path.Combine(directory, "synthetic.partial.xlsx");
        partial.SetCurrentValue(TextBox.TextProperty, resumePath);
        Render();
        Assert.Equal(resumePath, harness.ViewModel.ResumePartialPath);
        Assert.Equal(directory, output.Text);
        Assert.Equal(directory, harness.ViewModel.OutputDirectoryOverride);
        Assert.Equal(harness.ViewModel.OutputModeText, Required<TextBlock>(harness.Panel, "OutputModeSummary").Text);
        AssertFullyInside(partial, harness.Panel);

        resume.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
        Render();
        Assert.False(harness.ViewModel.IsResumeMode);
        Assert.False(partial.IsEffectivelyVisible);
        Assert.Equal(resumePath, harness.ViewModel.ResumePartialPath);
        Assert.Equal(directory, output.Text);
        AssertNoAutomaticActivity(harness);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_progress_keeps_current_run_separate_from_next_settings_through_completion(bool cancelRun)
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 3);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(definition, metadata, cancelAfterFirst: cancelRun);
        TaskCompletionSource<RunSummary> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken runToken = default;
        string runOutput = Path.Combine(Path.GetTempPath(), "T20-current-output-" + new string('長', 150));
        string nextOutput = Path.Combine(Path.GetTempPath(), "T20-next-output-" + new string('次', 150));
        string directory = Path.Combine(Path.GetTempPath(), "T20-reserved-" + new string('長', 150));
        string finalPath = Path.Combine(directory, "reserved.xlsx");
        string partialPath = Path.Combine(directory, "reserved.partial.xlsx");
        RecordingRunBoundary runner = new((_, report, token) =>
        {
            runToken = token;
            report?.Invoke(new EvaluationProgress(5, 3, 1, EvaluationProgressStatus.Running,
                DurableEvaluationStage.EvaluatingRows, referenceCompleted: 1, referenceTotal: 1,
                rowCompleted: 1, rowTotal: 2, finalPath: finalPath, partialPath: partialPath));
            return completion.Task;
        });
        using LoginViewHarness harness = new(new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.Available,
            [U04TestSupport.Model("model-a"), U04TestSupport.Model("model-b"), U04TestSupport.Model("auto")],
            U04TestSupport.RuntimeIdentity()), runner);
        harness.ViewModel.Configure(definition, metadata, Path.Combine(Path.GetTempPath(), "T20-progress-input.xlsx"));
        harness.ViewModel.ApplySettings(new ApplicationSettings
        {
            PreferredModelId = "model-a", MaxConcurrency = 1, OutputDirectoryOverride = runOutput,
        });
        await harness.ViewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Render();
        Button start = Required<Button>(harness.View, "StartRunButton");
        Assert.False(harness.ViewModel.HasCurrentRun);
        Assert.False(Required<TextBox>(harness.View, "CurrentRunOutputTextBox").IsEffectivelyVisible);
        Assert.Equal("次回 model-a 並列1 · effort: 未指定（model非対応またはauto） · Context Size: 64K (Default) · 希望: model-a · 上限: 64,000 tokens", Required<TextBox>(harness.View, "EffectiveModelTextBox").Text);
        TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        void ObserveRunFinished(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(ExecutionViewModel.IsRunning) && !harness.ViewModel.IsRunning)
            {
                finished.TrySetResult();
            }
        }

        harness.ViewModel.PropertyChanged += ObserveRunFinished;
        try
        {
            Activate(harness.Window, start, useKeyboard: true);
            Assert.Equal(1, runner.CallCount);
            Assert.True(harness.ViewModel.IsRunning);
            Assert.False(start.IsEffectivelyEnabled);
            QuantificationRunRequest request = Assert.IsType<QuantificationRunRequest>(runner.LastRequest);
            QuantificationDefinition runDefinition = request.DraftDefinition;
            Assert.True(harness.ViewModel.HasCurrentRun);
            Assert.Equal("実行中 model-a 並列1 · effort: 未指定（model非対応またはauto） · context: default · 上限: 64,000 tokens / 次回 model-a 並列1 · effort: 未指定（model非対応またはauto） · Context Size: 64K (Default) · 希望: model-a · 上限: 64,000 tokens",
                Required<TextBox>(harness.View, "EffectiveModelTextBox").Text);
            AssertValidationStatus(harness.View, harness.ViewModel, "実行中");

            harness.ViewModel.ApplySettings(new ApplicationSettings
            {
                PreferredModelId = "model-b", MaxConcurrency = 3, OutputDirectoryOverride = nextOutput,
            });
            Render();

            Assert.Equal("実行中 model-a 並列1 · effort: 未指定（model非対応またはauto） · context: default · 上限: 64,000 tokens / 次回 model-b 並列3 · effort: 未指定（model非対応またはauto） · Context Size: 64K (Default) · 希望: model-b · 上限: 64,000 tokens",
                Required<TextBox>(harness.View, "EffectiveModelTextBox").Text);
            Assert.Equal("次回並列\n3 件", Required<TextBlock>(harness.View, "ConcurrencySummary").Text);
            Assert.Equal("model-a", harness.ViewModel.CurrentRunModelId);
            Assert.Equal(1, harness.ViewModel.CurrentRunMaxConcurrency);
            Assert.Equal("今回run 新規出力先: " + runOutput, harness.ViewModel.CurrentRunOutputSummary);
            Assert.Equal(nextOutput, Required<TextBox>(harness.View, "EffectiveOutputDirectoryTextBox").Text);
            Assert.Contains("次回", Required<TextBlock>(harness.View, "OutputDirectorySource").Text, StringComparison.Ordinal);
            TextBox currentOutput = Required<TextBox>(harness.View, "CurrentRunOutputTextBox");
            AssertReadOnlySingleLine(currentOutput, "今回run 新規出力先: " + runOutput, harness.Window);
            AssertFullyInside(currentOutput, harness.View);
            Assert.Equal(44d, Required<Grid>(harness.View, "ExecutionSettingsSummary").Bounds.Height);
            Assert.Equal(346d, Required<Grid>(harness.View, "ExecutionBody").Bounds.Height);
            Assert.Equal(44d, Required<Grid>(harness.View, "ExecutionActions").Bounds.Height);
            Assert.True(Required<Button>(harness.View, "ChangeExecutionSettingsButton").IsEffectivelyEnabled);
            Assert.False(start.IsEffectivelyEnabled);
            Assert.Same(request, runner.LastRequest);
            Assert.Same(runDefinition, request.DraftDefinition);
            Assert.Equal("model-a", request.ModelId);
            Assert.Equal(1, request.MaxConcurrency);
            Assert.Equal(runOutput, request.OutputDirectory);
            Assert.Null(request.ResumePartialPath);
            AssertValidationStatus(harness.View, harness.ViewModel, "実行中");
            Assert.Contains("送信済み件数ではありません", Required<TextBlock>(harness.View, "PlanSummaryLabel").Text, StringComparison.Ordinal);
            Assert.Equal(harness.ViewModel.PlanSummary, Required<TextBlock>(harness.View, "PlanSummaryText").Text);
            Assert.Equal("学生行を評価中", Required<TextBlock>(harness.View, "ExecutionStage").Text);
            Assert.Equal("処理単位: " + harness.ViewModel.ProgressText, Required<TextBlock>(harness.View, "OperationProgressSummary").Text);
            Assert.Equal("実測: 参照 1 / 1 · 行 1 / 2", Required<TextBlock>(harness.View, "DurableProgressSummary").Text);
            Assert.Equal(5d, Required<ProgressBar>(harness.View, "RunProgressBar").Maximum);
            Assert.Equal(3d, Required<ProgressBar>(harness.View, "RunProgressBar").Value);
            Assert.Equal(harness.ViewModel.RunStatusText, Required<TextBlock>(harness.View, "RunStatusSummary").Text);
            Assert.Contains("作成済みとは限りません", Required<TextBlock>(harness.View, "ReservedPathsLabel").Text, StringComparison.Ordinal);
            TextBox final = Required<TextBox>(harness.View, "ReservedFinalPathTextBox");
            TextBox checkpoint = Required<TextBox>(harness.View, "PartialPathTextBox");
            AssertReadOnlySingleLine(final, finalPath, harness.Window);
            AssertReadOnlySingleLine(checkpoint, partialPath, harness.Window);
            AssertFullyInside(final, harness.View);
            AssertFullyInside(checkpoint, harness.View);
            Assert.Equal(finalPath, harness.ViewModel.ReservedFinalPath);
            Assert.Equal(partialPath, harness.ViewModel.PartialPath);
            Assert.NotEqual(runOutput, Path.GetDirectoryName(harness.ViewModel.ReservedFinalPath));
            Assert.NotEqual(nextOutput, Path.GetDirectoryName(harness.ViewModel.ReservedFinalPath));
            Assert.Null(harness.ViewModel.LastRunContext); // A reservation is not a successful output.

            Button cancel = Required<Button>(harness.View, "CancelRunButton");
            Assert.True(cancel.IsEffectivelyEnabled);
            if (cancelRun)
            {
                Activate(harness.Window, cancel, useKeyboard: false);
                Assert.True(runToken.IsCancellationRequested);
                Assert.True(harness.ViewModel.IsCancelling);
                Assert.False(cancel.IsEffectivelyEnabled);
                AssertValidationStatus(harness.View, harness.ViewModel, "取消処理中");
                Assert.Equal("実行中 model-a 並列1 · effort: 未指定（model非対応またはauto） · context: default · 上限: 64,000 tokens / 次回 model-b 並列3 · effort: 未指定（model非対応またはauto） · Context Size: 64K (Default) · 希望: model-b · 上限: 64,000 tokens",
                    Required<TextBox>(harness.View, "EffectiveModelTextBox").Text);
                Assert.Equal("今回run 新規出力先: " + runOutput, currentOutput.Text);
            }
            else
            {
                Assert.False(runToken.IsCancellationRequested);
            }
        }
        finally
        {
            completion.TrySetResult(summary);
            try
            {
                if (runner.CallCount > 0)
                {
                    await finished.Task.WaitAsync(LoginTestWait, TestContext.Current.CancellationToken);
                }
            }
            finally
            {
                harness.ViewModel.PropertyChanged -= ObserveRunFinished;
            }
        }

        Render();
        Assert.False(harness.ViewModel.IsRunning);
        Assert.Same(summary, harness.ViewModel.LastRunContext?.Summary);
        Assert.Contains(cancelRun ? "保存済み checkpoint はありません" : "検証済みfinal", Required<TextBlock>(harness.View, "RunStatusSummary").Text, StringComparison.Ordinal);
        Assert.True(harness.ViewModel.HasCurrentRun);
        Assert.Equal("model-a", harness.ViewModel.CurrentRunModelId);
        Assert.Equal(1, harness.ViewModel.CurrentRunMaxConcurrency);
        Assert.Equal("前回run model-a 並列1 · effort: 未指定（model非対応またはauto） · context: default · 上限: 64,000 tokens / 次回 model-b 並列3 · effort: 未指定（model非対応またはauto） · Context Size: 64K (Default) · 希望: model-b · 上限: 64,000 tokens",
            Required<TextBox>(harness.View, "EffectiveModelTextBox").Text);
        Assert.Equal("前回run 新規出力先: " + runOutput, Required<TextBox>(harness.View, "CurrentRunOutputTextBox").Text);
        Assert.Equal(nextOutput, Required<TextBox>(harness.View, "EffectiveOutputDirectoryTextBox").Text);
        Assert.Equal(finalPath, Required<TextBox>(harness.View, "ReservedFinalPathTextBox").Text);
        Assert.Equal(partialPath, Required<TextBox>(harness.View, "PartialPathTextBox").Text);
        AssertValidationStatus(harness.View, harness.ViewModel, "run を開始できます");
        Assert.Equal(1, runner.CallCount);
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.Equal(0, harness.FactoryCallCount);
    }

    [AvaloniaFact]
    public async Task Technical_errors_keep_selection_virtualize_long_lists_and_hide_when_resolved()
    {
        using LoginViewHarness harness = new();
        harness.ViewModel.MaxConcurrency = 17;
        Render();
        ListBox list = Required<ListBox>(harness.Panel, "TechnicalErrorsList");
        TextBox detail = Required<TextBox>(harness.Panel, "SelectedTechnicalErrorDetail");
        Border pane = Required<Border>(harness.Panel, "ExecutionValidationSummary");
        ExecutionTechnicalError selected = Assert.Single(harness.ViewModel.TechnicalErrors, error => error.Code == "CONCURRENCY_OUT_OF_RANGE");
        list.SelectedItem = selected;
        Render();
        Assert.Equal(ErrorDetail(selected), detail.Text);
        harness.ViewModel.MaxConcurrency = 0;
        Render();
        ExecutionTechnicalError replacement = Assert.IsType<ExecutionTechnicalError>(list.SelectedItem);
        Assert.NotSame(selected, replacement);
        Assert.Equal(selected.Code, replacement.Code);
        Assert.Equal(selected.Field, replacement.Field);
        Assert.Equal(ErrorDetail(replacement), detail.Text);

        // Stress only presentation with synthetic items; never change VM validation
        // or the U04 shared fixtures to manufacture many live validation errors.
        ExecutionTechnicalError[] many = Enumerable.Range(1, 200).Select(index => new ExecutionTechnicalError(
            $"SYNTHETIC_{index}", $"対象 {index} " + new string('項', 80),
            string.Join('\n', Enumerable.Repeat("全説明を省略せず表示する合成エラー。", 50)) + "末尾確認")).ToArray();
        list.SetCurrentValue(ItemsControl.ItemsSourceProperty, many);
        list.SelectedItem = many[^1];
        list.ScrollIntoView(many[^1]);
        Render();
        Assert.Same(many[^1], list.SelectedItem);
        Assert.Equal(ErrorDetail(many[^1]), detail.Text);
        Assert.True(detail.IsReadOnly);
        Assert.Equal(TextWrapping.Wrap, detail.TextWrapping);
        Assert.InRange(list.Bounds.Height, 44d, 108d);
        Assert.True(list.GetVisualDescendants().OfType<ListBoxItem>().Count() < many.Length);
        ScrollViewer local = Assert.Single(detail.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.True(local.Extent.Height > local.Viewport.Height);
        local.ScrollToEnd();
        Render();
        Assert.True(local.Offset.Y > 0d);
        AssertFullyInside(detail, pane);
        AssertFullyInside(list, pane);
        AssertFullyInside(Required<Button>(harness.View, "StartRunButton"), harness.View);
        Assert.False(Required<Button>(harness.View, "StartRunButton").IsEffectivelyEnabled);
        AssertNoAutomaticActivity(harness);

        list.SetCurrentValue(ItemsControl.ItemsSourceProperty, harness.ViewModel.TechnicalErrors);
        harness.ViewModel.MaxConcurrency = 4;
        await harness.ViewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Render();
        Assert.Empty(harness.ViewModel.TechnicalErrors);
        Assert.False(pane.IsEffectivelyVisible);
        Assert.False(list.IsEffectivelyVisible);
        Assert.False(detail.IsEffectivelyVisible);
        Assert.Null(list.SelectedItem);
        Assert.Equal(string.Empty, detail.Text);
        Assert.True(Required<Button>(harness.View, "StartRunButton").IsEffectivelyEnabled);
        AssertNoAutomaticActivity(harness, authenticationChecks: 1);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Definition_weight_error_selection_retains_the_second_target_after_revalidation_and_first_fix(bool duplicateIds)
    {
        using LoginViewHarness harness = new();
        QuantificationDefinition definition = DefinitionWithTwoWeightErrors(duplicateIds);
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        string inputPath = Path.Combine(Path.GetTempPath(), "T20-error-targets.xlsx");
        harness.ViewModel.Configure(definition, metadata, inputPath);
        await harness.ViewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Render();
        ListBox list = Required<ListBox>(harness.Panel, "TechnicalErrorsList");
        TextBox detail = Required<TextBox>(harness.Panel, "SelectedTechnicalErrorDetail");
        ExecutionTechnicalError[] weightErrors = harness.ViewModel.TechnicalErrors
            .Where(error => error.Code == "WEIGHT_MUST_BE_POSITIVE").ToArray();
        Assert.Equal(2, weightErrors.Length);
        ExecutionTechnicalError selected = weightErrors[1];
        list.SelectedItem = selected;
        Render();
        Assert.Equal(ErrorDetail(selected), detail.Text);
        Assert.Contains(selected.NodeId!, detail.Text, StringComparison.Ordinal);
        Assert.Contains(selected.Path!, detail.Text, StringComparison.Ordinal);
        Assert.Contains("Weight", detail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(TechnicalPromptCanary, detail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(TechnicalBodyCanary, detail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(TechnicalNameCanary, detail.Text, StringComparison.Ordinal);

        harness.ViewModel.MaxConcurrency = 2;
        Render();
        ExecutionTechnicalError revalidated = Assert.IsType<ExecutionTechnicalError>(list.SelectedItem);
        Assert.NotSame(selected, revalidated);
        Assert.Equal((selected.Code, selected.NodeId, selected.Path, selected.Field),
            (revalidated.Code, revalidated.NodeId, revalidated.Path, revalidated.Field));
        Assert.Equal(ErrorDetail(revalidated), detail.Text);

        QuestionDefinition question = definition.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        evaluator = evaluator with { Criteria = evaluator.Criteria.SetItem(0, evaluator.Criteria[0] with { Weight = 1m }) };
        definition = definition with { Questions = [question with { Evaluators = [evaluator] }] };
        harness.ViewModel.Configure(definition, metadata, inputPath);
        Render();
        ExecutionTechnicalError remaining = Assert.Single(harness.ViewModel.TechnicalErrors,
            error => error.Code == "WEIGHT_MUST_BE_POSITIVE");
        Assert.Same(remaining, list.SelectedItem);
        Assert.Equal(selected.NodeId, remaining.NodeId);
        Assert.Equal(selected.Path, remaining.Path);
        Assert.Equal(ErrorDetail(remaining), detail.Text);
        Assert.False(Required<Button>(harness.View, "StartRunButton").IsEffectivelyEnabled);

        evaluator = evaluator with
        {
            Criteria = evaluator.Criteria.SetItem(1, evaluator.Criteria[1] with { Id = "C2", Weight = 1m }),
        };
        harness.ViewModel.Configure(definition with { Questions = [question with { Evaluators = [evaluator] }] }, metadata, inputPath);
        Render();
        Assert.Empty(harness.ViewModel.TechnicalErrors);
        Assert.Null(list.SelectedItem);
        Assert.Equal(string.Empty, detail.Text);
        Assert.False(Required<Border>(harness.Panel, "ExecutionValidationSummary").IsEffectivelyVisible);
        Assert.True(Required<Button>(harness.View, "StartRunButton").IsEffectivelyEnabled);
        AssertNoAutomaticActivity(harness, authenticationChecks: 1);
    }

    [AvaloniaFact]
    public async Task Reattachment_and_owner_replacement_keep_one_observer_and_do_not_restart_owned_login()
    {
        using LoginViewHarness harness = new();
        int settingsRequests = 0;
        harness.View.CommonSettingsRequested += (_, _) => settingsRequests++;
        for (int repeat = 0; repeat < 3; repeat++)
        {
            Assert.Equal(1, ViewObserverCount(harness.ViewModel, harness.View));
            harness.Window.Content = null;
            Render();
            Assert.Equal(0, ViewObserverCount(harness.ViewModel, harness.View));
            harness.Window.Content = harness.View;
            Render();
            Assert.Equal(1, ViewObserverCount(harness.ViewModel, harness.View));
        }

        Activate(harness.Window, Required<Button>(harness.View, "ChangeExecutionSettingsButton"), useKeyboard: true);
        Assert.Equal(1, settingsRequests);
        AssertNoAutomaticActivity(harness);
        Activate(harness.PanelWindow, Required<Button>(harness.Panel, "StartCopilotLogin"), useKeyboard: true);
        Task login = Assert.IsAssignableFrom<Task>(harness.ViewModel.LastLoginTask);
        harness.Window.Content = null;
        Render();
        Assert.Equal(0, ViewObserverCount(harness.ViewModel, harness.View));
        Assert.True(harness.ViewModel.IsLoggingIn);
        Assert.Empty(harness.Process.KillTreeArguments);
        harness.Window.Content = harness.View;
        Render();
        Assert.Equal(1, ViewObserverCount(harness.ViewModel, harness.View));
        Assert.Same(login, harness.ViewModel.LastLoginTask);
        Assert.Equal(1, harness.FactoryCallCount);
        Assert.Equal(1, harness.Process.StartCount);
        Assert.Equal(0, harness.Process.DisposeCount);
        harness.Process.Complete(0);
        await login.WaitAsync(LoginTestWait, TestContext.Current.CancellationToken);
        Render();
        Assert.Equal(1, harness.Process.DisposeCount);
        Assert.Empty(harness.Process.KillTreeArguments);

        RecordingAuthenticationBoundary authentication = new(new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired));
        RecordingRunBoundary runner = new((_, _, _) => throw new InvalidOperationException("must not run"));
        using ExecutionViewModel next = new(authentication, runner);
        harness.View.DataContext = next;
        harness.Panel.DataContext = next;
        next.ApplySettings(new ApplicationSettings { PreferredModelId = "new-owner" });
        harness.ViewModel.SelectedModelId = "old-owner";
        Render();
        Assert.Equal(0, ViewObserverCount(harness.ViewModel, harness.View));
        Assert.Equal(1, ViewObserverCount(next, harness.View));
        Assert.Equal("次回 未選択 並列8 · effort: 未選択 · Context Size: 未選択 · 希望: new-owner · 上限: 未選択", Required<TextBox>(harness.View, "EffectiveModelTextBox").Text);
        Assert.Same(next.CheckAuthenticationCommand, Required<Button>(harness.Panel, "CheckAuthenticationButton").Command);
        Assert.Same(next.StartCommand, Required<Button>(harness.View, "StartRunButton").Command);
        Assert.Same(next.LoginCommand, Required<Button>(harness.Panel, "StartCopilotLogin").Command);
        Activate(harness.Window, Required<Button>(harness.View, "ChangeExecutionSettingsButton"), useKeyboard: false);
        Assert.Equal(2, settingsRequests);
        Assert.Equal(0, authentication.CallCount);
        Assert.Equal(0, runner.CallCount);
        Assert.Null(next.LastLoginTask);
        Assert.Equal(1, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Runner.CallCount);
        Assert.Equal(1, harness.FactoryCallCount);

        harness.Window.Content = null;
        Render();
        Assert.Equal(0, ViewObserverCount(next, harness.View));
    }

    [AvaloniaTheory]
    [InlineData(ExecutionAuthenticationState.AuthRequired)]
    [InlineData(ExecutionAuthenticationState.CliUnavailable)]
    [InlineData(ExecutionAuthenticationState.RuntimeFailed)]
    public async Task Authentication_status_wraps_to_its_measured_height_without_runtime_diagnostics(ExecutionAuthenticationState state)
    {
        using LoginViewHarness harness = new(new ExecutionAuthenticationSnapshot(state));
        await harness.ViewModel.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Render();
        TextBlock authentication = Required<TextBlock>(harness.Panel, "CopilotAuthenticationStatus");
        Assert.Equal(harness.ViewModel.AuthenticationStatusText, authentication.Text);
        AssertFullyWrapped(authentication);
        AssertFitsLoginPanel(authentication, Required<StackPanel>(harness.Panel, "CopilotLoginPanel"));
        AssertLoginStatus(harness);
        Assert.False(Required<Button>(harness.View, "StartRunButton").IsEffectivelyEnabled);
        Assert.DoesNotContain(harness.View.GetVisualDescendants().OfType<TextBlock>(), text =>
            string.Equals(text.Text, harness.ViewModel.RuntimeIdentityText, StringComparison.Ordinal));
        AssertNoAutomaticActivity(harness, authenticationChecks: 1);
    }

    private const string LoginTokenCanary = "PRIVATE-A03-TOKEN-CANARY";
    private static readonly TimeSpan LoginTestWait = TimeSpan.FromSeconds(10);

    private const string TechnicalPromptCanary = "PRIVATE-T20-PROMPT-CANARY";
    private const string TechnicalBodyCanary = "PRIVATE-T20-ANSWER-CANARY";
    private const string TechnicalNameCanary = "PRIVATE-T20-DISPLAY-NAME-CANARY";

    private static QuantificationDefinition DefinitionWithTwoWeightErrors(bool duplicateIds)
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        QuestionDefinition question = definition.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        CriterionDefinition criterion = evaluator.Criteria[0] with
        {
            DisplayName = TechnicalNameCanary,
            Description = TechnicalBodyCanary,
            Weight = 0m,
        };
        return definition with
        {
            Name = TechnicalNameCanary,
            Questions = [question with
            {
                DisplayName = TechnicalNameCanary,
                QuestionText = TechnicalBodyCanary,
                Evaluators = [evaluator with
                {
                    DisplayName = TechnicalNameCanary,
                    CustomPromptTemplate = TechnicalPromptCanary + " {回答} {評価項目}",
                    Criteria = [criterion, criterion with { Id = duplicateIds ? "C1" : "C2" }],
                }],
            }],
        };
    }

    private sealed class DeferredAuthenticationBoundary(Task<ExecutionAuthenticationSnapshot> response)
        : IExecutionAuthenticationBoundary
    {
        public Task<ExecutionAuthenticationSnapshot> CheckAsync(CancellationToken cancellationToken) =>
            response.WaitAsync(cancellationToken);
    }

    private static T Required<T>(Control root, string name)
        where T : Control => Assert.IsType<T>(root.FindControl<T>(name));

    private static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static string ErrorDetail(ExecutionTechnicalError error) =>
        $"{error.Code}{Environment.NewLine}{error.TargetText}{Environment.NewLine}{error.Message}";

    private static void AssertValidationStatus(ExecutionView view, ExecutionViewModel viewModel, string expectedPart)
    {
        TextBlock status = Required<TextBlock>(view, "ExecutionValidationStatus");
        Assert.Equal(viewModel.ValidationSummary, status.Text);
        Assert.Contains(expectedPart, status.Text, StringComparison.Ordinal);
        Assert.Equal(viewModel.CanStart, status.Text!.Contains("開始できます", StringComparison.Ordinal));
        Assert.Equal(viewModel.CanStart, Required<Button>(view, "StartRunButton").IsEffectivelyEnabled);
        AssertFullyWrapped(status);
    }

    private static int ViewObserverCount(ExecutionViewModel owner, ExecutionView view)
    {
        FieldInfo field = typeof(UiObservableObject).GetField(nameof(INotifyPropertyChanged.PropertyChanged),
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The observable's event backing field must exist.");
        return ((Delegate?)field.GetValue(owner))?.GetInvocationList().Count(handler => ReferenceEquals(handler.Target, view)) ?? 0;
    }

    private static void AssertNoAutomaticActivity(LoginViewHarness harness, int authenticationChecks = 0)
    {
        Assert.Equal(authenticationChecks, harness.Authentication.CallCount);
        Assert.Equal(0, harness.Resolver.CallCount);
        Assert.Equal(0, harness.FactoryCallCount);
        Assert.Equal(0, harness.Process.StartCount);
        Assert.Equal(0, harness.Runner.CallCount);
        Assert.Null(harness.ViewModel.LastLoginTask);
        Assert.False(harness.ViewModel.IsLoggingIn);
        Assert.False(harness.ViewModel.IsRunning);
    }

    private static void AssertReadOnlySingleLine(TextBox field, string expected, Window window)
    {
        Assert.Equal(expected, field.Text);
        Assert.True(field.IsReadOnly);
        Assert.False(field.AcceptsReturn);
        Assert.Equal(TextWrapping.NoWrap, field.TextWrapping);
        Assert.Equal(44d, field.Bounds.Height);
        Assert.Equal(14d, field.FontSize);
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(field, TextBox.TextProperty));
        Assert.True(field.Focus(NavigationMethod.Tab));
        field.SelectionStart = 0;
        field.SelectionEnd = expected.Length;
        Render();
        Assert.Equal(expected.Length, field.SelectionEnd - field.SelectionStart);
        window.KeyTextInput("読取専用欄は変更しない");
        Render();
        Assert.Equal(expected, field.Text);
        ScrollViewer local = Assert.Single(field.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.True(local.Extent.Width > local.Viewport.Width);
        local.Offset = new Vector(local.Extent.Width, 0d);
        Render();
        Assert.True(local.Offset.X > 0d);
        Assert.Equal(expected, field.Text);
    }

    private static void AssertFullyInside(Control control, Control container)
    {
        Point origin = control.TranslatePoint(default, container)
            ?? throw new InvalidOperationException("The control must be attached to its container.");
        Assert.True(origin.X >= -1d && origin.Y >= -1d, control.Name);
        Assert.True(origin.X + control.Bounds.Width <= container.Bounds.Width + 1d, control.Name);
        Assert.True(origin.Y + control.Bounds.Height <= container.Bounds.Height + 1d, control.Name);
    }

    private static void AssertFullyWrapped(TextBlock text)
    {
        Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
        Assert.Equal(TextTrimming.None, text.TextTrimming);
        Assert.True(text.Bounds.Width > 0d);
        TextBlock measure = new()
        {
            Text = text.Text,
            FontFamily = text.FontFamily,
            FontSize = text.FontSize,
            FontWeight = text.FontWeight,
            FontStyle = text.FontStyle,
            FontStretch = text.FontStretch,
            LineHeight = text.LineHeight,
            LetterSpacing = text.LetterSpacing,
            TextWrapping = TextWrapping.Wrap,
        };
        measure.Measure(new Size(text.Bounds.Width, double.PositiveInfinity));
        Assert.True(text.Bounds.Height >= measure.DesiredSize.Height - 1d,
            $"{text.Name} must allocate the measured wrapped height, not just keep the source string.");
    }

    private static void AssertLoginStatus(LoginViewHarness harness)
    {
        TextBlock status = Required<TextBlock>(harness.Panel, "CopilotLoginStatus");
        Assert.Equal("CopilotLoginStatus", AutomationProperties.GetAutomationId(status));
        Assert.Equal(harness.ViewModel.LoginStatusText, status.Text);
        Assert.Equal(status.Text, AutomationProperties.GetName(status));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(status)));
        Assert.Equal(TextWrapping.Wrap, status.TextWrapping);
        Assert.True(status.IsEffectivelyVisible);
        Assert.False(status.Focusable);
        AssertFullyWrapped(status);
        string visible = string.Join("\n", status.Text,
            AutomationProperties.GetName(status), AutomationProperties.GetHelpText(status));
        Assert.DoesNotContain(LoginTokenCanary, visible, StringComparison.Ordinal);
        Assert.DoesNotContain("device-code-CANARY", visible, StringComparison.Ordinal);
        Assert.DoesNotContain(harness.Resolver.CliPath, visible, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(harness.ViewModel.OutputDirectory, visible, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(nameof(InvalidOperationException), visible, StringComparison.Ordinal);
    }

    private static void AssertFitsLoginPanel(Control control, StackPanel panel)
    {
        Point origin = control.TranslatePoint(default, panel)
            ?? throw new InvalidOperationException("The login control must be attached to its panel.");
        Assert.True(control.Bounds.Width > 0d);
        Assert.True(origin.X >= -1d && origin.X + control.Bounds.Width <= panel.Bounds.Width + 1d,
            $"{control.Name} must fit within the login panel without horizontal clipping.");
        if (control is TextBlock text)
        {
            AssertFullyWrapped(text);
        }
    }

    private static void Activate(Window window, Button button, bool useKeyboard, Key key = Key.Enter)
    {
        Assert.True(button.IsEffectivelyEnabled);
        button.BringIntoView();
        Render();
        if (useKeyboard)
        {
            Assert.True(button.Focus(NavigationMethod.Tab, KeyModifiers.None));
            Press(window, key);
            return;
        }

        Point point = button.TranslatePoint(new Point(button.Bounds.Width / 2d, button.Bounds.Height / 2d), window)
            ?? throw new InvalidOperationException("The clicked button must be attached to its window.");
        Assert.InRange(point.X, 0d, window.ClientSize.Width);
        Assert.InRange(point.Y, 0d, window.ClientSize.Height);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        Render();
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physicalKey = key switch
        {
            Key.Tab => PhysicalKey.Tab,
            Key.Enter => PhysicalKey.Enter,
            Key.Space => PhysicalKey.Space,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported test key."),
        };
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
        Render();
    }

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    // Reuse U04's configuration/auth/run fakes; A01/A02's login fakes are private to their test classes.
    private sealed class LoginViewHarness : IDisposable
    {
        public LoginViewHarness(ExecutionAuthenticationSnapshot? snapshot = null, RecordingRunBoundary? runner = null)
        {
            Authentication = new RecordingAuthenticationBoundary(snapshot ?? new ExecutionAuthenticationSnapshot(
                ExecutionAuthenticationState.Available,
                [U04TestSupport.Model("model-test"), U04TestSupport.Model("auto")], U04TestSupport.RuntimeIdentity()));
            Runner = runner ?? new RecordingRunBoundary((_, _, _) => throw new InvalidOperationException("must not run"));
            BundledCopilotLoginService service = new(Resolver, _ =>
            {
                FactoryCallCount++;
                return Process;
            });
            ViewModel = new ExecutionViewModel(Authentication, Runner, service);
            QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
            ViewModel.Configure(definition, U01TestSupport.ValidateMapping(definition).Metadata,
                Path.Combine(Path.GetTempPath(), "PRIVATE-A03-INPUT-CANARY.xlsx"));
            View = new ExecutionView(ViewModel);
            Panel = new ExecutionPreparationPanel { DataContext = ViewModel };
            Window = new Window { Width = 950, Height = 450, Content = View };
            PanelWindow = new Window { Width = 950, Height = 450, Content = Panel };
            Window.Show();
            PanelWindow.Show();
            Render();
        }

        public RecordingAuthenticationBoundary Authentication { get; }
        public RecordingRunBoundary Runner { get; }
        public LoginViewResolver Resolver { get; } = new();
        public LoginViewProcess Process { get; set; } = new();
        public int FactoryCallCount { get; private set; }
        public ExecutionViewModel ViewModel { get; }
        public ExecutionView View { get; }
        public ExecutionPreparationPanel Panel { get; }
        public Window Window { get; }
        public Window PanelWindow { get; }

        public void Dispose()
        {
            ViewModel.Dispose();
            Window.Close();
            PanelWindow.Close();
        }
    }

    private sealed class LoginViewResolver : ICopilotCliPathResolver
    {
        public string CliPath { get; } = Path.Combine(Path.GetTempPath(), "PRIVATE-A03-CLI-CANARY", "copilot.exe");
        public int CallCount { get; private set; }

        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return ValueTask.FromResult<string?>(CliPath);
        }
    }

    private sealed class LoginViewProcess : ICopilotLoginProcess
    {
        private readonly TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? StartFailureMessage { get; set; }
        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool HasExited => exit.Task.IsCompletedSuccessfully;
        public int ExitCode { get; private set; }
        public CancellationToken WaitToken { get; private set; }
        public List<bool> KillTreeArguments { get; } = [];

        public bool Start()
        {
            StartCount++;
            if (StartFailureMessage is not null)
            {
                throw new InvalidOperationException(StartFailureMessage);
            }

            return true;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitToken = cancellationToken;
            // Deliberately ignore cancellation: the service must still clean up its owned fake.
            return exit.Task;
        }

        public void Kill(bool entireProcessTree)
        {
            KillTreeArguments.Add(entireProcessTree);
            Complete(-1);
        }

        public bool WaitForExit(int milliseconds) => HasExited;

        public void Complete(int code)
        {
            ExitCode = code;
            exit.TrySetResult();
        }

        public void Dispose() => DisposeCount++;
    }
}

internal static class U04TestSupport
{
    internal static QuantificationDefinition Definition(int firstDataRow, int lastDataRow) =>
        U01TestSupport.Definition(
            firstDataRow,
            lastDataRow,
            U01TestSupport.Question(
                "Q1",
                "A",
                ["B"],
                true,
                U01TestSupport.Evaluator("E1", "C1")));

    internal static CopilotRuntimeIdentity RuntimeIdentity() =>
        new(
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), "copilot-u04-test.exe")),
            "1.0.82",
            new string('B', 64),
            "1.0.11");

    internal static CopilotModelAvailability Model(string id) =>
        string.Equals(id, "auto", StringComparison.Ordinal)
            ? AutoModel()
            : new(id, maximumPromptTokens: 64_000, maximumContextWindowTokens: 128_000);

    /// 実 CLI 1.0.79 は auto を max_prompt_tokens=null / max_context_window_tokens=0 で返す。
    internal static CopilotModelAvailability AutoModel() => new("auto", null, 0);

    internal static ExecutionRunContext Context(RunSummary summary, string? inputPath = null) =>
        new(
            summary,
            inputPath ?? Path.Combine(Path.GetTempPath(), "PRIVATE-U04-CONTEXT-CANARY.xlsx"),
            "model-test",
            RuntimeIdentity());

    internal static ExecutionViewModel ConfiguredExecutionViewModel(
        QuantificationDefinition definition,
        WorkbookMetadata metadata,
        IQuantificationRunBoundary runBoundary)
    {
        ExecutionViewModel viewModel = new(
            new RecordingAuthenticationBoundary(
                new ExecutionAuthenticationSnapshot(
                    ExecutionAuthenticationState.Available,
                    [Model("model-test"), Model("auto")],
                    RuntimeIdentity())),
            runBoundary);
        viewModel.Configure(
            definition,
            metadata,
            Path.Combine(Path.GetTempPath(), "PRIVATE-U04-RUN-CANARY.xlsx"));
        return viewModel;
    }

    internal static async Task<RunSummary> CreateSummaryAsync(
        QuantificationDefinition definition,
        WorkbookMetadata metadata,
        string primary = "synthetic answer",
        bool cancelAfterFirst = false)
    {
        using CancellationTokenSource cancellation = new();
        ScriptedRowSource rows = new((request, _) => Task.FromResult(new EvaluationRowData(
            request.SourceRowNumber,
            new Dictionary<string, string?>
            {
                ["A"] = primary,
                ["B"] = "synthetic support",
            })));
        int invocation = 0;
        ScriptedRunner runner = new((payload, _, _) =>
        {
            EvaluationRunnerResult result = EvaluationRunnerResult.Succeeded(
                U01TestSupport.ValidResult(payload, _ => 5m));
            if (cancelAfterFirst && Interlocked.Increment(ref invocation) == 1)
            {
                cancellation.Cancel();
            }

            return Task.FromResult(result);
        });
        return await new QuantificationOrchestrator(
            rows,
            runner,
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot())).RunAsync(
                new QuantificationRunRequest
                {
                    DraftDefinition = definition,
                    WorkbookMetadata = metadata,
                    InputPath = Path.Combine(Path.GetTempPath(), "PRIVATE-U04-SUMMARY-CANARY.xlsx"),
                    ModelId = "model-test",
                    MaximumPromptTokens = 64_000,
                    MaximumContextWindowTokens = 128_000,
                    MaxConcurrency = 1,
                },
                cancellationToken: cancellation.Token);
    }
}

internal sealed class RecordingAuthenticationBoundary(
    ExecutionAuthenticationSnapshot snapshot) : IExecutionAuthenticationBoundary
{
    public int CallCount { get; private set; }

    public Task<ExecutionAuthenticationSnapshot> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        return Task.FromResult(snapshot);
    }
}

internal sealed class RecordingRunBoundary(
    Func<QuantificationRunRequest, Action<EvaluationProgress>?, CancellationToken, Task<RunSummary>> handler)
    : IQuantificationRunBoundary
{
    public int CallCount { get; private set; }

    public QuantificationRunRequest? LastRequest { get; private set; }

    public Task<RunSummary> RunAsync(
        QuantificationRunRequest request,
        Action<EvaluationProgress>? progress,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        return handler(request, progress, cancellationToken);
    }
}

internal sealed class ControlledRunBoundary(RunSummary summary) : IQuantificationRunBoundary
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource cancellationObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => started.Task;

    public Task CancellationObserved => cancellationObserved.Task;

    public int CallCount { get; private set; }

    public void Release() => release.TrySetResult();

    public async Task<RunSummary> RunAsync(
        QuantificationRunRequest request,
        Action<EvaluationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        CallCount++;
        progress?.Invoke(new EvaluationProgress(
            summary.PlannedEvaluationCount,
            0,
            1,
            EvaluationProgressStatus.Running));
        started.TrySetResult();
        using CancellationTokenRegistration registration = cancellationToken.Register(
            () => cancellationObserved.TrySetResult());
        await release.Task;
        progress?.Invoke(new EvaluationProgress(
            summary.PlannedEvaluationCount,
            summary.CompletedEvaluationCount,
            0,
            EvaluationProgressStatus.Cancelled));
        return summary;
    }
}
