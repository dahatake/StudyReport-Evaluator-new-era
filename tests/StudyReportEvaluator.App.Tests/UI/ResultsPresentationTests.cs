using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows.Input;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Reading;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: FR-035 (AC-036)
public sealed class ResultsPresentationTests
{
    [Fact]
    public void Empty_results_disable_paging_jump_and_detail_commands()
    {
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel viewModel = new(output);

        Assert.Equal(4, viewModel.PageSize);
        viewModel.PageIndex = int.MaxValue;
        viewModel.GoToRowNumber = 10;
        Assert.All(PresentationCommands(viewModel), command =>
        {
            Assert.False(command.CanExecute(null));
            command.Execute(null);
        });

        Assert.Equal(0, viewModel.PageIndex);
        Assert.Equal("結果はありません（0 行）", viewModel.PageSummary);
        Assert.Empty(viewModel.Results);
        Assert.Empty(viewModel.RowScores);
        Assert.Empty(viewModel.VisibleRowScores);
        Assert.Empty(viewModel.SelectedRowCriteria);
        Assert.Null(viewModel.SelectedRow);
        Assert.Null(viewModel.SelectedCriterion);
        Assert.Equal(0, viewModel.OverrideErrorCount);
        Assert.False(viewModel.IsDetailVisible);
        Assert.False(viewModel.HasUnsavedOverrides);
        Assert.Equal(0, output.ExportCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(530)]
    public async Task Paging_uses_student_rows_and_keeps_all_original_references(int rowCount)
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync(rowCount: rowCount);
        ReadOnlyObservableCollection<ResultsCriterionViewModel> results = viewModel.Results;
        ReadOnlyObservableCollection<ResultsRowScoreViewModel> rowScores = viewModel.RowScores;
        ReadOnlyObservableCollection<ResultsRowScoreViewModel> visibleRows = viewModel.VisibleRowScores;
        ResultsCriterionViewModel[] originalCriteria = results.ToArray();
        ResultsRowScoreViewModel[] originalRows = rowScores.ToArray();
        List<int> visited = [];
        int lastPage = (rowCount - 1) / 4;

        Assert.Equal(rowCount * 2, results.Count);
        Assert.Equal(rowCount, rowScores.Count);
        Assert.False(viewModel.PreviousPageCommand.CanExecute(null));
        for (int page = 0; page <= lastPage; page++)
        {
            Assert.Equal(page, viewModel.PageIndex);
            ResultsRowScoreViewModel[] expected = originalRows.Skip(page * 4).Take(4).ToArray();
            Assert.Equal(expected.Length, visibleRows.Count);
            for (int index = 0; index < expected.Length; index++)
            {
                Assert.Same(expected[index], visibleRows[index]);
                visited.Add(visibleRows[index].SourceRowNumber);
            }

            Assert.Same(expected[0], viewModel.SelectedRow);
            Assert.Equal(2, viewModel.SelectedRowCriteria.Count);
            Assert.All(viewModel.SelectedRowCriteria, criterion =>
            {
                Assert.Equal(expected[0].SourceRowNumber, criterion.SourceRowNumber);
                Assert.Contains(originalCriteria, original => ReferenceEquals(original, criterion));
            });
            Assert.Equal(string.Format(
                CultureInfo.InvariantCulture,
                "{0:N0}–{1:N0} / {2:N0} 行",
                page * 4 + 1,
                Math.Min(page * 4 + 4, rowCount),
                rowCount), viewModel.PageSummary);
            Assert.Equal(page < lastPage, viewModel.NextPageCommand.CanExecute(null));
            viewModel.NextPageCommand.Execute(null);
        }

        Assert.Equal(Enumerable.Range(10, rowCount), visited);
        Assert.Equal(lastPage, viewModel.PageIndex);
        Assert.Same(results, viewModel.Results);
        Assert.Same(rowScores, viewModel.RowScores);
        Assert.Same(visibleRows, viewModel.VisibleRowScores);
        Assert.Equal(originalRows.Length, rowScores.Count);
        Assert.Equal(originalCriteria.Length, results.Count);
        for (int index = 0; index < originalRows.Length; index++)
        {
            Assert.Same(originalRows[index], rowScores[index]);
        }

        for (int index = 0; index < originalCriteria.Length; index++)
        {
            Assert.Same(originalCriteria[index], results[index]);
        }

        Assert.False(viewModel.HasUnsavedOverrides);
    }

    [Fact]
    public async Task Source_row_jump_reaches_the_last_student_and_page_index_is_clamped()
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync();

        viewModel.GoToRowNumber = 18;
        Assert.True(viewModel.GoToRowCommand.CanExecute(null));
        viewModel.GoToRowCommand.Execute(null);

        Assert.Equal(2, viewModel.PageIndex);
        Assert.Same(viewModel.RowScores[8], viewModel.SelectedRow);
        Assert.Same(viewModel.SelectedRow, Assert.Single(viewModel.VisibleRowScores));
        Assert.All(viewModel.SelectedRowCriteria, criterion => Assert.Equal(18, criterion.SourceRowNumber));
        Assert.Equal("9–9 / 9 行", viewModel.PageSummary);
        Assert.False(viewModel.NextPageCommand.CanExecute(null));
        Assert.True(viewModel.PreviousPageCommand.CanExecute(null));

