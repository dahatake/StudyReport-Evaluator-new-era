using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Prompting;
using StudyReportEvaluator.Core.Serialization;
using Xunit;

namespace StudyReportEvaluator.App.Tests.UI;

public sealed class SpecialEvaluationSettingsViewTests
{
    [AvaloniaFact]
    public void Parameterless_view_inherits_the_existing_design_without_creating_or_mutating_a_draft()
    {
        SpecialEvaluationSettingsView unattached = new();
        Assert.Null(unattached.DataContext);
        Assert.Throws<InvalidOperationException>(() => unattached.ViewModel);
        QuantificationDesignViewModel design = CreateDesign();
        QuantificationDefinition original = design.Draft;
        using ViewHarness harness = new(design, inheritDataContext: true);

        Assert.Same(design, harness.View.DataContext);
        Assert.Same(design, harness.View.ViewModel);
        Assert.Same(original, design.Draft);
        Assert.Same(design.Questions, harness.Questions.ItemsSource);
        Assert.Same(design.SelectedQuestion, harness.Questions.SelectedItem);
        Assert.Same(design.SelectedQuestion!.SpecialEvaluations, harness.Items.ItemsSource);
        Assert.Same(design.SelectedQuestion.SelectedSpecialEvaluation, harness.Items.SelectedItem);
        Assert.Same(harness.Questions, harness.Window.FocusManager?.GetFocusedElement());
        Assert.Same(design.ValidateCommand, ById<Button>(harness.View, "SpecialSettingsValidate").Command);
    }

    [AvaloniaFact]
    public void Selected_question_and_item_controls_perform_all_crud_without_editing_other_targets()
    {
        QuantificationDesignViewModel design = CreateDesign();
        QuantificationSnapshot original = design.BuildSnapshot();
        QuestionDesignItemViewModel otherQuestion = design.Questions[0];
        QuestionDesignItemViewModel question = design.Questions[1];
        SpecialEvaluationDesignItemViewModel otherItem = question.SpecialEvaluations[0];
        SpecialEvaluationDesignItemViewModel selected = question.SpecialEvaluations[1];
        using ViewHarness harness = new(design);

        Select(harness.Questions, question);
        Select(harness.Items, selected);
        SetText(ById<TextBox>(harness.View, selected.CardAutomationId + "-Name"), "編集した固有項目");
        Assert.Equal("編集した固有項目", SelectedDefinition(design).DisplayName);
        Assert.Same(question, design.SelectedQuestion);
        Assert.Same(selected, question.SelectedSpecialEvaluation);
        Assert.Equal("同じ固有項目名", otherItem.DisplayName);

        Click(harness, ById<Button>(harness.View, "SpecialSettingsAdd"));
        SpecialEvaluationDesignItemViewModel added = question.SpecialEvaluations[^1];
        Assert.Equal(3, question.SpecialEvaluations.Count);
        Assert.Same(selected, question.SelectedSpecialEvaluation);
        Select(harness.Items, added);
        SetText(ById<TextBox>(harness.View, added.CardAutomationId + "-Name"), "追加項目");
        ShowPrompt(harness);
        SetText(ById<TextBox>(harness.View, added.PromptAutomationId), "追加した指示\n{回答} {補助情報}");
        Assert.Equal("追加した指示\n{回答} {補助情報}", SelectedDefinition(design).PromptTemplate);
        ShowItemSettings(harness);

        Click(harness, ById<Button>(harness.View, added.CardAutomationId + "-MoveUp"));
        Assert.Same(added, question.SpecialEvaluations[1]);
        Assert.Same(added, harness.Items.SelectedItem);
        Click(harness, ById<Button>(harness.View, added.CardAutomationId + "-MoveDown"));
        Assert.Same(added, question.SpecialEvaluations[2]);
        Assert.False(ById<Button>(harness.View, added.CardAutomationId + "-MoveDown").IsEffectivelyEnabled);

        Click(harness, ById<Button>(harness.View, added.CardAutomationId + "-Duplicate"));
        SpecialEvaluationDesignItemViewModel copy = question.SpecialEvaluations[3];
        Assert.NotEqual(added.Id, copy.Id);
        Assert.Equal(added.DisplayName, copy.DisplayName);
        Assert.Equal(added.PromptTemplate, copy.PromptTemplate);
        Assert.Same(added, question.SelectedSpecialEvaluation);
        Select(harness.Items, copy);
        Click(harness, ById<CheckBox>(harness.View, copy.CardAutomationId + "-Enabled"));
        Assert.False(copy.Enabled);
        Assert.False(SelectedDefinition(design).Enabled);
        Assert.True(added.Enabled);

        Click(harness, ById<Button>(harness.View, copy.CardAutomationId + "-Delete"));
        Assert.DoesNotContain(question.SpecialEvaluations, item => item.Id == copy.Id);
        Assert.Same(added, question.SelectedSpecialEvaluation);
        Assert.Same(added, harness.Items.SelectedItem);
        Click(harness, ById<Button>(harness.View, added.CardAutomationId + "-Delete"));
        Assert.Equal(2, question.SpecialEvaluations.Count);
        Assert.Same(selected, question.SelectedSpecialEvaluation);
        Assert.Equal("編集した固有項目", ById<TextBox>(harness.View, selected.CardAutomationId + "-Name").Text);

        CanonicalDefinitionSerializer serializer = new();
        Assert.Equal(
            serializer.Serialize(original.Definition with { Questions = [original.Definition.Questions[0]] }),
            serializer.Serialize(design.Draft with { Questions = [design.Draft.Questions[0]] }));
        Assert.Equal(original.Definition.BasePoints, design.BasePoints);
        Assert.Equal(original.Definition.SpecialPoints, design.SpecialPoints);
        Assert.Equal(original.Definition.Questions[1].Points, question.Points);
        Assert.True(original.HasValidHash());
        Select(harness.Questions, otherQuestion);
        Select(harness.Questions, question);
        Assert.Same(selected, harness.Items.SelectedItem);
        AssertUniqueIds(harness.View);
    }

