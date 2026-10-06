# UI簡素化・設定保存・成果物の状態可視化 実装プラン

| 項目 | 内容 |
|---|---|
| 作成日 | 2026-09-07 |
| 状態 | **レビュー待ち・実装未着手** |
| 調査基点 | `main` / `0cdc351`。調査開始時の `git status --short` は空 |
| 要求の正本 | [`docs/requirements-definition.md`](../../../../docs/requirements-definition.md) v4.5 |
| 対象 | Windows 11 x64 / 既存AvaloniaアプリのUIとローカル設定保存 |
| 今回の成果物 | この計画書のみ。製品コード、要求正本、利用者文書、画像、設定ファイルは変更しない |
| 実装開始条件 | 要求所有者が本計画を確認し、**別途、作成・実装を指示すること** |

> 指定された `docs/requirement-definition.md`（単数形）は見つからず、実在する正本は `docs/requirements-definition.md`（複数形）だった。正本を優先して調査した。別名の要求定義書は新設しない。
>
> 以下の「採用」「変更」は、明記した事実を除き**承認後の提案**である。デフォルト案は承認済みの決定ではない。実装承認を、実AI利用・本人ログイン・commit・push・tag・公開の承認へ拡張しない。

## 1. 推奨案の要約

**既存の4ステップを残し、コンパクトな固定ナビゲーション、独立した設定画面、成果物の状態表示へ再構成する。**

1. メイン画面には、そのステップで判断・操作する項目と、その結果だけを置く。
2. 低頻度の編集項目は、常時見つけられる「歯車＋設定」から開く。設定は5番目の工程にしない。
3. 通常サイズでは画面全体のスクロールを不要にし、多数の設問・結果は一覧のページを切り替える。
4. ステップへの再訪、設定からの復帰で、入力・選択対象・編集内容を保持する。
5. 説明カードより「対象100行・2設問」「合計100/100」「作成前／保存中／完成」のような状態を優先する。
6. 設定を、利用者別フォルダーの **`setting.txt`** にUTF-8のJSONとして保存する。
7. Avalonia、既存FluentTheme、既存ViewModel、既存validator・計算処理を再利用する。新しいUIフレームワーク、DB、設定基盤は導入しない。

「スマートフォン風」は、明瞭なアイコン、選択状態、簡潔なラベル、まとまった操作領域を取り入れるという意味で扱う。iOS/Android対応やWebアプリ化は含めない。

## 2. 現状調査：事実と提案を分ける

本節はソースと既存文書の確認結果であり、今回の実機測定やユーザビリティ試験の結果ではない。

| ID | 確認した事実 | 改善での扱い・出典 |
|---|---|---|
| C01 | 正本は4ステップ、動的な設問数、必要時の縦スクロール、200%表示、警告文の常時表示を要求する | スクロール方針は要求変更として明示承認する。[要求§2・11、AC-016/017、TR-17](../../../../docs/requirements-definition.md) |
| C02 | shellは初期1180×800、最小1024×720。警告以外にヘッダー、進捗説明、4つの72以上の高さのステップボタンを持つ | 余白・重複説明を整理する。現在のはみ出し不具合を実測したという主張ではない。[MainWindow.axaml](../../../../src/StudyReportEvaluator.App/Views/MainWindow.axaml)、[Accessibility.axaml](../../../../src/StudyReportEvaluator.App/Styles/Accessibility.axaml) |
| C03 | 4工程の順序、前後移動、到達範囲への再訪は既にある。`GetState` のCompletedは現在位置より前かどうかで決まる | ナビゲーションは再利用。「訪問」と「入力有効／実行完了」を混同しない表示へ変更する。[WorkflowNavigator.cs](../../../../src/StudyReportEvaluator.App/Navigation/WorkflowNavigator.cs) |
| C04 | Designを離れるとdraftをInputへ同期する。Designへ入るとViewModelを再生成する。Executionは条件付きで再Configureする | 「戻れない」とは指摘しない。選択対象の維持、不要な再初期化、実行結果との区別を重点検証する。[MainWindowViewModel.cs / SynchronizeDraftsForTransition](../../../../src/StudyReportEvaluator.App/ViewModels/MainWindowViewModel.cs) |
| C05 | Inputにはファイル、sheet、質問行、回答範囲、候補一覧、質問別編集カードがある。主回答列変更時の設問text同期は実装済み | 同期処理・read-only読込を維持し、一覧をコンパクト化する。[InputView.axaml](../../../../src/StudyReportEvaluator.App/Views/InputView.axaml)、[InputViewModel.cs / SetPrimaryColumn](../../../../src/StudyReportEvaluator.App/ViewModels/InputViewModel.cs) |
| C06 | Designには固定数式ガイド、配点合計、全設問カード、定義情報、通常評価・固有評価・Prompt編集がある | 新しい計算エンジンは不要。配点・評価内容の概要を残し、詳細編集を設定へ移す。[QuantificationDesignView.axaml](../../../../src/StudyReportEvaluator.App/Views/QuantificationDesignView.axaml)、[同ViewModel](../../../../src/StudyReportEvaluator.App/ViewModels/QuantificationDesignViewModel.cs) |
| C07 | Executionで変更できるのはmodel、並列度、新規／再開、出力先／partial指定。`Configure` は出力先・再開状態等を初期化する | 設定値の再訪時保持を設計する。**タイムアウト入力欄は存在しないため追加しない。** [ExecutionView.axaml](../../../../src/StudyReportEvaluator.App/Views/ExecutionView.axaml)、[ExecutionViewModel.cs / Configure](../../../../src/StudyReportEvaluator.App/ViewModels/ExecutionViewModel.cs) |
| C08 | Resultsには行別score、criterion override、別名出力が既にある。各リストは仮想化されている | 既存結果を一覧＋選択行の詳細へ再配置する。[ResultsOutputView.axaml](../../../../src/StudyReportEvaluator.App/Views/ResultsOutputView.axaml)、[同ViewModel](../../../../src/StudyReportEvaluator.App/ViewModels/ResultsOutputViewModel.cs) |
| C09 | `ServiceRegistration` に設定ファイルの読込・保存の接続はない。利用者文書は独立したdefinition save/loadを非対応と明記する | `setting.txt` 保存は新規要求であり、文書の非対応記述を限定的に修正する。[ServiceRegistration.cs](../../../../src/StudyReportEvaluator.App/Composition/ServiceRegistration.cs)、[はじめに](../../../../docs/getting-started.md)、[README](../../../../README.md) |
| C10 | `CanonicalDefinitionSerializer` はSerializeとhash計算を提供するが、Deserializeは提供しない | 「既存の定義読込をそのまま使える」とはしない。設定用JSON読込はAppへ追加し、canonical hashをround-trip検証に利用する。[CanonicalDefinitionSerializer.cs](../../../../src/StudyReportEvaluator.Core/Serialization/CanonicalDefinitionSerializer.cs) |
| C11 | 既存UIテストは1024×720 shell、760×600 standalone、縦スクロール到達、44以上の操作対象、仮想化を確認する構造 | スクロールを要求するテストを単に削除せず、「通常は不要」「例外時は到達可能」に分ける。[ResponsiveLayoutTests.cs](../../../../tests/StudyReportEvaluator.App.Tests/UI/ResponsiveLayoutTests.cs)、[PrimaryJourneyAccessibilityTests.cs](../../../../tests/StudyReportEvaluator.App.Tests/UI/PrimaryJourneyAccessibilityTests.cs) |
| C12 | 文書画像は7枚、1440×1050、synthetic/fake状態で生成。ZIPと単一EXEは文書・画像を明示列挙している | 画像差替え・設定画像追加・ライセンス表示・配布リスト・contract testを連動させる。[DocumentationScreenshotTests.cs](../../../../tests/StudyReportEvaluator.App.Tests/UI/DocumentationScreenshotTests.cs)、[画像台帳](../../../../images/README.md)、[ZIP packager](../../../../scripts/package-windows.ps1)、[single-file profile](../../../../src/StudyReportEvaluator.App/Properties/PublishProfiles/WindowsSingleFile.pubxml) |

既存の[アーキテクチャ](../../../../dev/docs/architecture.md)に従い、CoreはUI／filesystemへ依存させず、production projectはCoreとAppの2つを維持する。

## 3. 公開論文・ベストプラクティスの調査と選択

### 3.1 出典と採用範囲

閲覧・本文確認は2026-09-07。記事の発行日と閲覧日を区別する。更新日を確認できなかったページについて、著作権年を更新日として扱わない。

