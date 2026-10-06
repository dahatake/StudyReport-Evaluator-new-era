# カタログ（既存資産の対応表）

本書は、後から来た人や AI エージェントが、多くのファイルを読まずに既存の資産を見つけて再利用するための対応表です。要求の正本は [requirements-definition.md](./requirements-definition.md)、詳細はリンク先のファイルを正とします。

| 項目 | 内容 |
|---|---|
| 最終更新 | 2026-10-06 |
| リポジトリの状態 | 前版（`dahatake/StudyReport-Evaluator` v0.8.6）のコードとテストを土台に取り込み（要求定義書 Q-010、仮実装）、新しい要求を実装した。アプリは `src/StudyReportEvaluator.App`、採点・数式・類似度は `src/StudyReportEvaluator.Core`、テストは `tests/`。テスト欄はテストのクラス名で、各テストのクラスの直前のコメント `// Requirements:` に要求 ID と受入基準 ID がある。全体の検証は `scripts/verify.ps1` |
| 廃止して取り除いた要求 | FR-059、FR-060、FR-068、FR-072（2026-10-06。経緯は要求定義書の変更履歴） |
| 更新の規則 | 実装工程は、機能・API・テーブル・共通部品を実装したときに、同じ変更でこの表を更新する。これから作るものの設計は書かない |

## 1. 機能

決定状態は要求定義書と一致させる（「承認済み」「承認待ち」「保留」「廃止」は要求定義書の決定状態の要約。詳細と日付は要求定義書の各要求を参照）。

