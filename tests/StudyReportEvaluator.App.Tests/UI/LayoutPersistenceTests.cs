using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using StudyReportEvaluator.App.Composition;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Navigation;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.App.Workspace;
using Xunit;
using static StudyReportEvaluator.App.Tests.UI.PanelWorkspaceTests;
using static StudyReportEvaluator.App.Tests.UI.ResponsiveLayoutTests;

namespace StudyReportEvaluator.App.Tests.UI;

// Requirements: NFR-UX-004 (AC-080), FR-065 (AC-087), FR-040
public sealed class LayoutPersistenceTests : IDisposable
{
    private const string Canary = "CANARY-学生-山田花子-回答本文-7f3a";

    private readonly string root = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-LayoutUi-" + Guid.NewGuid().ToString("N"));

    private string LocalData => Path.Combine(root, "LocalAppData");

    private string LayoutPath => Path.Combine(LocalData, "StudyReportEvaluator", "layout.json");

    private string SettingsPath => Path.Combine(LocalData, "StudyReportEvaluator", "setting.txt");

    [AvaloniaFact]
    public async Task Results_layout_is_restored_after_restart_and_setting_txt_bytes_never_change()
    {
        Assert.True((await new SettingsFileStore(SettingsPath).SaveAsync(new ApplicationSettings(),
            TestContext.Current.CancellationToken)).IsSuccess);
        byte[] settings = File.ReadAllBytes(SettingsPath);
        WorkspaceLayout changed;
        WorkspaceEffectiveLayout shown;
        using (Session first = await Session.StartAsync(LocalData))
        {
            Assert.Equal(LayoutLoadStatus.Missing, first.Shell.Workspace.LoadStatus);
            first.NavigateTo(WorkflowStep.Results);
            PanelWorkspace workspace = first.Workspace;
            InvokeMenu(workspace, WorkspacePanelIds.ResultsChart, WorkspacePanelOperation.Hide);
            InvokeMenu(workspace, WorkspacePanelIds.ResultsList, WorkspacePanelOperation.Grow);
            InvokeMenu(workspace, WorkspacePanelIds.ResultsList, WorkspacePanelOperation.ToggleOrientation);
            changed = workspace.Layout;
            shown = Assert.IsType<WorkspaceEffectiveLayout>(workspace.EffectiveLayout);
            Assert.Equal(WorkspaceOrientation.Vertical, shown.Orientation);
            Assert.True(File.Exists(LayoutPath));
            Assert.Equal(settings, File.ReadAllBytes(SettingsPath));
        }

        using Session second = await Session.StartAsync(LocalData);
        Assert.Equal(LayoutLoadStatus.Loaded, second.Shell.Workspace.LoadStatus);
        second.NavigateTo(WorkflowStep.Results);
        Assert.Equal(changed, second.Workspace.Layout);
        Assert.Equal(shown.Orientation, second.Workspace.EffectiveLayout?.Orientation);
        Assert.Equal(shown.Groups.Select(group => group.PanelIds.ToArray()),
            second.Workspace.EffectiveLayout?.Groups.Select(group => group.PanelIds.ToArray()));
        Assert.False(second.Workspace.IsPresented(WorkspacePanelIds.ResultsChart));
        Assert.Equal(settings, File.ReadAllBytes(SettingsPath));
        Assert.False(second.Shell.Settings.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task Corrupt_layout_json_starts_with_the_default_layout_and_is_left_byte_for_byte()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath)!);
        byte[] corrupt = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"personas\":[\"P-01\"],\"screens\":{\"Step.Results\":");
        File.WriteAllBytes(LayoutPath, corrupt);

        using Session session = await Session.StartAsync(LocalData);
        Assert.Equal(LayoutLoadStatus.Invalid, session.Shell.Workspace.LoadStatus);
        session.NavigateTo(WorkflowStep.Results);
        Assert.Equal(session.Shell.Workspace.GetDefaultLayout(WorkspaceScreens.Results), session.Workspace.Layout);
        Assert.True(session.Workspace.IsPresented(WorkspacePanelIds.ResultsChart));
        InvokeMenu(session.Workspace, WorkspacePanelIds.ResultsDetail, WorkspacePanelOperation.Maximize);
        Assert.Equal(WorkspacePanelIds.ResultsDetail, session.Workspace.Layout.MaximizedPanelId);
        Assert.True(session.Shell.Workspace.SetPersonaSelected(Persona.MaintenanceEngineer, true));

        Assert.Equal(corrupt, File.ReadAllBytes(LayoutPath));
        Assert.Contains("ファイルは変更していません", session.Shell.Settings.Workspace.StatusText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task A_run_with_canary_student_text_leaves_no_canary_in_layout_json()
    {
        WorkspaceLayoutService service = new(new LayoutFileStore(LayoutPath));
        service.Load();
        using WorkspaceUiFixture fixture = await WorkspaceUiFixture.CreateAsync(service, primary: Canary, questionName: Canary);
        fixture.Shell.ResultsOutputViewModel.OutputPath = Path.Combine(root, Canary + ".xlsx");
        foreach (WorkflowStep step in new[] { WorkflowStep.Design, WorkflowStep.Execution, WorkflowStep.Results })
        {
            fixture.NavigateTo(step);
            PanelWorkspace workspace = fixture.Workspace;
            string first = workspace.Panels[0].PanelId;
            workspace.Execute(WorkspacePanelOperation.ToggleOrientation, first);
            workspace.Execute(WorkspacePanelOperation.Grow, first);
            workspace.Execute(WorkspacePanelOperation.MoveLater, first);
            Render();
        }

        fixture.NavigateTo(WorkflowStep.Results);
        // The canary was the student answer of the run and is shown in the output path on screen.
        Assert.Contains(Canary, Required<Avalonia.Controls.TextBox>(fixture.CurrentView, "OutputPathTextBox").Text, StringComparison.Ordinal);
        service.SetPersonaSelected(Persona.AssessmentDesigner, true);

        string json = File.ReadAllText(LayoutPath, Encoding.UTF8);
        Assert.DoesNotContain(Canary, json, StringComparison.Ordinal);
        Assert.DoesNotContain("山田", json, StringComparison.Ordinal);
        Assert.DoesNotContain(root, json, StringComparison.OrdinalIgnoreCase);
        HashSet<string> allowed = [.. WorkspaceScreens.All, "P-01", "P-02", "P-03", "horizontal", "vertical",
            "schemaVersion", "personas", "screens", "orientation", "groups", "panels", "size", "hidden", "maximized",
            WorkspacePanelIds.DesignForm, WorkspacePanelIds.DesignAllocation, WorkspacePanelIds.ExecutionConditions,
            WorkspacePanelIds.ExecutionProgress, WorkspacePanelIds.ExecutionCost, WorkspacePanelIds.ResultsList,
            WorkspacePanelIds.ResultsDetail, WorkspacePanelIds.ResultsChart, WorkspacePanelIds.ResultsCost];
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.All(Strings(document.RootElement), value => Assert.Contains(value, allowed));
        Assert.Equal(3, document.RootElement.GetProperty("screens").EnumerateObject().Count());
        fixture.AssertPassive();
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static IEnumerable<string> Strings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return element.GetString()!;
                break;
            case JsonValueKind.Array:
                foreach (string value in element.EnumerateArray().SelectMany(Strings))
                {
                    yield return value;
                }

                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (string value in Strings(property.Value))
                    {
                        yield return value;
                    }
                }

                break;
        }
    }

    /// <summary>The production composition with an injected local-data directory (never the real one).</summary>
    private sealed class Session : IDisposable
    {
        private Session(MainWindowViewModel shell)
        {
            Shell = shell;
            Window = new MainWindow(shell);
            Window.Show();
            Render();
        }

        public MainWindowViewModel Shell { get; }

        public MainWindow Window { get; }

        public PanelWorkspace Workspace => Assert.Single(Avalonia.VisualTree.VisualExtensions
            .GetVisualDescendants(CurrentView(Window)).OfType<PanelWorkspace>());

        public static async Task<Session> StartAsync(string localData)
        {
            ServiceRegistration services = ServiceRegistration.FromStartup(LaunchStartupState.Empty, localData);
            MainWindowViewModel shell = services.CreateMainWindowViewModel();
            await services.InitializeAsync(shell, TestContext.Current.CancellationToken);
            return new Session(shell);
        }

        public void NavigateTo(WorkflowStep step)
        {
            while (Shell.CurrentStep != step)
            {
                Shell.NextCommand.Execute(null);
                Render();
            }
        }

        public void Dispose()
        {
            Window.Close();
            Render();
        }
    }
}
