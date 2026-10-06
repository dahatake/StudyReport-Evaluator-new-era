# UI layout and settings contract — v4.6

| 項目 | 契約 |
|---|---|
| 要求正本 | [requirements-definition.md](../../docs/requirements-definition.md) v4.6 / 2026-09-07、§9.1・11.4〜11.8・12.2・14・15 |
| 決定の出典 | [UI・設定保存プランv2](archive/work/20260907-ui-settings-redesign-plan-v2.md) §4〜8、D01〜D20。要求所有者の2026-09-07の後続指示でデフォルト採用・全実装を明示承認 |
| T01の範囲 | 最小実装契約を確定した履歴。T01時点の実測・build・testはNOT_RUN |
| T26の範囲 | 親担当の2026-09-07実行TRXで、下記2ファイルの全27ケースがPassed（headless UI＋ページ計算）。通常2寸法・狭小scale 1／2の実測を§5へ反映。今回の文書更新では再実行なし |
| T27の範囲 | 実設定保存／復元・実ファイル出力を扱うローカルE2Eが7/7 Passed（§5.3）。native UI・実認証／実AI・別process再起動の確認ではない |
| T28／T36の範囲 | 0.8.4のT28は8画像1440×1050・2回hash一致・最小frame検証を含む216/216でREVIEWED。T36は文書／画像contract 21/21でREVIEWED。0.8.6での再生成・実保存・nativeの証拠ではない |
| 残る実測 | T39追加nativeは3試行後に停止し、最新CONTROL_ID_PREDICATE_NOT_UNIQUEでFAIL。120 DPI／1180×800 DIP等の部分観測のみ（§5.4）。Narrator・本人walkthrough4項目・隔離利用者native保存はNOT_RUN_EXTERNAL_PREREQUISITE |
| 追跡 | AC-016／017／018／035〜037、TR-17／18／34〜36。[traceability](traceability.md)、[SystemTest](../../tests/SystemTest-prompt.md) ST-UC-12／13／27〜29 |
| 版と公開 | 製品正本は親担当が0.8.5未公開候補へPATCHし、その後のauto model対応で0.8.6へPATCH済み。F01はREVIEWED、公開済みはv0.8.1 ZIP。D17の当初上書きとT01の版・CHANGELOG非変更は履歴。後続の自律続行指示でT39 BLOCKEDのままF01／F02を進めるが、全タスクDONE・署名・公開PASSにはしない |

本書は要求から実装へ渡す契約と、§5に明示した対象に限る検証記録である。全UI・全要件の動作合格、改善率、native実DPI・本人walkthroughの証跡へは拡張しない。元プランの承認待ち表記は履歴として保持し、要求正本§22の後続承認を優先する。

T01〜T38はREVIEWED、T39はBLOCKED。F02の最終0.8.6再検証は本同期時点では親担当で未完了、以後は[実行記録](archive/work/20260907-ui-settings-execution-record.md)の最新F02欄を参照する。以下の0.8.4での測定とT01の静的契約履歴を、新しい版の試験成功へ転記しない。

## 1. T01時点に静的に確認した基点（履歴）

| 実在するsource | 確認した宣言 | 証明しないもの |
|---|---|---|
| [MainWindow.axaml](../../src/StudyReportEvaluator.App/Views/MainWindow.axaml) | `MinWidth="1024"`、`MinHeight="720"`、`Width="1180"`、`Height="800"`。`CurrentStepContent`に4ステップとSettingsのDataTemplate。固定header／footerと本文だけの`ShellScrollViewer` | 実ClientSize、非スクロール、設定画面の表示成功 |
| [Accessibility.axaml](../../src/StudyReportEvaluator.App/Styles/Accessibility.axaml)とMainWindowのlocal styles | ButtonのMinWidth／MinHeight 44、入力類のMinHeight 44／FontSize 14。共通step-buttonのMinHeight 48をshellのlocal styleで44へ指定 | 全Controlの実高さが44で固定されること、Compact変更後の収まり |
| [要求正本](../../docs/requirements-definition.md) §11.5 | 通常最小1024×720 DIP、初期1180×800 DIP、例外760×600 standalone／200%表示 | 実測した寸法・表示件数・native DPI・WCAG適合認証 |

上表はT01時点（2026-09-07）に確認したfile宣言と承認要求だけを出典とする履歴であり、現行の実行結果は§5と分ける。T01で実測・testを実施したことにはしない。未測定のcommit／artifact hash、asset revision、API互換性、成功件数を補わない。外部資料の調査履歴は元プラン§3にあり、本書で再取得・再測定したとは扱わない。

