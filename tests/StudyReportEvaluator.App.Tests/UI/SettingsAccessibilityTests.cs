using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: NFR-A11Y-001 (AC-078)
public sealed class SettingsAccessibilityTests
{
    [AvaloniaFact]
    public async Task Common_editors_keep_their_ids_keyboard_order_and_bind_the_same_execution_owner()
    {
        QuantificationDefinition definition = U04TestSupport.Definition(2, 2);
        RecordingAuthenticationBoundary authentication = new(new ExecutionAuthenticationSnapshot(
            ExecutionAuthenticationState.Available,
            [U04TestSupport.Model("model-test"), U04TestSupport.Model("model-other"), U04TestSupport.Model("auto")],
            U04TestSupport.RuntimeIdentity()));
        RecordingRunBoundary runner = new((_, _, _) => throw new InvalidOperationException("No run was requested."));
        using ExecutionViewModel execution = new(authentication, runner);
        execution.Configure(definition, U01TestSupport.ValidateMapping(definition).Metadata,
            Path.Combine(Path.GetTempPath(), "synthetic-T25-settings.xlsx"));
        await execution.CheckAuthenticationAsync(TestContext.Current.CancellationToken);
        using SettingsViewModel settings = new(new InputViewModel(), new QuantificationDesignViewModel(definition), execution);
        SettingsView view = new(settings);
        Window window = new() { Width = 950, Height = 450, Content = view };
        try
        {
            window.Show();
            window.SetRenderScaling(2d);
            Render();
            Assert.Equal(2d, window.RenderScaling);
            Assert.Same(settings, view.ViewModel);
            Assert.Same(execution, settings.Execution);
            ComboBox model = ById<ComboBox>(view, "ExecutionModel");
            ComboBox concurrency = ById<ComboBox>(view, "ExecutionConcurrency");
            TextBox output = ById<TextBox>(view, "ExecutionOutputDirectory");
            TextBox effective = ById<TextBox>(view, "SettingsEffectiveOutputDirectory");
            Assert.Same(execution.AvailableModelIds, model.ItemsSource);
            Assert.Same(execution.ConcurrencyOptions, concurrency.ItemsSource);
            Assert.Equal(Enumerable.Range(1, 16).ToArray(), concurrency.Items.Cast<int>());
            Assert.Equal(execution.SelectedModelId, model.SelectedItem);
            Assert.Equal(execution.MaxConcurrency, concurrency.SelectedItem);
            Assert.False(output.IsReadOnly);
            Assert.True(effective.IsReadOnly);
            string defaultOutput = execution.OutputDirectory;
            Assert.Equal(defaultOutput, effective.Text);
            Assert.True(string.IsNullOrEmpty(output.Text));
            Assert.All(new Control[] { model, concurrency, output, effective }, control =>
            {
                Assert.True(control.Focusable);
                Assert.True(control.IsTabStop);
                Assert.True(control.MinHeight >= 44d);
                Assert.True(control.Bounds.Height >= 44d);
                Assert.True(control.Bounds.Width >= 44d);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)));
            });
            string[] originalIds = UniqueIds(view);
            Assert.Same(model, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab);
            Assert.Same(concurrency, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(model, window.FocusManager?.GetFocusedElement());
            Assert.Equal(new Thickness(3d), model.BorderThickness);
            Press(window, Key.Tab);
            Press(window, Key.Tab);
            Assert.Same(output, window.FocusManager?.GetFocusedElement());

            model.SetCurrentValue(ComboBox.SelectedItemProperty, "model-other");
            concurrency.SetCurrentValue(ComboBox.SelectedItemProperty, 3);
            string explicitOutput = Path.Combine(Path.GetTempPath(), "synthetic-T25-explicit-output");
            output.SetCurrentValue(TextBox.TextProperty, explicitOutput);
            Press(window, Key.Tab);
            Assert.Same(effective, window.FocusManager?.GetFocusedElement());
            Assert.Equal("model-other", execution.SelectedModelId);
            Assert.Equal("model-other", execution.PreferredModelId);
            Assert.Equal(3, execution.MaxConcurrency);
            Assert.Equal(explicitOutput, execution.OutputDirectoryOverride);
            Assert.Equal(explicitOutput, effective.Text);
            effective.SelectAll();
            window.KeyTextInput("読取専用の表示は編集しない");
            Render();
            Assert.Equal(explicitOutput, effective.Text);
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(output, window.FocusManager?.GetFocusedElement());

            // Attach one real view at a time: moved IDs cannot be hidden duplicate editors.
            window.Content = null;
            ExecutionView executionView = new(execution);
            window.Content = executionView;
            Render();
            Assert.Same(execution, executionView.ViewModel);
            Assert.DoesNotContain(UniqueIds(executionView), id =>
                id is "ExecutionModel" or "ExecutionConcurrency" or "ExecutionOutputDirectory");
            TextBox actualModel = ById<TextBox>(executionView, "ExecutionEffectiveModel");
            Assert.True(actualModel.IsReadOnly);
            Assert.Equal("次回 model-other 並列3 · effort: 未指定（model非対応またはauto） · Context Size: 64K (Default) · 希望: model-other · 上限: 64,000 tokens", actualModel.Text);
            TextBox actualOutput = ById<TextBox>(executionView, "ExecutionEffectiveOutputDirectory");
            Assert.True(actualOutput.IsReadOnly);
            Assert.Equal(explicitOutput, actualOutput.Text);
            Assert.True(execution.CanStart);

            window.Content = view;
            Render();
            Assert.Same(model, ById<ComboBox>(view, "ExecutionModel"));
            Assert.Same(concurrency, ById<ComboBox>(view, "ExecutionConcurrency"));
            Assert.Same(output, ById<TextBox>(view, "ExecutionOutputDirectory"));
            Assert.Equal(originalIds, UniqueIds(view));
            Assert.Equal("model-other", model.SelectedItem);
            Assert.Equal(3, concurrency.SelectedItem);
            Assert.True(output.Focus(NavigationMethod.Tab));
            output.SetCurrentValue(TextBox.TextProperty, string.Empty);
            Press(window, Key.Tab);
            Assert.Same(effective, window.FocusManager?.GetFocusedElement());
            Assert.Null(execution.OutputDirectoryOverride);
            Assert.Equal(defaultOutput, effective.Text);
            Assert.Equal(1, authentication.CallCount);
            Assert.Equal(0, runner.CallCount);
            Assert.Null(execution.LastLoginTask);
            Assert.Null(settings.LastLoadTask);
            Assert.Null(settings.LastSaveTask);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Imported_prompt_keyboard_navigation_enters_the_selected_item_not_the_nonfocusable_list()
    {
        QuantificationDesignViewModel design = new(U04TestSupport.Definition(2, 2), availableColumnNames: null, importedPrompts:
        [
            new ImportedPrompt { Path = "first.txt", DisplayName = "first.txt", Content = "最初の原文 {回答}" },
            new ImportedPrompt { Path = "second.txt", DisplayName = "second.txt", Content = "次の原文 {回答}" },
        ]);
        RecordingAuthenticationBoundary authentication = new(new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired));
        RecordingRunBoundary runner = new((_, _, _) => throw new InvalidOperationException("No run was requested."));
        using ExecutionViewModel execution = new(authentication, runner);
        using SettingsViewModel settings = new(new InputViewModel(), design, execution)
        {
            SelectedCategory = SettingsCategory.ImportedPrompts,
        };
        SettingsView view = new(settings);
        Window window = new() { Width = 950, Height = 450, Content = view };
        try
        {
            window.Show();
            Render();
            Assert.Same(design, ById<ImportedPromptSettingsView>(view, "ImportedPromptSettingsView").ViewModel);
            QuantificationDefinition before = design.Draft;
            ListBox list = ById<ListBox>(view, "ImportedPrompts");
            TextBox preview = ById<TextBox>(view, "ImportedPromptPreview");
            Assert.Same(design.ImportedPrompts, list.ItemsSource);
            Assert.True(preview.IsReadOnly);
            Assert.Same(preview, window.FocusManager?.GetFocusedElement());
            Assert.False(list.Focusable);
            Assert.False(list.Focus(NavigationMethod.Tab));
            Assert.Same(preview, window.FocusManager?.GetFocusedElement());
            Assert.Same(design.ImportedPrompts[0], list.SelectedItem);
            Press(window, Key.Tab, RawInputModifiers.Shift);
            ListBoxItem first = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            Assert.Same(design.ImportedPrompts[0], first.DataContext);
            Assert.Same(first, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Down);
            ListBoxItem selected = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1));
            Assert.True(selected.IsSelected);
            Assert.Same(design.ImportedPrompts[1], selected.DataContext);
            Assert.Same(design.ImportedPrompts[1], design.SelectedImportedPrompt);
            Assert.Equal(design.ImportedPrompts[1].Content, preview.Text);
            Assert.Same(selected, window.FocusManager?.GetFocusedElement());
            Assert.True(selected.MinHeight >= 44d);
            Assert.True(selected.Bounds.Height >= 44d);
            Assert.True(selected.Bounds.Width >= 44d);
            Press(window, Key.Tab);
            Assert.Same(preview, window.FocusManager?.GetFocusedElement());
            Press(window, Key.Tab, RawInputModifiers.Shift);
            Assert.Same(selected, window.FocusManager?.GetFocusedElement());
            UniqueIds(view);
            Assert.Same(before, design.Draft);
            Assert.False(ById<Button>(view, "ApplyImportedPrompt").IsEffectivelyEnabled);
            Assert.Equal(0, authentication.CallCount);
            Assert.Equal(0, runner.CallCount);
            Assert.Null(execution.LastLoginTask);
            Assert.Null(settings.LastLoadTask);
            Assert.Null(settings.LastSaveTask);
        }
        finally
        {
            window.Close();
        }
    }

    private static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static string[] UniqueIds(Control root)
    {
        string[] ids = root.GetVisualDescendants().OfType<Control>().Concat(root.GetLogicalDescendants().OfType<Control>())
            .Prepend(root).Distinct().Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        return ids;
    }

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physical = key switch
        {
            Key.Tab => PhysicalKey.Tab,
            Key.Down => PhysicalKey.ArrowDown,
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
}