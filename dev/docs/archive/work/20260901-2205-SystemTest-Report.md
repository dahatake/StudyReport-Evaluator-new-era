# 添付実データ・初期値システムテストレポート

## 1. 結論

| 項目 | 今回の実測判定 |
|---|---|
| Run ID | `SYSTEM-TEST-REALDATA-V4-20260901-2205` |
| 最終実データtechnical test | **PASS（1/1、6分41秒）** |
| Evidence上のresult | **PASS_PRIMARY_APPLICATION_PATH** |
| Production durable components | **PASS** |
| 入力元本不変 | **PASS** |
| 現在の初期値・配点 | **PASS** |
| durable 530行処理 | **PASS** |
| 結果workbookのアプリ内検証 | **PASS** |
| 起動時のAI自動実行signal | **観測範囲内では未検出** |
| Live AI / Network-backed runner | **NOT_RUN_POLICY_REAL_DATA** |
| Microsoft Excel 16.0 external recalculation | **FAILED_ADVISORY** |
| Release全回帰 | **661/661 PASS、失敗0、skip 0** |

依頼された「実データをLive AIへ送らないtechnical deterministic runnerによるapplication path検証」の範囲ではPASSした。productionの行読取、durable orchestration、checkpoint、output planning、workbook finalizationを通過している。

ただし、通常の`ExecutionViewModel`、Copilot認証、CLI runtime identity、model discovery、Live model呼出しを連結した完全なuser-facing end-to-endではない。証跡のliteral result名`PASS_PRIMARY_APPLICATION_PATH`は、この限定されたtechnical durable pathを表すものとして解釈する。

外部Excel再計算は完了しなかったため、optional advisoryのFAILとして分離する。これをprimary technical pathのFAILへ混ぜず、同時に外部再計算PASSへも読み替えない。

## 2. 今回値の識別と過去結果の非流用

実データの最終判定値は、改善版test driverをRelease buildした後に新規生成した次の2点から採用した。

- `test/system-20260901-2205/realdata-v4-final-evidence.json`
- `test/system-20260901-2205/realdata-v4-final.trx`

| Recorded source field | 値 |
|---|---|
| commit | `b7b0c0ac91148c42c4aa61e04f1dab4efbb0fc98` |
| run開始時worktree status SHA-256 | `1EFD7C7C8D5A6A8678C1A171778E84FC69E17C850C2B4F1F78E43D914F437718` |
| 測定開始UTC | `2026-09-01T20:46:53.7646168Z` |
| test driver | `tests/StudyReportEvaluator.App.Tests/E2E/RealDataSystemSmokeTests.cs` |

同じ作業中の先行run `realdata-v4.*` も1/1 PASSしたが、その後にprivacy、no-auto-run、process cleanupの観測を強化した。先行run固有の起動時間、orchestrator時間、output名・size・hash、worktree hash、TRX時間は最終値へ混ぜていない。

### Source / binary provenanceの限界

- source commitとworktree status hashは呼出側が環境変数で渡し、testはその値を記録した。test自身によるsource検証ではない。
- worktree hashは`git status --porcelain`出力のhashであり、dirty file本文のcontent digestではない。run後に今回生成したfinal JSON/TRXだけを除外すると同じ値になったが、これはstatus listingが同じことだけを示す。
- 実行したtest DLLとapp EXEのSHA-256、およびsource-to-binary build manifestはrun時に保存していない。
- したがって、実行binaryと全dirty source bytesの暗号学的な対応付けや、run中のdirty file本文不変までは証明しない。
- 先行runとfinal runはtest定数上、同じ`run_id`を持つ。最終runはファイル名、`measured_at_utc`、artifact SHA-256の組合せで識別する。

## 3. 添付とlocal materializationの対応

回答本文、header本文、sheet名は掲載しない。

| 項目 | 今回の実測 |
|---|---:|
| logical name | `attached-realdata.xlsx` |
| size | 470,806 bytes |
| local SHA-256 | `73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA` |
| 添付previewとのsize一致 | true |
| 添付preview先頭128 bytes一致 | true |
| format | `StandardXlsx` |
| package parts | 11 |
| worksheets | 1 |
| dimension | `A1:L531` |
| header row | 1 |
| data rows | 2〜531（530行） |