## 2. 主画面と設定

2026-09-18のExecution／Results追加: 共有の`JobCostView`を進捗・結果一覧と同じ内容領域のタブへ置き、`ExecutionCost`／`ResultsCost`のscopeで`CostTabs`・`CostDetailsTextBox`・`LogTextBox`・`AutoFollowCheckBox`・`OpenLogButton`・`OpenDirectoryButton`のAutomation IDを一意化する。開始／停止と結果の主要操作は固定位置のまま、コスト詳細とログは局所scrollへ収める。ログ表示は最新200行で、追従offの間は表示位置と選択を奪わない。`JobCostViewTests`でheadless確認済みで、追加後のnative DPI／Narrator再測定は未実施。

2026-09-17のExecution追加: `ExecutionPickResumeCheckpoint`、`ExecutionResumeInterruptedRun`、`ExecutionValidateResumeCheckpoint`、`ExecutionApplyCheckpointInput`、`ExecutionApplyCheckpointModel`、`ExecutionResumeFindings`、`ExecutionResumeFindingDetail`。再開元編集→確認→項目一覧／全文詳細を局所scrollに置き、Start／中断を固定する。再開準備はAIを開始しない。`ResumeWorkflowTests`でpicker取消と繰返しwindow closeをheadless確認済み。追加後のnative DPI／Narrator／全レイアウト再測定は未実施で、過去の§5の成功を流用しない。

2026-09-30のExecution追加（§11.12）: 右列の進捗領域を`ExecutionProgressBody`（上3：`ExecutionProgressScroll`の局所scroll、下4：`ExecutionLivePreview`）へ分け、「実測」（`ExecutionDurableProgress`）の直下に`ExecutionLivePreviewHeading`・`ExecutionLivePreviewList`（`LivePreviewRowsList`、仮想化した全件一覧）・`ExecutionLivePreviewDetail`（`LivePreviewDetail`、読取専用の全文）を置く。TabIndexは一覧73・詳細74で、予約path（final／partial）は75／76へ移した。`LivePreviewViewTests`で1180×800・1024×720の収まり、命名、500行の仮想化をheadless確認済み。native DPI／Narratorは未実施。

固定の「1 入力 / 2 採点設計 / 3 実行 / 4 結果」を維持する。設定は同じwindowの独立した内容画面で、第5ステップ、modal、drawerではない。元ステップと同じ対象を保って1操作で開き、元の対象とfocusへ戻る。同じ入力欄を二重配置しない。

| 画面 | 主画面の最小責務 | 設定で扱う詳細 |
|---|---|---|
| 入力 | picker／path、読込、sheet／質問行1・2／回答範囲、対象設問、有効化、主回答列、設問text、必須エラー | 候補再適用、補助列、設問名、複製、並替え等 |
| 採点設計 | base／special／similarity係数、設問Points、exact合計・過不足、均等配分、有効評価の概要、読込Prompt件数 | 定義名・revision・丸め、通常evaluator／criterion CRUD・range・weight、固有評価、Prompt本文・明示適用 |
| 実行 | 本人llogin開始／取消／明示状態確認、実効modelとその上限の既知／不明とauto、実効出力先、新規／再開・partial、開始／停止、実progress・技術エラー | model変更、並列度、明示出力先、runtime診断。新timeout欄は作らない |
| 結果 | final／partial、完了／一部失敗／取消、件数、ページ一覧、元行の詳細・override・別名出力 | 結果とoverrideは設定へ移さない |

設定のカテゴリは「共通」「入力詳細」「通常評価」「固有評価」「読込Prompt」の5つ。共通内の定義情報はDesign所有のままで、6カテゴリにしない。対象なしは理由を示し、カテゴリの位置を変えない。警告全文は設定を含むshellの独立領域で常時nonblocking表示する。

既存FluentThemeと必要な静的iconだけを使用し、日本語ラベル・focus・contrastを維持する。素材6点のrevisionとLICENSE／NOTICEはT12で確定・REVIEWED済み。別themeファイル・新依存・汎用UI基盤を作らない。

## 3. 通常サイズと到達性例外