| 要求 ID | 題名 | 決定状態 | 実装ファイル | テスト | 使っている共通部品 |
|---|---|---|---|---|---|
| FR-001 | 入力 workbook の選択 | 承認済み | `src/StudyReportEvaluator.App/ViewModels/InputViewModel.cs`、`src/StudyReportEvaluator.App/Views/InputView.axaml` | InputViewTests | InputSnapshotService |
| FR-002 | 対応形式の限定 | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Intake/FileFormatClassifier.cs` | HostileInputAndFailureTests、FileFormatClassifierTests | — |
| FR-003 | worksheet・質問文行・回答範囲の選択 | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Reading/WorkbookMetadataReader.cs`、`src/StudyReportEvaluator.App/ViewModels/InputViewModel.cs` | InputPresentationTests、WorkbookMetadataReaderTests | — |
| FR-004 | 設問と列の対応付け | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Mapping/ColumnMappingSuggester.cs`、`src/StudyReportEvaluator.App/Workbooks/Mapping/ColumnMappingValidator.cs` | ColumnMappingSuggesterTests、ColumnMappingValidatorTests | — |
| FR-005 | 主回答列の選択と設問文の同期 | 承認済み | `src/StudyReportEvaluator.App/ViewModels/InputViewModel.cs`、`src/StudyReportEvaluator.App/Workbooks/Mapping/ColumnMappingValidator.cs` | InputPresentationTests、InputViewTests、ColumnMappingValidatorTests | — |
| FR-006 | 見本 workbook の構造契約 | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Mapping/ColumnMappingSuggester.cs`、`src/StudyReportEvaluator.App/Workbooks/Reading/WorkbookMetadataReader.cs` | SampleWorkbookStructuralTests | — |
| FR-007 | 元本不変 | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Intake/InputSnapshotService.cs` | HostileInputAndFailureTests、InputSnapshotServiceTests | InputSnapshotService |
| FR-008 | 評価定義の基本設定 | 承認済み | `src/StudyReportEvaluator.Core/Domain/QuantificationDefinition.cs`、`src/StudyReportEvaluator.Core/Validation/QuantificationDefinitionValidator.cs` | QuantificationDefinitionTests、QuantificationDefinitionValidatorTests | — |
| FR-009 | 通常設問と通常 evaluator | 承認済み | `src/StudyReportEvaluator.Core/Domain/QuestionDefinition.cs`、`src/StudyReportEvaluator.Core/Domain/EvaluatorDefinition.cs`、`src/StudyReportEvaluator.Core/Domain/CriterionDefinition.cs`、`src/StudyReportEvaluator.Core/Scoring/WeightedScoreCalculator.cs` | QuantificationDefinitionTests、WeightedScoreCalculatorTests | — |
| FR-010 | 設問固有評価項目 | 承認済み | `src/StudyReportEvaluator.Core/Domain/SpecialEvaluationDefinition.cs`、`src/StudyReportEvaluator.Core/Scoring/WeightedScoreCalculator.cs` | WeightedScoreCalculatorTests、AuxiliaryQuantificationResultValidatorTests | — |
| FR-011 | Prompt の placeholder 検証 | 承認済み | `src/StudyReportEvaluator.Core/Prompting/PromptTemplateRenderer.cs`、`src/StudyReportEvaluator.Core/Prompting/BuiltInPromptTemplates.cs` | PromptTemplateRendererTests | — |
| FR-012 | 配点の不変条件 | 承認済み | `src/StudyReportEvaluator.Core/Scoring/ScoringAllocationCalculator.cs`、`src/StudyReportEvaluator.Core/Validation/QuantificationDefinitionValidator.cs` | ScoringAllocationCalculatorTests、QuantificationDefinitionValidatorTests | ScoringAllocationCalculator |
| FR-013 | 初期配点と均等配分 | 承認済み | `src/StudyReportEvaluator.Core/Scoring/ScoringAllocationCalculator.cs`、`src/StudyReportEvaluator.App/ViewModels/QuantificationDesignViewModel.cs` | DesignStateTests、ScoringAllocationCalculatorTests | ScoringAllocationCalculator |
| FR-014 | 評価項目の説明の初期値 | 承認済み | `src/StudyReportEvaluator.Core/Prompting/DefaultCriterionDescriptions.cs` | DefaultCriterionDescriptionTests | — |
| FR-015 | model の列挙と選択 | 承認済み | `src/StudyReportEvaluator.App/Copilot/CopilotAuthenticationService.cs`、`src/StudyReportEvaluator.App/Copilot/ModelOptionPolicy.cs` | CopilotModelEnumerationTests、ModelCatalogTests | — |
| FR-016 | 思考レベルと Context Size | 承認済み | `src/StudyReportEvaluator.App/Copilot/ReasoningEffortPolicy.cs`、`src/StudyReportEvaluator.App/Copilot/ModelOptionPolicy.cs`、`src/StudyReportEvaluator.App/ViewModels/ExecutionViewModel.ModelOptions.cs` | ModelOptionsTests | — |
| FR-017 | AI session の境界 | 承認済み | `src/StudyReportEvaluator.App/Copilot/EphemeralEvaluationRunner.cs`、`src/StudyReportEvaluator.App/Copilot/EvaluationRequestCapacityValidator.cs` | CapabilityBoundaryTests、EvaluationRequestCapacityValidatorTests | — |
| FR-018 | 参照回答の生成 | 承認済み | `src/StudyReportEvaluator.App/Copilot/AuxiliaryEvaluationRunner.cs`、`src/StudyReportEvaluator.App/Copilot/SubmitReferenceAnswerTool.cs`、`src/StudyReportEvaluator.App/Workbooks/Writing/ReferenceAnswersSheetWriter.cs` | AuxiliaryEvaluationRunnerTests、SubmitReferenceAnswerToolTests、ReferenceAnswersSheetWriterTests | — |
| FR-019 | 通常回答の評価 | 承認済み | `src/StudyReportEvaluator.App/Copilot/SubmitQuantificationTool.cs`、`src/StudyReportEvaluator.Core/Validation/QuantificationResultValidator.cs` | SubmitQuantificationToolTests、QuantificationResultValidatorTests | — |
| FR-020 | 固有評価 | 承認済み | `src/StudyReportEvaluator.App/Copilot/SubmitSpecialQuantificationTool.cs`、`src/StudyReportEvaluator.Core/Validation/AuxiliaryQuantificationResultValidator.cs` | SubmitSpecialQuantificationToolTests、AuxiliaryQuantificationResultValidatorTests、QuantificationDefinitionValidatorTests | — |
| FR-021 | 類似度のローカル計算 | 承認済み | `src/StudyReportEvaluator.Core/Similarity/SurfaceTextSimilarityCalculator.cs` | SimilarityPeerAnalysisTests、SurfaceTextSimilarityCalculatorTests | — |
| FR-022 | 再試行と失敗の扱い | 承認済み | `src/StudyReportEvaluator.App/Copilot/RetryAndCleanupCoordinator.cs`、`src/StudyReportEvaluator.App/Workflow/AdaptiveEvaluationConcurrencyLimiter.cs` | EphemeralEvaluationRunnerTests、RetryAndCleanupCoordinatorTests | — |
| FR-023 | 空回答と技術的失敗の区別 | 承認済み | `src/StudyReportEvaluator.Core/Domain/QuantificationResult.cs`、`src/StudyReportEvaluator.App/Workbooks/Writing/ResultsSheetWriter.cs` | ResultsSheetWriterTests、QuantificationResultTests | — |
| FR-024 | Excel 数式による得点計算 | 承認済み | `src/StudyReportEvaluator.Core/Formulas/FormulaSerializer.cs`、`src/StudyReportEvaluator.App/Workbooks/Writing/ResultsSheetWriter.cs` | ResultsSheetWriterTests、FormulaSerializerTests | FormulaCellWriter |
| FR-025 | 数式とリテラルの所有区分 | 承認済み | `src/StudyReportEvaluator.Core/Formulas/FormulaPreflightValidator.cs`、`src/StudyReportEvaluator.App/Workbooks/Writing/FormulaCellWriter.cs` | FormulaCellWriterTests、FormulaPreflightValidatorTests | FormulaCellWriter |
| FR-026 | 出力後の手動変更 | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Writing/ConfigSheetWriter.cs` | ExternalSpreadsheetRecalculationSmokeTests、ConfigAndRunSheetWriterTests、手順書 docs/manual-tests.md | — |
| FR-027 | 出力先と命名 | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Writing/OutputPathPlanner.cs` | OutputPathPlannerTests | — |
| FR-028 | アプリ所有 sheet | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Writing/AppOwnedSheetNameResolver.cs`、`src/StudyReportEvaluator.App/Workbooks/Writing/ConfigSheetWriter.cs`、`src/StudyReportEvaluator.App/Workbooks/Writing/RunSheetWriter.cs` | AppOwnedSheetNameResolverTests、ConfigAndRunSheetWriterTests | UntrustedStringCellWriter、FormulaCellWriter |
| FR-029 | 完成ファイルの原子的な作成 | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Writing/AtomicOutputCommitter.cs`、`src/StudyReportEvaluator.App/Workbooks/Validation/OutputPackageValidator.cs` | OutputPackageValidatorTests、AtomicOutputCommitterFaultTests | AtomicOutputCommitter |
| FR-030 | checkpoint の作成と保存 | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Checkpoint/CheckpointStore.cs`、`src/StudyReportEvaluator.App/Workbooks/Checkpoint/CheckpointPayloadCodec.cs` | CheckpointStoreTests、DurableQuantificationOrchestratorTests | AtomicOutputCommitter |
| FR-031 | 再開条件の検証 | 承認済み | `src/StudyReportEvaluator.App/Workflow/ResumeAdmissionEvaluator.cs` | ResumeAdmissionEvaluatorTests | InputSnapshotService |
| FR-032 | 再開動作と partial の後始末 | 承認済み | `src/StudyReportEvaluator.App/Workflow/DurableEvaluationScheduler.cs`、`src/StudyReportEvaluator.App/Workflow/DurableQuantificationOrchestrator.cs` | DurableEvaluationSchedulerTests、DurableQuantificationOrchestratorTests | — |
| FR-033 | 中断と再開の引継ぎ | 承認済み | `src/StudyReportEvaluator.App/ViewModels/ExecutionViewModel.Resume.cs` | ResumeWorkflowTests | — |
| FR-034 | 4 step の workflow と画面の分担 | 承認済み | `src/StudyReportEvaluator.App/Views/MainWindow.axaml`、`src/StudyReportEvaluator.App/ViewModels/MainWindowViewModel.cs`、`src/StudyReportEvaluator.App/Navigation/WorkflowNavigator.cs` | MainWindowTests、WorkflowStateTests | — |
| FR-035 | 完了表示 | 承認済み | `src/StudyReportEvaluator.App/ViewModels/ResultsOutputViewModel.cs` | ResultsPresentationTests | — |
| FR-036 | 教育上の警告 | 承認済み | `src/StudyReportEvaluator.App/Resources/EthicsWarningText.cs`、`src/StudyReportEvaluator.App/Views/MainWindow.axaml` | EthicsWarningTests | — |
| FR-037 | GitHub login の開始と取消 | 承認済み | `src/StudyReportEvaluator.App/Copilot/BundledCopilotLoginService.cs` | BundledCopilotLoginServiceTests、CopilotLoginCommandTests | ExecutionViewModel（login 操作） |
| FR-038 | 起動時の自動認証確認と login | 承認済み | `src/StudyReportEvaluator.App/App.axaml.cs`、`src/StudyReportEvaluator.App/Copilot/CopilotAuthenticationService.cs` | CopilotAuthenticationServiceTests、CopilotLoginCommandTests | — |
| FR-039 | 設定画面の 5 カテゴリ | 承認済み | `src/StudyReportEvaluator.App/Views/SettingsView.axaml`、`src/StudyReportEvaluator.App/ViewModels/SettingsViewModel.cs` | MainWindowSettingsTests、SettingsViewTests | — |
| FR-040 | 設定ファイルの読込と明示保存 | 承認済み | `src/StudyReportEvaluator.App/Settings/SettingsFileStore.cs`、`src/StudyReportEvaluator.App/Settings/ApplicationSettings.cs` | ApplicationSettingsTests、SettingsFileStoreTests、LayoutPersistenceTests | SettingsFileStore |
| FR-041 | 保存定義の明示適用 | 承認済み | `src/StudyReportEvaluator.App/ViewModels/SettingsViewModel.cs`、`src/StudyReportEvaluator.App/ViewModels/InputViewModel.cs` | SavedDefinitionApplicationTests | — |
| FR-042 | 現在の run・次回の設定・前回の結果の分離 | 承認済み | `src/StudyReportEvaluator.App/ViewModels/MainWindowViewModel.cs`、`src/StudyReportEvaluator.App/ViewModels/ExecutionViewModel.cs` | SettingsWorkflowSystemTests、ExecutionViewTests、MainWindowSettingsTests | — |
| FR-043 | 実行コストとジョブログ | 承認済み | `src/StudyReportEvaluator.App/Usage/JobUsageTracker.cs`、`src/StudyReportEvaluator.App/Logging/JobCostLogger.cs`、`src/StudyReportEvaluator.App/Views/JobCostView.axaml` | JobCostBackendTests、JobCostViewTests、JobUsageTrackerTests | SafeLogger |
| FR-044 | 総実行時間と AI クレジット | 承認済み | `src/StudyReportEvaluator.App/Usage/RunMetricsFormatter.cs` | ResultsRunMetricsTests | — |
| FR-045 | 結果画面の設問文 | 承認済み | `src/StudyReportEvaluator.App/ViewModels/ResultsOutputViewModel.cs` | ResultsQuestionTextTests | — |
| FR-046 | 実行中の速報値 | 承認済み | `src/StudyReportEvaluator.App/Workflow/LivePreview.cs`、`src/StudyReportEvaluator.App/Workflow/LivePreviewTracker.cs`、`src/StudyReportEvaluator.App/ViewModels/LiveQuantificationPreviewViewModel.cs` | LivePreviewViewModelTests、LivePreviewSchedulerTests | — |
| FR-047 | 実行準備部品の入力画面への集約 | 承認済み | `src/StudyReportEvaluator.App/Views/ExecutionPreparationPanel.axaml`、`src/StudyReportEvaluator.App/Views/InputView.axaml` | PreparationOnInputTests | ExecutionViewModel（login 操作） |
| FR-048 | step 状態の表示名「設定済み」 | 承認済み | `src/StudyReportEvaluator.App/ViewModels/MainWindowViewModel.cs` | WorkflowStateTests | — |
| FR-049 | 共通設定の Copilot login 区画 | 承認済み | `src/StudyReportEvaluator.App/Views/SettingsView.axaml` | SettingsViewTests | ExecutionViewModel（login 操作） |
| FR-050 | 結果画面の学生回答と評価内容 | 承認済み | `src/StudyReportEvaluator.App/ViewModels/ResultsAnswerSource.cs`、`src/StudyReportEvaluator.App/ViewModels/ResultsOutputViewModel.cs` | ResultsAnswerReviewTests | InputSnapshotService |
| FR-051 | 通常評価の設問タブ | 承認済み | `src/StudyReportEvaluator.App/Views/EvaluatorSettingsView.axaml` | EvaluatorSettingsViewTests | — |
| FR-052 | 結果の確認と override | 承認済み | `src/StudyReportEvaluator.App/ViewModels/ResultsOutputViewModel.cs`、`src/StudyReportEvaluator.App/Views/ResultsOutputView.axaml` | ResultsOutputViewTests | — |
| FR-053 | 起動引数 | 承認済み | `src/StudyReportEvaluator.App/Launch/LaunchOptions.cs` | LaunchOptionsTests | — |
| FR-054 | 読込 Prompt の明示適用 | 承認済み | `src/StudyReportEvaluator.App/Views/ImportedPromptSettingsView.axaml`、`src/StudyReportEvaluator.App/ViewModels/SettingsViewModel.cs` | ImportedPromptSettingsViewTests | — |
| FR-055 | Windows 単一 EXE の主配布 | 承認済み | `scripts/publish-windows.ps1`、`scripts/package-windows-singlefile.ps1`、`src/StudyReportEvaluator.App/StudyReportEvaluator.App.csproj` | WindowsSingleFileArtifactTests、WindowsSingleFilePackageTests、WindowsSingleFileProfileTests、WindowsSingleFilePublishTests、手順書 docs/manual-tests.md | — |
| FR-056 | ZIP の代替配布 | 承認済み | `scripts/package-windows.ps1`、`scripts/test-windows-zip.ps1` | WindowsPublishPackageTests | — |
| FR-057 | 同梱 CLI の identity 検証 | 承認済み | `src/StudyReportEvaluator.App/Copilot/CopilotClientFactory.cs` | CopilotClientFactoryTests | — |
| FR-058 | 公開判定（公開 matrix と clean-host 試験） | 承認済み | `scripts/build-platform-release-matrix.ps1`、`scripts/validate-platform-release-matrix.ps1`、`.github/workflows/release.yml`、`.github/workflows/publish-release.yml` | ReleaseMatrixBuilderTests、ReleaseMatrixContractTests、ReleaseWorkflowContractTests、手順書 docs/manual-tests.md | — |
| FR-061 | 利用者向け文書 | 承認済み | `docs/README.md`、`docs/getting-started.md`、`docs/troubleshooting.md` | DocumentationContractTests | — |
| FR-062 | 結果 Excel の解説文書 | 承認済み | `docs/result-excel-description.md` | ResultExcelDescriptionTests | — |
| FR-063 | 教員向け「設問の詳細」の手順 | 承認済み | `docs/getting-started.md` | DocumentationContractTests | — |
| FR-064 | 開発者・運用者向け文書と追跡 | 承認済み | `scripts/verify.ps1`、`scripts/verify-management-data.ps1`、`docs/manual-tests.md` | DocumentationContractTests、RequirementTraceabilityTests | — |
| FR-065 | ペルソナの選択と既定の表現 | 承認済み | `src/StudyReportEvaluator.App/Workspace/WorkspaceLayoutService.cs`、`src/StudyReportEvaluator.App/Workspace/WorkspaceDefaults.cs`、`src/StudyReportEvaluator.App/Views/SettingsView.axaml` | LayoutFileStoreTests、LayoutPersistenceTests、PersonaLayoutTests、WorkspaceLayoutModelTests | WorkspaceLayoutService |
| FR-066 | 採点担当教員の結果表現（分布・ヒートマップ・類似度一覧） | 承認済み | `src/StudyReportEvaluator.App/Visualization/ResultChartCalculator.cs`、`src/StudyReportEvaluator.App/Visualization/ResultsChartsViewModel.cs`、`src/StudyReportEvaluator.App/Views/Charts/ResultsChartsView.axaml` | ResultsChartsViewTests、ResultChartCalculatorTests | PanelWorkspace、ChartControls |
| FR-067 | 評価設計担当の配点構成の表現 | 承認済み | `src/StudyReportEvaluator.App/Visualization/AllocationComposition.cs`、`src/StudyReportEvaluator.App/Visualization/AllocationChartViewModel.cs`、`src/StudyReportEvaluator.App/Views/Charts/AllocationChartView.axaml` | AllocationChartViewTests、ResultChartCalculatorTests | PanelWorkspace、ChartControls |
| FR-069 | 表現どうしの連動 | 承認済み | `src/StudyReportEvaluator.App/Visualization/ResultsChartsViewModel.cs`、`src/StudyReportEvaluator.App/ViewModels/ResultsOutputViewModel.cs` | AllocationChartViewTests、ResultsChartsViewTests | — |
| FR-070 | 視覚表現の代替手段 | 承認済み | `src/StudyReportEvaluator.App/Views/Charts/ChartControls.cs`、`src/StudyReportEvaluator.App/Views/Charts/ResultsChartsView.axaml` | AllocationChartViewTests、ResultsChartsViewTests、ResultChartCalculatorTests | ChartControls |
| FR-071 | 表現が成り立つデータの条件と代替 | 承認済み | `src/StudyReportEvaluator.App/Visualization/ResultsChartsViewModel.cs`、`src/StudyReportEvaluator.App/Visualization/AllocationChartViewModel.cs` | AllocationChartViewTests、ResultsChartsViewTests | — |
| FR-073 | 前版からの引継ぎ | 承認済み | `src/StudyReportEvaluator.App/Settings/SettingsFileStore.cs` | PredecessorCompatibilityTests | SettingsFileStore |
| FR-074 | CI 用の合成見本 workbook | 承認済み | `tests/fixtures/synthetic-sample/SyntheticSampleReport.xlsx`、`tests/StudyReportEvaluator.App.Tests/E2E/SyntheticSampleWorkbook.cs` | SampleWorkbookStructuralTests、SyntheticSampleWorkbook | — |
| NFR-SEC-001 | AI へ送るデータの最小化 | 承認済み | `src/StudyReportEvaluator.Core/Prompting/SafeEvaluationPayloadBuilder.cs` | SafeEvaluationPayloadBuilderTests | — |
| NFR-SEC-002 | 本文と credential をログに残さない | 承認済み | `src/StudyReportEvaluator.App/Logging/SafeLogger.cs`、`src/StudyReportEvaluator.App/Logging/JobCostLogger.cs` | SafeLoggerCanaryTests | SafeLogger |
| NFR-SEC-003 | 信頼できない文字列を数式として扱わない | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Writing/UntrustedStringCellWriter.cs` | UntrustedStringCellWriterTests | UntrustedStringCellWriter |
| NFR-SEC-004 | 出力・checkpoint・設定の機密性 | 承認済み | `docs/privacy-and-data-handling.md` | DocumentationContractTests | — |
| NFR-SEC-005 | OS の保護機能を回避しない | 承認済み | `docs/troubleshooting.md` | DocumentationContractTests | — |
| NFR-SEC-006 | ペルソナ表示はアクセス制御ではない | 承認済み | `src/StudyReportEvaluator.App/Visualization/ResultChartCalculator.cs` | ChartPrivacyTests、PersonaLayoutTests | — |
| NFR-PERF-001 | 処理容量 | 承認済み | `src/StudyReportEvaluator.App/Workflow/EvaluationPlanBuilder.cs`、`src/StudyReportEvaluator.App/Workflow/AdaptiveEvaluationConcurrencyLimiter.cs` | InputPresentationTests、EvaluationSchedulerTests | — |
| NFR-PERF-002 | 書込前・送信前の上限検査 | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Writing/WorkbookExecutionPreflight.cs`、`src/StudyReportEvaluator.App/Copilot/EvaluationRequestCapacityValidator.cs` | EvaluationRequestCapacityValidatorTests、WorkbookExecutionPreflightTests | — |
| NFR-PERF-003 | 性能の実測記録 | 承認済み | `docs/manual-tests.md` | 手順書 docs/manual-tests.md | — |
| NFR-OPS-001 | GUI 起動と AI 利用の分離 | 承認済み | `src/StudyReportEvaluator.App/App.axaml.cs`、`src/StudyReportEvaluator.App/Copilot/CopilotAuthenticationService.cs` | WindowsLocalApplicationTests、MainWindowTests | — |
| NFR-OPS-002 | 表計算ソフトを必須にしない | 承認済み | `src/StudyReportEvaluator.App/Workbooks/Writing/CalculationPropertiesWriter.cs` | ConfigAndRunSheetWriterTests | — |
| NFR-UX-001 | 通常表示とページ切替 | 承認済み | `src/StudyReportEvaluator.App/Views/MainWindow.axaml`、`src/StudyReportEvaluator.App/Workspace/PanelWorkspace.cs` | CompactWorkflowLayoutTests、ResponsiveLayoutTests、WorkspaceReflowTests | — |
| NFR-UX-002 | 未実測の保証を表示しない | 承認済み | `docs/README.md`、`docs/technical-guid.md` | DocumentationContractTests | — |
| NFR-UX-003 | パネルの配置変更 | 承認済み | `src/StudyReportEvaluator.App/Workspace/WorkspaceLayout.cs`、`src/StudyReportEvaluator.App/Workspace/PanelWorkspace.cs`、`src/StudyReportEvaluator.App/Workspace/WorkspacePanel.cs` | PanelWorkspaceTests、WorkspaceLayoutModelTests | PanelWorkspace |
| NFR-UX-004 | レイアウトの保存と復元 | 承認済み | `src/StudyReportEvaluator.App/Workspace/LayoutFileStore.cs`、`src/StudyReportEvaluator.App/Workspace/WorkspaceLayoutService.cs` | LayoutFileStoreTests、LayoutPersistenceTests、PersonaLayoutTests | WorkspaceLayoutService |
| NFR-UX-005 | 既定のレイアウトへの復元と作業状態の保持 | 承認済み | `src/StudyReportEvaluator.App/Workspace/WorkspaceLayoutService.cs` | PanelWorkspaceTests | WorkspaceLayoutService |
| NFR-UX-006 | ドラッグ以外の操作手段 | 承認済み | `src/StudyReportEvaluator.App/Workspace/WorkspacePanel.cs` | CommandPaletteTests、PanelWorkspaceTests、WorkspaceLayoutModelTests | PanelWorkspace |
| NFR-UX-007 | コマンド検索とキーボードからの到達 | 承認済み | `src/StudyReportEvaluator.App/Workspace/CommandPaletteEntry.cs`、`src/StudyReportEvaluator.App/Views/MainWindow.axaml` | CommandPaletteTests | — |
| NFR-UX-008 | 狭い画面と高い拡大率での縮退 | 承認済み | `src/StudyReportEvaluator.App/Workspace/WorkspaceLayout.cs`、`src/StudyReportEvaluator.App/Workspace/PanelWorkspace.cs` | WorkspaceLayoutModelTests、WorkspaceReflowTests | PanelWorkspace |
| NFR-UX-009 | ペルソナ別の既定レイアウト | 承認済み | `src/StudyReportEvaluator.App/Workspace/WorkspaceDefaults.cs` | PersonaLayoutTests、WorkspaceLayoutModelTests | WorkspaceLayoutService |
| NFR-UX-010 | OS の表示設定への追従 | 承認済み | `src/StudyReportEvaluator.App/Display/OsDisplaySettings.cs`、`src/StudyReportEvaluator.App/App.axaml.cs` | OsDisplaySettingsTests、手順書 docs/manual-tests.md | — |
| NFR-A11Y-001 | キーボード・読み上げ・操作対象の大きさ | 承認済み | `src/StudyReportEvaluator.App/Styles/Accessibility.axaml` | CommandPaletteTests、OsDisplaySettingsTests、PrimaryJourneyAccessibilityTests、SettingsAccessibilityTests、WorkspaceReflowTests、手順書 docs/manual-tests.md | — |