        viewModel.PageIndex = int.MaxValue;
        Assert.Equal(2, viewModel.PageIndex);
        viewModel.PreviousPageCommand.Execute(null);
        Assert.Equal(1, viewModel.PageIndex);
        Assert.Same(viewModel.RowScores[4], viewModel.SelectedRow);
        viewModel.PageIndex = int.MinValue;
        Assert.Equal(0, viewModel.PageIndex);
        Assert.Same(viewModel.RowScores[0], viewModel.SelectedRow);
        Assert.False(viewModel.PreviousPageCommand.CanExecute(null));
        Assert.False(viewModel.HasUnsavedOverrides);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(19)]
    [InlineData(int.MaxValue)]
    public async Task Invalid_source_row_jump_does_not_change_page_or_selection(int? sourceRowNumber)
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync();
        viewModel.PageIndex = 1;
        ResultsRowScoreViewModel? selected = viewModel.SelectedRow;

        viewModel.GoToRowNumber = sourceRowNumber;
        Assert.False(viewModel.GoToRowCommand.CanExecute(null));
        viewModel.GoToRowCommand.Execute(null);

        Assert.Equal(1, viewModel.PageIndex);
        Assert.Same(selected, viewModel.SelectedRow);
        Assert.Equal(sourceRowNumber, viewModel.GoToRowNumber);
        Assert.False(viewModel.HasUnsavedOverrides);
    }

    [Theory]
    [InlineData(1, 6)]
    [InlineData(3, 2)]
    [InlineData(7, 0)]
    [InlineData(int.MaxValue, 0)]
    public async Task Page_size_changes_keep_the_selected_source_row_and_criterion_references(
        int pageSize,
        int expectedPageIndex)
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync();
        viewModel.GoToRowNumber = 16;
        viewModel.GoToRowCommand.Execute(null);
        viewModel.ShowDetailCommand.Execute(null);
        ResultsRowScoreViewModel? selected = viewModel.SelectedRow;
        ResultsCriterionViewModel[] criteria = viewModel.SelectedRowCriteria.ToArray();
        // Simulate the two-way SelectedItem feedback caused by resetting an ItemsSource.
        ((INotifyCollectionChanged)viewModel.VisibleRowScores).CollectionChanged +=
            (_, _) => viewModel.SelectedRow = null;

        viewModel.PageSize = pageSize;

        Assert.Equal(pageSize, viewModel.PageSize);
        Assert.Equal(expectedPageIndex, viewModel.PageIndex);
        Assert.Same(selected, viewModel.SelectedRow);
        Assert.Equal(16, viewModel.SelectedRow?.SourceRowNumber);
        Assert.Contains(viewModel.VisibleRowScores, row => ReferenceEquals(row, selected));
        Assert.InRange(viewModel.VisibleRowScores.Count, 1, Math.Min(pageSize, 9));
        Assert.Equal(9, viewModel.RowScores.Count);
        Assert.Equal(18, viewModel.Results.Count);
        Assert.Same(criteria[0], viewModel.SelectedRowCriteria[0]);
        Assert.Same(criteria[1], viewModel.SelectedRowCriteria[1]);
        Assert.True(viewModel.IsDetailVisible);
        Assert.False(viewModel.HasUnsavedOverrides);
        if (pageSize == int.MaxValue)
        {
            Assert.Equal("1–9 / 9 行", viewModel.PageSummary);
            Assert.False(viewModel.NextPageCommand.CanExecute(null));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Nonpositive_page_sizes_are_rejected_without_changing_presentation(int pageSize)
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync();
        viewModel.PageIndex = 1;
        ResultsRowScoreViewModel? selected = viewModel.SelectedRow;
        ResultsRowScoreViewModel[] visible = viewModel.VisibleRowScores.ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() => viewModel.PageSize = pageSize);

        Assert.Equal(4, viewModel.PageSize);
        Assert.Equal(1, viewModel.PageIndex);
        Assert.Same(selected, viewModel.SelectedRow);
        Assert.Equal(visible, viewModel.VisibleRowScores);
        Assert.False(viewModel.HasUnsavedOverrides);
    }

    [Fact]
    public async Task Detail_commands_and_selection_use_original_criterion_editors()
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync();
        viewModel.SelectedRow = viewModel.RowScores[5];

        Assert.Equal(1, viewModel.PageIndex);
        Assert.Same(viewModel.RowScores[5], viewModel.SelectedRow);
        Assert.Equal(2, viewModel.SelectedRowCriteria.Count);
        Assert.All(viewModel.SelectedRowCriteria, criterion =>
        {
            Assert.Equal(15, criterion.SourceRowNumber);
            Assert.Same(viewModel.Results.Single(item =>
                item.SourceRowNumber == 15 && item.CriterionId == criterion.CriterionId), criterion);
        });
        IList<ResultsCriterionViewModel> readOnly = viewModel.SelectedRowCriteria;
        Assert.True(readOnly.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => readOnly.Add(viewModel.Results[0]));
        Assert.True(viewModel.ShowDetailCommand.CanExecute(null));
        Assert.False(viewModel.ShowListCommand.CanExecute(null));

        viewModel.ShowDetailCommand.Execute(null);
        Assert.True(viewModel.IsDetailVisible);
        Assert.False(viewModel.ShowDetailCommand.CanExecute(null));
        Assert.True(viewModel.ShowListCommand.CanExecute(null));
        viewModel.ShowListCommand.Execute(null);
        Assert.False(viewModel.IsDetailVisible);
        Assert.Same(viewModel.RowScores[5], viewModel.SelectedRow);

        viewModel.ShowDetailCommand.Execute(null);
        viewModel.SelectedRow = null;
        Assert.Null(viewModel.SelectedRow);
        Assert.Empty(viewModel.SelectedRowCriteria);
        Assert.False(viewModel.IsDetailVisible);
        Assert.False(viewModel.ShowDetailCommand.CanExecute(null));
        Assert.False(viewModel.HasUnsavedOverrides);
    }

    [Fact]
    public async Task Paging_and_recomputation_keep_overrides_and_do_not_recreate_selected_criterion_editors()
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync();
        viewModel.GoToRowNumber = 16;
        viewModel.GoToRowCommand.Execute(null);
        viewModel.ShowDetailCommand.Execute(null);
        ResultsRowScoreViewModel? selectedBeforeEdit = viewModel.SelectedRow;
        ResultsCriterionViewModel editor = viewModel.SelectedRowCriteria[0];
        ReadOnlyObservableCollection<ResultsCriterionViewModel> details = viewModel.SelectedRowCriteria;
        int editorCollectionChanges = 0;
        ((INotifyCollectionChanged)details).CollectionChanged += (_, _) => editorCollectionChanges++;

        editor.OverrideText = "0";

        Assert.Equal(0, editorCollectionChanges);
        Assert.Same(details, viewModel.SelectedRowCriteria);
        Assert.Same(editor, details[0]);
        Assert.NotSame(selectedBeforeEdit, viewModel.SelectedRow);
        Assert.Same(viewModel.RowScores[6], viewModel.SelectedRow);
        Assert.Contains(viewModel.VisibleRowScores, row => ReferenceEquals(row, viewModel.SelectedRow));
        Assert.Equal(0m, editor.EffectiveRaw);
        Assert.Equal(0m, editor.NormalizedScore);
        Assert.True(viewModel.HasUnsavedOverrides);

        viewModel.ShowListCommand.Execute(null);
        viewModel.NextPageCommand.Execute(null);
        Assert.Equal(2, viewModel.PageIndex);
        viewModel.GoToRowCommand.Execute(null);
        viewModel.ShowDetailCommand.Execute(null);

        Assert.Equal(1, viewModel.PageIndex);
        Assert.Equal(16, viewModel.SelectedRow?.SourceRowNumber);
        Assert.Same(editor, viewModel.SelectedRowCriteria[0]);
        Assert.Same(editor, viewModel.Results.Single(item =>
            item.SourceRowNumber == 16 && item.CriterionId == "C1"));
        Assert.Equal("0", editor.OverrideText);
        Assert.Equal(5m, editor.AiRawScore);
        Assert.Equal(9, viewModel.RowScores.Count);
        Assert.Equal(18, viewModel.Results.Count);
        Assert.True(viewModel.HasUnsavedOverrides);
    }

    [Fact]
    public async Task Zero_override_and_unfinished_blank_remain_distinct()
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync(
            rowCount: 2,
            cancelAfterFirst: true);
        ResultsCriterionViewModel zero = viewModel.SelectedRowCriteria[0];
        zero.OverrideText = "0";

        Assert.True(viewModel.IsPartial);
        Assert.Equal(0m, zero.EffectiveRaw);
        Assert.Equal("0", zero.EffectiveRawText);
        Assert.Equal(0m, zero.NormalizedScore);
        viewModel.GoToRowNumber = 11;
        viewModel.GoToRowCommand.Execute(null);

        Assert.Equal(2, viewModel.RowScores.Count);
        Assert.All(viewModel.SelectedRowCriteria, criterion =>
        {
            Assert.Equal(ResultsStatusCodes.Cancelled, criterion.StatusCode);
            Assert.False(criterion.CanOverride);
            Assert.Null(criterion.AiRawScore);
            Assert.Null(criterion.EffectiveRaw);
            Assert.Null(criterion.NormalizedScore);
            Assert.Null(criterion.OverallScore);
            Assert.Equal("—", criterion.EffectiveRawText);
            Assert.Equal("—", criterion.OverallScoreText);
        });
        Assert.True(viewModel.HasUnsavedOverrides);
    }

    [Fact]
    public async Task Empty_primary_rejects_override_without_marking_results_dirty()
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync(rowCount: 1, primary: "   ");

        Assert.All(viewModel.SelectedRowCriteria, criterion =>
        {
            Assert.Equal(ResultsStatusCodes.Empty, criterion.StatusCode);
            Assert.False(criterion.CanOverride);
            Assert.Null(criterion.EffectiveRaw);
            Assert.Equal("—", criterion.EffectiveRawText);
            criterion.OverrideText = "0";
            Assert.Equal(string.Empty, criterion.OverrideText);
        });

        Assert.Single(viewModel.VisibleRowScores);
        Assert.False(viewModel.HasOverrideErrors);
        Assert.False(viewModel.HasUnsavedOverrides);
    }

    [Fact]
    public async Task Invalid_override_keeps_the_selected_student_and_correction_editor_accessible()
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync();
        viewModel.GoToRowNumber = 16;
        viewModel.GoToRowCommand.Execute(null);
        viewModel.ShowDetailCommand.Execute(null);
        ResultsCriterionViewModel editor = viewModel.SelectedRowCriteria[0];
        const string expectedInputState = "run 終了時の exact hash / size / mtime は一致しています。final commit 直前にも再確認します。";
        Assert.Equal(expectedInputState, viewModel.InputStateText);

        editor.OverrideText = "not a number";

        Assert.True(viewModel.HasOverrideErrors);
        Assert.True(viewModel.HasUnsavedOverrides);
        Assert.False(viewModel.CanExport);
        Assert.True(viewModel.IsInputUnchanged);
        Assert.Equal(expectedInputState, viewModel.InputStateText);
        Assert.Equal(9, viewModel.RowScores.Count);
        Assert.Equal(18, viewModel.Results.Count);
        Assert.Equal(1, viewModel.PageIndex);
        Assert.Equal(4, viewModel.VisibleRowScores.Count);
        Assert.Same(viewModel.RowScores[6], viewModel.SelectedRow);
        Assert.Same(editor, viewModel.SelectedRowCriteria[0]);
        Assert.True(viewModel.IsDetailVisible);
        Assert.True(editor.HasOverrideError);
        Assert.Null(editor.EffectiveRaw);
        Assert.All(viewModel.RowScores, row =>
        {
            Assert.Null(row.FinalScore);
            Assert.Equal("—", row.FinalScoreText);
            Assert.Equal(ResultsRowStatus.Success, row.Status);
        });

        editor.OverrideText = "7";

        Assert.False(viewModel.HasOverrideErrors);
        Assert.True(viewModel.CanExport);
        Assert.Equal(7m, editor.EffectiveRaw);
        Assert.Same(editor, viewModel.SelectedRowCriteria[0]);
        Assert.Same(viewModel.RowScores[6], viewModel.SelectedRow);
        Assert.Equal(9, viewModel.RowScores.Count);
        Assert.True(viewModel.HasUnsavedOverrides);
    }

    [Fact]
    public async Task Invalid_overrides_across_pages_select_each_criterion_and_clear_after_correction()
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync();
        ResultsCriterionViewModel[] errors =
        [
            viewModel.Results.Single(item => item.SourceRowNumber == 10 && item.CriterionId == "C2"),
            viewModel.Results.Single(item => item.SourceRowNumber == 18 && item.CriterionId == "C1"),
            viewModel.Results.Single(item => item.SourceRowNumber == 18 && item.CriterionId == "C2"),
        ];
        List<int> counts = [];
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ResultsOutputViewModel.OverrideErrorCount))
            {
                counts.Add(viewModel.OverrideErrorCount);
            }
        };
        foreach (ResultsCriterionViewModel item in errors)
        {
            item.OverrideText = "11";
        }

        Assert.Equal(3, viewModel.OverrideErrorCount);
        Assert.Equal("エラー 3 件・次へ", viewModel.OverrideErrorNavigationText);
        Assert.Equal(0, viewModel.PageIndex);
        Assert.False(viewModel.IsDetailVisible);
        foreach (ResultsCriterionViewModel target in errors.Append(errors[0]))
        {
            Assert.True(viewModel.NextOverrideErrorCommand.CanExecute(null));
            viewModel.NextOverrideErrorCommand.Execute(null);

            Assert.Equal((target.SourceRowNumber - 10) / viewModel.PageSize, viewModel.PageIndex);
            Assert.Equal(target.SourceRowNumber, viewModel.SelectedRow?.SourceRowNumber);
            Assert.Contains(viewModel.VisibleRowScores, row => ReferenceEquals(row, viewModel.SelectedRow));
            Assert.Same(target, viewModel.SelectedCriterion);
            Assert.Contains(target, viewModel.SelectedRowCriteria);
            Assert.True(viewModel.IsDetailVisible);
            Assert.False(viewModel.CanExport);
        }

        errors[0].OverrideText = "8";
        viewModel.NextOverrideErrorCommand.Execute(null);
        Assert.Same(errors[1], viewModel.SelectedCriterion);
        errors[1].OverrideText = "0";
        viewModel.NextOverrideErrorCommand.Execute(null);
        Assert.Same(errors[2], viewModel.SelectedCriterion);
        errors[2].OverrideText = string.Empty;

        Assert.Equal([1, 2, 3, 2, 1, 0], counts);
        Assert.False(viewModel.HasOverrideErrors);
        Assert.True(viewModel.CanExport);
        Assert.False(viewModel.NextOverrideErrorCommand.CanExecute(null));
        viewModel.NextOverrideErrorCommand.Execute(null);
        Assert.Same(errors[2], viewModel.SelectedCriterion);
        Assert.Equal(18, viewModel.SelectedRow?.SourceRowNumber);
        Assert.Equal(8m, errors[0].EffectiveRaw);
        Assert.Equal(0m, errors[1].EffectiveRaw);
        Assert.Equal(5m, errors[2].EffectiveRaw);
        Assert.True(viewModel.HasUnsavedOverrides);
    }

    [Fact]
    public async Task Successful_output_clears_dirty_but_subsequent_edits_are_unsaved()
    {
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync(output: output);
        ResultsCriterionViewModel first = viewModel.Results[0];
        Assert.False(viewModel.HasUnsavedOverrides);
        first.OverrideText = "8";
        viewModel.GoToRowNumber = 18;
        viewModel.GoToRowCommand.Execute(null);
        ResultsCriterionViewModel last = viewModel.SelectedRowCriteria[1];
        last.OverrideText = "0";
        Assert.True(viewModel.HasUnsavedOverrides);

        await viewModel.ExportAsync(TestContext.Current.CancellationToken);

        Assert.False(viewModel.HasUnsavedOverrides);
        Assert.Equal(ResultsOutputStatusCodes.Success, viewModel.LastExportCode);
        ResultsOutputRequest request = Assert.IsType<ResultsOutputRequest>(output.LastRequest);
        Assert.Equal(2, request.Overrides.Length);
        Assert.Contains(request.Overrides, item =>
            item.SourceRowNumber == 10 && item.CriterionId == "C1" && item.Value == "8");
        Assert.Contains(request.Overrides, item =>
            item.SourceRowNumber == 18 && item.CriterionId == "C2" && item.Value == "0");
        Assert.Equal("8", first.OverrideText);
        Assert.Equal("0", last.OverrideText);

        viewModel.OutputPath = Path.Combine(Path.GetTempPath(), "synthetic-revised.xlsx");
        viewModel.PageSize = 6;
        viewModel.GoToRowNumber = 10;
        viewModel.GoToRowCommand.Execute(null);
        viewModel.ShowDetailCommand.Execute(null);
        viewModel.ShowListCommand.Execute(null);
        first.OverrideText = "8";
        Assert.False(viewModel.HasUnsavedOverrides);

        first.OverrideText = string.Empty;
        Assert.True(viewModel.HasUnsavedOverrides);
        Assert.Equal(5m, first.EffectiveRaw);
        Assert.Equal(1, output.ExportCount);
    }

    [Theory]
    [InlineData(ResultsOutputStatusCodes.ExportFailed)]
    [InlineData(ResultsOutputStatusCodes.OutputInvalid)]
    [InlineData(ResultsOutputStatusCodes.Cancelled)]
    [InlineData(ResultsOutputStatusCodes.TargetExists)]
    public async Task Failed_or_cancelled_output_keeps_unsaved_overrides(string code)
    {
        RecordingOutputBoundary output = new()
        {
            Export = (_, _) => Task.FromResult(new ResultsOutputResult(code)),
        };
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync(output: output);
        ResultsCriterionViewModel editor = viewModel.SelectedRowCriteria[0];
        editor.OverrideText = "8";

        await viewModel.ExportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, output.ExportCount);
        Assert.Equal(code, viewModel.LastExportCode);
        Assert.Equal(string.Empty, viewModel.LastSuccessfulExportPath);
        Assert.False(viewModel.HasSuccessfulExport);
        Assert.True(viewModel.HasUnsavedOverrides);
        Assert.Equal("8", editor.OverrideText);
        Assert.Same(editor, viewModel.SelectedRowCriteria[0]);
    }

    [Fact]
    public async Task Edits_during_output_remain_dirty_when_the_earlier_snapshot_succeeds()
    {
        TaskCompletionSource<ResultsOutputResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingOutputBoundary output = new() { Export = (_, _) => release.Task };
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync(output: output);
        ResultsCriterionViewModel editor = viewModel.SelectedRowCriteria[0];
        editor.OverrideText = "8";

        Task export = viewModel.ExportAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(viewModel.IsExporting);
            Assert.True(viewModel.HasUnsavedOverrides);
            ResultsOutputRequest request = Assert.IsType<ResultsOutputRequest>(output.LastRequest);
            Assert.Equal("8", Assert.Single(request.Overrides).Value);
            editor.OverrideText = "7";
        }
        finally
        {
            release.TrySetResult(new ResultsOutputResult(ResultsOutputStatusCodes.Success));
        }

        await export;

        Assert.False(viewModel.IsExporting);
        Assert.Equal(ResultsOutputStatusCodes.Success, viewModel.LastExportCode);
        Assert.True(viewModel.HasUnsavedOverrides);
        Assert.Equal("7", editor.OverrideText);
        await viewModel.ExportAsync(TestContext.Current.CancellationToken);
        Assert.False(viewModel.HasUnsavedOverrides);
        Assert.Equal(2, output.ExportCount);
    }

    [Fact]
    public async Task Loading_a_new_run_resets_presentation_and_ignores_old_editors_and_late_output()
    {
        TaskCompletionSource<ResultsOutputResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingOutputBoundary output = new() { Export = (_, _) => release.Task };
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync(output: output);
        ExecutionRunContext nextContext = await CreateContextAsync(firstDataRow: 30, rowCount: 2);
        viewModel.GoToRowNumber = 18;
        viewModel.GoToRowCommand.Execute(null);
        viewModel.ShowDetailCommand.Execute(null);
        ResultsCriterionViewModel oldEditor = viewModel.SelectedRowCriteria[0];
        oldEditor.OverrideText = "8";

        Task export = viewModel.ExportAsync(TestContext.Current.CancellationToken);
        try
        {
            viewModel.Load(nextContext);

            Assert.Equal(0, viewModel.PageIndex);
            Assert.Null(viewModel.GoToRowNumber);
            Assert.False(viewModel.IsDetailVisible);
            Assert.False(viewModel.HasUnsavedOverrides);
            Assert.Equal(30, viewModel.SelectedRow?.SourceRowNumber);
            Assert.Equal(2, viewModel.RowScores.Count);
            Assert.Equal(4, viewModel.Results.Count);
            oldEditor.OverrideText = "9";
            Assert.False(viewModel.HasUnsavedOverrides);
            Assert.All(viewModel.Results, criterion => Assert.Equal(string.Empty, criterion.OverrideText));

            viewModel.SelectedRowCriteria[0].OverrideText = "4";
            Assert.True(viewModel.HasUnsavedOverrides);
        }
        finally
        {
            release.TrySetResult(new ResultsOutputResult(ResultsOutputStatusCodes.Success));
        }

        await export;

        Assert.Equal(ResultsOutputStatusCodes.Ready, viewModel.LastExportCode);
        Assert.Equal(string.Empty, viewModel.LastSuccessfulExportPath);
        Assert.False(viewModel.HasSuccessfulExport);
        Assert.True(viewModel.HasUnsavedOverrides);
        Assert.Equal("4", viewModel.SelectedRowCriteria[0].OverrideText);
        Assert.Equal(30, viewModel.SelectedRow?.SourceRowNumber);
    }

    [Fact]
    public async Task Presentation_changes_notify_bindings_and_disposal_disables_commands()
    {
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync();
        List<string?> changed = [];
        int previousChanges = 0;
        int jumpChanges = 0;
        int detailChanges = 0;
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        viewModel.PreviousPageCommand.CanExecuteChanged += (_, _) => previousChanges++;
        viewModel.GoToRowCommand.CanExecuteChanged += (_, _) => jumpChanges++;
        viewModel.ShowDetailCommand.CanExecuteChanged += (_, _) => detailChanges++;

        viewModel.PageIndex = 1;
        viewModel.GoToRowNumber = 18;
        viewModel.ShowDetailCommand.Execute(null);
        viewModel.PageSize = 3;
        viewModel.Results[0].OverrideText = "8";

        Assert.Contains(nameof(ResultsOutputViewModel.PageIndex), changed);
        Assert.Contains(nameof(ResultsOutputViewModel.PageSize), changed);
        Assert.Contains(nameof(ResultsOutputViewModel.PageSummary), changed);
        Assert.Contains(nameof(ResultsOutputViewModel.SelectedRow), changed);
        Assert.Contains(nameof(ResultsOutputViewModel.GoToRowNumber), changed);
        Assert.Contains(nameof(ResultsOutputViewModel.IsDetailVisible), changed);
        Assert.Contains(nameof(ResultsOutputViewModel.HasUnsavedOverrides), changed);
        Assert.True(previousChanges > 0);
        Assert.True(jumpChanges > 0);
        Assert.True(detailChanges > 0);

        int pageIndex = viewModel.PageIndex;
        viewModel.Dispose();
        Assert.All(PresentationCommands(viewModel), command =>
        {
            Assert.False(command.CanExecute(null));
            command.Execute(null);
        });
        Assert.Equal(pageIndex, viewModel.PageIndex);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_zero_point_questions_load_and_override_using_only_positive_legacy_weights(bool mixedPoints)
    {
        ExecutionRunContext context = await CreateLegacyZeroPointContextAsync(mixedPoints);
        RunSummary summary = context.Summary;
        Assert.False(summary.IsDurable);
        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        Assert.Empty(summary.CompletedRows);
        Assert.Single(summary.Snapshot.Definition.Questions, question => question.Enabled && question.Points == 0m);
        string originalDefinition = summary.Snapshot.CanonicalJson;
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary());

        viewModel.Load(context);

        Assert.False(viewModel.IsAutomaticOutput);
        Assert.False(viewModel.HasUnsavedOverrides);
        Assert.Equal(mixedPoints ? 2 : 1, viewModel.Results.Count);
        decimal? expectedOverall = mixedPoints ? 50m : null;
        string expectedEarned = mixedPoints ? "Question text Q0: 0 · Question text Q1: 40" : "Question text Q0: 0";
        Assert.Equal(expectedEarned, Assert.Single(viewModel.RowScores).QuestionEarnedText);
        Assert.Null(viewModel.RowScores[0].FinalScore);
        Assert.All(viewModel.Results, item =>
        {
            Assert.Equal(5m, item.AiRawScore);
            Assert.Equal(50m, item.NormalizedScore);
            Assert.Equal(50m, item.EvaluatorScore);
            Assert.Equal(50m, item.QuestionScore);
            Assert.Equal(expectedOverall, item.OverallScore);
        });
        ResultsCriterionViewModel zero = viewModel.Results.Single(item => item.QuestionId == "Q0");
        Assert.True(zero.CanOverride);

        zero.OverrideText = "8";

        Assert.Equal(8m, zero.EffectiveRaw);
        Assert.Equal(80m, zero.NormalizedScore);
        Assert.Equal(80m, zero.EvaluatorScore);
        Assert.Equal(80m, zero.QuestionScore);
        Assert.Equal(expectedEarned, viewModel.RowScores[0].QuestionEarnedText);
        Assert.All(viewModel.Results, item => Assert.Equal(expectedOverall, item.OverallScore));
        if (mixedPoints)
        {
            ResultsCriterionViewModel positive = viewModel.Results.Single(item => item.QuestionId == "Q1");
            positive.OverrideText = "11";
            Assert.True(viewModel.HasOverrideErrors);
            Assert.Null(positive.EffectiveRaw);
            Assert.Equal("Question text Q0: 0 · Question text Q1: —", viewModel.RowScores[0].QuestionEarnedText);
            Assert.All(viewModel.Results, item =>
            {
                Assert.Null(item.OverallScore);
                Assert.Equal("—", item.OverallScoreText);
            });

            positive.OverrideText = "10";
            Assert.Equal(100m, positive.QuestionScore);
            expectedOverall = 100m;
            expectedEarned = "Question text Q0: 0 · Question text Q1: 80";
        }

        Assert.False(viewModel.HasOverrideErrors);
        Assert.True(viewModel.HasUnsavedOverrides);
        Assert.True(viewModel.CanExport);
        Assert.All(viewModel.Results, item =>
        {
            Assert.Equal(expectedOverall, item.OverallScore);
            Assert.Equal(expectedOverall?.ToString(CultureInfo.InvariantCulture) ?? "—", item.OverallScoreText);
        });
        ResultsRowScoreViewModel row = Assert.Single(viewModel.RowScores);
        Assert.Equal(ResultsRowStatus.Success, row.Status);
        Assert.Equal("成功", row.StatusText);
        Assert.Equal(expectedEarned, row.QuestionEarnedText);
        Assert.Equal(0m, row.SpecialEarned);
        // Legacy has no similarity result, even for zero-point questions. Do not substitute
        // the base/final score for its normalized overall or manufacture an earned zero.
        Assert.Null(row.SimilarityPenalty);
        Assert.Null(row.FinalRaw);
        Assert.Null(row.FinalScore);
        Assert.Equal("—", row.FinalRawText);
        Assert.Equal("—", row.FinalScoreText);
        Assert.Equal(originalDefinition, summary.Snapshot.CanonicalJson);
        Assert.All(summary.Units, unit => Assert.Equal(5m, Assert.Single(unit.AcceptedResult!.Criteria).RawScore));
    }

    [Theory]
    [InlineData(false, ResultsStatusCodes.Empty)]
    [InlineData(true, ResultsStatusCodes.Empty)]
    [InlineData(false, ResultsStatusCodes.NetworkFailed)]
    [InlineData(true, ResultsStatusCodes.NetworkFailed)]
    public async Task Legacy_zero_point_questions_preserve_empty_and_technical_error_blanks(bool mixedPoints, string statusCode)
    {
        ExecutionRunContext context = await CreateLegacyZeroPointContextAsync(mixedPoints, statusCode);
        Assert.False(context.Summary.IsDurable);
        Assert.All(context.Summary.Units, unit => Assert.Equal(statusCode, unit.StatusCode));
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary());

        viewModel.Load(context);

        bool empty = statusCode == ResultsStatusCodes.Empty;
        Assert.All(viewModel.Results, item =>
        {
            Assert.Equal(statusCode, item.StatusCode);
            Assert.Equal(!empty, item.CanOverride);
            Assert.Null(item.AiRawScore);
            Assert.Null(item.EffectiveRaw);
            Assert.Null(item.NormalizedScore);
            Assert.Null(item.EvaluatorScore);
            Assert.Null(item.QuestionScore);
            Assert.Null(item.OverallScore);
            Assert.Equal("—", item.EffectiveRawText);
            Assert.Equal("—", item.OverallScoreText);
            if (empty)
            {
                item.OverrideText = "0";
                Assert.Equal(string.Empty, item.OverrideText);
            }
        });
        ResultsRowScoreViewModel row = Assert.Single(viewModel.RowScores);
        Assert.Equal(empty ? ResultsRowStatus.Empty : ResultsRowStatus.TechnicalError, row.Status);
        Assert.Equal(empty ? "回答空欄" : "技術エラー", row.StatusText);
        Assert.Equal(0m, row.SpecialEarned);
        if (empty)
        {
            Assert.Equal(mixedPoints ? "Question text Q0: 0 · Question text Q1: 0" : "Question text Q0: 0", row.QuestionEarnedText);
            Assert.Equal(0m, row.SimilarityPenalty);
            Assert.Equal(mixedPoints ? 20m : 100m, row.FinalRaw);
            Assert.Equal(mixedPoints ? 20m : 100m, row.FinalScore);
        }
        else
        {
            Assert.Equal(mixedPoints ? "Question text Q0: — · Question text Q1: —" : "Question text Q0: —", row.QuestionEarnedText);
            Assert.Null(row.SimilarityPenalty);
            Assert.Null(row.FinalRaw);
            Assert.Null(row.FinalScore);
            Assert.Equal("—", row.FinalRawText);
            Assert.Equal("—", row.FinalScoreText);
        }

        Assert.False(viewModel.HasOverrideErrors);
        Assert.False(viewModel.HasUnsavedOverrides);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Durable_zero_point_questions_load_and_override_without_legacy_aggregation(bool mixedPoints)
    {
        ExecutionRunContext context = await new ResultsDurableFixture().RunAsync(
            basePoints: mixedPoints ? 20m : 100m,
            includeZeroPointQuestion: mixedPoints);
        RunSummary summary = context.Summary;
        Assert.True(summary.IsDurable);
        Assert.Equal(QuantificationRunStatusCodes.Success, summary.StatusCode);
        Assert.Single(summary.CompletedRows);
        Assert.Equal(mixedPoints ? 2 : 1, summary.Snapshot.Definition.Questions.Length);
        Assert.Single(summary.Snapshot.Definition.Questions, question => question.Points == 0m);
        string originalDefinition = summary.Snapshot.CanonicalJson;
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary());

        viewModel.Load(context);

        Assert.Equal(mixedPoints ? 60m : 100m, Assert.Single(viewModel.RowScores).FinalScore);
        Assert.Equal(mixedPoints ? "Question text Q0: 0 · Question text Q1: 40" : "Question text Q1: 0", viewModel.RowScores[0].QuestionEarnedText);
        ResultsCriterionViewModel zero = viewModel.Results.Single(item => item.QuestionId == (mixedPoints ? "Q0" : "Q1"));
        Assert.Equal(5m, zero.AiRawScore);
        Assert.True(zero.CanOverride);

        zero.OverrideText = "8";

        Assert.Equal(80m, zero.QuestionScore);
        Assert.Equal(mixedPoints ? 60m : 100m, viewModel.RowScores[0].FinalScore);
        Assert.Equal(mixedPoints ? "Question text Q0: 0 · Question text Q1: 40" : "Question text Q1: 0", viewModel.RowScores[0].QuestionEarnedText);
        if (mixedPoints)
        {
            viewModel.Results.Single(item => item.QuestionId == "Q1").OverrideText = "10";
            Assert.Equal("Question text Q0: 0 · Question text Q1: 80", viewModel.RowScores[0].QuestionEarnedText);
        }

        Assert.Equal(100m, viewModel.RowScores[0].FinalRaw);
        Assert.Equal(100m, viewModel.RowScores[0].FinalScore);
        Assert.All(viewModel.Results, item => Assert.Equal(100m, item.OverallScore));
        Assert.False(viewModel.HasOverrideErrors);
        Assert.True(viewModel.CanExport);
        Assert.Equal(originalDefinition, summary.Snapshot.CanonicalJson);
        Assert.All(summary.Units, unit => Assert.Equal(5m, Assert.Single(unit.AcceptedResult!.Criteria).RawScore));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Durable_output_or_checkpoint_failure_preserves_completed_scores_and_blocks_export(bool checkpointFailure)
    {
        ResultsDurableFixture fixture = new()
        {
            FinalizationCode = ResultsOutputStatusCodes.OutputInvalid,
            CheckpointFailureCode = checkpointFailure ? CheckpointStatusCodes.SaveFailed : null,
            CheckpointFailureAtRowCount = 2,
        };
        ExecutionRunContext context = await fixture.RunAsync(rowCount: 2, specialPoints: 10m);
        RunSummary summary = context.Summary;
        string expectedStatus = checkpointFailure
            ? QuantificationRunStatusCodes.CheckpointFailed
            : QuantificationRunStatusCodes.OutputInvalid;
        Assert.Equal(expectedStatus, summary.StatusCode);
        Assert.Equal(checkpointFailure ? 1 : 2, summary.CompletedRows.Length);
        Assert.Equal(checkpointFailure ? 0 : 1, fixture.FinalizerCalls);
        Assert.Null(summary.FinalPath);
        Assert.False(summary.PrepareOutput().IsExportReady);
        Assert.Empty(summary.PrepareOutput().Rows);
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel viewModel = new(output, context);

        foreach (CheckpointCompletedRow completed in summary.CompletedRows)
        {
            ResultsRowScoreViewModel row = viewModel.RowScores.Single(item => item.SourceRowNumber == completed.SourceRowNumber);
            Assert.Equal(ResultsRowStatus.Success, row.Status);
            Assert.Equal("Question text Q1: 35", row.QuestionEarnedText);
            Assert.Equal(8m, row.SpecialEarned);
            Assert.Equal(0m, row.SimilarityPenalty);
            Assert.Equal(63m, row.FinalRaw);
            Assert.Equal(63m, row.FinalScore);
            Assert.Equal(63m, viewModel.Results.Single(item => item.SourceRowNumber == completed.SourceRowNumber).OverallScore);
        }

        if (checkpointFailure)
        {
            ResultsRowScoreViewModel unfinished = viewModel.RowScores[1];
            Assert.Equal(ResultsRowStatus.Unprocessed, unfinished.Status);
            Assert.Equal("Question text Q1: —", unfinished.QuestionEarnedText);
            Assert.Null(unfinished.SpecialEarned);
            Assert.Null(unfinished.SimilarityPenalty);
            Assert.Null(unfinished.FinalRaw);
            Assert.Null(unfinished.FinalScore);
            Assert.False(viewModel.Results[1].CanOverride);
            Assert.False(summary.Units[1].ScorableKnown);
        }

        viewModel.Results[0].OverrideText = "11";
        Assert.Null(viewModel.RowScores[0].FinalScore);
        Assert.Equal(checkpointFailure ? (decimal?)null : 63m, viewModel.RowScores[1].FinalScore);
        viewModel.Results[0].OverrideText = "8";
        Assert.Equal("Question text Q1: 56", viewModel.RowScores[0].QuestionEarnedText);
        Assert.Equal(84m, viewModel.RowScores[0].FinalRaw);
        Assert.Equal(84m, viewModel.RowScores[0].FinalScore);
        Assert.False(viewModel.HasOverrideErrors);
        Assert.False(viewModel.CanExport);
        await viewModel.ExportAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, output.ExportCount);
        Assert.Equal(expectedStatus, summary.StatusCode);
        Assert.Empty(summary.PrepareOutput().Rows);
        Assert.Equal(string.Empty, viewModel.LastSuccessfulExportPath);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(20, 60)]
    public async Task Durable_unprocessed_similarity_stays_blank_instead_of_becoming_empty_answer_zero(
        int basePoints,
        int completedScore)
    {
        ResultsDurableFixture fixture = new();
        ExecutionRunContext context = await fixture.RunAsync(rowCount: 2, basePoints: basePoints, cancelAfterRows: 1);
        RunSummary summary = context.Summary;
        Assert.True(summary.IsDurable);
        Assert.Equal(QuantificationRunStatusCodes.Cancelled, summary.StatusCode);
        Assert.Equal(ResultsStatusCodes.Success, Assert.Single(summary.References).StatusCode);
        Assert.Single(summary.CompletedRows);
        Assert.Equal([10], fixture.ReadRows);
        Assert.Equal(0, fixture.FinalizerCalls);
        QuestionResultInput unfinishedInput = Assert.Single(summary.PrepareOutput().Rows
            .Single(row => row.SourceRowNumber == 11).Questions);
        SimilarityResultInput unfinishedSimilarity = Assert.IsType<SimilarityResultInput>(unfinishedInput.Similarity);
        Assert.False(unfinishedInput.Scorable);
        Assert.Equal(ResultsStatusCodes.Cancelled, unfinishedSimilarity.Status);
        Assert.Null(unfinishedSimilarity.AiRaw);
        Assert.All(summary.Units.Where(unit => unit.Item.SourceRowNumber == 11), unit =>
        {
            Assert.Equal(ResultsStatusCodes.Cancelled, unit.StatusCode);
            Assert.Equal(0, unit.AttemptCount);
            Assert.False(unit.ScorableKnown);
        });

        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), context);
        ResultsRowScoreViewModel completed = viewModel.RowScores[0];
        Assert.Equal(ResultsRowStatus.Success, completed.Status);
        Assert.Equal((decimal)completedScore, completed.FinalRaw);
        Assert.Equal((decimal)completedScore, completed.FinalScore);
        ResultsRowScoreViewModel unfinished = viewModel.RowScores[1];
        Assert.Equal(ResultsRowStatus.Unprocessed, unfinished.Status);
        Assert.Equal("未処理・未確定", unfinished.StatusText);
        // Workbook Answer_Present=0 remains zero; the UI must distinguish unknown input
        // from a completed empty answer. The fixed zero special budget is unchanged.
        Assert.Equal("Question text Q1: —", unfinished.QuestionEarnedText);
        Assert.Equal(0m, unfinished.SpecialEarned);
        Assert.Null(unfinished.SimilarityPenalty);
        Assert.Null(unfinished.FinalRaw);
        Assert.Null(unfinished.FinalScore);
        Assert.Equal("—", unfinished.FinalRawText);
        Assert.Equal("—", unfinished.FinalScoreText);
        Assert.All(viewModel.Results.Where(item => item.SourceRowNumber == 11), item =>
        {
            Assert.Null(item.AiRawScore);
            Assert.Null(item.EffectiveRaw);
            Assert.Null(item.NormalizedScore);
            Assert.Null(item.EvaluatorScore);
            Assert.Null(item.QuestionScore);
            Assert.Null(item.OverallScore);
            Assert.False(item.CanOverride);
        });

        ExecutionRunContext emptyContext = await new ResultsDurableFixture().RunAsync(primary: "   ", basePoints: basePoints);
        QuestionResultInput emptyInput = Assert.Single(Assert.Single(emptyContext.Summary.PrepareOutput().Rows).Questions);
        SimilarityResultInput emptySimilarity = Assert.IsType<SimilarityResultInput>(emptyInput.Similarity);
        Assert.Equal(ResultsStatusCodes.Empty, emptySimilarity.Status);
        Assert.Equal(0m, emptySimilarity.AiRaw);
        using ResultsOutputViewModel emptyViewModel = new(new RecordingOutputBoundary(), emptyContext);
        ResultsRowScoreViewModel empty = Assert.Single(emptyViewModel.RowScores);
        Assert.Equal(ResultsRowStatus.Empty, empty.Status);
        Assert.Equal("回答空欄", empty.StatusText);
        Assert.Equal("Question text Q1: 0", empty.QuestionEarnedText);
        Assert.Equal(0m, empty.SpecialEarned);
        Assert.Equal(0m, empty.SimilarityPenalty);
        Assert.Equal((decimal)basePoints, empty.FinalRaw);
        Assert.Equal((decimal)basePoints, empty.FinalScore);
        Assert.Null(Assert.Single(emptyViewModel.Results).QuestionScore);
        Assert.Equal((decimal)basePoints, Assert.Single(emptyViewModel.Results).OverallScore);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("special")]
    [InlineData("row-read")]
    public async Task Durable_row_status_includes_auxiliary_and_row_read_failures_not_score_inference(string operation)
    {
        ResultsDurableFixture fixture = new()
        {
            NormalStatusCode = operation == "normal" ? ResultsStatusCodes.NetworkFailed : ResultsStatusCodes.Success,
            SpecialStatusCode = operation == "special" ? ResultsStatusCodes.AiTimeout : ResultsStatusCodes.Success,
            FailRowRead = operation == "row-read",
        };
        ExecutionRunContext context = await fixture.RunAsync(specialPoints: 10m);
        Assert.Single(context.Summary.CompletedRows);
        Assert.False(context.Summary.IsPartial);
        if (operation is "special")
        {
            Assert.Equal(ResultsStatusCodes.Success, Assert.Single(context.Summary.Units).StatusCode);
        }

        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), context);
        ResultsRowScoreViewModel row = Assert.Single(viewModel.RowScores);
        Assert.Equal(ResultsRowStatus.TechnicalError, row.Status);
        Assert.Equal("技術エラー", row.StatusText);
        Assert.Null(row.FinalRaw);
        Assert.Null(row.FinalScore);
        if (operation == "normal")
        {
            Assert.Single(viewModel.Results).OverrideText = "0";
            row = Assert.Single(viewModel.RowScores);
            Assert.Equal(28m, row.FinalScore);
            Assert.Equal(ResultsRowStatus.TechnicalError, row.Status);
        }
    }

    [Fact]
    public async Task Legacy_dispatched_cancellation_is_distinct_from_undispatched_units_and_blank_success()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(10, 11);
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        RunSummary summary = await new QuantificationOrchestrator(
            new ScriptedRowSource((request, _) => Task.FromResult(new EvaluationRowData(
                request.SourceRowNumber, new Dictionary<string, string?> { ["A"] = "synthetic answer", ["B"] = "synthetic support" }))),
            new ScriptedRunner((_, _, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult(EvaluationRunnerResult.Failed(ResultsStatusCodes.Cancelled, attemptCount: 1));
            }),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot())).RunAsync(
                new QuantificationRunRequest
                {
                    DraftDefinition = definition,
                    WorkbookMetadata = U01TestSupport.ValidateMapping(definition).Metadata,
                    InputPath = Path.Combine(Path.GetTempPath(), "synthetic-T21-legacy-cancel.xlsx"),
                    ModelId = "model-test",
                    MaximumPromptTokens = 64_000,
                    MaximumContextWindowTokens = 128_000,
                    MaxConcurrency = 1,
                }, cancellationToken: cancellation.Token);
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), U04TestSupport.Context(summary));
        Assert.True(summary.Units[0].ScorableKnown);
        Assert.Equal(1, summary.Units[0].AttemptCount);
        Assert.False(summary.Units[1].ScorableKnown);
        Assert.Equal(0, summary.Units[1].AttemptCount);
        Assert.Equal(
            "run 終了時の exact hash / size / mtime は一致しています。final commit 直前にも再確認します。",
            viewModel.InputStateText);
        Assert.Equal(ResultsRowStatus.Cancelled, viewModel.RowScores[0].Status);
        Assert.Equal("取消", viewModel.RowScores[0].StatusText);
        Assert.Equal(ResultsRowStatus.Unprocessed, viewModel.RowScores[1].Status);
        Assert.Equal("Question text Q1: —", viewModel.RowScores[0].QuestionEarnedText);
        Assert.Equal("Question text Q1: —", viewModel.RowScores[1].QuestionEarnedText);
        Assert.All(viewModel.Results, item => Assert.Null(item.OverallScore));

        using ResultsOutputViewModel legacySuccess = await CreateViewModelAsync(rowCount: 1);
        ResultsRowScoreViewModel legacyRow = Assert.Single(legacySuccess.RowScores);
        Assert.Equal(ResultsRowStatus.Success, legacyRow.Status);
        // Legacy has no similarity object: keep its existing final blank and normalized overall.
        Assert.Null(legacyRow.FinalScore);
        Assert.All(legacySuccess.Results, item => Assert.Equal(50m, item.OverallScore));
    }

    [Theory]
    [InlineData("final", QuantificationRunStatusCodes.Success, true, true, 1,
        "run終了時とfinal commit直前のexact hash / size / mtimeを確認しました。")]
    [InlineData("cleanup-warning", QuantificationRunStatusCodes.Success, true, true, 1,
        "run終了時とfinal commit直前のexact hash / size / mtimeを確認しました。")]
    [InlineData("partial", QuantificationRunStatusCodes.Cancelled, true, true, 0,
        "run開始時の入力snapshotは取得済みです。取消による部分結果で、run終了時・final commit直前の不変性確認は未実施です。")]
    [InlineData("checkpoint-failed", QuantificationRunStatusCodes.CheckpointFailed, true, false, 0,
        "run開始時の入力snapshotは取得済みです。checkpoint保存に失敗したため、run終了時・final commit直前の不変性は未確認です。")]
    [InlineData("output-failed", QuantificationRunStatusCodes.OutputInvalid, true, false, 1,
        "run終了時のexact hash / size / mtimeは一致しています。final出力に失敗したため、commit直前の再確認・保存成功は未確認です。")]
    [InlineData("final-cancelled", QuantificationRunStatusCodes.Cancelled, true, true, 1,
        "run終了時のexact hash / size / mtimeは一致しています。final出力を取り消したため、commit直前の再確認・保存成功は未確認です。")]
    [InlineData("input-changed", QuantificationRunStatusCodes.InputChanged, false, false, 1,
        "入力変更を検出しました（INPUT_CHANGED）。出力は停止されています。")]
    [InlineData("final-input-changed", QuantificationRunStatusCodes.InputChanged, false, false, 1,
        "入力変更を検出しました（INPUT_CHANGED）。出力は停止されています。")]
    public async Task Input_state_uses_known_run_stages_not_export_readiness_or_new_filesystem_queries(
        string scenario,
        string expectedStatus,
        bool inputUnchanged,
        bool canExport,
        int recheckCount,
        string expectedText)
    {
        ResultsDurableFixture fixture = new()
        {
            CheckpointFailureCode = scenario == "checkpoint-failed" ? CheckpointStatusCodes.SaveFailed : null,
            CleanupSucceeds = scenario != "cleanup-warning",
            FinalizationCode = scenario switch
            {
                "output-failed" => ResultsOutputStatusCodes.OutputInvalid,
                "final-cancelled" => ResultsOutputStatusCodes.Cancelled,
                "final-input-changed" => ResultsOutputStatusCodes.InputChanged,
                _ => ResultsOutputStatusCodes.Success,
            },
        };
        fixture.InputSnapshots.Unchanged = scenario != "input-changed";
        ExecutionRunContext context = await fixture.RunAsync(rowCount: 2, cancelAfterRows: scenario == "partial" ? 1 : null);
        Assert.Equal(expectedStatus, context.Summary.StatusCode);
        Assert.Equal(1, fixture.InputSnapshots.CaptureCount);
        Assert.Equal(recheckCount, fixture.InputSnapshots.RecheckCount);
        using ResultsOutputViewModel viewModel = new(new RecordingOutputBoundary(), context);

        for (int read = 0; read < 3; read++)
        {
            Assert.Equal(inputUnchanged, viewModel.IsInputUnchanged);
            Assert.Equal(canExport, viewModel.CanExport);
            Assert.Equal(expectedText, viewModel.InputStateText);
            Assert.NotEmpty(viewModel.RunIdentityText);
        }

        Assert.Equal(1, fixture.InputSnapshots.CaptureCount);
        Assert.Equal(recheckCount, fixture.InputSnapshots.RecheckCount);
        if (inputUnchanged)
        {
            Assert.DoesNotContain("入力変更を検出", viewModel.InputStateText, StringComparison.Ordinal);
        }

        if (scenario is "partial" or "checkpoint-failed")
        {
            Assert.Contains("run開始時の入力snapshotは取得済み", viewModel.InputStateText, StringComparison.Ordinal);
            Assert.DoesNotContain("一致しています", viewModel.InputStateText, StringComparison.Ordinal);
            Assert.Equal(0, fixture.FinalizerCalls);
        }
        else if (scenario is "output-failed" or "final-cancelled")
        {
            Assert.Contains("run終了時のexact hash / size / mtimeは一致", viewModel.InputStateText, StringComparison.Ordinal);
            Assert.Contains("commit直前の再確認・保存成功は未確認", viewModel.InputStateText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Draft_edits_do_not_change_loaded_run_identity_scores_or_override_range()
    {
        ResultsDurableFixture fixture = new();
        ExecutionRunContext context = await fixture.RunAsync();
        RecordingOutputBoundary output = new();
        using ResultsOutputViewModel viewModel = new(output, context);
        QuantificationDesignViewModel draft = new(context.Summary.Snapshot.Definition);
        ResultsRowScoreViewModel originalRow = Assert.Single(viewModel.RowScores);
        ResultsCriterionViewModel criterion = Assert.Single(viewModel.Results);
        string identity = viewModel.RunIdentityText;
        string summary = viewModel.RunSummaryText;
        string canonicalDefinition = context.Summary.Snapshot.CanonicalJson;

        draft.BasePoints = 50m;
        draft.Questions[0].Points = 50m;
        draft.Questions[0].DisplayName = "Next draft question";
        draft.Questions[0].Evaluators[0].Maximum = 20m;

        Assert.NotEqual(context.Summary.DefinitionSha256, draft.BuildSnapshot().Sha256);
        Assert.Equal(identity, viewModel.RunIdentityText);
        Assert.Equal(summary, viewModel.RunSummaryText);
        Assert.Same(originalRow, Assert.Single(viewModel.RowScores));
        Assert.Same(criterion, Assert.Single(viewModel.Results));
        Assert.Equal(60m, originalRow.FinalRaw);
        Assert.Equal(60m, originalRow.FinalScore);
        Assert.Equal(new ScoreRange(0m, 10m), criterion.Range);
        Assert.Equal(50m, criterion.NormalizedScore);
        Assert.Equal("Question Q1", criterion.QuestionName);
        Assert.False(viewModel.HasUnsavedOverrides);

        criterion.OverrideText = "8";

        Assert.False(viewModel.HasOverrideErrors);
        Assert.Equal(80m, criterion.NormalizedScore);
        Assert.Equal(84m, Assert.Single(viewModel.RowScores).FinalRaw);
        Assert.Equal(84m, Assert.Single(viewModel.RowScores).FinalScore);
        Assert.Equal(identity, viewModel.RunIdentityText);
        Assert.Equal(canonicalDefinition, context.Summary.Snapshot.CanonicalJson);
        Assert.Equal([10, 10], fixture.ReadRows);
        Assert.Equal(1, fixture.FinalizerCalls);
        Assert.Equal(0, output.ExportCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("synthetic-T21-returned.xlsx")]
    public async Task Successful_receipt_uses_returned_or_captured_path_and_load_clears_it(string? returnedName)
    {
        string requestedPath = Path.Combine(Path.GetTempPath(), "synthetic-T21-requested.xlsx");
        string nextCandidate = Path.Combine(Path.GetTempPath(), "synthetic-T21-next-candidate.xlsx");
        string? returnedPath = string.IsNullOrWhiteSpace(returnedName) ? returnedName : Path.Combine(Path.GetTempPath(), returnedName);
        TaskCompletionSource<ResultsOutputResult> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingOutputBoundary output = new() { Export = (_, _) => release.Task };
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync(rowCount: 1, output: output);
        List<string?> notifications = [];
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.OutputPath = requestedPath;
        viewModel.Results[0].OverrideText = "8";
        Task exporting = viewModel.ExportAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(requestedPath, Assert.IsType<ResultsOutputRequest>(output.LastRequest).OutputPath);
            viewModel.OutputPath = nextCandidate;
            viewModel.Results[0].OverrideText = "7";
        }
        finally
        {
            release.TrySetResult(new ResultsOutputResult(ResultsOutputStatusCodes.Success, returnedPath));
        }

        await exporting;
        string saved = string.IsNullOrWhiteSpace(returnedPath) ? requestedPath : returnedPath;
        Assert.Equal(saved, viewModel.LastSuccessfulExportPath);
        Assert.Equal(nextCandidate, viewModel.OutputPath);
        Assert.True(viewModel.HasSuccessfulExport);
        Assert.Equal("保存済み修正版: " + saved, viewModel.LastSuccessfulExportText);
        Assert.True(viewModel.HasUnsavedOverrides);
        Assert.Equal(string.Empty, viewModel.FinalPath);
        Assert.Null(typeof(ResultsOutputViewModel).GetProperty(nameof(ResultsOutputViewModel.LastSuccessfulExportPath))!.SetMethod);
        Assert.Contains(nameof(ResultsOutputViewModel.LastSuccessfulExportPath), notifications);
        Assert.Contains(nameof(ResultsOutputViewModel.HasSuccessfulExport), notifications);
        Assert.Contains(nameof(ResultsOutputViewModel.LastSuccessfulExportText), notifications);

        viewModel.Load(await CreateContextAsync(firstDataRow: 30, rowCount: 1));

        Assert.Equal(string.Empty, viewModel.LastSuccessfulExportPath);
        Assert.Equal(string.Empty, viewModel.LastSuccessfulExportText);
        Assert.False(viewModel.HasSuccessfulExport);
        // The boundary supplied a synthetic receipt, not a real file-existence assertion.
    }

    [Theory]
    [InlineData(ResultsOutputStatusCodes.ExportFailed)]
    [InlineData(ResultsOutputStatusCodes.OutputInvalid)]
    [InlineData(ResultsOutputStatusCodes.Cancelled)]
    [InlineData(ResultsOutputStatusCodes.TargetExists)]
    public async Task Failed_output_cannot_replace_a_successful_receipt_even_when_it_supplies_a_path(string failureCode)
    {
        string saved = Path.Combine(Path.GetTempPath(), "synthetic-T21-saved.xlsx");
        string notSaved = Path.Combine(Path.GetTempPath(), "synthetic-T21-not-saved.xlsx");
        int calls = 0;
        RecordingOutputBoundary output = new()
        {
            Export = (_, _) => Task.FromResult(++calls == 1
                ? new ResultsOutputResult(ResultsOutputStatusCodes.Success, saved)
                : new ResultsOutputResult(failureCode, notSaved)),
        };
        using ResultsOutputViewModel viewModel = await CreateViewModelAsync(rowCount: 1, output: output);
        await viewModel.ExportAsync(TestContext.Current.CancellationToken);
        viewModel.OutputPath = notSaved;
        viewModel.Results[0].OverrideText = "8";
        Assert.Equal(saved, viewModel.LastSuccessfulExportPath);

        await viewModel.ExportAsync(TestContext.Current.CancellationToken);

        Assert.Equal(failureCode, viewModel.LastExportCode);
        Assert.Equal(saved, viewModel.LastSuccessfulExportPath);
        Assert.Equal(notSaved, viewModel.OutputPath);
        Assert.True(viewModel.HasSuccessfulExport);
        Assert.True(viewModel.HasUnsavedOverrides);
    }

    private static async Task<ExecutionRunContext> CreateLegacyZeroPointContextAsync(
        bool mixedPoints,
        string statusCode = ResultsStatusCodes.Success)
    {
        QuestionDefinition zero = U01TestSupport.Question(
            "Q0", "A", ["B"], true, U01TestSupport.Evaluator("E0", "C0")) with { Points = 0m };
        QuestionDefinition positive = U01TestSupport.Question(
            "Q1", "A", ["B"], true, U01TestSupport.Evaluator("E1", "C1")) with { Points = 80m };
        QuestionDefinition[] questions = mixedPoints ? [zero, positive] : [zero];
        QuantificationDefinition definition = U01TestSupport.Definition(10, 10, questions);
        RunSummary summary = await new QuantificationOrchestrator(
            new ScriptedRowSource((request, _) => Task.FromResult(new EvaluationRowData(
                request.SourceRowNumber, new Dictionary<string, string?>
                {
                    ["A"] = statusCode == ResultsStatusCodes.Empty ? "   " : "synthetic answer",
                    ["B"] = "synthetic support",
                }))),
            new ScriptedRunner((payload, _, _) => Task.FromResult(
                statusCode is ResultsStatusCodes.Success or ResultsStatusCodes.Empty
                    ? EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload, _ => 5m))
                    : EvaluationRunnerResult.Failed(statusCode))),
            new ScriptedInputSnapshots(U01TestSupport.InputSnapshot())).RunAsync(
                new QuantificationRunRequest
                {
                    DraftDefinition = definition,
                    WorkbookMetadata = U01TestSupport.ValidateMapping(definition).Metadata,
                    InputPath = Path.Combine(Path.GetTempPath(), "synthetic-T21-legacy-zero-input.xlsx"),
                    ModelId = "model-test",
                    MaximumPromptTokens = 64_000,
                    MaximumContextWindowTokens = 128_000,
                    MaxConcurrency = 1,
                }, cancellationToken: TestContext.Current.CancellationToken);
        return U04TestSupport.Context(summary);
    }

    private static async Task<ResultsOutputViewModel> CreateViewModelAsync(
        int rowCount = 9,
        RecordingOutputBoundary? output = null,
        string primary = "synthetic answer",
        bool cancelAfterFirst = false) =>
        new(output ?? new RecordingOutputBoundary(), await CreateContextAsync(
            rowCount: rowCount,
            primary: primary,
            cancelAfterFirst: cancelAfterFirst));

    private static async Task<ExecutionRunContext> CreateContextAsync(
        int firstDataRow = 10,
        int rowCount = 9,
        string primary = "synthetic answer",
        bool cancelAfterFirst = false)
    {
        QuantificationDefinition definition = U04TestSupport.Definition(firstDataRow, firstDataRow + rowCount - 1);
        QuestionDefinition question = definition.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        evaluator = evaluator with
        {
            Criteria = evaluator.Criteria.Add(evaluator.Criteria[0] with
            {
                Id = "C2",
                DisplayName = "Criterion C2",
            }),
        };
        definition = definition with { Questions = [question with { Evaluators = [evaluator] }] };
        WorkbookMetadata metadata = U01TestSupport.ValidateMapping(definition).Metadata;
        RunSummary summary = await U04TestSupport.CreateSummaryAsync(
            definition,
            metadata,
            primary,
            cancelAfterFirst);
        return U04TestSupport.Context(summary);
    }

    private static ICommand[] PresentationCommands(ResultsOutputViewModel viewModel) =>
    [
        viewModel.PreviousPageCommand,
        viewModel.NextPageCommand,
        viewModel.GoToRowCommand,
        viewModel.NextOverrideErrorCommand,
        viewModel.ShowDetailCommand,
        viewModel.ShowListCommand,
    ];
}