| ID | 一次資料・発行情報 | 確認できた内容 | 本計画への適用・限界 |
|---|---|---|---|
| R01 | Ben Shneiderman, **Direct Manipulation: A Step Beyond Programming Languages**, Computer, 1983年8月。[著者公開PDF](https://www.cs.umd.edu/~ben/papers/Shneiderman1983Direct.pdf) | 先頭ページ（footer表記は`57 August 1983`。掲載ページ番号はこのfooter由来）と結論部に、対象の可視性、迅速・可逆・段階的な操作、操作結果の即時可視性。本文は図式化が常に性能改善になるわけではないとも述べる | 設定変更で配点・対象・保存状態を更新する。アニメーションや図を増やせばよいとは解釈しない。概念・設計論であり、本製品の改善率の証拠ではない |
| R02 | Edwin L. Hutchins, James D. Hollan, Donald A. Norman, **Direct Manipulation Interfaces**, Human–Computer Interaction, 1, pp.311–338, 1985。[著者公開PDF](https://hci.ucsd.edu/hollan/Pubs/direct-manip.pdf) | §3.3、p.326の「Make the Output Show Semantic Concepts Directly」。利用者が頭の中で計算・翻訳する必要を減らす出力表現 | 技術用語の長文説明より「何行・何問・何点・どのファイル」の状態を直接見せる。理論的根拠であり、実利用者での優越性は別途検証する |
| R03 | Jakob Nielsen, **Progressive Disclosure**, 2006-12-03。[本文](https://www.nngroup.com/articles/progressive-disclosure/) | 重要・頻用機能は初期表示へ、低頻度機能は明瞭な入口の先へ。頻用機能を隠す誤り、過度な階層化、相互依存する工程の分断に注意 | 必須操作はメインへ、詳細設定は1回で開ける設定へ。頻度分類は実利用頻度を測っていないため暫定。利用者確認で調整する |
| R04 | Raluca Budiu, **Wizards: Definition and Design Recommendations**, 2017-06-25。[本文](https://www.nngroup.com/articles/wizards/) | 工程一覧と現在地、具体的な前後ボタン、状態保持。細分化しすぎると操作回数・比較の負担が増える | 4工程を増やさず、既存の順序と再訪を維持。厳格な「戻れないwizard」にはしない |
| R05 | Aurora Harley, **Icon Usability**, 2014-07-27。[本文](https://www.nngroup.com/articles/icon-usability/) | 多くのアイコンは曖昧で、特にナビゲーションは常時見える文字ラベルが重要。hoverだけのラベルは避ける | アイコン＋日本語ラベル。ハンバーガーへ全操作を隠す案、絵文字だけの操作は不採用 |
| R06 | Microsoft, **Guidelines for app settings / Navigation design basics**。[設定](https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings)、[navigation](https://learn.microsoft.com/windows/apps/design/basics/navigation-basics) | 通常業務の操作は設定へ隠さない。設定を簡潔にし、発見可能な入口を設ける。少数の行先にはtop navigation、項目編集にはlist/detailsが適する | 4工程の固定ナビと、設定の独立表示を採用。WinUIの設計原則だけを応用し、WinUI／Windows Community Toolkitへの移植はしない |
| R07 | Microsoft Fluent 2, **Iconography / Layout**。[icons](https://fluent2.microsoft.design/iconography)、[layout](https://fluent2.microsoft.design/layout) | system iconsのregular/filled状態、4を基準とする余白、過密回避、reflow、余白調整。system iconsはMIT | 既存FluentTheme＋少数のFluent System Icons、4/8/12/16の余白。商品ロゴの流用、全面的なデザインシステム導入はしない |
| R08 | W3C, **WCAG 2.2**。[公開標準](https://www.w3.org/TR/WCAG22/) §1.4.10、§2.5.8 | 2方向スクロールを避けるreflowと例外、pointer target 24×24 CSS px以上と例外。スクロールを全面禁止する規定ではない | Webの基準を設計の参考にする。AvaloniaのDIPへ機械的に同一視せず、既存44 DIPの主操作と拡大時到達性を維持。本製品のWCAG適合認証を主張しない |
| R09 | Avalonia公式、**FluentTheme density / ListBox virtualization**。[theme](https://github.com/AvaloniaUI/avalonia-docs/blob/main/docs/styling/themes.md)、[ListBox](https://github.com/AvaloniaUI/avalonia-docs/blob/main/docs/how-to/listbox-how-to.md) | FluentThemeのCompact密度と、仮想化に必要な有限の高さ。無限高さのStackPanelを避け、Gridの残領域等を利用する | 既存Avaloniaで実装する根拠。参照資料は最新mainであり、固定12.1.1でのcompile／描画確認をT11以降で行う。依存版更新で解決しない |

アイコン配布条件は[Fluent UI System Icons LICENSE](https://github.com/microsoft/fluentui-system-icons/blob/main/LICENSE)と[NOTICE](https://github.com/microsoft/fluentui-system-icons/blob/main/NOTICE)も確認した。採用する個別assetとrevisionを記録し、対応する著作権・ライセンスを同梱する。repository全体や未使用の生成ツールを取り込まない。

### 3.2 調査方法と未確認事項

- R01/R02はWeb本文抽出ができなかったため、著者公開PDFを取得し、既存Python 3.14の`pypdf`で本文を確認した。PDFの13／28ページという物理ページ数を論文の掲載ページ範囲とは混同しない。
- R08は通常のページ取得で403になったが、公開URLへの別のHTTP読取で200を確認し、該当する標準本文を照合した。補助的に読んだW3C GitHub側のUnderstanding文書はeditor's draftであり、公開標準と区別した。
- 一時環境へのPDF補助ライブラリ取得はTLSで失敗したが、既存環境で解決した。プロジェクト依存を追加していない。
- 著者の別ページ等で取得・特定できなかった資料は、論拠に採用していない。
- 現行利用者の操作頻度、所要時間、迷い、誤操作率は未測定。論文や指針から、本製品での効果率・最適性を捏造しない。
- **選択理由は、本要求と現行実装に最も小さな変更で適合すること。** 普遍的に最適なUIと証明したわけではない。

## 4. 不明点・選択肢・デフォルト案

すべて承認待ち。重要度の高いD01〜D06を先に確認する。デフォルト以外を選ぶ場合は、影響するタスクだけを再見積もりする。

| ID | 不明点 | 選択肢 | デフォルトの選択肢 | デフォルトを選ぶ明確な理由 |
|---|---|---|---|---|
| D01 | 「スクロール不要」の対象サイズ・範囲 | 全サイズ／全内容で禁止、通常画面のみ不要、従来どおりscroll | **現行最小1024×720 DIP以上で通常画面は不要。長文・dropdown・拡大／狭小表示は到達性のため局所scroll可** | 動的設問数、長文Prompt、200%表示を同時に無制限表示することはできない。項目や文字を切り捨てず、AC-016/TR-17の安全な到達性を残すため |
| D02 | 多数の設問・結果をどう見せるか | 全カード、一覧内scroll、ページ切替 | **コンパクト一覧＋前／次ページ＋対象行への移動** | 通常の一覧確認にscrollを要求しないため。固定件数を業務上限にせず、表示件数は利用可能な領域で決める。汎用pagination基盤は作らない |
| D03 | `setting.txt` に何を保存するか | 実行設定だけ、実行設定＋採点定義、作業／結果も全部 | **実行設定＋採点定義1件** | 詳細画面へ移すPrompt・評価基準も保存対象にし、「設定を保存」の期待に応える。結果・再開状態は既存xlsxの責務とし、二重のcheckpointを作らない |
| D04 | 保存した採点定義を自動適用するか | 毎回自動、同名fileなら自動、明示適用 | **読込済みExcelへ利用者が明示適用** | 別授業の列・行・設問・配点を黙って適用しないため。ファイル名や列位置で内容の一致を推測しない |
| D05 | どこまで設定へ移すか | 全項目、低頻度だけ、ほぼ現状維持 | **配点・入力対象・結果確認はメイン。model変更、並列度、保存先、定義情報、詳細評価・Prompt・補助列編集は設定** | 判断に必要な状態を隠さず、詳細操作の数を減らす。mainには有効値と1回で設定を開く入口を残す。利用頻度は本人確認で修正可能 |
| D06 | 警告文も縮める／設定へ移すか | 省略、折り畳み、現行全文の常時表示 | **全文をshellで常時表示** | 要求§11.2/AC-017が文面・常時表示・nonblockingを明示。警告を隠して画面を収める案は採用しない |
| D07 | 保存先 | EXE横、入力xlsx横、利用者別folder | **`%LOCALAPPDATA%\StudyReportEvaluator\setting.txt`** | read-onlyのEXE配置先と.NET抽出cacheに依存せず、Excelと設定を分離できる。OS標準folder解決を使い、新しい環境変数を要求しない |
| D08 | txtの内部形式 | 独自key=value、JSON、YAML | **UTF-8 JSON、拡張子は指定どおり `.txt`** | 複数行Prompt、配列、decimalを既存.NETで扱える。独自parserや追加packageを避けるため。手編集必須にはしない |
| D09 | 保存タイミング・設定変更の反映 | 自動保存、毎回Saveで適用、即時反映＋明示保存 | **有効な編集は次回run用draftへ即時反映。永続化は「setting.txtに保存」だけ** | 状態変化をすぐ示しつつ、ファイル書込を明確な利用者操作に限定する。アプリ内反映とdisk保存を別の状態で示す |
| D10 | 設定画面の表示方式 | modal、右drawer、独立した内容画面、5番目のstep | **同じwindow内の独立した設定画面** | 長いPromptも扱うため小さなdrawerより領域を確保でき、modalや新工程を増やさず戻り先を保持できる |
| D11 | 戻ったときに何を保持するか | 値だけ、値と選択対象、永続的な全操作履歴 | **値・対象ID・表示ページ・設定カテゴリを同一起動中に保持** | 再入力と見失いを避けるため。Undo/Redo engineや永続navigation履歴は要求されていない |
| D12 | 実行中に前工程・設定へ戻れるか | 全画面lock、閲覧のみ、次回用編集可 | **戻る／通常のdraft編集は可。現在runは固定。保存定義の一括適用はrun終了後** | immutable snapshotを維持しながら操作を止めすぎない。別画面でも実行中表示と停止操作を失わない。一括適用の対象混乱を避ける |
| D13 | 保存modelが列挙にない場合 | 別modelへ自動fallback、警告付きfallback、未選択 | **未選択で変更を促す。勝手に代替しない** | 費用・評価条件が変わり得るため。`auto`は参照／類似度用の固定条件のまま。認証確認やlogin自体も自動開始しない |
| D14 | アイコン・見た目 | 別UI framework、アイコンpackage一式、少数の既存Fluent assets | **既存FluentTheme＋必要なsystem iconだけを静的リソース化** | 依存増加なしで統一でき、文字ラベルも維持できる。テーマ切替、custom title bar、派手なanimationは追加しない |
| D15 | 破損設定・未知version | 自動上書き修復、起動停止、保存file保持＋safeな既定値 | **fileを保持して通知し、offline GUIを利用可能にする** | 設定の問題をアプリ起動不能にしない。自動migratorや修復engineは作らず、上書き保存には明示操作が必要 |
| D16 | 同時起動時の設定保存 | lock/merge基盤、profile分離、最後の明示保存を採用 | **atomic保存し、最後の明示保存が優先** | multi-user共同編集はscope外。途中書込による破損だけを防ぎ、監視・merge・履歴DBを増やさない。この制約を設定ガイドへ明記する |
| D17 | 製品版と公開まで含めるか | 直ちに版上げ・公開、実装とローカル検証のみ | **CHANGELOGのUnreleased更新まで。製品版上げ・公開は別承認** | UI改善の承認とrelease承認を分離する。要求文書の改訂版と製品版を混同しない |
| D18 | 要求正本の文書版・基準日を上げるか | 据え置き、今回の改訂で上げる、公開時にまとめて上げる | **今回の改訂で文書版と基準日を上げる** | 新AC/TRを追加すると内容と版が食い違う。文書contract testが`文書版`・`基準日`を完全一致で検査し、実データsmokeも要求版metadataを参照するため、据え置きは不整合を残す。製品版（D17）とは別に扱う |
| D19 | 開発用MSIXの同梱文書 | 触らない、同梱文書だけ公開経路と同期、installer機能ごと拡張 | **同梱文書リストだけをZIP／単一EXEと同期** | `scripts/package-windows-msix.ps1`が独立した文書リストを持ち、放置すると3経路の同梱内容が乖離する。要求どおりinstaller機能・署名・公開範囲は据え置き、リストの一致だけを保つ |

**D01の例外も含めて一切scrollを禁止したい場合は、長文を含むすべての編集を追加ページへ分割する必要がある。** 操作回数と実装量が増えるため、現時点のデフォルトにはしない。例外を黙って「スクロール不要達成」と数えない。

## 5. 画面の情報設計

### 5.1 共通shell

- 上部に小さなアプリ名と「設定」。その下に `1 入力 / 2 採点設計 / 3 実行 / 4 結果` を固定表示する。
- 現在地、訪問状態、処理の準備状態を区別する。色だけではなく、ラベルと記号で示す。
- 指定の警告文は、設定画面を含めてshellの独立領域に全文表示する。本文やfooterをoverlayで覆わない。
- 下部の前後操作は「入力へ戻る」「採点設計へ」「実行内容を確認」のように行先を示す。最終stepに無効な「最終ステップ」主ボタンを置かない。
- 大きなhero、重複した英語見出し、workflow説明、装飾用badgeを通常表示から除く。ヘルプは必要箇所と文書へ寄せる。
- 実行中に別画面へ移っても、現在runの進捗への入口と停止操作を固定領域から利用できる。実行画面内の停止ボタンとのAutomation ID重複は避ける。

### 5.2 主画面に残す項目／設定へ移す項目

| Surface | メインに残す・表示する | 設定へ移す | 結果として分かること |
|---|---|---|---|
| 入力 | ファイル選択・直接path入力・読込、sheet、質問文行1/2、回答開始/終了、対象設問の有効化、主回答列、選択中の設問textと必須エラー | 詳細な候補一覧・再適用、補助列編集、質問名・複製・並替え等の詳細操作 | `回答範囲 2–101 → 100行`、`対象2設問`、`主回答B / 補助D`、header交差セルから変わった設問text |
| 採点設計 | Base/Special/類似度係数、設問別配点・有効状態、均等配分、使用中の評価方法・観点の短い一覧、配点構成とエラー | 定義名・revision・丸め、evaluator/criterionの詳細CRUD・range・weight、固有評価、Knowledge本文、Custom Prompt、Imported Prompt本文と適用 | 配点構成、`合計110/100・超過10 → 合計100/100`、`固有配点あり／項目未設定`等 |
| 実行 | 認証状態・明示login／取消／状態確認、通常modelと固定autoの有効状態、新規／再開、partial指定、保存先の有効値、開始／停止、進捗、実行を止めるエラー | 通常modelの変更、並列度、出力directoryの変更、runtimeの版・hash等の診断情報 | 対象・配点・model・保存先の最終状態、予約済みpath、保存済み行、完成前／完成後 |
| 結果 | final／partial、完了・一部失敗・取消、件数、行別最終点、選択行の内訳、修正の有無 | **結果やoverrideは設定へ移さない** | 完成xlsxと未保存の修正版の違い、0と空欄の違い、選択行の計算値 |

補助列・model・保存先等のメイン側には「変更」を置き、対応する設定カテゴリと対象を1操作で開く。値の入力UIを両方に複製して同期させない。

通常評価・Custom編集を頻繁に使うことが本人確認で分かった場合は、該当編集を採点設計に残す案へ変更する。全詳細を設定へ移すこと自体を目的にしない。[R03/R06]

### 5.3 設定画面

固定カテゴリは次の5つに限定する。対象がないカテゴリも位置は変えず、利用できない理由を表示する。

| カテゴリ | 内容 | データの所有者 |
|---|---|---|
| 共通 | 通常model、並列度、出力先、定義名・revision・丸め、runtime情報、設定fileの状態 | 実行設定＋現在のDesign draft。診断情報は読取専用・保存対象外 |
| 入力詳細 | 選択した設問、補助列、候補、質問mappingの詳細操作 | Inputの既存draft |
| 通常評価 | 設問・evaluator・criterionを選択し、既存の設定とCustom／Knowledge Promptを表示 | Designの既存draft |
| 固有評価 | 設問・固有項目の選択、source、補助列、Prompt | Designの既存draft |
| 読込Prompt | 起動引数の順序を保つ一覧、本文、適用先、明示的な「Promptを適用」 | 既存ImportedPrompts＋Design draft |

- 入力一覧→「補助列を変更」なら、その設問を選択済みにして開く。設定画面内でも対象の表示名を必ず確認できる。
- 「設定から戻る」で元のstepと選択対象へ戻る。設定カテゴリの移動で4stepの進捗を進めない。
- 「アプリ内に反映済み／ファイル未保存」「setting.txtへ保存済み」「保存失敗」を区別する。
- 通常の編集は即時にdraftの状態へ反映する。「保存」は反映のためではなく永続化のための操作とする。[R06への適用上の区別]
- 起動時のPrompt取込件数は採点設計から分かるようにする。本文と適用を設定へ移すため、現行要求§12.2の画面配置を承認後に改訂する。順序・明示適用・no-auto-runの契約は変更しない。

### 5.4 結果画面の二つの表示

1. **一覧**：行番号、最終点、設問獲得点合計、固有獲得点、類似度減点、状態。詳細は選択して確認する。
2. **選択行の詳細・修正**：設問別内訳、criterion raw／override／effective値、既存計算preview、別名出力先と明示出力。

多数の設問の得点を横に無限追加しない。設問別値は選択行の詳細で扱い、横scrollを避ける。一覧のページが変わってもoverrideの所有先は元のResults collectionであり、表示用のcopyへ値を閉じ込めない。

「一覧」「詳細・修正」は結果の表示切替で、5番目・6番目の業務stepではない。任意overrideを必須工程にしない。

## 6. スクロール・密度・アクセシビリティの契約

### 6.1 通常サイズでの受入目標

以下は実装前の設計目標であり、現在達成済みではない。

- 現行最小window **1024×720 DIP** と初期 **1180×800 DIP** で、実際のClientSizeを測って判定する。`MainWindow`の`MinWidth`／`MinHeight`と初期サイズは変更しない。既存`MainWindowTests`がこの値を検証しているため、収めるための最小サイズ引き上げは行わない。
- 通常の各stepと設定カテゴリは、初期offsetのまま主要操作、状態、前後移動、警告全文を確認できる。
- 外側ScrollViewerは通常状態で `Extent <= Viewport`。scrollbarをDisabledにしただけで合格にしない。
- 一覧に何件収めるかは最小画面での実測で決める。実装前に固定件数を目標値として約束しない。表示件数を業務上限とも、まだ測っていない達成値とも扱わない。
- 広い・高いwindowでは一覧の表示行を増やす。表示ページの大きさを利用者向け設定にはしない。
- 文字は本文・入力14 DIPを基準とし、主操作targetは既存の44 DIPを維持する。情報を詰めるために文字やhit targetを極端に縮小しない。
- 密度は余白・typography・配置で下げる。既存`Accessibility.axaml`が主要Controlへ`MinHeight 44`を明示しているため、FluentThemeの`DensityStyle="Compact"`を指定しても主操作の高さは変わらない。**44 DIP契約が密度指定より優先する**ことをこの順で固定し、テーマ設定だけで収まったとは判定しない。[R09]
- shellの重複説明、24の二重margin、大きなhero等を先に減らす。4/8/12/16を基準とする余白で関連項目をまとめる。[R07]

### 6.2 多数項目と長文

- 設問・結果は利用可能領域に収まるページを表示し、前／次・現在範囲・全件数を示す。結果の元行番号で対象へ移動できるようにする。
- 大量データの母集団を画面件数へ切り詰めない。最後の項目まで到達し、編集後も値が残ることを試験する。
- 既存の仮想化リストを有限高さのGrid内に置く。全データのControl生成を避ける。[R09]
- 長い設問text・Prompt本文、長いpathの全文確認、dropdownの候補には局所scrollを許容する。省略表示した値の全文へkeyboardでも到達できるようにする。
- 安全なエラー・未設定の状態を隠さない。複数エラーは件数と現在の対象エラーを示し、次の問題へ移動できるようにする。

### 6.3 拡大・狭小表示

- 760×600のstandalone検証と200%表示の既存試験意図を維持する。必要なら列を組み替え、表示ページの行数を減らす。
- それでも不足する場合は本文の縦scrollを例外として許容する。固定領域がfocusや必須操作を覆わないことを確認する。
- DIP、物理解像度、OS scaling、ClientSizeを記録する。1920×1080の200%が1024×720 DIP以上になるとは扱わない。
- 論理作業領域が現行最小window未満の環境まで無条件に1画面へ収まるとは保証しない。その対応範囲を広げる場合はD01の再決定が必要。
- keyboardのTab/Shift+Tab/Enter/Space、設定の復帰focus、スクリーンリーダーの名前、色以外の状態表現、文字・icon・focusのcontrastを確認する。
- ネイティブDPI／Narrator確認とheadlessのlayout確認は別の証跡にする。ブラウザーでHTMLを描いた結果をAvaloniaの検証に代用しない。

## 7. `setting.txt` の最小保存設計

### 7.1 保存内容

新規 `Settings/ApplicationSettings.cs` の明示的な型と、`Settings/SettingsFileStore.cs` の単一file読込・保存で実装する。App層に配置する。

| 保存項目 | 初期値・意味 |
|---|---|
| 設定schema version | 1。設定file自身の形式識別。Coreのcanonical schemaや製品版とは別 |
| 通常model ID | 未指定。列挙結果から選択した値だけを保存 |
| 最大並列度 | 1。既存の許可範囲1〜3 |
| 出力directory | 未指定なら従来どおり入力隣接 `result`。指定時はabsolute path |
| 採点定義1件（任意） | 既存 `QuantificationDefinition`。定義ID・name・revision、sheet・header・行範囲、設問ID／text／mapping／配点／enabled、evaluator、criteria、range、weight、固有評価、Prompt、丸めを保持 |

保存する定義は既存の型を使い、UIのControl、Command、selection、表示ページ等はシリアライズしない。

**保存しないもの**：入力xlsxのpath・bytes・回答本文、AI raw／reason／evidence／参照回答、final／partialのrun状態、credential、login状態、CLI hash、警告の承認状態、未適用Imported Promptのfile一覧・本文。適用済みPromptは採点定義の一部として保存する。

学生の回答セルを設定へコピーする経路は作らない。一方で、**設問textは既存の`SetPrimaryColumn`がworkbookのheader交差セル値から自動反映するため、workbook由来の文字列は既定で`setting.txt`へ平文保存される。** これに加えて、利用者がPromptや評価基準へ貼り付けた内容も定義の一部として残る。`setting.txt` を非機密として説明せず、この2つの流入経路を明示して文書化する（T32）。

### 7.2 読込・保存

- 初回・fileなし：既存の既定値で起動。起動成功のためにfile作成を要求せず、最初の明示保存で作成する。
- 読込：UTF-8のBOMあり／なしを受け入れ、JSONの型、schema、既知項目、数値、enum、必須値を検査する。既存のdefinition／Prompt validatorを再利用する。
- 設定の保存用JSONとrunのcanonical JSONは別の表現。`CanonicalDefinitionSerializer` を読込APIとして誤用せず、System.Text.Jsonで既存domain型を復元し、前後のcanonical hash一致を試験する。
- 保存：同directoryの一意な一時fileへ書込・close後に置換する。失敗時は以前の有効fileを保持し、UIに未保存状態を残す。汎用transaction／backup／locking frameworkは作らない。
- 保存対象の定義が存在する場合は、既存の定義・Prompt・配点検証を通す。invalidなdraftはメモリに保持して修正できるが、有効な保存済み定義を黙って置換しない。
- 入力未読込で共通設定だけを保存する場合、読み込んである保存定義を空の仮定義へ置換しない。
- 破損・未知schema（設定schema versionが`1`以外）・権限不足：fileを自動修復／削除しない。内容や例外全文をlogへ出さず、状態と対処だけを表示する。offlineでの入力・設計は利用可能にする。
- 複数起動・外部editorとの同時編集はmergeしない。最後の明示保存が優先されることを文書化する。
- テストはstoreへ一時directoryのabsolute pathを注入する。実利用者のLocalApplicationDataを読んだり書いたりしない。保存先切替の新CLI optionや環境変数は追加しない。
- 書込失敗の検証は、一時領域に作ったread-onlyディレクトリや占有中pathなど実ファイルシステムの条件で行う。テストのためにfile system抽象interfaceやmock層を新設しない。

### 7.3 保存定義の明示適用

1. 起動時に保存定義を読み取っても、現在のInput／Designには自動適用しない。
2. Excel読込後、「保存した採点設定」の設問数、sheet、行範囲、mapping概要を表示する。
3. 利用者が「現在の入力に適用」を実行する。run中はこの一括操作を無効にし理由を示す。
4. 保存定義のheader行で既存read-only loaderを使い、sheet・行範囲・列と定義全体を検証する。別headerを読む必要がある場合も現在のmetadataを旧headerのまま使わない。
5. 成功時だけInputのmetadata／選択値／draftとDesignを整合させる。失敗時は現在の選択・draft・保存fileを変更しない。
6. 保存済みのIDと順序を保持する。自動mapping再生成でIDを作り直したり、設問textを別の値へ黙って置き換えたりしない。
7. 適用後に利用者が主回答列を変更した場合は、従来どおりその交差セル値へ設問textを更新する。
8. 再開時の最終判断は既存checkpoint admissionが行う。設定fileが一致しただけで、input／runtime／modelの検証を省略しない。

異なるExcelへ使う場合は利用者がmapping内容を確認する。列名やheaderが存在することは、教育上の意味まで一致する証明ではない。

### 7.4 優先順位と実行の分離

- 共通設定：今回のメモリ内編集 → 保存済み共通設定 → 従来の既定値。
- 出力directory：入力workbookを新たに読み込んだ直後は、既存`Configure`どおり入力隣接 `result` を既定にする。その後の利用者指定と、入力を変えない往復での保持を優先する。保存済みpathで入力隣接の既定を黙って上書きしない。
- 採点定義：明示適用とその後の編集だけが現在draftを変更する。保存済みであるだけでは優先しない。
- 起動引数：`--input` の対象と既存cwd解決を維持。`--prompt` は現在の起動順に読み、明示適用されるまで定義を変更しない。
- model：明示的な状態確認で得た一覧に保存IDが存在すれば復元。保存IDが一覧に存在しなければ未選択として利用者に変更を求める。保存ID未指定の初回は現行の初期選択挙動を維持する。
- login完了、設定保存・読込、画面遷移から認証確認・AI評価を自動開始しない。
- run開始時に有効値を既存request／immutable snapshotへ固定する。その後の設定編集を現在runへ混入させない。

## 8. 「説明ではなく状態変化」の設計

以下の数値例はUI設計用の合成例であり、実データや実行結果ではない。

| 操作 | 表示の変化 | 真のデータ源・禁止する推測 |
|---|---|---|
| Excelを読む | `未選択 → 読込済み / 回答100行 / 対象2設問` | 既存metadataと選択範囲。学生の氏名や回答を概要へ自動表示しない |
| 主回答列を変更 | `B列の設問text → C列のheader交差セルの値` | `SetPrimaryColumn`。空cellへ代替説明を生成しない |
| 配点を修正 | `Base60 + Q1=30 + Q2=20 : 110/100 → Q2=10 : 100/100` | 既存decimal配点計算。丸め表示で100に見せない |
| 固有評価を設定 | `固有配点あり・項目不足 → 対象1項目 / 検証済み` | 既存validation。Special=0なら固有評価AIを実行しない契約は維持 |
| 類似度係数を変更 | `係数0.1 → 0` と有効設定を更新 | 未実行のscoreを予測しない。係数0を類似度処理自体の無効化とは扱わない |
| Promptを適用 | `対象設問／評価方法` とtemplateの内容が変化し、`未保存`へ | 既存templateのcopyのみ。実AI結果previewを新設しない |
| 設定を保存 | `アプリ内反映・file未保存 → setting.txt保存済み` | 実際の保存成功。save開始だけで保存済みにしない |
| run開始前 | `完成予定：別xlsx / 元sheet保持＋設定・参照回答・評価結果・実行記録` | 既存出力契約。timestamp名は未予約のため候補と明示 |
| run開始後 | `未作成 → path予約済み → checkpoint保存中 → 保存済み行が増加` | 実progressと予約情報。予約済みfinal pathを完成fileと表示しない |
| run終了 | `完成xlsxあり`、`空欄の評価あり`、`取消・partialあり`を区別 | RunSummaryとfinalization結果。経過時間で擬似progressを進めない |
| override | `元のraw → override / 計算preview更新 / 修正版未保存 → 別名出力済み` | 既存calculator／output boundary。元の完成fileが書き換わったように見せない |
| 実行後に設計変更 | 結果に `前回の実行結果`、設計に `次回実行用`を表示 | 保存済み結果のsnapshotと現在draftを区別。過去結果を最新設計で再評価したと表示しない |

UIだけの簡易計算式でscoreを再実装しない。実行単位の計画数と実AI送信数、成功件数と完了件数、空回答と技術的空欄は分ける。

### 8.1 Navigationと状態保持

- `WorkflowNavigator` の4要素・順序・到達範囲を維持する。新しい汎用routerは作らない。
- 「準備済み」は必要な入力／検証から、「実行中」「完了」は実runから導く。stepを開いただけで処理成功のcheckmarkを付けない。
- 技術エラーがあっても、その修正先へ移動できることを維持する。Inputの検証に採点設計エラーが含まれても、Designへの移動を新しいgateで閉じない。
- 設定を開く／入力詳細と採点カテゴリを切り替える／保存する／閉じる境界で、直前の編集元（InputまたはDesign）のdraftを相手へ同期する。保存元は常に最新の編集元とし、古いDesign copyでInputの変更を上書きしない。既存同期処理を使い、第三の汎用draft storeは追加しない。
- Design再生成を無条件に繰り返さず、同一draftでは選択IDを維持する。変更があれば既存同期処理を限定的に拡張する。
- 未変更の往復で出力directoryや再開pathを初期化しない。入力file／sheet等が変わって初期化が必要な場合は、古い設定・結果を現在のものと誤表示しない。
- 数値の変換エラーや入力途中のTextBoxも往復試験に含める。未確定textを黙って有効値へ戻す問題が再現した場合は、その編集面に限って保持方法を修正する。全field共通の新しい入力frameworkは作らない。
- 設定を開いたままrunが完了しても設定を強制的に閉じない。結果の到着状態を更新し、利用者が結果へ移れるようにする。

## 9. タスク分割とファイル対応

### 9.1 実施単位の規則

- **T01〜T39はすべて未着手。** G0の承認前に実施しない。
- 1タスクは1つの変更目的と小さな受入条件を持ち、原則1〜3のproductionファイル＋対応テストに限定する。
- 下表の新規ファイルは**提案名**。実装開始時に重複を再確認する。既存classを分割するだけの作業や、将来用のinterfaceは作らない。
- 入力contextは、本書の該当節、該当要求、直前タスクの公開契約、編集対象の該当class／XAMLに限定する。run生ログ、無関係なrelease履歴、他stepの全コードを常時渡さない。
- 同じファイルの編集を含むタスクは必ず直列。別ファイルでも未完成APIへの依存は、前タスクの完了後に開始する。
- 各タスクは、変更ファイル、公開した契約、実施テスト名と結果、未解決点だけを短く引き継ぐ。テスト件数や成功を実測前に埋めない。

表中のpath省略記法：

- `A/` = `src/StudyReportEvaluator.App/`
- `AT/` = `tests/StudyReportEvaluator.App.Tests/`
- `UI/` = `tests/StudyReportEvaluator.App.Tests/UI/`
- `D/` = `docs/`
- `DD/` = `dev/docs/`
- 「新」は新規予定、それ以外は既存ファイル。`*.axaml(.cs)` はそのaxamlと同名axaml.csの2ファイルを表す。

### 9.2 要求・データ・ViewModel

| ID | 作業と出力 | 依存 | 入力context／編集するファイル | 完了条件 |
|---|---|---|---|---|
| T01 | 承認内容を要求へ反映し、UIの寸法・状態・保存契約を固定 | G0 | 本書§4〜8と要求§11/12/14/17.1、AC-016/022、TR-17。編集：`D/requirements-definition.md`、新`DD/ui-layout-contract.md` | 本書の例外・保存範囲・4stepが追跡可能。**AC-016/TR-17の到達手段へページ切替を追記し、縦scrollだけを唯一の到達手段とする文言を改める。§17.1のrequired scopeへ設定保存を追加し、D18に従って文書版・基準日を更新する。** 既存AC/TRを削除・再番号付けせず、必要な新IDを末尾追加。要求改訂と製品版を区別 |
| T02 | 設定データ契約を追加 | T01 | Coreの既存definition型。新`A/Settings/ApplicationSettings.cs`、新`AT/Settings/ApplicationSettingsTests.cs` | 共通値と定義1件だけ。既定値、null定義、入れ子・ID・decimal・Unicodeのround-tripを検証 |
| T03 | `setting.txt` の読込・atomic保存 | T02 | §7.2。新`A/Settings/SettingsFileStore.cs`、新`AT/Settings/SettingsFileStoreTests.cs` | fileなし／BOM／破損／未知schema（version≠1）／権限・書込失敗／旧file保持。権限系は一時領域の実read-only pathで確認し、file system抽象interfaceを新設しない。実ユーザーfolderを使わない |
| T04 | 保存定義を読込済みExcelへ明示適用する処理 | T02 | `InputViewModel`のload・同期・validator。編集：`A/ViewModels/InputViewModel.cs`、新`UI/SavedDefinitionApplicationTests.cs` | 成功時だけmetadata・sheet・行・draftを一致させる。失敗は無変更。ID・Prompt・配点保持とAI送信0を確認 |
| T05 | Design draftの同期と選択対象保持 | T01 | `QuantificationDesignViewModel`のCommit/SynchronizeQuestions。編集：`A/ViewModels/QuantificationDesignViewModel.cs`、新`UI/DesignStateTests.cs` | 設問／evaluator／criterion／固有項目のIDで選択を維持。削除時だけ妥当な隣接項目へ。Imported Promptの順序を保持 |
| T06 | 実行設定の反映・準備状態を整理 | T02 | `Configure`、`ApplyAuthentication`、`StartAsync`。編集：`A/ViewModels/ExecutionViewModel.cs`、新`UI/ExecutionSettingsTests.cs` | 有効model・並列度・出力先が開始requestへ反映。未変更再訪で設定保持。保存model欠落、auto欠落、no-auto-login/run、run中固定を試験 |
| T07 | 結果の選択行・ページ・修正版状態を追加 | T01 | 既存Results collectionとpreview更新。編集：`A/ViewModels/ResultsOutputViewModel.cs`、新`UI/ResultsPresentationTests.cs` | ページ移動してもoverride保持。完成版と未保存修正版、0とblank、過去runを区別。計算ロジックは再実装しない |
| T08 | Inputの選択設問・ページ・対象summaryを追加 | T04 | §5.2/6、InputのQuestions/metadata。編集：`A/ViewModels/InputViewModel.cs`、新`UI/InputPresentationTests.cs` | 全件への到達、最終ページで削除した場合の補正、header同期、対象数が実dataから決まる |
| T09 | Designのコンパクト表示用summary・ページを追加 | T05 | 既存AllocationSummary/Questions。編集：`A/ViewModels/QuantificationDesignViewModel.cs`、新`UI/DesignPresentationTests.cs` | 合計・残り配点がexact decimal。ページは表示だけ、手動配点を変えない。未実行scoreを生成しない |
| T10 | 設定画面の状態・保存・読込・適用commandを接続 | T03,T04,T05,T06,T08,T09 | §7、既存VMを参照。新`A/ViewModels/SettingsViewModel.cs`、新`UI/SettingsViewModelTests.cs` | アプリ反映とdisk保存を区別。no-input時に保存定義を失わない。invalid・保存失敗で現在draft保持。設定categoryと対象が明確 |

### 9.3 見た目と設定内の編集面

| ID | 作業と出力 | 依存 | 入力context／編集するファイル | 完了条件 |
|---|---|---|---|---|
| T11 | 共通のコンパクトな余白・色・操作target | T01 | R07/R09と既存styles。編集：`A/App.axaml`、`A/Styles/Accessibility.axaml`、新`A/Styles/WorkflowTheme.axaml` | 固定Avaloniaでcompile。**`Accessibility.axaml`はhit target・focusタcontrastの最低保証、`WorkflowTheme.axaml`は余白・typography・icon配置だけを持ち、同じ属性を両方で設定しない。** 既存44 DIP主操作とfocusを維持。未使用theme optionなし。密度変更だけで「収まった」としない |
| T12 | 必要なFluent system iconsとライセンスを追加 | T11 | R05/R07、個別assetのrevision。新`A/Resources/WorkflowIcons.axaml`、編集`A/App.axaml`、新`D/third-party-notices.md` | 使用するiconだけ。静的Geometry等で追加renderer依存なし。文字ラベル、出典、MIT表記。未使用assetなし |
| T13 | 入力詳細を独立した編集viewへ移す | T08,T12 | 元Input mappingの対象部分。新`A/Views/MappingSettingsView.axaml(.cs)`、新`UI/MappingSettingsViewTests.cs` | 選択した設問の補助列・候補・詳細CRUDが操作可能。主列重複禁止・ID・validatorを維持 |
| T14 | 通常評価・criterionの詳細編集viewを作る | T09,T12 | 元Designの通常evaluator/criterion部分。新`A/Views/EvaluatorSettingsView.axaml(.cs)`、新`UI/EvaluatorSettingsViewTests.cs` | 既存CRUD/range/weight、Knowledge read-only、Custom編集・previewを維持。選択対象を取り違えない |
| T15 | 固有評価の詳細編集viewを作る | T09,T12 | 元Designの固有評価部分。新`A/Views/SpecialEvaluationSettingsView.axaml(.cs)`、新`UI/SpecialEvaluationSettingsViewTests.cs` | source／補助列／Prompt／enabledを既存draftへ反映。Special配点条件を維持 |
| T16 | Imported Promptの編集面を分離 | T09,T12 | 元ImportedPrompts部分と要求§12.2。新`A/Views/ImportedPromptSettingsView.axaml(.cs)`、新`UI/ImportedPromptSettingsViewTests.cs` | basename・本文・順序・対象名を保持。「適用」までcopyなし、AI送信0、step変更なし |
| T17 | 設定画面の共通項目と5カテゴリを組み立てる | T10,T13,T14,T15,T16 | §5.3。新`A/Views/SettingsView.axaml(.cs)`、新`UI/SettingsViewTests.cs` | 共通項目・保存状態・保存fileの場所・戻る入口が明確。対象なしの理由が表示され、カテゴリで状態が失われない |

新規viewの分割は、**実際に移す編集面ごとの所有範囲**を小さくするためである。汎用form builder、metadata駆動renderer、将来plugin向けの抽象層は作らない。

### 9.4 4stepとshellへの統合

T22は表の掲載順と異なり、依存が揃えばT18〜T21より先に実施する。これによりviewが未定義のshell commandへ依存することを避ける。

| ID | 作業と出力 | 依存 | 入力context／編集するファイル | 完了条件 |
|---|---|---|---|---|
| T18 | Inputの通常画面をコンパクト一覧にする | T08,T12,T22 | §5.2/6。編集：`A/Views/InputView.axaml(.cs)`、`UI/InputViewTests.cs` | picker／直接path／取消／読込を維持。主列変更後の設問textが可視。詳細リンクは同じ設問の設定を開く |
| T19 | Designを配点と評価内容の概要中心にする | T09,T12,T17,T22 | §5.2/8。編集：`A/Views/QuantificationDesignView.axaml(.cs)`、`UI/QuantificationDesignViewTests.cs` | 合計・有効値・全設問への到達・均等配分を維持。詳細viewへ移した機能が消えていない。数式要約は現行値と一致 |
| T20 | Executionを開始判断と進捗中心にする | T06,T12,T17,T22 | §5.2/8、既存loginの仕様。編集：`A/Views/ExecutionView.axaml(.cs)`、`UI/ExecutionViewTests.cs` | model／保存先を確認して設定へ移動可能。new/resume・partial指定・開始・取消が明確。loginの明示性・44 target・所有process境界は維持 |
| T21 | Resultsを一覧と選択行の詳細に再構成 | T07,T12,T22 | §5.4/8。編集：`A/Views/ResultsOutputView.axaml(.cs)`、`UI/ResultsOutputViewTests.cs` | 項目増加で横幅が増えない。ページ移動・詳細編集・別名出力・cleanup warningが全て到達可能 |
| T22 | 4stepと設定の切替・draft／run表示の分離 | T04,T05,T06,T07,T08,T09,T10 | `MainWindowViewModel`とNavigator。編集：`A/ViewModels/MainWindowViewModel.cs`、`A/Navigation/WorkflowNavigator.cs`、`UI/MainWindowTests.cs` | 4stepを維持。設定はstep外。**設定画面の表示は既存の`CurrentEditorViewModel`＋DataTemplate解決に乗せ、`HasEditorContent`等の既存判定を壊さない。** 再訪で値・選択保持。訪問を処理完了と表示しない。技術エラーの修正先をnavigation gateで閉じない |
| T23 | shell固定領域・起動compositionを接続 | T17,T18,T19,T20,T21,T22 | §5.1/7.2。編集：`A/Views/MainWindow.axaml(.cs)`、`A/Composition/ServiceRegistration.cs`、新`AT/Composition/SettingsCompositionTests.cs` | 警告・nav・主操作を維持しsettings表示を接続。設定読込はproduction compositionだけ。VM単体／headless testが実利用者fileへアクセスしない |

`WorkflowNavigator`は必要なら表示名を調整するだけに留める。step enumを増やしたり、全アプリ向けの履歴stackへ置き換えたりしない。既存constructor／テスト用注入の利用方法は可能な限り保持する。

### 9.5 回帰・画面検証

| ID | 作業と出力 | 依存 | 入力context／編集するファイル | 完了条件 |
|---|---|---|---|---|
| T24 | 往復・設定・進行中runの結合回帰 | T23 | §8.1。新`UI/WorkflowStateTests.cs` | Input→Design→Settings→Input→Executionの往復、設定カテゴリ間の交互編集→最新draft保存、選択保持、入力変更、実行中の別画面、設定中のrun完了、過去結果の区別を検証 |
| T25 | Keyboard・警告・focus・login回帰 | T24 | 現行AC-017/TR-17/32。編集：`UI/PrimaryJourneyAccessibilityTests.cs`、`UI/EthicsWarningTests.cs`、`UI/CopilotLoginCommandTests.cs` | 警告全文が全step／設定で常時見える。focus移動・復帰・名前・停止が利用可能。秘密情報・警告同意の新規入力なし |
| T26 | 画面収まり・多数項目・拡大を分けて検証 | T24 | §6。編集：`UI/ResponsiveLayoutTests.cs`、新`UI/CompactWorkflowLayoutTests.cs` | 通常サイズの非scroll・主要Controlの完全包含、狭小／長文の例外到達、最終ページ、仮想化を検証。既存scroll testを削除するだけにしない |
| T27 | 設定保存を含むdeterministic E2E | T24 | 既存synthetic fixture／fake boundaries。新`AT/E2E/SettingsWorkflowSystemTests.cs` | save→再読込→明示適用→fake run→出力。canonical hash・元本不変・数式／blank・同条件resume・no-auto-runを確認 |
| T28 | 実viewの説明画像を再生成 | T25,T26,T27 | 既存capture fixture。編集：`UI/DocumentationScreenshotTests.cs`、生成：`images/01-input-workbook.png`〜`07-output-export.png`、新`images/08-settings.png` | syntheticのみ、実viewから描画、同環境2回一致。保存済みPNGへ推測の成功状態を描かない。通常最小サイズの別captureも検証artifactへ残す |
| T29 | 画像の出典と対象版を更新 | T28 | 今回の実生成結果。編集：`images/README.md` | 8枚の内容、寸法、日付、generator、synthetic/fake、対象版を正確に記録。画像と実機証跡を区別 |

T28の画像は入力ファイルではなく生成物であるため、複数PNGを同じ生成タスクが所有する。画像ごとに別agentが手描き・差替えする運用はしない。

### 9.6 利用者・開発文書と配布への反映

| ID | 作業と出力 | 依存 | 入力context／編集するファイル | 完了条件 |
|---|---|---|---|---|
| T30 | 新しい通常操作と設定保存ガイド | T28 | 実UIと§7。編集：`D/getting-started.md`、`D/README.md`、新`D/settings.md` | 4step・戻る・設定の場所・保存／明示適用・例外scrollが実UIと一致。**新規`D/settings.md`にも既存公開文書と同じ対象版開示（未公開と現行公開版の区別）を入れ、文書contractの表記規則へ合わせる。** 独立save/load非対応の旧記述を今回の範囲だけ修正 |
| T31 | 採点・Custom・Prompt起動ガイド | T28 | 既存計算・placeholder契約と新配置。編集：`D/features.md`、`D/custom-evaluator-guide.md`、`D/prompt-launch.md` | 配点・Prompt・model等の変更先が正しい。起動引数と明示適用・no-auto-runは不変 |
| T32 | privacyと障害時の扱いを更新 | T03,T04,T20,T27 | 実装済みの保存境界と§7.1。編集：`D/privacy-and-data-handling.md`、`D/troubleshooting.md` | 平文設定の含有情報・保存先・未保存・破損・モデル欠落・別Excelへの適用失敗を説明。**workbookのheader由来の設問textが既定で保存される点と、Prompt／評価基準へ貼り付けた内容が残る点の2経路を明記する。** 回答やcredentialの収集経路を追加しない |
| T33 | READMEとUnreleasedを同期 | T29,T30,T31,T32 | 完了した機能と実画像。編集：`README.md`、`CHANGELOG.md` | 現行公開版と未公開候補を区別。未測定のUX効果・公開成功を記載しない。製品版は自動で上げない |
| T34 | 開発設計のUI／保存境界を同期 | T24,T27 | 実装された型と責務。編集：`DD/architecture.md`、`DD/detailed-design.md` | Core/App境界、設定・draft・run snapshot・checkpointの所有先が一致。新たなDB／frameworkなし |
| T35 | 要求・system test・claim追跡を同期 | T01,T25,T26,T27,T34 | T01の追加AC/TRと実施結果。編集：`DD/traceability.md`、`DD/readme-claim-ledger.md`、`SystemTest-prompt.md` | 既存IDを維持し今回の要求／testを追記。**`ST-UC-`と`C-`の連番、TRの網羅範囲、要求ID対応表は文書contract testがexact検査するため、追加は末尾連番で行い欠番を作らない。** 過去の試験結果を新UIのPASSへ転記しない |
| T36 | 文書contractと要求版metadataの更新 | T29,T30,T31,T32,T33,T34,T35 | 各文書の実内容。編集：`AT/Content/DocumentationContractTests.cs`、必要時`AT/E2E/RealDataSystemSmokeTests.cs`の要求版metadataのみ | 新doc・画像・設定境界・AC/TR追跡が検証され、local links／anchor／unsupported claimの検査を維持。**hard-codeされた`AC-`／`C-`／`ST-UC-`の件数、要求IDの網羅範囲、期待mapping配列、`文書版`・`基準日`の完全一致文字列、公開doc pathリスト、対象版開示の対象fileを新しい実態へ更新する。** 実データE2E自体は起動しない |
| T37 | ZIPと開発用MSIXの同梱fileリストを更新 | T12,T28,T30,T36 | 現行の明示allowlist。編集：`scripts/package-windows.ps1`、`scripts/package-windows-msix.ps1`、`AT/Packaging/WindowsPublishPackageTests.cs`、必要時`AT/Packaging/WindowsInstallerPackageTests.cs` | 設定ガイド・第三者notice・08画像を同梱。**MSIX scriptは独立した文書リストを持つため同じ集合へ同期する（D19）。installer機能・署名・公開範囲は拡張しない。** 利用者の`setting.txt`、入力／final／partial、workは同梱しない |
| T38 | 単一EXEの同梱リストと契約を同期 | T37 | 既存App限定profileとpublish／package tests。編集：`A/Properties/PublishProfiles/WindowsSingleFile.pubxml`、`scripts/publish-windows.ps1`、`AT/Packaging/WindowsSingleFileProfileTests.cs`、`AT/Packaging/WindowsSingleFilePackageTests.cs` | ZIPと同じ公開文書集合。**pubxmlと`publish-windows.ps1`の`Get-SingleFileDocumentationPaths`は同一集合を別々に保持しているため必ず同時更新する。片方だけを変えると既存のprofile突合testが失敗し、EXEから新規docsが欠落する。** SDK／CLI／publish方式は変更しない |
| T39 | 全体検証・利用者walkthrough・実施記録 | T25,T26,T27,T36,T37,T38 | 下記§10。出力：`artifacts/test/ui-settings/`内の限定証跡、実施時に新規`work/<実施日>-ui-settings-execution-record.md` | 既存required testsと今回のtest結果を確認。実施／未実施・native／headlessを区別。未解決差分なし、利用者確認事項は未確認と明記 |

### 9.7 実行順序と並列化

**依存関係を優先し、表の番号順を機械的に実行しない。**

| Wave | 実施候補 | 並列にできる理由・制約 |
|---|---|---|
| 0 | G0 → T01 | 承認した範囲を先に固定 |
| 1 | T02、T05、T07、T11 | 型・Design・Results・styleで編集ファイルが分離 |
| 2 | T03、T04、T06、T09、T12 | 各依存が完了したものだけ開始。T11→T12はApp.axamlを共有するため直列 |
| 3 | T08 → T10 → T22 | T04→T08はInputViewModelを共有。T10はstore・draft契約の統合。T22でshell commandと状態契約を先に確定 |
| 4 | T13、T14、T15、T16、T18、T21 | 独立view／testの編集。T18/T21はT22完了が必要 |
| 5 | T17 → T19/T20 → T23 | 設定を組立後、削減した主画面とshellを接続 |
| 6 | T24 → T25/T26/T27 | 往復統合の後に、accessibility・layout・E2Eの異なるtestファイルを担当 |
| 7 | T28 → T29/T30/T31。T32/T34/T35も依存成立後に実施 | 実画面に文書を合わせる。画像generatorは単一担当 |
| 8 | T33 → T36 → T37 → T38 → T39 | 文書契約・配布・最終検証を直列で閉じる |

同じApp projectのbuild/testは、共有workspaceでは並列起動しない。編集が並列可能でも、bin/obj・capture・package出力を共有する実行は単一runnerで行う。

**共有ファイルの直列所有**：InputViewModelはT04→T08、DesignViewModelはT05→T09、App.axamlはT11→T12。MainWindowViewModelはT22、MainWindow.axamlとcompositionはT23が所有する。統合後に想定外の共有ファイル変更が必要になった場合は、該当担当へ戻し、並列に上書きしない。

## 10. 受入基準と検証手順

### 10.1 今回追加する受入観点

ここでのUX番号は本計画内のチェックID。要求正本のAC/TR番号はT01で末尾に追加して対応付ける。

| ID | 合格条件 | 主な担当 |
|---|---|---|
| UX-01 | 4工程が常に分かり、到達済みstepと設定から、値・選択対象を保持して戻れる | T22〜25 |
| UX-02 | 通常サイズで主要画面に外側scrollが不要。警告全文・主操作がviewport内にある | T11,T18〜23,T26 |
| UX-03 | 多数項目は欠落せず、最終ページに到達。長文・拡大の例外もkeyboardで全文・操作へ到達 | T07〜09,T13〜21,T25,T26 |
| UX-04 | 設定の有効値と変更結果が各stepで分かり、未実行scoreや未完成fileを完成済みとしない | T06〜10,T18〜24 |
| UX-05 | `setting.txt` が明示保存・次回読込でき、定義のID・配点・Promptが往復で変わらない | T02〜04,T10,T27 |
| UX-06 | 別Excel・別header・不足列・未知model・破損fileで誤った自動適用やfallbackを行わない | T03,T04,T06,T27 |
| UX-07 | 設定・画面遷移・Prompt適用でAI/loginが自動開始されず、run中のsnapshotは不変 | T06,T10,T16,T24,T27 |
| UX-08 | blank/zero、100点exact制約、元本不変、別名出力、checkpoint再開が既存契約どおり | T19〜21,T27,T39 |
| UX-09 | 一貫したicon＋文字、44 DIP主操作、focus、Automation ID、警告文、contrastを維持 | T11〜26 |
| UX-10 | docs・画像と、ZIP／単一EXE／開発用MSIXの3経路の同梱リストが一致し、利用者設定や実データを配布しない | T28〜39 |

### 10.2 最小の試験行列

- 入力：未選択、picker取消、正常、形式不正、質問行1/2、header未再読込、空header、長い日本語名・path。
- 設問：1、5、ページを超える件数。追加・複製・並替え・削除・全無効・最後のページ削除を含む。
- 採点：60/0/0.1の既定、合計不足／超過／exact100、均等化、Special>0で項目不足、個別range、Custom placeholder不正。
- 保存：初回、再起動相当、BOM、Unicode・改行・brace・decimal、定義なし、invalid draft、壊れたJSON、未知schema（version≠1）、read-only先への保存拒否、置換前の失敗時に旧fileが残ること。これらは実ファイルシステムの条件で確認する。
- 適用：同じ入力、異なるheader、sheet／列／行不一致、失敗後無変更、既存Prompt一覧保持、同じIDでcheckpointの定義一致を確認。
- 実行：認証未確認／必要／失敗／利用可、保存model利用不可、autoなし、新規／再開、run中に別step・設定、取消、設定画面を開いたまま完了。
- 結果：未実行、全成功、一部技術失敗、空回答、取消、final生成失敗、partial cleanup失敗、override不正・修正・別名出力。
- 件数：既存の100行・530行syntheticをUI表示の上限fixtureとする。要求上限の20,000行は、ページ数・現在範囲表示・最終ページ到達の計算だけを軽量に検証し、20,000件分のViewModel／Control生成を伴うUI testは作らない。表示件数を実runのattempt上限と混同しない。
- 表示：1024×720、1180×800、760×600 standalone、200%相当headless、native Windowsの実DPI。結果にはClientSize／論理・物理寸法と実施範囲を残す。

### 10.3 テストの実施順

1. 変更対象のVM／store unit tests。追加直後のtestは名前を明示して、古いdiscoveryだけの成功を避ける。
2. 対応するAvalonia headless UI tests。
3. 4step＋設定の結合テスト、synthetic deterministic E2E。
4. 既存Core／App required tests。今回触らない採点・formula・checkpoint・privacyの回帰を含む。
5. 実view screenshotの2回一致、docs link／anchor／claim、package allowlist。
6. 固定SDKでlocked restore → Release build → tests → package検証。コマンドは既存[CI](../../../../.github/workflows/ci.yml)と[開発者ガイド](../../../../dev/README.md)の手順を使う。新しいビルド基盤は作らない。
7. native Windowsで主要画面・DPI・keyboard／Narrator・実保存を確認する。保存試験は隔離された利用者環境を使う。開発hostでの成功をclean-host公開証跡としない。

実AI smoke、実学生data、本人ログイン、Office再計算は今回のUI検証に必須としない。既存のopt-inを勝手に有効化しない。実AIを使わずに確認可能な品質を先に完結させる。

### 10.4 利用者walkthrough

説明文を読ませるのではなく、次の3課題で結果と現在地が理解できるかを本人に確認する。

1. 合成Excelを選び、対象行・設問・主回答列を確認し、2問の配点を30/10へ変えて合計100を確認する。
2. 設定で評価観点を修正し保存、元stepへ戻り、変更内容と保存状態を確認する。
3. fake実行結果から完成fileを特定し、1件overrideした状態が「別名出力前」であることを確認する。

記録するのは、完遂の可否、迷った箇所、誤って理解した状態、修正要望。任意に所要時間・操作数を測る場合も比較条件を記録し、単一利用者の結果を統計的な改善率へ一般化しない。

## 11. docsの更新要否と非変更領域

**docs更新は必要。** 現行文書が画面上の場所・操作名を説明し、definition独立save/loadを非対応としているため、UIだけを変更すると不整合になる。[C09/C12]

| 文書群 | 必要な更新 | タスク |
|---|---|---|
| 要求正本 | scrollの通常／例外、設定の位置と保存、状態保持、Prompt表示場所、新AC/TR | T01 |
| 利用開始・index・新設定guide | 新しい画面配置、保存と明示適用、戻る方法、保存場所と制約 | T30 |
| 機能・Custom・Prompt起動 | 設定の移動先と有効値表示。計算・placeholder・引数契約は維持 | T31 |
| privacy・troubleshooting | 平文`setting.txt`、含有情報、破損時の挙動、保存model欠落 | T32 |
| README・CHANGELOG | 利用者の入口・画像・追加機能・Unreleased表記 | T33 |
| 開発設計・追跡・SystemTest | 状態所有、file契約、AC/TRとテストの対応 | T34〜36 |
| 画像・第三者notice・package | 実view由来画像、iconライセンス、ZIP／単一EXE／開発用MSIXの3経路それぞれの明示リスト | T12,T28〜29,T37〜38 |

過去ADR・過去実施記録は履歴として残し、現在のUIに合わせて過去の試験事実を書き換えない。`DD/implementation-status.md`等に今回の変更と矛盾する現在状態の記述が見つかった場合は、T34の対象節へ限定追加し、無関係なrelease状態を更新しない。

原則として変更しない領域：

- Coreの採点式、Prompt placeholderの仕様、empty/technical failureの意味。
- workbook形式、app-owned sheet名、atomic finalization、checkpoint schema／admission。
- SDK／CLI／Avalonia／.NETの固定版、bundled resolver、login子processの所有境界。
- 公開workflow／matrixのpolicy、署名、MSIX機能、macOS source foundation。
- 入力sample、実在の入力・出力・partial、利用者の既存credential。

## 12. オーバーエンジニアリング防止と完了ゲート

### 12.1 今回追加しないもの

- 汎用設定framework、設定providerのStrategy/Factory、plugin、event bus、汎用navigation router。
- テーマ・言語・font・densityの利用者向け切替、ログレベル設定、未使用flag。
- 複数profile、設定共有、cloud同期、DB、暗号化container、独自migration／backup／履歴engine。
- Undo/Redo engine、永続navigation履歴、AIによる設定提案、実AIの試し採点preview。
- 新しい評価アルゴリズム、学習者analytics、scoreのグラフdashboard。
- 全icon集／SVG library／新UI framework、WebView、独自title bar、drag-and-drop専用操作。
- 新しいCLI引数・必須環境変数、設定の自動AI適用、login自動開始。
- 新しいNuGet package。`Directory.Packages.props`とlock fileは変更しない。iconはXAML静的リソースとして追加する。
- リリース公開、branch作成、commit/push、製品版の自動bump。

### 12.2 ゲート

| Gate | 判定 |
|---|---|
| G0：実装前 | 本計画とD01〜D19への承認、および作成・実装の明示指示。現時点は未通過 |
| G1：情報設計 | T01の画面・保存契約が要求と一致。scroll例外、mainに残す項目、定義保存範囲に未承認の拡張がない |
| G2：機能・表示 | T24〜27で状態・設定・layout・安全境界を検証。視覚上の疑問は本人へ提示し、合格を捏造しない |
| G3：成果物 | T28〜38でdocs・画像・配布リストを同期。T39で実施結果、未実施、残課題を明示 |
| 公開（別scope） | UI変更でEXE bytesが変わるため、既存のexact artifact／clean-host公開条件を別途満たす。UI検証成功だけで公開しない |

この計画作成中には、製品のbuild/test、アプリ起動、実AI評価、ログイン、設定ファイルの作成は実施していない。**計画書の品質確認と、提案するUIの実装・動作検証は別である。**