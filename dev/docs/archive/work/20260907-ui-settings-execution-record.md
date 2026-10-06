# UI簡素化・設定保存 実行記録

## 承認・実行規則

- 開始日: 2026-09-07。基点: `0cdc351147ab2163de400244335081a9dc1dd629`。
- 対象計画: [実装プランv2](20260907-ui-settings-redesign-plan-v2.md)。D01〜D20のデフォルトを採用する最新の利用者指示によりG0通過。
- 最新指示は全タスクの実装、独立タスクの並列化、各タスク完了後の敵対的レビューと指摘反映・確認を要求する。後続依存タスクはそのゲート通過後に開始する。
- D17／T33の旧版管理方針を上書きする。全実装タスクの最後にKeep a Changelog形式のUnreleased概要・項目を追加し、その後PATCHを`0.8.4`から`0.8.5`へ一度だけ増やして最終版を再検証する。
- T33ではREADMEのみ先に同期し、CHANGELOGは最終F01へ移す。版上げ後に候補版を説明する文書・期待値を同期する。公開版`0.8.1`は変えない。
- commit／push／tag／公開、実AI、本人login、実学生sample利用の承認ではない。通常テストはfake／syntheticと隔離した設定保存先を使う。
- build／test／capture／packageは単一runnerで行う。レビューは実コード・実テストに基づき、架空の問題や最低件数を作らない。
- `REVIEWED`は当該タスクの実装・対象検証・敵対的レビュー・必要修正の確認完了。製品公開、本人walkthrough、全環境の成功を意味しない。

## 基点確認

- PowerShell 7.6.5 / .NET SDK 10.0.400 / 製品版0.8.4を確認。
- 開始時の追跡ファイル変更なし。旧計画書とv2計画書だけ未追跡。
- 変更前`DocumentationContractTests`: 17 passed / 0 failed。

## タスク状態

