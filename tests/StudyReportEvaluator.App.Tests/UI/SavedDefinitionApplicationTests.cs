using System.Collections.Immutable;
using System.Reflection;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using StudyReportEvaluator.Core.Validation;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-041 (AC-042)
public sealed class SavedDefinitionApplicationTests
{
    private const string PrivateCanary = "PRIVATE-SAVED-CONTENT-CANARY";
    private const string SavedPrompt = "保存した Prompt\r\n{設問}\n{回答}\n{補助情報}\n{評価項目}\n{最小点}..{最大点}\n{{literal}}";
    private const string SavedSpecialPrompt = "保存した固有評価\r\n{回答}\n{補助情報}\n{{literal}}";
    private static readonly CanonicalDefinitionSerializer Serializer = new();

    [Fact]
    public async Task Apply_API_accepts_a_domain_definition_and_null_throws_exactly_for_saved()
    {
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        InputState before = new(viewModel);
        MethodInfo method = Assert.IsAssignableFrom<MethodInfo>(
            typeof(InputViewModel).GetMethod(nameof(InputViewModel.ApplySavedDefinitionAsync)));
        Assert.Equal(typeof(Task<bool>), method.ReturnType);
        Assert.Equal(
            [typeof(QuantificationDefinition), typeof(CancellationToken)],
            method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(method.GetParameters()[1].HasDefaultValue);

        ArgumentNullException exception = await Assert.ThrowsAsync<ArgumentNullException>(
            () => viewModel.ApplySavedDefinitionAsync(null!, TestContext.Current.CancellationToken));

        Assert.Equal("saved", exception.ParamName);
        Assert.Empty(loader.Calls);
        Assert.Null(viewModel.SavedDefinitionApplicationError);
        before.AssertUnchanged(viewModel);
        Assert.False(viewModel.IsBusy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_definition_requires_an_already_loaded_workbook_without_loading_or_fallback(
        bool hasUnloadedPath)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(2);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        if (hasUnloadedPath)
        {
            viewModel.FilePath = workbook.Path;
        }

        InputState before = new(viewModel);

    Assert.False(await viewModel.ApplySavedDefinitionAsync(CreateSavedDefinition(), TestContext.Current.CancellationToken));

        Assert.Empty(loader.Calls);
        AssertApplicationError(viewModel, "INPUT_WORKBOOK_REQUIRED", workbook.Path);
        before.AssertUnchanged(viewModel);
        Assert.False(viewModel.IsBusy);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    public async Task Application_reloads_the_saved_header_and_preserves_ids_order_prompts_points_and_canonical_content(
        int initialHeaderRow,
        int savedHeaderRow)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile((uint)savedHeaderRow);
        byte[] sourceBytes = File.ReadAllBytes(workbook.Path);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader) { HeaderRow = initialHeaderRow };
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        WorkbookMetadata? previousMetadata = viewModel.Metadata;
        InputSnapshot? previousSnapshot = viewModel.Snapshot;
        QuantificationDefinition saved = CreateSavedDefinition(savedHeaderRow) with { SourceSheet = "responses" };
        byte[] canonical = Serializer.SerializeToUtf8Bytes(saved);
        string hash = Serializer.ComputeSha256(saved);

        Assert.True(await viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));

        Assert.Equal([(uint)initialHeaderRow, (uint)savedHeaderRow], loader.Calls.Select(call => call.HeaderRow));
        Assert.All(loader.Calls, call => Assert.Equal(workbook.Path, call.Path));
        Assert.Equal(TestContext.Current.CancellationToken, loader.Calls[1].Token);
        Assert.NotSame(previousMetadata, viewModel.Metadata);
        Assert.NotSame(previousSnapshot, viewModel.Snapshot);
        Assert.Equal(previousSnapshot, viewModel.Snapshot);
        Assert.Equal((uint)savedHeaderRow, viewModel.Metadata!.HeaderRowNumber);
        Assert.Equal(saved.SourceSheet, viewModel.SelectedSheet);
        Assert.Equal("Responses", viewModel.SelectedWorksheetChoice!.Name);
        Assert.Equal(saved.HeaderRow, viewModel.HeaderRow);
        Assert.Equal(saved.FirstDataRow, viewModel.FirstDataRow);
        Assert.Equal(saved.LastDataRow, viewModel.LastDataRow);
        Assert.Equal(canonical, Serializer.SerializeToUtf8Bytes(viewModel.DefinitionDraft));
        Assert.Equal(hash, Serializer.ComputeSha256(viewModel.DefinitionDraft));
        AssertDeepCopy(saved, viewModel.DefinitionDraft);
        AssertQuestionsMatch(saved, viewModel);
        Assert.False(viewModel.IsUsingSuggestedMapping);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanContinue);
        Assert.Empty(viewModel.ValidationErrors);
        Assert.Null(viewModel.SavedDefinitionApplicationError);
        Assert.Equal("Report answer 1", Assert.Single(viewModel.AvailableColumns, column => column.ColumnName == "F").HeaderText);
        Assert.Equal("Report answer 1", Assert.Single(viewModel.MappingSuggestions, column => column.ColumnName == "F").HeaderText);
        Assert.NotEqual("Report answer 1", viewModel.Questions[0].QuestionText);
        Assert.Equal(3, viewModel.Questions.Count);
        Assert.Equal(5, viewModel.MappingSuggestions.Count(candidate => candidate.IsPrimaryCandidate));

