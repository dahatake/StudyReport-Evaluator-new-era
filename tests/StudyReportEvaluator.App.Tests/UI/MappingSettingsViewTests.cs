using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workbooks.Mapping;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

public sealed class MappingSettingsViewTests
{
    private static readonly CanonicalDefinitionSerializer Serializer = new();

    [AvaloniaFact]
    public void Parameterless_view_inherits_Input_without_creating_a_VM_loading_or_applying_launch_input()
    {
        MappingSettingsView unattached = new();
        Assert.Null(unattached.DataContext);
        Assert.Throws<InvalidOperationException>(() => unattached.ViewModel);
        Assert.Throws<ArgumentNullException>(() => new MappingSettingsView(null!));
        NeverLoadInput loader = new();
        InputViewModel input = new(loader);
        input.ApplyLaunchInput("synthetic-t13-never-open.xlsx");
        QuantificationDefinition before = input.DefinitionDraft;
        using ViewHarness harness = new(input, inheritDataContext: true);

        Assert.Same(input, harness.View.ViewModel);
        Assert.Same(input, harness.View.DataContext);
        Assert.Same(input.Questions, harness.Questions.ItemsSource);
        Assert.Null(harness.Questions.SelectedItem);
        Assert.Same(input.AddQuestionCommand, harness.Control<Button>("AddQuestionButton").Command);
        Assert.Same(input.ApplySuggestionsCommand, harness.Control<Button>("ApplySuggestionsButton").Command);
        Assert.False(harness.Control<Button>("AddQuestionButton").IsEffectivelyEnabled);
        Assert.False(harness.Control<Button>("ApplySuggestionsButton").IsEffectivelyEnabled);
        Assert.True(harness.Control<TextBlock>("NoQuestionMessage").IsEffectivelyVisible);
        Assert.True(harness.Control<TextBlock>("NoCandidateMessage").IsEffectivelyVisible);
        Assert.Same(harness.Questions, harness.Window.FocusManager?.GetFocusedElement());
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(harness.Questions, ComboBox.SelectedItemProperty));
        Assert.Equal(0, loader.CallCount);
        Assert.Same(before, input.DefinitionDraft);
        Assert.False(input.HasLoadedWorkbook);
        Assert.Null(harness.View.FindControl<Border>("EthicsWarningBanner"));
        AssertUniqueIds(harness.View);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_selected_question_disables_detail_CRUD_without_changing_the_loaded_draft(bool removeAll)
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook(3, 6);
        InputViewModel input = await LoadAsync(workbook);
        if (removeAll)
        {
            foreach (InputQuestionMappingViewModel question in input.Questions.ToArray())
            {
                input.DeleteQuestion(question.Id);
            }
        }

        input.SelectedQuestion = null;
        QuantificationDefinition before = input.DefinitionDraft;
        using ViewHarness harness = new(input);

        Assert.Null(input.SelectedQuestion);
        Assert.Null(harness.Questions.SelectedItem);
        Assert.Equal(removeAll ? 0 : 3, harness.Questions.Items.Count);
        Assert.True(harness.Control<TextBlock>("NoQuestionMessage").IsEffectivelyVisible);
        Assert.False(harness.Control<TextBox>("QuestionNameEditor").IsEffectivelyVisible);
        Assert.False(harness.Control<TextBox>("QuestionTextEditor").IsEffectivelyVisible);
        Assert.False(harness.Supports.IsEffectivelyEnabled);
        Assert.False(harness.Include.IsEffectivelyEnabled);
        foreach (string name in new[] { "DuplicateQuestionButton", "MoveQuestionUpButton", "MoveQuestionDownButton", "DeleteQuestionButton" })
        {
            Button button = harness.Control<Button>(name);
            Assert.Null(button.Command);
            Assert.False(button.IsEffectivelyEnabled);
        }

