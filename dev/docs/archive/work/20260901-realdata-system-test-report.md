# 添付実データによる追加システムテスト実行レポート

| 項目 | 判定 |
|---|---|
| Run ID | `SYSTEM-TEST-REALDATA-20260901-0912` |
| 対象 commit | `3f4227e15d725d2a010b4c6a9a94eb78b20b8573` |
| Primary application workflow | **PASS** |
| 結果 Excelのアプリ内検証 | **PASS** |
| Release build / regression | **PASS** |
| Microsoft Excel再保存後のstrict schema | **FAIL** |
| 総合 | **FAIL（外部Excel roundtrip互換性defect 1件。アプリ直出力は利用可能）** |

## 前回レポートとの違い

前回レポート `artifacts/test/system-20260901-1713/system-test-report.md` は commit `e3403a7…` を対象に、実データの1質問・530評価単位を一時出力まで通し、結果 workbookを削除した。外部spreadsheet再計算は `NOT_RUN` だった。

今回の追加テストは次を拡張した。

- 現行clean commit `3f4227e…` を対象。
- workbook由来の質問候補5件をすべて採用。
- 2,650評価単位を実行。
- 検証済み結果 Excel を `work/` に保持。
- 通常Release apphostを実際に起動。
- Microsoft Excel 16.0で一時copyをfull recalculationして再保存。
- 恒久test suiteを再実行。

したがって、前回のPASSを流用したものではなく、別runの実測である。

## テスト結果

| Test surface | 実測 | 判定 |
|---|---|---|
| Locked restore | success | PASS |
| Release build | 0 warnings / 0 errors | PASS |
| 恒久test suite | 483 passed / 0 failed | PASS |
| Normal Release app startup | 1 passed / 0 failed | PASS |
| Real workbook retained-output probe | 1 passed / 0 failed | PASS |
| Input identity recheck | exact match | PASS |
| 5-question local run | 2,650 completed / 0 failures | PASS |
| Atomic output commit | `SUCCESS` | PASS |
| Final app output validation | 0 errors | PASS |
| Formula marker scan | 11,130 formulas / 0 formula errors | PASS |
| Excel full recalculation | completed; original output unchanged | PASS |
| Excel-saved copy formula scan | 11,130 formulas / 0 formula errors | PASS |
| Excel-saved copy strict Open XML validation | latest 8,392 semantic errors | FAIL |
| calcChain removal control | formulas 11,130 retained / schema errors 0 | PASS_ROOT_CAUSE_CONFIRMED |

## 実データ実行

質問候補はprimary F、G、H、I、Jの5件。JのみKをsupporting columnとして使用した。回答本文とheader本文は証跡・レポートへ含めていない。

| Status | Count |
|---|---:|
| SUCCESS | 2,031 |
| EMPTY | 619 |
| FAILURE | 0 |
| CANCELLED | 0 |
| Total | 2,650 |

評価は外部AIを呼ばない技術テスト用midpoint runnerであり、教育的品質の試験ではない。

## 成果物

| File | 用途 | SHA-256 |
|---|---|---|
| `work/20260901-realdata-application-result.xlsx` | retained result workbook | `913F1F0F4E94A8E47A74182C171903DC2EA2038AF48C0AA214DDBD7F83A1CCD3` |
| `work/20260901-realdata-application-run-evidence.json` | application run evidence | `E33055F90A27489FF39950F950369737F09E73D58B5DC47F655739308FDF4DAB` |
| `work/20260901-realdata-recalculation-evidence.json` | Excel roundtrip/root-cause evidence | `918E07B974FC63C494A39C02C882357082AC4EF1AD481F198793ED5FDB7E16E6` |
| `work/20260901-realdata-system-test-evidence.json` | aggregate machine evidence | `887A393132A54C79463232D13C4BD3900E18F9952DA8A06C96D474F302277061` |

## Defect disposition

アプリ直出力はcalcChainを含まず、Open XML schema 0 error、formula error 0、atomic validation PASSである。一方、Excel再保存copyには `/xl/calcChain.xml` が追加され、`Sem_MissingIndexedElement` が発生した。これは外部roundtrip互換性defectとしてFAILに算入した。

数式値自体のエラーではなく、optional calculation-chain metadataのsemantic validation問題である。詳細と再現・対照実験は `work/20260901-realdata-error-investigation-report.md` を参照すること。

## 既知状態との整合

`dev/docs/implementation-status.md` では旧 `IMPL-GAP-001/002` はclosed、new GATE-ACCEPTANCEは `PENDING_REQUIRED_RERUN`。今回のrunはgap closure後の非文書検証を補完するが、独立reviewや正式gate recordの代替とはしない。

## Privacy

- 実データをLive AIへ送信していない。
- cell本文、header本文、input sheet名、credential、private pathをレポートへ含めていない。
- Excel再計算はlocal copyに限定し、copyは削除した。
- 結果 Excel は入力全体を含むため、通常のsource artifactへcommitせず機密fileとして扱う。

## 出典

- `work/20260901-realdata-application-run-evidence.json`
- `work/20260901-realdata-recalculation-evidence.json`
- `work/20260901-realdata-system-test-evidence.json`
- `artifacts/test/system-20260901-1713/system-test-report.md`
- `dev/docs/implementation-status.md`
- `tests/StudyReportEvaluator.App.Tests/E2E/ExternalSpreadsheetRecalculationSmokeTests.cs`
- `tests/StudyReportEvaluator.App.Tests/Workbooks/Intake/FileFormatClassifierTests.cs`
- `src/StudyReportEvaluator.App/Workbooks/Writing/ConfigSheetWriter.cs`
- `src/StudyReportEvaluator.App/Workbooks/Validation/OutputPackageValidator.cs`
