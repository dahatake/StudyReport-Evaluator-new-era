using System.Text;
using System.Text.Json;
using StudyReportEvaluator.App.Workspace;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Settings;

// Requirements: NFR-UX-004 (AC-080), FR-065 (AC-087)
public sealed class LayoutFileStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "StudyReportEvaluator-LayoutStore-" + Guid.NewGuid().ToString("N"));

    private string Directory => Path.Combine(root, "LocalAppData", "StudyReportEvaluator");

    private string LayoutPath => Path.Combine(Directory, LayoutFileStore.FileName);

    private string SettingsPath => Path.Combine(Directory, "setting.txt");

    [Fact]
    public void Missing_file_is_not_created_by_loading_and_each_change_is_saved_atomically_with_layout_data_only()
    {
        WorkspaceLayoutService service = new(new LayoutFileStore(LayoutPath));
        service.Load();

        Assert.Equal(LayoutLoadStatus.Missing, service.LoadStatus);
        Assert.True(service.CanPersist);
        Assert.False(System.IO.Directory.Exists(root));
        Assert.Equal([Persona.Grader], service.Personas);

        WorkspaceLayout changed = service.GetLayout(WorkspaceScreens.Results)
            .Execute(WorkspacePanelOperation.Hide, WorkspacePanelIds.ResultsChart)
            .Execute(WorkspacePanelOperation.Grow, WorkspacePanelIds.ResultsList);
        service.SetLayout(WorkspaceScreens.Results, changed);
        Assert.True(service.SetPersonaSelected(Persona.MaintenanceEngineer, true));

        Assert.Equal(2, service.SaveCount);
        Assert.Equal([LayoutFileStore.FileName], System.IO.Directory.GetFiles(Directory).Select(Path.GetFileName));
        using JsonDocument json = JsonDocument.Parse(File.ReadAllBytes(LayoutPath));
        Assert.Equal(["schemaVersion", "personas", "screens"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["P-01", "P-03"], json.RootElement.GetProperty("personas").EnumerateArray().Select(item => item.GetString()));
        JsonElement results = json.RootElement.GetProperty("screens").GetProperty(WorkspaceScreens.Results);
        Assert.Equal(["orientation", "groups", "hidden", "maximized"], results.EnumerateObject().Select(property => property.Name));
        Assert.Contains(WorkspacePanelIds.ResultsChart, results.GetProperty("hidden").EnumerateArray().Select(item => item.GetString()));

        WorkspaceLayoutService restarted = new(new LayoutFileStore(LayoutPath));
        restarted.Load();
        Assert.Equal(LayoutLoadStatus.Loaded, restarted.LoadStatus);
        Assert.Equal(changed, restarted.GetLayout(WorkspaceScreens.Results));
        Assert.Equal([Persona.Grader, Persona.MaintenanceEngineer], restarted.Personas);
        Assert.True(restarted.HasCustomLayout(WorkspaceScreens.Results));
        Assert.False(restarted.HasCustomLayout(WorkspaceScreens.Execution));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"schemaVersion\":2,\"personas\":[\"P-01\"],\"screens\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"personas\":[],\"screens\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"personas\":[\"P-01\"],\"screens\":{\"Step.Results\":{\"orientation\":\"horizontal\",\"groups\":[{\"panels\":[\"C:\\\\Users\\\\student.xlsx\"],\"size\":1}],\"hidden\":[],\"maximized\":null}}}")]
    [InlineData("{\"schemaVersion\":1,\"personas\":[\"P-01\"],\"screens\":{},\"answer\":\"学生の回答\"}")]
    public void Unreadable_or_unknown_layout_file_is_ignored_and_never_rewritten(string content)
    {
        System.IO.Directory.CreateDirectory(Directory);
        byte[] original = Encoding.UTF8.GetBytes(content);
        File.WriteAllBytes(LayoutPath, original);
        WorkspaceLayoutService service = new(new LayoutFileStore(LayoutPath));

        service.Load();

        Assert.Contains(service.LoadStatus, new LayoutLoadStatus?[] { LayoutLoadStatus.Invalid, LayoutLoadStatus.UnsupportedVersion });
        Assert.False(service.CanPersist);
        Assert.Equal(WorkspaceDefaults.Resolve(WorkspaceScreens.Results, [Persona.Grader]), service.GetLayout(WorkspaceScreens.Results));
        Assert.Contains("ファイルは変更していません", service.StatusText, StringComparison.Ordinal);

        service.SetLayout(WorkspaceScreens.Results, service.GetLayout(WorkspaceScreens.Results)
            .Execute(WorkspacePanelOperation.Maximize, WorkspacePanelIds.ResultsList));
        service.SetPersonaSelected(Persona.AssessmentDesigner, true);

        Assert.Equal(WorkspacePanelIds.ResultsList, service.GetLayout(WorkspaceScreens.Results).MaximizedPanelId);
        Assert.Equal(0, service.SaveCount);
        Assert.Equal(original, File.ReadAllBytes(LayoutPath));
        Assert.Equal([LayoutFileStore.FileName], System.IO.Directory.GetFiles(Directory).Select(Path.GetFileName));
    }

    [Fact]
    public void Layout_saves_never_touch_setting_txt_and_a_bom_is_accepted()
    {
        System.IO.Directory.CreateDirectory(Directory);
        byte[] settings = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"maxConcurrency\":8}");
        File.WriteAllBytes(SettingsPath, settings);
        DateTime settingsWritten = File.GetLastWriteTimeUtc(SettingsPath);
        byte[] layout = [.. Encoding.UTF8.GetPreamble(), .. LayoutFileStore.Serialize(new LayoutDocument(
            [Persona.AssessmentDesigner], System.Collections.Immutable.ImmutableDictionary<string, WorkspaceLayout>.Empty))];
        File.WriteAllBytes(LayoutPath, layout);
        WorkspaceLayoutService service = new(new LayoutFileStore(LayoutPath));

        service.Load();
        Assert.Equal(LayoutLoadStatus.Loaded, service.LoadStatus);
        Assert.Equal([Persona.AssessmentDesigner], service.Personas);
        Assert.False(service.CanChangeAssessmentDesigner);
        Assert.False(service.SetPersonaSelected(Persona.AssessmentDesigner, false));
        Assert.Equal([Persona.AssessmentDesigner], service.Personas);
        service.ResetLayout(WorkspaceScreens.Design);
        service.SetLayout(WorkspaceScreens.Design, service.GetLayout(WorkspaceScreens.Design)
            .Execute(WorkspacePanelOperation.ToggleOrientation, WorkspacePanelIds.DesignForm));
        service.ResetLayout(WorkspaceScreens.Design);

        Assert.Equal(2, service.SaveCount);
        Assert.False(service.HasCustomLayout(WorkspaceScreens.Design));
        Assert.Equal(settings, File.ReadAllBytes(SettingsPath));
        Assert.Equal(settingsWritten, File.GetLastWriteTimeUtc(SettingsPath));
        Assert.Equal(["layout.json", "setting.txt"], System.IO.Directory.GetFiles(Directory).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Store_requires_a_fully_qualified_file_path_and_does_not_reveal_it()
    {
        Assert.Throws<ArgumentException>(() => new LayoutFileStore("layout.json"));
        Assert.Throws<ArgumentException>(() => new LayoutFileStore(" "));
        LayoutFileStore store = new(LayoutPath);
        Assert.DoesNotContain(root, store.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LayoutLoadStatus.Missing, store.Load().Status);
        Assert.False(System.IO.Directory.Exists(root));
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(root))
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }
}