chat attachmentからlocal pathや添付全体の独立SHA-256は公開されなかった。このため、添付との対応はbyte sizeとchatで得られた先頭128 bytesの一致で確認し、local materialization全体のSHA-256を今回値として記録した。添付全体の独立hash同士を比較した、という主張はしない。

source sheet名は本文へ出さず、証跡にはSHA-256だけを保存した。

## 4. 現在のアプリ初期値

`InputViewModel`が実データを読み込んだ後に生成したdefinitionを直接検査した。

| 設定 | 実測初期値 |
|---|---:|
| Base points | 60 |
| Special points | 0 |
| Similarity penalty weight | 0.1 |
| Rounding digits | 1 |
| Max concurrency | 1 |
| Question count | 5 |
| Allocation total | 100 |
| Allocation remaining | 0 |

| 設問 | Primary列 | Supporting列 | Evaluator | Points |
|---:|---|---|---|---:|
| 1 | F | なし | `KnowledgeCoverage` | 8 |
| 2 | G | なし | `CustomPrompt` | 8 |
| 3 | H | なし | `KnowledgeCoverage` | 8 |
| 4 | I | なし | `KnowledgeCoverage` | 8 |
| 5 | J | K | `CustomPrompt` | 8 |

`60 + 0 + (8 × 5) = 100`であり、初期配点は有効である。special budgetが0のため、special operationはdispatchされなかった。

## 5. 実行したtechnical path

### 5.1 起動optionとprefill

同じtest process内で`ServiceRegistration.FromStartup`から`MainWindowViewModel`を作り、`--input` parser受理と`InputViewModel`の明示loadによるprefillを確認した。

別途、Release apphostを`--input`付きで起動し、2秒間のprocess生存、Office runtime module不存在、起動前後のresult directory state、Copilot process ID集合を確認した。外部apphost内部のViewModel stateを直接観測したわけではない。

| 観測 | 実測 |
|---|---:|
| parser accepted（in-process composition） | true |
| prefill loaded（in-process composition） | true |
| Release apphost process started | true |
| startup probe | 2,046.3568 ms |
| 2秒probe中に終了 | false |
| controlled apphost cleanup | true |
| Office runtime module | 0 |
| result directory state unchanged | true |
| new Copilot process ID | 0 |
| potential automatic-run signal | false |

no-auto-run確認は起動前後のsnapshot比較であり、連続監視ではない。既存Copilot processの再利用、probe中だけ存在した短命process、result directory以外への出力、process増加を伴わないnetwork activity、2秒後の遅延挙動は検出できない。したがって「観測範囲内でsignal未検出」とする。

### 5.2 Durable orchestration

実データ行読取、input snapshot boundary、checkpoint store、output path planner、workbook finalizer、partial cleanerにはproduction classを使用し、`DurableQuantificationOrchestrator`をtestが直接構築した。

通常の`ExecutionViewModel`、production Copilot runner、認証、model discoveryは通していない。runtime identityにはtest用CLI versionと全ゼロCLI hashを使用した。

AI operationは、実データを外部へ送らないcontent-discarding deterministic runnerへ置換した。

- Live AI invoked: false
- Network-backed runner invoked: false
- Policy: `NOT_RUN_POLICY_REAL_DATA`
- Model identity: `local-deterministic-midpoint-no-network-not-for-grading`
- Normal result: effective range midpoint
- Similarity fixed value: 0.25
- Reference result: 固定technical text
- 教育的採点品質を評価するrunnerではない

fake runner実装はpayloadのIDとrangeだけを参照し、workbook本文を判定材料として使用しない。これはAI品質試験ではなく、application components、件数、checkpoint、formula、出力構造の技術試験である。

## 6. Durable run実測

| 項目 | 実測 |
|---|---:|
| Orchestrator elapsed | 382.7887109秒 |
| Status / finalization | `SUCCESS` / `SUCCESS` |
| Completed rows | 530 |
| References | 5 |
| Planned / completed normal evaluations | 2,650 / 2,650 |
| Succeeded / empty | 2,031 / 619 |
| Failed / cancelled normal evaluations | 0 / 0 |
| Planned / completed operations | 5,305 / 5,305 |
| Operation failures / cancelled | 0 / 0 |
| Reference runner calls | 5 |
| Normal runner calls | 2,031 |
| Similarity runner calls | 2,031 |
| Special runner calls | 0 |
| Source row reads | 530 |
| Checkpoint create / update / load | 1 / 535 / 0 |
| Maximum observed concurrency | 1 |
| Final in-flight | 0 |
| Partial cleanup failure | false |