## 2. API・イベント

このアプリは Web API を公開しない。外部との境界は次のとおり。

| 名前 | 定義ファイル | 関連する要求 ID |
|---|---|---|
| 起動引数 `--input <xlsx-path>`、`--prompt <txt-path>`（複数可） | `src/StudyReportEvaluator.App/Launch/LaunchOptions.cs` | FR-053、FR-054 |
| GitHub Copilot SDK の session と、アプリ所有の構造化結果 tool（通常評価・固有評価・参照回答） | `src/StudyReportEvaluator.App/Copilot/EphemeralEvaluationRunner.cs`、`src/StudyReportEvaluator.App/Copilot/SubmitQuantificationTool.cs`、`src/StudyReportEvaluator.App/Copilot/SubmitSpecialQuantificationTool.cs`、`src/StudyReportEvaluator.App/Copilot/SubmitReferenceAnswerTool.cs`、`src/StudyReportEvaluator.App/Copilot/EvaluationSchemaFactory.cs` | FR-017〜FR-020、NFR-SEC-001 |
| 同梱 Copilot CLI の `login` 子プロセス | `src/StudyReportEvaluator.App/Copilot/BundledCopilotLoginService.cs` | FR-037、FR-038 |
| 同梱 CLI の解決と identity 検証 | `src/StudyReportEvaluator.App/Copilot/CopilotClientFactory.cs` | FR-057 |
| 環境変数 `STUDY_REPORT_EVALUATOR_AUTO_COPILOT_LOGIN`（`0`／`false` で起動時の自動 login を抑止） | `src/StudyReportEvaluator.App/App.axaml.cs` | FR-038 |

