using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using StudyReportEvaluator.App.Composition;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Tests.Workbooks.Intake;
using StudyReportEvaluator.App.Tests.Workflow;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using StudyReportEvaluator.App.Workflow;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Settings;

// Requirements: FR-073 (AC-097)
public sealed class PredecessorCompatibilityTests
{
    private static readonly Version PredecessorVersion = new(0, 8, 6);

    // A setting.txt exactly as the predecessor v0.8.6 (dahatake/StudyReport-Evaluator) writes it after an
    // explicit save: compact UTF-8 JSON without BOM, camelCase, non-ASCII escaped, settings schema 1, with
    // common settings, modelPreferences, one definition and the display-only cachedModels. Synthetic values only.
    private const string PredecessorSettingsJson =
        """{"schemaVersion":1,"preferredModelId":"auto","modelPreferences":[{"modelId":"claude-sonnet-5","reasoningEffort":"low","contextTier":"default"}],"maxConcurrency":8,"outputDirectoryOverride":"C:\\Reports\\Results","definition":{"id":"DEF-V086","name":"\u524D\u7248\u306E\u63A1\u70B9\u5B9A\u7FA9","revision":"1","sourceSheet":"Responses","headerRow":1,"firstDataRow":2,"lastDataRow":531,"basePoints":50,"specialPoints":10,"similarityPenaltyWeight":0.1,"roundingDigits":1,"questions":[{"id":"Q1","displayName":"\u8A2D\u554F1","questionText":"\u8A2D\u554F1 \u30EC\u30DD\u30FC\u30C8\u672C\u6587","primarySourceColumn":"D","supportingSourceColumns":["F"],"points":40,"evaluators":[{"id":"E1","displayName":"Knowledge Cover","type":0,"weight":3,"range":{"minimum":0,"maximum":10},"criteria":[{"id":"C1","displayName":"\u8AD6\u70B9","description":"\u8AD6\u70B9:\n- xxx","weight":4,"range":null,"enabled":true}],"builtInTemplateVersion":"knowledge-v1","customPromptTemplate":null,"enabled":true}],"specialEvaluations":[{"id":"S1","displayName":"\u56FA\u6709","primarySourceColumn":"E","supportingSourceColumns":[],"promptTemplate":"\u56FA\u6709 {\u56DE\u7B54}","enabled":true}],"enabled":true}]},"cachedModels":[{"id":"auto","maximumPromptTokens":null,"maximumContextWindowTokens":null},{"id":"claude-sonnet-5","maximumPromptTokens":128000,"maximumContextWindowTokens":200000}]}""";

    [Fact]
    public void Settings_path_is_the_predecessor_location_under_local_application_data()
    {
        string localData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);
        Assert.True(Path.IsPathFullyQualified(localData), "LocalApplicationData must resolve on the test host.");

        // Resolve only; never initialize this production registration, so the real setting.txt is not read.
        using MainWindowViewModel shell = ServiceRegistration.FromStartup(LaunchStartupState.Empty)
            .CreateMainWindowViewModel();

