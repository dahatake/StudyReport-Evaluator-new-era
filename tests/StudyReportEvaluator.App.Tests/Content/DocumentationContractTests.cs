using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using StudyReportEvaluator.App.Copilot;
using StudyReportEvaluator.App.Launch;
using StudyReportEvaluator.App.Resources;
using StudyReportEvaluator.App.Settings;
using StudyReportEvaluator.App.Workbooks.Checkpoint;
using StudyReportEvaluator.App.Workbooks.Intake;
using StudyReportEvaluator.App.Workbooks.Writing;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Content;

// Requirements: FR-061 (AC-062), FR-063 (AC-064), FR-064, NFR-SEC-004 (AC-069), NFR-SEC-005 (AC-070), NFR-UX-002 (AC-077)
public sealed class DocumentationContractTests
{
    private static readonly string[] PublicDocumentPaths =
    [
        "README.md",
        "docs/README.md",
        "docs/getting-started.md",
        "docs/settings.md",
        "docs/features.md",
        "docs/custom-evaluator-guide.md",
        "docs/technical-guid.md",
        "docs/prompt-launch.md",
        "docs/privacy-and-data-handling.md",
        "docs/troubleshooting.md",
        "docs/third-party-notices.md",
        "docs/result-excel-description.md",
        "images/README.md",
    ];