## 3. テーブル

データベースはない。永続データはファイルで、正本は次のとおり。

| テーブル名 | 定義ファイル（スキーマやマイグレーション） | 正本のシステム | 関連する要求 ID |
|---|---|---|---|
| 入力 workbook（Forms から export した `.xlsx`。読取専用） | `src/StudyReportEvaluator.App/Workbooks/Reading/WorkbookMetadataReader.cs` | Microsoft Forms／Google Forms と、利用者が export した元本。学生の識別子は Excel の行番号 | FR-001〜FR-007 |
| 出力 workbook の `Quantification_Config` | `src/StudyReportEvaluator.App/Workbooks/Writing/ConfigSheetWriter.cs` | 本アプリ（run の snapshot） | FR-026、FR-028 |
| 出力 workbook の `Quantification_References` | `src/StudyReportEvaluator.App/Workbooks/Writing/ReferenceAnswersSheetWriter.cs` | 本アプリ | FR-018、FR-028 |
| 出力 workbook の `Quantification_Results` | `src/StudyReportEvaluator.App/Workbooks/Writing/ResultsSheetWriter.cs` | 本アプリ（最終評点の正本は Excel 数式） | FR-023〜FR-025、FR-028 |
| 出力 workbook の `Quantification_Run` | `src/StudyReportEvaluator.App/Workbooks/Writing/RunSheetWriter.cs` | 本アプリ | FR-028 |
| checkpoint（`.partial.xlsx` の `Quantification_Checkpoint`） | `src/StudyReportEvaluator.App/Workbooks/Checkpoint/CheckpointStore.cs`、`src/StudyReportEvaluator.App/Workbooks/Checkpoint/CheckpointPayloadCodec.cs` | 本アプリ | FR-030〜FR-032 |
| `setting.txt`（schema 1、LocalApplicationData\StudyReportEvaluator） | `src/StudyReportEvaluator.App/Settings/SettingsFileStore.cs`、`src/StudyReportEvaluator.App/Settings/ApplicationSettings.cs` | 本アプリ（前版 v0.8.6 と共通） | FR-040、FR-073 |
| `layout.json`（schema 1、同じフォルダ） | `src/StudyReportEvaluator.App/Workspace/LayoutFileStore.cs` | 本アプリ | NFR-UX-004、FR-065 |
| job log（`jobs\<job id>.jsonl`、同じフォルダ） | `src/StudyReportEvaluator.App/Logging/JobCostLogger.cs`、`src/StudyReportEvaluator.App/Logging/JobCostLogEntry.cs` | 本アプリ | FR-043 |
| 公開 matrix v2（EXE・ZIP の 2 行） | `eng/schemas/platform-release-matrix-v2.schema.json` | 本アプリの release workflow | FR-058 |