- 最小1024×720 DIP／初期1180×800 DIPを引き上げない。Window指定と実ClientSizeを分ける。
- 通常サイズは初期offsetで警告全文、現在地、主要操作、有効値、前後移動が完全包含され、外側スクロール不要・horizontal overflowなし。外側ScrollViewerがある場合は`Extent <= Viewport`を確認する。scrollbarをDisabledにするだけ、clippingするだけでは合格にしない。
- 設問・結果はコンパクト一覧＋ページ切替＋結果元行番号への移動。前／次、現在範囲、全件数を表示する。表示件数は残領域と実際の行高から求め、領域拡大時に増やせることを検証する。未実測の5件／6行を固定しない。
- 空／1件／ページ境界前後／最終ページ、追加・削除・複製・並替え・リサイズで範囲を補正し、残る対象はIDで保持する。削除時だけ隣接へ移す。有限高さとvirtualizationを保ち、全件Control生成を避ける。
- 多数evaluator／criterionは選択詳細で扱い、全カードを積んだまま通常表示の例外へ逃がさない。複数エラーも件数・現在対象・次への移動を示す。
- 長い設問text／Prompt、全文path、dropdown候補の局所scrollは例外。全文へkeyboardで到達できることを確認する。
- 760×600 standalone／200%表示はreflowと表示行数削減を優先し、不足時だけ本文の縦scrollを許容する。固定領域で操作・focusを覆わない。この成功を通常サイズの非スクロール達成へ算入しない。
- 本文・入力14 DIP、主操作target最小44 DIPを維持する。MinHeightは下限。余白4／8／12／16を基準に重複を削り、文字縮小で密度を偽らない。
- Tab／Shift+Tab／Enter／Space、カテゴリ切替と設定終了後のfocus、日本語Accessible Name、安定IDの一意性を確認する。動的IDは対象ID＋操作名等とし、ページ連番・表示名を使わない。固定ナビ→内容→前後操作のTab順を検証する。

### 3.1 T26の検証方針（実行結果は§5）

- 実際の`MainWindow`、4つの主View、`SettingsView`と4つの詳細Viewを使用する。HTML代替画面、全件分の独自編集Control、汎用paginationハーネスは作らない。T23の[MainWindowSettingsTests](../../tests/StudyReportEvaluator.App.Tests/UI/MainWindowSettingsTests.cs)の構成・入力方法に合わせ、T26内の境界検査と小さなfixtureを共有する。
- 通常条件は指定`Width`／`Height`と実`ClientSize`を別に照合する。shellおよびDesign／Results／Settingsの本文`Extent <= Viewport`、初期offset、実Controlの完全包含を確認する。横方向はwindow境界だけでなく途中のclip／ScrollViewerのviewportも検査する。スクロールバーのDisabled設定を合格根拠にしない。
- 警告の全文、折返しに必要な測定高さ、固定ナビとfooterの位置を、ステップ遷移・カテゴリ変更・本文scroll後にも照合する。主操作の44 DIPと入力font 14 DIPを維持する。長い名前が自然に収まる場合に、不要なscrollまで強制しない。
- 旧Input／Executionの外側scroll検査は、Inputのページ末尾到達とExecutionの局所本文scrollへ移す。旧Resultsの全行override編集検査は、学生ページ・選択学生のcriterion一覧・単一override編集欄へ適合させる。起動エラーの縦scroll回帰は残し、多数criterionとPrompt一覧では実際のoverflowと末尾Controlを検査する。
- Input／Design／Resultsは実`ListBox.ItemsSource`と元collectionの参照、ページ全行の実container、viewport完全包含、有限高さ、`VirtualizingStackPanel`を照合する。100／530行はU04の合成summary、metadataはX02／U01の一時合成workbookを再利用する。`CreateSampleLike`も合成生成器であり、`sample/`の読込ではない。全20,000件のVM／ControlやAI処理を作らない。
- リサイズはPageSizeの手動書換えではなく実windowを通常2寸法間で変更し、容量の増加・元サイズへの復帰・最終対象の参照／ID保持を検査する。入力詳細の実削除ボタンから末尾削除→空一覧→追加を行い、主Input／Designへ戻ったページを確認する。Resultsでは末尾ページから空VMへの再bindで古いControlが残らないことを確認する。
- 長い設問・名前・出力path・評価説明・Custom／固有／読込Promptは合成値だけを使用する。共通の編集／保存定義／診断、通常・固有評価のBasic／Prompt表示も切り替える。Ctrl+Home／Ctrl+Endのcaretと必要時の実scroll、設問dropdownのF4／End／Enter、実ListBoxItemのEnd、Tab／Shift+Tab、設定から戻るfocusを確認する。保存・認証・実行ボタンを自動起動しない。
- 760×600は4主ViewとSettingsのstandalone条件、さらに**テスト内だけ**最小制約を外したshell条件を区別する。scale 1／2で横切れと末尾操作への到達を検査し、本文縦scrollは許容するが強制しない。製品の最小サイズは変更しない。
- 20,000件の追加試験は、[InputPresentationTests](../../tests/StudyReportEvaluator.App.Tests/UI/InputPresentationTests.cs)と同じproductionの`CalculateQuestionPage`を直接呼ぶ計算試験。最終範囲→容量変更→削除時のclamp→空の境界を扱う。既存の19,999／20,000／20,001・整数上限ケースも維持する。Design／Resultsの20,000件UI性能を検証したことにはしない。