        Assert.Equal(
            Path.GetFullPath(Path.Combine(localData, "StudyReportEvaluator", "setting.txt")),
            shell.Settings.FilePath);
        Assert.Null(shell.Settings.LastLoadTask);
    }

    [Fact]
    public async Task Setting_txt_saved_by_v0_8_6_loads_common_settings_and_definition_without_changing_bytes()
    {
        string localData = Path.Combine(Path.GetTempPath(), $"StudyReportEvaluator-FR073-{Guid.NewGuid():N}");
        try
        {
            using MainWindowViewModel shell = ServiceRegistration.FromStartup(LaunchStartupState.Empty, localData)
                .CreateMainWindowViewModel();
            string settingsPath = shell.Settings.FilePath;
            Assert.Equal(Path.Combine(localData, "StudyReportEvaluator", "setting.txt"), settingsPath);
            byte[] original = new UTF8Encoding(false, true).GetBytes(PredecessorSettingsJson);
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllBytes(settingsPath, original);
            DateTime writtenAtUtc = File.GetLastWriteTimeUtc(settingsPath);

            SettingsLoadResult result = await new SettingsFileStore(settingsPath)
                .LoadAsync(TestContext.Current.CancellationToken);

            Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
            ApplicationSettings settings = Assert.IsType<ApplicationSettings>(result.Settings);
            Assert.Equal(ApplicationSettings.CurrentSchemaVersion, settings.SchemaVersion);
            Assert.Equal("auto", settings.PreferredModelId);
            Assert.Equal(8, settings.MaxConcurrency);
            Assert.Equal(@"C:\Reports\Results", settings.OutputDirectoryOverride);
            ModelSelectionPreference preference = Assert.Single(settings.ModelPreferences!.Value);
            Assert.Equal(new ModelSelectionPreference("claude-sonnet-5", "low", "default"), preference);
            Assert.Equal(["auto", "claude-sonnet-5"], settings.CachedModels!.Value.Select(model => model.Id));
            Assert.Equal(128000, settings.CachedModels.Value[1].MaximumPromptTokens);

            QuantificationDefinition definition = Assert.IsType<QuantificationDefinition>(settings.Definition);
            Assert.Equal("DEF-V086", definition.Id);
            Assert.Equal("前版の採点定義", definition.Name);
            Assert.Equal("Responses", definition.SourceSheet);
            Assert.Equal((2, 531), (definition.FirstDataRow, definition.LastDataRow));
            Assert.Equal((50m, 10m), (definition.BasePoints, definition.SpecialPoints));
            QuestionDefinition question = Assert.Single(definition.Questions);
            Assert.Equal("設問1 レポート本文", question.QuestionText);
            Assert.Equal(("D", 40m), (question.PrimarySourceColumn, question.Points));
            Assert.Equal(["F"], question.SupportingSourceColumns);
            EvaluatorDefinition evaluator = Assert.Single(question.Evaluators);
            Assert.Equal(EvaluatorType.KnowledgeCoverage, evaluator.Type);
            Assert.Equal("論点:\n- xxx", Assert.Single(evaluator.Criteria).Description);
            Assert.Equal("固有 {回答}", Assert.Single(question.SpecialEvaluations).PromptTemplate);

            // Loading is read-only: same bytes, same timestamp, and no sibling file (no repair or migration).
            byte[] after = File.ReadAllBytes(settingsPath);
            Assert.Equal(original, after);
            Assert.Equal(SHA256.HashData(original), SHA256.HashData(after));
            Assert.Equal(writtenAtUtc, File.GetLastWriteTimeUtc(settingsPath));
            Assert.Equal(settingsPath, Assert.Single(Directory.GetFileSystemEntries(Path.GetDirectoryName(settingsPath)!)));
        }
        finally
        {
            if (Directory.Exists(localData))
            {
                Directory.Delete(localData, recursive: true);
            }
        }
    }

    [Fact]
    public void Predecessor_checkpoint_with_a_different_copilot_cli_runtime_is_rejected_without_changing_bytes()
    {
        using TemporaryWorkbook input = X01SyntheticWorkbookFactory.Create();
        QuantificationDefinition definition = U01TestSupport.Definition(
            2,
            2,
            U01TestSupport.Question("Q1", "A", ["B"], true, U01TestSupport.Evaluator("E1", "C1")));
        QuantificationSnapshot snapshot = QuantificationSnapshot.Create(definition);
        EvaluationPlan plan = new EvaluationPlanBuilder().Build(
            snapshot,
            U01TestSupport.ValidateMapping(definition).Mapping!);
        CheckpointRuntimeIdentity predecessorRuntime = new()
        {
            ApplicationIdentity = "StudyReportEvaluator.App/0.8.6",
            CliVersion = "1.0.79",
            CliSha256 = new string('A', 64),
            SdkInformationalVersion = "1.0.11",
        };
        DateTimeOffset startedAtUtc = new(2026, 10, 1, 1, 0, 0, TimeSpan.Zero);
        OutputPathReservation reservation = new OutputPathPlanner().Reserve(
            input.Path,
            startedAtUtc.ToOffset(TimeSpan.FromHours(9)));
        CheckpointEnvelope envelope = new()
        {
            InputPath = Path.GetFullPath(input.Path),
            Input = new InputSnapshotService().Capture(input.Path),
            DefinitionCanonicalJson = snapshot.CanonicalJson,
            DefinitionSha256 = snapshot.Sha256,
            NormalModelId = "model-test",
            ReferenceModelId = "model-test",
            Runtime = predecessorRuntime,
            FinalPath = reservation.FinalPath,
            PartialPath = reservation.PartialPath,
            StartedAtUtc = startedAtUtc,
            SavedAtUtc = startedAtUtc.AddMinutes(1),
        };
        CheckpointStore store = new();
        Assert.True(store.Create(envelope, TestContext.Current.CancellationToken).IsSuccess);
        byte[] original = File.ReadAllBytes(envelope.PartialPath);
        DateTime writtenAtUtc = File.GetLastWriteTimeUtc(envelope.PartialPath);

        CheckpointLoadResult loaded = store.Load(envelope.PartialPath, TestContext.Current.CancellationToken);
        Assert.True(loaded.IsSuccess, loaded.ToString());
        CheckpointEnvelope checkpoint = loaded.Envelope!;
        InputSnapshot currentInput = new InputSnapshotService().Capture(input.Path);
        string currentApplication = QuantificationRunBoundary.ApplicationIdentity();
        Assert.StartsWith("StudyReportEvaluator.App/", currentApplication, StringComparison.Ordinal);
        Assert.NotEqual(predecessorRuntime.ApplicationIdentity, currentApplication);

        foreach (CheckpointRuntimeIdentity differentCli in new[]
                 {
                     predecessorRuntime with { ApplicationIdentity = currentApplication, CliVersion = "1.0.80" },
                     predecessorRuntime with { ApplicationIdentity = currentApplication, CliSha256 = new string('B', 64) },
                     predecessorRuntime with { ApplicationIdentity = currentApplication, SdkInformationalVersion = "1.0.12" },
                 })
        {
            ResumeAdmissionReport rejected = ResumeAdmissionEvaluator.Evaluate(
                checkpoint, envelope.PartialPath, snapshot, plan, currentInput,
                envelope.InputPath, checkpoint.NormalModelId, differentCli);

            Assert.False(rejected.CanResume);
            Assert.Equal(CheckpointAdmissionStatusCodes.RuntimeMismatch, rejected.BlockingStatusCode);
            Assert.Equal(
                [ResumeAdmissionItem.Runtime],
                rejected.Findings.Where(finding => !finding.IsSatisfied).Select(finding => finding.Item));
        }

        // Control: with the same CLI runtime and the same app major (0), the predecessor checkpoint is admitted.
        ResumeAdmissionReport admitted = ResumeAdmissionEvaluator.Evaluate(
            checkpoint, envelope.PartialPath, snapshot, plan, currentInput,
            envelope.InputPath, checkpoint.NormalModelId,
            predecessorRuntime with { ApplicationIdentity = currentApplication });
        Assert.True(admitted.CanResume);

        byte[] after = File.ReadAllBytes(envelope.PartialPath);
        Assert.Equal(original, after);
        Assert.Equal(SHA256.HashData(original), SHA256.HashData(after));
        Assert.Equal(writtenAtUtc, File.GetLastWriteTimeUtc(envelope.PartialPath));
    }

    [Fact]
    public void Product_version_continues_after_the_predecessor_v0_8_6()
    {
        Assembly app = typeof(SettingsFileStore).Assembly;
        string informational = app.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion.Split('+', 2)[0];
        Version product = Version.Parse(informational.Split('-', 2)[0]);
        Version file = Version.Parse(FileVersionInfo.GetVersionInfo(app.Location).FileVersion!);

        Assert.True(product > PredecessorVersion, $"Product version {product} must be greater than {PredecessorVersion}.");
        Assert.True(file > PredecessorVersion, $"File version {file} must be greater than {PredecessorVersion}.");
        Assert.Equal(0, product.Major);
        Assert.Equal(new Version(product.Major, product.Minor, product.Build, 0), file);
        Assert.Equal("StudyReportEvaluator.App/" + informational, QuantificationRunBoundary.ApplicationIdentity());
    }
}