| ID | 状態 | 検証・レビュー結果 |
|---|---|---|
| T01 | REVIEWED | 文書contract 18/18成功（新test名前指定成功）。独立敵対的レビュー: 修正必須0。差分形式検証成功。AC035〜037/TR34〜36/ST-UC27〜29/C045〜047追加、未実装を明記 |
| T02 | REVIEWED | Wave1再検証115/115の対象。独立レビュー修正必須0。設定型・canonical往復・redacted ToString |
| T03 | REVIEWED | Wave2再検証486/486。厳密JSON/UTF8・atomic保存・IO拒否・旧file保持。独立レビュー0 |
| T04 | REVIEWED | Wave2再検証486/486。明示適用のheader再読込・失敗/取消/競合無変更。xUnit token規約2箇所を修正、独立レビュー0 |
| T05 | REVIEWED | ComboBox候補差替えで旧値書戻しを再現（初回2失敗）。通知順・同期中書戻し抑止を修正。Design関連44/44、Wave1再検証115/115。再レビュー指摘0 |
| T06 | REVIEWED | Wave2再検証486/486。reviewで既存auto欠落開始を検出し事前gate追加、positive fixtureとno-fallback期待を同期。再レビュー0 |
| T07 | REVIEWED | Wave1再検証115/115の対象。学生行単位ページ・元criterion参照・出力中dirty保持。独立レビュー整形のみ、反映済み |
| T08 | REVIEWED | 編集面再検証195/195内、InputPresentation53件。実binding/page/state test、独立review0 |
| T09 | REVIEWED | Wave2再検証486/486。実ListBoxの選択再同期修正とrealView回帰7件、T05 guard維持。再レビュー0 |
| T10 | REVIEWED | 設定統合351/351成功。初期読込取消後再試行の編集保持、decimal同値比較をreview指摘から修正。再review0 |
| T11 | REVIEWED | Wave1再検証115/115に既存UI回帰を含む。44 DIP下限・focus維持、独立レビュー修正必須0 |
| T12 | REVIEWED | 固定Fluent素材6件とLICENSE/NOTICE一致を独立照合。Wave2のcompiled XAML/画像回帰成功、指摘0 |
| T13 | REVIEWED | 設定統合351/351成功。keyboard fixtureのfocus対象を実ListBoxItemへ訂正、Local Tabscope追加。独立再review0 |
| T14 | REVIEWED | 通信中断後のpartial確認・回復。headless13件成功、独立review0 |
| T15 | REVIEWED | headless19件成功、独立review0。旧Window API/xUnit記法3build errorsを2箇所修正し再確認。44DIP/有限領域条件は維持 |
| T16 | REVIEWED | headless15件成功、独立review0。Prompt一覧順序・明示適用・局所scroll |
| T17 | REVIEWED | navigation最終181/181。PlaceholderText、入力保持、実Tab順を修正。最終独立review0 |
| T18 | REVIEWED | 主画面対象540/540成功。数値未反映の成功誤表示とDataContext再接続の一時null書戻しを修正。実binding/選択ID回帰、最終独立review0 |
| T19 | REVIEWED | 主画面対象540/540成功。設問別の未確定配点保持・エラー導線・微小decimal表示、移設先testを修正。最終独立review0 |
| T20 | REVIEWED | 主画面対象540/540成功。現在run/次回概要分離、同種エラーの対象保持、相対出力先拒否を修正。最終独立review0 |
| T21 | REVIEWED | 主画面対象540/540成功。durable/legacyゼロ配点、出力失敗時の確定値保持、未処理blank、ページ外overrideエラー移動・Enter処理を修正。最終独立review0 |
| T22 | REVIEWED | navigation最終181/181。同カテゴリopen同期、Results経由の次回定義、カテゴリ保持、適用中遷移後refreshを修正。追加8case成功B/失敗A、最終独立review0 |
| T23 | REVIEWED | `t23-fixed.trx` 275/275成功。manual templateのDataContext競合、Results行移動の未確定入力保持を修正。既知shell固定領域試験も通過。composition/shell最終独立review0 |
| T24 | REVIEWED | `t24.trx`関連67/67成功、新規WorkflowState12case。実shellで交互編集/保存/往復、run固定/停止、保存定義成功・失敗、前回結果保持。独立review0 |
| T25 | REVIEWED | `t25-27-reviewed.trx`関連74/74成功。全step/設定の警告、移設編集欄・keyboard・no-auto-login。結果listの初期focus条件を実装へ合わせ、操作検証維持。最終review0 |
| T26 | REVIEWED | 同74/74。必須状態textと狭小設定本文9面の末尾到達検査を補強。ナビ幅をStretchへ固定。1024x720/1180x800、760x600、scale2、100/530行・20,000件計算。最終review0、nativeは別 |
| T27 | REVIEWED | 同74/74内7E2E。実設定store/read/apply・OpenXML output/validation・partial/resume、元本不変とblank/zero。fakeは認証/AI/時刻、writer/checkpointは実装。独立review0 |
| T28 | REVIEWED | `t28-reviewed.trx`216/216。新8枚1440x1050生成、全8枚2回hash一致、最小1024x720の実pixel capture。独立review0。画像はsynthetic/fakeで実保存・native証跡ではない |
| T29 | REVIEWED | 8枚の生成日/寸法/用途/fake境界をgenerator・実生成と照合。再review0。画像dateと固定fake run時刻を分離 |
| T30 | REVIEWED | getting-started/index/settingsを現UIへ同期。適用失敗理由の表示先説明を修正。独立再review0、対象リンク/見出し契約4/4。全公開集合検査はT36 |
| T31 | REVIEWED | 機能/Custom/Promptを同期。Reference失敗とEMPTYの違い、resume app-major互換を実装に合わせ訂正。独立再review0。全公開集合検査はT36 |
| T32 | REVIEWED | Privacy/障害案内を実装へ同期。review指摘の入力→設定で旧出力先表示をproduction修正、適用失敗時draft保持/再読込中切替も回帰確認216/216、修正再review0。全体文書contractはT36 |
| T33 | REVIEWED | READMEを新UI/設定操作へ同期。T36文書契約21/21、最終独立review0。CHANGELOGはF01へ |
| T34 | REVIEWED | architecture/detailed-design/implementation-statusを実装済み責務へ同期。T32と共通の出力先表示指摘は同期修正216/216で反映確認。過去証跡・公開境界維持。全体文書contractはT36 |
| T35 | REVIEWED | `t35-reviewed.trx`4/4。AC37/TR36/STUC29/C47維持、実在ownerと限定証跡へ同期。現UIラベル期待を修正、独立review0。全体/public gatesは保留 |
| T36 | REVIEWED | `t36-current.trx`21/21成功。公開文書11件・画像8枚、設定境界・対象版・local links/anchors。独立review0。配布集合の追加assertionはT38へ |
| T37 | REVIEWED | `t37.trx`9/9成功。ZIP実publish/生成/再現性/展開起動とMSIX静的契約。公開20件の独立完全一致、大小文字余剰・重複拒否を補強、再review0。MSIX実物はT39、全経路一致はT38 |
| T38 | REVIEWED | `t38.trx`114/114、EXE生成/P02/P05成功、P06実機7/7・P07 `PASS_DEVELOPMENT`。文書20件/CLI/標準展開/移動/再起動/同時起動/Prompt設定を実検証、独立review0。利用者設定file未作成 |
| T39 | BLOCKED | 自動回帰はCore190＋App1702＝1892/1892成功、skip0。旧fixtureの不整合1件を修正し126/126・独立review0後に全体再実行。MSIX実物PASS_MECHANISM。追加native観測はUIA行識別で停止、本人/Narrator/隔離user保存は未実施。下記の境界を参照 |
| F01 | REVIEWED | Unreleased概要・Added/Changed/Fixedを更新。計算式位置の旧記述と日本語ラベルの不足を修正し再review0、版verify成功。公開0.8.1節をHEADと改行正規化比較して不変を確認。T39未達は維持 |
| F02 | REVIEWED | PATCH 0.8.4→0.8.5を一度だけ反映。現行候補文書・独立期待値を同期、文書reviewの2点を修正し再review0。2026-09-08 JSTの最終runでlocked restore/Release/版verify/15 assertions、文書21/21、実EXE7/7、実ZIP3/3、MSIX PASS_MECHANISM、全体1892/1892・skip0。証跡と実物のhash再照合・独立最終review0。T39/G4/公開は未達のまま |