## 4. 保存・復元と状態の所有

| 境界 | 最小契約 |
|---|---|
| 保存先／形式 | LocalApplicationData配下`StudyReportEvaluator/setting.txt`、UTF-8 JSON、設定schema整数1。共通設定＋任意の採点定義1件 |
| 明示保存 | 有効編集は次回draft、diskは明示保存だけ。fileなしは初回保存まで未作成。旧fileの先行削除／直接切り詰めなし、同directory tempのwrite／flush／close後にatomic置換 |
| 保存失敗 | 型・schema・未知項目・既存validatorで検証。破損・未知版・IO拒否で元fileを保持し通知、offline編集継続。現在draftを失わず、自分のtempだけ後始末 |
| 保存状態 | 未保存／保存中／保存済み／保存失敗を分離。保存中の再編集後は未保存のまま。同一画面二重保存を防止、別processは最後の成功が優先 |
| 平文／非保存対象 | 明示保存時はheader由来設問text・貼付Promptも平文に含まれ得る。入力path／bytes・回答行の自動収集・AI結果・credential／login・run／checkpoint・未適用Prompt一覧・UI選択／履歴は保存しない |
| 保存定義の適用 | Excel読込後の明示操作だけ。保存headerのmetadata・sheet・行・列・全定義を検証し、成功時だけInput／Designを一括更新。失敗・取消は現在状態とfile無変更。ID／順序／text／Prompt／配点／canonical hash、Imported Prompt一覧を保持。run中は適用不可 |
| 出力先（D20） | 今回の明示編集 → 保存済み指定 → null。明示absolute pathは再起動・入力変更後も保持。空欄はnullへ戻す操作で、nullの場合だけ入力隣接resultを都度算出。自動resultを明示指定として保存しない。利用不可でno fallback、復元だけでdirectoryを作らない |
| model | 保存希望IDと実効選択を分離。明示確認の候補にある場合だけ実効選択。不在は未選択でno fallback、確認失敗だけで希望IDを消さない。希望未指定の初回既定と固定autoの独立検証を維持 |
| draft | Input／Designの既存draftが所有し、Settingsは編集面を束ねる。最新編集元を同期し古いDesign参照で上書きしない。値・対象ID・ページ・カテゴリ・入力途中を同一起動中に保持 |
| 現在run | 開始request／immutable snapshotは固定。次回編集を混ぜず、実行中に再構成しない。全画面で進捗・停止・予約pathを維持。設定中完了は結果通知だけで強制遷移しない |
| 再開／前回結果 | 同じ入力・定義の往復で再初期化しない。変更時の再開指定は再確認または解除し理由を表示。checkpoint admissionは不変。前回結果／overrideを次回draftから分離し、ページ後も元collectionに保持 |
| 禁止する自動処理 | 遷移、設定読込・保存・適用、Prompt適用、login完了から認証確認・login・AI runを開始しない |

単体VM・自動testは実利用者設定へアクセスせず、一時absolute pathを注入する。保存拒否は排他file、file-valued親path等の実際に失敗する条件で検証し、WindowsのReadOnlyディレクトリだけを拒否の証拠にしない。

## 5. 実測欄

T26／T27の測定欄は、親担当の2026-09-07（製品0.8.4）実行証跡`artifacts/test/ui-settings/t25-27/t25-27-reviewed.trx`の`UnitTestResult`・`StdOut`・`Counters`を照合した記録を保持する。combined runは`total=74`、`executed=74`、`passed=74`、`failed=0`、`notExecuted=0`。**全成功は、このTRXに収録された74ケースに限る**。T26の27ケースとT27の7ケースはその内数であり、74ケース全てがlayout検査という意味ではない。全suite・native・公開gateのPASSへ拡張しない。

本書は開発文書であり、上記パスはGit対象外の**ローカルartifact**をcode spanで参照する。TRXは配布物には同梱せず、公開checkoutや配布先での存在を保証しない。生ログ・個人path・host識別子は本書に転記しない。未実測は`NOT_RUN`、実装・観測手段がない場合は`BLOCKED`とし、数値・合格件数・証跡hashを推測で埋めない。

