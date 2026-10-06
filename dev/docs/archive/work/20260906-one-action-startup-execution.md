# 1操作起動 — 承認・実行・敵対的レビュー記録

## 承認と今回の優先指示

- 2026-09-06、要求所有者が `20260906-0617-one-action-startup-plan.md` のデフォルト案と全タスク実行を承認。
- 全タスクで、実施・直接検証 → 敵対的レビュー → 根拠のある指摘の反映・再検証 → 次の依存タスク、の順序を守る。
- 独立したファイルownerのタスクだけを並列実行する。同一worktreeのbuild/publishは直列。
- **最新指示による変更:** D-14/R03のMINOR更新を取り消し、全タスク完了後にPATCHを1つ増やす。開始版は `0.8.3`、予定版は `0.8.4`。版とCHANGELOGの更新は最後に行う。
- P05/D13のR03依存は版方針の確定だけとし、開発中は0.8.3のまま検証する。最後のR03で0.8.4へ変更した後、最終artifactを再検証する。
- CHANGELOGは最後にKeep a Changelog形式のUnreleasedへ概要文と実装済み変更を追加する。
- tag/push/公開、installer追加、保護機能無効化は実行しない。
- **2026-09-06 追加指示:** V02（fresh Windows clean-host検証）をスキップする。実施済み・PASSへ読み替えない。
- baseline: `26512a4a8a686134ea9bbdf4f271d04ca1fff91b`。既存dirtyは確認用プラン1件のみ。

## タスク状態

| ID | 状態 | 実測/レビュー/反映 |
|---|---|---|
| S01 | PASS_REVIEW | GUI3ケース、28既存tests成功。敵対的レビュー指摘0、hash/source不変確認済み |
| R01・R02 | PASS_REVIEW | v4.5要求/ADR0016。それぞれ独立レビュー指摘0、診断/diff確認 |
| P01・P03 | PASS_REVIEW | App限定profile/専用lock。直接test群49件PASS、各独立レビュー指摘0 |
| P02 | PASS_REVIEW | 129直接/画面回帰tests、実SingleFile publish成功。独立レビュー指摘0 |
| P04 | N/A_REVIEWED | 既存manifest/CLI contentで実publish成功。変更不要を独立レビューで確認 |
| P05 | PASS_REVIEW | SUBST別名指摘を実再現・修正。実EXE含む13tests成功、再レビュー指摘0 |
| P06 | PASS_REVIEW | CP932変換を実測してUTF-8へ修正。全7件PASS、独立レビュー指摘0 |
| P07 | PASS_REVIEW | 実7件・9観測からPASS_DEVELOPMENT生成。独立レビュー指摘0、公開用証跡とは分離 |
| A01 | PASS_REVIEW | login専用service。直接test群49件PASS、独立レビュー指摘0、固定CLI help引数受理 |
| A02・A03 | PASS_REVIEW | ViewModel/画面を実装。129/73test群PASS、個別レビュー指摘0 |
| C01 | PASS_REVIEW | v2 closed schema、合成正例/異常例23件PASS、独立レビュー指摘0 |
| C02 | PASS_REVIEW | v1既存9件＋v2新規63件、計72/72 PASS。UTF-8修正後に再検証、独立レビュー完了 |
| C03 | PASS_REVIEW | candidate builder 18/18 PASS。source不浄/改竄/rollback/排他を実process検証 |
| C04 | PASS_REVIEW | CIへEXE検証を直列追加、静的診断0・独立レビュー指摘0。実workflowは未実行 |
| C05〜C07 | PASS_REVIEW | matrix/workflow契約をv2へ同期。V01全体実行で全件PASS、独立レビュー指摘0。実workflow dispatchは未実行 |
| D01 | PASS_REVIEW | architectureの標準抽出・login境界を同期。公開gateは未実装として分離、独立レビュー指摘0 |
| D04 | PASS_REVIEW | implementation-status/claim-ledgerを同期。claim C-001〜C-044、独立レビュー指摘0 |
| D05・D06 | PASS_REVIEW | README/guide入口を未公開EXEと公開v0.8.1ZIPに区別。個別レビュー指摘0 |
| D07〜D10 | PASS_REVIEW | 利用者guide4件同期。D08の2指摘を反映、関連文書とP06進捗の再レビュー指摘0 |
| D11 | PASS_REVIEW | 05画像の2描画一致・viewport/状態assert成功。textレビュー指摘0、05だけcopy・他6枚不変確認 |
| D12 | PASS_REVIEW | dev入口2件へADR-0016/feasibility/profile/scriptを追加。独立レビュー指摘0 |
| D14 | PASS_REVIEW | SystemTest-prompt.mdをv4.5・26 scenarioへ同期。ST-UC-16/25/26を単一EXE契約へ改訂 |
| D15 | PASS_REVIEW | DocumentationContractTests 17/17 PASS。文書側の強い開示表現を弱めず期待値を実文言へ一致 |
| R03 | PASS | `0.8.3`→`0.8.4` PATCH、CHANGELOG Unreleased追記。`version.ps1 verify` PASS、self-test 15 assertions PASS |
| V01 | PASS | 0.8.4最終artifactを再生成し全体opt-in回帰。Core 190/190＋App 805/805＝995/995 PASS（failed 0、skipped 0） |
| V02 | SKIPPED_BY_INSTRUCTION | 2026-09-06、要求所有者の明示指示によりfresh Windows clean-host試験を実施していない。CH-01〜06は未実施のまま |
| V03 | PASS_REVIEW | 最終整合確認。文書claimと実測の一致、未実施範囲のNOT_RUN維持、source不変を確認。自動変数衝突1件を修正 |

