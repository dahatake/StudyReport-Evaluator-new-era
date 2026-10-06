# Microsoft Excel再保存後のOpen XML検証エラー調査レポート

| 項目 | 内容 |
|---|---|
| Defect ID | `REALDATA-EXCEL-CALCCHAIN-001` |
| 状態 | **根本原因確認済み・production未修正** |
| Primary app run | PASS |
| 発生境界 | Microsoft Excel 16.0による一時copyのfull recalculation・保存後 |
| Formula error | 0 |
| Strict schema/semantic error | FAIL |
| 最終結果 Excel | 変更なし・アプリ直出力のvalidationはPASS |

## 現象

`work/20260901-realdata-application-result.xlsx` の一時copyをMicrosoft Excel 16.0で開き、`CalculateFullRebuild`、保存、closeした。Excel処理は正常完了し、11,130 formula cellに `#REF!`、`#DIV/0!`、`#VALUE!`、`#N/A`、`#NAME?` 等は0件だった。

その後、Open XML SDK 3.5.1の`OpenXmlValidator`でcopyを検査すると、`/xl/calcChain.xml` の`c` elementに `Sem_MissingIndexedElement` が発生した。最初のrunはvalidator既定上限1,000件で打ち切られた。上限を20,000へ拡大した複数runでは8,330〜8,414件、最終runでは8,392件だった。件数はcalculation orderの再構築に伴い変動したが、error ID、part、nodeは全件同一だった。

## 再現条件

1. 添付実データをアプリのproduction local workflowで別workbookへ出力する。
2. アプリ直出力を検査する。
   - calcChain part: absent
   - formula count: 11,130
   - formula error: 0
   - Open XML schema/semantic error: 0
3. 出力のcopyをMicrosoft Excel 16.0でfull recalculationして保存する。
4. 保存後copyをOpen XML SDKで検査する。
   - calcChain part: present
   - calcChain cells: 11,130
   - all explicit `i`: 5
   - `Sem_MissingIndexedElement`: 8,392（最終run）
   - formula error: 0
5. 同じcopyからcalcChain partだけを削除し、再検査する。
   - formula count: 11,130（不変）
   - formula error: 0
   - schema/semantic error: 0

一時copyは各run後に削除し、original result workbookのSHA-256が不変であることを確認した。

## 根本原因

### 直接原因

Microsoft Excel再保存時に追加されたoptional Calculation Chain partが、Open XML SDKのsemantic validatorで解決できないindex参照を含む。全errorは`Sem_MissingIndexedElement`、node `c`、part `/xl/calcChain.xml` に限定された。calcChainを除去するとerrorは完全に0になるため、formula本体、cached result、Config/Results/Run構造、元worksheetの破損ではない。

### 発火条件

添付workbookの元sheetは`sheetId=3`。アプリの`WorkbookSheetWriter.AddWorksheet`は既存最大sheetIdへ1を加算するため、追加sheet IDsは4、5、6となり、formulaを持つResults sheetはordinal 3 / `sheetId=5`となる。

Excelは再保存時、11,130件のcalcChain cellすべてへ`i=5`を明示した。workbookのsheet数は4である。Microsoft Learnの仕様説明ではcalcChain `c`の`i`は関連sheetの**index**を示す。非連続sheetIdを保持したworkbookと、Excelが生成するcalcChainの`i`、Open XML SDKのindexed-element semantic checkの組合せがfailureを発生させる。

### なぜ既存synthetic smokeで検出されなかったか

既存`ExternalSpreadsheetRecalculationSmokeTests`は小さなsynthetic workbookを使用し、元sheet IDsは1、2で連続している。app-owned sheet追加後もResultsのsheetIdは4、全sheet数は5で、今回の「sheetIdがsheet数を超える」形状を作らない。また同testはformula cached valueを検査するが、保存後に`OpenXmlValidator`を実行しない。

前回システムテストレポートではexternal spreadsheet recalculation自体が`NOT_RUN`だったため、本defectを検出していない。これは前回PASSとの矛盾ではなく、今回初めて追加した境界での新規検出である。

## 影響

### 影響なしと確認できた範囲

- アプリ直出力のatomic commit。
- アプリ直出力のOpen XML validation。
- formula数、formula cached value、formula error marker。
- original input identity。
- Microsoft Excelによるopen、full recalculation、save、close。
- 最終成果物（再計算はcopyのみで実施）。

### 影響する範囲

- Excelで一度保存したoutputを、Open XML SDKのstrict semantic validationへ通すdownstream処理。
- calcChain errorをpackage不正として拒否する連携。

Excel自身は生成したcopyを正常に保存しており、式エラーは0件だった。ただしstrict validator failureを無視して「完全互換」とは判定しない。

## 推奨対応

1. `ExternalSpreadsheetRecalculationSmokeTests`へ、元sheetIdが3など非連続のsynthetic fixtureを追加する。
2. Excel保存後にも`OpenXmlValidator`を実行し、formula値だけでなくpackage semantic validityを確認する。
3. roundtrip要件を決める。
   - calcChainは任意なので、管理下の再計算pipelineでは保存後にcalcChain partを削除して再検証する。
   - またはoriginal sheet metadata保持契約との整合を確認した上でsheetId正規化／割当方式を再設計する。
   - strict downstream validator側で特定errorを除外する案は、他のmissing referenceを隠すため推奨しない。
4. 修正時は、非連続sheetId、formula保持、input sheet identity、Excel roundtrip、calcChain有無を同時にregression testする。

本依頼ではproduction codeの修正は行わず、結果生成と根本原因調査に限定した。

## 公式仕様との照合

Microsoft Learnは、calculation chainについて次を説明している。

- calcChainはformula cellの計算順を記録するpart。
- `c/@r`はcell address、`c/@i`は関連sheetのindex。
- calculation chainは必須ではなく、spreadsheet applicationはload時にformula依存から再構築できる。
- calculation eventによりchain順序は変更され得る。

出典: [Working with the calculation chain](https://learn.microsoft.com/office/open-xml/spreadsheet/working-with-the-calculation-chain)

## 証跡

- `work/20260901-realdata-recalculation-evidence.json`
- 初回失敗: `artifacts/test/realdata-retained-20260901-0912/recalculation/recalculation.trx`
- full-count再現: `artifacts/test/realdata-retained-20260901-0912/recalculation-diagnostic/recalculation-diagnostic.trx`
- sheetId診断: `artifacts/test/realdata-retained-20260901-0912/sheetid-diagnostic/sheetid-diagnostic.trx`
- 根本原因対照: `artifacts/test/realdata-retained-20260901-0912/final-recalculation-diagnostic/final-recalculation-diagnostic.trx`
- `src/StudyReportEvaluator.App/Workbooks/Writing/ConfigSheetWriter.cs`（`WorkbookSheetWriter.AddWorksheet`）
- `tests/StudyReportEvaluator.App.Tests/E2E/ExternalSpreadsheetRecalculationSmokeTests.cs`
- `tests/StudyReportEvaluator.App.Tests/Workbooks/Intake/FileFormatClassifierTests.cs`（synthetic sheet IDs）
- `artifacts/test/system-20260901-1713/system-test-report.md`