## 4. 共通部品

| 部品名 | ファイル | 用途 | 使っている要求 ID |
|---|---|---|---|
| InputSnapshotService | `src/StudyReportEvaluator.App/Workbooks/Intake/InputSnapshotService.cs` | 入力の SHA-256・size・最終更新時刻の取得と再確認 | FR-001、FR-007、FR-031、FR-050 |
| SafeLogger | `src/StudyReportEvaluator.App/Logging/SafeLogger.cs` | 本文・credential を含めないログ | FR-043、NFR-SEC-002 |
| UntrustedStringCellWriter | `src/StudyReportEvaluator.App/Workbooks/Writing/UntrustedStringCellWriter.cs` | 信頼できない文字列を文字列セルとして書く | FR-028、NFR-SEC-003 |
| FormulaCellWriter | `src/StudyReportEvaluator.App/Workbooks/Writing/FormulaCellWriter.cs` | 数式セルと cached value の書込 | FR-024、FR-025、FR-028 |
| AtomicOutputCommitter | `src/StudyReportEvaluator.App/Workbooks/Writing/AtomicOutputCommitter.cs` | 一時ファイルからの上書きなしの完成 | FR-029、FR-030 |
| ScoringAllocationCalculator | `src/StudyReportEvaluator.Core/Scoring/ScoringAllocationCalculator.cs` | 配点の初期値・均等配分・合計の検証（配点構成の図も同じ値を使う） | FR-012、FR-013、FR-067 |
| ExecutionViewModel（login・再開の操作） | `src/StudyReportEvaluator.App/ViewModels/ExecutionViewModel.cs` | 入力画面・実行画面・共通設定で同じ login と再開の操作を共有する | FR-037、FR-047、FR-049 |
| SettingsFileStore | `src/StudyReportEvaluator.App/Settings/SettingsFileStore.cs` | `setting.txt` の検査付きの読込と原子的な保存 | FR-040、FR-073 |
| PanelWorkspace／WorkspacePanel | `src/StudyReportEvaluator.App/Workspace/PanelWorkspace.cs`、`src/StudyReportEvaluator.App/Workspace/WorkspacePanel.cs` | step 画面のパネルの配置・メニュー・縮退 | NFR-UX-003、NFR-UX-006、NFR-UX-008、FR-066、FR-067 |
| WorkspaceLayoutService | `src/StudyReportEvaluator.App/Workspace/WorkspaceLayoutService.cs` | レイアウトとペルソナの保持・自動保存・既定への復元 | NFR-UX-004、NFR-UX-005、NFR-UX-009、FR-065 |
| ChartControls | `src/StudyReportEvaluator.App/Views/Charts/ChartControls.cs` | 棒・格子・積み上げ棒の描画と、キーボード操作・読み上げ名 | FR-066、FR-067、FR-070 |
| OsDisplaySettings | `src/StudyReportEvaluator.App/Display/OsDisplaySettings.cs` | OS のライト／ダーク・ハイコントラスト・文字サイズ・視覚効果の取得 | NFR-UX-010 |

## 5. 管理データ以外の文書

| ファイル | 用途 | 備考 |
|---|---|---|
| [requirements-definition-draft.md](./requirements-definition-draft.md) | 要求の出典（SRC-001）。前版 `dahatake/StudyReport-Evaluator` の要求定義書 v4.6 と同一内容 | 正本ではない。前版リポジトリ相対のリンクは本リポジトリでは切れている（要求定義書 AF-001） |
| [manual-tests.md](./manual-tests.md) | 手順で確かめる受入基準（clean-host、native のアクセシビリティ、性能の実測など） | 要求 ID で引ける |