## F02最終候補の検証結果（2026-09-08 JST）

- 対象: 製品`0.8.5`（UNRELEASED・未公開）、要求v4.6。公開済み`v0.8.1`は変更していない。
- 実行時間: 2026-09-08 00:00:54〜00:29:45 JST（UTC: 2026-09-07 15:00:54〜15:29:45）。計画承認日・画像生成日の2026-09-07とは区別する。
- 集約正本: F02 summary.json (`artifacts/test/ui-settings/f02/run-925e1f9cacdf420ea4ed23e9567e45eb/summary.json`) (当時のローカル生成物。Git管理外)、判定`PASS_SCOPED`。各TRXの相対pathとSHA-256を記録し、終了後に実fileのhashを再照合した。
- locked restore、Release build、製品版verify、版管理toolの15 assertionsが成功。publish後のApp/Core DLLはAssemblyVersion／FileVersion `0.8.5.0`、ProductVersion `0.8.5`（SDK付与のcommit metadataあり）と一致。

| 検証 | 最終結果 | 範囲 |
|---|---|---|
| 文書契約 | 21/21成功 | 公開11文書、8画像の来歴、設定保存境界、候補／公開版、local link／anchor、公開20項目の集合。下記1892件の内数で、再加算しない |
| 実EXE P06／P07 | 7/7成功、`PASS_DEVELOPMENT` | 0.8.5の最終EXEで標準展開・初回／再起動・同時起動・cache復旧・移動・起動引数／読込Prompt設定・限定障害とdata不変を検証。追加native全画面確認やclean-hostの代替ではない |
| 実ZIP | 3/3成功 | publish／package／展開起動／再現性と公開docs・画像・LICENSE・同梱CLIを検証 |
| 非公開MSIX | `PASS_MECHANISM` | 0.8.5.0の開発用package作成・展開・integrity／policy検証のみ。署名・install・一般配布は未実施 |
| CI同等の自動回帰 | Core190＋App1702＝1892/1892成功、失敗0／skip0 | 実学生sample構造・別実行ZIP・P06の3クラス除外を維持。既に生成したP02成果物のopt-in検査を含む。実AI／再計算／実データの無効経路も含み、live成功へ読み替えない |

### 最終ローカル配布物

以下は一般公開assetではなく、ローカルで生成した未署名候補である。終了後に容量・SHA-256・sidecarの完全一致を再確認した。