通常2寸法の一覧は、先頭ページ（`PageIndex=0`）の`pageItems = realized`を表示件数とする。Input／Designは**13設問**、Resultsは**100合成行**のfixtureで測った値であり、固定表示件数・製品上限ではない。Settings値は長文テストの全5カテゴリ／9内部ページから採用した。寸法・行高はDIP、scaleはheadlessの`RenderScaling`であり、T26ではnative DPIを測定していない。T39の部分観測は別行・§5.4に分離する。

| 条件 | 実ClientSize／scale・DPI | 内容領域・実際の行高／表示件数 | Extent／Viewport・完全包含・到達性 | 判定／証跡 |
|---|---|---|---|---|
| 通常shell 1024×720 DIP、4ステップ＋設定 | 1024×720／scale 1。指定値と一致 | Input 3件・行高44／Design 5件・行高48／Results 3件・行高44。Settings本文1008×402 | ShellはExtent＝Viewport＝1008×506、offset＝(0,0)。Design／Resultsの外側本文も同寸法。Settings本文もExtent＝Viewport・offset＝(0,0)。対象Controlの完全包含・横切れなし | PASS（headless）。§5.1の通常shell・長文テスト、同TRX |
| 通常shell 1180×800 DIP、4ステップ＋設定 | 1180×800／scale 1。指定値と一致 | Input 5件・行高44／Design 6件・行高48／Results 5件・行高44。Settings本文1164×482 | ShellはExtent＝Viewport＝1164×586、offset＝(0,0)。Design／Resultsの外側本文も同寸法。Settings本文もExtent＝Viewport・offset＝(0,0)。対象Controlの完全包含・横切れなし | PASS（headless）。§5.1の通常shell・長文テスト、同TRX |
| 長文・path・dropdown局所scroll、設定全カテゴリ・内部tab | 1024×720／1180×800、scale 1 | 4主画面＋設定5カテゴリ・9内部ページ（共通3／入力詳細1／通常評価2／固有評価2／読込Prompt1） | 通常外側の非スクロール・対象Controlの完全包含を維持。許可された局所scroll・dropdown末尾・keyboard／focusを確認 | PASS（headless）。§5.1の長文・日本語pathテスト、同TRX |
| 760×600 standalone（4主View＋Settings）例外 | 760×600、scale 1／2。各条件で指定値と一致 | Settings本文760×496。各主Viewの個別件数・行高はTRXのstandalone出力を参照し、通常fixtureの件数を流用しない | Settings本文はExtent＝Viewport＝760×496、offset＝(0,0)。各Viewの末尾操作・局所scroll到達と横切れなしを確認。本文縦scrollは許容するが強制しない | PASS（headless例外）。§5.1の4主Viewの狭小テスト・standalone Settings、同TRX |
| 最小制約をテスト内だけ解除した760×600 shell | 760×600、scale 1／2。**最小制約の解除はテスト内だけ** | Settings本文744×416。4主画面・全5カテゴリ／9内部ページ | ShellはExtent 744×520 ＞ Viewport 744×370、4主画面の末尾offset＝(0,150)。本文縦scroll後も警告・ナビ・footerを維持し末尾操作へ到達 | PASS（headless例外）。§5.1の狭小shell、同TRX。製品の最小サイズ変更・通常非スクロール達成を意味しない |
| scale 2相当headless、reflow／本文縦scroll例外 | 760×600／scale 2。`derivedPixels=1520×1200`は計算値 | 上記standalone・最小制約解除shellのscale 2ケース | 同じDIP指定でのreflow・到達性を確認。native物理作業領域・Windows実DPIの測定ではない | PASS（headlessのみ）。上記ケースの再掲で、成功件数に重複加算しない |
| native Windows実DPI、keyboard／Narrator（追加観測） | 120 DPI、実client 1475×1000 pixel＝1180×800 DIP | 合成入力読込を確認。4画面・5カテゴリ・1024×720の追加検証は未完了 | UIA行識別の一意predicateで停止。実keyboard／focus・警告全文／全操作の完全包含は未検証、NarratorはNOT_RUN_EXTERNAL_PREREQUISITE | FAIL（T39、3試行後停止）。`artifacts/test/ui-settings/t39/native-final-attempt.json`、CONTROL_ID_PREDICATE_NOT_UNIQUE |
| 100行／530行synthetic、先頭・次・最終ページ・virtualization | 760×600／scale 1 | 一覧領域736×253、各観測ページ5件・行高44、`pageItems=realized=5`。最終PageIndexは100行で19、530行で105 | Results本文はExtent＝Viewport＝760×600、offset＝(0,0)。有限高さ・virtualization・元collectionとの参照を確認 | PASS（headless）。§5.1のResults virtualization 2ケース、同TRX |
| 画像2回一致（0.8.4生成履歴） | 通常8枚・各1440×1050 pixel | 全8枚を2回生成してhash一致。最小1024×720の実frame検証も別case | synthetic／fake説明画像。通常画像の寸法をnative DPIや保存／出力成功へ転記しない | PASS（T28、関連216/216、`artifacts/test/ui-settings/t28/t28-reviewed.trx`）。T36 contractは自身の21/21 |
| 本人walkthrough4項目・隔離利用者native保存 | NOT_RUN | NOT_RUN | 入力／30・10配点、保存と復帰、再起動・明示適用、override未保存の識別は本人確認待ち | NOT_RUN_EXTERNAL_PREREQUISITE（T39）。headless／新instance E2Eで代替しない |