// Real orchestrator/scheduler and summary projection; AI, checkpoint, and final output
// boundaries return synthetic receipts. Only the existing metadata helper creates a temp workbook.
internal sealed class ResultsDurableFixture : IEvaluationRowSource, IEvaluationRunner,
    IReferenceAnswerOperationRunner, ISpecialEvaluationOperationRunner,
    ICheckpointStore, IOutputPathPlanner, IDurableRunFinalizer, IPartialCheckpointCleaner
{
    private string primary = string.Empty;
    private int? cancelAfterRows;
    private CancellationTokenSource? cancellation;
    private CheckpointEnvelope? checkpoint;

    internal string InputPath { get; init; } = Path.Combine(Path.GetTempPath(), "synthetic-T21-durable-input.xlsx");

    internal ScriptedInputSnapshots InputSnapshots { get; } = new(U01TestSupport.InputSnapshot());

    internal string NormalStatusCode { get; init; } = ResultsStatusCodes.Success;

    internal string SpecialStatusCode { get; init; } = ResultsStatusCodes.Success;

    internal string FinalizationCode { get; init; } = ResultsOutputStatusCodes.Success;

    internal string? CheckpointFailureCode { get; init; }

    internal int? CheckpointFailureAtRowCount { get; init; }

    internal bool FailRowRead { get; init; }

    internal bool CleanupSucceeds { get; init; } = true;

    internal List<int> ReadRows { get; } = [];

    internal int FinalizerCalls { get; private set; }

    internal async Task<ExecutionRunContext> RunAsync(
        int rowCount = 1,
        string primary = "synthetic answer",
        decimal basePoints = 20m,
        decimal specialPoints = 0m,
        int? cancelAfterRows = null,
        bool includeZeroPointQuestion = false)
    {
        this.primary = primary;
        this.cancelAfterRows = cancelAfterRows;
        QuantificationDefinition source = U04TestSupport.Definition(10, 10 + rowCount - 1);
        QuantificationDefinition definition = source with
        {
            BasePoints = basePoints,
            SpecialPoints = specialPoints,
            Questions =
            [
                source.Questions[0] with
                {
                    Points = 100m - basePoints - specialPoints,
                    SpecialEvaluations =
                    [
                        new()
                        {
                            Id = "S1",
                            DisplayName = "Synthetic special",
                            PrimarySourceColumn = "B",
                            SupportingSourceColumns = ["A"],
                            PromptTemplate = "Special {回答} {補助情報}",
                        },
                    ],
                },
            ],
        };
        if (includeZeroPointQuestion)
        {
            QuestionDefinition zero = U01TestSupport.Question(
                "Q0", "A", ["B"], true, U01TestSupport.Evaluator("E0", "C0")) with { Points = 0m };
            definition = definition with { Questions = [zero, .. definition.Questions] };
        }

        using CancellationTokenSource currentCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation = currentCancellation;
        try
        {
            RunSummary summary = await new DurableQuantificationOrchestrator(
                this, this, this, this, InputSnapshots, this, this, this, this, new FixtureClock()).RunAsync(
                    new DurableQuantificationRunRequest
                    {
                        Run = new QuantificationRunRequest
                        {
                            DraftDefinition = definition,
                            WorkbookMetadata = U01TestSupport.ValidateMapping(definition).Metadata,
                            InputPath = InputPath,
                            ModelId = "model-test",
                            MaximumPromptTokens = 64_000,
                            MaximumContextWindowTokens = 128_000,
                            MaxConcurrency = 1,
                        },
                        Runtime = new CheckpointRuntimeIdentity
                        {
                            ApplicationIdentity = "StudyReportEvaluator.App/4.0.0",
                            CliVersion = "1.0.82",
                            CliSha256 = new string('B', 64),
                            SdkInformationalVersion = "1.0.11",
                        },
                    }, cancellationToken: currentCancellation.Token);
            return U04TestSupport.Context(summary, InputPath);
        }
        finally
        {
            cancellation = null;
        }
    }

    public Task<EvaluationRowData> ReadAsync(EvaluationRowRequest request, CancellationToken cancellationToken)
    {
        ReadRows.Add(request.SourceRowNumber);
        if (FailRowRead)
        {
            throw new IOException("Synthetic row read failure.");
        }

        return Task.FromResult(new EvaluationRowData(request.SourceRowNumber,
            new Dictionary<string, string?> { ["A"] = primary, ["B"] = primary }));
    }

    public Task<EvaluationRunnerResult> EvaluateAsync(SafeEvaluationPayload payload, string modelId, CancellationToken cancellationToken) =>
        Task.FromResult(NormalStatusCode == ResultsStatusCodes.Success
            ? EvaluationRunnerResult.Succeeded(U01TestSupport.ValidResult(payload, _ => 5m))
            : EvaluationRunnerResult.Failed(NormalStatusCode));

    public Task<AuxiliaryOperationResult<ReferenceAnswerResult>> EvaluateAsync(SafeReferenceAnswerPayload payload, CancellationToken cancellationToken) =>
        Task.FromResult(AuxiliaryOperationResult<ReferenceAnswerResult>.Succeeded(
            new ReferenceAnswerResult { QuestionId = payload.QuestionId, Answer = "参照" }));

    public Task<AuxiliaryOperationResult<SpecialQuantificationResult>> EvaluateAsync(SafeSpecialEvaluationPayload payload, string modelId, CancellationToken cancellationToken) =>
        Task.FromResult(SpecialStatusCode == ResultsStatusCodes.Success
            ? AuxiliaryOperationResult<SpecialQuantificationResult>.Succeeded(new SpecialQuantificationResult
            {
                SpecialEvaluationId = payload.SpecialEvaluationId,
                Score = 0.8m,
                Reason = "synthetic reason",
                Evidence = string.Empty,
                EvidenceSource = EvidenceSourceKind.None,
                EvidenceSourceColumnId = string.Empty,
            })
            : AuxiliaryOperationResult<SpecialQuantificationResult>.Failed(SpecialStatusCode));


    public CheckpointSaveResult Create(CheckpointEnvelope envelope, CancellationToken cancellationToken = default)
    {
        checkpoint = envelope;
        return CheckpointSaveResult.Succeeded(createdNew: true);
    }

    public CheckpointSaveResult Update(CheckpointEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (CheckpointFailureCode is { } failure
            && (CheckpointFailureAtRowCount is null || envelope.CompletedRows.Length == CheckpointFailureAtRowCount))
        {
            return CheckpointSaveResult.Failed(failure);
        }

        checkpoint = envelope;
        if (cancelAfterRows is int count && envelope.CompletedRows.Length == count)
        {
            cancellation?.Cancel();
        }

        return CheckpointSaveResult.Succeeded(createdNew: false);
    }

    public CheckpointLoadResult Load(string partialPath, CancellationToken cancellationToken = default) =>
        checkpoint is null ? CheckpointLoadResult.Failed(CheckpointStatusCodes.Invalid) : CheckpointLoadResult.Succeeded(checkpoint);

    public OutputPathReservation Reserve(string inputPath, DateTimeOffset localTime, string? outputDirectory = null)
    {
        string directory = outputDirectory ?? Path.GetTempPath();
        return OutputPathReservation.Create(directory,
            Path.Combine(directory, "synthetic-T21-durable-final.xlsx"),
            Path.Combine(directory, "synthetic-T21-durable-final.partial.xlsx"));
    }

    public DurableFinalizationResult Finalize(RunSummary summary, CheckpointEnvelope envelope, CancellationToken cancellationToken)
    {
        FinalizerCalls++;
        return cancellationToken.IsCancellationRequested
            ? DurableFinalizationResult.Failed(ResultsOutputStatusCodes.Cancelled)
            : FinalizationCode == ResultsOutputStatusCodes.Success
                ? DurableFinalizationResult.Succeeded(envelope.FinalPath)
                : DurableFinalizationResult.Failed(FinalizationCode);
    }

    public bool TryDelete(string partialPath) => CleanupSucceeds;

    private sealed class FixtureClock : TimeProvider
    {
        private long ticks = new DateTimeOffset(2026, 9, 7, 1, 2, 3, TimeSpan.Zero).UtcTicks;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Add(ref ticks, TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