| 成果物 | Bytes | SHA-256 |
|---|---:|---|
| `artifacts/package/StudyReportEvaluator-win-x64.exe` | 283,410,362 | `E691A8D72AFFA069F715096D9A21B2DD68FB1A80B70D2545340D32AD0A614B25` |
| `artifacts/package/StudyReportEvaluator-win-x64.zip` | 156,499,920 | `028CE7A53AE63EA7168E654F8AD303D5EF9B52E052DB468CFD78B802A6BC3241` |
| `artifacts/package/mechanism/StudyReportEvaluator-win-x64.unsigned.test.msix` | 155,798,883 | `83083F236EC43D3AC5D7BAD6218A7A0857F7E7E6223DBD9B708BC3DF3A3315AB` |

- 検証前後のsource／公開文書fingerprintはともに`8684DB55B0BF34EF3EDBDFC20AABB257D2A6378ED0DE43155A3C59B1BC55764B`。最終照合でも一致した。これは一時検証手順の明示scopeであり、P07の全Git inventory hashと同じ方式ではない。事後更新する本実行記録・ignored出力はこのscopeに含まれない。
- P07は実行時のdirty checkoutを固定して検証した開発証跡。本実行記録の追記後の全Git inventoryやclean-source公開資格を証明するものではない。最終配布bytesと公開文書・コードは事後変更しない。
- 固定SDK／NuGet依存／lock、Core source、solution、CI／公開workflowはHEADとの差分なし。HEADは開始時と同じで、indexは空、実学生sampleは未追跡を維持。commit／push／tag／公開は行っていない。
- 実利用者の`setting.txt`は検証前後とも不存在を確認。実利用者設定を作成・変更しない。画像8枚は0.8.4でのT28生成物を保持し、F02で再生成していない。
- 0.8.4のEXE／MSIX証跡はbaseline-0.8.4 (`artifacts/test/ui-settings/f02/baseline-0.8.4/`) (当時のローカル生成物。Git管理外)へbytes不変で退避し、旧結果を0.8.5の実物証拠へ転記していない。
- 独立最終レビューは既存証跡と範囲の照合で追加指摘0。F02だけを`REVIEWED`とし、追加native FAIL、本人確認等の外部前提、T39 `BLOCKED`、G4未完了、公開未達を維持する。

## 未実施・公開境界

- T18〜21のheadless対象検証は`artifacts/test/ui-settings/t18-21/t18-21-reviewed.trx`で540/540。全体試験ではない。旧shell固定領域試験はT23へ分離後、`artifacts/test/ui-settings/t23/t23-fixed.trx`の275/275で通過を確認。
- 全体回帰は`artifacts/test/ui-settings/t39/reviewed/`の2 TRXで1892/1892成功（Core190、App1702）。CIと同じ3クラス除外（実学生sample構造、別途実行するZIP、P06）を使用し、生成済みP02 artifact検査はopt-inしてskip0。実AI等の無効経路の成功をlive実行へ読み替えない。
- locked restore、Release build、版管理verify、version toolの15 assertionsは成功。初回全体のExecutionViewTests失敗は、設定済みExecutionと未読込Inputを組み合わせたfixtureを正規の合成Excel読込・Execution遷移へ修正。開始可否・出力先復元・no-autoの期待を維持して再検証した。
- T26のheadless実測、T37の実ZIP、T38の実EXE/P06、開発MSIX実物は各限定範囲で成功。署名・MSIXインストール・clean-source公開evidenceは生成していない。
- 追加nativeプローブは`artifacts/test/ui-settings/t39/native-final-attempt.json`にFAILを保持。120 DPI、実client 1475×1000 pixel＝1180×800 DIP、合成入力読込、正常終了、入力・EXE不変、利用者setting.txt非作成だけを確認。UIA行IDの一意識別に失敗し、4画面・5カテゴリ・1024×720・実keyboardの追加検証は未完了。3回の試行後に再試行を停止し、成功へ書き換えない。
- 本人walkthrough4項目（入力/30・10配点、設定保存と復帰、再起動・明示適用、override未保存の識別）、Narrator、隔離利用者環境でのnative保存は`NOT_RUN_EXTERNAL_PREREQUISITE`。環境変数差替えをOS利用者の隔離保証とせず、実利用者設定へ書き込まない。
- 利用者不在時は自律判断で続行する指示を受領。T39をBLOCKEDのまま残し、承認済みF01/F02を進める。G4・全タスク完了・公開PASSは付与しない。
- clean-host CH-01〜06、本人認証、実AI、Office再計算は今回の成功として扱わない。