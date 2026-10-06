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
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-001 (AC-001), FR-005 (AC-005, AC-006)
public sealed class InputViewTests
{
    [Fact]
    public async Task Sample_like_workbook_loads_asynchronously_and_exposes_overridable_F_through_K_suggestions()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        CountingInputLoader loader = new();
        InputViewModel viewModel = new(loader);

        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);

        Assert.Equal(1, loader.LoadCount);
        Assert.True(viewModel.HasLoadedWorkbook);
        Assert.NotNull(viewModel.Snapshot);
        Assert.NotNull(viewModel.Metadata);
        Assert.Equal("Original", viewModel.SelectedSheet);
        Assert.Equal(1, viewModel.HeaderRow);
        Assert.Equal(2, viewModel.FirstDataRow);
        Assert.Equal(531, viewModel.LastDataRow);
        Assert.Equal(["F", "G", "H", "I", "J", "K"], viewModel.MappingSuggestions.Select(item => item.ColumnName));
        Assert.Equal(["F", "G", "H", "I", "J"], viewModel.Questions.Select(item => item.PrimarySourceColumn));

        MappingSuggestionViewModel studentPrompt = Assert.Single(
            viewModel.MappingSuggestions,
            item => item.ColumnName == "J");
        Assert.True(studentPrompt.IsStudentPromptPrimaryCandidate);
        Assert.Equal("K", studentPrompt.SuggestedSupportingColumns);
        InputQuestionMappingViewModel promptQuestion = Assert.Single(
            viewModel.Questions,
            item => item.PrimarySourceColumn == "J");
        Assert.Equal("K", Assert.Single(
            promptQuestion.SupportingColumns,
            item => item.IsSelected).ColumnName);
        Assert.Equal(EvaluatorType.CustomPrompt, Assert.Single(
            viewModel.DefinitionDraft.Questions,
            item => item.Id == promptQuestion.Id).Evaluators[0].Type);
        Assert.True(viewModel.IsUsingSuggestedMapping);
        Assert.True(viewModel.CanContinue);
        Assert.Empty(viewModel.ValidationErrors);

        QuantificationDefinition suggestedDraft = viewModel.DefinitionDraft;
        InputQuestionMappingViewModel first = viewModel.Questions[0];
        first.PrimarySourceColumn = "G";
        first.SetSupportingColumn("H", selected: true);
        viewModel.FirstDataRow = 3;
        viewModel.LastDataRow = 500;

        Assert.NotSame(suggestedDraft, viewModel.DefinitionDraft);
        Assert.Equal("F", suggestedDraft.Questions[0].PrimarySourceColumn);
        Assert.Equal("G", viewModel.DefinitionDraft.Questions[0].PrimarySourceColumn);
        Assert.Contains("H", viewModel.DefinitionDraft.Questions[0].SupportingSourceColumns);
        Assert.Equal(3, viewModel.DefinitionDraft.FirstDataRow);
        Assert.Equal(500, viewModel.DefinitionDraft.LastDataRow);
        Assert.False(viewModel.IsUsingSuggestedMapping);
        Assert.True(viewModel.CanContinue);
        Assert.True(viewModel.TryCreateDesignDefinition(out QuantificationDefinition? designSeed));
        Assert.NotNull(designSeed);
        Assert.NotSame(viewModel.DefinitionDraft, designSeed);
        Assert.Equal("G", designSeed!.Questions[0].PrimarySourceColumn);

        first.SetSupportingColumn("G", selected: true);
        Assert.False(viewModel.CanContinue);
        Assert.Contains(viewModel.ValidationErrors, error => error.Code == "PRIMARY_COLUMN_REUSED");
        first.SetSupportingColumn("G", selected: false);
        Assert.True(viewModel.CanContinue);

        first.SetSupportingColumn("H", selected: true);
        first.PrimarySourceColumn = "H";
        Assert.DoesNotContain("H", viewModel.DefinitionDraft.Questions[0].SupportingSourceColumns);
        Assert.False(Assert.Single(
            first.SupportingColumns,
            column => column.ColumnName == "H").IsSelected);
        Assert.True(viewModel.CanContinue);

        Assert.DoesNotContain(workbook.Path, viewModel.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.All(
            viewModel.ValidationErrors,
            error => Assert.DoesNotContain(workbook.Path, error.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Changing_primary_column_replaces_manual_question_text_and_removes_duplicate_supporting_column()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputQuestionMappingViewModel question = viewModel.Questions[0];
        SourceColumnOption selectedColumn = Assert.Single(
            viewModel.AvailableColumns,
            option => option.ColumnName == "G");

        question.QuestionText = "MANUAL-QUESTION-TEXT";
        question.SetSupportingColumn("G", selected: true);
        QuestionDefinition before = Assert.Single(
            viewModel.DefinitionDraft.Questions,
            item => item.Id == question.Id);

        question.PrimarySourceColumn = "G";

        QuestionDefinition updated = Assert.Single(
            viewModel.DefinitionDraft.Questions,
            item => item.Id == question.Id);
        Assert.Equal(selectedColumn.HeaderText, question.QuestionText);
        Assert.Equal(selectedColumn.HeaderText, updated.QuestionText);
        Assert.NotEqual("MANUAL-QUESTION-TEXT", updated.QuestionText);
        Assert.Equal("G", updated.PrimarySourceColumn);
        Assert.DoesNotContain("G", updated.SupportingSourceColumns, StringComparer.OrdinalIgnoreCase);
        Assert.False(Assert.Single(
            question.SupportingColumns,
            column => column.ColumnName == "G").IsSelected);
        Assert.Equal(before.Id, updated.Id);
        Assert.Equal(before.DisplayName, updated.DisplayName);
        Assert.Equal(before.Points, updated.Points);
        Assert.Equal(before.Enabled, updated.Enabled);
        Assert.Equal(before.Evaluators.Select(evaluator => evaluator.Id), updated.Evaluators.Select(evaluator => evaluator.Id));
        Assert.Equal(
            before.SpecialEvaluations.Select(special => special.Id),
            updated.SpecialEvaluations.Select(special => special.Id));
        Assert.True(viewModel.CanContinue);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Changing_primary_column_uses_the_loaded_question_text_row_for_supported_form_profiles(
        int questionTextRow)
    {
        await AssertPrimaryColumnQuestionTextAsync(
            X02SyntheticWorkbookFactory.CreateMicrosoftFormsLikeProfile((uint)questionTextRow),
            questionTextRow,
            initialColumn: "F",
            selectedColumn: "I",
            expectedQuestionText: "Report answer 2");
        await AssertPrimaryColumnQuestionTextAsync(
            X02SyntheticWorkbookFactory.CreateGoogleFormsLikeProfile((uint)questionTextRow),
            questionTextRow,
            initialColumn: "C",
            selectedColumn: "E",
            expectedQuestionText: "Report answer 2");
    }

    private static async Task AssertPrimaryColumnQuestionTextAsync(
        X02TemporaryWorkbook workbook,
        int questionTextRow,
        string initialColumn,
        string selectedColumn,
        string expectedQuestionText)
    {
        using (workbook)
        {
            InputViewModel viewModel = new()
            {
                HeaderRow = questionTextRow,
            };
            await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
            InputQuestionMappingViewModel question = Assert.Single(
                viewModel.Questions,
                item => item.PrimarySourceColumn == initialColumn);

            question.PrimarySourceColumn = selectedColumn;

            Assert.Equal((uint)questionTextRow, viewModel.Metadata!.HeaderRowNumber);
            Assert.Equal(expectedQuestionText, question.QuestionText);
            Assert.Equal(
                expectedQuestionText,
                Assert.Single(viewModel.DefinitionDraft.Questions, item => item.Id == question.Id).QuestionText);
            Assert.True(viewModel.CanContinue);
        }
    }

    [Fact]
    public async Task Selecting_a_column_without_a_header_clears_question_text_and_requires_manual_input()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Responses",
            headerRow: 1,
            lastRow: 4,
            lastColumn: 3,
            new X02Header(2, "Report answer"));
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputQuestionMappingViewModel question = Assert.Single(viewModel.Questions);
        Assert.Equal(string.Empty, Assert.Single(
            viewModel.AvailableColumns,
            option => option.ColumnName == "A").HeaderText);
        question.QuestionText = "MANUAL-QUESTION-TEXT";

        question.PrimarySourceColumn = "A";

        Assert.Equal(string.Empty, question.QuestionText);
        Assert.Equal(
            string.Empty,
            Assert.Single(viewModel.DefinitionDraft.Questions, item => item.Id == question.Id).QuestionText);
        InputValidationError error = Assert.Single(
            viewModel.ValidationErrors,
            item => item.Code == "REQUIRED"
                && item.NodeId == question.Id
                && item.Field == "QuestionText");
        Assert.Equal("REQUIRED", error.Code);
        Assert.False(viewModel.CanContinue);
    }

    [Fact]
    public async Task Changing_primary_column_while_header_metadata_is_stale_preserves_question_text()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputQuestionMappingViewModel question = viewModel.Questions[0];
        const string ManualQuestionText = "MANUAL-QUESTION-TEXT";
        question.QuestionText = ManualQuestionText;
        string staleHeaderText = Assert.Single(
            viewModel.AvailableColumns,
            option => option.ColumnName == "G").HeaderText;
        Assert.NotEqual(ManualQuestionText, staleHeaderText);

        viewModel.HeaderRow = 2;
        question.PrimarySourceColumn = "G";

        QuestionDefinition updated = Assert.Single(
            viewModel.DefinitionDraft.Questions,
            item => item.Id == question.Id);
        Assert.Equal("G", updated.PrimarySourceColumn);
        Assert.Equal(ManualQuestionText, question.QuestionText);
        Assert.Equal(ManualQuestionText, updated.QuestionText);
        Assert.DoesNotContain(
            viewModel.ValidationErrors,
            error => error.Code == "REQUIRED" && error.Field == "QuestionText");
        Assert.Contains(viewModel.ValidationErrors, error => error.Code == "HEADER_METADATA_MISMATCH");
        Assert.False(viewModel.CanContinue);
    }

    [AvaloniaFact]
    public async Task Selecting_primary_column_in_the_view_updates_the_visible_question_text()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputQuestionMappingViewModel question = viewModel.Questions[0];
        (string Id, string PrimarySourceColumn, string QuestionText)[] otherQuestionsBefore =
            viewModel.DefinitionDraft.Questions
                .Skip(1)
                .Select(item => (item.Id, item.PrimarySourceColumn, item.QuestionText))
                .ToArray();
        InputView view = new(viewModel);
        Window window = new()
        {
            Width = 1180,
            Height = 820,
            Content = view,
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            ComboBox primaryColumn = Assert.Single(
                view.GetVisualDescendants().OfType<ComboBox>(),
                comboBox => AutomationProperties.GetAutomationId(comboBox) == question.PrimaryAutomationId);
            TextBox questionText = Assert.Single(
                view.GetVisualDescendants().OfType<TextBox>(),
                textBox => ReferenceEquals(textBox.DataContext, question)
                    && AutomationProperties.GetName(textBox) == "評価する設問 text");
            const string ExpectedQuestionText = "質問 1 の学生プロンプト";
            Assert.False(questionText.IsReadOnly);
            questionText.SetCurrentValue(TextBox.TextProperty, "主入力で手動編集した設問文\n二行目");
            Render();
            Assert.Equal(questionText.Text, question.QuestionText);
            Assert.Equal("主入力で手動編集した設問文\n二行目",
                Assert.Single(viewModel.DefinitionDraft.Questions, item => item.Id == question.Id).QuestionText);

            primaryColumn.SelectedItem = "G";
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.Equal("G", question.PrimarySourceColumn);
            Assert.Equal(ExpectedQuestionText, questionText.Text);
            Assert.Equal(
                ExpectedQuestionText,
                Assert.Single(viewModel.DefinitionDraft.Questions, item => item.Id == question.Id).QuestionText);
            Assert.True(questionText.Focus(NavigationMethod.Tab));
            Press(window, Key.End, RawInputModifiers.Control);
            window.KeyTextInput("\n自動反映後にも手動追記");
            Render();
            Assert.Equal(ExpectedQuestionText + "\n自動反映後にも手動追記", questionText.Text);
            Assert.Equal(questionText.Text, question.QuestionText);
            Assert.Equal(questionText.Text,
                Assert.Single(viewModel.DefinitionDraft.Questions, item => item.Id == question.Id).QuestionText);
            Assert.Equal(
                otherQuestionsBefore,
                viewModel.DefinitionDraft.Questions
                    .Skip(1)
                    .Select(item => (item.Id, item.PrimarySourceColumn, item.QuestionText))
                    .ToArray());
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task Refreshing_headers_preserves_the_selected_sheet_and_manual_mapping()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        viewModel.SelectedSheet = "Final";
        InputQuestionMappingViewModel question = viewModel.Questions[0];
        question.DisplayName = "Manually named question";
        question.QuestionText = "Manually edited question";
        question.Weight = 7m;
        question.SetSupportingColumn("B", selected: true);
        viewModel.FirstDataRow = 3;
        viewModel.LastDataRow = 100;
        QuantificationDefinition before = viewModel.DefinitionDraft;

        await viewModel.RefreshHeaderAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Final", viewModel.SelectedSheet);
        Assert.Equal(before.FirstDataRow, viewModel.FirstDataRow);
        Assert.Equal(before.LastDataRow, viewModel.LastDataRow);
        Assert.Equal(before.Questions.Select(item => item.Id), viewModel.Questions.Select(item => item.Id));
        QuestionDefinition updated = viewModel.DefinitionDraft.Questions[0];
        Assert.Equal(before.Questions[0].DisplayName, updated.DisplayName);
        Assert.Equal(before.Questions[0].QuestionText, updated.QuestionText);
        Assert.Equal(7m, updated.Points);
        Assert.Equal(before.Questions[0].SupportingSourceColumns, updated.SupportingSourceColumns);
        Assert.Equal(before.Questions[0].Evaluators, updated.Evaluators);
        Assert.False(viewModel.IsUsingSuggestedMapping);
    }

    [Fact]
    public async Task Loading_or_adding_a_question_with_an_empty_header_does_not_invent_question_text()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Responses", 1, 4, 2);
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);

        Assert.Equal(string.Empty, Assert.Single(viewModel.Questions).QuestionText);
        InputQuestionMappingViewModel added = viewModel.AddQuestion();
        Assert.Equal(string.Empty, added.QuestionText);
        Assert.Contains(viewModel.ValidationErrors, error =>
            error.Code == "REQUIRED" && error.NodeId == added.Id && error.Field == "QuestionText");
        Assert.False(viewModel.CanContinue);
    }

    [Fact]
    public async Task Adding_a_question_does_not_copy_a_stale_header()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Responses", 1, 4, 2, new X02Header(1, "Report answer from row 1"));
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        viewModel.HeaderRow = 2;

        InputQuestionMappingViewModel question = viewModel.AddQuestion();

        Assert.Equal(string.Empty, question.QuestionText);
        Assert.Contains(viewModel.ValidationErrors, error => error.Code == "HEADER_METADATA_MISMATCH");
    }

    [AvaloniaFact]
    public async Task Refreshing_headers_in_the_bound_view_preserves_edited_question_fields()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputView view = new(viewModel);
        Window window = new() { Width = 1180, Height = 820, Content = view };
        try
        {
            window.Show();
            Render();
            InputQuestionMappingViewModel question = viewModel.Questions[0];
            question.PrimarySourceColumn = "G";
            question.SetSupportingColumn("H", true);
            question.QuestionText = "Manually edited visible text";
            Render();

            await viewModel.RefreshHeaderAsync(TestContext.Current.CancellationToken);
            Render();

            Assert.Same(question, viewModel.Questions[0]);
            Assert.Equal("G", question.PrimarySourceColumn);
            Assert.Equal("Manually edited visible text", question.QuestionText);
            Assert.Equal("H", Assert.Single(question.SupportingColumns, column => column.IsSelected).ColumnName);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Long_worksheet_and_supporting_names_remain_identifiable_in_their_current_editing_surfaces()
    {
        string sheetPrefix = new('表', 30);
        string header = new('補', 256);
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.Create(
            new X02SheetSpec(sheetPrefix + "A", 1, 4, 2, [new X02Header(1, "Report answer"), new X02Header(2, header)]),
            new X02SheetSpec(sheetPrefix + "B", 1, 4, 2, [new X02Header(1, "Report answer"), new X02Header(2, header)]));
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputView view = new(viewModel);
        Window window = new() { Width = 1024, Height = 720, Content = view };
        try
        {
            window.Show();
            Render();
            ComboBox sheets = Required<ComboBox>(view, "WorksheetComboBox");
            sheets.SelectedItem = viewModel.Worksheets[1];
            Render();
            Assert.Equal(sheetPrefix + "B", viewModel.SelectedSheet);
            Assert.Equal(viewModel.SelectedWorksheetChoice!.DisplayText, ToolTip.GetTip(sheets));
            Assert.Contains(sheets.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Text == viewModel.SelectedWorksheetChoice.DisplayText
                && Equals(ToolTip.GetTip(text), viewModel.SelectedWorksheetChoice.DisplayText));
            Assert.Contains(sheetPrefix + "B", Required<TextBlock>(view, "InputCountSummary").Text, StringComparison.Ordinal);
            AssertInputControlsContained(view);

            // Supplementary-column editing moved, not disappeared. Exercise its actual
            // replacement list, full-text inspection field, and live selection checkbox.
            InputQuestionMappingViewModel question = viewModel.SelectedQuestion!;
            MappingSettingsView mapping = new(viewModel);
            window.Content = mapping;
            Render();
            ListBox columns = Required<ListBox>(mapping, "SupportingColumnList");
            Assert.Equal(question.SupportingAutomationId, AutomationProperties.GetAutomationId(columns));
            columns.SelectedItem = Assert.Single(question.SupportingColumns, column => column.ColumnName == "B");
            Render();
            TextBox fullHeader = Required<TextBox>(mapping, "AvailableColumnTitle");
            Assert.Equal("B · " + header, fullHeader.Text);
            Assert.True(fullHeader.IsReadOnly);
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, fullHeader.TextWrapping);
            CheckBox supporting = Required<CheckBox>(mapping, "IncludeSupportingColumnCheckBox");
            Assert.True(supporting.IsEnabled);
            supporting.IsChecked = true;
            Render();
            Assert.True(Assert.Single(question.SupportingColumns, column => column.ColumnName == "B").IsSelected);
            Assert.Same(question, Required<ComboBox>(mapping, "QuestionSelector").SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task Editing_the_file_path_invalidates_loaded_metadata_snapshot_and_suggestions()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        Assert.True(viewModel.CanContinue);

        viewModel.FilePath = workbook.Path + ".replacement.xlsx";

        Assert.False(viewModel.HasLoadedWorkbook);
        Assert.Null(viewModel.Metadata);
        Assert.Null(viewModel.Snapshot);
        Assert.Empty(viewModel.Worksheets);
        Assert.Empty(viewModel.MappingSuggestions);
        Assert.Empty(viewModel.Questions);
        Assert.False(viewModel.IsUsingSuggestedMapping);
        Assert.False(viewModel.CanContinue);
        Assert.Contains(viewModel.ValidationErrors, error => error.Code == "INPUT_WORKBOOK_REQUIRED");
    }

    [Fact]
    public async Task Editing_the_file_path_supersedes_an_in_flight_load_without_adopting_stale_results()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        DelayedInputLoader loader = new();
        InputViewModel viewModel = new(loader);
        viewModel.FilePath = workbook.Path;
        Task load = viewModel.LoadAsync(TestContext.Current.CancellationToken);
        await loader.Started;
        Assert.True(viewModel.IsBusy);

        viewModel.FilePath = workbook.Path + ".replacement.xlsx";
        loader.Release();
        await load;

        Assert.False(viewModel.IsBusy);
        Assert.False(viewModel.HasLoadedWorkbook);
        Assert.Null(viewModel.Metadata);
        Assert.Null(viewModel.Snapshot);
        Assert.False(viewModel.CanContinue);
    }

    [AvaloniaFact]
    public async Task Direct_path_and_load_button_use_the_existing_loader_only_after_explicit_activation()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        CountingInputLoader loader = new();
        InputViewModel input = new(loader);
        ScriptedInputWorkbookPicker picker = new();
        InputView view = new(input, picker);
        Window window = new() { Width = 950, Height = 450, Content = view };
        try
        {
            window.Show();
            Render();
            Required<TextBox>(view, "FilePathTextBox").Text = workbook.Path;
            Render();
            Assert.Equal(workbook.Path, input.FilePath);
            Assert.Equal(0, loader.LoadCount);
            Assert.False(input.HasLoadedWorkbook);
            Click(window, Required<Button>(view, "LoadFileButton"));
            Assert.Equal(1, loader.LoadCount);
            Assert.NotNull(loader.LastLoad);
            await loader.LastLoad;
            Render();
            Assert.True(input.HasLoadedWorkbook);
            Assert.True(input.CanContinue);
            Assert.Equal(0, picker.CallCount);
            Assert.Equal(input.InputSummary, Required<TextBlock>(view, "InputCountSummary").Text);
            AssertInputControlsContained(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Native_picker_selection_uses_the_same_read_only_loader_and_cancel_preserves_state()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        CountingInputLoader loader = new();
        InputViewModel viewModel = new(loader);
        ScriptedInputWorkbookPicker picker = new(workbook.Path, null);
        InputView view = new(viewModel, picker);
        Window window = new() { Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            await view.PickFileAsync();

            Assert.Equal(Path.GetFullPath(workbook.Path), Path.GetFullPath(viewModel.FilePath));
            Assert.True(viewModel.HasLoadedWorkbook);
            Assert.True(viewModel.CanContinue);
            Assert.Equal(1, loader.LoadCount);
            InputSnapshot? snapshot = viewModel.Snapshot;
            WorkbookMetadata? metadata = viewModel.Metadata;
            InputQuestionMappingViewModel selected = viewModel.SelectedQuestion!;
            selected.QuestionText = "cancel 後も保持する設問文";
            QuantificationDefinition draft = viewModel.DefinitionDraft;

            await view.PickFileAsync();

            Assert.Equal(2, picker.CallCount);
            Assert.Equal(1, loader.LoadCount);
            Assert.Same(snapshot, viewModel.Snapshot);
            Assert.Same(metadata, viewModel.Metadata);
            Assert.Same(selected, viewModel.SelectedQuestion);
            Assert.Same(draft, viewModel.DefinitionDraft);
            Assert.Equal("cancel 後も保持する設問文", selected.QuestionText);
            Assert.Equal(workbook.Path, viewModel.FilePath);
            Button pick = Required<Button>(view, "PickFileButton");
            Assert.True(pick.IsEnabled);
            Assert.Equal("PickInputWorkbook", AutomationProperties.GetAutomationId(pick));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Question_text_row_choices_are_closed_to_one_or_two()
    {
        InputViewModel viewModel = new();

        Assert.Equal([1, 2], viewModel.HeaderRowOptions);
        viewModel.HeaderRow = 2;
        Assert.Equal(2, viewModel.HeaderRow);
        Assert.Throws<ArgumentOutOfRangeException>(() => viewModel.HeaderRow = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => viewModel.HeaderRow = 3);
        Assert.Equal(2, viewModel.HeaderRow);
    }

    [Fact]
    public async Task Changing_question_text_row_preserves_manual_points_and_requires_explicit_header_reload()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        decimal[] points = viewModel.DefinitionDraft.Questions.Select(question => question.Points).ToArray();

        viewModel.HeaderRow = 2;

        Assert.Equal(points, viewModel.DefinitionDraft.Questions.Select(question => question.Points));
        Assert.False(viewModel.CanContinue);
        Assert.Contains(viewModel.ValidationErrors, error => error.Code == "HEADER_METADATA_MISMATCH");
    }

    [AvaloniaFact]
    public async Task Picker_local_path_unavailable_is_distinguished_from_cancel_without_disclosing_content()
    {
        InputViewModel viewModel = new();
        InputView view = new(viewModel, new ThrowingInputWorkbookPicker());
        Window window = new() { Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            await view.PickFileAsync();

            TextBlock status = Required<TextBlock>(view, "PickerStatusMessage");
            Assert.True(status.IsVisible);
            Assert.Contains("pathを直接入力", status.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("PRIVATE", status.Text, StringComparison.Ordinal);
            Assert.Equal(string.Empty, viewModel.FilePath);
            Assert.False(viewModel.HasLoadedWorkbook);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task Question_mapping_add_copy_reorder_disable_delete_always_creates_a_new_draft()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);

        QuantificationDefinition beforeAdd = viewModel.DefinitionDraft;
        InputQuestionMappingViewModel added = viewModel.AddQuestion();
        Assert.NotSame(beforeAdd, viewModel.DefinitionDraft);
        Assert.Contains(viewModel.Questions, item => item.Id == added.Id);

        QuantificationDefinition beforeCopy = viewModel.DefinitionDraft;
        InputQuestionMappingViewModel copy = viewModel.DuplicateQuestion(added.Id);
        Assert.NotSame(beforeCopy, viewModel.DefinitionDraft);
        Assert.NotEqual(added.Id, copy.Id);
        Assert.NotEqual(
            Assert.Single(beforeCopy.Questions, item => item.Id == added.Id).Evaluators[0].Id,
            Assert.Single(viewModel.DefinitionDraft.Questions, item => item.Id == copy.Id).Evaluators[0].Id);

        viewModel.MoveQuestionUp(copy.Id);
        int copyIndex = viewModel.Questions.IndexOf(copy);
        Assert.True(copyIndex >= 0);
        Assert.Equal(copy.Id, viewModel.DefinitionDraft.Questions[copyIndex].Id);

        copy.Enabled = false;
        Assert.False(Assert.Single(viewModel.DefinitionDraft.Questions, item => item.Id == copy.Id).Enabled);
        Assert.Contains(viewModel.Questions, item => item.Id == copy.Id);
        viewModel.DeleteQuestion(copy.Id);
        Assert.DoesNotContain(viewModel.Questions, item => item.Id == copy.Id);
        Assert.DoesNotContain(viewModel.DefinitionDraft.Questions, item => item.Id == copy.Id);
        Assert.True(viewModel.CanContinue);
    }

    [Fact]
    public async Task Rejected_input_is_a_blocking_technical_error_without_path_or_content_disclosure()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-U03-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "PRIVATE-CANARY.csv");
        await File.WriteAllTextAsync(
            path,
            "PRIVATE-CONTENT-CANARY",
            TestContext.Current.CancellationToken);
        try
        {
            InputViewModel viewModel = new();

            await viewModel.SetFilePathAsync(path, TestContext.Current.CancellationToken);

            Assert.False(viewModel.HasLoadedWorkbook);
            Assert.False(viewModel.CanContinue);
            InputValidationError error = Assert.Single(viewModel.ValidationErrors);
            Assert.Equal("CommaSeparatedValues", error.Code);
            Assert.DoesNotContain(path, error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PRIVATE-CANARY", error.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("PRIVATE-CONTENT-CANARY", error.ToString(), StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => viewModel.CreateDesignDefinition());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Input_contract_has_no_warning_acknowledgement_or_native_dialog_dependency()
    {
        string[] prohibitedTerms =
        [
            "Warning",
            "Ethics",
            "Acknowledge",
            "Consent",
            "Dismiss",
            "OpenFileDialog",
            "StorageProvider",
        ];
        MemberInfo[] members = typeof(InputViewModel).GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(
            members,
            member => prohibitedTerms.Any(term => member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        Assert.NotNull(typeof(InputViewModel).GetConstructor([typeof(IInputWorkbookLoader)]));
        Assert.NotNull(typeof(InputViewModel).GetMethod(nameof(InputViewModel.SetFilePath)));
        Assert.NotNull(typeof(InputViewModel).GetMethod(nameof(InputViewModel.SetFilePathAsync)));
        Assert.NotNull(typeof(InputView).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(InputView).GetConstructor([typeof(InputViewModel)]));
        Assert.NotNull(typeof(InputView).GetConstructor([typeof(InputViewModel), typeof(IInputWorkbookPicker)]));
        Assert.Equal(typeof(Task), typeof(InputView).GetMethod(nameof(InputView.PickFileAsync))!.ReturnType);
    }

    [AvaloniaFact]
    public async Task Standalone_input_view_is_labelled_keyboard_focusable_paged_and_contained_at_two_hundred_percent()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputView view = new(viewModel);
        Window window = new() { Width = 950, Height = 450, Content = view };

        try
        {
            window.Show();
            window.SetRenderScaling(2d);
            Render();
            window.UpdateLayout();

            TextBox filePath = Required<TextBox>(view, "FilePathTextBox");
            Button load = Required<Button>(view, "LoadFileButton");
            Button pick = Required<Button>(view, "PickFileButton");
            ComboBox headerRow = Required<ComboBox>(view, "HeaderRowComboBox");
            ListBox questions = Required<ListBox>(view, "InputQuestionList");
            Border validation = Required<Border>(view, "InputValidationSummary");
            ComboBox selector = Required<ComboBox>(view, "QuestionSelector");
            ComboBox primaryColumn = Required<ComboBox>(view, "PrimaryColumnComboBox");
            TextBox questionText = Required<TextBox>(view, "QuestionTextEditor");
            InputQuestionMappingViewModel firstQuestion = viewModel.SelectedQuestion!;

            Assert.Same(viewModel, view.DataContext);
            Assert.Equal(2d, window.RenderScaling);
            Assert.Equal(new Thickness(0), view.Margin);
            Assert.Equal("InputFilePath", AutomationProperties.GetAutomationId(filePath));
            Assert.Equal("LoadInputWorkbook", AutomationProperties.GetAutomationId(load));
            Assert.Equal("PickInputWorkbook", AutomationProperties.GetAutomationId(pick));
            Assert.Equal("InputHeaderRow", AutomationProperties.GetAutomationId(headerRow));
            Assert.Equal([1, 2], headerRow.Items.Cast<int>());
            Assert.Equal("InputQuestionMappings", AutomationProperties.GetAutomationId(questions));
            Assert.Equal("InputValidationSummary", AutomationProperties.GetAutomationId(validation));
            Assert.Equal(firstQuestion.PrimaryAutomationId, AutomationProperties.GetAutomationId(primaryColumn));
            Assert.Equal($"InputQuestion-{firstQuestion.Id}-Text", AutomationProperties.GetAutomationId(questionText));
            Assert.Same(viewModel.VisibleQuestions, questions.ItemsSource);
            Assert.Same(firstQuestion, questions.SelectedItem);
            Assert.NotNull(BindingOperations.GetBindingExpressionBase(questions, ListBox.SelectedItemProperty));
            Assert.NotNull(BindingOperations.GetBindingExpressionBase(selector, ComboBox.SelectedItemProperty));
            Assert.Same(filePath, window.FocusManager?.GetFocusedElement());

            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "ファイル path（標準 .xlsx）");
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "主回答列");
            Assert.Contains(view.GetVisualDescendants(), descendant => descendant is VirtualizingStackPanel);
            Assert.Null(view.FindControl<Border>("EthicsWarningBanner"));
            Assert.Null(view.FindControl<ScrollViewer>("InputScrollViewer"));
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(), control =>
                AutomationProperties.GetAutomationId(control) is "InputMappingSuggestions" or "ApplyInputSuggestions" or "AddInputQuestion"
                || AutomationProperties.GetAutomationId(control) == firstQuestion.SupportingAutomationId
                || AutomationProperties.GetAutomationId(control) == firstQuestion.NameAutomationId);
            AssertInputControlsContained(view);
            AssertPageCapacityAndRows(view);

            Press(window, Key.Tab);
            Assert.Same(pick, window.FocusManager?.GetFocusedElement());
            Assert.True(selector.Focus(NavigationMethod.Tab, KeyModifiers.None));
            Press(window, Key.Tab);
            Assert.Same(questions.ContainerFromIndex(0), window.FocusManager?.GetFocusedElement());
            Assert.True(load.Focus(NavigationMethod.Tab, KeyModifiers.None));
            Assert.Same(load, window.FocusManager?.GetFocusedElement());
            Assert.Equal(new Thickness(3d), load.BorderThickness);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Question_panes_split_by_available_width_instead_of_loaded_content_length()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();
        InputView view = new(viewModel);
        Window window = new() { Width = 950, Height = 450, Content = view };

        try
        {
            window.Show();
            Render();

            Grid list = Required<Grid>(view, "QuestionListPane");
            Grid editor = Required<Grid>(view, "SelectedQuestionEditor");
            double emptyListWidth = list.Bounds.Width;
            double emptyEditorWidth = editor.Bounds.Width;

            await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
            Render();

            Assert.NotEmpty(viewModel.MappingSuggestions);
            Assert.Equal(emptyListWidth, list.Bounds.Width, 1d);
            Assert.Equal(emptyEditorWidth, editor.Bounds.Width, 1d);
            Assert.Equal(0, Grid.GetColumn(list));
            Assert.Equal(1, Grid.GetColumn(editor));
            Assert.Equal(list.Bounds.Y, editor.Bounds.Y, 1d);
            Assert.Equal(8d, editor.Bounds.X - list.Bounds.Right, 1d);
            Assert.Equal(1.5d, editor.Bounds.Width / list.Bounds.Width, 2);
            AssertInputControlsContained(view);
            AssertPageCapacityAndRows(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Range_fields_reflow_without_concealing_primary_controls_in_a_narrow_view()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputView view = new(viewModel);
        Window window = new() { Width = 760, Height = 600, Content = view };

        try
        {
            window.Show();
            Render();

            StackPanel sheet = Required<StackPanel>(view, "WorksheetField");
            StackPanel firstRow = Required<StackPanel>(view, "FirstDataRowField");
            StackPanel lastRow = Required<StackPanel>(view, "LastDataRowField");
            Assert.Equal(0, Grid.GetRow(sheet));
            Assert.Equal(2, Grid.GetColumnSpan(sheet));
            Assert.Equal(1, Grid.GetRow(firstRow));
            Assert.Equal(1, Grid.GetRow(lastRow));
            Assert.Equal(2, Grid.GetColumnSpan(firstRow));
            Assert.Equal(2, Grid.GetColumnSpan(lastRow));
            Assert.True(firstRow.Bounds.Top >= sheet.Bounds.Bottom);
            AssertInputControlsContained(view);
            AssertPageCapacityAndRows(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Range_fields_switch_at_the_local_840_dip_container_boundary()
    {
        InputView view = new(new InputViewModel());
        Window window = new() { Width = 950, Height = 600, Content = view };

        try
        {
            window.Show();
            Render();

            Grid root = Required<Grid>(view, "InputContentRoot");
            StackPanel sheet = Required<StackPanel>(view, "WorksheetField");
            StackPanel firstRow = Required<StackPanel>(view, "FirstDataRowField");
            double widthOutsideContainer = window.ClientSize.Width - root.Bounds.Width;

            window.Width = 839d + widthOutsideContainer;
            Render();
            Assert.InRange(root.Bounds.Width, 838.5d, 839.5d);
            Assert.Equal(1, Grid.GetRow(firstRow));
            Assert.Equal(2, Grid.GetColumnSpan(sheet));
            AssertInputControlsContained(view);

            window.Width = 840d + widthOutsideContainer;
            Render();
            Assert.InRange(root.Bounds.Width, 839.5d, 840.5d);
            Assert.Equal(0, Grid.GetRow(firstRow));
            Assert.Equal(2, Grid.GetColumn(firstRow));
            Assert.Equal(1, Grid.GetColumnSpan(sheet));
            AssertInputControlsContained(view);

            window.Width = 841d + widthOutsideContainer;
            Render();
            Assert.InRange(root.Bounds.Width, 840.5d, 841.5d);
            Assert.Equal(0, Grid.GetRow(firstRow));
            Assert.Equal(2, Grid.GetColumn(firstRow));
            AssertInputControlsContained(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(950, 450)]
    [InlineData(760, 600)]
    public async Task Long_unbreakable_header_and_editable_question_text_scroll_locally_without_widening_the_input_layout(
        double width, double height)
    {
        const string LongHeader =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJKLMNOPQRSTUVWXYZABCDEFGHIJ";
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Original",
            headerRow: 1,
            lastRow: 12,
            lastColumn: 2,
            new X02Header(1, LongHeader),
            new X02Header(2, LongHeader + "-supporting"));
        InputViewModel viewModel = new();
        await viewModel.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputView view = new(viewModel);
        Window window = new() { Width = width, Height = height, Content = view };

        try
        {
            window.Show();
            Render();

            Assert.Contains(viewModel.AvailableColumns, column => column.HeaderText == LongHeader);
            Grid list = Required<Grid>(view, "QuestionListPane");
            Grid editorPane = Required<Grid>(view, "SelectedQuestionEditor");
            TextBox text = Required<TextBox>(view, "QuestionTextEditor");
            Assert.Equal(LongHeader, text.Text);
            Assert.False(text.IsReadOnly);
            Assert.True(text.AcceptsReturn);
            Assert.False(text.AcceptsTab);
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, text.TextWrapping);
            AssertInputControlsContained(view);
            double originalWidth = editorPane.Bounds.Width;
            string fullText = string.Join("\n", Enumerable.Repeat(LongHeader, 80)) + "\n全文終端";

            text.Text = fullText;
            Assert.True(text.Focus(NavigationMethod.Tab, KeyModifiers.None));
            Press(window, Key.End, RawInputModifiers.Control);
            Render();
            Assert.Equal(fullText.Length, text.CaretIndex);
            window.KeyTextInput("・手動追記");
            Render();

            Assert.Equal(fullText + "・手動追記", text.Text);
            Assert.Equal(text.Text, viewModel.SelectedQuestion!.QuestionText);
            Assert.Equal(text.Text, viewModel.DefinitionDraft.Questions[0].QuestionText);
            ScrollViewer local = Assert.Single(text.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Equal(ScrollBarVisibility.Auto, local.VerticalScrollBarVisibility);
            Assert.True(local.Extent.Height > local.Viewport.Height);
            Assert.True(local.Offset.Y > 0d);
            Assert.True(local.Extent.Width <= local.Viewport.Width + 1d);
            Assert.Equal(originalWidth, editorPane.Bounds.Width, 1d);
            Assert.Equal(1.5d, editorPane.Bounds.Width / list.Bounds.Width, 2);
            AssertInputControlsContained(view);
            AssertPageCapacityAndRows(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(950, 450, 1)]
    [InlineData(950, 450, 2)]
    [InlineData(760, 600, 1)]
    [InlineData(760, 600, 2)]
    public async Task Page_capacity_tracks_the_arranged_remaining_height_without_cloning_selection_or_layout_thrash(
        double width, double height, double scale)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        while (input.Questions.Count < 27)
        {
            input.AddQuestion();
        }

        InputQuestionMappingViewModel[] originals = input.Questions.ToArray();
        input.SelectedQuestion = originals[19];
        input.SelectedQuestion.QuestionText = "リサイズ中も同じ設問を編集";
        QuantificationDefinition draft = input.DefinitionDraft;
        InputView view = new(input);
        Window window = new() { Width = width, Height = height, Content = view };
        int sizeNotifications = 0;
        input.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(InputViewModel.PageSize))
            {
                sizeNotifications++;
            }
        };

        try
        {
            window.Show();
            window.SetRenderScaling(scale);
            Render();
            TextBox editor = Required<TextBox>(view, "QuestionTextEditor");
            int initialCapacity = input.PageSize;
            AssertPageCapacityAndRows(view);
            AssertInputControlsContained(view);
            Assert.Same(originals[19], input.SelectedQuestion);
            Assert.Equal(19 / input.PageSize, input.PageIndex);

            window.Height = height + 176d;
            Render();
            Assert.Equal(initialCapacity + 4, input.PageSize);
            Assert.Equal(19 / input.PageSize, input.PageIndex);
            Assert.Same(originals[19], input.SelectedQuestion);
            Assert.Same(editor, Required<TextBox>(view, "QuestionTextEditor"));
            Assert.Equal("リサイズ中も同じ設問を編集", editor.Text);
            AssertPageCapacityAndRows(view);
            AssertInputControlsContained(view);

            int settledNotifications = sizeNotifications;
            view.InvalidateMeasure();
            window.UpdateLayout();
            Render();
            Render();
            Assert.Equal(settledNotifications, sizeNotifications);

            window.Height = height;
            Render();
            Assert.Equal(initialCapacity, input.PageSize);
            Assert.Same(draft, input.DefinitionDraft);
            Assert.Same(originals[19], input.SelectedQuestion);
            Assert.Equal(19 / input.PageSize, input.PageIndex);
            for (int index = 0; index < originals.Length; index++)
            {
                Assert.Same(originals[index], input.Questions[index]);
            }

            AssertPageCapacityAndRows(view);
            AssertInputControlsContained(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Page_buttons_visit_every_question_and_jump_selector_restores_the_real_selection_binding()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        while (input.Questions.Count < 17)
        {
            input.AddQuestion();
        }

        InputQuestionMappingViewModel selected = input.SelectedQuestion!;
        InputView view = new(input);
        Window window = new() { Width = 950, Height = 450, Content = view };
        try
        {
            window.Show();
            Render();
            ListBox list = Required<ListBox>(view, "InputQuestionList");
            ComboBox selector = Required<ComboBox>(view, "QuestionSelector");
            Button previous = Required<Button>(view, "PreviousPageButton");
            Button next = Required<Button>(view, "NextPageButton");
            var selectionBinding = BindingOperations.GetBindingExpressionBase(list, ListBox.SelectedItemProperty);
            Assert.NotNull(selectionBinding);
            Assert.Same(input.PreviousPageCommand, previous.Command);
            Assert.Same(input.NextPageCommand, next.Command);
            // CanExecute changes effective state, not the local IsEnabled value.
            Assert.False(previous.IsEffectivelyEnabled);
            Assert.True(next.IsEffectivelyEnabled);
            Assert.True(input.PageSize < input.Questions.Count);
            int pageCount = (input.Questions.Count + input.PageSize - 1) / input.PageSize;
            List<string> visited = [];

            for (int page = 0; page < pageCount; page++)
            {
                if (page > 0)
                {
                    Click(window, next);
                }

                Assert.Equal(page, input.PageIndex);
                Assert.Same(selected, input.SelectedQuestion);
                Assert.Same(selected, Required<TextBox>(view, "QuestionTextEditor").DataContext);
                Assert.Equal(input.PageSummary, Required<TextBlock>(view, "QuestionPageSummary").Text);
                visited.AddRange(input.VisibleQuestions.Select(question => question.Id));
                AssertPageCapacityAndRows(view);
            }

            Assert.Equal(input.Questions.Select(question => question.Id), visited);
            Assert.False(next.IsEffectivelyEnabled);
            Assert.True(previous.IsEffectivelyEnabled);
            Assert.DoesNotContain(selected, input.VisibleQuestions);

            InputQuestionMappingViewModel target = input.Questions[7];
            selector.SelectedItem = target;
            Render();
            Assert.Same(target, input.SelectedQuestion);
            Assert.Same(target, list.SelectedItem);
            Assert.Equal(7 / input.PageSize, input.PageIndex);
            Assert.Equal(target.PrimaryAutomationId, AutomationProperties.GetAutomationId(Required<ComboBox>(view, "PrimaryColumnComboBox")));
            Assert.Equal($"InputQuestion-{target.Id}-Text", AutomationProperties.GetAutomationId(Required<TextBox>(view, "QuestionTextEditor")));

            Click(window, next);
            Assert.Same(target, input.SelectedQuestion);
            Click(window, previous);
            Assert.Same(target, list.SelectedItem);
            Assert.Same(selectionBinding, BindingOperations.GetBindingExpressionBase(list, ListBox.SelectedItemProperty));
            Click(window, next);
            InputQuestionMappingViewModel rowTarget = input.VisibleQuestions[0];
            Assert.NotSame(target, rowTarget);
            list.SelectedItem = rowTarget;
            Render();
            Assert.Same(rowTarget, input.SelectedQuestion);
            Assert.Same(rowTarget, selector.SelectedItem);
            Assert.Same(rowTarget, Required<TextBox>(view, "QuestionTextEditor").DataContext);
            CheckBox enabled = Required<CheckBox>(view, "QuestionEnabledCheckBox");
            Assert.Equal($"InputQuestion-{rowTarget.Id}-Enabled", AutomationProperties.GetAutomationId(enabled));
            enabled.IsChecked = false;
            Render();
            Assert.False(rowTarget.Enabled);
            Assert.Contains("設問 16 / 17 件有効", Required<TextBlock>(view, "InputCountSummary").Text, StringComparison.Ordinal);
            Assert.Contains(list.GetVisualDescendants().OfType<TextBlock>(), text =>
                ReferenceEquals(text.DataContext, rowTarget) && text.Text == "無効" && text.IsVisible);
            enabled.IsChecked = true;
            Render();
            Assert.True(rowTarget.Enabled);
            Assert.Contains("設問 17 / 17 件有効", Required<TextBlock>(view, "InputCountSummary").Text, StringComparison.Ordinal);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Bound_primary_column_uses_the_loaded_header_and_blank_text_has_a_visible_required_error(int headerRow)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSingleSheet(
            "Responses", (uint)headerRow, 6, 3,
            new X02Header(2, "Report answer one"), new X02Header(3, "Report answer two"));
        InputViewModel input = new() { HeaderRow = headerRow };
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputView view = new(input);
        Window window = new() { Width = 950, Height = 450, Content = view };
        try
        {
            window.Show();
            Render();
            InputQuestionMappingViewModel selected = input.SelectedQuestion!;
            TextBox text = Required<TextBox>(view, "QuestionTextEditor");
            ComboBox primary = Required<ComboBox>(view, "PrimaryColumnComboBox");
            TextBlock required = Required<TextBlock>(view, "QuestionTextRequiredError");
            text.Text = "手動の設問文";
            primary.SelectedItem = "C";
            Render();
            Assert.Equal((uint)headerRow, input.Metadata!.HeaderRowNumber);
            Assert.Equal("Report answer two", text.Text);
            Assert.Equal("Report answer two", selected.QuestionText);
            Assert.False(required.IsVisible);

            primary.SelectedItem = "A";
            Render();
            Assert.Equal(string.Empty, text.Text);
            Assert.True(required.IsVisible);
            Assert.Contains("必須", required.Text, StringComparison.Ordinal);
            Assert.Equal($"InputQuestion-{selected.Id}-TextError", AutomationProperties.GetAutomationId(required));
            InputValidationError error = Assert.Single(input.ValidationErrors, item =>
                item.Code == "REQUIRED" && item.NodeId == selected.Id && item.Field == "QuestionText");
            Required<ComboBox>(view, "InputValidationErrorList").SelectedItem = error;
            Render();
            Assert.Contains("QuestionText", Required<TextBox>(view, "InputValidationDetail").Text, StringComparison.Ordinal);
            Assert.False(input.CanContinue);
            AssertContained(view, required);
            AssertInputControlsContained(view);

            text.Text = "見出しが空でも人が入力した設問文";
            Render();
            Assert.Equal(text.Text, selected.QuestionText);
            Assert.False(required.IsVisible);
            Assert.DoesNotContain(input.ValidationErrors, item => item.NodeId == selected.Id && item.Field == "QuestionText");
            Assert.True(input.CanContinue);
            AssertInputControlsContained(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Header_mismatch_invalid_range_and_manual_text_remain_safe_and_visible_until_explicit_reload()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        CountingInputLoader loader = new();
        InputViewModel input = new(loader);
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputView view = new(input);
        Window window = new() { Width = 950, Height = 450, Content = view };
        try
        {
            window.Show();
            Render();
            InputQuestionMappingViewModel selected = input.SelectedQuestion!;
            TextBox text = Required<TextBox>(view, "QuestionTextEditor");
            text.Text = "未更新metadataより手動入力を優先";
            ComboBox header = Required<ComboBox>(view, "HeaderRowComboBox");
            header.SelectedItem = 2;
            Required<ComboBox>(view, "PrimaryColumnComboBox").SelectedItem = "G";
            Required<TextBox>(view, "FirstDataRowTextBox").Text = "0";
            Render();

            Assert.Equal([1, 2], header.Items.Cast<int>());
            Assert.Equal(2, input.HeaderRow);
            Assert.Equal(1u, input.Metadata!.HeaderRowNumber);
            Assert.Equal(1, loader.LoadCount);
            Assert.Equal(0, input.FirstDataRow);
            Assert.Equal("未更新metadataより手動入力を優先", text.Text);
            Assert.Same(selected, input.SelectedQuestion);
            Assert.False(input.CanContinue);
            InputValidationError mismatch = Assert.Single(input.ValidationErrors, error => error.Code == "HEADER_METADATA_MISMATCH");
            Required<ComboBox>(view, "InputValidationErrorList").SelectedItem = mismatch;
            Render();
            Assert.Contains(mismatch.Message, Required<TextBox>(view, "InputValidationDetail").Text, StringComparison.Ordinal);
            Assert.Contains("再読込が必要", Required<TextBlock>(view, "InputCountSummary").Text, StringComparison.Ordinal);
            AssertInputControlsContained(view);

            Required<TextBox>(view, "FirstDataRowTextBox").Text = "3";
            await input.RefreshHeaderAsync(TestContext.Current.CancellationToken);
            Render();
            Assert.Equal(2, loader.LoadCount);
            Assert.Equal(2u, input.Metadata.HeaderRowNumber);
            Assert.Same(selected, input.SelectedQuestion);
            Assert.Equal("未更新metadataより手動入力を優先", text.Text);
            Assert.DoesNotContain(input.ValidationErrors, error => error.Code == "HEADER_METADATA_MISMATCH");
            AssertPageCapacityAndRows(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("FirstDataRowTextBox", "FirstDataRow")]
    [InlineData("LastDataRowTextBox", "LastDataRow")]
    public async Task Uncommitted_row_number_text_is_reported_and_navigable_until_corrected(string editorName, string field)
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputView view = new(input);
        Window window = new() { Width = 950, Height = 600, Content = view };
        try
        {
            window.Show();
            Render();
            TextBox editor = Required<TextBox>(view, editorName);
            TextBox detail = Required<TextBox>(view, "InputValidationDetail");
            ComboBox errors = Required<ComboBox>(view, "InputValidationErrorList");
            QuantificationDefinition draft = input.DefinitionDraft;
            string? confirmedText = editor.Text;
            var binding = BindingOperations.GetBindingExpressionBase(editor, TextBox.TextProperty);
            Assert.NotNull(binding);
            Assert.Contains("技術検証を通過", detail.Text, StringComparison.Ordinal);

            Assert.True(editor.Focus(NavigationMethod.Tab));
            editor.SelectAll();
            window.KeyTextInput("-");
            Render();

            Assert.Equal("-", editor.Text);
            Assert.True(DataValidationErrors.GetHasErrors(editor));
            Assert.Equal(draft.FirstDataRow, input.FirstDataRow);
            Assert.Equal(draft.LastDataRow, input.LastDataRow);
            Assert.Same(draft, input.DefinitionDraft);
            Assert.Empty(input.ValidationErrors);
            Assert.True(errors.IsEnabled);
            Assert.Equal(field, Assert.IsType<InputValidationError>(Assert.Single(errors.Items)).Field);
            Assert.Contains("未反映の入力 1 件", detail.Text, StringComparison.Ordinal);
            Assert.Contains(field, detail.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("技術検証を通過", detail.Text, StringComparison.Ordinal);
            Assert.Equal(detail.Text, ToolTip.GetTip(Required<Border>(view, "InputValidationSummary")));
            Click(window, Required<Button>(view, "InputGoToProblemButton"));
            Assert.Same(editor, window.FocusManager?.GetFocusedElement());

            // The source value does not change: the view must observe the binding,
            // not depend on a VM PropertyChanged notification to remove this error.
            editor.SetCurrentValue(TextBox.TextProperty, confirmedText);
            Render();
            Assert.False(DataValidationErrors.GetHasErrors(editor));
            Assert.Empty(errors.Items);
            Assert.False(errors.IsEnabled);
            Assert.False(Required<Button>(view, "InputGoToProblemButton").IsEnabled);
            Assert.Equal(input.ValidationSummary, detail.Text);
            Assert.Same(draft, input.DefinitionDraft);
            Assert.Same(binding, BindingOperations.GetBindingExpressionBase(editor, TextBox.TextProperty));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Uncommitted_row_number_errors_are_aggregated_with_confirmed_validation_errors()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputView view = new(input);
        Window window = new() { Width = 950, Height = 600, Content = view };
        try
        {
            window.Show();
            Render();
            TextBox first = Required<TextBox>(view, "FirstDataRowTextBox");
            TextBox last = Required<TextBox>(view, "LastDataRowTextBox");
            TextBox detail = Required<TextBox>(view, "InputValidationDetail");
            ComboBox errors = Required<ComboBox>(view, "InputValidationErrorList");
            string? confirmedFirst = first.Text;
            string? confirmedLast = last.Text;
            input.SelectedQuestion!.QuestionText = string.Empty;
            InputValidationError required = Assert.Single(input.ValidationErrors, error => error.Field == "QuestionText");
            QuantificationDefinition draft = input.DefinitionDraft;
            first.SetCurrentValue(TextBox.TextProperty, "-");
            last.SetCurrentValue(TextBox.TextProperty, "編集中");
            Render();

            Assert.Equal(input.ValidationErrors.Count + 2, errors.Items.Count);
            Assert.Contains(required, errors.Items.Cast<InputValidationError>());
            Assert.Contains("未反映の入力 2 件", detail.Text, StringComparison.Ordinal);
            foreach ((TextBox editor, string field) in new[] { (first, "FirstDataRow"), (last, "LastDataRow") })
            {
                errors.SetCurrentValue(ComboBox.SelectedItemProperty,
                    Assert.Single(errors.Items.Cast<InputValidationError>(), error => error.Field == field));
                Click(window, Required<Button>(view, "InputGoToProblemButton"));
                Assert.Same(editor, window.FocusManager?.GetFocusedElement());
            }

            first.SetCurrentValue(TextBox.TextProperty, confirmedFirst);
            Render();
            Assert.Contains("未反映の入力 1 件", detail.Text, StringComparison.Ordinal);
            Assert.Equal(input.ValidationErrors.Count + 1, errors.Items.Count);
            Assert.Equal("LastDataRow", Assert.IsType<InputValidationError>(errors.SelectedItem).Field);
            last.SetCurrentValue(TextBox.TextProperty, confirmedLast);
            Render();
            Assert.DoesNotContain("未反映", detail.Text, StringComparison.Ordinal);
            Assert.Contains(required.Message, detail.Text, StringComparison.Ordinal);
            Assert.Same(required, Assert.Single(errors.Items));
            Assert.Same(draft, input.DefinitionDraft);
            Assert.Equal(draft.FirstDataRow, input.FirstDataRow);
            Assert.Equal(draft.LastDataRow, input.LastDataRow);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Jump_and_details_open_mapping_for_the_same_ID_without_starting_a_ready_execution()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        CountingRunBoundary run = new();
        CountingAuthenticationBoundary authentication = new();
        ExecutionViewModel execution = new(authentication, run);
        execution.Configure(input.DefinitionDraft, input.Metadata!, input.FilePath);
        await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        Assert.True(execution.CanStart);
        using MainWindowViewModel shell = new(
            new WorkflowNavigator(), input, new QuantificationDesignViewModel(), execution, new ResultsOutputViewModel());
        InputView view = new(input);
        Window window = new() { Width = 950, Height = 450, DataContext = shell, Content = new Border { Child = view } };
        int standaloneRequests = 0;
        view.SettingsRequested += (_, _) => standaloneRequests++;
        try
        {
            window.Show();
            Render();
            Assert.Equal(0, run.StartCount);
            InputQuestionMappingViewModel target = input.Questions[^1];
            Required<ComboBox>(view, "QuestionSelector").SelectedItem = target;
            Render();
            Assert.Equal(input.Questions.IndexOf(target) / input.PageSize, input.PageIndex);
            Assert.Same(target, Required<ListBox>(view, "InputQuestionList").SelectedItem);
            Required<TextBox>(view, "QuestionTextEditor").Text = "詳細へ引き継ぐ編集本文";
            Button details = Required<Button>(view, "MappingDetailsButton");
            Assert.Equal($"InputQuestion-{target.Id}-Details", AutomationProperties.GetAutomationId(details));
            Click(window, details);

            Assert.True(shell.IsSettingsOpen);
            Assert.Equal(SettingsCategory.Mapping, shell.Settings.SelectedCategory);
            Assert.Equal(WorkflowStep.Input, shell.CurrentStep);
            Assert.Same(input, shell.Settings.Input);
            Assert.Same(target, shell.Settings.Input.SelectedQuestion);
            Assert.Equal("詳細へ引き継ぐ編集本文", target.QuestionText);
            Assert.Equal(0, standaloneRequests);

            // T23 owns shell composition. Check the existing category view's actual
            // selector contract without replacing its production template or navigation.
            MappingSettingsView mapping = new(shell.Settings.Input);
            window.Content = mapping;
            Render();
            Assert.Same(target, Required<ComboBox>(mapping, "QuestionSelector").SelectedItem);
            Assert.Equal(target.NameAutomationId, AutomationProperties.GetAutomationId(Required<TextBox>(mapping, "QuestionNameEditor")));
            Assert.True(execution.CanStart);
            Assert.Equal(0, run.StartCount);
            Assert.Equal(1, authentication.CheckCount);
            Assert.Null(execution.LastRunContext);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Standalone_details_event_and_cached_view_keep_pending_input_and_same_question()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        InputQuestionMappingViewModel selected = input.SelectedQuestion!;
        InputView view = new(input);
        MappingSettingsView mapping = new(input);
        Window window = new() { Width = 950, Height = 450, Content = view };
        int requests = 0;
        view.SettingsRequested += (sender, _) =>
        {
            Assert.Same(view, sender);
            Assert.Same(selected, view.ViewModel.SelectedQuestion);
            requests++;
            window.Content = mapping;
        };
        try
        {
            window.Show();
            Render();
            TextBox firstRow = Required<TextBox>(view, "FirstDataRowTextBox");
            int originalFirstRow = input.FirstDataRow;
            firstRow.Text = "編集中";
            Render();
            Assert.True(DataValidationErrors.GetHasErrors(firstRow));
            Assert.Equal(originalFirstRow, input.FirstDataRow);
            Click(window, Required<Button>(view, "MappingDetailsButton"));
            Assert.Equal(1, requests);
            Assert.Same(selected, Required<ComboBox>(mapping, "QuestionSelector").SelectedItem);

            window.Content = view;
            window.Height = 600;
            Render();
            Assert.Same(firstRow, Required<TextBox>(view, "FirstDataRowTextBox"));
            Assert.Equal("編集中", firstRow.Text);
            Assert.True(DataValidationErrors.GetHasErrors(firstRow));
            Assert.Same(selected, input.SelectedQuestion);
            Assert.Same(selected, Required<ListBox>(view, "InputQuestionList").SelectedItem);
            Assert.Equal(1, requests);
            AssertPageCapacityAndRows(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Empty_and_rebound_views_use_only_the_current_input_VM_for_selection_and_capacity()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel first = new();
        InputView view = new(first);
        Window window = new() { Width = 950, Height = 450, Content = view };
        try
        {
            window.Show();
            Render();
            Assert.Empty(Required<ListBox>(view, "InputQuestionList").Items);
            Assert.False(Required<Button>(view, "PreviousPageButton").IsEffectivelyEnabled);
            Assert.False(Required<Button>(view, "NextPageButton").IsEffectivelyEnabled);
            Assert.False(Required<ComboBox>(view, "PrimaryColumnComboBox").IsEnabled);
            Assert.False(Required<TextBox>(view, "QuestionTextEditor").IsEnabled);
            Assert.Equal(first.PageSummary, Required<TextBlock>(view, "QuestionPageSummary").Text);
            AssertInputControlsContained(view);

            await first.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
            Render();
            InputQuestionMappingViewModel firstSelection = first.SelectedQuestion!;
            QuantificationDefinition firstDraft = first.DefinitionDraft;
            int firstCapacity = first.PageSize;
            var selectionBinding = BindingOperations.GetBindingExpressionBase(
                Required<ListBox>(view, "InputQuestionList"), ListBox.SelectedItemProperty);
            Assert.NotNull(selectionBinding);
            InputViewModel replacement = new();
            await replacement.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
            InputQuestionMappingViewModel replacementSelection = replacement.Questions[^1];
            replacement.SelectedQuestion = replacementSelection;
            QuantificationDefinition replacementDraft = replacement.DefinitionDraft;
            view.DataContext = replacement;
            // Rebinding must not regenerate mappings and then repair only the display later.
            Assert.Same(replacementSelection, replacement.SelectedQuestion);
            Assert.Same(replacementDraft, replacement.DefinitionDraft);
            Assert.Same(firstSelection, first.SelectedQuestion);
            Assert.Same(firstDraft, first.DefinitionDraft);
            window.Height = 600;
            Render();

            Assert.Same(replacement, view.ViewModel);
            Assert.Same(replacementSelection, replacement.SelectedQuestion);
            Assert.Same(replacementSelection, Required<ComboBox>(view, "QuestionSelector").SelectedItem);
            Assert.Same(replacementSelection, Required<TextBox>(view, "QuestionTextEditor").DataContext);
            Assert.Same(firstSelection, first.SelectedQuestion);
            Assert.Same(firstDraft, first.DefinitionDraft);
            Assert.Same(replacementDraft, replacement.DefinitionDraft);
            Assert.Equal(firstCapacity, first.PageSize);
            AssertPageCapacityAndRows(view);

            view.DataContext = null;
            Render();
            Assert.Same(replacementSelection, replacement.SelectedQuestion);
            Assert.Same(replacementDraft, replacement.DefinitionDraft);
            Assert.False(Required<TextBox>(view, "QuestionTextEditor").IsEnabled);
            Assert.False(Required<ComboBox>(view, "PrimaryColumnComboBox").IsEnabled);
            view.DataContext = replacement;
            Render();
            Assert.Same(replacementSelection, replacement.SelectedQuestion);
            AssertPageCapacityAndRows(view);

            // Leave old rebind work queued, then replace the context while detached.
            view.DataContext = first;
            window.Content = null;
            view.DataContext = replacement;
            window.Content = view;
            Render();
            Assert.Same(replacementSelection, replacement.SelectedQuestion);
            Assert.Same(replacementSelection, Required<ListBox>(view, "InputQuestionList").SelectedItem);
            Assert.Same(replacementSelection, Required<ComboBox>(view, "QuestionSelector").SelectedItem);
            Assert.Same(replacementSelection, Required<TextBox>(view, "QuestionTextEditor").DataContext);
            Assert.Same(firstSelection, first.SelectedQuestion);
            Assert.Same(firstDraft, first.DefinitionDraft);
            Assert.Same(replacementDraft, replacement.DefinitionDraft);
            Assert.Equal(firstCapacity, first.PageSize);
            Assert.Same(selectionBinding, BindingOperations.GetBindingExpressionBase(
                Required<ListBox>(view, "InputQuestionList"), ListBox.SelectedItemProperty));
            AssertPageCapacityAndRows(view);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Name_CRUD_and_candidates_remain_operable_in_mapping_settings_not_in_the_main_input()
    {
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        InputViewModel input = new();
        await input.SetFilePathAsync(workbook.Path, TestContext.Current.CancellationToken);
        MappingSettingsView mapping = new(input);
        Window window = new() { Width = 950, Height = 600, Content = mapping };
        try
        {
            window.Show();
            Render();
            InputQuestionMappingViewModel selected = input.SelectedQuestion!;
            TextBox name = Required<TextBox>(mapping, "QuestionNameEditor");
            Assert.Equal(selected.NameAutomationId, AutomationProperties.GetAutomationId(name));
            name.Text = "入力詳細で変更した表示名";
            Render();
            Assert.Equal(name.Text, selected.DisplayName);
            ComboBox candidates = Required<ComboBox>(mapping, "CandidateSelector");
            Assert.Equal("InputMappingSuggestions", AutomationProperties.GetAutomationId(candidates));
            QuantificationDefinition beforeInspect = input.DefinitionDraft;
            MappingSuggestionViewModel candidate = input.MappingSuggestions.Last();
            candidates.SelectedItem = candidate;
            Render();
            Assert.Same(beforeInspect, input.DefinitionDraft);
            string? overview = Required<TextBox>(mapping, "CandidateOverview").Text;
            Assert.Contains(candidate.HeaderText, overview, StringComparison.Ordinal);
            Assert.Contains(candidate.SupportText, overview, StringComparison.Ordinal);

            int initialCount = input.Questions.Count;
            Click(window, Required<Button>(mapping, "AddQuestionButton"));
            Assert.Equal(initialCount + 1, input.Questions.Count);
            Assert.Same(selected, input.SelectedQuestion);
            Click(window, Required<Button>(mapping, "DuplicateQuestionButton"));
            Assert.Equal(initialCount + 2, input.Questions.Count);
            InputQuestionMappingViewModel copy = input.Questions[input.Questions.IndexOf(selected) + 1];
            Assert.NotEqual(selected.Id, copy.Id);
            Assert.Equal(selected.DisplayName, copy.DisplayName);
            Required<ComboBox>(mapping, "QuestionSelector").SelectedItem = copy;
            Render();
            int copyIndex = input.Questions.IndexOf(copy);
            Click(window, Required<Button>(mapping, "MoveQuestionDownButton"));
            Assert.Equal(copyIndex + 1, input.Questions.IndexOf(copy));
            Assert.Same(copy, input.SelectedQuestion);
            Click(window, Required<Button>(mapping, "MoveQuestionUpButton"));
            Assert.Equal(copyIndex, input.Questions.IndexOf(copy));
            Click(window, Required<Button>(mapping, "DeleteQuestionButton"));
            Assert.DoesNotContain(copy, input.Questions);
            Assert.Equal(initialCount + 1, input.Questions.Count);

            Button reapply = Required<Button>(mapping, "ApplySuggestionsButton");
            Assert.Equal("ApplyInputSuggestions", AutomationProperties.GetAutomationId(reapply));
            Click(window, reapply);
            Assert.Equal(initialCount, input.Questions.Count);
            Assert.True(input.IsUsingSuggestedMapping);
            Assert.Equal(["F", "G", "H", "I", "J"], input.Questions.Select(question => question.PrimarySourceColumn));
            Assert.Same(input.SelectedQuestion, Required<ComboBox>(mapping, "QuestionSelector").SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertInputControlsContained(InputView view)
    {
        Assert.IsType<Grid>(view.Content);
        string[] names =
        [
            "FilePathTextBox", "PickFileButton", "LoadFileButton", "WorksheetComboBox", "HeaderRowComboBox",
            "FirstDataRowTextBox", "LastDataRowTextBox", "RefreshHeaderButton", "QuestionSelector",
            "InputQuestionList", "PreviousPageButton", "NextPageButton", "QuestionEnabledCheckBox",
            "PrimaryColumnComboBox", "MappingDetailsButton", "QuestionTextEditor", "InputValidationErrorList",
            "InputValidationDetail", "InputGoToProblemButton",
        ];
        foreach (string name in names)
        {
            Control control = view.FindControl<Control>(name)!;
            Assert.NotNull(control);
            Assert.True(control.MinHeight >= 44d, name);
            Assert.True(control.Bounds.Height >= 44d, name);
            Assert.True(control.Bounds.Width >= 44d, name);
            AssertContained(view, control);
            if (control is TemplatedControl templated)
            {
                Assert.True(templated.FontSize >= 14d, name);
            }
        }

        AssertContained(view, Required<TextBlock>(view, "InputCountSummary"));
        Grid editor = Required<Grid>(view, "SelectedQuestionEditor");
        AssertContained(editor, Required<ComboBox>(view, "PrimaryColumnComboBox"));
        AssertContained(editor, Required<TextBox>(view, "QuestionTextEditor"));
        AssertContained(editor, Required<Button>(view, "MappingDetailsButton"));
        Assert.All(view.GetVisualDescendants().OfType<ScrollViewer>(), scroll =>
            Assert.Contains(scroll.GetVisualAncestors(), ancestor => ancestor is TextBox or ListBox or ComboBox));
    }

    private static void AssertPageCapacityAndRows(InputView view)
    {
        InputViewModel input = view.ViewModel;
        Grid viewport = Required<Grid>(view, "QuestionPageViewport");
        ListBox list = Required<ListBox>(view, "InputQuestionList");
        Assert.True(double.IsFinite(viewport.Bounds.Height));
        Assert.True(viewport.Bounds.Height >= 44d);
        Assert.Equal(Math.Max(1, (int)Math.Floor(viewport.Bounds.Height / 44d)), input.PageSize);
        Assert.Same(input.VisibleQuestions, list.ItemsSource);
        Assert.Equal(input.VisibleQuestions.Count, list.Items.Count);
        for (int index = 0; index < input.VisibleQuestions.Count; index++)
        {
            InputQuestionMappingViewModel question = input.VisibleQuestions[index];
            Assert.Same(input.Questions[input.PageIndex * input.PageSize + index], question);
            ListBoxItem container = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(index));
            Assert.Same(question, container.DataContext);
            Assert.True(container.Bounds.Height >= 44d);
            AssertContained(viewport, container);
            Assert.Contains(container.GetVisualDescendants().OfType<Control>(), control =>
                AutomationProperties.GetAutomationId(control) == $"InputQuestion-{question.Id}-Row");
        }
    }

    private static void AssertContained(Control parent, Control control)
    {
        Assert.True(control.IsVisible);
        Assert.All(control.GetVisualAncestors().OfType<Control>(), ancestor => Assert.True(ancestor.IsVisible));
        Point? origin = control.TranslatePoint(default, parent);
        Assert.NotNull(origin);
        Assert.True(double.IsFinite(control.Bounds.Width) && control.Bounds.Width > 0d);
        Assert.True(double.IsFinite(control.Bounds.Height) && control.Bounds.Height > 0d);
        Assert.InRange(origin.Value.X, -0.5d, parent.Bounds.Width + 0.5d);
        Assert.InRange(origin.Value.Y, -0.5d, parent.Bounds.Height + 0.5d);
        Assert.True(origin.Value.X + control.Bounds.Width <= parent.Bounds.Width + 0.5d, control.Name);
        Assert.True(origin.Value.Y + control.Bounds.Height <= parent.Bounds.Height + 0.5d, control.Name);
    }

    private static void Click(TopLevel window, Button button)
    {
        Assert.True(button.IsEffectivelyEnabled);
        Assert.True(button.Focus(NavigationMethod.Tab, KeyModifiers.None));
        Press(window, Key.Enter);
        Render();
    }

    private static T Required<T>(Control root, string name)
        where T : Control =>
        Assert.IsType<T>(root.FindControl<T>(name));

    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(TopLevel window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physicalKey = key switch
        {
            Key.Tab => PhysicalKey.Tab,
            Key.Enter => PhysicalKey.Enter,
            Key.End => PhysicalKey.End,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unsupported test key."),
        };
        window.KeyPress(key, modifiers, physicalKey, null);
        window.KeyRelease(key, modifiers, physicalKey, null);
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class CountingRunBoundary : IQuantificationRunBoundary
    {
        public int StartCount { get; private set; }

        public Task<RunSummary> RunAsync(
            QuantificationRunRequest request,
            Action<EvaluationProgress>? progress,
            CancellationToken cancellationToken)
        {
            StartCount++;
            throw new InvalidOperationException("Input and Mapping settings must not start evaluation.");
        }
    }

    private sealed class CountingAuthenticationBoundary : IExecutionAuthenticationBoundary
    {
        public int CheckCount { get; private set; }

        public Task<ExecutionAuthenticationSnapshot> CheckAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            // In-memory identity only: no CLI process, credential, or user settings access.
            return Task.FromResult(new ExecutionAuthenticationSnapshot(
                ExecutionAuthenticationState.Available,
                [new CopilotModelAvailability("auto", 128_000, 128_000)],
                new CopilotRuntimeIdentity(Path.Combine(Path.GetTempPath(), "input-view-fake-cli"),
                    "1.0.0", new string('A', 64), "1.0.0")));
        }
    }

    private sealed class CountingInputLoader : IInputWorkbookLoader
    {
        private readonly InputWorkbookLoader inner = new();

        public int LoadCount { get; private set; }

        public Task<InputWorkbookLoadResult>? LastLoad { get; private set; }

        public Task<InputWorkbookLoadResult> LoadAsync(
            string filePath,
            uint headerRow,
            CancellationToken cancellationToken = default)
        {
            LoadCount++;
            return LastLoad = inner.LoadAsync(filePath, headerRow, cancellationToken);
        }
    }

    private sealed class DelayedInputLoader : IInputWorkbookLoader
    {
        private readonly InputWorkbookLoader inner = new();
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public void Release() => release.TrySetResult();

        public async Task<InputWorkbookLoadResult> LoadAsync(
            string filePath,
            uint headerRow,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await inner.LoadAsync(filePath, headerRow, cancellationToken);
        }
    }

    private sealed class ScriptedInputWorkbookPicker(params string?[] paths) : IInputWorkbookPicker
    {
        private readonly Queue<string?> values = new(paths);

        public int CallCount { get; private set; }

        public Task<string?> PickAsync(TopLevel topLevel)
        {
            ArgumentNullException.ThrowIfNull(topLevel);
            CallCount++;
            return Task.FromResult(values.Count > 0 ? values.Dequeue() : null);
        }
    }

    private sealed class ThrowingInputWorkbookPicker : IInputWorkbookPicker
    {
        public Task<string?> PickAsync(TopLevel topLevel) =>
            throw new InputWorkbookPickerPathUnavailableException();
    }
}