    [AvaloniaFact]
    public void Primary_and_support_controls_bind_exact_candidates_and_prevent_duplicates()
    {
        QuantificationDesignViewModel design = CreateDesign();
        QuestionDesignItemViewModel question = design.Questions[0];
        SpecialEvaluationDesignItemViewModel special = question.SpecialEvaluations[0];
        SpecialEvaluationDesignItemViewModel other = question.SpecialEvaluations[1];
        other.PrimarySourceColumn = "D";
        using ViewHarness harness = new(design);
        ComboBox primary = ById<ComboBox>(harness.View, special.CardAutomationId + "-Primary");
        ComboBox support = ById<ComboBox>(harness.View, special.CardAutomationId + "-SupportingColumns");
        CheckBox include = ById<CheckBox>(harness.View, special.CardAutomationId + "-SupportingSelected");

        Assert.Same(special.AvailableColumnNames, primary.ItemsSource);
        Assert.Same(special.SupportingColumns, support.ItemsSource);
        Assert.Equal(["A", "B", "C", "D"], primary.ItemsSource!.Cast<string>());
        Assert.Equal("B", primary.SelectedItem);
        Assert.False(include.IsEffectivelyEnabled);

        Select(support, special.SupportingColumns.Single(column => column.ColumnName == "A"));
        Click(harness, include);
        Assert.Equal(["C", "A"], SelectedDefinition(design).SupportingSourceColumns);
        Select(support, special.SupportingColumns.Single(column => column.ColumnName == "C"));
        Assert.True(include.IsChecked);
        Click(harness, include);
        Assert.Equal(["A"], SelectedDefinition(design).SupportingSourceColumns);
        Select(support, special.SupportingColumns.Single(column => column.ColumnName == "B"));
        Assert.False(include.IsEffectivelyEnabled);
        Assert.False(include.IsChecked);
        Assert.False(special.SupportingColumns.Single(column => column.ColumnName == "B").CanSelect);

        Select(primary, "A");
        Assert.Equal("A", special.PrimarySourceColumn);
        Assert.Empty(SelectedDefinition(design).SupportingSourceColumns);
        Assert.True(include.IsEffectivelyEnabled);
        Assert.True(include.Focus(NavigationMethod.Tab));
        Press(harness.Window, Key.Space);
        Assert.Equal(["B"], SelectedDefinition(design).SupportingSourceColumns);
        include.SetCurrentValue(CheckBox.IsCheckedProperty, true);
        Render();
        Assert.Equal(["B"], SelectedDefinition(design).SupportingSourceColumns);
        Select(primary, "B");
        Assert.Empty(SelectedDefinition(design).SupportingSourceColumns);
        Assert.False(include.IsEffectivelyEnabled);
        Assert.False(include.IsChecked);
        Assert.Equal("A", question.PrimarySourceColumn);
        Assert.Equal(["D"], design.Draft.Questions[0].SupportingSourceColumns);
        Assert.Equal(["C"], design.Draft.Questions[0].SpecialEvaluations[1].SupportingSourceColumns);
        Select(harness.Items, other);
        Assert.Equal("D", ById<ComboBox>(harness.View, other.CardAutomationId + "-Primary").SelectedItem);
        Assert.Equal("D", SelectedDefinition(design).PrimarySourceColumn);
        Assert.Equal("B", special.PrimarySourceColumn);
        Select(harness.Items, special);
        Assert.Equal("B", ById<ComboBox>(harness.View, special.CardAutomationId + "-Primary").SelectedItem);
        Assert.Equal("D", other.PrimarySourceColumn);
        Assert.True(design.IsValid);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Candidate_refresh_keeps_incoming_primary_support_hash_and_user_editability(bool retainOldPrimary)
    {
        QuantificationDesignViewModel design = CreateDesign();
        using ViewHarness harness = new(design);
        Select(harness.Questions, design.Questions[1]);
        QuestionDesignItemViewModel question = design.SelectedQuestion!;
        Select(harness.Items, question.SpecialEvaluations[1]);
        SpecialEvaluationDesignItemViewModel special = question.SelectedSpecialEvaluation!;
        ComboBox primary = ById<ComboBox>(harness.View, special.CardAutomationId + "-Primary");
        QuantificationDefinition incoming = design.Draft;
        QuestionDefinition nextQuestion = incoming.Questions[1];
        nextQuestion = nextQuestion with
        {
            SpecialEvaluations = nextQuestion.SpecialEvaluations.SetItem(1, nextQuestion.SpecialEvaluations[1] with
            {
                PrimarySourceColumn = "D",
                SupportingSourceColumns = ["A", "C"],
            }),
        };
        incoming = incoming with { Questions = incoming.Questions.SetItem(1, nextQuestion) };
        string[] candidates = retainOldPrimary ? ["D", "A", "C", "B"] : ["D", "A", "C"];
        CanonicalDefinitionSerializer serializer = new();
        string expected = serializer.ComputeSha256(incoming);

        design.SynchronizeFromInput(incoming, candidates);
        Render();

        Assert.Equal(expected, serializer.ComputeSha256(design.Draft));
        Assert.Same(question, harness.Questions.SelectedItem);
        Assert.Same(special, harness.Items.SelectedItem);
        Assert.Equal("D", special.PrimarySourceColumn);
        Assert.Equal("D", primary.SelectedItem);
        Assert.Equal(candidates, primary.ItemsSource!.Cast<string>());
        Assert.Equal(candidates, special.SupportingColumns.Select(column => column.ColumnName));

        // A missing candidate is a display null, not permission for the view to
        // clear a still-owned draft value or manufacture a fallback column.
        QuantificationDefinition beforeRemoval = design.Draft;
        design.SynchronizeFromInput(beforeRemoval, ["A", "C"]);
        Render();
        Assert.Same(beforeRemoval, design.Draft);
        Assert.Null(primary.SelectedItem);
        Assert.Equal("D", special.PrimarySourceColumn);
        Assert.Equal(["A", "C"], SelectedDefinition(design).SupportingSourceColumns);

        Select(primary, "C");
        Assert.Equal("C", SelectedDefinition(design).PrimarySourceColumn);
        Assert.Equal(["A"], SelectedDefinition(design).SupportingSourceColumns);
        Assert.Same(special, question.SelectedSpecialEvaluation);
        ComboBox support = ById<ComboBox>(harness.View, special.CardAutomationId + "-SupportingColumns");
        Select(support, special.SupportingColumns.Single(column => column.ColumnName == "A"));
        CheckBox include = ById<CheckBox>(harness.View, special.CardAutomationId + "-SupportingSelected");
        Assert.True(include.IsChecked);
        Click(harness, include);
        Assert.Empty(SelectedDefinition(design).SupportingSourceColumns);
    }

    [AvaloniaTheory]
    [InlineData("", "REQUIRED")]
    [InlineData("説明だけ", "ANSWER_PLACEHOLDER_REQUIRED")]
    [InlineData("{{回答}}", "ANSWER_PLACEHOLDER_REQUIRED")]
    [InlineData("{回答} {未知}", "UNKNOWN_PLACEHOLDER")]
    [InlineData("{回答} {", "UNCLOSED_PLACEHOLDER")]
    [InlineData("{回答} }", "UNMATCHED_CLOSING_BRACE")]
    public void Prompt_edits_use_existing_validation_and_special_zero_to_one_preview(string invalid, string code)
    {
        QuantificationDesignViewModel design = CreateDesign();
        SpecialEvaluationDesignItemViewModel special = design.SelectedQuestion!.SelectedSpecialEvaluation!;
        using ViewHarness harness = new(design);
        ShowPrompt(harness);
        TextBox prompt = ById<TextBox>(harness.View, special.PromptAutomationId);
        TextBox preview = ById<TextBox>(harness.View, special.CardAutomationId + "-PromptPreview");
        ComboBox errors = ById<ComboBox>(harness.View, "SpecialSettingsValidationErrors");
        Assert.StartsWith("{回答}", Assert.IsType<string>(ToolTip.GetTip(prompt)), StringComparison.Ordinal);
        SetText(prompt, invalid);

        Assert.Equal(invalid, SelectedDefinition(design).PromptTemplate);
        Assert.False(design.IsValid);
        Assert.True(special.HasErrors);
        Assert.Equal(special.ValidationText, AutomationProperties.GetHelpText(ById<Border>(harness.View, special.CardAutomationId)));
        DesignValidationError error = Assert.Single(design.ValidationErrors, item =>
            item.NodeId == special.Id && item.Field == "PromptTemplate" && item.Code == code);
        Assert.Same(design.ValidationErrors, errors.ItemsSource);
        Assert.True(errors.IsEffectivelyEnabled);
        Select(errors, error);
        Assert.Same(error, errors.SelectedItem);
        Assert.Contains(errors.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == error.AccessibleText);
        Assert.Equal(design.ValidationSummary, ById<TextBlock>(harness.View, "SpecialSettingsValidationSummary").Text);
        Assert.Contains("設定エラー", preview.Text ?? string.Empty, StringComparison.Ordinal);

        const string valid = "literal {{brace}}\n{回答} / {設問} / {補助情報}\n範囲 {最小点}..{最大点} / 観点[{評価項目}]";
        SetText(prompt, valid);

        Assert.True(design.IsValid);
        Assert.Equal(valid, special.PromptTemplate);
        Assert.Contains("literal {brace}", preview.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("【回答 preview】", preview.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("【設問 preview】", preview.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("【補助情報 preview】", preview.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("範囲 0..1 / 観点[]", preview.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains(BuiltInPromptTemplates.SpecialOutputInstruction, preview.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("0..10", preview.Text ?? string.Empty, StringComparison.Ordinal);
        Assert.True(preview.IsReadOnly);
        Assert.False(prompt.IsReadOnly);
        Assert.Empty(errors.Items);
        Assert.False(errors.IsEffectivelyEnabled);
        SetText(prompt, "{回答}");
        Assert.True(design.IsValid); // Special does not require {評価項目}.
        Assert.Equal("{回答}", design.BuildSnapshot().Definition.Questions[0].SpecialEvaluations[0].PromptTemplate);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(10)]
    public void Empty_items_and_enabled_state_preserve_special_points_conditions_without_duplicate_inputs(int points)
    {
        QuantificationDesignViewModel design = CreateDesign(questionCount: 1, specialCount: 0, specialPoints: points);
        using ViewHarness harness = new(design);
        Assert.True(ById<TextBlock>(harness.View, "SpecialSettingsNoItem").IsEffectivelyVisible);
        Assert.Empty(harness.Items.Items);
        Assert.Equal(points == 0, design.IsValid);
        Assert.Equal(points > 0, design.ValidationErrors.Any(error => error.Code == "SPECIAL_ITEMS_REQUIRED"));
        string displayed = ById<TextBlock>(harness.View, "SpecialSettingsPointsStatus").Text ?? string.Empty;
        Assert.Contains($"固有配点 {points} 点", displayed, StringComparison.Ordinal);
        decimal basePoints = design.BasePoints;
        decimal questionPoints = design.Questions[0].Points;

        Click(harness, ById<Button>(harness.View, "SpecialSettingsAdd"));
        SpecialEvaluationDesignItemViewModel special = Assert.Single(design.Questions[0].SpecialEvaluations);
        Assert.Same(special, harness.Items.SelectedItem);
        Assert.True(design.IsValid);
        Assert.Equal("評価範囲は 0～1 固定（変更不可）", ById<TextBlock>(harness.View, "SpecialSettingsFixedRange").Text);
        Click(harness, ById<CheckBox>(harness.View, special.CardAutomationId + "-Enabled"));
        Assert.Equal(points == 0, design.IsValid);
        Assert.Equal(points > 0, design.ValidationErrors.Any(error => error.Code == "SPECIAL_ITEMS_REQUIRED"));

        foreach (bool promptTab in new[] { false, true })
        {
            if (promptTab)
            {
                ShowPrompt(harness);
            }

            string[] allowedTextInputs =
            [special.CardAutomationId + "-Name", special.PromptAutomationId, special.CardAutomationId + "-PromptPreview"];
            Assert.All(harness.View.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsEffectivelyVisible),
                box => Assert.Contains(AutomationProperties.GetAutomationId(box), allowedTextInputs));
            Assert.Empty(harness.View.GetVisualDescendants().OfType<NumericUpDown>());
        }

        Assert.Equal((decimal)points, design.SpecialPoints);
        Assert.Equal(basePoints, design.BasePoints);
        Assert.Equal(questionPoints, design.Questions[0].Points);
        Assert.DoesNotContain(harness.View.GetVisualDescendants().OfType<Control>(),
            control => AutomationProperties.GetAutomationId(control) == "DesignSpecialPoints");
        Click(harness, ById<Button>(harness.View, special.CardAutomationId + "-Delete"));
        Assert.True(ById<TextBlock>(harness.View, "SpecialSettingsNoItem").IsEffectivelyVisible);
        AssertUniqueIds(harness.View);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void No_question_state_is_focusable_nonmutating_and_disables_item_actions(bool emptyQuestions)
    {
        QuantificationDefinition seed = CreateDesign().Draft;
        QuantificationDesignViewModel design = new(emptyQuestions ? seed with { Questions = [] } : seed, ["A", "B", "C", "D"]);
        design.SelectedQuestion = null;
        QuantificationDefinition original = design.Draft;
        using ViewHarness harness = new(design);

        Assert.True(ById<TextBlock>(harness.View, "SpecialSettingsNoQuestion").IsEffectivelyVisible);
        Assert.Same(harness.Questions, harness.Window.FocusManager?.GetFocusedElement());
        Assert.Equal(emptyQuestions ? 0 : 2, harness.Questions.Items.Count);
        Assert.False(harness.Items.IsEffectivelyEnabled);
        Assert.False(ById<Button>(harness.View, "SpecialSettingsAdd").IsEffectivelyEnabled);
        Button[] itemActions = harness.View.GetVisualDescendants().OfType<Button>().Where(button =>
            button.Content is string text && text is "複製" or "上へ" or "下へ" or "削除").ToArray();
        Assert.Equal(4, itemActions.Length);
        Assert.All(itemActions, button =>
        {
            Assert.Null(button.Command);
            Assert.False(button.IsEffectivelyEnabled);
        });
        Assert.DoesNotContain(harness.View.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible);
        Button validate = ById<Button>(harness.View, "SpecialSettingsValidate");
        Assert.True(validate.Focus(NavigationMethod.Tab));
        Press(harness.Window, Key.Enter);
        Assert.Same(original, design.Draft);
        Assert.Null(design.SelectedQuestion);
        AssertUniqueIds(harness.View);
    }

    [AvaloniaFact]
    public void Compact_content_has_unique_accessible_targets_and_only_prompt_local_overflow()
    {
        QuantificationDesignViewModel design = CreateDesign();
        SpecialEvaluationDesignItemViewModel special = design.SelectedQuestion!.SelectedSpecialEvaluation!;
        special.PromptTemplate = string.Join('\n', Enumerable.Range(1, 240).Select(index => $"長い指示の行 {index}：{{回答}}"));
        using ViewHarness harness = new(design);

        Assert.InRange(harness.View.Bounds.Width, 959d, 961d);
        Assert.InRange(harness.View.Bounds.Height, 439d, 441d);
        AssertUniqueIds(harness.View);
        AssertAccessibleTargetsFit(harness.View);
        Assert.True(harness.Questions.Focus(NavigationMethod.Tab));
        Press(harness.Window, Key.Tab);
        Assert.Same(harness.Items, harness.Window.FocusManager?.GetFocusedElement());
        Press(harness.Window, Key.Tab);
        Assert.Same(ById<Button>(harness.View, "SpecialSettingsAdd"), harness.Window.FocusManager?.GetFocusedElement());
        Press(harness.Window, Key.Tab, RawInputModifiers.Shift);
        Assert.Same(harness.Items, harness.Window.FocusManager?.GetFocusedElement());

        ShowPrompt(harness);
        AssertUniqueIds(harness.View);
        AssertAccessibleTargetsFit(harness.View);
        TextBox prompt = ById<TextBox>(harness.View, special.PromptAutomationId);
        TextBox preview = ById<TextBox>(harness.View, special.CardAutomationId + "-PromptPreview");
        ScrollViewer scroll = Assert.Single(prompt.GetVisualDescendants().OfType<ScrollViewer>());
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        Assert.True(scroll.Viewport.Height >= 44d);
        Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        Point before = prompt.TranslatePoint(default, harness.View)!.Value;
        Assert.True(prompt.Focus(NavigationMethod.Tab));
        Press(harness.Window, Key.End, RawInputModifiers.Control);
        Assert.Equal(special.PromptTemplate.Length, prompt.CaretIndex);
        Assert.True(scroll.Offset.Y > 0d);
        Assert.Equal(before, prompt.TranslatePoint(default, harness.View)!.Value);
        Press(harness.Window, Key.Tab);
        Assert.Same(preview, harness.Window.FocusManager?.GetFocusedElement());
        Press(harness.Window, Key.Tab, RawInputModifiers.Shift);
        Assert.Same(prompt, harness.Window.FocusManager?.GetFocusedElement());
        Assert.Equal(new Thickness(3d), prompt.BorderThickness);
        Assert.All(harness.View.GetVisualDescendants().OfType<ScrollViewer>().Where(viewer =>
                viewer.Extent.Height > viewer.Viewport.Height + 1d),
            viewer => Assert.Contains(viewer.GetVisualAncestors(), ancestor =>
                ReferenceEquals(ancestor, prompt) || ReferenceEquals(ancestor, preview)));
        Assert.Equal(special.PromptTemplate, prompt.Text);
    }

    [AvaloniaFact]
    public void Reordering_and_deletion_keep_the_same_question_and_special_selection_or_adjacent_fallback()
    {
        QuantificationDesignViewModel design = CreateDesign(questionCount: 3, specialCount: 3);
        using ViewHarness harness = new(design);
        QuestionDesignItemViewModel question = design.Questions[1];
        SpecialEvaluationDesignItemViewModel special = question.SpecialEvaluations[1];
        SpecialEvaluationDesignItemViewModel nextSpecial = question.SpecialEvaluations[2];
        QuestionDesignItemViewModel nextQuestion = design.Questions[2];
        Select(harness.Questions, question);
        Select(harness.Items, special);

        QuantificationDefinition incoming = design.Draft.MoveQuestion(1, 0);
        incoming = incoming with { Questions = incoming.Questions.SetItem(0, incoming.Questions[0].MoveSpecialEvaluation(1, 0)) };
        CanonicalDefinitionSerializer serializer = new();
        string expected = serializer.ComputeSha256(incoming);
        design.SynchronizeFromInput(incoming, design.AvailableColumnNames);
        Render();

        Assert.Equal(expected, serializer.ComputeSha256(design.Draft));
        Assert.Same(question, harness.Questions.SelectedItem);
        Assert.Same(special, harness.Items.SelectedItem);
        Assert.Same(question, design.SelectedQuestion);
        Assert.Same(special, question.SelectedSpecialEvaluation);
        Assert.Equal(special.CardAutomationId, AutomationProperties.GetAutomationId(ById<Border>(harness.View, special.CardAutomationId)));

        // Restore the original order, then delete the selected nodes through the
        // existing APIs to exercise T05's next/previous neighbor contract.
        design.MoveSpecialEvaluationDown(question.Id, special.Id);
        design.MoveQuestionDown(question.Id);
        Render();
        Click(harness, ById<Button>(harness.View, special.CardAutomationId + "-Delete"));
        Assert.Same(nextSpecial, harness.Items.SelectedItem);
        design.DeleteQuestion(question.Id);
        Render();
        Assert.Same(nextQuestion, harness.Questions.SelectedItem);
        Assert.Same(nextQuestion.SelectedSpecialEvaluation, harness.Items.SelectedItem);
        AssertUniqueIds(harness.View);
    }

    [AvaloniaFact]
    public void Selection_refresh_subscription_is_replaced_once_and_removed_on_detach()
    {
        QuantificationDesignViewModel first = CreateDesign();
        QuantificationDesignViewModel second = CreateDesign(prefix: "second");
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
        Assert.Equal(0, ViewSubscriptionCount(second, harness.View));
        second.SelectedQuestion = second.Questions[1];
        harness.Window.Content = harness.View;
        Render();
        Assert.Equal(1, ViewSubscriptionCount(second, harness.View));
        Assert.Same(second.SelectedQuestion, harness.Questions.SelectedItem);
        Assert.Same(second.SelectedQuestion.SelectedSpecialEvaluation, harness.Items.SelectedItem);
        first.DefinitionName = "以前のVMだけを変更";
        Render();
        Assert.Same(second, harness.View.ViewModel);
        Assert.Same(second.SelectedQuestion, harness.Questions.SelectedItem);
        harness.Window.Close();
        Assert.Equal(0, ViewSubscriptionCount(second, harness.View));
    }

    [AvaloniaFact]
    public void Display_edit_preview_validation_and_reattach_never_load_input_authenticate_or_run_ai()
    {
        NeverLoadInput loader = new();
        InputViewModel input = new(loader);
        // Even an available explicit input path must not become an automatic read.
        input.SetFilePath(Path.Combine(Path.GetTempPath(), "synthetic-t15-never-open.xlsx"));
        QuantificationDefinition inputBefore = input.DefinitionDraft;
        RecordingAuthenticationBoundary authentication = new(new ExecutionAuthenticationSnapshot(ExecutionAuthenticationState.AuthRequired));
        RecordingRunBoundary runner = new((_, _, _) => throw new InvalidOperationException("T15 must not start AI."));
        ExecutionViewModel execution = new(authentication, runner);
        ImportedPrompt imported = new()
        {
            Path = "unapplied-t15.txt",
            DisplayName = "unapplied-t15.txt",
            Content = "未適用の指示 {回答}",
        };
        QuantificationDesignViewModel design = CreateDesign(prompts: [imported]);
        using MainWindowViewModel host = new(new WorkflowNavigator(), input, design, execution,
            new ResultsOutputViewModel(new RecordingOutputBoundary()));
        using ViewHarness harness = new(host.DesignViewModel);
        harness.Window.DataContext = host;
        Select(harness.Questions, design.Questions[1]);
        SpecialEvaluationDesignItemViewModel special = design.SelectedQuestion!.SelectedSpecialEvaluation!;
        string oldPrompt = special.PromptTemplate;
        Assert.NotEqual(imported.Content, oldPrompt);

        SetText(ById<TextBox>(harness.View, special.CardAutomationId + "-Name"), "オフライン編集");
        Select(ById<ComboBox>(harness.View, special.CardAutomationId + "-Primary"), "D");
        ShowPrompt(harness);
        SetText(ById<TextBox>(harness.View, special.PromptAutomationId), "オフラインの指示 {回答}");
        Button validate = ById<Button>(harness.View, "SpecialSettingsValidate");
        Assert.True(validate.Focus(NavigationMethod.Tab));
        Press(harness.Window, Key.Enter);
        harness.Window.Content = null;
        Render();
        harness.Window.Content = harness.View;
        Render();

        Assert.Equal(0, loader.CallCount);
        Assert.Equal(0, authentication.CallCount);
        Assert.Equal(0, runner.CallCount);
        Assert.Null(execution.LastLoginTask);
        Assert.Null(execution.LastRunContext);
        Assert.False(execution.IsRunning);
        Assert.False(input.HasLoadedWorkbook);
        Assert.Same(inputBefore, input.DefinitionDraft);
        Assert.Equal(WorkflowStep.Input, host.CurrentStep);
        Assert.Same(imported, Assert.Single(design.ImportedPromptSources));
        Assert.Equal(imported.Content, Assert.Single(design.ImportedPrompts).Content);
        Assert.Equal("オフラインの指示 {回答}", special.PromptTemplate);
        Assert.Equal(ImportedPromptTarget.CustomEvaluator, design.SelectedPromptTarget);
    }

    private static QuantificationDesignViewModel CreateDesign(
        int questionCount = 2,
        int specialCount = 2,
        decimal specialPoints = 10m,
        string prefix = "t15",
        IEnumerable<ImportedPrompt>? prompts = null)
    {
        QuantificationDefinition seed = new QuantificationDesignViewModel().Draft;
        QuestionDefinition question = seed.Questions[0];
        EvaluatorDefinition evaluator = question.Evaluators[0];
        return new QuantificationDesignViewModel(seed with
        {
            Id = prefix + "-definition",
            BasePoints = 60m - specialPoints,
            SpecialPoints = specialPoints,
            Questions =
            [
                .. Enumerable.Range(1, questionCount).Select(index => question with
                {
                    Id = $"{prefix}-question-{index}",
                    DisplayName = "同じ設問名",
                    PrimarySourceColumn = "A",
                    SupportingSourceColumns = ["D"],
                    Points = index == questionCount ? 40m - 10m * (questionCount - 1) : 10m,
                    Evaluators = [evaluator with
                    {
                        Id = $"{prefix}-evaluator-{index}",
                        Criteria = [evaluator.Criteria[0] with { Id = $"{prefix}-criterion-{index}" }],
                    }],
                    SpecialEvaluations =
                    [
                        .. Enumerable.Range(1, specialCount).Select(item => new SpecialEvaluationDefinition
                        {
                            Id = $"{prefix}-special-{index}-{item}",
                            DisplayName = "同じ固有項目名",
                            PrimarySourceColumn = "B",
                            SupportingSourceColumns = ["C"],
                            PromptTemplate = $"固有項目 {index}-{item} の指示 {{回答}}",
                        }),
                    ],
                }),
            ],
        }, ["A", "B", "C", "D"], prompts);
    }

    private static SpecialEvaluationDefinition SelectedDefinition(QuantificationDesignViewModel design) =>
        design.Draft.Questions.Single(question => question.Id == design.SelectedQuestion!.Id)
            .SpecialEvaluations.Single(special => special.Id == design.SelectedQuestion!.SelectedSpecialEvaluation!.Id);

    private static T ById<T>(Control root, string id) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => AutomationProperties.GetAutomationId(control) == id);

    private static void Select(ComboBox selector, object item)
    {
        selector.SetCurrentValue(ComboBox.SelectedItemProperty, item);
        Render();
        Assert.Equal(item, selector.SelectedItem);
    }

    private static void SetText(TextBox box, string text)
    {
        box.SetCurrentValue(TextBox.TextProperty, text);
        Render();
        Assert.Equal(text, box.Text);
    }

    private static void ShowPrompt(ViewHarness harness) =>
        Click(harness, ById<TabItem>(harness.View, "SpecialSettingsPromptTab"));

    private static void ShowItemSettings(ViewHarness harness) =>
        Click(harness, ById<TabItem>(harness.View, "SpecialSettingsItemTab"));

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

    private static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        PhysicalKey physical = key switch
        {
            Key.Tab => PhysicalKey.Tab,
            Key.Enter => PhysicalKey.Enter,
            Key.Space => PhysicalKey.Space,
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

    private static void AssertUniqueIds(Control root)
    {
        string[] ids = root.GetVisualDescendants().OfType<Control>()
            .Select(AutomationProperties.GetAutomationId)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToArray();
        Assert.NotEmpty(ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static void AssertAccessibleTargetsFit(Control root)
    {
        TemplatedControl[] targets = root.GetVisualDescendants().OfType<TemplatedControl>()
            .Where(control => control.IsEffectivelyVisible
                && control is Button or ComboBox or CheckBox or TextBox or TabItem
                && !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(control))).ToArray();
        Assert.NotEmpty(targets);
        foreach (TemplatedControl target in targets)
        {
            Assert.True(target.MinHeight >= 44d);
            Assert.True(target.MinWidth >= 44d);
            Assert.True(target.Bounds.Height >= 44d);
            Assert.True(target.Bounds.Width >= 44d);
            Assert.True(target.FontSize >= 14d);
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(target)));
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(target)?.ToString()));
            AssertFits(target, root);
        }
    }

    private static void AssertFits(Control control, Control root)
    {
        Point origin = control.TranslatePoint(default, root)!.Value;
        Assert.True(control.Bounds.Width > 0d && control.Bounds.Height > 0d);
        Assert.True(origin.X >= -1d && origin.Y >= -1d
            && origin.X + control.Bounds.Width <= root.Bounds.Width + 1d
            && origin.Y + control.Bounds.Height <= root.Bounds.Height + 1d,
            $"{AutomationProperties.GetAutomationId(control)} must fit inside the finite content area.");
    }

    private static int ViewSubscriptionCount(UiObservableObject model, SpecialEvaluationSettingsView view)
    {
        // Inspect the existing field-like event, rather than relying on nondeterministic GC.
        FieldInfo? field = typeof(UiObservableObject).GetField(nameof(INotifyPropertyChanged.PropertyChanged),
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (field.GetValue(model) as MulticastDelegate)?.GetInvocationList()
            .Count(handler => ReferenceEquals(handler.Target, view)) ?? 0;
    }

    private sealed class ViewHarness : IDisposable
    {
        public ViewHarness(QuantificationDesignViewModel model, bool inheritDataContext = false)
        {
            View = inheritDataContext ? new SpecialEvaluationSettingsView() : new SpecialEvaluationSettingsView(model);
            Window = new Window
            {
                Width = 960,
                Height = 440,
                WindowDecorations = WindowDecorations.None,
                DataContext = model,
                Content = View,
            };
            Window.Show();
            Render();
        }

        public SpecialEvaluationSettingsView View { get; }
        public Window Window { get; }
        public ComboBox Questions => ById<ComboBox>(View, "SpecialSettingsQuestions");
        public ComboBox Items => ById<ComboBox>(View, "SpecialSettingsItems");

        public void Dispose() => Window.Close();
    }

    private sealed class NeverLoadInput : IInputWorkbookLoader
    {
        public int CallCount { get; private set; }

        public Task<InputWorkbookLoadResult> LoadAsync(string filePath, uint headerRow, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException("T15 must not read an input workbook.");
        }
    }
}