using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-003 (AC-003), FR-005 (AC-005), NFR-PERF-001 (AC-071)
public sealed class InputPresentationTests
{
    private static readonly CanonicalDefinitionSerializer Serializer = new();

    [Fact]
    public void Presentation_API_matches_Design_names_with_original_read_only_mapping_editors()
    {
        AssertProperty(nameof(InputViewModel.SelectedQuestion), typeof(InputQuestionMappingViewModel), writable: true);
        AssertProperty(nameof(InputViewModel.Questions), typeof(ReadOnlyObservableCollection<InputQuestionMappingViewModel>), writable: false);
        AssertProperty(nameof(InputViewModel.VisibleQuestions), typeof(ReadOnlyObservableCollection<InputQuestionMappingViewModel>), writable: false);
        AssertProperty(nameof(InputViewModel.PageSize), typeof(int), writable: true);
        AssertProperty(nameof(InputViewModel.PageIndex), typeof(int), writable: true);
        AssertProperty(nameof(InputViewModel.PageSummary), typeof(string), writable: false);
        AssertProperty(nameof(InputViewModel.InputSummary), typeof(string), writable: false);
        AssertProperty(nameof(InputViewModel.PreviousPageCommand), typeof(ICommand), writable: false);
        AssertProperty(nameof(InputViewModel.NextPageCommand), typeof(ICommand), writable: false);
    }