通常2寸法間の実windowリサイズでは、Input 3→5、Design 5→6、Results 3→5と**容量**が増加し、元サイズへの復帰・最終対象の保持がPassed。末尾ページからの削除→空→追加、空Resultsへの再bindも§5.1の該当ケースがPassed。容量と、末尾ページに実在する表示項目数は区別する。

20,000件のページ境界は全Controlを作らない計算testで、capacity 1／4／7／13の**4/4ケースがPassed**（同TRX、T26の27ケースの内数）。100／530行はfixture規模であり製品上限ではない。20,000件の計算成功を実画面の性能実測にしない。

### 5.1 検証済みT26テスト一覧

以下15テストメソッド、パラメーター展開後**27/27ケースのPassed**を同TRXで確認した。UIケースはheadless、20,000件のケースは計算のみ。列の寸法・件数は対象fixture条件であり、製品全体の合格を意味しない。

| ファイル | テスト名 | 狙い／指定条件 | TRX判定（ケース数） |
|---|---|---|---|
| [ResponsiveLayoutTests.cs](../../tests/StudyReportEvaluator.App.Tests/UI/ResponsiveLayoutTests.cs) | `Normal_shell_contains_every_step_without_body_scroll_at_actual_client_size` | 通常1024×720／1180×800、4画面・固定領域・完全包含 | Passed 2/2 |
| 同上 | `Input_view_reaches_the_last_question_page_at_a_narrow_viewport` | standalone760×600、scale 1／2、全設問の末尾ページ | Passed 2/2 |
| 同上 | `Design_view_reaches_the_last_question_page_at_a_narrow_viewport` | 同条件、設問ページと再検証操作への到達 | Passed 2/2 |
| 同上 | `Execution_view_keeps_actions_reachable_with_local_scroll_at_a_narrow_viewport` | 同条件、局所本文・実効path／partial path・固定操作 | Passed 2/2 |
| 同上 | `Results_view_reaches_last_row_and_last_criterion_at_a_narrow_viewport` | 同条件、100合成行・末尾学生／criterion・単一編集欄 | Passed 2/2 |
| 同上 | `Long_japanese_names_and_paths_do_not_widen_the_layout` | 長い設問名・出力path・未読込の直接入力path | Passed 1/1 |
| 同上 | `Results_rows_stay_virtualized_after_the_responsive_reflow` | 100／530合成行、先頭／次／末尾ページの実Controlと詳細欄 | Passed 2/2 |
| 同上 | `Startup_error_window_scrolls_vertically_and_fits_its_minimum_width` | 既存起動エラーの実scrollと閉じる操作を維持 | Passed 1/1 |
| [CompactWorkflowLayoutTests.cs](../../tests/StudyReportEvaluator.App.Tests/UI/CompactWorkflowLayoutTests.cs) | `Long_content_in_four_steps_and_every_settings_category_keeps_normal_body_contained` | 通常2寸法、長文、全5カテゴリ・9内部ページ、keyboard／focus | Passed 2/2 |
| 同上 | `Narrow_scaled_shell_reaches_last_operations_without_moving_warning_or_navigation` | 狭小shell、scale 1／2、本文末尾・固定領域・Tab往復 | Passed 2/2 |
| 同上 | `Standalone_settings_at_760_by_600_keeps_all_categories_and_last_actions_reachable` | Settings単体760×600、scale 1／2、全カテゴリの末尾操作 | Passed 2/2 |
| 同上 | `Resizing_real_main_lists_grows_capacity_and_keeps_the_last_selected_item` | 実windowの拡大／縮小、Input／Design／Resultsの容量・参照保持 | Passed 1/1 |
| 同上 | `Deleting_the_last_page_through_mapping_settings_repairs_main_pages_and_empty_state` | 実削除→末尾補正→空Input／Design→追加 | Passed 1/1 |
| 同上 | `Empty_results_replace_a_real_last_page_without_leaving_stale_controls` | 末尾結果から空VMへ再bind、古い行／詳細の非残留 | Passed 1/1 |
| 同上 | `Twenty_thousand_item_page_math_resizes_clamps_after_deletion_and_empties_without_UI` | 20,000件の計算のみ。capacity 1／4／7／13、UI・workbook・AI生成なし | Passed 4/4 |