5,305 operationsは、5 reference operations、2,650 normal evaluation operations、2,650 similarity operationsの合計である。空入力619件はrunnerを呼ばず、仕様どおり完了扱いになった。

確認したstageは`Preparing`、`GeneratingReferences`、`SavingCheckpoint`、`EvaluatingRows`、`FinalizingWorkbook`、`Completed`で、進捗は単調増加した。checkpoint update 535件は5 reference完了後と530 row完了後の保存に対応する。

## 7. 結果workbook

結果workbookと外部再計算用copyはtest専用一時領域に生成し、検査後に削除した。入力元本は全操作後もhash、size、last-write timeが一致した。

| 項目 | 実測 |
|---|---:|
| logical output name | `eval-20260902-0546.xlsx` |
| temporary output size | 971,387 bytes |
| temporary output SHA-256 | `27CCB1DC903E60EBB454B9F053FBAF13AB7D349BDB10C738B5625A5043EC23A2` |
| worksheets | 5 |
| app-owned sheets | 4 |
| checkpoint sheet retained | false |
| result rows / columns | 530 / 100 |
| Config formulas | 2 |
| Results formulas | 20,670 |
| References / Run formulas | 0 / 0 |
| formula errors | 0 |
| Open XML validation errors | 0 |
| numeric / blank FinalScore | 530 / 0 |
| calculation mode automatic | true |
| full calculation on load | true |
| original output unchanged after recalc-copy operation | true |

5 sheetsは元source sheetと、Config、References、Results、Runの4 app-owned sheetsである。完成時にpartial checkpoint sheetは残っていない。

## 8. External spreadsheet recalculation

| 項目 | 実測 |
|---|---|
| Application | Microsoft Excel 16.0 |
| Status | `FAILED_ADVISORY` |
| Rationale | `external_recalculation_failed` |
| Evidenceのprocess cleanup flag | true |

Excel automationが完了しなかったため、再計算後workbookのformula値・Open XML状態は検証できていない。証跡上の再計算後formula/error件数0は、成功やerrorなしを意味せず、失敗時にinspectionを実行していないことを表す。

cleanup flagはtrueだが、test実装はExcel PIDを取得できなかった場合もtrueを返し得る。したがって、今回process leakが存在しないことを直接証明する値としては扱わない。

外部spreadsheetはrequired runtimeではなく、このadvisory failureをtechnical durable pathのFAILへ混ぜていない。一方で、外部Excel roundtripの互換性をPASSとも記載しない。

## 9. Privacy・cleanup

### Structured JSON

final JSONは固定metadata schemaであり、書込前に次を検査した。

- input absolute path、input directory、user-profile pathの文字列一致
- workbook cellから読んだ、16文字以上かつ文字を含むtextの完全一致

再検査でもfinal JSONにinput absolute pathとuser-profile pathの完全一致はなかった。証跡flagsはcell本文、header本文、sheet名、private pathをfalseとしている。

この検査は汎用DLPではない。16文字未満のtext、変形・分割・encodingされた値、すべての個人識別子の不存在までは保証しない。

### TRX

final TRXにはinput absolute pathとuser-profile pathの完全一致はなかったが、test runnerが生成する実行user/domain、machine識別、private build/storage path等のmetadataを実際に含む。privacy-reviewed共有artifactではなく、local evidenceとして扱う。

### Temporary files / processes

- 入力元本のidentityは操作後も一致した。
- final output、recalculation copy、temporary directoryはtest終了時に削除した。
- Release apphostは保持した`Process` handleで停止を確認した。
- Excel cleanupは前節の計測上の限界があるため、process leakなしとは断定しない。

## 10. Build・test結果

