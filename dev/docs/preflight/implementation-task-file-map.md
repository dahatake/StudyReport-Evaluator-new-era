# Implementation task file map — plan v4.0

> [!NOTE]
> **OWNERSHIP SNAPSHOT:** exact path ownershipの記録として有効です。本文の`future`、`pending`、phase開始条件はtask実行前の時間的snapshotであり、現在の実装進捗ではありません。現在状態は[`dev/docs/implementation-status.md`](../implementation-status.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **CURRENT NORMATIVE PATH MAP** |
| Requirement | `docs/requirements-definition.md` v3.0 |
| Plan | `dev/docs/archive/work/20260831-implementation-plan.md` v4.0 |
| Scope decision | ADR-0011 |
| Initial target | Windows 11 x64 |
| Production projects | 2 |
| Test projects | 2 |
| Task/gate rows | 37 |
| Rule | plan shorthandとtestsを本表のexact pathへ展開する |
| 記録日 | 2026-09-01 |

No production or production-test path may be created before the current GATE-0 passes。This map assigns future ownership and does not authorize implementation by itself。

## Root aliases

| Alias | Exact root |
|---|---|
| `Core/` | `src/StudyReportEvaluator.Core/` |
| `App/` | `src/StudyReportEvaluator.App/` |
| `Core.Tests/` | `tests/StudyReportEvaluator.Core.Tests/` |
| `App.Tests/` | `tests/StudyReportEvaluator.App.Tests/` |

No `Application/`、`Infrastructure/`、`Platform/`、`Workbooks/`、`Desktop/`、separate `E2E.Tests/` project alias exists in plan v4.0。

## Phase 0 — Baseline

下表の旧 `work/` パスは当時のexact path ownershipとして保持する。計画書の[現在の保存先](../archive/work/20260831-implementation-plan.md)は移動後の参照用であり、当時の所有パスを変更しない。

| Task | Exact files |
|---|---|
| B-01 | `docs/requirements-definition.md`; `work/20260831-implementation-plan.md` |
| B-02 | `dev/docs/adr/0011-dynamic-quantification-excel-formulas.md`; `dev/docs/preflight/sample-workbook-profile.md` |
| B-03 | `dev/docs/preflight/requirements-baseline.md`; `dev/docs/preflight/implementation-task-file-map.md` |
| B-04 | `dev/docs/traceability.md` |
| GATE-0 | `dev/docs/preflight/gate-result.md` |

## Phase 1 — Foundation

| Task | Exact files |
|---|---|
| F-01 | `global.json`; `Directory.Build.props`; `Directory.Packages.props`; `NuGet.Config`; `.gitignore` |
| F-02 | `StudyReportEvaluator.slnx`; `Core/StudyReportEvaluator.Core.csproj`; `App/StudyReportEvaluator.App.csproj`; `App/Program.cs`; `App/App.axaml`; `App/App.axaml.cs`; `Core.Tests/StudyReportEvaluator.Core.Tests.csproj`; `App.Tests/StudyReportEvaluator.App.Tests.csproj` |
| F-03 | `Core/packages.lock.json`; `App/packages.lock.json`; `Core.Tests/packages.lock.json`; `App.Tests/packages.lock.json`; `Core.Tests/Architecture/DependencyRulesTests.cs`; `App.Tests/SupplyChain/PackageLockTests.cs` |
| GATE-1 | generated `artifacts/test/gate-foundation.json` |

## Phase 2 — Core

| Task | Exact files |
|---|---|
| C-01 | `Core/Domain/QuantificationDefinition.cs`; `Core/Domain/QuestionDefinition.cs`; `Core/Domain/EvaluatorDefinition.cs`; `Core/Domain/CriterionDefinition.cs`; `Core.Tests/Domain/QuantificationDefinitionTests.cs`; `Core.Tests/Domain/DefinitionCollectionTests.cs` |
| C-02 | `Core/Domain/QuantificationSnapshot.cs`; `Core/Validation/QuantificationDefinitionValidator.cs`; `Core/Serialization/CanonicalDefinitionSerializer.cs`; `Core.Tests/Validation/QuantificationDefinitionValidatorTests.cs`; `Core.Tests/Serialization/CanonicalDefinitionSerializerTests.cs`; `Core.Tests/Domain/QuantificationSnapshotTests.cs` |
| C-03 | `Core/Prompting/BuiltInPromptTemplates.cs`; `Core/Prompting/PromptRenderContext.cs`; `Core/Prompting/PromptTemplateRenderer.cs`; `Core/Prompting/SafeEvaluationPayloadBuilder.cs`; `Core.Tests/Prompting/BuiltInPromptTemplatesTests.cs`; `Core.Tests/Prompting/PromptTemplateRendererTests.cs`; `Core.Tests/Prompting/SafeEvaluationPayloadBuilderTests.cs` |
| C-04 | `Core/Domain/QuantificationResult.cs`; `Core/Validation/QuantificationResultValidator.cs`; `Core.Tests/Domain/QuantificationResultTests.cs`; `Core.Tests/Validation/QuantificationResultValidatorTests.cs` |
| C-05 | `Core/Scoring/WeightedScoreCalculator.cs`; `Core/Formulas/FormulaExpression.cs`; `Core/Formulas/FormulaSerializer.cs`; `Core/Formulas/FormulaPreflightValidator.cs`; `Core.Tests/Scoring/WeightedScoreCalculatorTests.cs`; `Core.Tests/Formulas/FormulaSerializerTests.cs`; `Core.Tests/Formulas/FormulaPreflightValidatorTests.cs` |
| C-06 | `tests/fixtures/v3/definition-minimal.json`; `tests/fixtures/v3/definition-matrix.json`; `tests/fixtures/v3/result-oracles.json`; `tests/fixtures/v3/workbook-scenarios.json`; `tests/fixtures/v3/manifest.json`; `Core.Tests/Fixtures/SyntheticSpecificationContractTests.cs` |
| GATE-CORE | generated `artifacts/test/gate-core.json` |

## Phase 3A — Excel adapter

| Task | Exact files |
|---|---|
| X-01 | `App/Workbooks/Intake/FileFormatClassifier.cs`; `App/Workbooks/Intake/InputSnapshotService.cs`; `App/Workbooks/Reading/WorkbookMetadataReader.cs`; `App.Tests/Workbooks/Intake/FileFormatClassifierTests.cs`; `App.Tests/Workbooks/Intake/InputSnapshotServiceTests.cs`; `App.Tests/Workbooks/Reading/WorkbookMetadataReaderTests.cs` |
| X-02 | `App/Workbooks/Mapping/ColumnMappingSuggester.cs`; `App/Workbooks/Mapping/ColumnMappingValidator.cs`; `App.Tests/Workbooks/Mapping/ColumnMappingSuggesterTests.cs`; `App.Tests/Workbooks/Mapping/ColumnMappingValidatorTests.cs` |
| X-03 | `App/Workbooks/Writing/WorkingPackage.cs`; `App/Workbooks/Writing/AppOwnedSheetNameResolver.cs`; `App/Workbooks/Writing/ConfigSheetWriter.cs`; `App/Workbooks/Writing/RunSheetWriter.cs`; `App/Workbooks/Writing/CalculationPropertiesWriter.cs`; `App.Tests/Workbooks/Writing/WorkingPackageTests.cs`; `App.Tests/Workbooks/Writing/AppOwnedSheetNameResolverTests.cs`; `App.Tests/Workbooks/Writing/ConfigAndRunSheetWriterTests.cs` |
| X-04 | `App/Workbooks/Writing/ResultsSheetWriter.cs`; `App/Workbooks/Writing/FormulaCellWriter.cs`; `App/Workbooks/Writing/UntrustedStringCellWriter.cs`; `App.Tests/Workbooks/Writing/ResultsSheetWriterTests.cs`; `App.Tests/Workbooks/Writing/FormulaCellWriterTests.cs`; `App.Tests/Workbooks/Writing/UntrustedStringCellWriterTests.cs` |
| X-05 | `App/Workbooks/Validation/OutputPackageValidator.cs`; `App/Workbooks/Writing/AtomicOutputCommitter.cs`; `App.Tests/Workbooks/Validation/OutputPackageValidatorTests.cs`; `App.Tests/Workbooks/Writing/AtomicOutputCommitterFaultTests.cs` |
| GATE-EXCEL | generated `artifacts/test/gate-excel.json` |

## Phase 3B — Copilot adapter

| Task | Exact files |
|---|---|
| A-01 | `App/Copilot/CopilotClientFactory.cs`; `App/Copilot/CopilotAuthenticationService.cs`; `App.Tests/Copilot/CopilotClientFactoryTests.cs`; `App.Tests/Copilot/CopilotAuthenticationServiceTests.cs` |
| A-02 | `App/Copilot/EvaluationSchemaFactory.cs`; `App/Copilot/SubmitQuantificationTool.cs`; `App.Tests/Copilot/EvaluationSchemaFactoryTests.cs`; `App.Tests/Copilot/SubmitQuantificationToolTests.cs` |
| A-03 | `App/Copilot/EphemeralEvaluationRunner.cs`; `App/Copilot/RetryAndCleanupCoordinator.cs`; `App/Logging/SafeLogger.cs`; `App.Tests/Copilot/EphemeralEvaluationRunnerTests.cs`; `App.Tests/Copilot/RetryAndCleanupCoordinatorTests.cs`; `App.Tests/Copilot/CapabilityBoundaryTests.cs`; `App.Tests/Copilot/AuthenticatedSyntheticSmokeTests.cs`; `App.Tests/Logging/SafeLoggerCanaryTests.cs` |
| GATE-AI | generated `artifacts/test/gate-ai.json` |

## Phase 4 — Workflow and UI

| Task | Exact files |
|---|---|
| U-01 | `App/Workflow/EvaluationPlanBuilder.cs`; `App/Workflow/EvaluationScheduler.cs`; `App/Workflow/QuantificationOrchestrator.cs`; `App/Workflow/RunSummary.cs`; `App.Tests/Workflow/EvaluationPlanBuilderTests.cs`; `App.Tests/Workflow/EvaluationSchedulerTests.cs`; `App.Tests/Workflow/QuantificationOrchestratorTests.cs` |
| U-02 | `App/App.axaml`; `App/App.axaml.cs`; `App/Views/MainWindow.axaml`; `App/Views/MainWindow.axaml.cs`; `App/ViewModels/MainWindowViewModel.cs`; `App/Navigation/WorkflowNavigator.cs`; `App/Composition/ServiceRegistration.cs`; `App/Resources/EthicsWarningText.cs`; `App/Styles/Accessibility.axaml`; `App.Tests/UI/MainWindowTests.cs`; `App.Tests/UI/EthicsWarningTests.cs`; `App.Tests/Composition/ServiceRegistrationTests.cs` |
| U-03 | `App/Views/InputView.axaml`; `App/Views/InputView.axaml.cs`; `App/ViewModels/InputViewModel.cs`; `App/Views/QuantificationDesignView.axaml`; `App/Views/QuantificationDesignView.axaml.cs`; `App/ViewModels/QuantificationDesignViewModel.cs`; `App.Tests/UI/InputViewTests.cs`; `App.Tests/UI/QuantificationDesignViewTests.cs` |
| U-04 | `App/Views/ExecutionView.axaml`; `App/Views/ExecutionView.axaml.cs`; `App/ViewModels/ExecutionViewModel.cs`; `App/Views/ResultsOutputView.axaml`; `App/Views/ResultsOutputView.axaml.cs`; `App/ViewModels/ResultsOutputViewModel.cs`; `App.Tests/UI/ExecutionViewTests.cs`; `App.Tests/UI/ResultsOutputViewTests.cs`; `App.Tests/UI/PrimaryJourneyAccessibilityTests.cs` |
| GATE-APP | generated `artifacts/test/gate-app.json` |

## Phase 5 — Acceptance, package, docs

| Task | Exact files |
|---|---|
| E-01 | `App.Tests/E2E/SyntheticWorkbookFactory.cs`; `App.Tests/E2E/FakeCopilotTransport.cs`; `App.Tests/E2E/SyntheticQuantificationJourneyTests.cs`; generated ignored workbooks under `artifacts/test/fixtures/` |
| E-02 | `App.Tests/E2E/SampleWorkbookStructuralTests.cs`; `App.Tests/E2E/WindowsLocalApplicationTests.cs`; `App.Tests/E2E/HostileInputAndFailureTests.cs`; `artifacts/test/performance-windows-x64.json` |
| P-01 | `scripts/publish-windows.ps1`; `scripts/package-windows.ps1`; `App.Tests/Packaging/WindowsPublishPackageTests.cs`; generated ignored `artifacts/package/StudyReportEvaluator-win-x64.zip`; generated ignored `artifacts/package/StudyReportEvaluator-win-x64.zip.sha256` |
| D-01 | `README.md`; `dev/docs/architecture.md`; `dev/docs/excel-contract.md`; `dev/docs/custom-evaluator-guide.md`; `App.Tests/Content/DocumentationContractTests.cs` |
| E-TR | `dev/docs/traceability.md` |
| GATE-ACCEPTANCE | generated `artifacts/test/gate-acceptance.json` |

## Shared and sequential ownership

| Path | Ordered owners | Rule |
|---|---|---|
| `App/App.axaml`; `App/App.axaml.cs` | F-02 → U-02 | F-02 creates minimal bootstrap; U-02 integrates shell/resources without changing project topology |
| `dev/docs/traceability.md` | B-04 → E-TR | B-04 creates planned mapping; E-TR replaces planned status with final evidence only after task/gate completion |
| `.gitignore` | F-01 | Read and merge existing uncommitted content; never reset or overwrite unrelated lines |
| `README.md` | D-01 | Read and merge existing uncommitted content; never reset or overwrite unrelated sections |

No other production/source path has multiple task owners。A necessary ownership change requires plan/map revision before editing。

## Generated artifact rules

1. Gate JSON、synthetic workbook、performance output、package ZIP/hash under `artifacts/` are generated evidence and remain ignored unless release policy is explicitly changed。
2. Generated evidence never contains sample/student answer、Prompt、reason、evidence、token、credential、file path。
3. Actual input workbooks are not copied into test output or package artifacts。
4. Optional live Copilot smoke uses fixed synthetic text only and records advisory status separately。
5. Optional external spreadsheet recalculation never replaces required formula/cached-value oracle evidence。

## Task execution rules

1. A task edits only its listed files and direct generated evidence。
2. Production implementation starts only after current GATE-0 PASS。
3. Each implementation task adds or updates its listed tests in the same commit。
4. Each task runs diagnostics、target tests、applicable build、`git diff --check` before commit。
5. Gate rows verify all prerequisite tasks and record exact commit/test status; they do not implement missing behavior。
6. `NOT_RUN`、`SKIPPED`、optional advisory PASS are not promoted to required-test PASS。
7. A task does not invent alternate project roots or silently split projects。

## Required boundary allocation

- C-02 `QuantificationDefinitionValidatorTests.cs` owns selected-row boundaries of 1、20,000、20,001, reversed ranges, enabled-child minima, numeric range, and weight validation。
- C-03 `SafeEvaluationPayloadBuilderTests.cs` owns selected same-row primary/supporting inclusion, other-row/nonselected-column/file-path exclusion, stable source IDs, and opaque placeholder insertion。
- C-04 `QuantificationResultValidatorTests.cs` consumes the exact source-ID map created for dispatch, resolves each returned ID to its same-row cell, and rejects evidence found only in a different sent or unsent cell。
- C-05 formula tests own 8,191／8,192-character, function-argument, Excel-column, reference allowlist, and cycle boundaries。
- X-01 tests own file/ZIP/relationship、sheet/dimension/header、cell-length metadata, and exact input snapshot behavior。
- X-04 tests own Scorable、blank AI raw、valid/invalid override、cached value、literal-string injection, and rounded-child formula serialization。
- U-01 `QuantificationOrchestratorTests.cs` changes criterion range/weight/enabled state/mapping during a run and proves override validation, Prompt, result validation, and formula layout continue to use the original run snapshot。
- E-02 Windows local E2E runs against normal GATE-APP build output and does not require P-01 artifacts。P-01 alone owns self-contained publish/package generation and layout verification。
- GATE-CORE、GATE-EXCEL、GATE-AI、GATE-APP、GATE-ACCEPTANCE rerun F-03 architecture and supply-chain regression tests; a foundation invariant cannot become stale after GATE-1。

## Superseded map

The previous requirement v2.0 / plan v3.0 map with 5 production projects、5 test projects、72 task/gate rows is historical。Its fixed candidate-review、send-confirmation、Workbooks/Platform/Desktop project paths do not authorize or constrain current implementation。
