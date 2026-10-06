# 添付実データによるアプリケーション実行レポート

| 項目 | 結果 |
|---|---|
| Run ID | `REAL-WORKBOOK-RETAINED-OUTPUT-20260901-0912` |
| 対象 source | clean commit `3f4227e15d725d2a010b4c6a9a94eb78b20b8573` |
| アプリケーション経路 | **PASS** |
| 結果 Excel | `work/20260901-realdata-application-result.xlsx` |
| Live AI | **未実行**（実在データの外部送信を回避） |
| 評価値 | **技術テスト専用 midpoint。教育的採点結果ではない** |

## 実施内容

添付実データ `sample/realdata.xlsx` を read-only で読み込み、アプリの `InputViewModel` が workbook の見出しから自動生成した有効な質問候補をすべて選択した。production の snapshot、mapping、request preflight、row source、scheduler、score/formula writer、output validator、atomic committer を通して別 workbook を生成した。

外部AIへ実データを送らないため、`IEvaluationRunner` 境界だけを content-discarding deterministic runner に置換した。非空回答には各criterion rangeのmidpointを返し、理由欄へ技術テスト専用である旨を記録した。この値を学生の評価・成績・教育判断へ使用してはならない。

## 入力

| 項目 | 実測値 |
|---|---|
| Size | 470,806 bytes |
| SHA-256 | `73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA` |
| Classification | `StandardXlsx` |
| Sheet / dimension | 1 sheet / `A1:L531` |
| Header / data rows | 1 / 2〜531（530 rows） |
| 実行後 identity | `MATCH` |

入力の回答本文、header本文、input sheet名は本レポートへ掲載していない。

## 選択した質問項目

アプリの自動候補5件をそのまま採用した。質問本文は出力 workbook の `Quantification_Config` に保持されるが、本レポートではprivacyのため列identityだけを示す。

| 順序 | Primary | Supporting | Evaluator |
|---:|---|---|---|
| 1 | F | なし | Knowledge coverage |
| 2 | G | なし | Custom Prompt |
| 3 | H | なし | Knowledge coverage |
| 4 | I | なし | Knowledge coverage |
| 5 | J | K | Custom Prompt |

選択元とquestion textのSHA-256は `work/20260901-realdata-application-run-evidence.json` に記録した。

## 実行結果

| 項目 | 実測値 |
|---|---:|
| Planned | 2,650 |
| Completed | 2,650 |
| Success | 2,031 |
| Empty | 619 |
| Failure | 0 |
| Cancelled | 0 |
| Requested concurrency | 3 |
| Maximum observed concurrency | 2 |
| Run elapsed | 78.490060 s |
| Total probe elapsed | 84.371680 s |
| Network invoked | false |

`EMPTY` はprimary cellが空白の評価単位であり、0点へ変換していない。

## 結果 Excel

| 項目 | 実測値 |
|---|---:|
| Path | `work/20260901-realdata-application-result.xlsx` |
| Size | 786,819 bytes |
| SHA-256 | `913F1F0F4E94A8E47A74182C171903DC2EA2038AF48C0AA214DDBD7F83A1CCD3` |
| Worksheets | 4（元1 + app-owned 3） |
| Result rows / columns | 530 / 61 |
| Formula cells | 11,130 |
| Formula error cells | 0 |
| Open XML schema errors | 0 |
| Direct/final package validation errors | 0 / 0 |
| Atomic commit | `SUCCESS` |
| Working file remains | 0 |

追加sheetは `Quantification_Config`、`Quantification_Results`、`Quantification_Run`。結果 workbook は入力全体をbyte-copyしているため、入力と同等以上の機密情報として扱う必要がある。

## 通常アプリ起動

Release apphostを別途起動し、startup probe中のprocess生存、AMD64、`net10.0` framework-dependent runtime、Office/COM runtime module非load、終了時cleanupを確認した。結果は1/1 PASS。

## 判定

**アプリケーションによる結果 workbook 生成は PASS。** 実行時 failure、cancel、atomic output error、formula errorは発生しなかった。

ただし、Microsoft Excelでcopyを再計算・保存した後のstrict Open XML validationに互換性defectを検出した。アプリ直出力は影響を受けず、詳細は `work/20260901-realdata-error-investigation-report.md` を参照すること。

## 出典

- `work/20260901-realdata-application-run-evidence.json`
- `work/20260901-realdata-recalculation-evidence.json`
- `src/StudyReportEvaluator.App/ViewModels/InputViewModel.cs`
- `src/StudyReportEvaluator.App/Workflow/QuantificationOrchestrator.cs`
- `src/StudyReportEvaluator.App/Workbooks/Validation/OutputPackageValidator.cs`
- `src/StudyReportEvaluator.App/Workbooks/Writing/AtomicOutputCommitter.cs`
- `tests/StudyReportEvaluator.App.Tests/E2E/WindowsLocalApplicationTests.cs`
