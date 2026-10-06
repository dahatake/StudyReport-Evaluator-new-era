using System.Reflection;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StudyReportEvaluator.App.Composition;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Tests.Workbooks.Mapping;
using StudyReportEvaluator.App.ViewModels;
using StudyReportEvaluator.App.Views;
using StudyReportEvaluator.Core.Domain;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Launch;

// Requirements: FR-053 (AC-054)
public sealed class LaunchOptionsTests
{
    [Fact]
    public void Parser_accepts_case_insensitive_input_and_ordered_prompts_deduplicated_by_canonical_path()
    {
        using LaunchDirectory directory = new();
        string input = directory.File("input.xlsx", []);
        string first = directory.File("first.txt", "first"u8.ToArray());
        string second = directory.File("second.txt", "second"u8.ToArray());

        LaunchOptions options = LaunchOptions.Parse(
            [
                "--INPUT", Path.GetFileName(input),
                "--prompt", Path.GetFileName(first),
                "--PROMPT", Path.GetFileName(second),
                "--prompt", Path.Combine(".", Path.GetFileName(first)),
            ],
            directory.Path);

        Assert.Equal(Path.GetFullPath(input), options.InputPath);
        Assert.Equal([Path.GetFullPath(first), Path.GetFullPath(second)], options.PromptPaths);
        Assert.Contains("PromptCount = 2", options.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(input, options.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(first, options.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unknown", LaunchErrorCodes.UnknownOption)]
    [InlineData("missing", LaunchErrorCodes.MissingValue)]
    [InlineData("duplicate-input", LaunchErrorCodes.DuplicateInput)]
    [InlineData("non-txt", LaunchErrorCodes.PromptExtensionInvalid)]
    public void Parser_rejects_closed_option_contract_with_safe_errors(string scenario, string expectedCode)
    {
        string[] args = scenario switch
        {
            "unknown" => ["--other", "value"],
            "missing" => ["--prompt"],
            "duplicate-input" => ["--input", "a.xlsx", "--INPUT", "b.xlsx"],
            "non-txt" => ["--prompt", "prompt.md"],
            _ => throw new InvalidOperationException(),
        };

        LaunchOptionsException error = Assert.Throws<LaunchOptionsException>(() =>
            LaunchOptions.Parse(args, Path.GetTempPath()));

        Assert.Equal(expectedCode, error.Code);
        Assert.Contains("<redacted>", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("prompt.md", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Prompt_loader_accepts_strict_utf8_with_or_without_BOM_and_preserves_basename_and_order()
    {
        using LaunchDirectory directory = new();
        string content = "評価 {回答} {評価項目} 😀";
        string bom = directory.File(
            "01-bom.txt",
            Encoding.UTF8.Preamble.ToArray().Concat(Encoding.UTF8.GetBytes(content)).ToArray());
        string plain = directory.File("02-plain.txt", Encoding.UTF8.GetBytes("special {回答}"));
        LaunchOptions options = LaunchOptions.Parse(
            ["--prompt", bom, "--prompt", plain],
            directory.Path);

        var prompts = new PromptFileLoader().Load(options);

        Assert.Equal(["01-bom.txt", "02-plain.txt"], prompts.Select(prompt => prompt.DisplayName));
        Assert.Equal(content, prompts[0].Content);
        Assert.Equal("special {回答}", prompts[1].Content);
        Assert.DoesNotContain(content, prompts[0].ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(bom, prompts[0].ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("invalid-utf8", LaunchErrorCodes.PromptUtf8Invalid)]
    [InlineData("empty", LaunchErrorCodes.PromptLengthInvalid)]
    [InlineData("oversized", LaunchErrorCodes.PromptLengthInvalid)]
    [InlineData("missing", LaunchErrorCodes.PromptReadFailed)]
    public void Prompt_loader_rejects_invalid_encoding_length_or_read_failure_without_content_echo(
        string scenario,
        string expectedCode)
    {
        using LaunchDirectory directory = new();
        byte[] privateBytes = scenario switch
        {
            "invalid-utf8" => [0xFF, 0xFE, 0x41],
            "empty" => [],
            "oversized" => Encoding.UTF8.GetBytes(new string('X', PromptFileLoader.MaximumCharacters + 1)),
            "missing" => [],
            _ => throw new InvalidOperationException(),
        };
        string path = scenario == "missing"
            ? Path.Combine(directory.Path, "missing.txt")
            : directory.File("PRIVATE-CANARY.txt", privateBytes);

        LaunchOptionsException error = Assert.Throws<LaunchOptionsException>(() =>
            new PromptFileLoader().LoadOne(path));

        Assert.Equal(expectedCode, error.Code);
        Assert.DoesNotContain("PRIVATE", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(path, error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Valid_startup_prefills_and_loads_input_then_prompts_require_explicit_reusable_target_copy()
    {
        using LaunchDirectory directory = new();
        using X02TemporaryWorkbook workbook = X02SyntheticWorkbookFactory.CreateSampleLike();
        string customContent = "Custom launch {回答} {評価項目}";
        string specialContent = "Special launch {回答}";
        string custom = directory.File("01-custom.txt", Encoding.UTF8.GetBytes(customContent));
        string special = directory.File("02-special.txt", Encoding.UTF8.GetBytes(specialContent));
        LaunchStartupState startup = LaunchStartupState.Create(
            ["--input", workbook.Path, "--prompt", custom, "--prompt", special],
            directory.Path);
        ServiceRegistration services = ServiceRegistration.FromStartup(startup);
        MainWindowViewModel shell = services.CreateMainWindowViewModel();

        Assert.True(startup.IsValid);
        Assert.Equal(workbook.Path, shell.InputViewModel.FilePath);
        Assert.False(shell.InputViewModel.HasLoadedWorkbook);
        Assert.Equal(["01-custom.txt", "02-special.txt"],
            shell.DesignViewModel.ImportedPrompts.Select(prompt => prompt.DisplayName));
        Assert.Null(shell.ExecutionViewModel.LastRunContext);
        Assert.Equal(ExecutionAuthenticationState.NotChecked, shell.ExecutionViewModel.AuthenticationState);
        await shell.InputViewModel.LoadLaunchInputIfRequestedAsync(TestContext.Current.CancellationToken);
        Assert.True(shell.InputViewModel.HasLoadedWorkbook);
        shell.NextCommand.Execute(null);
        QuantificationDesignViewModel design = shell.DesignViewModel;
        Assert.Equal(["01-custom.txt", "02-special.txt"],
            design.ImportedPrompts.Select(prompt => prompt.DisplayName));
        QuestionDesignItemViewModel question = Assert.IsType<QuestionDesignItemViewModel>(design.SelectedQuestion);
        EvaluatorDesignItemViewModel customEvaluator = design.AddEvaluator(question.Id, EvaluatorType.CustomPrompt);
        question.SelectedEvaluator = customEvaluator;
        string originalTemplate = customEvaluator.CustomPromptTemplate!;
        Assert.NotEqual(customContent, originalTemplate);
        design.SelectedImportedPrompt = design.ImportedPrompts[0];
        design.SelectedPromptTarget = ImportedPromptTarget.CustomEvaluator;
        Assert.True(design.CanApplyImportedPrompt);
        design.ApplyImportedPrompt();
        Assert.Equal(customContent, customEvaluator.CustomPromptTemplate);
        SpecialEvaluationDesignItemViewModel specialTarget = design.AddSpecialEvaluation(question.Id);
        question.SelectedSpecialEvaluation = specialTarget;
        design.SelectedImportedPrompt = design.ImportedPrompts[1];
        design.SelectedPromptTarget = ImportedPromptTarget.SpecialEvaluation;
        Assert.True(design.CanApplyImportedPrompt);
        design.ApplyImportedPrompt();
        Assert.Equal(specialContent, specialTarget.PromptTemplate);
        question.SelectedEvaluator = customEvaluator;
        design.SelectedImportedPrompt = design.ImportedPrompts[0];
        design.SelectedPromptTarget = ImportedPromptTarget.CustomEvaluator;
        design.ApplyImportedPrompt();
        Assert.Equal(customContent, customEvaluator.CustomPromptTemplate);
        Assert.Null(shell.ExecutionViewModel.LastRunContext);
    }

    [AvaloniaFact]
    public void Invalid_startup_creates_a_safe_error_window_instead_of_the_main_workflow()
    {
        LaunchStartupState startup = LaunchStartupState.Create(
            ["--prompt", "private.md"],
            Path.GetTempPath());
        LaunchOptionsException error = Assert.IsType<LaunchOptionsException>(startup.Error);
        StartupErrorWindow window = new(error);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            TextBlock code = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), item =>
                AutomationProperties.GetAutomationId(item) == "StartupErrorCode");
            Assert.Equal(LaunchErrorCodes.PromptExtensionInvalid, code.Text);
            Assert.DoesNotContain("private.md", window.ToString(), StringComparison.Ordinal);
            Assert.Equal("StartupErrorCode", AutomationProperties.GetAutomationId(code));
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void Launch_contract_exposes_no_AI_autorun_or_custom_protocol_surface()
    {
        string[] prohibited = ["AutoRun", "Execute", "Send", "UriScheme", "Daemon", "Server"];
        MemberInfo[] members = typeof(LaunchOptions)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Concat(typeof(LaunchStartupState).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .ToArray();
        Assert.DoesNotContain(members, member => prohibited.Any(term =>
            member.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class LaunchDirectory : IDisposable
    {
        internal LaunchDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "StudyReportEvaluator-Launch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        internal string Path { get; }
        internal string File(string name, byte[] content)
        {
            string path = System.IO.Path.Combine(Path, name);
            System.IO.File.WriteAllBytes(path, content);
            return path;
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