## 検証の境界

- 開発hostのPATH/.NET隔離試験を「OSのみの端末」の成功としない。
- 現hostに `WindowsSandbox.exe` が存在することだけを確認。機能の実行可否、fresh OS試験、本人loginはまだ未確認。
- 本sessionでは `Get-WindowsOptionalFeature -Online` が管理者特権不足で失敗し、Sandbox機能の状態自体を確認できていない。
- V02はこの環境制約とは別に、要求所有者の指示で意図的にスキップした。試行して失敗したのではない。
- 未実施試験はNOT_RUNとし、過去の結果やfake認証を流用しない。

## 記録

実測と各タスクのレビュー結果を、完了時にこの下へ追記する。秘密値・device code・学生data・生ログを記録しない。

### S01

- App限定single-file profileでEXE生成。bytes `282870564`、SHA-256 `EDFBBD4C1E60885DC5AB038BA7474E494AAC999FF401342A8EAF9142A2C22AE1`。
- cold/warm/相対input＋Promptの実画面をUI Automationで確認。CLI hash、抽出先App base、同梱doc、input不変、正常終了を確認。
- 追加build依存はAppの `Microsoft.NET.ILLink.Tasks 10.0.11` だけ。Core追加なし。
- 敵対的レビュー: `PASS_REVIEW; actionable findings: 0`。反映対象なし。evidenceと実EXEのhash/sizeを再照合しPASS。
- `dev/docs/preflight/windows-singlefile-feasibility.md` に詳細とNOT_RUNを記録。G1は機構範囲だけで通過。

### R01・R02

- R01: 要求v4.5へ改版、AC029〜034/TR30〜33追加。既存業務要件保持。独立敵対的レビュー `PASS_REVIEW`、指摘0。診断0/diff check成功。
- R02: ADR0016でApp限定標準single-fileとCLI所有範囲、公開gate、最終PATCH上書きを記録。独立敵対的レビュー `PASS_REVIEW0`。診断0、要求との整合確認。

### P01・P03・A01

- 独立file ownerで並列実装し、Release build成功。対象49 tests PASS（失敗/skip0、`TestResults/singlefile/p01-p03-a01.trx`）。
- P01: profileの17公開file allowlistとApp限定条件を検証。独立敵対的レビュー指摘0。
- P03: S01由来App専用lockを追加しcanonical一致とILLinkだけの追加を検証。独立敵対的レビュー指摘0。Core専用lock不要。
- A01: 固定引数/非収集/排他/取消/dispose/再試行/失敗を検証。独立敵対的レビュー指摘0。
- CLI1.0.79で `--no-auto-update --log-level none login --web-flow --help` がexit0。認証は未実行。
- 3タスクとも修正指摘なしのため反映対象なし。診断0/diff check成功を確認後に次へ進行。

### P02・P04・A02

- A02追加testの不足namespaceを修正してRelease build成功。P02/A02/既存UIの129tests PASS、失敗/skip0（`p02-a02.trx`）。
- P02 actual `publish-windows.ps1 -SingleFile` 成功。標準抽出後のmanifest/CLI hash/版/docを検証。独立敵対的レビュー指摘0。
- P04はその実測から `.csproj` 変更不要。独立レビューもN/A妥当・指摘0。独立実行済みとは表示しない。
- A02は既存MainWindow→VMのDispose経路で所有CLIを終了し、明示再確認まで認証状態を無効化。独立レビュー指摘0。

### P05・A03

- 実artifactをopt-inした73tests PASS（`p05-a03.trx`）。A03は実際のbutton/fake、keyboard、200% layoutを含め独立レビュー指摘0。
- P05初回レビューでSUBSTによる別drive input aliasを指摘。親が一時SUBSTのpath resolutionだけで再現し、sourceへの書込みなし・SUBST解除済み。
- package引数を同一logical driveに限定する最小修正。13tests PASS（`p05-review-fix.trx`）、再レビューで指摘解消・残0。end-user起動に制約を追加していない。