T23の`MainWindowSettingsTests`、既存Input／Design／ResultsのPresentation／Viewテスト、T25の警告・アクセシビリティ回帰を置き換えない。required profileの対象path追加は親担当の既定計画に従い、T26ではprofileファイルを変更しない。

### 5.2 証跡の読み方と残る範囲

- T26／T27測定欄は親担当の完了済みTRXを照合した記録を保持する。F02文書同期も端末・build・test・native測定を新規実行していない。最終版の再検証は親担当の単一runnerと実行記録へ接続する。
- `RecordLayout`は実行時だけ、指定window寸法、実ClientSize、RenderScaling、本文サイズ・margin／spacing、Extent／Viewport／offset、PageIndex／容量／総数、ページ項目数・実現container数・実行時の行高をテスト出力へ記録する。本文・表示名・実path・画像を新規証跡ファイルへ出力しない。
- `derivedPixels`はClientSize×RenderScalingの**計算値**であり、native screenshotの実pixel寸法でもWindowsの実DPI測定でもない。scale 2でも同じDIPを指定する条件なので、物理作業領域が半減した環境の収まりまで証明しない。
- 本書のPassedは各テスト全体の結果と実出力を照合した判定である。再測定でも、途中の観測行、エディター診断、source上の44／48等の宣言だけでPASSや表示行数を記録しない。実装不具合が出た場合は失敗箇所・実境界・最小のproduction修正案を親担当へ渡し、期待値を緩めて隠さない。
- 画像2回一致はT28で完了しており、NOT_RUNに戻さない。T39の追加nativeは部分観測でFAIL、Narrator・本人walkthrough・隔離利用者native保存はNOT_RUN_EXTERNAL_PREREQUISITE。headlessやHTMLモックで代替しない。性能profileはT26のTRXの検証対象外。T27の保存／復元E2Eは§5.3の範囲で完了しており、未実施項目に含めない。20,000件のControl生成時間・メモリ量は測定対象外。

### 5.3 検証済みT27設定E2E

[SettingsWorkflowSystemTests.cs](../../tests/StudyReportEvaluator.App.Tests/E2E/SettingsWorkflowSystemTests.cs)の**7/7ケースがPassed**（パラメーター展開後、同TRXの74ケースの内数）。一時directoryへの実`setting.txt`保存・復元、実reader／durable orchestrator／checkpoint／writer／validator／atomic commitを通す、VM→実filesystemのローカルE2Eである。4合成データ行を用い、認証・model／runtime identity・AI応答・時刻は合成境界である。実AIや本人loginを実行した証跡ではない。

復元・再開は新しいstore／VM／orchestratorのinstanceで確認しており、別processの再起動、native UI操作、T39の本人walkthroughとは区別する。

| テスト名 | 実証した範囲 | TRX判定（ケース数） |
|---|---|---|
| `Saved_output_preference_survives_new_instances_and_input_change_before_real_final_output` | 出力先の明示指定／nullを実保存し、新規instance復元・入力変更後の実final出力先と元本不変を確認 | Passed 2/2 |
| `Saved_definition_admission_rejects_missing_sheet_without_mutation_then_accepts_the_matching_input` | 保存定義のsheet不一致を状態・file無変更で拒否し、一致入力への明示適用と実final出力を確認 | Passed 1/1 |
| `Cancel_saves_a_real_partial_and_new_instances_resume_without_repeating_completed_AI_or_reference` | 実partial保存・新規instance再開・完了済み合成AI／referenceの非再実行・実finalとの整合を確認 | Passed 1/1 |
| `Override_exports_a_separate_real_workbook_using_the_run_snapshot_not_next_settings` | 次回設定から分離したrun snapshotに基づくoverride・別名実workbook出力・既存file不変を確認 | Passed 1/1 |
| `Real_final_and_preview_distinguish_missing_answer_numeric_zero_and_technical_failure` | 空回答・数値zero・合成技術失敗を実finalとpreviewで区別 | Passed 1/1 |
| `Real_atomic_final_validation_rejects_a_corrupted_cached_score_and_does_not_publish_it` | 数式cacheを破損させた候補を実validator／atomic commitで拒否し、出力しないことを確認 | Passed 1/1 |