    [Fact]
    public void Getting_started_explains_the_question_details_screen_for_teachers()
    {
        string document = Read("docs/getting-started.md");
        int design = document.IndexOf("## 2. 採点設計", StringComparison.Ordinal);
        int execution = document.IndexOf("## 3. 実行", StringComparison.Ordinal);
        int section = document.IndexOf("### 「設問の詳細」を押した後の画面", StringComparison.Ordinal);
        Assert.True(design >= 0 && design < section && section < execution);
        Assert.Single(Regex.Matches(document, "### 「設問の詳細」を押した後の画面"));

        string body = document[section..execution];
        Assert.Single(Regex.Matches(body, @"!\[[^\]]+\]\(\.\./images/02-input-mapping\.png\)"));
        Assert.True(File.Exists(Path.Combine(FindRepositoryRoot(), "images", "02-input-mapping.png")));
        Assert.Equal(8, Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "images"), "*.png").Count());
        AssertContainsAll(
            body,
            "設問", "追加", "複製", "上へ", "下へ", "削除", "設問名", "設問文", "補助列", "補助に含める",
            "列候補の詳細", "候補一式を再適用", "設定から戻る", "設定を保存",
            "**基本の使い方**", "**注意してください**",
            "手で直した設定や設問文は置き換わる",
            "配点は自動では調整されません",
            "「1. 入力」で変更します",
            "保存もされず、編集内容の破棄もされません");
    }

    [Fact]
    public void Public_documents_exist_are_nonempty_and_have_no_broken_local_links()
    {
        string root = FindRepositoryRoot();
        int linkCount = 0;
        foreach (string relativePath in PublicDocumentPaths)
        {
            string path = Resolve(root, relativePath);
            Assert.True(File.Exists(path), $"Missing public document: {relativePath}");
            string content = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(content));

            foreach (Match match in Regex.Matches(
                         content,
                         @"!?\[[^\]]*\]\((?<target>[^)]+)\)",
                         RegexOptions.CultureInvariant))
            {
                string target = match.Groups["target"].Value.Trim().Trim('<', '>');
                if (target.StartsWith('#')
                    || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                linkCount++;
                target = target.Split('#', 2)[0].Split('?', 2)[0];
                target = Uri.UnescapeDataString(target);
                string resolved = Path.GetFullPath(
                    target.Replace('/', Path.DirectorySeparatorChar),
                    Path.GetDirectoryName(path)!);
                Assert.True(
                    IsWithinRoot(root, resolved),
                    $"Public link escapes the repository: {relativePath} -> {target}");
                Assert.True(
                    File.Exists(resolved) || Directory.Exists(resolved),
                    $"Broken public link: {relativePath} -> {target}");
            }
        }

        Assert.True(linkCount >= 25, $"Expected at least 25 local links, found {linkCount}.");
    }

    [Fact]
    public void Public_document_inventory_and_settings_notice_links_are_explicit()
    {
        Assert.Equal(13, PublicDocumentPaths.Length);
        Assert.Equal(13, PublicDocumentPaths.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(new[]
        {
            "README.md", "docs/README.md", "docs/getting-started.md", "docs/settings.md",
            "docs/features.md", "docs/custom-evaluator-guide.md", "docs/technical-guid.md",
            "docs/prompt-launch.md", "docs/privacy-and-data-handling.md",
            "docs/troubleshooting.md", "docs/third-party-notices.md",
            "docs/result-excel-description.md", "images/README.md",
        }, PublicDocumentPaths);

        // Fixed entry points, not expectations collected from the links under test.
        // Both new documents also participate in the shared file and anchor checks.
        // The docs index reaches the notice through the product README, not a direct link.
        foreach ((string document, string target) in new[]
                 {
                     ("README.md", "docs/settings.md"),
                     ("README.md", "docs/third-party-notices.md"),
                     ("README.md", "docs/technical-guid.md"),
                     ("README.md", "docs/result-excel-description.md"),
                     ("docs/README.md", "../README.md"),
                     ("docs/README.md", "settings.md"),
                     ("docs/README.md", "technical-guid.md"),
                     ("docs/README.md", "result-excel-description.md"),
                     ("docs/getting-started.md", "settings.md"),
                     ("docs/settings.md", "../images/08-settings.png"),
                 })
        {
            string[] targets = Regex.Matches(
                    Read(document),
                    @"!?\[[^\]]*\]\((?<target>[^)]+)\)",
                    RegexOptions.CultureInvariant)
                .Select(match => match.Groups["target"].Value)
                .ToArray();
            Assert.Contains(target, targets);
        }

        string notice = Read("docs/third-party-notices.md");
        AssertContainsAll(
            notice,
            "Fluent System Icons 6点だけを対象",
            "WorkflowIcons.axaml",
            "5bae3fb7771054c252a54b1d9210e9c03439fa1b",
            "Copyright (c) 2020 Microsoft Corporation",
            "[LICENSE原本](",
            "[NOTICE原本](",
            "同梱CLIが本書のMIT Licenseで許諾されるという意味ではありません",
            "現在の公開版への収録や配布物の更新を示すものではありません");
        Assert.Equal(
            new[] { "WorkflowInputIcon", "WorkflowDesignIcon", "WorkflowExecutionIcon",
                "WorkflowResultsIcon", "WorkflowSettingsIcon", "WorkflowSaveIcon" },
            Regex.Matches(notice, @"^\| `(?<key>[^`]+)` \|", RegexOptions.Multiline | RegexOptions.CultureInvariant)
                .Select(match => match.Groups["key"].Value));
    }

    [Fact]
    public void Developer_documents_and_product_version_management_are_current()
    {
        string root = FindRepositoryRoot();
        string developerRoot = Resolve(root, "dev/docs");
        Assert.True(Directory.Exists(developerRoot));
        Assert.False(Directory.Exists(Resolve(root, "docs" + "-dev")));

        string[] requiredPaths =
        [
            "CHANGELOG.md",
            "dev/README.md",
            "dev/version.ps1",
            "dev/version.tests.ps1",
            "dev/docs/README.md",
            "dev/docs/version-management.md",
            "dev/docs/ui-layout-contract.md",
            "dev/docs/adr/0014-product-versioning.md",
            "dev/docs/adr/0016-windows-one-action-startup.md",
            "dev/docs/preflight/windows-singlefile-feasibility.md",
            "src/StudyReportEvaluator.App/Properties/PublishProfiles/WindowsSingleFile.pubxml",
            "scripts/package-windows-singlefile.ps1",
            "scripts/test-windows-singlefile.ps1",
        ];
        foreach (string relativePath in requiredPaths)
        {
            string path = Resolve(root, relativePath);
            Assert.True(File.Exists(path), $"Missing developer version-management artifact: {relativePath}");
            Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(path)));
        }

        string[] developerDocuments = Directory.GetFiles(
            developerRoot,
            "*.md",
            SearchOption.AllDirectories);
        Assert.True(
            developerDocuments.Length >= 32,
            $"Expected at least 32 developer documents after migration, found {developerDocuments.Length}.");
        int linkCount = 0;
        foreach (string path in developerDocuments)
        {
            string content = File.ReadAllText(path);
            Assert.DoesNotContain("docs" + "-dev", content, StringComparison.Ordinal);
            foreach (Match match in Regex.Matches(
                         content,
                         @"!?\[[^\]]*\]\((?<target>[^)]+)\)",
                         RegexOptions.CultureInvariant))
            {
                string target = match.Groups["target"].Value.Trim().Trim('<', '>');
                if (target.StartsWith('#')
                    || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                linkCount++;
                target = Uri.UnescapeDataString(target.Split('#', 2)[0].Split('?', 2)[0]);
                string resolved = Path.GetFullPath(
                    target.Replace('/', Path.DirectorySeparatorChar),
                    Path.GetDirectoryName(path)!);
                Assert.True(
                    IsWithinRoot(root, resolved),
                    $"Developer document link escapes the repository: {Path.GetRelativePath(root, path)} -> {target}");
                if (IsArchivedLinkToRetiredFile(root, path, resolved))
                {
                    continue;
                }

                Assert.True(
                    File.Exists(resolved) || Directory.Exists(resolved),
                    $"Broken developer document link: {Path.GetRelativePath(root, path)} -> {target}");
            }
        }

        Assert.True(linkCount >= 100, $"Expected at least 100 developer-document local links, found {linkCount}.");
        AssertContainsAll(
            Read("dev/docs/README.md"),
            "[アプリケーション版管理手順](version-management.md)",
            "[ADR-0014](adr/0014-product-versioning.md)",
            "[ADR-0016](adr/0016-windows-one-action-startup.md)",
            "[Windows単一EXEの方式適合](preflight/windows-singlefile-feasibility.md)",
            "`PASS_DEVELOPMENT`",
            "CH-01〜06");
        AssertContainsAll(
            Read("dev/README.md"),
            "[Windows単一EXEの設計決定](docs/adr/0016-windows-one-action-startup.md)",
            "[`WindowsSingleFile.pubxml`](../src/StudyReportEvaluator.App/Properties/PublishProfiles/WindowsSingleFile.pubxml)",
            "[`package-windows-singlefile.ps1`](../scripts/package-windows-singlefile.ps1)",
            "[`test-windows-singlefile.ps1`](../scripts/test-windows-singlefile.ps1)");
        AssertContainsAll(
            Read("dev/version.ps1"),
            "#Requires -Version 7.0",
            "#Requires -PSEdition Core",
            "'show', 'set', 'bump', 'verify'",
            "Directory.Build.props must contain exactly one VersionPrefix and one VersionSuffix element");
        Assert.Contains("## [Unreleased]", Read("CHANGELOG.md"), StringComparison.Ordinal);

        string buildProperties = Read("Directory.Build.props");
        Match prefix = Regex.Match(
            buildProperties,
            @"<VersionPrefix>(?<value>[^<]+)</VersionPrefix>",
            RegexOptions.CultureInvariant);
        Match suffix = Regex.Match(
            buildProperties,
            @"<VersionSuffix>(?<value>[^<]*)</VersionSuffix>",
            RegexOptions.CultureInvariant);
        Assert.True(prefix.Success);
        Assert.True(suffix.Success);
        string productVersion = prefix.Groups["value"].Value;
        if (suffix.Groups["value"].Value.Length > 0)
        {
            productVersion += "-" + suffix.Groups["value"].Value;
        }

        Assert.Matches(
            new Regex(
                @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
                RegexOptions.CultureInvariant),
            productVersion);
    }

    [Fact]
    public void Root_readme_contains_no_development_progress_or_placeholder_text()
    {
        string readme = Read("README.md");
        string[] forbidden =
        [
            "GATE-ACCEPTANCE",
            "IMPL-GAP-",
            "IMPLEMENTATION_IN_PROGRESS",
            "U-03",
            "U-04",
            "HEAD ",
            "dev/docs/",
            "traceability",
            "sourceからbuild",
            "dotnet test",
            "TODO",
            "TBD",
            "example.com",
            "<URL>",
            "公開後にこの節へ追加",
        ];
        AssertDoesNotContainAny(readme, forbidden);
        Assert.DoesNotMatch(
            new Regex(@"\b[0-9a-f]{7,40}\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            readme);
        Assert.DoesNotMatch(
            new Regex(@"\b\d+\s*/\s*\d+\s*(?:PASS|成功|passed)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            readme);
    }

    [Fact]
    public void Current_documents_link_to_existing_local_markdown_headings()
    {
        string root = FindRepositoryRoot();
        IEnumerable<string> paths = PublicDocumentPaths.Select(path => Resolve(root, path))
            .Concat(Directory.EnumerateFiles(Resolve(root, "dev/docs"), "*.md"))
            .Distinct();
        List<string> broken = [];
        foreach (string path in paths)
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(path), @"!?\[[^\]]*\]\((?<target>[^)]+)\)"))
            {
                string target = match.Groups["target"].Value.Trim().Trim('<', '>');
                if (Regex.IsMatch(target, @"^[a-z]+:", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                string[] parts = target.Split('#', 2);
                if (parts.Length != 2 || parts[1].Length == 0)
                {
                    continue;
                }

                string destination = parts[0].Length == 0 ? path : Path.GetFullPath(
                    Uri.UnescapeDataString(parts[0]), Path.GetDirectoryName(path)!);
                if (Path.GetExtension(destination).Equals(".md", StringComparison.OrdinalIgnoreCase)
                    && (!File.Exists(destination) || !MarkdownAnchors(File.ReadAllText(destination))
                        .Contains(Uri.UnescapeDataString(parts[1]))))
                {
                    broken.Add($"{Path.GetRelativePath(root, path)} -> {target}");
                }
            }
        }

        Assert.True(broken.Count == 0, string.Join(Environment.NewLine, broken));
    }

    [Fact]
    public void Heading_anchor_check_ignores_code_and_detects_missing_targets()
    {
        HashSet<string> anchors = MarkdownAnchors("## Evidence integrity and storage\n```text\n## Not a heading\n```\n### 主なvalidation\n");
        Assert.Contains("evidence-integrity-and-storage", anchors);
        Assert.Contains("主なvalidation", anchors);
        Assert.DoesNotContain("not-a-heading", anchors);
        Assert.DoesNotContain("missing", anchors);
    }

    [Fact]
    public void Ethics_warning_is_exact_in_readme_and_user_guides()
    {
        foreach (string path in new[]
                 {
                     "README.md",
                     "docs/README.md",
                     "docs/getting-started.md",
                     "docs/custom-evaluator-guide.md",
                     "docs/privacy-and-data-handling.md",
                     "docs/result-excel-description.md",
                 })
        {
            Assert.Contains(EthicsWarningText.Message, Read(path), StringComparison.Ordinal);
        }

        Assert.Single(
            typeof(EthicsWarningText).GetFields(BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void Platform_and_published_package_claims_match_the_windows_delivery_contract()
    {
        string readme = Read("README.md");
        string userIndex = Read("docs/README.md");
        string publishScript = Read("scripts/publish-windows.ps1");
        string packageScript = Read("scripts/package-windows.ps1");

        AssertContainsAll(
            readme,
            "Windows 11 x64",
            ".NET 10 self-contained",
            "公開`v0.8.6`はunsigned単一EXEを主導線とし、unsigned ZIPも提供",
            "StudyReportEvaluator-win-x64.zip",
            "StudyReportEvaluator-win-x64.zip.sha256",
            "StudyReportEvaluator-win-x64.exe",
            "StudyReportEvaluator-win-x64.exe.sha256",
            "現在入手できる公開版は、前版の`v0.8.6`です",
            "本アプリの製品版は`0.9.0`です",
            "このリポジトリのGitHub Releasesから配布しますが、**まだ公開していません**",
            "https://github.com/dahatake/StudyReport-Evaluator/releases/tag/v0.8.6",
            "fresh Windows 11 x64でのclean-host試験CH-01〜06と本人loginを実施しないまま公開",
            "macOS、Linux、Windows Arm64は初版対応対象外",
            "installer、code signing、notarizationを提供しません",
            RetiredPackagingNote);

        AssertContainsAll(
            readme,
            "https://github.com/dahatake/StudyReport-Evaluator/releases/download/v0.8.6/StudyReportEvaluator-win-x64.exe",
            "https://github.com/dahatake/StudyReport-Evaluator/releases/download/v0.8.6/StudyReportEvaluator-win-x64.exe.sha256",
            "https://github.com/dahatake/StudyReport-Evaluator/releases/download/v0.8.6/StudyReportEvaluator-win-x64.zip",
            "https://github.com/dahatake/StudyReport-Evaluator/releases/download/v0.8.6/StudyReportEvaluator-win-x64.zip.sha256");
        AssertContainsAll(
            userIndex,
            "Windows 11 x64",
            "公開EXE／ZIPともunsigned",
            "現在入手できる公開版は、前版の`v0.8.6`です",
            "本アプリの製品版は`0.9.0`です",
            "`v0.8.6`の`StudyReportEvaluator-win-x64.exe`",
            "CH-01〜06 `NOT_RUN`は維持します",
            RetiredPackagingNote,
            "macOS、Linux、Windows Arm64");
        AssertContainsAll(
            Read("docs/getting-started.md"),
            "現在入手できる公開版は、前版の`v0.8.6`です",
            "本アプリの製品版は`0.9.0`です",
            "https://github.com/dahatake/StudyReport-Evaluator/releases/tag/v0.8.6",
            "clean-host試験CH-01〜06と本人loginを実施しないまま公開",
            RetiredPackagingNote);
        AssertContainsAll(
            publishScript,
            "$RuntimeIdentifier = 'win-x64'",
            "--self-contained",
            "[switch] $SingleFile",
            "Assert-BundledCopilotRuntime");
        AssertContainsAll(
            packageScript,
            "Signing: UNSIGNED",
            "GitHub Copilot CLI runtime: BUNDLED");

        // The development MSIX and the macOS foundation were retired: user documents must not offer them as current features.
        foreach (string retired in new[] { "development MSIX", "PASS_MECHANISM", "source foundation" })
        {
            Assert.DoesNotContain(retired, readme, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(retired, userIndex, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(retired, Read("docs/getting-started.md"), StringComparison.OrdinalIgnoreCase);
        }
    }

    // The development MSIX and the macOS foundation were retired on 2026-10-06 and their files deleted. The archive is an
    // immutable historical record, so only archived links to exactly these files are exempt.
    private static readonly string[] RetiredFiles =
    [
        "scripts/acquire-copilot-cli-macos.sh",
        "scripts/notarize-package-macos.sh",
        "scripts/package-macos.sh",
        "scripts/package-windows-msix.ps1",
        "scripts/publish-macos.sh",
        "scripts/sign-macos.sh",
        "scripts/test-windows-msix-unsigned.ps1",
        "tests/StudyReportEvaluator.App.Tests/Packaging/MacOsPublishPackageTests.cs",
    ];

    private static bool IsArchivedLinkToRetiredFile(string root, string documentPath, string resolvedTarget)
    {
        string archiveRoot = Resolve(root, "dev/docs/archive");
        if (!IsWithinRoot(archiveRoot, documentPath))
        {
            return false;
        }

        foreach (string retired in RetiredFiles)
        {
            string retiredPath = Path.GetFullPath(Resolve(root, retired));
            if (string.Equals(retiredPath, resolvedTarget, StringComparison.OrdinalIgnoreCase))
            {
                Assert.False(File.Exists(retiredPath), $"Retired file must stay deleted: {retired}");
                return true;
            }
        }

        return false;
    }

    private const string RetiredPackagingNote =
        "開発用 MSIX と macOS の基盤は 2026-10-06 に廃止した";

    [Fact]
    public void Bundled_cli_documentation_matches_the_default_resolver_and_never_advises_path_fallback()
    {
        string publicContent = string.Join(Environment.NewLine, PublicDocumentPaths.Select(Read));
        AssertContainsAll(
            publicContent,
            "配布物へ同梱したGitHub Copilot CLI",
            "PATH上の別CLIへfallbackしません",
            "runtimes\\win-x64\\native\\copilot.exe");
        AssertDoesNotContainAny(
            publicContent,
            "Copilot CLIを別途導入",
            "PATHから`copilot.exe`を解決",
            "PATH上のCLIを使います");

        CopilotClientFactory factory = new();
        FieldInfo resolverField = typeof(CopilotClientFactory).GetField(
            "_pathResolver",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Copilot path resolver field not found.");
        Assert.IsType<BundledCopilotCliPathResolver>(resolverField.GetValue(factory));
    }

    [Fact]
    public void Input_format_claims_match_the_closed_classifier_vocabulary()
    {
        string content = Read("README.md") + Environment.NewLine + Read("docs/troubleshooting.md");
        AssertContainsAll(
            content,
            "標準Office Open XML `.xlsx`",
            ".xls",
            ".xlsb",
            "CSV",
            "PDF",
            "MacroEnabledWorkbook",
            "EncryptedOrRightsProtected",
            "UnsafePackage",
            "InvalidRelationship");
        Assert.Equal(FileFormatClassification.StandardXlsx, Enum.GetValues<FileFormatClassification>()[0]);
        Assert.Equal(14, Enum.GetValues<FileFormatClassification>().Length);
    }

    [Fact]
    public void Scoring_output_and_checkpoint_names_match_production_constants()
    {
        string readme = Read("README.md");
        string features = Read("docs/features.md");
        foreach (string name in new[]
                 {
                     AppOwnedSheetNameResolver.ConfigBaseName,
                     AppOwnedSheetNameResolver.ReferencesBaseName,
                     AppOwnedSheetNameResolver.ResultsBaseName,
                     AppOwnedSheetNameResolver.RunBaseName,
                     CheckpointStore.CheckpointSheetName,
                 })
        {
            Assert.Contains(name, readme, StringComparison.Ordinal);
        }

        AssertContainsAll(
            readme,
            "BasePoints + SpecialPoints + \\sum_{q=1}^{N}QuestionPoints_q = 100",
            "QuestionEarned_q=QuestionPoints_q\\times QuestionRate_q",
            "SimilarityPenalty_q=QuestionPoints_q\\times Similarity_q\\times SimilarityPenaltyWeight",
            "eval-yyyyMMdd-HHmm[-NN].xlsx",
            "eval-yyyyMMdd-HHmm[-NN].partial.xlsx",
            "主回答が空と確定した完了行: Normal／SimilarityのAI callなし、Question earnedとSimilarityは0相当",
            "非空回答の技術的AI失敗: 対象値はblank",
            "取消・未処理／未確定: 未確定値はblankで、0点とはしない",
            "必要な値がblank: Final raw / Final scoreもblank");
        AssertContainsAll(
            features,
            "各Reference完了後",
            "1 student row",
            "Final rawは監査用",
            "Final scoreを0〜100");
    }

    [Fact]
    public void Launch_documentation_matches_parser_utf8_and_no_auto_run_contract()
    {
        string readme = Read("README.md");
        string guide = Read("docs/prompt-launch.md");
        AssertContainsAll(
            readme,
            "公開`v0.8.6`の`StudyReportEvaluator-win-x64.exe`と、同じ`v0.8.6` ZIPの`StudyReportEvaluator.App.exe`",
            "[Promptファイルから起動](docs/prompt-launch.md)",
            "`--input`は0または1回",
            "`--prompt`は0回以上",
            "strict UTF-8",
            "起動引数やPrompt適用ではloginもAI処理も自動開始しません",
            "AI処理はExecution画面の明示操作まで開始しません");
        AssertContainsAll(
            guide,
            "StudyReportEvaluator.App.exe --input \"<xlsx-path>\" --prompt \"<txt-path>\" [--prompt \"<txt-path>\" ...]",
            "StudyReportEvaluator-win-x64.exe --input \"<xlsx-path>\" --prompt \"<txt-path>\" [--prompt \"<txt-path>\" ...]",
            "BOMあり／なしを受理",
            "1〜32,767 UTF-16 code units",
            "`--run`や`--resume`はありません",
            "filenameによる自動割当や、選択だけでの適用は行いません");
        Assert.Equal(32_767, PromptFileLoader.MaximumCharacters);
        Assert.NotNull(LaunchOptions.Parse([]));
    }

    [Fact]
    public void Privacy_documentation_distinguishes_payload_log_and_output_boundaries()
    {
        string readme = Read("README.md");
        string privacy = Read("docs/privacy-and-data-handling.md");
        AssertContainsAll(
            readme,
            "current rowの選択済みprimary／supporting／special sourceだけ",
            "他row、非選択列、workbook pathは通常payloadへ含めません",
            "application logは回答、Prompt、Reference、reason、evidence、credentialを受け取るfree-text surfaceを持ちません",
            "入力と同等以上に機密");
        AssertContainsAll(
            privacy,
            "| Reference | なし |",
            "| Normal | current rowの選択済み主回答・補助列 |",
            "| Special | current rowの選択済み固有評価主値・補助列 |",
            "| Similarity | current rowの主回答 |",
            "shell、filesystem、Web、GitHub write、MCP toolを公開しません",
            "partialは暗号化containerではありません");
    }

    [Fact]
    public void test_settings_contract_plaintext_schema_single_definition_and_explicit_apply()
    {
        string guide = Read("docs/settings.md");
        ApplicationSettings defaults = new();
        Assert.Equal(1, ApplicationSettings.CurrentSchemaVersion);
        Assert.Equal(1, defaults.SchemaVersion);
        Assert.Equal(8, defaults.MaxConcurrency);
        Assert.Null(defaults.PreferredModelId);
        Assert.Null(defaults.OutputDirectoryOverride);
        Assert.Null(defaults.Definition);
        Assert.Null(defaults.CachedModels);

        // Independently fixed schema and meanings, as exercised by SettingsFileStoreTests.
        // Do not infer the allowed fields from ApplicationSettings or from the guide itself.
        (string Field, string Meaning)[] expectedFields =
        [
            ("schemaVersion", "必須の整数`1`"),
            ("preferredModelId", "通常modelの希望ID、または`null`。利用可能と確認した実行状態ではない"),
            ("modelPreferences", "任意のモデル別明示選択の配列。最大4096件、重複ID不可。各項目は`modelId`、`reasoningEffort`（未指定はnull）、`contextTier`（`default`／`long-context`）。省略／nullは未編集。旧設定はそのまま読めます。cache自動保存には未保存の編集を混ぜません"),
            ("maxConcurrency", "1〜16、既定8"),
            ("outputDirectoryOverride", "明示した完全修飾の絶対出力path、または`null`。自動算出`result`は保存しない"),
            ("definition", "任意の採点定義**1件**、または`null`"),
            ("cachedModels", "任意の最後に取得成功したmodel一覧。省略／`null`はキャッシュなし、空配列`[]`も有効"),
        ];
        Match formatSection = Regex.Match(
            guide,
            @"^## 保存場所と形式\r?\n(?<body>[\s\S]*?)(?=^## |\z)",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(formatSection.Success);
        Match[] fields = Regex.Matches(
                formatSection.Groups["body"].Value,
                @"^\| `(?<field>[^`]+)` \| (?<meaning>[^\r\n]+) \|\r?$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Cast<Match>()
            .ToArray();
        Assert.Equal(expectedFields.Select(field => field.Field), fields.Select(field => field.Groups["field"].Value));
        for (int index = 0; index < expectedFields.Length; index++)
        {
            Assert.Equal(expectedFields[index].Meaning, fields[index].Groups["meaning"].Value);
        }

        // SettingsViewModelTests covers missing files, explicit save/reload, dirty edits,
        // and failed/cancelled apply. No store or real user settings are opened here.
        AssertContainsAll(
            guide,
            @"%LOCALAPPDATA%\StudyReportEvaluator\setting.txt",
            "UTF-8 JSON、設定schemaは整数`1`",
            "BOMあり／なしを受け付けます",
            "保存定義は保持するだけで自動適用しません。login・AI評価も開始しません",
            "認証・一覧の自動再確認は前述のキャッシュがある場合だけ",
            "明示保存または認証・一覧取得成功後のキャッシュ自動保存で初めてfileを作ります",
            "設定読込の完了前は、未読の保存定義を消さないため明示保存できません",
            "編集した共通設定・採点定義の永続化には**設定を保存**が必要",
            "保存済み設定を読み直して**`cachedModels`だけを置き換え**",
            "未保存の希望model・並列度・出力先・採点定義は保存しません",
            "`cachedModels`の各要素は`id`、`maximumPromptTokens`、`maximumContextWindowTokens`を持つobject",
            "各上限値は正の整数または`null`（未取得）で、一覧は最大4096件",
            "保存時にキャッシュが`null`なら項目を省略します",
            "**schemaは`1`のまま**で、項目のない旧設定も読み込めます",
            "キャッシュにcredential・account情報・login状態は保存しません",
            "一覧が表示されても、現在の認証確認に成功するまでは評価できません",
            "自動保存は破損・未対応schema・読込不能の設定fileを上書きしません",
            "Excel未読込なら、読込済みの保存定義を消さずに共通値を保存します",
            "戻る・画面遷移・終了では自動保存しない",
            "保存中に再編集した現在draftは未保存のまま残り得る",
            "失敗や置換前の取消では**旧bytesを保持**",
            "tempの後始末は自分が作ったfileだけへのbest effort",
            "読込前からある未保存の共通編集が保存値へ戻り得る",
            "**設定 → 共通 → 保存定義**",
            "**現在の入力に適用**",
            "Excel未読込・保存定義なし・読込／保存／適用中・run中は利用できません",
            "保存定義の質問文行でExcel metadataをread-only再読込",
            "古いheader metadataを流用しません",
            "成功時だけInput metadata・選択値・draftとDesignを更新し、失敗・取消では現在のInput／Designを置き換えず、設定fileも変更しません",
            "保存したID・順序・設問text・Prompt・配点を保持",
            "Imported Promptの一覧・本文・順序は変更・消去せず、未適用Promptを定義へ取り込みません",
            "見出しセルから取り込まれた設問textは、定義を明示保存すると平文で含まれます",
            "貼り付けた学生回答・氏名・秘密情報も、定義に入れば保存され得ます",
            "`setting.txt`は暗号化containerではなく",
            "入力xlsxのpath・bytes、学生の回答行本文",
            "AI結果、reason、evidence、参照回答、結果override",
            "run／checkpoint状態、新規／再開mode、partial指定",
            "認証情報としてのcredential・account情報・login状態、CLI hash等のruntime診断（model一覧のキャッシュとは別）",
            "Imported Promptの取込file一覧・未適用本文・順序",
            "同schemaの未知項目・重複項目等を拒否",
            "その後に有効な設定を明示保存すると元fileを置き換えます",
            "複数profileの管理・切替と保存済みfinal workbookの再importは未対応");
        AssertContainsAll(
            Read("docs/privacy-and-data-handling.md"),
            "**UTF-8 JSONの平文file**",
            "設定schemaは`1`のままで、`cachedModels`のない旧設定も読み込めます",
            "キャッシュはmodel metadataのみで、credential・account情報・login状態を含まず、現在の利用権限の証明でもありません",
            "保存済み設定を読み直して`cachedModels`だけを自動更新します",
            "未保存の希望model・並列度・出力先・採点定義は保存しません",
            "終了で共通設定や採点定義を自動保存しません",
            "認証状態・runtime情報は表示だけで保存しません",
            "認証情報としてのcredential・account情報・login状態、CLI hash等のruntime診断",
            "定義を明示保存すると、主回答列の選択で見出しセルから取り込んだ質問文も平文で保存されます",
            "「回答行を自動収集しない」は「機密な本文が設定に絶対に含まれない」という保証ではありません",
            "**「AI評価なし」は「network通信なし」ではありません。**");
        AssertContainsAll(
            Read("README.md"),
            "任意の採点定義1件",
            "UTF-8 JSON・`schemaVersion`は整数`1`",
            "画面移動・編集・終了では自動保存せず",
            "採点定義は保持するだけで自動適用しません",
            "**設定 → 共通 → 保存定義 → 現在の入力に適用**",
            "**`setting.txt`は暗号化されていない平文です。**",
            "定義の明示保存時に含まれ得ます",
            "**設定を保存**では結果・overrideを保存しません");
    }

    [Fact]
    public void test_settings_contract_model_output_reset_and_run_isolation()
    {
        string guide = Read("docs/settings.md");
        // ExecutionSettingsTests separates desired/effective values, explicit null reset,
        // and immutable current requests. These assertions keep that distinction public.
        AssertContainsAll(
            guide,
            "保存するのは**希望ID**で、認証済み・利用可能という判定ではありません",
            "候補に存在するときだけ実効選択へ反映",
            "不在なら未選択のまま",
            "別modelへfallbackしません",
            "確認失敗だけでは希望IDを消しません",
            "それだけで暗黙の希望IDを保存しません",
            "参照回答・通常評価・固有評価はrunで選んだ同じmodelとreasoning effortを使います",
            "再起動後や別Excelへ変更した後もその絶対pathを保持",
            "指定出力先を空欄にする操作は未指定（`null`）への明示変更",
            "以前の保存指定へ勝手に戻りません",
            "`null`の場合だけ、現在の入力fileに隣接する`result`を都度算出",
            "入力Aから入力Bへ変えればBの隣接先",
            "入力未選択なら**入力後に決定**",
            "自動算出したpathは指定値として保存しません",
            "指定先が利用不可でも別pathへfallbackしません",
            "設定の復元だけでは出力directoryを作りません",
            "checkpoint再開では保存済みの予約pathを使い、この新規run用指定で置き換えません",
            "実行中に編集・保存する設定は**次回用draft**",
            "現在runは開始時のrequest／immutable snapshot、model・並列度・出力条件、予約済みpathを保持",
            "保存定義の一括適用だけはrun中に行えません",
            "Settings表示中に完了しても結果へ強制移動しません",
            "前回結果とoverrideは次回設定から分離",
            "**設定を保存**ではありません");
        AssertContainsAll(
            Read("README.md"),
            "実行画面の**モデル・並列度・実効出力先は読取専用**",
            "**変更 → 設定の共通**",
            "保存希望model・選択値が利用不可なら自動fallbackせず、選び直しを求めます",
            "希望IDの復元は認証済み・利用可能という判定ではありません",
            "空欄（`outputDirectoryOverride: null`）",
            "保存した明示指定は再起動・入力変更後も保持",
            "`null`の場合だけ現在の入力に隣接する`result`を算出し、算出path自体は保存しません",
            "利用できない指定先から別pathへfallbackしません",
            "開始済みrunの入力・採点定義・モデル・並列度・出力条件と予約済みpathは固定",
            "設定表示中に完了しても強制移動せず",
            "前回結果は次回設定から分離され、次回用の編集で自動再評価されません");
    }

    [Fact]
    public void Screenshot_captions_and_manifest_disclose_synthetic_and_fake_state()
    {
        string readme = Read("README.md");
        string manifest = Read("images/README.md");
        AssertContainsAll(
            readme,
            "画像は合成データ・fake結果による説明用",
            "fake runの100行とfake score",
            "実認証・実保存・clean-host動作の証拠ではありません");
        AssertContainsAll(
            manifest,
            "fake authentication boundary",
            "fake row/AI/input-snapshot/checkpoint/path-planner/finalizer/output boundaries",
            "実file作成の証跡ではない",
            "personal/student data・secret: なし",
            "生成日: 2026-09-27（再生成日。初回生成は2026-09-07の親T28",
            "renderer: Avalonia 12.1.1 Headless + Skia",
            "generator source: `tests/StudyReportEvaluator.App.Tests/UI/DocumentationScreenshotTests.cs`",
            "1440 × 1050 pixels、8枚",
            "`NotChecked`（認証未確認）・ログイン未開始",
            "Final score 92.0",
            "Final score 94.0",
            "**未保存候補**で、exportは未実行",
            "`settingsStore: null`を明示し、保存・読込再試行は無効",
            "`setting.txt`の読込・保存は行わない",
            "本番アプリはユーザー用の保存先を解決する",
            "**非opt-inではrepository画像を検査・更新しません**",
            "2回生成の一致や最小サイズtestの成功を、repository画像と現行UIの自動的一致確認へ拡張しません");

        string[] expectedFiles =
        [
            "01-input-workbook.png",
            "02-input-mapping.png",
            "03-design-knowledge.png",
            "04-design-custom-prompt.png",
            "05-execution-auto.png",
            "06-results-review.png",
            "07-output-export.png",
            "08-settings.png",
        ];
        string imageDirectory = Resolve(FindRepositoryRoot(), "images");
        Assert.Equal(expectedFiles, Directory.EnumerateFiles(imageDirectory, "*.png")
            .Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(expectedFiles, Regex.Matches(
                manifest,
            @"^\| \[`[^`]+`\]\((?<file>[^)]+\.png)\) \|",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Select(match => match.Groups["file"].Value));

        // Check saved-file presence/IHDR, not current UI pixels or native rendering.
        // Rendering/repeatability stays with DocumentationScreenshotTests; no generation here.
        foreach (string fileName in expectedFiles)
        {
            string path = Path.Combine(imageDirectory, fileName);
            Assert.True(File.Exists(path), $"Missing documentation screenshot: {fileName}");
            byte[] png = File.ReadAllBytes(path);
            Assert.True(png.Length > 10_000, $"Documentation screenshot is unexpectedly small: {fileName}");
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
            Assert.Equal(new byte[] { 0, 0, 0, 13, 73, 72, 68, 82 }, png[8..16]); // Length 13, IHDR.
            int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            Assert.Equal(1440, width);
            Assert.Equal(1050, height);
            Assert.Contains(fileName, manifest, StringComparison.Ordinal);
        }

        string generator = Read("tests/StudyReportEvaluator.App.Tests/UI/DocumentationScreenshotTests.cs");
        Match generatorFiles = Regex.Match(
            generator,
            @"private static readonly string\[\] ScreenshotFileNames\s*=\s*\[(?<files>[\s\S]*?)\];",
            RegexOptions.CultureInvariant);
        Assert.True(generatorFiles.Success);
        Assert.Equal(expectedFiles, Regex.Matches(generatorFiles.Groups["files"].Value, "\"(?<file>[^\"]+\\.png)\"")
            .Select(match => match.Groups["file"].Value));
        AssertContainsAll(generator, "private const int ScreenshotWidth = 1440;", "private const int ScreenshotHeight = 1050;");
        foreach (string testName in new[]
                 {
                     "Documentation_screenshots_use_only_synthetic_state_and_have_expected_dimensions",
                     "Synthetic_screenshot_renders_are_repeatable",
                     "Minimum_client_screenshots_verify_all_eight_actual_pixel_frames_without_publishing",
                     "Execution_documentation_screenshot_renders_are_repeatable",
                 })
        {
            Assert.Contains($"public async Task {testName}()", generator, StringComparison.Ordinal);
            Assert.Contains($"`{testName}`", manifest, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Architecture_diagrams_are_accessible_self_contained_svg_files()
    {
        string[] expectedFiles =
        [
            "architecture-overview.svg",
            "evaluation-message-flow.svg",
            "technical-architecture.svg",
        ];
        string imageDirectory = Resolve(FindRepositoryRoot(), "images");
        Assert.Equal(expectedFiles, Directory.EnumerateFiles(imageDirectory, "*.svg")
            .Select(Path.GetFileName).Order(StringComparer.Ordinal));

        XNamespace svg = "http://www.w3.org/2000/svg";
        foreach (string fileName in expectedFiles)
        {
            string path = Path.Combine(imageDirectory, fileName);
            XElement root = Assert.IsType<XElement>(
                XDocument.Load(path, LoadOptions.PreserveWhitespace).Root);
            Assert.Equal(svg + "svg", root.Name);
            Assert.False(string.IsNullOrWhiteSpace((string?)root.Attribute("viewBox")));
            Assert.False(string.IsNullOrWhiteSpace(Assert.Single(root.Elements(svg + "title")).Value));
            Assert.False(string.IsNullOrWhiteSpace(Assert.Single(root.Elements(svg + "desc")).Value));
            Assert.DoesNotContain(root.Descendants(), element => element.Name.LocalName is
                "script" or "foreignObject" or "image" or "use");
            Assert.DoesNotContain(root.DescendantsAndSelf().Attributes(), attribute =>
                attribute.Name.LocalName is "href" or "src");
            Assert.Contains($"({fileName})", Read("images/README.md"), StringComparison.Ordinal);
        }

        Assert.Contains(
            "(images/architecture-overview.svg)",
            Read("README.md"),
            StringComparison.Ordinal);
        string guide = Read("docs/technical-guid.md");
        foreach (string fileName in expectedFiles)
        {
            Assert.Contains($"(../images/{fileName})", guide, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Product_0_9_0_is_distinguished_from_the_predecessor_public_0_8_6_and_historical_0_8_1()
    {
        // Literal version expectations are deliberate: synchronized with the version process, not by reading a
        // version back from the documents being checked. This repository's product version is 0.9.0 (AS-010,
        // not yet published); the predecessor's public 0.8.6 is the release users can get today; 0.8.1 is history.
        Assert.Contains("<VersionPrefix>0.9.0</VersionPrefix>", Read("Directory.Build.props"), StringComparison.Ordinal);
        foreach ((string path, string current, string predecessor, string cleanHost) in new[]
                 {
                     ("README.md", "本アプリの製品版は`0.9.0`です", "fresh Windows 11 x64でのclean-host試験CH-01〜06と本人loginを実施しないまま公開",
                         "`0.9.0`のclean-host試験CH-01〜06（追加ソフトのないfresh Windows 11 x64での確認）と本人loginはまだ実施しておらず、公開前に実施します"),
                     ("docs/README.md", "本アプリの製品版は`0.9.0`です", "CH-01〜06 `NOT_RUN`は維持します",
                         "`0.9.0`のclean-host試験CH-01〜06と本人loginは未実施で、公開前に実施します"),
                     ("docs/getting-started.md", "本アプリの製品版は`0.9.0`です", "clean-host試験CH-01〜06と本人loginを実施しないまま公開",
                         "`0.9.0`のclean-host試験CH-01〜06と本人loginはまだ実施しておらず、公開前に実施します"),
                     ("docs/settings.md", "製品版`0.9.0`（未公開。前版の公開`v0.8.6`の後継）", "旧`v0.8.1`はZIP配布で、本頁の設定保存・適用機能はありません",
                         "`0.9.0`のclean-host試験CH-01〜06と本人loginはまだ実施しておらず（公開前に実施します）"),
                     ("docs/features.md", "製品版`0.9.0`（未公開。前版の公開`v0.8.6`の後継）", "旧`v0.8.1`（ZIP）の機能・画面配置とは区別",
                         "`0.9.0`のclean-host試験CH-01〜06と本人loginはまだ実施しておらず、公開前に実施します"),
                     ("docs/custom-evaluator-guide.md", "製品版`0.9.0`（未公開。前版の公開`v0.8.6`の後継）", "旧`v0.8.1`（ZIP）の旧Design画面とは配置が異なります",
                         "`0.9.0`のclean-host試験CH-01〜06と本人loginはまだ実施しておらず、公開前に実施します"),
                     ("docs/prompt-launch.md", "**製品版`0.9.0`（未公開）**", "公開`v0.8.6`（ZIP代替経路）",
                         "`0.9.0`のclean-host試験CH-01〜06と本人loginはまだ実施しておらず、公開前に実施します"),
                     ("docs/privacy-and-data-handling.md", "製品版`0.9.0`（未公開。前版の公開`v0.8.6`の後継）向け", "旧`v0.8.1`はZIP配布",
                         "`0.9.0`の**clean-host試験CH-01〜06と本人loginはまだ実施しておらず`NOT_RUN`**"),
                     ("docs/troubleshooting.md", "製品版`0.9.0`（未公開。前版の公開`v0.8.6`の後継）向け", "旧`v0.8.1`はZIP配布",
                         "`0.9.0`のclean-host試験CH-01〜06と本人loginはまだ実施しておらず、公開前に実施します"),
                     ("docs/result-excel-description.md", "本アプリの製品版`0.9.0`の出力", "前版の公開`v0.8.6`は、clean-host試験CH-01〜06と本人loginを公開前に実施しないまま公開されました",
                         "`0.9.0`のclean-host試験CH-01〜06と本人loginはまだ実施していません"),
                     ("images/README.md", "製品版`0.9.0`（未公開。前版の公開`v0.8.6`の後継）への版更新ではPNGを再生成しておらず", "旧`0.8.1`の画面を示すものではありません",
                         "clean-host試験CH-01〜06や本人loginの証跡でもありません"),
                 })
        {
            string content = Read(path);
            AssertContainsAll(content, current, predecessor, cleanHost);
            Assert.DoesNotContain("UNRELEASED", content, StringComparison.Ordinal);
            AssertDoesNotContainAny(content, "公開`v0.9.0`", "公開`0.9.0`", "`0.9.0`は公開済み", "現在の公開版は`0.9.0`",
                "現在の公開版は`0.8.6`です", "現在の公開版は`v0.8.6`です", "releases/tag/v0.9.0", "releases/download/v0.9.0");
        }

        AssertContainsAll(Read("images/README.md"), "公開`v0.8.6`のUI説明用", "一時directoryへ描画", "2回生成の一致", "finally",
            "生成時の製品版: `0.8.6`", "その時点ではPNGを再生成していません");
        string changelog = Read("CHANGELOG.md");
        AssertContainsAll(changelog, "## [Unreleased]", "## [0.8.6] - 2026-10-01",
            "製品版`0.9.0`の公開前の候補です（未公開）", "前版（`dahatake/StudyReport-Evaluator`）の公開`v0.8.6`の後継");
        // version-management.md §6.6: the dated section is created only by the release commit.
        Assert.DoesNotMatch(new Regex(@"^## \[0\.9\.0\]", RegexOptions.Multiline | RegexOptions.CultureInvariant), changelog);
        AssertContainsAll(
            Read("docs/getting-started.md"),
            "確認のためだけにPowerShell等を導入する必要はありません",
            "certutil",
            "clean-host試験CH-01〜06と本人loginを実施しないまま公開");
    }

    [Fact]
    public void User_documents_explain_the_migration_from_the_predecessor_and_the_shared_settings_file()
    {
        // FR-073: successor of the public v0.8.6, same settings location, and the file is shared when both are used.
        AssertContainsAll(
            Read("README.md"),
            "### 前版（v0.8.6）からの移行と設定ファイルの共有",
            "`0.9.0`は前版（[`dahatake/StudyReport-Evaluator`](https://github.com/dahatake/StudyReport-Evaluator)）の公開`v0.8.6`の後継",
            "設定の保存先`%LOCALAPPDATA%\\StudyReportEvaluator\\setting.txt`、設定の形式（schema `1`）は前版と同じです",
            "**前版と同時に使うと、同じ設定ファイルを共有します。**",
            "満たさない場合は再開を拒否し、checkpointは変更しません");
        AssertContainsAll(
            Read("docs/README.md"),
            "前版で保存した設定をそのまま読み込み、**前版と同時に使うと設定ファイルを共有します**",
            "(../README.md#前版v086からの移行と設定ファイルの共有)");
        AssertContainsAll(
            Read("docs/getting-started.md"),
            "**前版から移行する場合:**",
            "**前版と同時に使うと、同じ設定ファイルを共有します。**",
            "(../README.md#前版v086からの移行と設定ファイルの共有)");
        AssertContainsAll(
            Read("docs/settings.md"),
            "この保存先と形式は前版の公開`v0.8.6`と同じです",
            "**前版と同時に使うと、同じ設定ファイルを共有します。**");
        AssertContainsAll(
            Read("docs/privacy-and-data-handling.md"),
            "**前版と同時に使うと、同じ`setting.txt`を共有します。**");
        AssertContainsAll(
            Read("docs/troubleshooting.md"),
            "前版と同時に使うと同じ`setting.txt`を共有するため");
    }

    [Fact]
    public void Readme_excludes_unsupported_guarantees_and_links_the_mit_license()
    {
        string readme = Read("README.md");
        AssertContainsAll(
            readme,
            "AI品質、教育的妥当性、公平性、法的適合性、組織policy適合性、不正行為を保証・判定しません",
            "未実測の処理時間、token数、費用を保証しません",
            "すべての端末で無警告・無条件に1操作で起動できるとは表示しません",
            "1ファイル配布は「ディスク上も1ファイル」「痕跡なし」ではありません",
            "保存済みfinal workbookのアプリへの再importと、複数definition profileの管理・切替は提供しません",
            "公開`v0.8.6`の採点定義1件の明示保存・適用とは別です",
            "[MIT License](LICENSE)");
        AssertDoesNotContainAny(
            readme,
            "自動採点します",
            "不正検知",
            "公平性を保証",
            "signed installer",
            "macOS対応",
            "設定の永続化は未対応",
            "採点定義の保存は未対応",
            "設定・definitionの一般的な永続化・再import");
        Assert.StartsWith("MIT License", Read("LICENSE"), StringComparison.Ordinal);
    }

    [Fact]
    public void Requirements_claim_ledger_and_system_prompts_are_complete_and_current()
    {
        string root = FindRepositoryRoot();
        string requirements = Read("docs/requirements-definition-draft.md");
        string ledger = Read("dev/docs/readme-claim-ledger.md");
        string sampleProfile = Read("dev/docs/preflight/sample-workbook-profile.md");
        string prompts = Read("tests/SystemTest-prompt.md");
        string realDataSmoke = Read("tests/StudyReportEvaluator.App.Tests/E2E/RealDataSystemSmokeTests.cs");

        Assert.False(
            File.Exists(Resolve(root, "SystemTest-prompt.md")),
            "The consolidated tests/SystemTest-prompt.md must be the only system-test Prompt source.");
        Assert.False(
            File.Exists(Resolve(root, "tests/system-test-prompt.md")),
            "The legacy system-test Prompt must remain consolidated into tests/SystemTest-prompt.md.");
        Assert.False(
            File.Exists(Resolve(root, "tests/e2e-systemtest-prompt.md")),
            "The legacy real-data Prompt must remain consolidated into tests/SystemTest-prompt.md.");

        AssertContainsAll(
            requirements,
            "| 文書版 | 4.6 |",
            "| 基準日 | 2026-09-07 |",
            "ADR-0013",
            "ADR-0016",
            "| 対応環境 | Windows 11 x64。macOS、Linux、Windows Arm64は現版の正式公開対象外 |",
            "StudyReportEvaluator-win-x64.exe",
            "CH-01〜06",
            "`sample/SampleReport.xlsx`",
            "| Bytes | 470,806 |",
            "73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA",
            "| package entries / relationships | 11 / 8 |",
            "2026-09-16 現行sample採用追補",
            "1 worksheet、`A1:J531`、531行／10列、header 1、data 2〜531（530行）",
            "target D/E/F/G/H/I、対象外 A/B/C/J",
            "D/Gは`PrimaryAnswer`、E/Hは`PrimaryAnswer | StudentPromptPrimary`、F/Iは`Supporting`",
            "初期supportingはE→F、H→Iで、他候補は空",
            "旧12列profileは履歴として保持し、現行sampleの期待値だけを本追補で上書きする",
            "既存12列syntheticと10人fixtureの契約は変更しない",
            "bytes・SHA-256・worksheet name hash・package entries／relationships 11／8は履歴測定として保持し、現在sampleとの固定一致を合否条件にしない",
            "実行前後SHA-256／size／last-write time一致は維持する",
            "privacy・opt-in・実データのLive AI送信禁止は緩和しない",
            "同directoryの他fileを列挙、fallback、代用しない",
            "利用者が主回答列を選択した場合",
            "交差セルが空または存在しない場合は質問文を空として扱い",
            "`HEADER_METADATA_MISMATCH`で再読込を要求");
        AssertDoesNotContainAny(
            requirements,
            "sample/realdata.xlsx",
            "469,995",
            "F7C5364449B1026F2725828F47418B8E105D7E50CF4DF0B224FE4EAF134A2E3D");
        AssertContainsAll(
            sampleProfile,
            "| Requirement | `docs/requirements-definition.md` v4.3 |",
            "| Verification date | 2026-09-04 |",
            "| Sample | `sample/SampleReport.xlsx` |",
            "| Bytes | 470,806 |",
            "73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA",
            "| ZIP entries | 11 |",
            "| Package relationships | 8 |",
            "現行sample採用追補（2026-09-16）",
            "| Worksheet / dimension | 1件 / `A1:J531` |",
            "| Rows / columns | 531 / 10 |",
            "| Header / first / last data row | 1 / 2 / 531（data 530行） |",
            "| Initial target / unselected | D/E/F/G/H/I / A/B/C/J |",
            "| D | PrimaryAnswer | なし |",
            "| E | PrimaryAnswer + StudentPromptPrimary | F |",
            "| F | Supporting | なし |",
            "| G | PrimaryAnswer | なし |",
            "| H | PrimaryAnswer + StudentPromptPrimary | I |",
            "| I | Supporting | なし |",
            "旧`A1:L531`／12列・F〜Kの履歴値は変更せず",
            "`historical_sample_identity_gating=false`",
            "`false`でもそれだけでFAIL／BLOCKEDにせず",
            "実行前後SHA-256／size／last-write UTC不変",
            "privacy・opt-in・Live AI送信禁止を維持する",
            "既存12列synthetic／10人fixtureを変更・代用しない",
            "同directoryの他fileを列挙、fallback、代用しない");
        AssertDoesNotContainAny(
            sampleProfile,
            "sample/realdata.xlsx",
            "469,995",
            "F7C5364449B1026F2725828F47418B8E105D7E50CF4DF0B224FE4EAF134A2E3D");
        AssertSequentialTableIds(requirements, "AC-", 53);
        AssertSequentialTableIds(ledger, "C-", 47);
        AssertContainsAll(ledger, "VERIFIED", "BLOCKED", "EXCLUDED");
        AssertContainsAll(
            ledger,
            "`docs/requirements-definition.md` v4.6",
            "`tests/SystemTest-prompt.md` v4.6、ST-UC-01〜29、TR-01〜36",
            "`PASS_DEVELOPMENT`",
            "fresh user本人login／再起動後確認のCH-06は`NOT_RUN`");

        int[] scenarioHeadingIds = Regex.Matches(
                prompts,
                @"^### ST-UC-(\d{2}):",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(Enumerable.Range(1, 29), scenarioHeadingIds);

        Match[] standalonePrompts = Regex.Matches(
                prompts,
                @"^Test ID: ST-UC-(\d{2})\r?\nRequirement: (?<requirement>[^\r\n]+)\r?$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Cast<Match>()
            .ToArray();
        Assert.Equal(
            Enumerable.Range(1, 29),
            standalonePrompts.Select(match => int.Parse(
                match.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture)));

        string[] expectedRequirementMappings =
        [
            "TR-01 / AC-001 / AC-002",
            "TR-02 / AC-003 / docs/requirements-definition.md §4.4",
            "TR-03 / AC-001 / AC-002",
            "TR-04 / TR-05 / AC-004 / AC-005 / AC-006 / AC-008",
            "TR-06 / AC-007 / AC-008",
            "TR-07 / TR-08 / AC-009 / AC-010 / AC-012",
            "TR-09 / TR-10 / AC-006 / AC-011 / AC-012",
            "TR-11 / TR-12 / AC-013",
            "TR-13 / TR-14 / AC-014 / AC-015",
            "TR-15 / AC-012 / AC-016",
            "TR-16 / AC-019",
            "TR-17 / AC-002 / AC-016 / AC-017",
            "TR-18 / AC-018",
            "TR-19 / AC-020",
            "TR-20 / AC-020",
            "TR-21 / TR-22 / AC-021 / AC-022",
            "supplemental deterministic scenario / AC-009〜AC-016 / AC-019",
            "TR-23 / AC-009〜AC-016 / AC-019",
            "real-data technical E2E / AC-009〜AC-016 / AC-019",
            "TR-24A advisory",
            "TR-24B advisory",
            "TR-25 / AC-023 / AC-027",
            "TR-26 / AC-024",
            "TR-27 / AC-025",
            "TR-28 / TR-29 / AC-026 / AC-027 / AC-028",
            "TR-30 / TR-31 / TR-32 / TR-33 / AC-029 / AC-030 / AC-031 / AC-032 / AC-033 / AC-034",
            "TR-34 / AC-035",
            "TR-35 / AC-036",
            "TR-36 / AC-016 / AC-017 / AC-018 / AC-019 / AC-035 / AC-036 / AC-037",
        ];
        Assert.Equal(
            expectedRequirementMappings,
            standalonePrompts.Select(match => match.Groups["requirement"].Value));

        int[] coveredRequirementIds = Regex.Matches(
                string.Join(
                    Environment.NewLine,
                    standalonePrompts.Select(match => match.Groups["requirement"].Value)),
                @"\bTR-(\d{2})(?:A|B)?\b",
                RegexOptions.CultureInvariant)
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .Order()
            .ToArray();
        Assert.Equal(Enumerable.Range(1, 36), coveredRequirementIds);
        AssertContainsAll(
            prompts,
            "| 対象 | StudyReport Evaluator v4.6 |",
            "| 基準日 | 2026-09-07 |",
            "| 要求正本 | `docs/requirements-definition.md` v4.6 |",
            "ST-UC-26",
            "ST-UC-29",
            "CH-01〜CH-06",
            "`sample/SampleReport.xlsx`",
            "73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA",
            "2026-09-16 現行sample採用追補",
            "1 worksheet、`A1:J531`、531行／10列、header 1、data 2〜531",
            "target D/E/F/G/H/I、対象外 A/B/C/J",
            "D/GはPrimaryAnswer、E/HはPrimaryAnswer + StudentPromptPrimary、F/IはSupporting",
            "suggested supportingはE→F・H→Iだけ（他候補は空）",
            "旧12列sample-only期待値を上書きし、12列syntheticと10人fixtureの固定契約・coverageは変更しない",
            "旧bytes・SHA-256・worksheet name hash・先頭128 bytes・11 entries／8 relationships一致は非ゲートとする",
            "旧測定値は履歴として保持し、現在の測定値へ書き換えない",
            "実行前後SHA-256／size／last-write UTC不変も引き続き必須",
            "opt-in、privacy、実データのLive AI送信禁止は変更しない",
            "他fileを列挙、fallback、代用しない",
            "question row 1と2の各fixtureで主回答列を変更すると",
            "Inputの主回答列ComboBoxを操作すると");
        Assert.DoesNotContain("sample/realdata.xlsx", prompts, StringComparison.OrdinalIgnoreCase);
        AssertContainsAll(
            realDataSmoke,
            "requirements = \"docs/requirements-definition.md v4.6\"",
            "system_test_prompt = \"SystemTest-prompt.md v4.6\"");
        AssertContainsAll(
            Read("docs/getting-started.md"),
            "主回答列を選択すると",
            "**設問文（必須）**へ即座に反映",
            "**見出し行を再読込**");
        AssertContainsAll(
            Read("docs/features.md"),
            "設問textへそのまま即座に反映",
            "空または存在しないheader cellには代替文を生成しません",
            "同一Questionの主回答列と補助列に同じ列を重複指定することはできません");
    }

    [Fact]
    public void Ui_settings_baseline_preserves_explicit_boundaries_and_pending_evidence()
    {
        string requirements = Read("docs/requirements-definition-draft.md");
        string contract = Read("dev/docs/ui-layout-contract.md");
        string traceability = Read("dev/docs/traceability.md");
        string ledger = Read("dev/docs/readme-claim-ledger.md");
        string prompts = Read("tests/SystemTest-prompt.md");

        AssertContainsAll(
            requirements,
            "全タスク完了後だけUnreleased追記",
            "T01では製品版・CHANGELOGを変更しない",
            "明示指定はabsolute pathとして再起動・入力Excel変更後も保持する",
            "空欄への明示編集は`null`への変更として扱い",
            "明示指定が`null`の場合だけ",
            "自動算出したresult pathを明示指定として保存しない",
            "指定先が利用不可でも別pathへfallbackせず",
            "設定復元だけではdirectoryを作成しない",
            "UTF-8 JSON、設定schema整数1",
            "任意の採点定義1件だけを保存する",
            "diskへの書込は利用者の明示保存だけ",
            "見出しセルから取り込まれた設問textもsetting.txtに平文で含まれる",
            "失敗・取消では現在の状態と保存fileを変更しない",
            "確認失敗だけで保存希望IDを消さない",
            "実行中も戻る・次回用編集を許可するが現在runを再構成しない",
            "既存checkpointの予約済みfinal／partial pathと再開条件は変更しない",
            "T01時点の未実装・試験NOT_RUNは履歴",
            "T01〜T35はREVIEWED",
            "T35の対象文書試験は4/4成功・敵対的レビュー済み",
            "製品0.8.6は2026-10-01に公開済み",
            "前回公開版は`v0.8.1` ZIP");
        // T35's four historical results are not T36's expanded document/image gate.
        // Check the evidence boundary without requiring T36 to remain pending forever.
        foreach (string content in new[] { requirements, traceability, ledger, prompts })
        {
            AssertContainsAll(content, "T35の対象文書試験は4/4成功", "t35-reviewed.trx", "敵対的レビュー済み", "T36", "別scope");
            AssertDoesNotContainAny(content,
                "T35の文書変更後試験はNOT_RUN",
                "T35の文書変更後試験・独立レビューはNOT_RUN",
                "T35変更後文書試験・native",
                "T35自体の試験・独立レビュー成功を付与しない");
        }
        // Check only CURRENT status rows. T01 dates and unimplemented history must remain historical.
        foreach ((string content, string label) in new[]
                 {
                     (requirements, "状態"),
                     (traceability, "Current status"),
                 })
        {
            Match currentStatus = Regex.Match(
                content,
                $@"^\| {Regex.Escape(label)} \|[^\r\n]+\r?$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            Assert.True(currentStatus.Success, $"Missing current status row: {label}");
            Assert.Contains("VERIFIED_SCOPED", currentStatus.Value, StringComparison.Ordinal);
            AssertDoesNotContainAny(currentStatus.Value, "未実装", "NOT_IMPLEMENTED");
        }

        AssertContainsAll(
            contract,
            "v4.6 / 2026-09-07",
            "MinWidth=\"1024\"",
            "MinHeight=\"720\"",
            "Width=\"1180\"",
            "Height=\"800\"",
            "第5ステップ、modal、drawerではない",
            "「共通」「入力詳細」「通常評価」「固有評価」「読込Prompt」の5つ",
            "Extent <= Viewport",
            "760×600 standalone／200%表示",
            "主操作target最小44 DIP",
            "未実測は`NOT_RUN`",
            "headlessやHTMLモックで代替しない");
        AssertContainsAll(
            traceability,
            "`docs/requirements-definition.md` v4.6 / 2026-09-07",
            "T01時点の予定ownerを実在確認済みの試験ownerへ更新",
            "T35の対象文書試験は4/4成功・敵対的レビュー済み",
            "SettingsFileStore.LoadAsync",
            "SettingsFileStore.SaveAsync",
            "MainWindowSettingsTests",
            "WorkflowStateTests",
            "ResponsiveLayoutTests",
            "CompactWorkflowLayoutTests",
            "SettingsWorkflowSystemTests",
            "540/540",
            "275/275",
            "67/67",
            "74/74",
            "216/216",
            "27と7を74へ再加算しない",
            "合算してfull gateを作らない",
            "Closed resume identity",
            "ST-UC-27",
            "ST-UC-28",
            "ST-UC-29");
        AssertContainsAll(
            prompts,
            "VERIFIED_SCOPED",
            "4合成回答行、6メソッド・7ケース",
            "MainWindowTests.Failed_saved_definition_apply_preserves_divergent_drafts_and_latest_preview",
            "MainWindowTests.Input_replacement_during_reload_publishes_only_coherent_execution_state",
            "SettingsFileStoreTests.Exclusive_handle_refuses_read_and_replace_keeps_old_bytes_and_cleans_only_own_temp",
            "SettingsWorkflowSystemTests",
            "Real_atomic_final_validation_rejects_a_corrupted_cached_score_and_does_not_publish_it",
            "Manual user-visible UI at 200%",
            "overall PASSには両scopeの実測PASSが必要",
            "CHの欠落、`FAIL`、`NOT_RUN`",
            "ADV-01/ADV-02の`NOT_RUN`は許容");

        AssertSequentialTableIds(traceability, "AC-", 53);
        Assert.Equal(
            Enumerable.Range(1, 48),
            Regex.Matches(
                    traceability,
                    @"^\| TR-(\d{2}) \|",
                    RegexOptions.Multiline | RegexOptions.CultureInvariant)
                .Select(match => int.Parse(
                    match.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture)));

        // Independent owner/status expectations, not generated from the documents under test.
        // T36 documents and T38 P06 may cite their measured, scoped results, not full
        // native, fault or publish acceptance. A source check cannot certify its own execution.
        (string Id, string[] Statuses, string Owner)[] expectedCurrentRows =
        [
            ("AC-013", ["VERIFIED_SCOPED"], "ExecutionSettingsTests"),
            ("AC-016", ["VERIFIED_SCOPED"], "ResponsiveLayoutTests"),
            ("AC-017", ["VERIFIED_SCOPED"], "EthicsWarningTests"),
            ("AC-018", ["VERIFIED_SCOPED"], "ImportedPromptSettingsViewTests"),
            ("AC-022", ["NOT_RUN_CURRENT_CANDIDATE", "VERIFIED_SCOPED", "PASS_REQUIRED"], "DocumentationContractTests"),
            ("AC-029", ["NOT_RUN_EXTERNAL_PREREQUISITE"], "CH-01"),
            ("AC-032", ["VERIFIED_SCOPED"], "WindowsSingleFilePackageTests"),
            ("AC-033", ["NOT_RUN_EXTERNAL_PREREQUISITE"], "CH-06"),
            ("AC-034", ["NOT_RUN_EXTERNAL_PREREQUISITE"], "ReleaseMatrixContractTests"),
            ("AC-035", ["VERIFIED_SCOPED"], "SettingsFileStoreTests"),
            ("AC-036", ["VERIFIED_SCOPED"], "SavedDefinitionApplicationTests"),
            ("AC-037", ["VERIFIED_SCOPED"], "WorkflowStateTests"),
            ("TR-17", ["VERIFIED_SCOPED"], "CompactWorkflowLayoutTests"),
            ("TR-18", ["VERIFIED_SCOPED"], "ImportedPromptSettingsViewTests"),
            ("TR-22", ["NOT_RUN_CURRENT_CANDIDATE", "VERIFIED_SCOPED", "PASS_REQUIRED"], "DocumentationContractTests"),
            ("TR-31", ["VERIFIED_SCOPED"], "WindowsSingleFilePackageTests"),
            ("TR-32", ["NOT_RUN_EXTERNAL_PREREQUISITE"], "BundledCopilotLoginServiceTests"),
            ("TR-33", ["NOT_RUN_EXTERNAL_PREREQUISITE"], "ReleaseWorkflowContractTests"),
            ("TR-34", ["VERIFIED_SCOPED"], "SettingsFileStoreTests"),
            ("TR-35", ["VERIFIED_SCOPED"], "SavedDefinitionApplicationTests"),
            ("TR-36", ["VERIFIED_SCOPED"], "SettingsWorkflowSystemTests"),
        ];
        Dictionary<string, string> documentStatuses = new(StringComparer.Ordinal);
        foreach ((string id, string[] statuses, string owner) in expectedCurrentRows)
        {
            Match row = Regex.Match(
                traceability,
                $@"^\| {Regex.Escape(id)} \|[^\r\n]*\| (?<status>[A-Z_]+) \|\r?$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            Assert.True(row.Success, $"Missing current traceability row: {id}.");
            Assert.Contains(row.Groups["status"].Value, statuses);
            Assert.Contains(owner, row.Value, StringComparison.Ordinal);
            if (id is "AC-032" or "TR-31")
            {
                AssertContainsAll(row.Value, "0.8.4", "artifacts/test/ui-settings/t38/native/",
                    "disk-full／directory ACL／抽出中断／EXE・ZIP間checkpoint再開は未実施",
                    "全faultのPASS_REQUIREDではない");
            }
            if (id is "AC-022" or "TR-22")
            {
                AssertContainsAll(row.Value, "T36", "ST-UC-16");
                documentStatuses.Add(id, row.Groups["status"].Value);
            }
        }

        Assert.Equal(2, documentStatuses.Count);
        string documentStatus = documentStatuses["AC-022"];
        Assert.Equal(documentStatus, documentStatuses["TR-22"]);
        if (documentStatus != "NOT_RUN_CURRENT_CANDIDATE")
        {
            // A later measured T36 gate must name its own evidence, not reuse T35's TRX.
            // This validates the reference, not the execution or contents of a local artifact.
            Assert.Matches(@"`[^`\r\n]*t36[^`\r\n]*\.trx`", traceability);
        }

        string[] allowedClaimStatuses = documentStatus == "NOT_RUN_CURRENT_CANDIDATE"
            ? ["BLOCKED"]
            : ["BLOCKED", "VERIFIED"];
        (string Id, string Owner, string Requirement, string Scenario)[] expectedScopedClaims =
        [
            ("C-045", "SettingsFileStore", "AC-035", "TR-34／ST-UC-27"),
            ("C-046", "InputViewModel.ApplySavedDefinitionAsync", "AC-036", "TR-35／ST-UC-28"),
            ("C-047", "MainWindowSettingsTests", "AC-016／017／037", "ST-UC-12／29"),
        ];
        foreach ((string id, string owner, string requirement, string scenario) in expectedScopedClaims)
        {
            Match row = Regex.Match(
                ledger,
                $@"^\| {Regex.Escape(id)} \|[^\r\n]*\| (?<status>BLOCKED|VERIFIED) \|[^\r\n]*\r?$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            Assert.True(row.Success, $"Missing scoped public claim: {id}.");
            Assert.Contains(row.Groups["status"].Value, allowedClaimStatuses);
            AssertContainsAll(row.Value, "実装済み", "VERIFIED_SCOPED", owner, requirement, scenario, "T36");
            AssertDoesNotContainAny(row.Value, "未実装", "未検証", "予定`", "PASS_PRODUCTION");
        }
    }

    [Fact]
    public void Package_script_includes_every_public_document_image_and_license()
    {
        // Independent closed expectation: never derive it from another producer's allowlist.
        // These are source contracts only; no PowerShell, MSBuild or package execution here.
        string[] expected =
        [
            "README.md",
            "LICENSE",
            "docs/README.md",
            "docs/getting-started.md",
            "docs/features.md",
            "docs/custom-evaluator-guide.md",
            "docs/technical-guid.md",
            "docs/prompt-launch.md",
            "docs/privacy-and-data-handling.md",
            "docs/troubleshooting.md",
            "docs/settings.md",
            "docs/third-party-notices.md",
            "docs/result-excel-description.md",
            "images/README.md",
            "images/architecture-overview.svg",
            "images/technical-architecture.svg",
            "images/evaluation-message-flow.svg",
            "images/01-input-workbook.png",
            "images/02-input-mapping.png",
            "images/03-design-knowledge.png",
            "images/04-design-custom-prompt.png",
            "images/05-execution-auto.png",
            "images/06-results-review.png",
            "images/07-output-export.png",
            "images/08-settings.png",
        ];
        Assert.Equal(25, expected.Length);
        Assert.Equal(25, expected.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(13, PublicDocumentPaths.Length);
        Assert.Equal(
            PublicDocumentPaths.Order(StringComparer.Ordinal),
            expected.Where(path => path.EndsWith(".md", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        Assert.Equal(8, expected.Count(path => path.StartsWith("images/", StringComparison.Ordinal)
            && path.EndsWith(".png", StringComparison.Ordinal)));
        Assert.Equal(3, expected.Count(path => path.StartsWith("images/", StringComparison.Ordinal)
            && path.EndsWith(".svg", StringComparison.Ordinal)));

        const RegexOptions options = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        foreach ((string path, string declaration) in new[]
                 {
                     ("scripts/package-windows.ps1", "$documentationRelativePaths = @("),
                 })
        {
            Match list = Assert.Single(Regex.Matches(
                Read(path),
                @"^" + Regex.Escape(declaration) + @"[ \t]*\r?\n(?<paths>[\s\S]*?)^\)[ \t]*\r?$",
                options).Cast<Match>());
            AssertPublicPaths(ParsePublicDocumentationLiterals(list.Groups["paths"].Value));
        }

        // Match the entire helper body, including its return, rather than collecting
        // matching-looking filenames from comments or from an unrelated array.
        Match publishHelper = Assert.Single(Regex.Matches(
            Read("scripts/publish-windows.ps1"),
            @"^function Get-SingleFileDocumentationPaths[ \t]*\{\r?\n"
            + @"(?:[ \t]*#[^\r\n]*\r?\n)*[ \t]*return[ \t]+@\(\r?\n"
            + @"(?<paths>[\s\S]*?)^[ \t]*\)[ \t]*\r?\n\}[ \t]*\r?$",
            options).Cast<Match>());
        AssertPublicPaths(ParsePublicDocumentationLiterals(publishHelper.Groups["paths"].Value));

        // The profile also has an ILLink pin ItemGroup; select the allowlist by any Content child.
        XElement itemGroup = Assert.Single(XDocument.Parse(Read(
            "src/StudyReportEvaluator.App/Properties/PublishProfiles/WindowsSingleFile.pubxml"))
            .Descendants("ItemGroup"), group => group.Elements("Content").Any());
        AssertPublicPaths(itemGroup.Elements().Select(item =>
        {
            Assert.Equal("Content", item.Name.ToString()); // Do not filter away unknown item types.
            string link = Assert.IsType<string>((string?)item.Attribute("Link"));
            Assert.Equal("$(MSBuildProjectDirectory)\\..\\..\\" + link, (string?)item.Attribute("Include"));
            return link;
        }));
        Assert.Contains(
            "Documentation package input must be LICENSE, Markdown, PNG, or SVG and nonempty",
            Read("scripts/package-windows.ps1"), StringComparison.Ordinal);

        // Same-size substitutions must fail too: unknowns, duplicates, case drift,
        // user settings and input/final/partial workbooks cannot hide behind a count.
        foreach (string replacement in new[]
                 {
                     "docs/unlisted.md", "docs/features.md", "DOCS/settings.md",
                     "setting.txt", "input.xlsx", "result/keep.final.xlsx", "result/keep.partial.xlsx",
                 })
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => AssertPublicPaths(
                expected.Select(path => path == "docs/settings.md" ? replacement : path)));
        }
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            ParsePublicDocumentationLiterals("'README.md', $unexpected"));

        void AssertPublicPaths(IEnumerable<string> paths)
        {
            string[] actual = paths.Select(path => path.Replace('\\', '/')).ToArray();
            Assert.Equal(25, actual.Length);
            Assert.Equal(25, actual.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.DoesNotContain(actual, path => string.Equals(
                Path.GetFileName(path), "setting.txt", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(actual, path => path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(
                expected.Order(StringComparer.Ordinal).ToArray(),
                actual.Order(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        }
    }

    private static string[] ParsePublicDocumentationLiterals(string body)
    {
        // Consume the WHOLE literal array; unknown expressions must not be silently skipped.
        Match literals = Regex.Match(
            body,
            @"\A\s*'(?<path>[^'\r\n]+)'(?:\s*,\s*'(?<path>[^'\r\n]+)')*\s*\z",
            RegexOptions.CultureInvariant);
        Assert.True(literals.Success, "Public documentation arrays must contain only comma-separated path literals.");
        return literals.Groups["path"].Captures.Select(capture => capture.Value).ToArray();
    }

    private static void AssertSequentialTableIds(string content, string prefix, int expectedCount)
    {
        int[] actual = Regex.Matches(
                content,
                $@"^\| {Regex.Escape(prefix)}(\d{{3}}) \|",
                RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(Enumerable.Range(1, expectedCount), actual);
    }

    private static void AssertContainsAll(string content, params string[] expectedFragments)
    {
        foreach (string fragment in expectedFragments)
        {
            Assert.Contains(fragment, content, StringComparison.Ordinal);
        }
    }

    private static void AssertDoesNotContainAny(string content, params string[] forbiddenFragments)
    {
        foreach (string fragment in forbiddenFragments)
        {
            Assert.DoesNotContain(fragment, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Resolve(FindRepositoryRoot(), relativePath));

    private static HashSet<string> MarkdownAnchors(string content)
    {
        HashSet<string> anchors = new(StringComparer.Ordinal);
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        bool fenced = false;
        foreach (string line in content.Split('\n'))
        {
            string text = line.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal) || text.StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            Match heading = Regex.Match(text, @"^#{1,6}\s+(?<title>.+?)\s*#*$");
            if (fenced || !heading.Success)
            {
                continue;
            }

            string slug = Regex.Replace(heading.Groups["title"].Value.ToLowerInvariant(), @"[^\p{L}\p{M}\p{N}_\- ]", string.Empty)
                .Replace(' ', '-');
            int count = counts.GetValueOrDefault(slug);
            counts[slug] = count + 1;
            anchors.Add(count == 0 ? slug : $"{slug}-{count}");
        }

        return anchors;
    }

    private static string Resolve(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static bool IsWithinRoot(string root, string path)
    {
        string canonicalRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string canonicalPath = Path.GetFullPath(path);
        return canonicalPath.StartsWith(
            canonicalRoot,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "StudyReportEvaluator.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "The repository root containing StudyReportEvaluator.slnx was not found.");
    }
}