### D05・D06・D11

- D05 READMEとD06 guide入口は公開v0.8.1 ZIP/未公開candidateを区別し、個別独立レビュー指摘0。clean-host/loginのNOT_RUNを維持。
- D11はsynthetic・認証未確認・login未開始の05を2回描画し一致。viewport/状態をassertしPASS。text独立レビュー指摘0。
- 画像応答取得は不安定だったため、独立した目視確認済みとは記録しない。検証済み05 PNGのみcopyし他6枚のhash不変を確認。画像自体は実Avalonia headless描画で捏造ではない。

### P06途中の実測

- 初回7package試験: native抽出先file障害/EXE破損の2件PASS、正常系5件は観測側で失敗。D11試験は別にPASS。
- trace共有modeとmain window有限待機を修正。再試験ではBOM処理/ComboBox選択名で失敗し、製品障害とは判定していない。
- 合成GUIの独立probeでmetadata読込済み、sheet実表示 `Synthetic · A1:A2 · 表示`、Prompt2件の表示順を確認。SelectionPattern配列は空という実測を優先。
- P06ではBOM対応text読取と実表示/metadata照合へ変更。真のGUI/CLI integrity/data不変検証は外していない。

### P06・P07完了

- 最後のmetadata不一致は、VSTest→pwsh stdinのCP932 best-fitで期待U+00B7がU+30FBへ変換されることを文字コード比較で実測。アプリ表示は正しい。送受信をstrict UTF-8へ固定しcanaryを追加した。
- `p06-unicode-fixed.trx`の該当1件PASS後、`p06-reviewed.trx`で全7件PASS（失敗/skip0）。独立レビュー指摘0。旧失敗TRXを成功で上書きしていない。
- P07は最新docを含む再package後、実7件PASS・9観測を照合し`artifacts/test/singlefile/StudyReportEvaluator-win-x64.exe.evidence.json`へ`PASS_DEVELOPMENT`を生成。独立レビュー指摘0。
- その時点の測定artifactは282,948,413 bytes、SHA-256 `49B3E5F1D06C1A35280D47F7E65FACF6132F5C82BEF19E9C86E5469F1F1039D8`。V01で0.8.4として再package済み（下記参照）。公開用`PASS_REQUIRED`やCH成功へ読み替えない。

### C01・C04・D01とguide follow-up

- C01はv1を残したclosed v2。23件の合成in-memory検証で正例・必須CH欠落・重複・開発用証跡・未知field等の許可/拒否を確認。架空のPASS証跡fileは書いていない。独立レビュー指摘0。
- C04は既存ZIP後にEXE publish/package/P07を直列追加。Windows CIを開発hostと明記。独立レビュー指摘0、実CI dispatchは未実行。
- D01は2-project、標準抽出、17公開file allowlist、login/認証network、将来v2gateを区別し独立レビュー指摘0。
- D07/D09は初回レビュー指摘0。D08の「旧login終了未確認で再起動だけを復旧としない」「runtime確認失敗をCLI破損だけに限定しない」2件を反映し、D07/D09にも安全案内を同期。
- D10のP06完了記述は実測済み。指摘の原因だった古いREADME/guide入口を更新し、D05〜D10の各再レビュー指摘0。未実施CH/本人認証はNOT_RUNのまま。

### C02直接検証

- v1既存9件を維持し、v2構造・実bytes/hash/sidecar・candidate/CH/runtime結合を追加。candidateではMSIX実物必須、最終matrixでは同candidateの記録だけを使用。
- nullable型のテストcompile errorを修正。同梱MSBuild manifestだけBOMを許可し、制御JSONはBOM/duplicate拒否を維持。
- 最初の72件中旧v1エラー表示の2件がCP932出力/UTF-8受信の不一致で失敗。テスト用pwshの出力を両端UTF-8にし、`c02-matrix-unicode-fixed.trx`で72/72 PASS（失敗/skip0）。

### D04・D12・D14・D15

- D04は`implementation-status.md`へ1操作起動の実装/検証/最新artifact/版/clean-host/公開の各状態行を追加し、`readme-claim-ledger.md`へC-039〜C-044を追加。claim IDはC-001〜C-044の連番で`AssertSequentialTableIds`が検証。
- D12は`dev/README.md`と`dev/docs/README.md`へADR-0016、feasibility文書、single-file publish profile/scriptの導線を追加。PASS_DEVELOPMENTの但し書きを明記。
- D14は`SystemTest-prompt.md`をv4.5・26 scenarioへ同期。ST-UC-16をADR-0016/matrix v2/単一EXEの配布移行oracleへ、ST-UC-25を決定的platform release gateへ改訂。
- D15は`DocumentationContractTests.cs`を実文言へ一致させ17/17 PASS。MSIX非公開開示やPowerShell不要の記述など、文書側の強い表現を弱める方向の変更はしていない。
- D04/D12/D14/D15をまとめた独立敵対的レビュー: actionable findings 0。