        Assert.True(harness.Control<Button>("AddQuestionButton").IsEffectivelyEnabled);
        Assert.Same(before, input.DefinitionDraft);
        AssertAccessibleTargetsFit(harness.View);
        AssertUniqueIds(harness.View);
    }

    [AvaloniaFact]
    public async Task Question_controls_edit_add_duplicate_reorder_delete_and_keep_original_IDs_and_clones()
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook(3, 6);
        InputViewModel input = await LoadAsync(workbook);
        InputQuestionMappingViewModel[] originals = input.Questions.ToArray();
        InputQuestionMappingViewModel selected = originals[1];
        QuantificationDefinition initial = input.DefinitionDraft;
        string initialHash = Serializer.ComputeSha256(initial);
        using ViewHarness harness = new(input);
        Select(harness.Questions, selected);
        SetText(harness.Control<TextBox>("QuestionNameEditor"), "同じ名前でもIDは不変");
        SetMainQuestionText(harness, "手動設問文\n二行目");
        Assert.Equal("同じ名前でもIDは不変", selected.DisplayName);
        Assert.Equal("手動設問文\n二行目", SelectedDefinition(input).QuestionText);
        Assert.Equal(selected.NameAutomationId, AutomationProperties.GetAutomationId(harness.Control<TextBox>("QuestionNameEditor")));
        Assert.NotSame(initial.Questions[1], SelectedDefinition(input));
        Assert.NotSame(initial.Questions[1].Evaluators[0], SelectedDefinition(input).Evaluators[0]);
        Assert.Equal(initialHash, Serializer.ComputeSha256(initial));

        Click(harness, harness.Control<Button>("AddQuestionButton"));
        InputQuestionMappingViewModel added = input.Questions[^1];
        Assert.Equal(4, input.Questions.Count);
        Assert.Same(selected, input.SelectedQuestion);
        Assert.Same(selected, harness.Questions.SelectedItem);
        QuestionDefinition beforeDuplicate = SelectedDefinition(input);
        Click(harness, harness.Control<Button>("DuplicateQuestionButton"));
        InputQuestionMappingViewModel copy = input.Questions[2];
        Assert.Equal(5, input.Questions.Count);
        Assert.NotEqual(selected.Id, copy.Id);
        Assert.Same(selected, harness.Questions.SelectedItem);
        Select(harness.Questions, copy);
        Assert.Equal(beforeDuplicate.DisplayName, copy.DisplayName);
        Assert.Equal(beforeDuplicate.QuestionText, copy.QuestionText);
        Assert.NotEqual(beforeDuplicate.Evaluators[0].Id, SelectedDefinition(input).Evaluators[0].Id);
        Assert.NotEqual(beforeDuplicate.Evaluators[0].Criteria[0].Id, SelectedDefinition(input).Evaluators[0].Criteria[0].Id);
        // Reusing a primary column in another question remains allowed.
        Assert.Equal(selected.PrimarySourceColumn, copy.PrimarySourceColumn);
        Assert.DoesNotContain(input.ValidationErrors, error => error.Code == "PRIMARY_COLUMN_REUSED");
        Click(harness, harness.Control<Button>("MoveQuestionUpButton"));
        Assert.Same(copy, input.Questions[1]);
        Assert.Same(copy, harness.Questions.SelectedItem);
        Click(harness, harness.Control<Button>("MoveQuestionDownButton"));
        Assert.Same(copy, input.Questions[2]);
        Assert.Equal($"InputQuestion-{copy.Id}-Delete", AutomationProperties.GetAutomationId(harness.Control<Button>("DeleteQuestionButton")));
        Click(harness, harness.Control<Button>("DeleteQuestionButton"));
        Assert.Same(originals[2], harness.Questions.SelectedItem);
        Assert.DoesNotContain(input.Questions, question => question.Id == copy.Id);
        Select(harness.Questions, added);
        Click(harness, harness.Control<Button>("DeleteQuestionButton"));
        Assert.Same(originals[2], harness.Questions.SelectedItem);
        Assert.Equal(originals, input.Questions);
        AssertQuestionUnchanged(initial, input.DefinitionDraft, originals[0].Id);
        AssertQuestionUnchanged(initial, input.DefinitionDraft, originals[2].Id);
        Assert.Equal(initialHash, Serializer.ComputeSha256(initial));

        while (input.Questions.Count > 0)
        {
            Select(harness.Questions, input.Questions[^1]);
            Click(harness, harness.Control<Button>("DeleteQuestionButton"));
        }

        Assert.Null(input.SelectedQuestion);
        Assert.Null(harness.Questions.SelectedItem);
        Assert.True(harness.Control<TextBlock>("NoQuestionMessage").IsEffectivelyVisible);
        Click(harness, harness.Control<Button>("AddQuestionButton"));
        Assert.Same(Assert.Single(input.Questions), harness.Questions.SelectedItem);
        AssertUniqueIds(harness.View);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Supporting_columns_add_remove_prevent_primary_duplicates_and_show_actual_available_titles(int headerRow)
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook(3, 6, headerRow);
        InputViewModel input = await LoadAsync(workbook, headerRow);
        InputQuestionMappingViewModel question = input.Questions[0];
        QuantificationDefinition initial = input.DefinitionDraft;
        using ViewHarness harness = new(input);
        Assert.Same(question.SupportingColumns, harness.Supports.ItemsSource);
        SelectColumn(harness, "B");
        SupportingColumnSelectionViewModel oldOption = Assert.IsType<SupportingColumnSelectionViewModel>(harness.Supports.SelectedItem);
        string actualTitle = input.AvailableColumns.Single(column => column.ColumnName == "B").DisplayText;
        Assert.Equal(actualTitle, harness.Control<TextBox>("AvailableColumnTitle").Text);
        Assert.True(harness.Include.IsEffectivelyEnabled);
        Assert.False(harness.Include.IsChecked);
        Click(harness, harness.Include);

        Assert.Equal(["B"], SelectedDefinition(input).SupportingSourceColumns);
        Assert.NotSame(oldOption, harness.Supports.SelectedItem);
        Assert.Equal("B", Assert.IsType<SupportingColumnSelectionViewModel>(harness.Supports.SelectedItem).ColumnName);
        Assert.True(harness.Include.IsChecked);
        Assert.Equal($"InputQuestion-{question.Id}-Support-B", AutomationProperties.GetAutomationId(harness.Include));
        harness.Include.SetCurrentValue(CheckBox.IsCheckedProperty, true);
        Render();
        Assert.Equal(["B"], SelectedDefinition(input).SupportingSourceColumns);
        Click(harness, harness.Include);
        Assert.Empty(SelectedDefinition(input).SupportingSourceColumns);
        SelectColumn(harness, "A");
        Assert.False(harness.Include.IsEffectivelyEnabled);
        Assert.False(harness.Include.IsChecked);

        // Bypassing the UI is still rejected by the unmodified validator.
        question.SetSupportingColumn("A", true);
        Render();
        InputValidationError error = input.ValidationErrors.First(item => item.Code == "PRIMARY_COLUMN_REUSED");
        Assert.False(input.CanContinue);
        Select(harness.Control<ComboBox>("MappingValidationErrors"), error);
        Assert.Equal($"{error.NodeId} / {error.Field}\n{error.Message}", harness.Control<TextBox>("MappingValidationDetail").Text);
        Assert.Same(input.ValidationErrors, harness.Control<ComboBox>("MappingValidationErrors").ItemsSource);
        question.SetSupportingColumn("A", false);
        SelectColumn(harness, "B");
        Click(harness, harness.Include);
        SetMainQuestionText(harness, "手動入力");
        question.PrimarySourceColumn = "B"; // The main screen owns this input.
        Render();

        Assert.Empty(SelectedDefinition(input).SupportingSourceColumns);
        Assert.False(harness.Include.IsEffectivelyEnabled);
        Assert.False(harness.Include.IsChecked);
        Assert.Equal("Report answer 2", harness.Control<TextBox>("QuestionTextEditor").Text);
        Assert.Equal("主回答列: B（主画面で変更）", harness.Control<TextBlock>("PrimaryColumnSummary").Text);
        Assert.Equal(actualTitle, harness.Control<TextBox>("AvailableColumnTitle").Text);
        Assert.DoesNotContain(harness.View.GetVisualDescendants().OfType<Control>(), control =>
            AutomationProperties.GetAutomationId(control) == question.PrimaryAutomationId);
        Assert.Single(harness.View.GetVisualDescendants().OfType<CheckBox>());
        question.PrimarySourceColumn = "F";
        Render();
        Assert.Equal(string.Empty, harness.Control<TextBox>("QuestionTextEditor").Text);
        Assert.False(input.CanContinue);
        SetMainQuestionText(harness, "空の見出しに対する手動設問");
        Assert.True(input.CanContinue);
        AssertQuestionUnchanged(initial, input.DefinitionDraft, input.Questions[1].Id);
        AssertQuestionUnchanged(initial, input.DefinitionDraft, input.Questions[2].Id);
        AssertUniqueIds(harness.View);
    }

    [AvaloniaFact]
    public async Task Question_text_is_read_only_one_way_and_tracks_main_input_edits_and_question_selection()
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook(3, 6);
        InputViewModel input = await LoadAsync(workbook);
        using ViewHarness harness = new(input);
        TextBox text = harness.Control<TextBox>("QuestionTextEditor");
        Assert.True(text.IsReadOnly);
        Assert.True(text.AcceptsReturn);
        Assert.False(text.AcceptsTab);
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, text.TextWrapping);
        Assert.Equal("設問文の全文・読取専用", AutomationProperties.GetName(text));
        Assert.Contains("入力画面", Assert.IsType<string>(ToolTip.GetTip(text)), StringComparison.Ordinal);
        Assert.Contains(harness.View.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "設問文（全文・読取専用）");
        const string manual = "入力画面だけで編集する設問文\n全文の終端 🧪";
        SetMainQuestionText(harness, manual);
        QuantificationDefinition before = input.DefinitionDraft;
        Assert.Equal(manual, text.Text);

        Assert.True(text.Focus(NavigationMethod.Tab));
        text.SelectionStart = 0;
        text.SelectionEnd = manual.Length;
        harness.Window.KeyTextInput("設定側からの編集は不可");
        Render();
        Assert.Equal(manual, text.Text);
        Assert.Equal(manual, SelectedDefinition(input).QuestionText);
        Assert.Same(before, input.DefinitionDraft);

        // IsReadOnly blocks keyboard edits; OneWay independently blocks source
        // writeback even when a caller changes the presentation property directly.
        text.SetCurrentValue(TextBox.TextProperty, "表示プロパティだけの変更");
        Render();
        Assert.Equal(manual, SelectedDefinition(input).QuestionText);
        Assert.Same(before, input.DefinitionDraft);
        SetMainQuestionText(harness, manual + "\n主画面で再編集");
        Assert.Equal(manual + "\n主画面で再編集", text.Text);
        QuantificationDefinition edited = input.DefinitionDraft;
        InputQuestionMappingViewModel other = input.Questions[1];
        Select(harness.Questions, other);
        Assert.Equal(other.QuestionText, text.Text);
        Assert.True(text.IsReadOnly);
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(text, TextBox.TextProperty));
        Assert.Same(edited, input.DefinitionDraft);
        AssertQuestionUnchanged(before, edited, other.Id);
    }

    [AvaloniaFact]
    public async Task Candidate_inspection_is_nonmutating_and_explicit_reapply_keeps_existing_whole_mapping_behavior()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        ScriptedLoader loader = new();
        InputViewModel input = await LoadAsync(workbook, loader: loader);
        InputQuestionMappingViewModel question = input.Questions[1];
        input.SelectedQuestion = question;
        question.DisplayName = "手動設定";
        question.QuestionText = "候補の参照では消えない設問文";
        question.SetSupportingColumn("H", true);
        QuantificationDefinition manual = input.DefinitionDraft;
        string manualHash = Serializer.ComputeSha256(manual);
        string[] previousIds = input.Questions.Select(item => item.Id).ToArray();
        using ViewHarness harness = new(input);
        Assert.Same(input.MappingSuggestions, harness.Candidates.ItemsSource);

        foreach (MappingSuggestionViewModel candidate in input.MappingSuggestions)
        {
            Select(harness.Candidates, candidate);
            Assert.Equal($"{candidate.RoleText}\n{candidate.HeaderText}\n{candidate.SupportText}", harness.Control<TextBox>("CandidateOverview").Text);
            Assert.Same(question, input.SelectedQuestion);
            Assert.Same(manual, input.DefinitionDraft);
            Assert.False(input.IsUsingSuggestedMapping);
        }

        // K is support-only: the existing command must still apply the entire set,
        // not turn the inspected candidate into a new primary mapping.
        Select(harness.Candidates, input.MappingSuggestions.Single(candidate => candidate.ColumnName == "K"));
        Button apply = harness.Control<Button>("ApplySuggestionsButton");
        Assert.Same(input.ApplySuggestionsCommand, apply.Command);
        Assert.Contains("候補一式", Assert.IsType<string>(apply.Content), StringComparison.Ordinal);
        Click(harness, apply);

        Assert.Equal(["F", "G", "H", "I", "J"], input.Questions.Select(item => item.PrimarySourceColumn));
        Assert.Equal(["K"], input.DefinitionDraft.Questions.Single(item => item.PrimarySourceColumn == "J").SupportingSourceColumns);
        Assert.All(input.Questions, item => Assert.DoesNotContain(item.Id, previousIds));
        Assert.Same(input.Questions[0], harness.Questions.SelectedItem);
        Assert.Equal("K", Assert.IsType<MappingSuggestionViewModel>(harness.Candidates.SelectedItem).ColumnName);
        Assert.Equal(2, input.FirstDataRow);
        Assert.Equal(531, input.LastDataRow);
        Assert.True(input.IsUsingSuggestedMapping);
        Assert.True(input.CanContinue);
        Assert.Equal(1, loader.CallCount);
        Assert.Equal(manualHash, Serializer.ComputeSha256(manual));
        QuantificationDefinition applied = input.DefinitionDraft;
        harness.Window.Content = null;
        Render();
        harness.Window.Content = harness.View;
        Render();
        Assert.Same(applied, input.DefinitionDraft);
        Assert.Equal(1, loader.CallCount);
        AssertUniqueIds(harness.View);
    }

    [AvaloniaFact]
    public async Task Missing_candidates_are_explained_without_hiding_manual_columns_or_inventing_a_suggestion()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Responses", 1, 101, 3, new X02Header(1, "Synthetic unclassified heading"));
        InputViewModel input = await LoadAsync(workbook);
        QuantificationDefinition before = input.DefinitionDraft;
        using ViewHarness harness = new(input);

        Assert.Empty(harness.Candidates.Items);
        Assert.Null(harness.Candidates.SelectedItem);
        Assert.True(harness.Control<TextBlock>("NoCandidateMessage").IsEffectivelyVisible);
        Assert.False(harness.Control<TextBox>("CandidateOverview").IsEffectivelyVisible);
        Assert.Equal(3, harness.Supports.Items.Count);
        SelectColumn(harness, "C");
        Assert.Equal("C", harness.Control<TextBox>("AvailableColumnTitle").Text);
        Assert.Same(before, input.DefinitionDraft);
        Click(harness, harness.Include);
        Assert.Equal(["C"], SelectedDefinition(input).SupportingSourceColumns);
        Assert.True(input.CanContinue);
    }

    [AvaloniaFact]
    public async Task Real_root_selection_binding_survives_paging_reorder_sync_explicit_null_and_same_reference_refresh()
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook();
        InputViewModel input = await LoadAsync(workbook);
        InputQuestionMappingViewModel selected = input.Questions[4];
        input.SelectedQuestion = selected;
        using ViewHarness harness = new(input);
        BindingExpressionBase? expression = BindingOperations.GetBindingExpressionBase(harness.Questions, ComboBox.SelectedItemProperty);
        Assert.NotNull(expression);
        QuantificationDefinition before = input.DefinitionDraft;

        input.PageIndex = 2;
        Render();
        Assert.Same(selected, harness.Questions.SelectedItem);
        Assert.Equal(2, input.PageIndex);
        Assert.Same(before, input.DefinitionDraft);
        input.PageSize = 3;
        Render();
        Assert.Same(selected, harness.Questions.SelectedItem);
        Click(harness, harness.Control<Button>("MoveQuestionUpButton"));
        Assert.Same(selected, harness.Questions.SelectedItem);
        Click(harness, harness.Control<Button>("MoveQuestionDownButton"));
        Assert.Same(selected, harness.Questions.SelectedItem);
        Assert.Equal(Serializer.ComputeSha256(before), Serializer.ComputeSha256(input.DefinitionDraft));

        QuantificationDefinition incoming = before.MoveQuestion(4, 0);
        incoming = incoming with { Questions = incoming.Questions.SetItem(0, incoming.Questions[0] with
        {
            DisplayName = "同期済み名称", QuestionText = "同期した設問文", SupportingSourceColumns = ["L"], Enabled = false,
        }) };
        SynchronizeFromDesign(input, incoming);
        Render();
        Assert.Same(selected, input.SelectedQuestion);
        Assert.Same(selected, harness.Questions.SelectedItem);
        Assert.Equal("同期済み名称", harness.Control<TextBox>("QuestionNameEditor").Text);
        Assert.Equal("同期した設問文", harness.Control<TextBox>("QuestionTextEditor").Text);
        Assert.Equal(Serializer.ComputeSha256(incoming), Serializer.ComputeSha256(input.DefinitionDraft));
        Assert.False(selected.Enabled);
        SelectColumn(harness, "L");
        Assert.True(harness.Include.IsChecked);
        Select(harness.Questions, null);
        Assert.Null(input.SelectedQuestion);
        SynchronizeFromDesign(input, input.DefinitionDraft with { Revision = "still-no-selection" });
        Render();
        Assert.Null(harness.Questions.SelectedItem);
        Assert.True(harness.Control<TextBlock>("NoQuestionMessage").IsEffectivelyVisible);
        Select(harness.Questions, selected);
        Assert.Same(selected, input.SelectedQuestion);
        Assert.Same(expression, BindingOperations.GetBindingExpressionBase(harness.Questions, ComboBox.SelectedItemProperty));
    }

    [AvaloniaFact]
    public async Task Saved_application_refreshes_actual_column_titles_and_keeps_saved_text_IDs_deep_clones_and_selection()
    {
        const string header = "行2の実際の見出し・合成値";
        using X02TemporaryWorkbook workbook = CreateWorkbook(headerRow: 2, lastHeader: header);
        InputViewModel input = await LoadAsync(workbook, headerRow: 2);
        InputQuestionMappingViewModel selected = input.Questions[8];
        selected.QuestionText = "保存した手動設問文\n見出しへ戻さない";
        selected.SetSupportingColumn("L", true);
        input.SelectedQuestion = selected;
        QuantificationDefinition saved = input.DefinitionDraft;
        string savedHash = Serializer.ComputeSha256(saved);
        input.HeaderRow = 1;
        await input.RefreshHeaderAsync(TestContext.Current.CancellationToken);
        using ViewHarness harness = new(input);
        SelectColumn(harness, "L");
        Assert.Equal("L", harness.Control<TextBox>("AvailableColumnTitle").Text);
        Assert.Empty(harness.Candidates.Items);

        Assert.True(await input.ApplySavedDefinitionAsync(saved, TestContext.Current.CancellationToken));
        Render();

        Assert.Same(selected, harness.Questions.SelectedItem);
        Assert.Same(selected, input.SelectedQuestion);
        Assert.Equal(2, input.PageIndex);
        Assert.Equal("保存した手動設問文\n見出しへ戻さない", harness.Control<TextBox>("QuestionTextEditor").Text);
        Assert.Equal("L · " + header, harness.Control<TextBox>("AvailableColumnTitle").Text);
        Assert.Equal(input.AvailableColumns.Single(column => column.ColumnName == "L").DisplayText,
            harness.Control<TextBox>("AvailableColumnTitle").Text);
        Assert.True(harness.Include.IsChecked);
        Assert.Equal(savedHash, Serializer.ComputeSha256(input.DefinitionDraft));
        Assert.Equal(savedHash, Serializer.ComputeSha256(saved));
        Assert.NotSame(saved, input.DefinitionDraft);
        Assert.NotSame(saved.Questions[8], SelectedDefinition(input));
        Assert.NotSame(saved.Questions[8].Evaluators[0], SelectedDefinition(input).Evaluators[0]);
        Assert.NotSame(saved.Questions[8].Evaluators[0].Criteria[0], SelectedDefinition(input).Evaluators[0].Criteria[0]);
        Assert.False(input.IsUsingSuggestedMapping);
        Assert.Equal(9, harness.Candidates.Items.Count);
        Assert.True(input.CanContinue);
    }

    [AvaloniaTheory]
    [InlineData("display")]
    [InlineData("edit")]
    [InlineData("cancel")]
    [InlineData("failure")]
    public async Task Bound_view_preserves_T04_application_commit_conflict_cancellation_and_failure_boundaries(string operation)
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook();
        ScriptedLoader loader = new();
        InputViewModel input = await LoadAsync(workbook, loader: loader);
        using ViewHarness harness = new(input);
        QuantificationDefinition original = input.DefinitionDraft;
        QuantificationDefinition saved = original with { Name = "明示適用された定義" };
        InputWorkbookLoadResult prepared = new(input.Snapshot!, input.Metadata!, new ColumnMappingSuggester().Suggest(input.Metadata!));
        TaskCompletionSource<InputWorkbookLoadResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.NextLoad = (_, _, _) => release.Task;
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<bool> application = input.ApplySavedDefinitionAsync(saved, cancellation.Token);
        Render();
        Assert.True(input.IsBusy);
        Assert.False(harness.Control<Button>("ApplySuggestionsButton").IsEffectivelyEnabled);
        InputQuestionMappingViewModel selected = input.Questions[8];
        Select(harness.Questions, selected);
        SelectColumn(harness, "K");
        Select(harness.Candidates, input.MappingSuggestions[^1]);
        Assert.Same(original, input.DefinitionDraft);
        if (operation == "edit")
        {
            SetText(harness.Control<TextBox>("QuestionNameEditor"), "適用待ち中の新しい編集");
        }
        else if (operation == "cancel")
        {
            cancellation.Cancel();
        }

        QuantificationDefinition beforeCompletion = input.DefinitionDraft;
        object metadata = input.Metadata!;
        object snapshot = input.Snapshot!;
        if (operation == "failure")
        {
            release.SetException(new IOException("Synthetic T13 read failure."));
        }
        else
        {
            release.SetResult(prepared);
        }

        Assert.Equal(operation == "display", await application);
        Render();
        Assert.False(input.IsBusy);
        Assert.Same(selected, input.SelectedQuestion);
        Assert.Same(selected, harness.Questions.SelectedItem);
        Assert.Equal("K", Assert.IsType<SupportingColumnSelectionViewModel>(harness.Supports.SelectedItem).ColumnName);
        Assert.Equal("I", Assert.IsType<MappingSuggestionViewModel>(harness.Candidates.SelectedItem).ColumnName);
        if (operation == "display")
        {
            Assert.Equal(Serializer.ComputeSha256(saved), Serializer.ComputeSha256(input.DefinitionDraft));
        }
        else
        {
            Assert.Same(beforeCompletion, input.DefinitionDraft);
            Assert.Same(metadata, input.Metadata);
            Assert.Same(snapshot, input.Snapshot);
        }

        Assert.Equal(operation switch
        {
            "cancel" => "SAVED_DEFINITION_CANCELLED",
            "failure" => "SAVED_DEFINITION_LOAD_FAILED",
            _ => null,
        }, input.SavedDefinitionApplicationError?.Code);
        Assert.Equal(2, loader.CallCount);
    }

    [AvaloniaFact]
    public async Task Lifecycle_replaces_subscriptions_once_and_ignores_old_VM_and_detached_updates()
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook(3, 6);
        InputViewModel first = await LoadAsync(workbook);
        InputViewModel second = await LoadAsync(workbook);
        using ViewHarness harness = new(first);
        Assert.Equal(1, ViewSubscriptionCount(first, harness.View));
        for (int cycle = 0; cycle < 3; cycle++)
        {
            harness.View.DataContext = second;
            Render();
            Assert.Equal(0, ViewSubscriptionCount(first, harness.View));
            Assert.Equal(1, ViewSubscriptionCount(second, harness.View));
            Assert.Same(second.SelectedQuestion, harness.Questions.SelectedItem);
            harness.View.DataContext = first;
            Render();
            Assert.Equal(0, ViewSubscriptionCount(second, harness.View));
            Assert.Equal(1, ViewSubscriptionCount(first, harness.View));
        }

        harness.Window.Content = null;
        Render();
        Assert.Equal(0, ViewSubscriptionCount(first, harness.View));
        harness.View.DataContext = second;
        second.SelectedQuestion = second.Questions[2];
        Assert.Equal(0, ViewSubscriptionCount(second, harness.View));
        harness.Window.Content = harness.View;
        Render();
        Assert.Equal(1, ViewSubscriptionCount(second, harness.View));
        Assert.Same(second.SelectedQuestion, harness.Questions.SelectedItem);
        first.SelectedQuestion = first.Questions[1];
        first.Questions[0].DisplayName = "旧VMの編集";
        Render();
        Assert.Same(second, harness.View.ViewModel);
        Assert.Same(second.SelectedQuestion, harness.Questions.SelectedItem);
        harness.Window.Close();
        Assert.Equal(0, ViewSubscriptionCount(second, harness.View));
    }

    [AvaloniaFact]
    public async Task Embedded_category_inherits_input_and_does_not_steal_parent_focus_or_change_question()
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook();
        InputViewModel input = await LoadAsync(workbook);
        input.SelectedQuestion = input.Questions[7];
        QuantificationDefinition before = input.DefinitionDraft;
        MappingSettingsView view = new();
        Button parentAction = new() { Content = "カテゴリへ戻る" };
        Grid host = new() { RowDefinitions = new RowDefinitions("44,*"), Children = { parentAction } };
        Grid.SetRow(view, 1);
        Window window = new()
        {
            Width = 950, Height = 494, WindowDecorations = WindowDecorations.None, DataContext = input, Content = host,
        };
        try
        {
            window.Show();
            Render();
            Assert.True(parentAction.Focus(NavigationMethod.Tab));
            host.Children.Add(view);
            Render();
            Assert.Same(input, view.ViewModel);
            Assert.Same(input.SelectedQuestion, view.FindControl<ComboBox>("QuestionSelector")!.SelectedItem);
            Assert.Same(parentAction, window.FocusManager?.GetFocusedElement());
            Assert.Same(before, input.DefinitionDraft);
            Assert.InRange(view.Bounds.Width, 949d, 951d);
            Assert.InRange(view.Bounds.Height, 449d, 451d);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Finite_950_by_450_content_keeps_all_fixed_targets_inside_and_only_local_lists_or_text_scroll()
    {
        string longHeader = string.Join('\n', Enumerable.Range(1, 120).Select(index => $"補助列見出しの行 {index}"));
        using X02TemporaryWorkbook workbook = CreateWorkbook(9, 128, lastHeader: longHeader);
        InputViewModel input = await LoadAsync(workbook);
        input.Questions[0].QuestionText = string.Join('\n', Enumerable.Range(1, 200).Select(index => $"手動設問の行 {index}"));
        using ViewHarness harness = new(input);
        SelectColumn(harness, input.AvailableColumns[^1].ColumnName);

        Assert.Equal(new Size(950d, 450d), harness.Window.ClientSize);
        Assert.InRange(harness.View.Bounds.Width, 949d, 951d);
        Assert.InRange(harness.View.Bounds.Height, 449d, 451d);
        AssertAccessibleTargetsFit(harness.View);
        AssertUniqueIds(harness.View);
        Assert.Equal(128, harness.Supports.Items.Count);
        Assert.Contains(harness.Supports.GetVisualDescendants(), item => item is VirtualizingStackPanel);
        Assert.InRange(harness.Supports.GetVisualDescendants().OfType<ListBoxItem>().Count(), 1, 12);
        Assert.DoesNotContain(harness.Supports.GetVisualDescendants(), item => item is CheckBox);
        Assert.True(harness.Supports.Bounds.Height >= 44d);
        Control[] localOwners = [harness.Supports, harness.Control<TextBox>("QuestionNameEditor"),
            harness.Control<TextBox>("QuestionTextEditor"), harness.Control<TextBox>("AvailableColumnTitle"),
            harness.Control<TextBox>("CandidateOverview"), harness.Control<TextBox>("MappingValidationDetail")];
        foreach (ScrollViewer scroll in harness.View.GetVisualDescendants().OfType<ScrollViewer>())
        {
            Assert.True(double.IsFinite(scroll.Viewport.Width) && double.IsFinite(scroll.Viewport.Height));
            if (scroll.Extent.Height > scroll.Viewport.Height + 1d)
            {
                Assert.Contains(scroll.GetVisualAncestors(), ancestor => localOwners.Any(owner => ReferenceEquals(owner, ancestor)));
            }
        }

        TextBox title = harness.Control<TextBox>("AvailableColumnTitle");
        Assert.Equal(input.AvailableColumns[^1].DisplayText, title.Text);
        Assert.True(title.IsReadOnly);
        Assert.True(title.Focus(NavigationMethod.Tab));
        Press(harness.Window, Key.End, RawInputModifiers.Control);
        Assert.Equal(title.Text!.Length, title.CaretIndex);
        Assert.True(Assert.Single(title.GetVisualDescendants().OfType<ScrollViewer>()).Offset.Y > 0d);
        TextBox question = harness.Control<TextBox>("QuestionTextEditor");
        Assert.True(question.IsReadOnly);
        Assert.Equal(input.SelectedQuestion!.QuestionText, question.Text);
        Assert.True(question.Focus(NavigationMethod.Tab));
        Press(harness.Window, Key.End, RawInputModifiers.Control);
        Assert.Equal(input.SelectedQuestion!.QuestionText.Length, question.CaretIndex);
        Assert.True(Assert.Single(question.GetVisualDescendants().OfType<ScrollViewer>()).Offset.Y > 0d);
        AssertAccessibleTargetsFit(harness.View);
    }

    [AvaloniaFact]
    public async Task Every_question_candidate_and_support_is_reachable_without_truncation_or_changed_draft()
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook(33, 80);
        InputViewModel input = await LoadAsync(workbook);
        QuantificationDefinition before = input.DefinitionDraft;
        using ViewHarness harness = new(input);
        Assert.Equal(33, harness.Questions.Items.Count);
        Assert.Equal(33, harness.Candidates.Items.Count);
        Assert.Equal(80, harness.Supports.Items.Count);
        foreach (InputQuestionMappingViewModel question in input.Questions)
        {
            Select(harness.Questions, question);
            Assert.Same(question, input.SelectedQuestion);
            Assert.Same(question.SupportingColumns, harness.Supports.ItemsSource);
        }

        foreach (MappingSuggestionViewModel candidate in input.MappingSuggestions)
        {
            Select(harness.Candidates, candidate);
            Assert.Contains(candidate.HeaderText, harness.Control<TextBox>("CandidateOverview").Text ?? string.Empty, StringComparison.Ordinal);
        }

        foreach (SourceColumnOption option in input.AvailableColumns)
        {
            SelectColumn(harness, option.ColumnName);
            Assert.Equal(option.DisplayText, harness.Control<TextBox>("AvailableColumnTitle").Text);
        }

        Assert.Same(before, input.DefinitionDraft);
        Assert.Same(input.Questions[^1], harness.Questions.SelectedItem);
        Assert.Equal(4, input.PageSize); // Settings does not repurpose the main list's capacity.
        Assert.True(input.CanContinue);
        AssertUniqueIds(harness.View);
    }

    [AvaloniaFact]
    public async Task Keyboard_Tab_ShiftTab_Enter_Space_and_list_End_keep_selection_and_edit_the_current_target()
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook(3, 32);
        InputViewModel input = await LoadAsync(workbook);
        using ViewHarness harness = new(input);
        Assert.True(harness.Questions.Focus(NavigationMethod.Tab), FocusDiagnostic(harness.Window, harness.Questions));
        AssertFocused(harness.Window, harness.Questions);
        Press(harness.Window, Key.Tab);
        AssertFocused(harness.Window, harness.Control<Button>("AddQuestionButton"));
        Press(harness.Window, Key.Tab, RawInputModifiers.Shift);
        AssertFocused(harness.Window, harness.Questions);
        Press(harness.Window, Key.Tab);
        Press(harness.Window, Key.Enter);
        Assert.Equal(4, input.Questions.Count);
        InputQuestionMappingViewModel question = input.SelectedQuestion!;
        SelectColumn(harness, "B");
        Assert.True(harness.Include.Focus(NavigationMethod.Tab), FocusDiagnostic(harness.Window, harness.Include));
        AssertFocused(harness.Window, harness.Include);
        Press(harness.Window, Key.Space);
        Assert.Equal(["B"], SelectedDefinition(input).SupportingSourceColumns);
        AssertFocused(harness.Window, harness.Include);
        Press(harness.Window, Key.Space);
        Assert.Empty(SelectedDefinition(input).SupportingSourceColumns);
        AssertFocused(harness.Window, harness.Include);

        // ListBox itself is not focusable: real Tab navigation enters its selected
        // ListBoxItem. Do not force Focusable=true or bypass the keyboard route.
        Assert.False(harness.Supports.Focusable);
        Press(harness.Window, Key.Tab, RawInputModifiers.Shift);
        AssertSupportingColumnFocused(harness, "B");
        Press(harness.Window, Key.End);
        AssertSupportingColumnFocused(harness, input.AvailableColumns[^1].ColumnName);
        Assert.Equal(input.AvailableColumns[^1].ColumnName,
            Assert.IsType<SupportingColumnSelectionViewModel>(harness.Supports.SelectedItem).ColumnName);
        Assert.Equal(input.AvailableColumns[^1].DisplayText, harness.Control<TextBox>("AvailableColumnTitle").Text);
        Press(harness.Window, Key.Tab);
        AssertFocused(harness.Window, harness.Include);
        Press(harness.Window, Key.Space);
        Assert.Equal([input.AvailableColumns[^1].ColumnName], SelectedDefinition(input).SupportingSourceColumns);
        AssertFocused(harness.Window, harness.Include);

        // The commit recreated the options. Shift+Tab must find the new selected
        // last-row container, not a stale/recycled item or the first visible row.
        Press(harness.Window, Key.Tab);
        AssertFocused(harness.Window, harness.Control<TextBox>("AvailableColumnTitle"));
        Press(harness.Window, Key.Tab, RawInputModifiers.Shift);
        AssertFocused(harness.Window, harness.Include);
        Press(harness.Window, Key.Tab, RawInputModifiers.Shift);
        AssertSupportingColumnFocused(harness, input.AvailableColumns[^1].ColumnName);
        Press(harness.Window, Key.Home);
        AssertSupportingColumnFocused(harness, question.PrimarySourceColumn);
        Assert.False(harness.Include.IsEffectivelyEnabled);
        Press(harness.Window, Key.Tab);
        AssertFocused(harness.Window, harness.Control<TextBox>("AvailableColumnTitle"));
        Press(harness.Window, Key.Tab, RawInputModifiers.Shift);
        AssertSupportingColumnFocused(harness, question.PrimarySourceColumn);
        Assert.Equal([input.AvailableColumns[^1].ColumnName], SelectedDefinition(input).SupportingSourceColumns);
        Assert.Same(question, input.SelectedQuestion);
        Assert.Same(question, harness.Questions.SelectedItem);
        TextBox name = harness.Control<TextBox>("QuestionNameEditor");
        Assert.True(name.Focus(NavigationMethod.Tab), FocusDiagnostic(harness.Window, name));
        AssertFocused(harness.Window, name);
        Press(harness.Window, Key.Tab);
        AssertFocused(harness.Window, harness.Control<TextBox>("QuestionTextEditor"));
        Press(harness.Window, Key.Tab, RawInputModifiers.Shift);
        AssertFocused(harness.Window, name);
        AssertUniqueIds(harness.View);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Embedded_tab_order_is_local_skips_hidden_details_and_exits_to_the_parent(bool clearSelection)
    {
        using X02TemporaryWorkbook workbook = CreateWorkbook(3, 32);
        InputViewModel input = await LoadAsync(workbook);
        if (clearSelection)
        {
            input.SelectedQuestion = null;
        }

        InputQuestionMappingViewModel? selection = input.SelectedQuestion;
        QuantificationDefinition before = input.DefinitionDraft;
        MappingSettingsView view = new(input) { TabIndex = 1 };
        Button previous = new() { Name = "BeforeMapping", Content = "前の操作", TabIndex = 0 };
        Button next = new() { Name = "AfterMapping", Content = "次の操作", TabIndex = 2 };
        Grid.SetRow(view, 1);
        Grid.SetRow(next, 2);
        Window window = new()
        {
            Width = 950, Height = 538, WindowDecorations = WindowDecorations.None,
            Content = new Grid
            {
                RowDefinitions = new RowDefinitions("44,*,44"), Children = { previous, view, next },
            },
        };
        try
        {
            window.Show();
            Render();
            Assert.True(previous.Focus(NavigationMethod.Tab), FocusDiagnostic(window, previous));
            ComboBox questions = view.FindControl<ComboBox>("QuestionSelector")!;
            Control[] targets = clearSelection
                ? [questions, view.FindControl<Button>("AddQuestionButton")!, view.FindControl<ComboBox>("CandidateSelector")!]
                : [questions, view.FindControl<Button>("AddQuestionButton")!, view.FindControl<Button>("DuplicateQuestionButton")!,
                    view.FindControl<Button>("MoveQuestionDownButton")!];
            foreach (Control target in targets)
            {
                Press(window, Key.Tab);
                AssertFocused(window, target);
            }

            TextBox last = view.FindControl<TextBox>("MappingValidationDetail")!;
            Assert.True(last.Focus(NavigationMethod.Tab), FocusDiagnostic(window, last));
            Press(window, Key.Tab);
            AssertFocused(window, next);
            Press(window, Key.Tab, RawInputModifiers.Shift);
            AssertFocused(window, last);
            Assert.True(questions.Focus(NavigationMethod.Tab), FocusDiagnostic(window, questions));
            Press(window, Key.Tab, RawInputModifiers.Shift);
            AssertFocused(window, previous);
            Assert.Same(selection, input.SelectedQuestion);
            Assert.Same(selection, questions.SelectedItem);
            Assert.Same(before, input.DefinitionDraft);
        }
        finally
        {
            window.Close();
        }
    }

    private static X02TemporaryWorkbook CreateWorkbook(int questionCount = 9, int columnCount = 12,
        int headerRow = 1, string? lastHeader = null)
    {
        List<X02Header> headers = [.. Enumerable.Range(1, questionCount)
            .Select(index => new X02Header((uint)index, $"Report answer {index}"))];
        if (lastHeader is not null)
        {
            headers.Add(new X02Header((uint)columnCount, lastHeader));
        }

        return X02SyntheticWorkbookFactory.CreateSingleSheet("Responses", (uint)headerRow,
            (uint)(headerRow + 100), (uint)columnCount, [.. headers]);
    }

    private static async Task<InputViewModel> LoadAsync(X02TemporaryWorkbook workbook, int headerRow = 1,
        IInputWorkbookLoader? loader = null)
    {
        InputViewModel input = loader is null ? new() : new(loader);
        input.HeaderRow = headerRow;
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        Assert.True(input.HasLoadedWorkbook);
        return input;
    }

    private static QuestionDefinition SelectedDefinition(InputViewModel input) =>
        input.DefinitionDraft.Questions.Single(question => question.Id == input.SelectedQuestion!.Id);

    private static void AssertQuestionUnchanged(QuantificationDefinition before, QuantificationDefinition after, string id) =>
        Assert.Equal(Serializer.Serialize(before with { Questions = [before.Questions.Single(question => question.Id == id)] }),
            Serializer.Serialize(after with { Questions = [after.Questions.Single(question => question.Id == id)] }));

    private static void SynchronizeFromDesign(InputViewModel input, QuantificationDefinition incoming)
    {
        MethodInfo method = Assert.IsAssignableFrom<MethodInfo>(typeof(InputViewModel).GetMethod(
            "SynchronizeFromDesignDraft", BindingFlags.Instance | BindingFlags.NonPublic));
        method.Invoke(input, [incoming]);
    }

    private static void Select(ComboBox selector, object? item)
    {
        selector.SetCurrentValue(ComboBox.SelectedItemProperty, item);
        Render();
        Assert.Same(item, selector.SelectedItem);
    }

    private static void SelectColumn(ViewHarness harness, string name)
    {
        InputQuestionMappingViewModel question = harness.View.ViewModel.SelectedQuestion!;
        SupportingColumnSelectionViewModel column = question.SupportingColumns.Single(item => item.ColumnName == name);
        harness.Supports.SetCurrentValue(ListBox.SelectedItemProperty, column);
        harness.Supports.ScrollIntoView(question.SupportingColumns.IndexOf(column));
        Render();
        Assert.Same(column, harness.Supports.SelectedItem);
    }

    private static void SetText(TextBox box, string value)
    {
        Assert.NotNull(BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty));
        box.SetCurrentValue(TextBox.TextProperty, value);
        Render();
        Assert.Equal(value, box.Text);
    }

    private static void SetMainQuestionText(ViewHarness harness, string value)
    {
        // Preserve the original mapping/CRUD assertions while exercising the sole
        // editable question-text surface, rather than writing a read-only preview.
        InputViewModel input = harness.View.ViewModel;
        InputQuestionMappingViewModel selected = input.SelectedQuestion!;
        InputView main = new(input);
        Window window = new()
        {
            Width = 950, Height = 450, WindowDecorations = WindowDecorations.None, Content = main,
        };
        try
        {
            window.Show();
            Render();
            TextBox editor = Assert.IsType<TextBox>(main.FindControl<TextBox>("QuestionTextEditor"));
            Assert.Same(selected, editor.DataContext);
            Assert.False(editor.IsReadOnly);
            SetText(editor, value);
            Assert.Equal(value, SelectedDefinition(input).QuestionText);
            Assert.Equal(value, harness.Control<TextBox>("QuestionTextEditor").Text);
            Assert.True(harness.Control<TextBox>("QuestionTextEditor").IsReadOnly);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Click(ViewHarness harness, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.IsEffectivelyEnabled);
        AssertFits(control, harness.View);
        Point position = control.TranslatePoint(new Point(control.Bounds.Width / 2d, control.Bounds.Height / 2d), harness.Window)!.Value;
        harness.Window.MouseDown(position, MouseButton.Left, RawInputModifiers.None);
        harness.Window.MouseUp(position, MouseButton.Left, RawInputModifiers.None);
        Render();
    }

    private static void AssertSupportingColumnFocused(ViewHarness harness, string columnName)
    {
        SupportingColumnSelectionViewModel selected = Assert.IsType<SupportingColumnSelectionViewModel>(harness.Supports.SelectedItem);
        Assert.Equal(columnName, selected.ColumnName);
        ListBoxItem container = Assert.IsType<ListBoxItem>(harness.Supports.ContainerFromIndex(harness.Supports.SelectedIndex));
        Assert.Same(selected, container.DataContext);
        Assert.True(container.IsSelected, FocusDiagnostic(harness.Window, container));
        AssertFocused(harness.Window, container);
    }

    private static void AssertFocused(Window window, Control expected) =>
        Assert.True(ReferenceEquals(expected, window.FocusManager?.GetFocusedElement()) && expected.IsFocused,
            FocusDiagnostic(window, expected));

    private static string FocusDiagnostic(Window window, Control expected) =>
        $"Expected focus: {DescribeFocus(expected)}; actual focus: {DescribeFocus(window.FocusManager?.GetFocusedElement())}.";

    private static string DescribeFocus(IInputElement? element) => element is Control control
        ? $"{control.GetType().Name}(Name={control.Name ?? "<none>"}, Id={AutomationProperties.GetAutomationId(control) ?? "<none>"}, "
            + $"Focused={control.IsFocused}, Focusable={control.Focusable}, Enabled={control.IsEffectivelyEnabled}, Visible={control.IsEffectivelyVisible})"
        : element?.GetType().Name ?? "<none>";

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physical = key switch
        {
            Key.Tab => PhysicalKey.Tab,
            Key.Enter => PhysicalKey.Enter,
            Key.Space => PhysicalKey.Space,
            Key.Home => PhysicalKey.Home,
            Key.End => PhysicalKey.End,
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
        Render();
    }

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertFits(Control control, Control root)
    {
        Point origin = control.TranslatePoint(default, root)!.Value;
        Assert.True(control.Bounds.Width > 0d && control.Bounds.Height > 0d);
        Assert.True(origin.X >= -1d && origin.Y >= -1d
            && origin.X + control.Bounds.Width <= root.Bounds.Width + 1d
            && origin.Y + control.Bounds.Height <= root.Bounds.Height + 1d,
            $"{AutomationProperties.GetAutomationId(control)} must fit in the finite T13 content slot.");
    }

    private static void AssertAccessibleTargetsFit(Control root)
    {
        TemplatedControl[] controls = root.GetVisualDescendants().OfType<TemplatedControl>()
            .Where(control => control.IsEffectivelyVisible && control is Button or ComboBox or CheckBox or ListBox or TextBox
                && !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(control))).ToArray();
        Assert.NotEmpty(controls);
        foreach (TemplatedControl control in controls)
        {
            Assert.True(control.MinWidth >= 44d && control.MinHeight >= 44d);
            Assert.True(control.Bounds.Width >= 44d && control.Bounds.Height >= 44d);
            Assert.True(control.FontSize >= 14d);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(control)?.ToString()));
            AssertFits(control, root);
        }
    }

    private static void AssertUniqueIds(Control root)
    {
        string[] ids = root.GetVisualDescendants().OfType<Control>().Prepend(root)
            .Select(AutomationProperties.GetAutomationId).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray();
        Assert.NotEmpty(ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static int ViewSubscriptionCount(UiObservableObject model, MappingSettingsView view)
    {
        FieldInfo field = Assert.IsAssignableFrom<FieldInfo>(typeof(UiObservableObject).GetField(
            nameof(INotifyPropertyChanged.PropertyChanged), BindingFlags.Instance | BindingFlags.NonPublic));
        return (field.GetValue(model) as MulticastDelegate)?.GetInvocationList()
            .Count(handler => ReferenceEquals(handler.Target, view)) ?? 0;
    }

    private sealed class ViewHarness : IDisposable
    {
        public ViewHarness(InputViewModel model, bool inheritDataContext = false)
        {
            View = inheritDataContext ? new MappingSettingsView() : new MappingSettingsView(model);
            Window = new Window
            {
                Width = 950, Height = 450, WindowDecorations = WindowDecorations.None, DataContext = model, Content = View,
            };
            Window.Show();
            Render();
        }

        public MappingSettingsView View { get; }
        public Window Window { get; }
        public ComboBox Questions => Control<ComboBox>("QuestionSelector");
        public ComboBox Candidates => Control<ComboBox>("CandidateSelector");
        public ListBox Supports => Control<ListBox>("SupportingColumnList");
        public CheckBox Include => Control<CheckBox>("IncludeSupportingColumnCheckBox");
        public T Control<T>(string name) where T : Avalonia.Controls.Control => Assert.IsType<T>(View.FindControl<T>(name));
        public void Dispose() => Window.Close();
    }

    private sealed class NeverLoadInput : IInputWorkbookLoader
    {
        public int CallCount { get; private set; }
        public Task<InputWorkbookLoadResult> LoadAsync(string filePath, uint headerRow, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("Mapping settings must not load workbooks.");
        }
    }

    private sealed class ScriptedLoader : IInputWorkbookLoader
    {
        private readonly InputWorkbookLoader inner = new();
        public int CallCount { get; private set; }
        public Func<string, uint, CancellationToken, Task<InputWorkbookLoadResult>>? NextLoad { get; set; }
        public Task<InputWorkbookLoadResult> LoadAsync(string filePath, uint headerRow, CancellationToken cancellationToken = default)
        {
            CallCount++;
            var next = NextLoad;
            NextLoad = null;
            return next is null ? inner.LoadAsync(filePath, headerRow, cancellationToken) : next(filePath, headerRow, cancellationToken);
        }
    }
}