| 実行 | 今回結果 | Durable artifact |
|---|---|---|
| 最新driverを含むRelease build | PASS | retained manifestなし |
| opt-inなしtarget test | 1/1 PASS（実データ処理なし） | retained TRXなし |
| opt-inありfresh real-data test | 1/1 PASS（6分41秒） | final TRXあり |
| Full solution Release tests | 661/661 PASS、失敗0、skip 0 | retained TRXなし |
| Core tests | 189/189 PASS | session terminal outputのみ |
| App tests | 472/472 PASS | session terminal outputのみ |
| Full test duration | 48.0秒（wall 49.1秒） | session terminal outputのみ |
| Driver static diagnostics | 0 | editor diagnostics |

full suiteは実データopt-in環境変数を明示解除して実行したため、実データrunを重複実行していない。

Release buildと661件の回帰結果は今回sessionのfresh terminal outputで確認したが、専用TRXやbuild manifestを保存していない。このため、final JSON/TRXと独立に監査可能なmachine artifactではなく、実行binaryのhashにも紐付かない。

## 11. Evidence

| File | 用途 | SHA-256 |
|---|---|---|
| `test/system-20260901-2205/realdata-v4-final-evidence.json` | privacy scan済みstructured evidence | `FD2D32180E839095DA33E57F18517AF15A2545978510A69E531917C1F2C77C74` |
| `test/system-20260901-2205/realdata-v4-final.trx` | private metadataを含むlocal VSTest record | `26B70B013B30C7FF72F279816BEDF76845126883A7B4C74010C581C2E7491050` |
| `tests/StudyReportEvaluator.App.Tests/E2E/RealDataSystemSmokeTests.cs` | opt-in technical test driver | binary provenanceは未保存 |

## 12. 確認済み・未確認・残存finding

### 確認済み

- 現在の初期値と5設問配点
- 実データ530行のproduction durable components
- 5,305 operationsの完了とcheckpoint schedule
- input-preserving 5-sheet output
- app内Open XML、formula、FinalScore検証
- in-process startup compositionによるprefill
- Release apphostの短時間生存と観測範囲内のno-auto-run signal不存在
- 入力元本不変、temporary output cleanup、deterministic runnerのAI/network非実行

### 未確認・非保証

- 通常のExecution UI、Copilot認証、CLI、model discoveryを連結した完全なuser-facing run
- Live GitHub Copilotによる教育的評価品質、公平性、妥当性
- 実データのLive AI結果
- 手動GUIによる全step操作と画面表示
- 外部apphost内部のprefill state
- 2秒の前後snapshotで検出できない自動実行やnetwork activity
- external Excel recalculation後のformula値とOpen XML互換性
- Excel process leakの直接的な不存在証明
- source bytesと実行binaryの暗号学的な対応
- macOS、Linux、Windows Arm64
- package install、署名、notarization

### 残存finding

1. **Evidence provenance（high assurance gap）:** dirty source contentと実行DLL/EXEをhashで紐付けていない。application failureではないが、厳密な再現性・監査性を制限する。
2. **Test scope（medium assurance gap）:** production durable componentsは通過したが、通常のExecution UI、auth、CLI、model discoveryを含むfull pathではない。
3. **Launch observability（medium assurance gap）:** apphostのprefillを直接観測しておらず、no-auto-runは2秒間の前後snapshotに限定される。
4. **External Excel（advisory）:** recalculationは`FAILED_ADVISORY`。cleanup flagにもPID未取得時の限界がある。
5. **Regression provenance:** 661/661 PASSはfresh terminal実測だが専用machine artifactを保持していない。
6. **Attachment binding:** 添付全体の独立hashが取得できず、size + 先頭128 bytesでlocal materializationを対応付けた。
7. **Run identity:** preliminary/final evidenceが同一`run_id`を使用しており、ファイル名・時刻・hashの併用が必要。
8. **Privacy assurance:** JSONの固定schemaと混入scanを確認したが汎用DLP保証ではなく、TRXはprivate metadataを含む。

## 13. 最終判定

- **Technical durable path:** PASS
- **Evidence literal result:** `PASS_PRIMARY_APPLICATION_PATH`（本レポートでは限定scopeとして解釈）
- **Live AI:** `NOT_RUN_POLICY_REAL_DATA`
- **External recalculation:** `FAILED_ADVISORY`
- **Full user-facing application E2E:** NOT_RUN
- **教育的採点品質:** NOT_EVALUATED

今回観測したproduction durable componentsにfailureはない。一方、未実施surfaceやprovenance gapを含めた完全なapplication全体について「blockerなし」とは主張しない。