    [Fact]
    public void Unloaded_input_stays_empty_and_page_commands_do_not_create_questions_or_load()
    {
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        QuantificationDefinition draft = viewModel.DefinitionDraft;

        Assert.Equal(4, viewModel.PageSize);
        viewModel.PageIndex = int.MaxValue;
        viewModel.PageIndex = int.MinValue;
        viewModel.PageSize = int.MaxValue;
        viewModel.SelectedQuestion = null;
        viewModel.PreviousPageCommand.Execute(null);
        viewModel.NextPageCommand.Execute(null);

        Assert.Empty(viewModel.Questions);
        Assert.Null(viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "設問はありません（0 件）", [], false, false);
        Assert.Equal("入力は未読込です。", viewModel.InputSummary);
        Assert.Same(draft, viewModel.DefinitionDraft);
        Assert.False(viewModel.CanContinue);
        Assert.Equal(0, loader.LoadCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(33)]
    public void Every_question_is_reachable_without_truncating_copying_or_mutating_the_draft(int questionCount)
    {
        InputViewModel viewModel = CreateInput(questionCount);
        QuantificationDefinition draft = viewModel.DefinitionDraft;
        string canonical = Serializer.Serialize(draft);
        ReadOnlyObservableCollection<InputQuestionMappingViewModel> questions = viewModel.Questions;
        ReadOnlyObservableCollection<InputQuestionMappingViewModel> visible = viewModel.VisibleQuestions;
        InputQuestionMappingViewModel[] originals = questions.ToArray();
        ICommand previous = viewModel.PreviousPageCommand;
        ICommand next = viewModel.NextPageCommand;
        List<InputQuestionMappingViewModel> visited = [];
        int lastPage = (questionCount - 1) / 4;

        for (int page = 0; page <= lastPage; page++)
        {
            string summary = string.Format(CultureInfo.InvariantCulture,
                "{0:N0}–{1:N0} / {2:N0} 件", page * 4 + 1, Math.Min(page * 4 + 4, questionCount), questionCount);
            AssertPage(viewModel, page, summary, originals.Skip(page * 4).Take(4), page > 0, page < lastPage);
            Assert.Same(originals[0], viewModel.SelectedQuestion);
            visited.AddRange(visible);
            next.Execute(null);
        }

        Assert.Equal(originals.Length, visited.Count);
        for (int index = 0; index < originals.Length; index++)
        {
            Assert.Same(originals[index], visited[index]);
            Assert.Same(originals[index], questions[index]);
        }

        Assert.Same(questions, viewModel.Questions);
        Assert.Same(visible, viewModel.VisibleQuestions);
        Assert.Same(previous, viewModel.PreviousPageCommand);
        Assert.Same(next, viewModel.NextPageCommand);
        Assert.Same(draft, viewModel.DefinitionDraft);
        Assert.Equal(canonical, Serializer.Serialize(viewModel.DefinitionDraft));
        IList<InputQuestionMappingViewModel> readOnly = visible;
        Assert.True(readOnly.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => readOnly.Add(originals[0]));
    }

    [Fact]
    public void Page_index_clamps_and_reselecting_the_same_editor_returns_to_its_page()
    {
        InputViewModel viewModel = CreateInput();
        InputQuestionMappingViewModel[] questions = viewModel.Questions.ToArray();
        QuantificationDefinition draft = viewModel.DefinitionDraft;
        viewModel.SelectedQuestion = questions[6];

        viewModel.PageIndex = int.MaxValue;
        AssertPage(viewModel, 2, "9–9 / 9 件", [questions[8]], true, false);
        viewModel.NextPageCommand.Execute(null);
        Assert.Equal(2, viewModel.PageIndex);
        viewModel.PreviousPageCommand.Execute(null);
        AssertPage(viewModel, 1, "5–8 / 9 件", questions.Skip(4).Take(4), true, true);
        viewModel.PageIndex = int.MinValue;
        AssertPage(viewModel, 0, "1–4 / 9 件", questions.Take(4), false, true);
        Assert.Same(questions[6], viewModel.SelectedQuestion);

        viewModel.SelectedQuestion = questions[6];

        AssertPage(viewModel, 1, "5–8 / 9 件", questions.Skip(4).Take(4), true, true);
        Assert.Same(questions[6], viewModel.SelectedQuestion);
        Assert.Same(draft, viewModel.DefinitionDraft);
    }

    [Fact]
    public void Foreign_same_id_references_are_rejected_and_explicit_null_survives_resize_and_value_sync()
    {
        InputViewModel viewModel = CreateInput();
        InputViewModel foreign = new();
        SynchronizeFromDesign(foreign, viewModel.DefinitionDraft);
        InputQuestionMappingViewModel last = viewModel.Questions[8];
        QuantificationDefinition draft = viewModel.DefinitionDraft;
        viewModel.SelectedQuestion = last;

        Assert.Equal(last.Id, foreign.Questions[8].Id);
        Assert.NotSame(last, foreign.Questions[8]);
        viewModel.SelectedQuestion = foreign.Questions[8];
        viewModel.SelectedQuestion = foreign.Questions[0];
        Assert.Same(last, viewModel.SelectedQuestion);
        Assert.Equal(2, viewModel.PageIndex);
        viewModel.SelectedQuestion = null;
        viewModel.PageSize = 5;

        Assert.Null(viewModel.SelectedQuestion);
        AssertPage(viewModel, 1, "6–9 / 9 件", viewModel.Questions.Skip(5), true, false);
        Assert.Same(draft, viewModel.DefinitionDraft);
        QuantificationDefinition incoming = draft with { Revision = "value-only" };
        SynchronizeFromDesign(viewModel, incoming);
        Assert.Null(viewModel.SelectedQuestion);
        AssertPage(viewModel, 1, "6–9 / 9 件", viewModel.Questions.Skip(5), true, false);
        AssertCanonical(incoming, viewModel.DefinitionDraft);
    }

    [Theory]
    [InlineData(1, 6, "7–7 / 9 件")]
    [InlineData(3, 2, "7–9 / 9 件")]
    [InlineData(7, 0, "1–7 / 9 件")]
    [InlineData(9, 0, "1–9 / 9 件")]
    [InlineData(int.MaxValue, 0, "1–9 / 9 件")]
    public void Resize_preserves_the_selected_id_and_canonical_content(int size, int index, string summary)
    {
        InputViewModel viewModel = CreateInput();
        InputQuestionMappingViewModel selected = viewModel.Questions[6];
        viewModel.SelectedQuestion = selected;
        viewModel.PageIndex = 0;
        QuantificationDefinition draft = viewModel.DefinitionDraft;
        string canonical = Serializer.Serialize(draft);
        ((INotifyCollectionChanged)viewModel.VisibleQuestions).CollectionChanged +=
            (_, _) => viewModel.SelectedQuestion = null;

        viewModel.PageSize = size;

        Assert.Equal(size, viewModel.PageSize);
        Assert.Same(selected, viewModel.SelectedQuestion);
        AssertPage(viewModel, index, summary, viewModel.Questions.Skip(index * size).Take(size),
            index > 0, size is 1 or 7);
        Assert.Same(draft, viewModel.DefinitionDraft);
        Assert.Equal(canonical, Serializer.Serialize(viewModel.DefinitionDraft));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Nonpositive_page_size_is_rejected_without_changes_or_notifications(int size)
    {
        InputViewModel viewModel = CreateInput();
        viewModel.SelectedQuestion = viewModel.Questions[6];
        QuantificationDefinition draft = viewModel.DefinitionDraft;
        List<string?> changes = [];
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        Assert.Throws<ArgumentOutOfRangeException>(() => viewModel.PageSize = size);

        Assert.Equal(4, viewModel.PageSize);
        Assert.Same(viewModel.Questions[6], viewModel.SelectedQuestion);
        AssertPage(viewModel, 1, "5–8 / 9 件", viewModel.Questions.Skip(4).Take(4), true, true);
        Assert.Same(draft, viewModel.DefinitionDraft);
        Assert.Empty(changes);
    }

    [Theory]
    [InlineData(0, int.MaxValue, 4, 0, 0, 0)]
    [InlineData(1, int.MinValue, 4, 0, 0, 1)]
    [InlineData(4, 1, 4, 0, 0, 4)]
    [InlineData(5, 1, 4, 1, 4, 1)]
    [InlineData(19999, int.MaxValue, 4, 4999, 19996, 3)]
    [InlineData(20000, int.MaxValue, 4, 4999, 19996, 4)]
    [InlineData(20001, int.MaxValue, 4, 5000, 20000, 1)]
    [InlineData(20000, int.MaxValue, 7, 2857, 19999, 1)]
    [InlineData(20000, int.MaxValue, int.MaxValue, 0, 0, 20000)]
    [InlineData(int.MaxValue, int.MaxValue, 4, 536870911, 2147483644, 3)]
    [InlineData(int.MaxValue, int.MaxValue, 1, int.MaxValue - 1, int.MaxValue - 1, 1)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue, 0, 0, int.MaxValue)]
    public void Page_math_handles_twenty_thousand_and_integer_boundaries_without_editor_allocation(
        int count, int requestedIndex, int size, int expectedIndex, int expectedStart, int expectedCount)
    {
        // Exercise the production calculation without widening its API or allocating thousands of VMs.
        MethodInfo method = Assert.IsAssignableFrom<MethodInfo>(typeof(InputViewModel).GetMethod(
            "CalculateQuestionPage", BindingFlags.NonPublic | BindingFlags.Static));
        var page = Assert.IsType<(int PageIndex, int Start, int Count)>(method.Invoke(null, [count, requestedIndex, size]));

        Assert.Equal((expectedIndex, expectedStart, expectedCount), page);
        Assert.InRange((long)page.Start + page.Count, 0L, count);
    }

    [Fact]
    public void Add_duplicate_and_cross_page_reorder_keep_the_surviving_selection_and_original_values()
    {
        InputViewModel viewModel = CreateInput(4);
        InputQuestionMappingViewModel[] originals = viewModel.Questions.ToArray();
        viewModel.SelectedQuestion = originals[3];
        QuantificationDefinition draft = viewModel.DefinitionDraft;
        InputQuestionMappingViewModel added = viewModel.AddQuestion();
        Assert.Same(originals[3], viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "1–4 / 5 件", originals, false, true);

        InputQuestionMappingViewModel duplicate = viewModel.DuplicateQuestion(originals[0].Id);

        Assert.Same(originals[3], viewModel.SelectedQuestion);
        AssertPage(viewModel, 1, "5–6 / 6 件", [originals[3], added], true, false);
        Assert.NotEqual(originals[0].Id, duplicate.Id);
        Assert.NotEqual(draft.Questions[0].Evaluators[0].Id, viewModel.DefinitionDraft.Questions[1].Evaluators[0].Id);
        Assert.NotEqual(draft.Questions[0].Evaluators[0].Criteria[0].Id,
            viewModel.DefinitionDraft.Questions[1].Evaluators[0].Criteria[0].Id);
        string[] originalIds = originals.Select(item => item.Id).ToArray();
        AssertCanonical(draft, viewModel.DefinitionDraft with
        {
            Questions = [.. viewModel.DefinitionDraft.Questions.Where(item => originalIds.Contains(item.Id))],
        });
        QuantificationDefinition beforeMove = viewModel.DefinitionDraft;
        originals[3].MoveUpCommand.Execute(null);
        AssertPage(viewModel, 0, "1–4 / 6 件", [originals[0], duplicate, originals[1], originals[3]], false, true);
        Assert.Same(originals[3], viewModel.SelectedQuestion);
        AssertCanonical(beforeMove.MoveQuestion(4, 3), viewModel.DefinitionDraft);
        originals[3].MoveDownCommand.Execute(null);
        AssertPage(viewModel, 1, "5–6 / 6 件", [originals[3], added], true, false);
        AssertCanonical(beforeMove, viewModel.DefinitionDraft);
        viewModel.SelectedQuestion = duplicate;
        Assert.Equal(0, viewModel.PageIndex);
        viewModel.SelectedQuestion = added;
        Assert.Equal(1, viewModel.PageIndex);
    }

    [Fact]
    public void Last_page_deletion_uses_the_previous_neighbor_then_empty_and_allows_add_again()
    {
        InputViewModel viewModel = CreateInput();
        InputQuestionMappingViewModel[] originals = viewModel.Questions.ToArray();
        QuantificationDefinition expected = viewModel.DefinitionDraft;
        viewModel.SelectedQuestion = originals[8];

        for (int index = 8; index >= 0; index--)
        {
            originals[index].DeleteCommand.Execute(null);
            expected = expected.RemoveQuestion(index);
            AssertCanonical(expected, viewModel.DefinitionDraft);
            Assert.Same(index > 0 ? originals[index - 1] : null, viewModel.SelectedQuestion);
            viewModel.SelectedQuestion = originals[index];
            Assert.Same(index > 0 ? originals[index - 1] : null, viewModel.SelectedQuestion);
            if (index == 8)
            {
                AssertPage(viewModel, 1, "5–8 / 8 件", originals.Skip(4).Take(4), true, false);
            }
        }

        Assert.Null(viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "設問はありません（0 件）", [], false, false);
        InputQuestionMappingViewModel added = viewModel.AddQuestion();
        Assert.Same(added, viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "1–1 / 1 件", [added], false, false);
    }

    [Fact]
    public void Middle_deletion_and_incoming_reorder_use_old_order_neighbors_not_incoming_positions()
    {
        InputViewModel viewModel = CreateInput();
        InputQuestionMappingViewModel[] originals = viewModel.Questions.ToArray();
        viewModel.SelectedQuestion = originals[3];
        QuantificationDefinition expected = viewModel.DefinitionDraft.RemoveQuestion(3);

        originals[3].DeleteCommand.Execute(null);

        Assert.Same(originals[4], viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "1–4 / 8 件", [originals[0], originals[1], originals[2], originals[4]], false, true);
        AssertCanonical(expected, viewModel.DefinitionDraft);
        originals[0].DeleteCommand.Execute(null);
        Assert.Same(originals[4], viewModel.SelectedQuestion);
        viewModel.SelectedQuestion = originals[6];
        QuantificationDefinition incoming = viewModel.DefinitionDraft with
        {
            Questions = [.. new[] { originals[8], originals[7], originals[1] }
                .Select(item => viewModel.DefinitionDraft.Questions.Single(question => question.Id == item.Id))],
        };

        SynchronizeFromDesign(viewModel, incoming);

        Assert.Same(originals[7], viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "1–3 / 3 件", [originals[8], originals[7], originals[1]], false, false);
        AssertCanonical(incoming, viewModel.DefinitionDraft);
    }

    [Fact]
    public void Replacing_all_ids_never_retains_an_editor_from_the_previous_draft()
    {
        InputViewModel viewModel = CreateInput();
        InputQuestionMappingViewModel[] originals = viewModel.Questions.ToArray();
        viewModel.SelectedQuestion = originals[8];
        QuantificationDefinition incoming = viewModel.DefinitionDraft with
        {
            Id = "replacement-definition",
            Questions = [.. viewModel.DefinitionDraft.Questions.Take(5).Select(question => question with { Id = "new-" + question.Id })],
        };

        SynchronizeFromDesign(viewModel, incoming);

        Assert.Same(viewModel.Questions[0], viewModel.SelectedQuestion);
        Assert.All(viewModel.Questions, item => Assert.DoesNotContain(item, originals));
        AssertPage(viewModel, 0, "1–4 / 5 件", viewModel.Questions.Take(4), false, true);
        AssertCanonical(incoming, viewModel.DefinitionDraft);
        viewModel.SelectedQuestion = originals[8];
        Assert.Same(viewModel.Questions[0], viewModel.SelectedQuestion);
    }

    [Fact]
    public void Value_only_edits_and_design_sync_keep_a_separately_browsed_page_and_disabled_questions()
    {
        InputViewModel viewModel = CreateInput();
        InputQuestionMappingViewModel selected = viewModel.Questions[1];
        viewModel.SelectedQuestion = selected;
        viewModel.PageIndex = 2;
        int collectionChanges = 0;
        ((INotifyCollectionChanged)viewModel.VisibleQuestions).CollectionChanged += (_, _) => collectionChanges++;

        selected.DisplayName = "編集中の設問";
        selected.QuestionText = "手動の設問文\r\n第二行";
        selected.Weight = 0.123456789m;
        selected.PrimarySourceColumn = "B";
        selected.SetSupportingColumn("C", true);
        foreach (InputQuestionMappingViewModel question in viewModel.Questions)
        {
            question.Enabled = false;
            Assert.Equal(2, viewModel.PageIndex);
        }

        QuantificationDefinition incoming = viewModel.DefinitionDraft with { Name = "value-only sync" };
        SynchronizeFromDesign(viewModel, incoming);

        Assert.Equal(0, collectionChanges);
        Assert.Same(selected, viewModel.SelectedQuestion);
        AssertPage(viewModel, 2, "9–9 / 9 件", [viewModel.Questions[8]], true, false);
        Assert.Equal(9, viewModel.Questions.Count);
        Assert.All(viewModel.Questions, question => Assert.False(question.Enabled));
        Assert.Equal("手動の設問文\r\n第二行", selected.QuestionText);
        AssertCanonical(incoming, viewModel.DefinitionDraft);
    }

    [Fact]
    public void Collection_and_property_writebacks_cannot_clear_or_replace_logical_selection()
    {
        InputViewModel viewModel = CreateInput();
        InputQuestionMappingViewModel selected = viewModel.Questions[6];
        InputQuestionMappingViewModel other = viewModel.Questions[0];
        viewModel.SelectedQuestion = selected;
        int writebacks = 0;
        void WriteBack()
        {
            writebacks++;
            viewModel.SelectedQuestion = null;
            viewModel.SelectedQuestion = other;
        }

        ((INotifyCollectionChanged)viewModel.Questions).CollectionChanged += (_, _) => WriteBack();
        ((INotifyCollectionChanged)viewModel.VisibleQuestions).CollectionChanged += (_, _) => WriteBack();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(InputViewModel.SelectedQuestion))
            {
                WriteBack();
            }
        };
        QuantificationDefinition draft = viewModel.DefinitionDraft;