        InputQuestionMappingViewModel[] questionItems = viewModel.Questions.ToArray();
        QuantificationDefinition firstAppliedDraft = viewModel.DefinitionDraft;
        Assert.True(await viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));

        Assert.Equal(questionItems, viewModel.Questions);
        Assert.NotSame(firstAppliedDraft, viewModel.DefinitionDraft);
        Assert.Equal(canonical, Serializer.SerializeToUtf8Bytes(viewModel.DefinitionDraft));
        Assert.Equal(hash, Serializer.ComputeSha256(saved));
        Assert.Equal(sourceBytes, File.ReadAllBytes(workbook.Path));
        Assert.Equal(3, loader.Calls.Count);
        Assert.All(loader.Calls, call => Assert.Equal(workbook.Path, call.Path));
    }

    [Fact]
    public async Task Application_selects_the_saved_existing_sheet_instead_of_the_suggested_sheet()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        byte[] sourceBytes = File.ReadAllBytes(workbook.Path);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        Assert.Equal("Original", viewModel.SelectedSheet);
        Assert.Equal(12, viewModel.AvailableColumns.Count);
        QuantificationDefinition saved = CreateSavedDefinition(headerRow: 1, sourceSheet: "Final");

        Assert.True(await viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));

        Assert.Equal("Final", viewModel.SelectedSheet);
        Assert.Equal("Final", viewModel.SelectedWorksheetChoice!.Name);
        Assert.Equal(30, viewModel.AvailableColumns.Count);
        Assert.Equal("集計レポート回答 6", Assert.Single(viewModel.AvailableColumns, column => column.ColumnName == "F").HeaderText);
        Assert.Equal("集計レポート回答 6", Assert.Single(viewModel.MappingSuggestions, column => column.ColumnName == "F").HeaderText);
        Assert.Equal(9, viewModel.MappingSuggestions.Count);
        Assert.Equal(Serializer.ComputeSha256(saved), Serializer.ComputeSha256(viewModel.DefinitionDraft));
        AssertQuestionsMatch(saved, viewModel);
        Assert.True(viewModel.CanContinue);
        Assert.False(viewModel.IsUsingSuggestedMapping);
        Assert.Equal(sourceBytes, File.ReadAllBytes(workbook.Path));
        Assert.Equal(2, loader.Calls.Count);
        Assert.All(loader.Calls, call => Assert.Equal(workbook.Path, call.Path));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Primary_column_edits_after_application_use_the_new_header_without_mutating_saved_content(
        int savedHeaderRow)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile((uint)savedHeaderRow);
        byte[] sourceBytes = File.ReadAllBytes(workbook.Path);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader) { HeaderRow = savedHeaderRow == 1 ? 2 : 1 };
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, Assert.Single(viewModel.AvailableColumns, column => column.ColumnName == "I").HeaderText);
        QuantificationDefinition saved = CreateSavedDefinition(savedHeaderRow);
        string savedHash = Serializer.ComputeSha256(saved);
        Assert.True(await viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));

        viewModel.Questions[0].PrimarySourceColumn = "I";

        Assert.Equal("Report answer 2", viewModel.Questions[0].QuestionText);
        Assert.Equal("Report answer 2", viewModel.DefinitionDraft.Questions[0].QuestionText);
        Assert.Equal(saved.Questions[0].Id, viewModel.Questions[0].Id);
        Assert.Equal(saved.Questions[0].Points, viewModel.Questions[0].Weight);
        Assert.Equal(SavedPrompt, viewModel.DefinitionDraft.Questions[0].Evaluators[0].CustomPromptTemplate);
        Assert.Equal(savedHash, Serializer.ComputeSha256(saved));
        Assert.Equal(sourceBytes, File.ReadAllBytes(workbook.Path));
        Assert.Equal(2, loader.Calls.Count);
    }

    [Fact]
    public async Task Loading_new_input_and_refreshing_headers_do_not_automatically_reapply_saved_content()
    {
        using X02TemporaryWorkbook first = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(2);
        using X02TemporaryWorkbook second = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(2);
        byte[] firstBytes = File.ReadAllBytes(first.Path);
        byte[] secondBytes = File.ReadAllBytes(second.Path);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        QuantificationDefinition saved = CreateSavedDefinition();
        string savedHash = Serializer.ComputeSha256(saved);

        Assert.Empty(loader.Calls);
        await viewModel.SetFilePathAsync(first.Path, TestContext.Current.CancellationToken);
        Assert.NotEqual(saved.Id, viewModel.DefinitionDraft.Id);
        Assert.DoesNotContain(viewModel.Questions, question => question.Id == saved.Questions[0].Id);
        Assert.Null(viewModel.SavedDefinitionApplicationError);

        Assert.True(await viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));
        await viewModel.SetFilePathAsync(second.Path, TestContext.Current.CancellationToken);
        string newInputHash = Serializer.ComputeSha256(viewModel.DefinitionDraft);
        await viewModel.RefreshHeaderAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(savedHash, newInputHash);
        Assert.Equal(newInputHash, Serializer.ComputeSha256(viewModel.DefinitionDraft));
        Assert.DoesNotContain(viewModel.Questions, question => question.Id == saved.Questions[0].Id);
        Assert.Equal([first.Path, first.Path, second.Path, second.Path], loader.Calls.Select(call => call.Path));
        Assert.Equal(savedHash, Serializer.ComputeSha256(saved));
        Assert.Equal(firstBytes, File.ReadAllBytes(first.Path));
        Assert.Equal(secondBytes, File.ReadAllBytes(second.Path));
    }

    [Theory]
    [InlineData("sheet", "SOURCE_SHEET_NOT_FOUND")]
    [InlineData("primary", "SOURCE_COLUMN_NOT_FOUND")]
    [InlineData("supporting", "SOURCE_COLUMN_NOT_FOUND")]
    [InlineData("disabled-primary", "SOURCE_COLUMN_NOT_FOUND")]
    [InlineData("special-primary", "SOURCE_COLUMN_NOT_FOUND")]
    [InlineData("special-supporting", "SOURCE_COLUMN_NOT_FOUND")]
    [InlineData("first-row", "DATA_ROW_OUTSIDE_WORKSHEET")]
    [InlineData("last-row", "DATA_ROW_OUTSIDE_WORKSHEET")]
    [InlineData("header-row", "HEADER_ROW_OUTSIDE_WORKSHEET")]
    [InlineData("stale-metadata", "HEADER_METADATA_MISMATCH")]
    public async Task Mapping_failures_preserve_all_loaded_state_without_sheet_column_or_row_fallback(
        string failure,
        string expectedCode)
    {
        using X02TemporaryWorkbook workbook = failure == "header-row"
            ? X02SyntheticWorkbookFactory.CreateSingleSheet("Responses", 1, 1, 12, new X02Header(6, "Report answer"))
            : X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(2);
        byte[] sourceBytes = File.ReadAllBytes(workbook.Path);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition saved = CreateSavedDefinition();
        saved = failure switch
        {
            "sheet" => saved with { SourceSheet = PrivateCanary },
            "primary" => WithFirstQuestion(saved, question => question with { PrimarySourceColumn = "M" }),
            "supporting" => WithFirstQuestion(saved, question => question with { SupportingSourceColumns = ["M"] }),
            "disabled-primary" => saved with
            {
                Questions = saved.Questions.SetItem(2, saved.Questions[2] with { PrimarySourceColumn = "M" }),
            },
            "special-primary" => WithFirstSpecial(saved, special => special with { PrimarySourceColumn = "M" }),
            "special-supporting" => WithFirstSpecial(saved, special => special with { SupportingSourceColumns = ["M"] }),
            "first-row" => saved with { FirstDataRow = 13, LastDataRow = 14 },
            "last-row" => saved with { LastDataRow = 13 },
            _ => saved,
        };
        if (failure == "stale-metadata")
        {
            loader.NextLoad = (path, _, _) => Task.FromResult(ReadResult(path, 1));
        }

        Assert.True(new QuantificationDefinitionValidator().Validate(saved).IsValid);
        string savedHash = Serializer.ComputeSha256(saved);
        InputState before = new(viewModel);
        bool couldContinue = viewModel.CanContinue;

        Assert.False(await viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));

        AssertApplicationError(viewModel, expectedCode, workbook.Path);
        before.AssertUnchanged(viewModel);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(couldContinue, viewModel.CanContinue);
        Assert.Equal(savedHash, Serializer.ComputeSha256(saved));
        Assert.Equal(sourceBytes, File.ReadAllBytes(workbook.Path));
        Assert.Equal(2, loader.Calls.Count);
        Assert.All(loader.Calls, call => Assert.Equal(workbook.Path, call.Path));
        Assert.Equal(2U, loader.Calls[1].HeaderRow);
    }

    [Theory]
    [InlineData("null-name", "REQUIRED")]
    [InlineData("header-zero", "ROW_OUT_OF_RANGE")]
    [InlineData("header-negative", "ROW_OUT_OF_RANGE")]
    [InlineData("header-three", "QUESTION_TEXT_ROW_INVALID")]
    [InlineData("data-before-header", "DATA_ROW_NOT_AFTER_HEADER")]
    [InlineData("reversed-rows", "DATA_ROW_RANGE_REVERSED")]
    [InlineData("duplicate-id", "DUPLICATE_ID")]
    [InlineData("empty-questions", "QUESTION_REQUIRED")]
    [InlineData("default-questions", "QUESTION_REQUIRED")]
    [InlineData("null-question", "NULL_NODE")]
    [InlineData("null-evaluator", "NULL_NODE")]
    [InlineData("null-criterion", "NULL_NODE")]
    [InlineData("null-special", "NULL_NODE")]
    [InlineData("default-enabled-evaluators", "ENABLED_EVALUATOR_REQUIRED")]
    [InlineData("evaluator-type", "INVALID_EVALUATOR_TYPE")]
    [InlineData("criterion-weight", "WEIGHT_MUST_BE_POSITIVE")]
    [InlineData("evaluator-range", "SCORE_RANGE_INVALID")]
    [InlineData("custom-prompt", "UNKNOWN_PLACEHOLDER")]
    [InlineData("special-prompt", "UNKNOWN_PLACEHOLDER")]
    [InlineData("question-points", "QUESTION_POINTS_OUT_OF_RANGE")]
    [InlineData("allocation-total", "ALLOCATION_TOTAL_INVALID")]
    [InlineData("missing-special-items", "SPECIAL_ITEMS_REQUIRED")]
    public async Task Invalid_domain_prompt_and_allocation_are_rejected_before_loading_and_without_mutation(
        string failure,
        string expectedCode)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(1);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition saved = CreateSavedDefinition();
        saved = failure switch
        {
            "null-name" => saved with { Name = null! },
            "header-zero" => saved with { HeaderRow = 0 },
            "header-negative" => saved with { HeaderRow = -1 },
            "header-three" => saved with { HeaderRow = 3 },
            "data-before-header" => saved with { FirstDataRow = 2 },
            "reversed-rows" => saved with { LastDataRow = 3 },
            "duplicate-id" => WithFirstQuestion(saved, question => question with { Id = saved.Id }),
            "empty-questions" => saved with { Questions = [] },
            "default-questions" => saved with { Questions = default },
            "null-question" => saved with { Questions = saved.Questions.Add(null!) },
            "null-evaluator" => WithFirstQuestion(saved, question => question with { Evaluators = question.Evaluators.Add(null!) }),
            "null-criterion" => WithFirstEvaluator(saved, evaluator => evaluator with { Criteria = evaluator.Criteria.Add(null!) }),
            "null-special" => WithFirstQuestion(saved, question => question with { SpecialEvaluations = question.SpecialEvaluations.Add(null!) }),
            "default-enabled-evaluators" => WithFirstQuestion(saved, question => question with { Evaluators = default }),
            "evaluator-type" => WithFirstEvaluator(saved, evaluator => evaluator with { Type = (EvaluatorType)99 }),
            "criterion-weight" => WithFirstEvaluator(saved, evaluator => evaluator with
            {
                Criteria = evaluator.Criteria.SetItem(0, evaluator.Criteria[0] with { Weight = 0m }),
            }),
            "evaluator-range" => WithFirstEvaluator(saved, evaluator => evaluator with { Range = new ScoreRange(1m, 1m) }),
            "custom-prompt" => WithFirstEvaluator(saved, evaluator => evaluator with { CustomPromptTemplate = "{" + PrivateCanary + "}" }),
            "special-prompt" => WithFirstSpecial(saved, special => special with { PromptTemplate = "{" + PrivateCanary + "}" }),
            "question-points" => WithFirstQuestion(saved, question => question with { Points = -1m }),
            "allocation-total" => saved with { BasePoints = 61.125m },
            "missing-special-items" => WithFirstQuestion(saved, question => question with { SpecialEvaluations = [] }),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        InputState before = new(viewModel);

        Assert.False(await viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));

        AssertApplicationError(viewModel, expectedCode, workbook.Path);
        before.AssertUnchanged(viewModel);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanContinue);
        Assert.Single(loader.Calls);
    }

    [Fact]
    public async Task Validator_accepted_default_optional_collections_are_deep_copied_without_canonical_changes()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(2);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition saved = WithFirstQuestion(CreateSavedDefinition(), question => question with
        {
            SupportingSourceColumns = default,
            SpecialEvaluations = [.. question.SpecialEvaluations.Select(special => special with { SupportingSourceColumns = default })],
            Evaluators = question.Evaluators.Add(question.Evaluators[0] with
            {
                Id = "evaluator-disabled-default",
                Enabled = false,
                Criteria = default,
            }),
        });
        saved = saved with
        {
            Questions = saved.Questions.SetItem(2, saved.Questions[2] with
            {
                SupportingSourceColumns = default,
                Evaluators = default,
                SpecialEvaluations = default,
            }),
        };
        Assert.True(new QuantificationDefinitionValidator().Validate(saved).IsValid);
        byte[] canonical = Serializer.SerializeToUtf8Bytes(saved);

        Assert.True(await viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));

        Assert.Equal(canonical, Serializer.SerializeToUtf8Bytes(viewModel.DefinitionDraft));
        Assert.Equal(canonical, Serializer.SerializeToUtf8Bytes(saved));
        Assert.True(saved.Questions[2].Evaluators.IsDefault);
        Assert.False(viewModel.DefinitionDraft.Questions[2].Evaluators.IsDefault);
        Assert.NotSame(saved.Questions[2], viewModel.DefinitionDraft.Questions[2]);
        Assert.NotSame(saved.Questions[0].Evaluators[2], viewModel.DefinitionDraft.Questions[0].Evaluators[2]);
        AssertQuestionsMatch(saved, viewModel);
        Assert.True(viewModel.CanContinue);
        Assert.Null(viewModel.SavedDefinitionApplicationError);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("data")]
    [InlineData("technical")]
    [InlineData("unexpected")]
    public async Task Loader_failures_preserve_loaded_state_and_redact_exception_details(string failure)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(1);
        byte[] sourceBytes = File.ReadAllBytes(workbook.Path);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        string detail = PrivateCanary + " " + workbook.Path;
        Exception exception = failure switch
        {
            "io" => new IOException(detail),
            "access" => new UnauthorizedAccessException(detail),
            "data" => new InvalidDataException(detail),
            "technical" => new InputWorkbookLoadException(detail),
            "unexpected" => new InvalidOperationException(detail),
            _ => throw new ArgumentOutOfRangeException(nameof(failure)),
        };
        loader.NextLoad = (_, _, _) => Task.FromException<InputWorkbookLoadResult>(exception);
        InputState before = new(viewModel);

        Assert.False(await viewModel.ApplySavedDefinitionAsync(CreateSavedDefinition(), TestContext.Current.CancellationToken));

        AssertApplicationError(viewModel, "SAVED_DEFINITION_LOAD_FAILED", workbook.Path);
        before.AssertUnchanged(viewModel);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanContinue);
        Assert.Equal(sourceBytes, File.ReadAllBytes(workbook.Path));
        Assert.Equal(2, loader.Calls.Count);
        Assert.All(loader.Calls, call => Assert.Equal(workbook.Path, call.Path));

        Assert.True(await viewModel.ApplySavedDefinitionAsync(CreateSavedDefinition(), TestContext.Current.CancellationToken));
        Assert.Null(viewModel.SavedDefinitionApplicationError);
    }

    [Fact]
    public async Task Cancellation_before_loading_preserves_state_and_does_not_call_the_loader()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(1);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputState before = new(viewModel);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.False(await viewModel.ApplySavedDefinitionAsync(CreateSavedDefinition(), cancellation.Token));

        AssertApplicationError(viewModel, "SAVED_DEFINITION_CANCELLED", workbook.Path);
        before.AssertUnchanged(viewModel);
        Assert.Single(loader.Calls);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanContinue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_during_loading_preserves_state_even_if_the_loader_ignores_the_token(
        bool loaderAcknowledgesCancellation)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(1);
        byte[] sourceBytes = File.ReadAllBytes(workbook.Path);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputState before = new(viewModel);
        InputWorkbookLoadResult prepared = ReadResult(workbook.Path, 2);
        TaskCompletionSource<InputWorkbookLoadResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.NextLoad = (_, _, _) => release.Task;
        using CancellationTokenSource cancellation = new();

        Task<bool> application = viewModel.ApplySavedDefinitionAsync(CreateSavedDefinition(), cancellation.Token);
        Assert.True(viewModel.IsBusy);
        before.AssertUnchanged(viewModel);
        cancellation.Cancel();
        if (loaderAcknowledgesCancellation)
        {
            release.SetCanceled(cancellation.Token);
        }
        else
        {
            release.SetResult(prepared);
        }

        Assert.False(await application);

        AssertApplicationError(viewModel, "SAVED_DEFINITION_CANCELLED", workbook.Path);
        before.AssertUnchanged(viewModel);
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanContinue);
        Assert.Equal(cancellation.Token, loader.Calls[1].Token);
        Assert.Equal(sourceBytes, File.ReadAllBytes(workbook.Path));
    }

    [Theory]
    [InlineData("file-path")]
    [InlineData("file-round-trip")]
    [InlineData("sheet")]
    [InlineData("header")]
    [InlineData("first-row")]
    [InlineData("last-row")]
    [InlineData("question-text")]
    [InlineData("primary-column")]
    [InlineData("points")]
    [InlineData("add-question")]
    [InlineData("reorder-question")]
    [InlineData("reload")]
    public async Task Input_edits_during_await_are_not_overwritten_by_the_prepared_saved_definition(string edit)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        byte[] sourceBytes = File.ReadAllBytes(workbook.Path);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition saved = CreateSavedDefinition(sourceSheet: "Original");
        string savedHash = Serializer.ComputeSha256(saved);
        InputState before = new(viewModel);
        InputWorkbookLoadResult prepared = ReadResult(workbook.Path, 2);
        TaskCompletionSource<InputWorkbookLoadResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.NextLoad = (_, _, _) => release.Task;
        Task<bool> application = viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken);
        Assert.True(viewModel.IsBusy);
        before.AssertUnchanged(viewModel);

        switch (edit)
        {
            case "file-path":
                viewModel.FilePath = workbook.Path + ".replacement.xlsx";
                break;
            case "file-round-trip":
                viewModel.FilePath = workbook.Path + ".replacement.xlsx";
                viewModel.FilePath = workbook.Path;
                break;
            case "sheet":
                viewModel.SelectedSheet = "Final";
                break;
            case "header":
                viewModel.HeaderRow = 2;
                break;
            case "first-row":
                viewModel.FirstDataRow = 4;
                break;
            case "last-row":
                viewModel.LastDataRow = 100;
                break;
            case "question-text":
                viewModel.Questions[0].QuestionText = "Edited while applying";
                break;
            case "primary-column":
                viewModel.Questions[0].PrimarySourceColumn = "G";
                break;
            case "points":
                viewModel.Questions[0].Weight = 9.99m;
                break;
            case "add-question":
                viewModel.AddQuestion();
                break;
            case "reorder-question":
                viewModel.MoveQuestionDown(viewModel.Questions[0].Id);
                break;
            case "reload":
                await viewModel.LoadAsync(TestContext.Current.CancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit));
        }

        InputState edited = new(viewModel);
        release.SetResult(prepared);

        Assert.False(await application);

        edited.AssertUnchanged(viewModel);
        Assert.False(viewModel.IsBusy);
        Assert.Null(viewModel.SavedDefinitionApplicationError);
        Assert.Equal(savedHash, Serializer.ComputeSha256(saved));
        Assert.Equal(sourceBytes, File.ReadAllBytes(workbook.Path));
        Assert.Equal(edit == "reload" ? 3 : 2, loader.Calls.Count);
        Assert.All(loader.Calls, call => Assert.Equal(workbook.Path, call.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_application_completion_does_not_clear_a_newer_load_or_publish_stale_errors(
        bool staleLoaderFails)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(1);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputWorkbookLoadResult prepared = ReadResult(workbook.Path, 2);
        InputWorkbookLoadResult refreshed = ReadResult(workbook.Path, 1);
        TaskCompletionSource<InputWorkbookLoadResult> applyRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<InputWorkbookLoadResult> loadRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.NextLoad = (_, _, _) => applyRelease.Task;
        Task<bool> application = viewModel.ApplySavedDefinitionAsync(CreateSavedDefinition(), TestContext.Current.CancellationToken);
        loader.NextLoad = (_, _, _) => loadRelease.Task;
        Task reload = viewModel.RefreshHeaderAsync(TestContext.Current.CancellationToken);
        InputState whileReloading = new(viewModel);

        if (staleLoaderFails)
        {
            applyRelease.SetException(new IOException(PrivateCanary));
        }
        else
        {
            applyRelease.SetResult(prepared);
        }

        Assert.False(await application);

        Assert.True(viewModel.IsBusy);
        Assert.Null(viewModel.SavedDefinitionApplicationError);
        whileReloading.AssertUnchanged(viewModel);
        loadRelease.SetResult(refreshed);
        await reload;
        Assert.False(viewModel.IsBusy);
        Assert.True(viewModel.CanContinue);
        Assert.Equal([1U, 2U, 1U], loader.Calls.Select(call => call.HeaderRow));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Busy_application_does_not_supersede_an_existing_load_or_application(bool firstIsApplication)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(1);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition saved = CreateSavedDefinition();
        string previousHash = Serializer.ComputeSha256(viewModel.DefinitionDraft);
        InputWorkbookLoadResult prepared = ReadResult(workbook.Path, firstIsApplication ? 2U : 1U);
        TaskCompletionSource<InputWorkbookLoadResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.NextLoad = (_, _, _) => release.Task;
        Task pending = firstIsApplication
            ? viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken)
            : viewModel.RefreshHeaderAsync(TestContext.Current.CancellationToken);
        InputState before = new(viewModel);

        Assert.False(await viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));

        AssertApplicationError(viewModel, "INPUT_LOAD_IN_PROGRESS", workbook.Path);
        before.AssertUnchanged(viewModel);
        Assert.True(viewModel.IsBusy);
        Assert.Equal(2, loader.Calls.Count);
        release.SetResult(prepared);
        await pending;
        Assert.False(viewModel.IsBusy);
        Assert.Equal(
            firstIsApplication ? Serializer.ComputeSha256(saved) : previousHash,
            Serializer.ComputeSha256(viewModel.DefinitionDraft));
    }

    [Fact]
    public async Task Changed_workbook_snapshot_is_rejected_without_fallback_or_loaded_state_changes()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile(1);
        byte[] sourceBytes = File.ReadAllBytes(workbook.Path);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputWorkbookLoadResult prepared = ReadResult(workbook.Path, 2);
        InputSnapshot changed = new(
            prepared.Snapshot.Sha256,
            prepared.Snapshot.SizeBytes + 1,
            prepared.Snapshot.LastWriteTimeUtc);
        loader.NextLoad = (_, _, _) => Task.FromResult(new InputWorkbookLoadResult(changed, prepared.Metadata, prepared.Suggestions));
        InputState before = new(viewModel);

        Assert.False(await viewModel.ApplySavedDefinitionAsync(CreateSavedDefinition(), TestContext.Current.CancellationToken));

        AssertApplicationError(viewModel, "INPUT_CHANGED", workbook.Path);
        before.AssertUnchanged(viewModel);
        Assert.True(viewModel.CanContinue);
        Assert.False(viewModel.IsBusy);
        Assert.Equal(2, loader.Calls.Count);
        Assert.All(loader.Calls, call => Assert.Equal(workbook.Path, call.Path));
        Assert.Equal(sourceBytes, File.ReadAllBytes(workbook.Path));
    }

    private static QuantificationDefinition CreateSavedDefinition(int headerRow = 2, string sourceSheet = "Responses")
    {
        CriterionDefinition criterion = new()
        {
            Id = "criterion-z",
            DisplayName = "保存した観点",
            Description = "合成の評価基準\r\nUnicode と改行を保持する。",
            Weight = 0.125m,
            Range = new ScoreRange(-0.25m, 9.75m),
        };
        EvaluatorDefinition custom = new()
        {
            Id = "evaluator-z",
            DisplayName = "保存した Custom",
            Type = EvaluatorType.CustomPrompt,
            Weight = 1.375m,
            Range = new ScoreRange(-1.25m, 10.75m),
            CustomPromptTemplate = SavedPrompt,
            Criteria =
            [
                criterion,
                criterion with { Id = "criterion-a", Range = null, Weight = 2.5m, Enabled = false },
            ],
        };
        return new QuantificationDefinition
        {
            Id = "definition-保存",
            Name = "保存した採点定義",
            Revision = "rev-2.5",
            SourceSheet = sourceSheet,
            HeaderRow = headerRow,
            FirstDataRow = headerRow + 2,
            LastDataRow = headerRow + 8,
            BasePoints = 60.125m,
            SpecialPoints = 9.875m,
            SimilarityPenaltyWeight = 0.125m,
            RoundingDigits = 3,
            Questions =
            [
                new QuestionDefinition
                {
                    Id = "question-z",
                    DisplayName = "保存した設問 Z",
                    QuestionText = "保存済みの見出し由来の設問文\r\n現在の Excel の見出しで置換しない。",
                    PrimarySourceColumn = "F",
                    SupportingSourceColumns = ["H", "G"],
                    Points = 17.125m,
                    Evaluators =
                    [
                        custom,
                        new EvaluatorDefinition
                        {
                            Id = "evaluator-a",
                            DisplayName = "保存した Knowledge",
                            Type = EvaluatorType.KnowledgeCoverage,
                            Weight = 0.25m,
                            Range = new ScoreRange(0m, 10m),
                            BuiltInTemplateVersion = "knowledge-v1",
                            Criteria = [criterion with { Id = "criterion-knowledge" }],
                        },
                    ],
                    SpecialEvaluations =
                    [
                        new SpecialEvaluationDefinition
                        {
                            Id = "special-z",
                            DisplayName = "保存した固有評価 Z",
                            PrimarySourceColumn = "J",
                            SupportingSourceColumns = ["K", "H"],
                            PromptTemplate = SavedSpecialPrompt,
                        },
                        new SpecialEvaluationDefinition
                        {
                            Id = "special-a",
                            DisplayName = "保存した固有評価 A",
                            PrimarySourceColumn = "F",
                            SupportingSourceColumns = ["G"],
                            PromptTemplate = SavedSpecialPrompt + "\nDisabled",
                            Enabled = false,
                        },
                    ],
                },
                new QuestionDefinition
                {
                    Id = "question-a",
                    DisplayName = "保存した設問 A",
                    QuestionText = "二つ目の保存した設問文",
                    PrimarySourceColumn = "I",
                    SupportingSourceColumns = ["K", "J"],
                    Points = 12.875m,
                    Evaluators =
                    [
                        custom with
                        {
                            Id = "evaluator-second",
                            Criteria = [criterion with { Id = "criterion-second", Range = null }],
                        },
                    ],
                },
                new QuestionDefinition
                {
                    Id = "question-disabled",
                    DisplayName = "保存した無効設問",
                    QuestionText = "無効でも ID と配点を保持する。",
                    PrimarySourceColumn = "C",
                    Points = 123.456m,
                    Enabled = false,
                },
            ],
        };
    }

    private static QuantificationDefinition WithFirstQuestion(
        QuantificationDefinition definition,
        Func<QuestionDefinition, QuestionDefinition> update) => definition with
    {
        Questions = definition.Questions.SetItem(0, update(definition.Questions[0])),
    };

    private static QuantificationDefinition WithFirstEvaluator(
        QuantificationDefinition definition,
        Func<EvaluatorDefinition, EvaluatorDefinition> update) =>
        WithFirstQuestion(definition, question => question with
        {
            Evaluators = question.Evaluators.SetItem(0, update(question.Evaluators[0])),
        });

    private static QuantificationDefinition WithFirstSpecial(
        QuantificationDefinition definition,
        Func<SpecialEvaluationDefinition, SpecialEvaluationDefinition> update) =>
        WithFirstQuestion(definition, question => question with
        {
            SpecialEvaluations = question.SpecialEvaluations.SetItem(0, update(question.SpecialEvaluations[0])),
        });

    private static InputWorkbookLoadResult ReadResult(string path, uint headerRow)
    {
        WorkbookMetadata metadata = new WorkbookMetadataReader().Read(path, headerRow);
        return new InputWorkbookLoadResult(
            new InputSnapshotService().Capture(path),
            metadata,
            new ColumnMappingSuggester().Suggest(metadata));
    }

    private static void AssertApplicationError(InputViewModel viewModel, string code, string path)
    {
        InputValidationError error = Assert.IsType<InputValidationError>(viewModel.SavedDefinitionApplicationError);
        Assert.Equal(code, error.Code);
        Assert.Equal("<saved-definition>", error.NodeId);
        string exposed = string.Join("|", error.Code, error.NodeId, error.Field, error.Message, error.ToString());
        Assert.DoesNotContain(PrivateCanary, exposed, StringComparison.Ordinal);
        Assert.DoesNotContain(path, exposed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SavedPrompt, exposed, StringComparison.Ordinal);
    }

    private static void AssertDeepCopy(QuantificationDefinition saved, QuantificationDefinition applied)
    {
        Assert.NotSame(saved, applied);
        Assert.False(saved.Questions.Equals(applied.Questions));
        for (int questionIndex = 0; questionIndex < saved.Questions.Length; questionIndex++)
        {
            QuestionDefinition source = saved.Questions[questionIndex];
            QuestionDefinition copy = applied.Questions[questionIndex];
            Assert.NotSame(source, copy);
            Assert.Equal(source.SupportingSourceColumns, copy.SupportingSourceColumns);
            for (int evaluatorIndex = 0; evaluatorIndex < source.Evaluators.Length; evaluatorIndex++)
            {
                EvaluatorDefinition sourceEvaluator = source.Evaluators[evaluatorIndex];
                EvaluatorDefinition copiedEvaluator = copy.Evaluators[evaluatorIndex];
                Assert.NotSame(sourceEvaluator, copiedEvaluator);
                for (int criterionIndex = 0; criterionIndex < sourceEvaluator.Criteria.Length; criterionIndex++)
                {
                    Assert.NotSame(sourceEvaluator.Criteria[criterionIndex], copiedEvaluator.Criteria[criterionIndex]);
                }
            }

            for (int specialIndex = 0; specialIndex < source.SpecialEvaluations.Length; specialIndex++)
            {
                Assert.NotSame(source.SpecialEvaluations[specialIndex], copy.SpecialEvaluations[specialIndex]);
                Assert.Equal(
                    source.SpecialEvaluations[specialIndex].SupportingSourceColumns,
                    copy.SpecialEvaluations[specialIndex].SupportingSourceColumns);
            }
        }
    }

    private static void AssertQuestionsMatch(QuantificationDefinition definition, InputViewModel viewModel)
    {
        Assert.Equal(definition.Questions.Select(question => question.Id), viewModel.Questions.Select(question => question.Id));
        for (int index = 0; index < definition.Questions.Length; index++)
        {
            QuestionDefinition expected = definition.Questions[index];
            InputQuestionMappingViewModel actual = viewModel.Questions[index];
            Assert.Equal(expected.DisplayName, actual.DisplayName);
            Assert.Equal(expected.QuestionText, actual.QuestionText);
            Assert.Equal(expected.PrimarySourceColumn, actual.PrimarySourceColumn);
            Assert.Equal(expected.Points, actual.Weight);
            Assert.Equal(expected.Enabled, actual.Enabled);
            ImmutableArray<string> supporting = expected.SupportingSourceColumns.IsDefault ? [] : expected.SupportingSourceColumns;
            Assert.Equal(
                supporting.Order(StringComparer.OrdinalIgnoreCase),
                actual.SupportingColumns.Where(column => column.IsSelected).Select(column => column.ColumnName).Order(StringComparer.OrdinalIgnoreCase));
            Assert.Equal(
                supporting.IsEmpty ? "補助列なし" : string.Join(", ", supporting),
                actual.SupportingSummary);
            Assert.All(actual.SupportingColumns, column => Assert.Equal(
                !string.Equals(column.ColumnName, expected.PrimarySourceColumn, StringComparison.OrdinalIgnoreCase),
                column.CanSelect));
        }
    }

    private sealed class InputState(InputViewModel viewModel)
    {
        private readonly QuantificationDefinition draft = viewModel.DefinitionDraft;
        private readonly byte[] canonical = Serializer.SerializeToUtf8Bytes(viewModel.DefinitionDraft);
        private readonly WorkbookMetadata? metadata = viewModel.Metadata;
        private readonly InputSnapshot? snapshot = viewModel.Snapshot;
        private readonly WorksheetChoiceViewModel? selectedChoice = viewModel.SelectedWorksheetChoice;
        private readonly (string Path, string Sheet, int Header, int First, int Last, bool Suggested) selection = (
            viewModel.FilePath, viewModel.SelectedSheet, viewModel.HeaderRow, viewModel.FirstDataRow, viewModel.LastDataRow, viewModel.IsUsingSuggestedMapping);
        private readonly WorksheetChoiceViewModel[] worksheets = viewModel.Worksheets.ToArray();
        private readonly SourceColumnOption[] columns = viewModel.AvailableColumns.ToArray();
        private readonly string[] columnNames = viewModel.AvailableColumnNames.ToArray();
        private readonly MappingSuggestionViewModel[] suggestions = viewModel.MappingSuggestions.ToArray();
        private readonly InputQuestionMappingViewModel[] questions = viewModel.Questions.ToArray();
        private readonly SupportingColumnSelectionViewModel[][] supporting = viewModel.Questions.Select(question => question.SupportingColumns.ToArray()).ToArray();
        private readonly InputValidationError[] errors = viewModel.ValidationErrors.ToArray();

        public void AssertUnchanged(InputViewModel current)
        {
            Assert.Same(draft, current.DefinitionDraft);
            Assert.Equal(canonical, Serializer.SerializeToUtf8Bytes(current.DefinitionDraft));
            Assert.Same(metadata, current.Metadata);
            Assert.Same(snapshot, current.Snapshot);
            Assert.Same(selectedChoice, current.SelectedWorksheetChoice);
            Assert.Equal(selection, (
                current.FilePath, current.SelectedSheet, current.HeaderRow, current.FirstDataRow, current.LastDataRow, current.IsUsingSuggestedMapping));
            Assert.Equal(worksheets, current.Worksheets);
            Assert.Equal(columns, current.AvailableColumns);
            Assert.Equal(columnNames, current.AvailableColumnNames);
            Assert.Equal(suggestions, current.MappingSuggestions);
            Assert.Equal(questions, current.Questions);
            Assert.Equal(errors, current.ValidationErrors);
            for (int index = 0; index < supporting.Length; index++)
            {
                Assert.Equal(supporting[index], current.Questions[index].SupportingColumns);
            }

            AssertQuestionsMatch(draft, current);
        }
    }

    private sealed class ScriptedReadOnlyLoader : IInputWorkbookLoader
    {
        private readonly InputWorkbookLoader inner = new();

        public List<(string Path, uint HeaderRow, CancellationToken Token)> Calls { get; } = [];

        public Func<string, uint, CancellationToken, Task<InputWorkbookLoadResult>>? NextLoad { get; set; }

        public Task<InputWorkbookLoadResult> LoadAsync(
            string filePath,
            uint headerRow,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((filePath, headerRow, cancellationToken));
            Func<string, uint, CancellationToken, Task<InputWorkbookLoadResult>>? next = NextLoad;
            NextLoad = null;
            return next is null
                ? inner.LoadAsync(filePath, headerRow, cancellationToken)
                : next(filePath, headerRow, cancellationToken);
        }
    }
}