### 5.4 T28画像とT39追加nativeの境界（0.8.4）

T28は通常8枚の1440×1050 pixel・2回hash一致と最小frame検証を含む関連216/216でREVIEWED。T36の`artifacts/test/ui-settings/t36/t36-current.trx`は公開文書11件・画像8枚のcontract 21/21でREVIEWED。生成日・版は0.8.4の履歴として保持し、現在のソース0.8.6で再生成・目視確認したとしない。

T39追加nativeの最新`artifacts/test/ui-settings/t39/native-final-attempt.json`はFAILで、failureCodeは`CONTROL_ID_PREDICATE_NOT_UNIQUE`。3試行後は再試行を停止する。120 DPI、実client 1475×1000 pixel＝1180×800 DIP、合成入力読込、正常終了、入力／EXE不変、実利用者setting.txtの開始前後不存在は確認したが、4画面・5カテゴリ・1024×720・実keyboardの追加検証は未完了。この観測はT26の通常／例外headless測定を置き換えず、全UIのnative成功を証明しない。

本人walkthrough4項目（入力／30・10配点、設定保存と復帰、再起動・明示適用、override未保存の識別）、Narrator、隔離利用者環境のnative保存は`NOT_RUN_EXTERNAL_PREREQUISITE`。環境変数差替えをOS利用者隔離の保証とせず、実設定・secretの読書／保存、本人login、実AI、実学生data、公開操作を追加しない。T39自動回帰1892/1892・skip 0をこれらの代替にせず、T39 BLOCKEDを保持する。

## 6. 維持する境界

採点式、6 placeholder、exact100、blank／zero、元本不変、別名atomic output、checkpoint schema／admission、runtime固定版、所有login process限定終了は変更しない。署名・公開matrix・installer機能を拡張しない（開発用 MSIX と macOS の基盤は 2026-10-06 に廃止した）。D19の文書同梱はT37／T38で公開文書11件・8画像・LICENSEの20ファイルへ同期済みで、利用者setting.txtを配布しない。0.8.4での実ZIP／実EXE確認とF02最終版の再検証を分ける。

過去profile・gate・画像・本人login／clean-host evidenceを本変更のPASSへ転記しない。文書同梱や製品版変更で最終EXE bytesが変われば、既存のexact artifact公開条件に従って再検証する。
## 7. 可変ワークスペース（NFR-UX-003〜010、FR-065）

- 2 定量化設計・3 実行・4 結果・出力の内容は`PanelWorkspace`（Grid・GridSplitter・TabStrip、AS-011）のパネルで、IDは`Design.Form`／`Design.Allocation`、`Execution.Conditions`／`Execution.Progress`／`Execution.Cost`、`Results.List`／`Results.Detail`／`Results.Chart`／`Results.Cost`。step・警告・開始／中断・前後移動・実行条件の要約行・結果のrun要約・ページ行・別名出力は固定。
- Automation IDはパネル`{PanelId}`、メニュー`{PanelId}.Menu`、項目`{PanelId}.Menu.{操作}`、タブ`{PanelId}.Tab`、見出し`{PanelId}.Title`、ワークスペース`{画面キー}.Workspace`、境界`{画面キー}.Splitter.{n}`。コマンド検索は`CommandPalette`／`CommandPaletteSearch`／`CommandPaletteResults`、項目は`Command.{Id}`。
- 保存は`layout.json`（`LayoutFileStore`、schema 1、利用者×画面キー`Step.*`／`Settings.*`）で、`setting.txt`（schema 1）を変えない。読めない・未知schemaのfileは変更せず、その起動中は保存しない。
- 縮退: 窓幅1180未満で3列目を隣のタブへ、幅760以下または表示倍率（OSのテキストサイズを含む）200%以上で1列に積む。最小幅が入らない場合も次の段へ縮退する。縮退は保存値を変えない。
- 既定: 画面に既定を持つ最初のペルソナ（P-01→P-02→P-03）、なければ従来配置。表示の追従はOSのライト／ダーク（`RequestedThemeVariant=Default`）、ハイコントラスト（`ColorContrastPreference.High`時にWin32システム色へ置換）、テキストサイズ（内容の拡大）、アニメーション効果（transitionの無効化）。
- 試験: `UI/PanelWorkspaceTests`、`UI/CommandPaletteTests`、`UI/LayoutPersistenceTests`、`UI/WorkspaceReflowTests`、`UI/PersonaLayoutTests`、`UI/OsDisplaySettingsTests`、`UI/WorkspaceLayoutModelTests`、`Settings/LayoutFileStoreTests`。