        viewModel.NextPageCommand.Execute(null);
        Assert.Same(selected, viewModel.SelectedQuestion);
        viewModel.PageSize = 3;
        Assert.Same(selected, viewModel.SelectedQuestion);
        Assert.Same(draft, viewModel.DefinitionDraft);
        selected.MoveUpCommand.Execute(null);

        Assert.True(writebacks > 0);
        Assert.Same(selected, viewModel.SelectedQuestion);
        AssertCanonical(draft.MoveQuestion(6, 5), viewModel.DefinitionDraft);
        viewModel.SelectedQuestion = null;
        Assert.Null(viewModel.SelectedQuestion);
    }

    [Fact]
    public void Presentation_notifications_publish_settled_ranges_and_never_report_a_draft_edit()
    {
        InputViewModel viewModel = CreateInput();
        List<string?> properties = [];
        List<(int Page, string Summary, int Count, bool Previous, bool Next)> states = [];
        void Capture() => states.Add((viewModel.PageIndex, viewModel.PageSummary, viewModel.VisibleQuestions.Count,
            viewModel.PreviousPageCommand.CanExecute(null), viewModel.NextPageCommand.CanExecute(null)));
        viewModel.PropertyChanged += (_, args) =>
        {
            properties.Add(args.PropertyName);
            Capture();
        };
        viewModel.PreviousPageCommand.CanExecuteChanged += (_, _) => Capture();
        viewModel.NextPageCommand.CanExecuteChanged += (_, _) => Capture();
        QuantificationDefinition draft = viewModel.DefinitionDraft;

        viewModel.SelectedQuestion = viewModel.Questions[8];

        Assert.NotEmpty(states);
        Assert.All(states, state => Assert.Equal((2, "9–9 / 9 件", 1, true, false), state));
        states.Clear();
        viewModel.PageSize = 5;
        Assert.NotEmpty(states);
        Assert.All(states, state => Assert.Equal((1, "6–9 / 9 件", 4, true, false), state));
        Assert.Contains(nameof(InputViewModel.PageSize), properties);
        Assert.Contains(nameof(InputViewModel.PageIndex), properties);
        Assert.Contains(nameof(InputViewModel.PageSummary), properties);
        Assert.Contains(nameof(InputViewModel.SelectedQuestion), properties);
        Assert.DoesNotContain(nameof(InputViewModel.DefinitionDraft), properties);
        Assert.Same(draft, viewModel.DefinitionDraft);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("resize")]
    [InlineData("structure")]
    public void Presentation_guard_is_released_even_when_a_notification_throws(string operation)
    {
        InputViewModel viewModel = CreateInput();
        void Fail(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(InputViewModel.PageSummary))
            {
                throw new InvalidOperationException("Synthetic notification failure.");
            }
        }

        viewModel.PropertyChanged += Fail;
        try
        {
            Assert.Throws<InvalidOperationException>(() =>
            {
                switch (operation)
                {
                    case "page": viewModel.PageIndex = 1; break;
                    case "resize": viewModel.PageSize = 3; break;
                    case "structure": viewModel.AddQuestion(); break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation));
                }
            });
        }
        finally
        {
            viewModel.PropertyChanged -= Fail;
        }

        viewModel.SelectedQuestion = null;
        Assert.Null(viewModel.SelectedQuestion);
        viewModel.SelectedQuestion = viewModel.Questions[^1];
        Assert.Same(viewModel.Questions[^1], viewModel.SelectedQuestion);
        Assert.Contains(viewModel.VisibleQuestions, item => ReferenceEquals(item, viewModel.SelectedQuestion));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Input_summary_uses_loaded_metadata_actual_range_and_all_enabled_questions(int headerRow)
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook(5, headerRow);
        byte[] bytes = File.ReadAllBytes(workbook.Path);
        InputViewModel viewModel = new() { HeaderRow = headerRow };
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        string initialSummary = $"Responses · 質問行 {headerRow} · 回答行 {headerRow + 1}–{headerRow + 100}（100 行） · 設問 5 / 5 件有効";
        Assert.Equal(initialSummary, viewModel.InputSummary);
        viewModel.PageIndex = int.MaxValue;
        Assert.Equal(initialSummary, viewModel.InputSummary);
        List<string?> changes = [];
        viewModel.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        viewModel.Questions[0].Enabled = false;
        viewModel.FirstDataRow = headerRow + 3;
        viewModel.LastDataRow = headerRow + 12;

        Assert.Equal($"Responses · 質問行 {headerRow} · 回答行 {headerRow + 3}–{headerRow + 12}（10 行） · 設問 4 / 5 件有効",
            viewModel.InputSummary);
        Assert.Equal(1, viewModel.PageIndex);
        Assert.Contains(nameof(InputViewModel.InputSummary), changes);
        int nextHeader = headerRow == 1 ? 2 : 1;
        viewModel.HeaderRow = nextHeader;
        Assert.Contains($"質問行 {nextHeader}（読込済み {headerRow}・再読込が必要）", viewModel.InputSummary, StringComparison.Ordinal);
        QuantificationDefinition beforeRefresh = viewModel.DefinitionDraft;
        InputQuestionMappingViewModel? selected = viewModel.SelectedQuestion;

        await viewModel.RefreshHeaderAsync(TestContext.Current.CancellationToken);

        Assert.Equal($"Responses · 質問行 {nextHeader} · 回答行 {headerRow + 3}–{headerRow + 12}（10 行） · 設問 4 / 5 件有効",
            viewModel.InputSummary);
        Assert.Equal((uint)nextHeader, viewModel.Metadata!.HeaderRowNumber);
        Assert.Same(selected, viewModel.SelectedQuestion);
        Assert.Equal(1, viewModel.PageIndex);
        AssertCanonical(beforeRefresh, viewModel.DefinitionDraft);
        Assert.DoesNotContain(workbook.Path, viewModel.InputSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("Report answer", viewModel.InputSummary, StringComparison.Ordinal);
        foreach (InputQuestionMappingViewModel question in viewModel.Questions)
        {
            question.Enabled = false;
        }

        Assert.Equal($"Responses · 質問行 {nextHeader} · 回答行 {headerRow + 3}–{headerRow + 12}（10 行） · 設問 0 / 5 件有効",
            viewModel.InputSummary);
        Assert.Equal(5, viewModel.Questions.Count);
        Assert.Equal(1, viewModel.PageIndex);
        Assert.Equal(bytes, File.ReadAllBytes(workbook.Path));
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(2, 1)]
    [InlineData(2, 102)]
    [InlineData(int.MinValue, int.MaxValue)]
    public async Task Invalid_ranges_are_disclosed_without_clamping_draft_values_or_inventing_row_counts(int first, int last)
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook();
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        viewModel.FirstDataRow = first;
        viewModel.LastDataRow = last;
        QuantificationDefinition draft = viewModel.DefinitionDraft;

        viewModel.PageIndex = int.MaxValue;
        viewModel.PageSize = 1;

        Assert.Contains("範囲を確認してください", viewModel.InputSummary, StringComparison.Ordinal);
        Assert.Equal(first, viewModel.DefinitionDraft.FirstDataRow);
        Assert.Equal(last, viewModel.DefinitionDraft.LastDataRow);
        Assert.Same(draft, viewModel.DefinitionDraft);
        Assert.False(viewModel.CanContinue);
        viewModel.SelectedSheet = "missing-sheet";
        Assert.Equal("回答 sheet を選択してください。 · 設問 9 / 9 件有効", viewModel.InputSummary);
    }

    [Fact]
    public async Task Explicit_application_refresh_reload_and_new_file_never_keep_stale_question_references()
    {
        using X02TemporaryWorkbook first = CreateWorkbook();
        using X02TemporaryWorkbook second = CreateWorkbook(headerRow: 2);
        byte[] firstBytes = File.ReadAllBytes(first.Path);
        byte[] secondBytes = File.ReadAllBytes(second.Path);
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(first.Path, TestContext.Current.CancellationToken);
        InputQuestionMappingViewModel[] originals = viewModel.Questions.ToArray();
        InputQuestionMappingViewModel selected = originals[6];
        viewModel.SelectedQuestion = selected;
        viewModel.PageIndex = 2;
        QuantificationDefinition saved = viewModel.DefinitionDraft with
        {
            HeaderRow = 2,
            FirstDataRow = 3,
            LastDataRow = 80,
            Questions = [.. viewModel.DefinitionDraft.Questions.Select(question => question with
            {
                QuestionText = "保存した設問文\r\n現在の見出しで上書きしない。",
                Evaluators = [.. question.Evaluators.Select(evaluator => evaluator with
                {
                    Type = EvaluatorType.CustomPrompt,
                    BuiltInTemplateVersion = null,
                    CustomPromptTemplate = "保存した Custom\r\n{回答} {評価項目}",
                })],
            })],
        };
        string savedHash = Serializer.ComputeSha256(saved);

        Assert.True(await viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));

        Assert.Same(selected, viewModel.SelectedQuestion);
        Assert.Equal(2, viewModel.PageIndex);
        AssertCanonical(saved, viewModel.DefinitionDraft);
        Assert.NotSame(saved, viewModel.DefinitionDraft);
        Assert.NotSame(saved.Questions[0], viewModel.DefinitionDraft.Questions[0]);
        Assert.NotSame(saved.Questions[0].Evaluators[0], viewModel.DefinitionDraft.Questions[0].Evaluators[0]);
        Assert.NotSame(saved.Questions[0].Evaluators[0].Criteria[0], viewModel.DefinitionDraft.Questions[0].Evaluators[0].Criteria[0]);
        Assert.Equal("Responses · 質問行 2 · 回答行 3–80（78 行） · 設問 9 / 9 件有効", viewModel.InputSummary);
        await viewModel.RefreshHeaderAsync(TestContext.Current.CancellationToken);
        Assert.Same(selected, viewModel.SelectedQuestion);
        Assert.Equal(2, viewModel.PageIndex);
        AssertCanonical(saved, viewModel.DefinitionDraft);
        QuantificationDefinition reordered = saved.MoveQuestion(6, 8);
        Assert.True(await viewModel.ApplySavedDefinitionAsync(reordered, TestContext.Current.CancellationToken));
        Assert.Same(selected, viewModel.SelectedQuestion);
        AssertPage(viewModel, 2, "9–9 / 9 件", [selected], true, false);
        AssertCanonical(reordered, viewModel.DefinitionDraft);

        await viewModel.LoadAsync(TestContext.Current.CancellationToken);

        Assert.All(viewModel.Questions, item => Assert.DoesNotContain(item, originals));
        Assert.Same(viewModel.Questions[0], viewModel.SelectedQuestion);
        viewModel.FilePath = second.Path;
        Assert.Null(viewModel.SelectedQuestion);
        Assert.Empty(viewModel.Questions);
        AssertPage(viewModel, 0, "設問はありません（0 件）", [], false, false);
        Assert.Equal("入力は未読込です。", viewModel.InputSummary);
        await viewModel.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(await viewModel.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));
        Assert.All(viewModel.Questions, item => Assert.DoesNotContain(item, originals));
        Assert.Same(viewModel.Questions[0], viewModel.SelectedQuestion);
        viewModel.SelectedQuestion = selected;
        Assert.Same(viewModel.Questions[0], viewModel.SelectedQuestion);
        AssertCanonical(saved, viewModel.DefinitionDraft);
        Assert.Equal(savedHash, Serializer.ComputeSha256(saved));
        Assert.Equal(firstBytes, File.ReadAllBytes(first.Path));
        Assert.Equal(secondBytes, File.ReadAllBytes(second.Path));
    }

    [Theory]
    [InlineData("display")]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("failure")]
    public async Task Presentation_during_application_preserves_T04_commit_conflict_and_failure_boundaries(string operation)
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook();
        byte[] bytes = File.ReadAllBytes(workbook.Path);
        ScriptedReadOnlyLoader loader = new();
        InputViewModel viewModel = new(loader);
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        QuantificationDefinition original = viewModel.DefinitionDraft;
        QuantificationDefinition saved = original with { Name = "Explicitly applied" };
        InputWorkbookLoadResult prepared = new(viewModel.Snapshot!, viewModel.Metadata!,
            new ColumnMappingSuggester().Suggest(viewModel.Metadata!));
        TaskCompletionSource<InputWorkbookLoadResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.NextLoad = (_, _, _) => release.Task;
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<bool> application = viewModel.ApplySavedDefinitionAsync(saved, cancellation.Token);
        Assert.True(viewModel.IsBusy);
        InputQuestionMappingViewModel selected = viewModel.Questions[8];

        viewModel.SelectedQuestion = selected;
        viewModel.PageSize = 3;
        viewModel.PageIndex = 0;
        Assert.Same(original, viewModel.DefinitionDraft);
        if (operation == "edit")
        {
            selected.DisplayName = "Newer input edit";
        }
        else if (operation == "cancel")
        {
            cancellation.Cancel();
        }

        QuantificationDefinition beforeCompletion = viewModel.DefinitionDraft;
        if (operation == "failure")
        {
            release.SetException(new IOException("Synthetic read failure."));
        }
        else
        {
            release.SetResult(prepared);
        }

        Assert.Equal(operation == "display", await application);
        Assert.False(viewModel.IsBusy);
        Assert.Same(selected, viewModel.SelectedQuestion);
        AssertPage(viewModel, 0, "1–3 / 9 件", viewModel.Questions.Take(3), false, true);
        if (operation == "display")
        {
            AssertCanonical(saved, viewModel.DefinitionDraft);
        }
        else
        {
            Assert.Same(beforeCompletion, viewModel.DefinitionDraft);
        }

        string? expectedError = operation switch
        {
            "cancel" => "SAVED_DEFINITION_CANCELLED",
            "failure" => "SAVED_DEFINITION_LOAD_FAILED",
            _ => null,
        };
        Assert.Equal(expectedError, viewModel.SavedDefinitionApplicationError?.Code);
        Assert.Equal(2, loader.LoadCount);
        Assert.Equal(bytes, File.ReadAllBytes(workbook.Path));
    }

    [AvaloniaFact]
    public void Real_Questions_binding_keeps_selection_through_pages_structure_and_clear() =>
        AssertBoundQuestions(useVisibleQuestions: false);

    [AvaloniaFact]
    public void Real_VisibleQuestions_binding_keeps_selection_through_pages_structure_and_clear() =>
        AssertBoundQuestions(useVisibleQuestions: true);

    [AvaloniaFact]
    public async Task Real_primary_ComboBox_updates_header_text_immediately_without_jumping_the_browsed_page()
    {
        foreach (int headerRow in new[] { 1, 2 })
        {
            using X02TemporaryWorkbook workbook = CreateWorkbook(headerRow: headerRow);
            byte[] bytes = File.ReadAllBytes(workbook.Path);
            InputViewModel viewModel = new() { HeaderRow = headerRow };
            await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
            InputQuestionMappingViewModel question = viewModel.Questions[0];
            question.SetSupportingColumn("C", true);
            question.QuestionText = "Manual text";
            viewModel.PageIndex = 2;
            QuantificationDefinition before = viewModel.DefinitionDraft;
            ComboBox primary = new() { DataContext = question };
            primary.Bind(ComboBox.ItemsSourceProperty,
                CompiledBinding.Create<InputQuestionMappingViewModel, ReadOnlyObservableCollection<string>>(
                    model => model.AvailableColumnNames, mode: BindingMode.OneWay));
            primary.Bind(ComboBox.SelectedItemProperty,
                CompiledBinding.Create<InputQuestionMappingViewModel, string>(
                    model => model.PrimarySourceColumn, mode: BindingMode.TwoWay));
            TextBlock text = new() { DataContext = question };
            text.Bind(TextBlock.TextProperty,
                CompiledBinding.Create<InputQuestionMappingViewModel, string>(model => model.QuestionText, mode: BindingMode.OneWay));
            TextBlock summary = new() { DataContext = viewModel };
            summary.Bind(TextBlock.TextProperty,
                CompiledBinding.Create<InputViewModel, string>(model => model.InputSummary, mode: BindingMode.OneWay));
            Window window = new() { Width = 640, Height = 360, Content = new StackPanel { Children = { primary, text, summary } } };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                primary.SetCurrentValue(ComboBox.SelectedItemProperty, "C");
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("C", question.PrimarySourceColumn);
                Assert.Equal("Report answer 3", text.Text);
                Assert.Equal(viewModel.InputSummary, summary.Text);
                Assert.Equal(2, viewModel.PageIndex);
                Assert.Same(question, viewModel.SelectedQuestion);
                Assert.DoesNotContain("C", viewModel.DefinitionDraft.Questions[0].SupportingSourceColumns);
                AssertCanonical(before with
                {
                    Questions = before.Questions.SetItem(0, before.Questions[0] with
                    {
                        PrimarySourceColumn = "C",
                        QuestionText = "Report answer 3",
                        SupportingSourceColumns = [],
                    }),
                }, viewModel.DefinitionDraft);
                primary.SetCurrentValue(ComboBox.SelectedItemProperty, "J");
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(string.Empty, text.Text);
                Assert.Equal(string.Empty, question.QuestionText);
                Assert.Equal(2, viewModel.PageIndex);
                Assert.Contains(viewModel.ValidationErrors, error => error.NodeId == question.Id && error.Field == "QuestionText");
                Assert.Equal(bytes, File.ReadAllBytes(workbook.Path));
            }
            finally
            {
                window.Close();
            }
        }
    }

    private static void AssertBoundQuestions(bool useVisibleQuestions)
    {
        InputViewModel viewModel = CreateInput();
        InputQuestionMappingViewModel selected = viewModel.Questions[4];
        viewModel.SelectedQuestion = selected;
        using BoundQuestionList bound = new(viewModel, useVisibleQuestions);
        bound.AssertSelection(selected);
        foreach (int page in new[] { 2, 0, 1 })
        {
            viewModel.PageIndex = page;
            bound.AssertSelection(selected);
            Assert.Equal(page, viewModel.PageIndex);
        }

        QuantificationDefinition before = viewModel.DefinitionDraft;
        selected.MoveUpCommand.Execute(null);
        bound.AssertSelection(selected);
        Assert.Equal(0, viewModel.PageIndex);
        AssertCanonical(before.MoveQuestion(4, 3), viewModel.DefinitionDraft);
        selected.MoveDownCommand.Execute(null);
        bound.AssertSelection(selected);
        Assert.Equal(1, viewModel.PageIndex);
        AssertCanonical(before, viewModel.DefinitionDraft);
        viewModel.PageSize = 3;
        bound.AssertSelection(selected);
        bound.List.SetCurrentValue(ListBox.SelectedItemProperty, viewModel.VisibleQuestions[0]);
        selected = viewModel.VisibleQuestions[0];
        bound.AssertSelection(selected);
        InputQuestionMappingViewModel neighbor = viewModel.Questions[viewModel.Questions.IndexOf(selected) + 1];
        selected.DeleteCommand.Execute(null);
        bound.AssertSelection(neighbor);
        viewModel.FilePath = "replacement.xlsx";
        bound.AssertSelection(null);
        Assert.Empty(bound.List.Items);
        Assert.Equal(0, viewModel.PageIndex);
        InputQuestionMappingViewModel added = viewModel.AddQuestion();
        bound.AssertSelection(added);
        bound.List.SetCurrentValue(ListBox.SelectedItemProperty, null);
        bound.AssertSelection(null);
    }

    private static InputViewModel CreateInput(int questionCount = 9)
    {
        InputViewModel viewModel = new();
        for (int index = 0; index < questionCount; index++)
        {
            viewModel.AddQuestion();
        }

        return viewModel;
    }

    private static X02TemporaryWorkbook CreateWorkbook(int questionCount = 9, int headerRow = 1) =>
        X02SyntheticWorkbookFactory.CreateSingleSheet("Responses", (uint)headerRow, (uint)(headerRow + 100),
            (uint)(questionCount + 1), [.. Enumerable.Range(1, questionCount)
                .Select(index => new X02Header((uint)index, $"Report answer {index}"))]);

    private static void SynchronizeFromDesign(InputViewModel viewModel, QuantificationDefinition incoming)
    {
        // Exercise the existing internal application boundary without adding a public test-only API.
        MethodInfo method = Assert.IsAssignableFrom<MethodInfo>(typeof(InputViewModel).GetMethod(
            "SynchronizeFromDesignDraft", BindingFlags.Instance | BindingFlags.NonPublic));
        method.Invoke(viewModel, [incoming]);
    }

    private static void AssertProperty(string name, Type type, bool writable)
    {
        PropertyInfo property = Assert.IsAssignableFrom<PropertyInfo>(typeof(InputViewModel).GetProperty(name));
        Assert.Equal(type, property.PropertyType);
        Assert.True(property.GetMethod!.IsPublic);
        Assert.Equal(writable, property.SetMethod?.IsPublic == true);
    }

    private static void AssertCanonical(QuantificationDefinition expected, QuantificationDefinition actual)
    {
        Assert.Equal(Serializer.Serialize(expected), Serializer.Serialize(actual));
        Assert.Equal(Serializer.ComputeSha256(expected), Serializer.ComputeSha256(actual));
    }

    private static void AssertPage(InputViewModel viewModel, int index, string summary,
        IEnumerable<InputQuestionMappingViewModel> expectedVisible, bool canPrevious, bool canNext)
    {
        Assert.Equal(index, viewModel.PageIndex);
        Assert.Equal(summary, viewModel.PageSummary);
        Assert.Equal(canPrevious, viewModel.PreviousPageCommand.CanExecute(null));
        Assert.Equal(canNext, viewModel.NextPageCommand.CanExecute(null));
        InputQuestionMappingViewModel[] expected = expectedVisible.ToArray();
        Assert.Equal(expected.Length, viewModel.VisibleQuestions.Count);
        for (int itemIndex = 0; itemIndex < expected.Length; itemIndex++)
        {
            Assert.Same(expected[itemIndex], viewModel.VisibleQuestions[itemIndex]);
            Assert.Contains(viewModel.Questions, item => ReferenceEquals(item, expected[itemIndex]));
        }

        if (viewModel.SelectedQuestion is { } selected)
        {
            Assert.Contains(viewModel.Questions, item => ReferenceEquals(item, selected));
        }
    }

    private sealed class BoundQuestionList : IDisposable
    {
        private readonly InputViewModel viewModel;
        private readonly bool useVisibleQuestions;
        private readonly Window window;

        public BoundQuestionList(InputViewModel viewModel, bool useVisibleQuestions)
        {
            this.viewModel = viewModel;
            this.useVisibleQuestions = useVisibleQuestions;
            List = new ListBox { DataContext = viewModel };
            CompiledBinding items = useVisibleQuestions
                ? CompiledBinding.Create<InputViewModel, ReadOnlyObservableCollection<InputQuestionMappingViewModel>>(
                    model => model.VisibleQuestions, mode: BindingMode.OneWay)
                : CompiledBinding.Create<InputViewModel, ReadOnlyObservableCollection<InputQuestionMappingViewModel>>(
                    model => model.Questions, mode: BindingMode.OneWay);
            List.Bind(ListBox.ItemsSourceProperty, items);
            List.Bind(ListBox.SelectedItemProperty,
                CompiledBinding.Create<InputViewModel, InputQuestionMappingViewModel?>(
                    model => model.SelectedQuestion, mode: BindingMode.TwoWay));
            // InputView still uses its old layout. T18/T13 consume this T09-compatible
            // settled-selection notification; this fixture is not a production-view test.
            viewModel.PropertyChanged += RefreshSelection;
            window = new Window { Width = 480, Height = 360, Content = List };
            window.Show();
        }

        public ListBox List { get; }

        public void AssertSelection(InputQuestionMappingViewModel? selected)
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Same(selected, viewModel.SelectedQuestion);
            Assert.Same(useVisibleQuestions ? viewModel.VisibleQuestions : viewModel.Questions, List.ItemsSource);
            InputQuestionMappingViewModel? displayed = selected is not null
                && (!useVisibleQuestions || viewModel.VisibleQuestions.Contains(selected)) ? selected : null;
            Assert.Same(displayed, List.SelectedItem);
            Assert.NotNull(BindingOperations.GetBindingExpressionBase(List, ListBox.SelectedItemProperty));
        }

        public void Dispose()
        {
            viewModel.PropertyChanged -= RefreshSelection;
            window.Close();
        }

        private void RefreshSelection(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(InputViewModel.SelectedQuestion))
            {
                BindingOperations.GetBindingExpressionBase(List, ListBox.SelectedItemProperty)?.UpdateTarget();
            }
        }
    }

    private sealed class ScriptedReadOnlyLoader : IInputWorkbookLoader
    {
        private readonly InputWorkbookLoader inner = new();

        public int LoadCount { get; private set; }

        public Func<string, uint, CancellationToken, Task<InputWorkbookLoadResult>>? NextLoad { get; set; }

        public Task<InputWorkbookLoadResult> LoadAsync(string filePath, uint headerRow, CancellationToken cancellationToken = default)
        {
            LoadCount++;
            Func<string, uint, CancellationToken, Task<InputWorkbookLoadResult>>? next = NextLoad;
            NextLoad = null;
            return next is null ? inner.LoadAsync(filePath, headerRow, cancellationToken) : next(filePath, headerRow, cancellationToken);
        }
    }
}