### R03（版とCHANGELOG）

- `Directory.Build.props`の`VersionPrefix`を`0.8.3`→`0.8.4`へPATCH。`VersionSuffix`は空のまま。
- `CHANGELOG.md`のUnreleasedへAdded 3件（単一EXE配布、login button/取消、matrix v2の閉じた3行・4 asset・clean-host契約）とChanged 2件（手動SHA-256比較の任意化、公開前のCH-01〜06必須化）を追記。`[0.8.1]`の履歴節は不変。
- repository全体の「0.8.3候補」記述を「0.8.4候補」へ更新。0.8.3を監査済みbaselineとして参照する履歴記述はそのまま残した。
- `dev/version.ps1 verify` PASS、`dev/version.tests.ps1` 15 assertions PASS。
- R03変更に対する独立敵対的レビュー: actionable findings 0。

### V01（0.8.4最終artifactの再生成と全体回帰）

- `publish-windows.ps1 -SingleFile` 成功後、EXEとZIPおよび各SHA-256 sidecarを再生成。
- P07を`-DevelopmentOnly`で再実行し7/7 PASS、`status=PASS_DEVELOPMENT`。最終EXEは282,949,002 bytes、SHA-256 `E03553BE3A0AA939C5CDBCAF6624A1F830D8AC4022144AB30F8AD65DEEFC382E`、`productVersion=0.8.4`。
- 単一EXE opt-in（artifact＋package）20/20 PASS。ZIP／MSIX package／macOS static／文書契約の焦点回帰28/28 PASS。
- 未署名MSIX機構検証は`PASS_MECHANISM`のみ。昇格した`Add-AppxPackage -AllowUnsigned`でのみinstall可能、配布対象外という但し書きを維持。
- 全体opt-in回帰: Core 190/190、App 805/805、合計995/995 PASS（failed 0、skipped 0、App所要29m13s、exit 0）。
- 実測した一過性事象: 最初の全体実行は`HostileInputAndFailureTests.App_owned_timeout_...`の直後にdotnet/testhostが応答停止し、15秒間CPU時間が全く増えないためbuild取消で終了した。同クラス単独では17/17 PASS（当該testは142ms）で再現せず、再実行した全体回帰も0 failed。原因は特定できていないため「解決済み」とは記録しない。
- 実測したもう1件: C05〜C07の絞り込み再実行（102件）で`ReleaseMatrixBuilderTests.Concurrent_candidate_builders_for_one_output_are_serialized_and_remain_valid`が1件失敗し、`C03_FAIL stage=output-commit code=C03_IO_OR_FORMAT`を出力した。同testの単独実行は1/1 PASS、全体回帰でもPASS。再現しないため無害とは断定せず、observed transientとして残す。

### V02

- 要求所有者の明示指示によりスキップ。fresh Windows clean-hostの起動、本人login、CH-01〜CH-06のいずれも実施していない。
- 開発hostの観測、fake認証、CLI help、contract testをCH成功の代用にしていない。
- traceabilityのCH-01〜06は`NOT_RUN_EXTERNAL_PREREQUISITE`のまま。公開は引き続きblocked。

### V03（最終レビュー）

- `implementation-status.md`をV01実測へ同期。最新artifact行を0.8.4の実bytes/hashへ更新し、R03を`PASS`、V01を995/995、V02を`SKIPPED_BY_INSTRUCTION`として記録。
- 文書の公開・clean-host・本人認証に関するclaimは未実施のまま。開発host観測を公開可否の根拠へ格上げしていない。
- 静的診断の見直しで、`build-platform-release-matrix.ps1`のfile copy関数がPowerShell自動変数`$input`へ代入していることを検出。pipeline enumeratorと同名のため非予約名`$sourceStream`へ改名（6箇所）。parse 0 error、C03 builder 18/18 PASS で回帰なしを確認。
- 上記は静的解析由来の予防的修正であり、observed transientの`C03_IO_OR_FORMAT`が解消したことを示す証拠ではない。因果関係は確認していない。
- `git diff --check` 成功、`version.ps1 verify` PASS（0.8.4、両project一致）、`DocumentationContractTests` 17/17 PASS。
- input workbookは全試験を通じて不変。生成物は`artifacts/`配下のみで、sourceへの意図しない変更はない。
- tag/push/公開、installer追加、保護機能無効化はいずれも未実行。
