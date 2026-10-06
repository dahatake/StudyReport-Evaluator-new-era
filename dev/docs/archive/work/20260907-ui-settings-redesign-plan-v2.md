# UI簡素化・設定保存・成果物の状態可視化 実装プラン v2

| 項目 | 内容 |
|---|---|
| 計画書の改訂 | 第2版 / 2026-09-07 |
| 状態 | **更新版の確認待ち・実装未着手・G0未通過** |
| 調査基点 | `main` / `0cdc351`。更新開始時の変更は旧計画書1件の未追跡のみ |
| 要求の正本 | [`docs/requirements-definition.md`](../../../../docs/requirements-definition.md) v4.5 / 基準日2026-09-06 |
| 分析対象 | [旧計画書](20260907-ui-settings-redesign-plan.md)と、本会話の敵対的レビュー・修正報告 |
| 対象 | Windows 11 x64 / 既存AvaloniaアプリのUIとローカル設定保存 |
| 今回の成果物 | **本書のみ**。旧計画書、製品コード、要求正本、利用者文書、画像、配布スクリプトは変更しない |
| 実装開始条件 | 要求所有者が本書の提案を確認し、別途、作成・実装を明示指示すること |

本書を次の承認確認に使う統合版とする。旧版は比較用の履歴として保持し、両方を並列の実装指示として使わない。計画書の版、要求文書の版、製品版、公開版は別物である。

本書の「採用」「変更」は承認後の提案であり、承認済み・実装済みという意味ではない。実装承認を実AI利用、本人ログイン、commit、push、tag、公開の承認へ拡張しない。`setting.txt`も今回の作業では作成しない。

## 1. 更新版の結論

**既存の4ステップを維持し、主画面を必要な判断と操作に絞る。詳細編集は同じウィンドウ内の設定画面へ移し、設定は明示操作で保存する。**

| 利用者の要望 | 採用する構成 | 検証観点 |
|---|---|---|
| 実行に必要な項目だけを見せたい | 主画面は入力対象・配点・開始判断・結果。詳細設定へは対象を保って1操作で移動 | UX-01、UX-04 |
| スクロールせず多くの項目を見たい | 通常サイズは外側スクロール不要。多数項目はページ切替。長文・狭小／拡大表示は明示した例外 | UX-02、UX-03 |
| 各ステップを理解して戻りたい | 固定の4ステップ、行先が分かる前後操作、値・選択対象・ページの保持 | UX-01、UX-07 |
| 説明より結果の状態を見たい | 対象数、配点合計、有効設定、未保存／保存済み、作成前／保存中／完成を実データから表示 | UX-04、UX-08 |
| 設定を`setting.txt`へ保存したい | 利用者別フォルダーへUTF-8 JSONで共通設定と採点定義1件を明示保存 | UX-05、UX-06 |
| モダンな見た目にしたい | 既存FluentTheme、少数のFluent System Icons、日本語ラベル、統一した余白とフォーカス | UX-09 |

「スマートフォン風」は簡潔なラベル・アイコン・操作領域の設計を指す。モバイル版、Web版、新UIフレームワークへの移植ではない。

### 1.1 旧版からの主要な変更

1. 前回レビューの件数・合格表示をそのまま引き継がず、根拠と修正効果を再確認した（§2）。
2. 保存済み出力先が新しいExcelの読込で失われる案を改め、**明示指定を保持し、未指定時だけ入力隣接`result`**とする提案へ統一した（D20、§7.4）。
3. 見出し由来の設問文が保存されることと、**保存自体は利用者の明示操作だけ**であることを分けた（§7.1）。
4. WindowsのフォルダーのReadOnly属性を保存拒否の根拠にしない。排他オープン等、実際に失敗を再現できる試験へ変更した（§7.2、§10.2）。
5. 要求版・追跡情報・直接影響する文書テストはT01で一緒に更新する。T36まで不整合を放置しない（§9.2、§9.8）。
6. ZIP・単一EXE・非公開MSIXに加え、**MSIX検証スクリプト側の独立リスト**も同期対象へ入れた（T37、§9.8）。
7. 追加の`WorkflowTheme.axaml`は作らず、既存スタイルを整理する。39タスクは維持し、責務・検証順序を具体化した。

## 2. 前回レビューの分析と取扱い

前回の「45件」は確定した欠陥数として再利用しない。「36〜45」は個別箇所・問題・根拠がない一括記載であり、No.33／34も現状維持で十分とした事項だった。また、構造検証の成功はUI実装・配布実物の成功を証明しない。

以下は**レビューへの対応表**であり、新たな欠陥件数の水増しではない。未実装の設定画面やページ切替が現在のコードにないこと自体も、現行製品の欠陥には数えない。

| 前回No. | 分析・採否 | 更新版への反映 |
|---|---|---|
| 1 | 同梱リストの編集漏れは採用。ただし「片方だけ変えると必ずEXEから欠落」は断定しすぎ。確認できたのは独立集合と突合テストの存在 | T38にprofile、`publish-windows.ps1`、対応テストを明示。集合一致と実物同梱を別々に検証 |
| 2 | 見出し由来の設問文の流入は事実。ただし旧修正文の「既定で保存」は自動書込と誤読できる | **定義を明示保存したときに含まれる**と訂正。見出しと貼付内容の両経路を§7.1・T32へ |
| 3、4、13、27 | 要求・追跡ID・版の同期、ページ切替を到達手段へ加える修正を採用。版据え置きだけでテストが失敗するわけではない | D18は変更履歴を識別するために版を上げる判断。T01で要求と関連期待値を同時更新 |
| 5、6 | MSIXの同梱文書同期を採用。公開対象を増やす案は採らない | D19、T37、UX-10。`test-windows-msix-unsigned.ps1`の`RequiredPublicEntries`も追加 |
| 7 | 出力先の優先順位の明確化は採用。ただし旧修正の「毎回resultへ戻す」は保存機能と矛盾 | D20と§7.4で明示指定／未指定を分離。要求§9.1／13.5／AC-032も整合させる |
| 8、21 | スタイル競合の回避は採用。「Compactなら高さが全く変わらない」は限定が必要 | 44 DIPは下限。余白等で実高さは変わり得る。既存スタイル内へ集約し二重setterを増やさない |
| 9 | 最小サイズを上げて達成を装わない方針は採用。既存テストが全寸法を検証済みとは言わない | XAMLの最小1024×720・初期1180×800を維持し、T23／26で実ClientSizeとともに検証 |
| 10 | 設定表示機構の明示を採用 | T22はVMと表示状態、T23は`CurrentStepContent`のDataTemplateと構成処理を担当 |
| 11 | 汎用filesystem抽象の新設は不要。ただしReadOnlyフォルダーだけで保存拒否を試験する旧修正は不適切 | §7.2で実ファイルの共有拒否・不正な親パス等を使用。OS権限変更や昇格を要求しない |
| 12、16 | 20,000行の試験コストは未測定で、性能障害とは断定しない。5件／6行は達成値ではなく旧版の目標例だが、導出根拠の補足は妥当 | UI試験は既存の100行・530行合成データを再利用。20,000件は境界計算を別試験。表示行数は実測で決定 |
| 14、15 | 文書一覧・対象版開示の追加を採用 | 設定ガイドは未公開UIと公開版を区別。第三者noticeはライセンス・素材revisionを記載し、UIガイドと混同しない |
| 17、18、30 | 調査日訂正と出典範囲の明示を採用 | §3で旧調査の継承と今回の再確認を区別。ページ番号や著作権年を新たな発行日へ読み替えない |
| 19、20 | schema定義と依存追加禁止を採用 | 設定schemaは整数1。未知版は保持して通知。NuGet・固定版・lockを変更しない |
| 22 | 5つのViewがあるだけで過剰分割とは断定しない | 実際に移設する4編集面と1設定コンテナーだけ。カテゴリーごとの新VMや汎用フォームは作らない |
| 23、24、25、26 | 小さな明確化として採用 | §5.3の値の所有先、§6.3のAutomation ID／Tab順を具体化。本文の業務工程は「ステップ」に統一 |
| 28 | ファイル総数だけでは「production 1〜3ファイル＋対応テスト」の違反にならないため、その理由での分割は不採用 | テストを別に数える。文書baselineの同時更新は整合性を保つための明示例外とする |
| 29、35 | 既存の境界を再確認し明確化 | 保存定義適用でImported Prompt一覧を変更しない。自動テストと隔離利用者環境での手動保存を区別 |
| 31、32 | artifact保管とスクロール例外を明確化 | 現行`.gitignore`の`/artifacts/`を確認。狭小時の縦スクロールは例外として記録し、通常目標の達成に数えない |
| 33、34 | 冗長な注記と合成例は具体的な不具合ではないため、修正必須の指摘としては撤回 | 依存優先・合成例の注記は有用な説明として残す |
| 36〜45 | 個別根拠がないため、10件の指摘としては採用しない | 本書で実際に直した表記だけを変更内容とする。旧件数や旧PASSを実装品質の証拠にしない |

### 2.1 実ファイルで再確認した基点

| ID | 確認した事実 | 計画への影響・根拠 |
|---|---|---|
| C01 | 要求は4ステップ、縦スクロール到達、警告全文、20,000回答行上限を規定 | ページ切替と設定保存は承認後に要求へ反映。[要求§9・11・14・15・18・19](../../../../docs/requirements-definition.md) |
| C02 | ウィンドウは最小1024×720、初期1180×800 | 寸法を引き上げない。[MainWindow.axaml](../../../../src/StudyReportEvaluator.App/Views/MainWindow.axaml) |
| C03 | 現在位置より前の工程を`Completed`とする既存ナビゲーションがある | 訪問済みと処理成功を区別。[WorkflowNavigator.cs](../../../../src/StudyReportEvaluator.App/Navigation/WorkflowNavigator.cs) |
| C04 | Design離脱でInputへ同期し、Design再訪でVMを再生成する | 設定画面が古いDesign参照を保持しないよう、同期と選択復元を一体で扱う。[MainWindowViewModel.cs](../../../../src/StudyReportEvaluator.App/ViewModels/MainWindowViewModel.cs) |
| C05 | `SetPrimaryColumn`は選択した列の`HeaderText`を設問文へ反映する | 明示保存時の含有情報として開示。[InputViewModel.cs](../../../../src/StudyReportEvaluator.App/ViewModels/InputViewModel.cs) |
| C06 | `Configure`は出力先、再開指定、直前run、進捗を初期化し、実行中の再構成を拒否する | 単なる往復と入力／定義変更を分ける。[ExecutionViewModel.cs](../../../../src/StudyReportEvaluator.App/ViewModels/ExecutionViewModel.cs) |
| C07 | `CurrentEditorViewModel`は`UiObservableObject`型で、4種類のDataTemplateがある | Settings VMも既存基底へ合わせる。5番目のworkflow enumは不要。[MainWindowViewModel.cs](../../../../src/StudyReportEvaluator.App/ViewModels/MainWindowViewModel.cs)、[MainWindow.axaml](../../../../src/StudyReportEvaluator.App/Views/MainWindow.axaml) |
| C08 | 主要入力のMinHeightは44、ステップボタンは72。余白・色等も既存Stylesにある | スタイルファイルの追加ではなく既存責務を整理。[Accessibility.axaml](../../../../src/StudyReportEvaluator.App/Styles/Accessibility.axaml) |
| C09 | 設定storeの構成接続はなく、canonical serializerにDeserializeはない | 保存はAppに小さく追加し、canonical hashを往復のoracleに利用。[ServiceRegistration.cs](../../../../src/StudyReportEvaluator.App/Composition/ServiceRegistration.cs)、[CanonicalDefinitionSerializer.cs](../../../../src/StudyReportEvaluator.Core/Serialization/CanonicalDefinitionSerializer.cs) |
| C10 | 530行のUI試験は`U04TestSupport`等の合成データで構成できる | `sample/SampleReport.xlsx`の実データsmokeと区別。[ResponsiveLayoutTests.cs](../../../../tests/StudyReportEvaluator.App.Tests/UI/ResponsiveLayoutTests.cs)、[PrimaryJourneyAccessibilityTests.cs](../../../../tests/StudyReportEvaluator.App.Tests/UI/PrimaryJourneyAccessibilityTests.cs) |
| C11 | 文書テストは要求v4.5／基準日、AC34件、C44件、ST-UC26件、TR33件、対応配列を明示検証する | 変更した契約の期待値だけを同期。現在の公開文書一覧は9件。[DocumentationContractTests.cs](../../../../tests/StudyReportEvaluator.App.Tests/Content/DocumentationContractTests.cs) |
| C12 | 単一EXEの文書集合は17件。ZIPとの一致、publish関数との一致を別々に検証する | 新文書2件と画像1件を全経路へ追加。[WindowsSingleFileProfileTests.cs](../../../../tests/StudyReportEvaluator.App.Tests/Packaging/WindowsSingleFileProfileTests.cs)、[WindowsSingleFilePublishTests.cs](../../../../tests/StudyReportEvaluator.App.Tests/Packaging/WindowsSingleFilePublishTests.cs) |

実ファイルの確認は静的調査であり、本改修のビルド・UI実測・性能測定・配布実物の試験結果ではない。

## 3. UX/UIの根拠と選択

R01〜R08およびiconのライセンス調査は、旧版§3に記録された**2026-09-07の本文確認**を継承する。本更新ではR09を再確認し、IO試験を正すためR10を追加確認した。全資料を今回再取得したとは扱わない。発行日、更新日、閲覧日を区別し、不明な更新日を著作権年で補わない。

| ID | 資料 | 採用する原則・限界 |
|---|---|---|
| R01 | Ben Shneiderman, *Direct Manipulation: A Step Beyond Programming Languages*, Computer, 1983年8月。[著者公開PDF](https://www.cs.umd.edu/~ben/papers/Shneiderman1983Direct.pdf) | 対象と操作結果の可視性、段階的な操作。説明カードやアニメーションを増やす根拠にはしない |
| R02 | Hutchins / Hollan / Norman, *Direct Manipulation Interfaces*, HCI 1, pp.311–338, 1985。[著者公開PDF](https://hci.ucsd.edu/hollan/Pubs/direct-manip.pdf) | p.326の意味を直接示す出力表現を、対象数・配点・保存状態へ応用。改善率の証拠ではない |
| R03 | Nielsen, *Progressive Disclosure*, 2006-12-03。[本文](https://www.nngroup.com/articles/progressive-disclosure/) | 重要な操作を主画面、低頻度操作を明瞭な入口の先へ。頻用機能を隠さない。頻度分類は本人確認で調整 |
| R04 | Budiu, *Wizards: Definition and Design Recommendations*, 2017-06-25。[本文](https://www.nngroup.com/articles/wizards/) | 現在地、具体的な前後操作、状態保持。工程を細分化しすぎない |
| R05 | Harley, *Icon Usability*, 2014-07-27。[本文](https://www.nngroup.com/articles/icon-usability/) | アイコンと常時見える文字ラベルを併用。hoverだけの説明や絵文字だけの操作を避ける |
| R06 | Microsoft [App settings](https://learn.microsoft.com/windows/apps/design/app-settings/guidelines-for-app-settings)、[Navigation basics](https://learn.microsoft.com/windows/apps/design/basics/navigation-basics) | 通常業務を設定へ隠さず、少数の行先と一覧／詳細を整理。WinUIへ移植する意味ではない |
| R07 | Fluent 2 [Iconography](https://fluent2.microsoft.design/iconography)、[Layout](https://fluent2.microsoft.design/layout) | 少数のsystem icons、4を基準とする余白、reflow。テーマ切替機能や全asset導入は不要 |
| R08 | W3C [WCAG 2.2](https://www.w3.org/TR/WCAG22/) §1.4.10／2.5.8 | reflowと操作対象の考え方を参考にする。全面スクロール禁止規定ではない。CSS pxとDIPを同一視せず、製品の適合認証を主張しない |
| R09 | Avalonia [Themes](https://github.com/AvaloniaUI/avalonia-docs/blob/main/docs/styling/themes.md)、[ListBox](https://github.com/AvaloniaUI/avalonia-docs/blob/main/docs/how-to/listbox-how-to.md) | Compact密度と有限高さによる仮想化。資料は最新mainであり、固定12.1.1でのcompile／描画を別途検証する |
| R10 | Microsoft [File Attributes](https://learn.microsoft.com/openspecs/windows_protocols/ms-fscc/ca28ec38-f155-4768-81d6-4bfeb8586fc9)、[FileShare](https://learn.microsoft.com/dotnet/api/system.io.fileshare?view=net-10.0) | ReadOnlyディレクトリでも配下fileの作成・削除は可能。共有拒否を使ったIO試験の根拠。UX改善の根拠ではない |

Fluent System Iconsは使用する個別assetとrevisionをT12で確定し、[LICENSE](https://github.com/microsoft/fluentui-system-icons/blob/main/LICENSE)と[NOTICE](https://github.com/microsoft/fluentui-system-icons/blob/main/NOTICE)に従う内容を同梱する。採用前のassetを「導入済み」と記録しない。

**選択理由は、利用者の要望と現行実装に、小さな変更で適合すること。** 操作頻度、所要時間、誤操作率、教育的効果は未測定であり、普遍的な最適性や改善率を保証しない。

## 4. 承認する選択肢とデフォルト案

すべて承認待ち。D01〜D19の番号は維持し、旧修正で曖昧になった出力先をD20として明示した。D18／D19の承認が既に得られたとは扱わない。

| ID | 判断事項 | 選択肢 | 推奨デフォルト・理由 |
|---|---|---|---|
| D01 | スクロール不要の範囲 | 全内容で禁止／通常サイズのみ／従来どおり | **最小1024×720 DIP以上の通常画面は外側スクロール不要**。長文・dropdown・狭小／拡大は到達性のため例外。無制限の情報を切り捨てない |
| D02 | 多数項目 | 全カード／リスト内スクロール／ページ切替 | **コンパクト一覧＋ページ切替＋対象行移動**。表示件数を業務上限にしない |
| D03 | 保存範囲 | 共通設定だけ／共通＋定義／結果も全保存 | **共通設定＋採点定義1件**。詳細編集も保存でき、checkpointを二重化しない |
| D04 | 保存定義の適用 | 自動／同名なら自動／明示適用 | **読込済みExcelへ明示適用**。別授業の範囲・配点を黙って使わない |
| D05 | 設定へ移す範囲 | 全項目／低頻度項目／現状維持 | **主画面に対象・配点・結果、設定に詳細編集**。主画面には有効値と変更入口を残す |
| D06 | 警告表示 | 省略／折り畳み／全文固定 | **設定を含め全文常時表示**。AC-017の文面・nonblockingを維持 |
| D07 | 保存場所 | EXE横／入力横／利用者別 | **OSのLocalApplicationData配下の`StudyReportEvaluator/setting.txt`**。Windowsでは通常`%LOCALAPPDATA%`配下。入力、配布物、抽出cacheと分離 |
| D08 | 内部形式 | key=value／JSON／YAML | **UTF-8 JSON・拡張子.txt**。複数行Prompt、配列、decimalを既存.NETで扱う |
| D09 | 反映と永続化 | 自動保存／保存時だけ反映／即時反映＋明示保存 | **有効な編集は次回用draftへ、diskは明示保存だけ**。操作結果と保存結果を区別 |
| D10 | 設定の表示 | modal／drawer／内容画面／第5工程 | **同一ウィンドウの独立した内容画面**。長文の領域を確保し、元のステップへ戻る |
| D11 | 再訪で保持するもの | 値だけ／値と選択／全履歴 | **値・対象ID・ページ・カテゴリを同一起動中に保持**。Undoエンジンや永続履歴は作らない |
| D12 | 実行中の編集 | 全画面lock／閲覧のみ／次回用編集可 | **戻る・次回用編集可、現在runは固定**。保存定義の一括適用はrun終了後。停止操作を常時利用可能にする |
| D13 | 保存modelが利用不可 | 自動fallback／警告付きfallback／未選択 | **有効な選択は未選択とし明示変更を要求**。保存希望IDと確認状態を混同せず、費用・条件を勝手に変えない |
| D14 | 見た目・icon | 別framework／全package／少数asset | **既存FluentTheme＋必要な静的iconだけ**。文字ラベルを維持し、新規依存を増やさない |
| D15 | 破損・未知設定版 | 自動修復／起動停止／保持して既定値 | **fileを保持し通知、offline編集は継続**。明示保存以外で上書きしない |
| D16 | 同時起動での保存 | lock／merge基盤／最後の保存 | **atomic置換、最後に成功した保存が優先**。共同編集は対象外。同時編集の統合を保証しない |
| D17 | 製品版・公開 | 即公開／ローカル実装まで | **Unreleased更新まで**。版上げ、commit／push、公開は別承認 |
| D18 | 要求文書の改訂版 | 据え置き／今回更新／公開時更新 | **承認内容を反映するT01で版・基準日更新**。基点がv4.5のままならv4.6を案とし、実際の承認日を記録。変更履歴の識別が理由であり、製品版は上げない |
| D19 | 非公開MSIXの文書 | 放置／文書だけ同期／installer拡張 | **同梱文書と検証リストのみ同期**。既存の非公開機構回帰を保持し、署名・公開・install要件を拡張しない |
| D20 | 保存出力先の復元 | 毎回result／都度適用／明示指定を保持 | **明示指定を再起動・入力変更後も保持。未指定時だけ入力隣接result**。保存した値が消えず、主画面で次回の実効出力先を確認できる。空欄で既定へ戻せる |

D01の例外まで全面禁止する場合は長文編集を追加ページへ分割する必要があり、操作回数が増える。現時点のデフォルトにはしない。本人確認で通常評価やCustom編集が高頻度と分かった場合は、D05の配置だけを再調整する。

## 5. 画面と状態表示

### 5.1 共通ウィンドウ

- 小さなアプリ名、「歯車＋設定」、固定の`1 入力 / 2 採点設計 / 3 実行 / 4 結果`を置く。
- 現在地・訪問済み・準備状態を区別する。開いただけで実行成功のチェックを付けない。
- 指定警告文は内容を変えず、設定画面でも独立領域に全文表示する。確認や同意を操作条件にしない。
- 前後操作は「入力へ戻る」「採点設計へ」「実行内容を確認」のように行先を示す。結果画面に無効な「最終ステップ」主ボタンを残さない。
- 実行中は別の画面でも進捗への入口と停止を使える。現在runと次回設定のラベルを分ける。
- 重複見出し、大きなhero、説明カード、装飾badgeを削減する。固定領域をoverlayで重ねて入力やfocusを覆わない。

### 5.2 主画面と設定の分担

| ステップ | 主画面に残す | 設定へ移す | 状態として見せるもの |
|---|---|---|---|
| 入力 | picker、直接path、読込、sheet、質問行1/2、回答範囲、対象設問、有効化、主回答列、選択設問text、必須エラー | 候補の詳細・再適用、補助列、設問名・複製・並替え等 | 回答行数、対象設問数、主列／補助列、主列変更で反映された設問文 |
| 採点設計 | Base／Special／類似度係数、設問配点、有効状態、均等配分、評価方法・観点の概要、配点エラー | 定義名・revision・丸め、通常評価・criterionのCRUD／range／weight、固有評価、Prompt | 合計・不足／超過、使用する評価方法、必要な固有項目の有無 |
| 実行 | 認証の明示確認・login／取消、通常modelとautoの有効状態、新規／再開、partial指定、実効保存先、開始／停止、実progress、技術エラー | model変更、並列度、出力先変更、runtime診断 | 対象、次回条件、現在run条件、予定名／予約済みpath、保存済み行、完了状態 |
| 結果 | final／partial、成功／一部失敗／取消、件数、行別点数、選択行内訳、override、別名出力 | **結果とoverrideは設定へ移さない** | 0と空欄、元の完成版と未保存修正版、前回runの結果であること |

主画面の「変更」から、設定の該当カテゴリと同じ対象IDを1操作で開く。入力欄を主画面と設定の両方へ複製しない。Executionには現存しないタイムアウト設定欄を追加しない。

### 5.3 設定の5カテゴリと所有先

| カテゴリ | 内容 | 値の所有先 |
|---|---|---|
| 共通 | 通常model、並列度、明示出力先、設定fileの場所・状態、runtime情報 | 共通設定のdraft。runtime・認証は読取専用で保存対象外 |
| 共通内の採点定義情報 | 定義名、revision、丸め | **Designの採点定義**。実行設定の値とは混同しない。独立した6番目のカテゴリは作らない |
| 入力詳細 | 選択設問、候補、補助列、mappingの詳細操作 | Inputの既存draft |
| 通常評価 | 設問／evaluator／criterion選択、既存CRUD、Knowledgeの読取、Custom編集 | Designの既存draft |
| 固有評価 | 設問／固有項目選択、source、補助列、Prompt、有効状態 | Designの既存draft |
| 読込Prompt | 起動引数順の一覧、本文、適用先、明示適用 | 既存Imported Prompt一覧。適用後のtemplateはDesignのdraft |

- 対象がないカテゴリも位置は変えず、利用できない理由を表示する。
- 保存状態は「未保存」「保存済み」「保存失敗」。アプリ内反映とdisk保存を区別する。
- 設定カテゴリの変更はworkflowを進めない。「設定から戻る」で元のステップと対象へ復帰する。
- 起動Promptの件数と設定への入口は採点設計から見える。本文・適用の移動に合わせて要求§12.2を改訂する。

### 5.4 結果の一覧／詳細

一覧は元行番号、最終点、設問獲得点合計、固有獲得点、類似度減点、状態を示す。選択行の詳細で設問内訳、criterionのraw／override／effective、既存計算previewと別名出力を扱う。

設問数に比例して横へ列を増やさない。多数の設問・criterionは対象を選択して編集し、一覧のページが変わってもoverrideは元のResults collectionに保持する。選択用の表示copyに値を閉じ込めない。

「一覧」「詳細・修正」は結果の表示切替であり、追加の業務ステップではない。任意overrideを必須工程にしない。

## 6. 寸法・密度・到達性の契約

### 6.1 通常サイズ

- 最小1024×720 DIP、初期1180×800 DIPを変更しない。Windowの指定値と実ClientSizeを別々に記録する。
- 通常状態では、警告全文、現在地、主要操作、有効値、前後移動を初期offsetで確認できる。
- 外側ScrollViewerが残る場合は`Extent <= Viewport`と主要Controlの完全包含を確認する。scrollbarをDisabledにしただけ、clippingしただけでは合格にしない。
- 一覧の表示件数は実際の残領域と行高から決める。正の件数が収まること、領域を広げたときに行数を増やせることを検証する。ページサイズの設定UIは作らない。
- 本文・入力14 DIP、主操作targetの最小44 DIPを維持する。Compact密度でも44未満にしない。**MinHeightは下限であり、すべてのControlの実高さが44に固定されるという意味ではない。**
- 二重margin、過大なpadding、重複説明を先に減らす。4／8／12／16を余白の基準とする。今回の共通スタイルは既存`Accessibility.axaml`と`App.axaml`で整理し、別のthemeファイルは増やさない。
- 達成する行数・余白をT26で記録する。未測定の「必ず5件／6行」や、文字を読めないほど縮めた密度を目標にしない。

### 6.2 多数項目と例外

- 設問・結果の一覧は、前／次、現在範囲、全件数を表示する。元の結果行番号による移動を用意する。
- 追加・削除・並替え・リサイズ時はページ範囲を補正する。選択対象が残ればIDで保持し、削除時だけ隣接対象へ移す。空一覧も明示する。
- 有限高さのGrid等に既存仮想化リストを配置する。全件のControl生成や無限高さの入れ子を避ける。[R09]
- 長い設問文・Prompt、全文path、dropdown候補には局所スクロールを許容する。全文へkeyboardでも到達できることを試験する。
- 多数の評価方法・criterionは選択対象の詳細を表示する。全編集カードを縦に積み、通常画面の例外へ丸ごと逃がさない。
- 複数エラーは件数と現在の対象を示し、次の問題へ移動できる。隠したエラーを検証成功として扱わない。
- 760×600 standalone、200%表示ではreflowと表示行数削減を先に行う。不足時だけ本文の縦スクロールを許容し、固定領域が操作を覆わないことを確認する。
- 論理作業領域が最小windowより小さい環境まで無条件に収まるとは保証しない。例外の成功を通常サイズの非スクロール達成へ数えない。

### 6.3 Keyboardとアクセシビリティ

- Tab／Shift+Tab／Enter／Space、カテゴリ切替後のfocus、設定終了時のfocus復帰を試験する。
- 既存の安定したAutomation IDは移設先でも可能な限り保持。新しい固定操作は`SettingsOpen`等の意味の分かるID、動的対象は既存同様の対象ID＋操作名とする。表示ページ内の連番・表示名・翻訳文字列をIDにせず、結果の元行番号のような既存の不変識別子は維持する。
- 同じウィンドウ内でIDを重複させない。主画面と設定に同じ入力欄を二重配置しない。設定カテゴリは必要な編集面だけを表示する。
- 既存TabIndexを機械的にコピーせず、固定ナビ→内容→前後操作の順序へ調整し、テストの期待順も同期する。
- 色以外の状態表現、日本語のAccessible Name、focusと文字・iconのcontrastを維持する。
- headlessのlayout確認と、native WindowsのDPI／Narrator確認は別の証跡。HTMLのモックをAvalonia実画面の検証に代用しない。

## 7. `setting.txt` の保存・復元契約

### 7.1 データと機密性

App層に明示的な`ApplicationSettings`型と、1ファイル用の`SettingsFileStore`を置く。CoreへfilesystemやUIを持ち込まず、既存の`QuantificationDefinition`を利用する。

| 保存項目 | 契約 |
|---|---|
| 設定schema version | 整数1。canonical schema、要求版、製品版とは独立 |
| 通常modelの希望ID | 未指定可。明示確認した候補からの利用者選択を保存。復元後の利用可能性は別途確認 |
| 最大並列度 | 既定1、既存の1〜3のみ |
| 明示出力先 | absolute pathまたは未指定。**自動算出した入力隣接resultのpathは明示指定として保存しない** |
| 採点定義1件 | 任意。ID・name・revision、sheet／header／行範囲、Base／Special／類似度係数、設問・text・mapping・配点・enabled、evaluator／criterion／range／weight、固有評価、適用済みPrompt、丸めを保持 |

**保存しないもの**：入力xlsxのpath・bytes、回答行を自動収集した本文、AI結果・reason・evidence・参照回答、run／checkpoint状態、credential・login状態・CLI hash、警告の承認状態、未適用Promptのfile一覧・本文、Control・Command・選択ID・表示ページ・操作履歴。

**定義を明示保存すると、既存の主回答列選択によって見出しセルから取り込まれた設問文も平文で含まれる。** Prompt・評価基準等へ利用者が貼り付けた内容も残る。読込や主列変更だけで自動書込するわけではない。「回答セルの自動収集を追加しない」と「機密な本文が絶対に含まれない」は別であり、後者を保証しない。

設定にはsheet名、設問文、明示出力path等が含まれ得る。OSの利用者別保存先を使っても暗号化containerではない。設定を公開物・画像・生ログに含めず、不要になった設定fileの取扱いは利用者ガイドで説明する。アプリによる自動削除機能は追加しない。

### 7.2 読込・明示保存・失敗

1. production構成で保存場所をOS標準APIから1回解決し、設定を読み込む。テスト／単体VMの既定コンストラクターから実利用者フォルダーへアクセスしない。
2. fileなしは既存の既定値で起動。最初の明示保存までfileを作らない。読込完了前に未読の定義を空で上書きできる状態にしない。
3. UTF-8のBOMあり／なしを受理し、JSONの型・必須値・enum・範囲を検査する。schema欠損・不正型・1以外を区別し、未知schemaを暗黙変換しない。同schemaの未知項目も拒否し、黙って捨てて再保存しない。
4. 設定JSONの復元はSystem.Text.Jsonと既存domain型・validatorを使う。canonical serializerへ存在しない読込APIを仮定しない。ID・decimal・入れ子・Unicode・Promptとcanonical hashの往復一致を検証する。
5. 保存対象に定義があれば既存の定義・Prompt・配点検証を通す。不正なdraftはメモリへ保持し、旧fileを上書きしない。共通設定だけを保存できる入力未読込状態では、読込済みの保存定義を維持する。
6. 保存時点の値を固定し、同じdirectoryの一意tempへ書込・flush・closeしてから原子的に置換する。保存開始だけでは「保存済み」にしない。保存中にdraftを再編集した場合は、保存成功後も現在draftの「未保存」を維持する。先に旧fileを削除したり、旧fileへ直接切り詰め書込したりしない。
7. 失敗時は旧fileと現在draftを保持して「未保存／保存失敗」。自分が作ったtempだけを後始末する。例外全文や本文をlogへ出さず、安全な状態と対処を示す。再帰cleanupやbackup engineは作らない。
8. 破損・未知schema・読込拒否でもoffline GUIを継続する。元fileを自動修復・削除せず、上書きは通知を見た利用者の明示保存だけとする。
9. 同一画面の二重保存は保存中状態で防ぐ。別プロセス間のmerge・監視・履歴は追加しない。最後に成功した置換が残る制約を明記する。電源断や任意ネットワークfilesystemまで無条件の耐久性を保証しない。

自動試験ではstoreへ一時directoryのabsolute pathを渡す。保存先切替用の製品CLI引数・環境変数・汎用filesystem interfaceは追加しない。

**Windowsの失敗試験**：一時領域の既存fileを`FileShare.None`等で保持した読込／置換拒否、親directory予定pathが通常fileである作成失敗、読取専用の既存file等、目的の失敗が実際に起きたと確認できる条件を使う。旧fileはハンドル解放後にbytes／hashを比較し、属性・ハンドルをfinallyで戻す。ReadOnlyディレクトリだけを「アクセス拒否」の証拠にしない。[R10]

### 7.3 保存定義の明示適用

- 起動時は保存定義を保持するだけで、Input／Designに自動適用しない。
- Excel読込後、保存定義の設問数、sheet、行範囲、mappingを示して「現在の入力に適用」を提供する。実行中は一括適用を無効にし理由を示す。
- 保存定義のheader行に対応するmetadataを、既存read-only loaderで取得する。sheet・行・列・定義全体を検証し、旧headerのmetadataを流用しない。
- 成功時だけInputのmetadata・選択値・draftとDesignを一緒に更新する。失敗・取消は現在の状態と保存fileを変更しない。
- ID・順序・設問文・Prompt・配点を保存時のまま保持する。適用時に候補再生成でIDを変えたり、設問文を別header値へ黙って置換したりしない。
- その後に利用者が主回答列を変更した場合は、既存どおり交差セル値へ設問文を更新する。
- `--prompt`の取込一覧、本文、順序は保存定義の適用で変更・消去しない。未適用のPromptは、既存の明示適用まで定義へcopyしない。
- 別Excelの意味的な適合は利用者が確認する。列・sheetの存在だけを授業内容の一致とみなさない。resumeの可否は既存checkpoint admissionで判定する。

### 7.4 復元の優先順位

| 対象 | 次回実行へ使う値・状態 |
|---|---|
| 共通設定 | 今回の明示編集 → 保存済み共通設定 → 従来の既定値 |
| 明示出力先あり | 今回の指定、なければ保存済み指定を使う。新しいExcelでも保持し、主画面へ実効pathを表示 |
| 明示出力先なし | 読込済み入力に隣接する`result`を都度算出。入力未選択なら「入力後に決定」。空欄への明示編集もこの状態への変更として保持 |
| 指定先が利用不可 | 無断で別pathへfallbackしない。設定修正または既存の作成可能先検証を促す。復元だけでdirectoryを作らない |
| 保存定義 | 明示適用された場合だけ現在draftへ反映。保存済みというだけでは優先しない |
| model | 希望IDは保持し、明示的な状態確認の結果に存在するときだけ実効選択にする。欠落時は実効選択を未選択にして変更を促す |
| model未指定の初回 | 現行の明示状態確認後の初期選択を維持。確認前はAI-readyとしない。通常modelと固定`auto`の検証を分ける |
| 新規／再開・partial | 永続化しない。同じ入力・定義の往復では保持し、入力／定義変更時は再開指定を再確認または解除して理由を示す |
| 起動引数 | `--input`の対象とcwd解決、`--prompt`の読込順は現行どおり。共通設定の読込を新たな自動適用・自動実行の契機にしない |

確認失敗で一時的に実効modelが未選択になっても、明示編集していない保存希望IDを空へ書き換えない。開始requestには実際に利用可能と確認した値だけを入れる。

login完了、設定読込・保存、画面遷移から認証確認・AI評価を自動開始しない。開始時のrequest／immutable snapshotに現在の有効値を固定し、後からの設定編集を進行中runへ混ぜない。

## 8. 状態の所有と画面遷移

### 8.1 同期の責務

- Input／Designの既存draftが採点定義を所有する。Settings VMはその編集面を束ねるだけとし、第三の汎用draft storeを作らない。
- MainWindow VMはステップ間・設定開閉の同期、Settings VMは設定カテゴリ間の編集境界を扱う。保存前は直前の編集元を確定し、その最新値を使う。同じ境界で二重に同期しない。
- InputとDesignを交互に編集しても、古いcopyで新しい値を上書きしない。Designを更新・再生成した場合は、Settingsの参照・表示も更新し、残る選択対象をIDで復元する。
- `CurrentEditorViewModel`で設定内容を排他的に選び、Settings VMは既存の`UiObservableObject`に合わせる。4ステップのenumと到達範囲を増やさない。
- 実行中は`Configure`しない。次回用draftを保持し、進捗・停止・現在runの予約pathをそのまま表示する。
- 非実行中の単なる往復では再初期化しない。実際に入力・定義が変わった場合だけ次回requestの準備を更新する。Resultsから戻る場合にも古い定義を次回用と誤認しない。
- 過去の結果は結果VMが保持し「前回run」と表示する。次回draftの編集を、過去結果の再採点や保存済みxlsxの変更として表示しない。
- 設定を開いている間にrunが完了しても、設定を閉じたり結果へ強制移動したりしない。結果の到着だけを通知する。
- 数値入力途中や変換エラーも往復試験に含める。再現した編集面だけで未確定textを保持し、全field用の新入力フレームワークは作らない。

### 8.2 表示の変化と根拠

以下は**合成の設計例**であり、実データ・実測成果ではない。

| 操作 | 表示の変化 | データ源・禁止する推測 |
|---|---|---|
| Excel読込 | 未選択 → 読込済み／100行／2設問 | 既存metadata・範囲。氏名や回答を概要へ自動表示しない |
| 主列変更 | B列の設問文 → C列のheader値 | `SetPrimaryColumn`。空cellへ代替文を生成しない |
| 配点変更 | Base60＋30＋20＝110 → Base60＋30＋10＝100 | 既存decimal計算。表示丸めで100に見せない |
| 固有評価設定 | 固有配点あり・項目不足 → 対象項目あり・検証済み | 既存validator。Special=0時のAI非実行契約を維持 |
| Prompt適用 | 対象のtemplate変更・未保存 | 既存のcopyのみ。AI結果previewは新設しない |
| 明示出力先を保存 | 指定先・未保存 → 指定先・保存済み | 保存成功だけ。次回起動でも値を保持 |
| 出力先を空欄へ変更 | 明示指定 → 入力隣接resultを使用・未保存 | 実効値と保存する未指定状態を区別 |
| 実行前 | 完成予定：別xlsx、元sheet＋設定・参照回答・結果・実行記録 | 名前は候補。存在する完成fileとしない |
| run開始後 | 予約済み → checkpoint保存中 → 保存済み行増加 | 実progress。経過時間から偽の進捗を生成しない |
| run終了 | 完成／空欄の評価あり／取消・partialあり | RunSummaryとfinalization。完了件数を成功件数と同一視しない |
| override | raw → override／preview更新／修正版未保存 → 別名出力済み | 既存calculator・output境界。元の完成fileは変更しない |

類似度係数0を類似度処理自体の無効化と解釈しない。UIだけに採点式を複製せず、既存の計算・検証結果を使う。

## 9. タスクとファイル対応

### 9.1 実施規則

- **T01〜T39は全て未着手。G0前に実施しない。** 番号は旧版との追跡用に維持し、依存順を優先する。
- 原則は1目的、1〜3のproductionファイルと対応テスト。T01の文書baseline同期、T28の画像一式、T37〜38の独立同梱リスト同期は、部分更新を防ぐための明示した整合性単位とする。
- 「新」は新規提案。他は既存ファイル。実装時に重複を再確認する。コード分割だけのタスクや将来用interfaceを追加しない。
- 各タスクへ渡すcontextは本書の対象節、関連要求、直前タスクの契約、編集対象に限定する。引継ぎは変更ファイル・契約・実施テスト・未解決点だけにする。
- 同一ファイル編集は依存関係で直列化。同一workspaceのbuild／test／画像生成／packageは単一runnerで実行する。
- 追加テストは名前指定で実行し、古いdiscovery件数だけの成功を避ける。未実装項目や未実施試験をPASSにしない。

path省略：`A/`＝`src/StudyReportEvaluator.App/`、`AT/`＝`tests/StudyReportEvaluator.App.Tests/`、`UI/`＝`tests/StudyReportEvaluator.App.Tests/UI/`、`D/`＝`docs/`、`DD/`＝`dev/docs/`。`*.axaml(.cs)`はXAMLと同名code-behindの2ファイルを表す。

### 9.2 要求・データ・ViewModel

| ID | 作業 | 依存 | 編集するファイル・入力context | 完了条件 |
|---|---|---|---|---|
| T01 | 承認範囲と要求baselineの同期 | G0 | §4〜8、要求§9.1／11／12.2／13.5／14／15／17〜19／22。編集：`D/requirements-definition.md`、新`DD/ui-layout-contract.md`、`DD/traceability.md`、`DD/readme-claim-ledger.md`、`SystemTest-prompt.md`、`AT/Content/DocumentationContractTests.cs`、`AT/E2E/RealDataSystemSmokeTests.cs`の要求版metadata | D18に従い文書版・基準日を同期。AC-016／TR-17にページ切替と例外、設定保存・出力先復元を追記。既存IDを再番号付けせず新IDは末尾。新機能は未実装、試験は未実施と記録。関連文書contractが通り、過去evidenceは変更しない |
| T02 | 最小設定データ契約 | T01 | §7.1。新`A/Settings/ApplicationSettings.cs`、新`AT/Settings/ApplicationSettingsTests.cs` | 共通値＋任意定義1件。希望model／明示出力先の未指定と実効値を分離。null、decimal、ID、入れ子、Unicode、Promptの往復一致 |
| T03 | 読込・明示atomic保存 | T02 | §7.2。新`A/Settings/SettingsFileStore.cs`、新`AT/Settings/SettingsFileStoreTests.cs` | fileなし、BOM、不正JSON／schema、読込／置換拒否、旧bytes保持、temp後始末。Windowsの実FS条件で失敗を確認。実ユーザーfolder・汎用filesystem抽象なし |
| T04 | 保存定義の明示適用 | T02 | §7.3、既存loader／validator。編集：`A/ViewModels/InputViewModel.cs`、新`UI/SavedDefinitionApplicationTests.cs` | 検証成功時だけmetadata・範囲・draftを一致。header違い、失敗／取消時無変更、ID・Prompt・配点保持、AI送信0 |
| T05 | Design同期と選択保持 | T01 | §8.1、既存Commit／同期。編集：`A/ViewModels/QuantificationDesignViewModel.cs`、新`UI/DesignStateTests.cs` | 設問／評価方法／criterion／固有項目のIDを保持。削除時のみ選択補正。Imported Promptの順序・本文を維持 |
| T06 | 次回実行設定と現在runを分離 | T02 | §7.4／8.1、Configure／ApplyAuthentication／StartAsync。編集：`A/ViewModels/ExecutionViewModel.cs`、新`UI/ExecutionSettingsTests.cs` | 明示出力先保持、未指定時result算出、model希望と有効選択の分離。未変更往復は非初期化。新入力／定義、resume指定、実行中編集のrequest不変、no-auto-login/run |
| T07 | 結果の選択・ページ・修正版状態 | T01 | §5.4／8.2。編集：`A/ViewModels/ResultsOutputViewModel.cs`、新`UI/ResultsPresentationTests.cs` | ページ移動後もoverride保持。元行番号への移動、空一覧、最終ページ補正、0／blank、完成版／未保存修正版を区別 |
| T08 | Inputの一覧・ページ・概要 | T04 | §5.2／6.2。編集：`A/ViewModels/InputViewModel.cs`、新`UI/InputPresentationTests.cs` | 全件到達、選択ID維持、追加／削除／並替え、主列の設問文同期。件数はmetadata・draftから算出 |
| T09 | Designの一覧・配点概要 | T05 | §5.2／6.2。編集：`A/ViewModels/QuantificationDesignViewModel.cs`、新`UI/DesignPresentationTests.cs` | 合計・過不足はexact decimal。表示ページで配点・enabledを変えない。未実行scoreを生成しない |
| T10 | 設定VMの保存・適用・編集境界 | T03,T04,T05,T06,T08,T09 | §7〜8。新`A/ViewModels/SettingsViewModel.cs`、新`UI/SettingsViewModelTests.cs` | 既存VMを参照し、最新編集元を保存。入力未読込時に保存定義を維持。希望modelを確認失敗だけで消さない。未保存／保存中／保存済み／失敗、5カテゴリ、対象を区別 |

T01では、まず承認した差分と追加IDを決め、次に要求・追跡文書を更新し、最後に**同じ変更単位で**対応する期待値を更新する。独立した期待mappingを維持し、被検査文書から期待値を自動生成して恒常的に成功させない。実データsmokeはmetadata編集だけで起動しない。

### 9.3 見た目と設定の編集面

| ID | 作業 | 依存 | 編集するファイル・入力context | 完了条件 |
|---|---|---|---|---|
| T11 | 既存共通スタイルの整理 | T01 | §6、R07／R09。編集：`A/App.axaml`、`A/Styles/Accessibility.axaml` | 固定版でcompile。44 DIP・focus・contrastを維持し、余白／文字／ステップ配置を整理。新themeファイルや未使用切替なし |
| T12 | 使用iconとライセンス | T11 | R05／R07、採用asset。新`A/Resources/WorkflowIcons.axaml`、編集`A/App.axaml`、新`D/third-party-notices.md` | 使用asset・revision・LICENSE／NOTICEを記録。静的Geometry等でrenderer依存なし。日本語ラベルを維持 |
| T13 | 入力詳細の編集面 | T08,T12 | 既存mapping操作。新`A/Views/MappingSettingsView.axaml(.cs)`、新`UI/MappingSettingsViewTests.cs` | 同じ設問の補助列・候補・詳細CRUDへ到達。ID、主列重複禁止、validatorを維持 |
| T14 | 通常評価の編集面 | T09,T12 | 既存evaluator／criterion操作。新`A/Views/EvaluatorSettingsView.axaml(.cs)`、新`UI/EvaluatorSettingsViewTests.cs` | CRUD／range／weight、Knowledge読取、Custom編集とpreviewを維持。対象選択で全項目へ到達 |
| T15 | 固有評価の編集面 | T09,T12 | 既存固有評価操作。新`A/Views/SpecialEvaluationSettingsView.axaml(.cs)`、新`UI/SpecialEvaluationSettingsViewTests.cs` | source／補助列／Prompt／enabledを正しいdraftへ反映。Special配点条件を維持 |
| T16 | 読込Promptの編集面 | T09,T12 | 既存Imported Prompt、要求§12.2。新`A/Views/ImportedPromptSettingsView.axaml(.cs)`、新`UI/ImportedPromptSettingsViewTests.cs` | basename・本文・順序・適用対象を保持。明示適用前copyなし、適用でAI送信・工程変更なし |
| T17 | 設定画面の組立 | T10,T13,T14,T15,T16 | §5.3。新`A/Views/SettingsView.axaml(.cs)`、新`UI/SettingsViewTests.cs` | 5カテゴリ、共通設定／定義情報の所有先、保存状態、戻る入口。対象なしの理由、カテゴリ間の編集保持 |

新規Viewは実際に移す4編集面＋コンテナーだけ。カテゴリ別VM、generic form builder、metadata駆動renderer、plugin抽象は作らない。さらにファイルを分ける場合は、現に移す責務と必要性を説明してから対象タスクを更新する。

### 9.4 4ステップと共通ウィンドウ

T22は依存が揃ったらT18〜T21より先に実施する。VMの表示契約を先に作り、ViewとDataTemplateは存在する型だけを参照する順序にする。

| ID | 作業 | 依存 | 編集するファイル・入力context | 完了条件 |
|---|---|---|---|---|
| T18 | Inputの主画面再配置 | T08,T12,T22 | §5.2／6。編集：`A/Views/InputView.axaml(.cs)`、`UI/InputViewTests.cs` | picker／取消／直接path／読込を維持。対象設問文と必須エラーが見え、同じ対象の設定を開く |
| T19 | Designを配点・概要中心へ | T09,T12,T17,T22 | §5.2／8。編集：`A/Views/QuantificationDesignView.axaml(.cs)`、`UI/QuantificationDesignViewTests.cs` | 合計・配点・均等配分・有効評価の概要。移設した機能へ到達でき、手動値を変えない |
| T20 | Executionを開始判断・進捗中心へ | T06,T12,T17,T22 | §5.2／7.4。編集：`A/Views/ExecutionView.axaml(.cs)`、`UI/ExecutionViewTests.cs` | 実効model／出力先の確認と変更入口。new／resume、partial、開始／取消。明示login・所有process境界・44 DIPを維持 |
| T21 | Resultsの一覧／詳細 | T07,T12,T22 | §5.4／8。編集：`A/Views/ResultsOutputView.axaml(.cs)`、`UI/ResultsOutputViewTests.cs` | 横に無限列を作らない。ページ・選択行詳細・override・別名出力・cleanup warningへ到達 |
| T22 | 画面切替と同期の統合 | T04,T05,T06,T07,T08,T09,T10 | §8.1。編集：`A/ViewModels/MainWindowViewModel.cs`、`A/Navigation/WorkflowNavigator.cs`、`UI/MainWindowTests.cs` | Settingsをstep外で`CurrentEditorViewModel`へ選択。HasEditorContentと型を整合。再訪・最新draft・設定中run完了・技術エラー修正先への移動を保証 |
| T23 | 固定領域・DataTemplate・構成接続 | T17,T18,T19,T20,T21,T22 | §5.1／7.2。編集：`A/Views/MainWindow.axaml(.cs)`、`A/Composition/ServiceRegistration.cs`、新`AT/Composition/SettingsCompositionTests.cs` | `CurrentStepContent`にSettingsのDataTemplateを登録。警告／ナビ／停止／前後操作、初期・最小寸法を維持。productionだけ設定読込、testは一時path注入 |

Navigatorのenumや汎用履歴stackは増やさない。既存の前工程への再訪を「新機能」として作り直さず、必要な保持と表示の改善に限定する。

### 9.5 結合・表示・画像

| ID | 作業 | 依存 | 編集するファイル・入力context | 完了条件 |
|---|---|---|---|---|
| T24 | 往復・編集・実行中の結合回帰 | T23 | §7.4／8.1。新`UI/WorkflowStateTests.cs` | Input→Design→Settings→Input→Execution、カテゴリ間の交互編集→保存、再訪／新入力、入力途中、実行中別画面、設定中完了、前回結果を検証 |
| T25 | Keyboard・警告・認証回帰 | T24 | §6.3。編集：`UI/PrimaryJourneyAccessibilityTests.cs`、`UI/EthicsWarningTests.cs`、`UI/CopilotLoginCommandTests.cs` | 移設先で既存操作を検証。全ステップ／設定の警告全文、focus復帰、Tab順、ID一意、停止、no-auto-login/run |
| T26 | 通常／例外layoutと境界 | T24 | §6／10.2。編集：`UI/ResponsiveLayoutTests.cs`、新`UI/CompactWorkflowLayoutTests.cs`、`DD/ui-layout-contract.md`の実測欄 | 通常非scrollと完全包含、例外到達、最終ページ、仮想化、実表示行数を記録。既存AssertVerticalScrollContractの意図を通常／例外へ分割し、単に削除しない |
| T27 | 設定を含むdeterministic E2E | T24 | 既存synthetic／fake境界。新`AT/E2E/SettingsWorkflowSystemTests.cs` | save→新VM／storeへ再読込→Excelへ明示適用→fake run→出力。ID／canonical hash、元本不変、数式／blank、resume、指定／未指定出力先、AI境界呼出条件 |
| T28 | 実Viewの画像再生成 | T25,T26,T27 | 既存capture。編集：`UI/DocumentationScreenshotTests.cs`、生成：`images/01-input-workbook.png`〜`images/07-output-export.png`、新`images/08-settings.png` | 移設先の実View・syntheticのみ。通常説明画像と最小ClientSize検証captureを分離。同環境2回一致。fakeの保存状態を実file作成証跡にしない |
| T29 | 画像台帳 | T28 | 実生成結果。編集：`images/README.md` | 8枚の内容、実寸法、日付、generator、synthetic／fake、未公開対象版を記録。native証跡と区別 |

画像はT28の単一担当で生成する。各画像を別担当が推測で描き直す運用はしない。

### 9.6 文書・配布・最終確認

| ID | 作業 | 依存 | 編集するファイル・入力context | 完了条件 |
|---|---|---|---|---|
| T30 | 利用開始と設定ガイド | T28 | 実UIと§7。編集：`D/getting-started.md`、`D/README.md`、新`D/settings.md` | 4ステップ、戻る、設定、明示保存／適用、出力先復元／既定へ戻す、scroll例外を説明。設定ガイドに未公開UIと公開版の区別を記載 |
| T31 | 機能・Custom・Prompt起動 | T28 | 実配置と既存計算契約。編集：`D/features.md`、`D/custom-evaluator-guide.md`、`D/prompt-launch.md` | 新しい変更先・有効値表示に一致。6 placeholder、起動引数順、明示適用、no-auto-runは不変 |
| T32 | Privacyと障害時ガイド | T03,T04,T20,T27 | §7.1〜7.4、実保存結果。編集：`D/privacy-and-data-handling.md`、`D/troubleshooting.md` | 見出し／貼付内容の平文保存は明示保存時のみと説明。保存場所、未保存、破損、model欠落、別Excel適用失敗。本文やcredentialの収集を増やさない |
| T33 | README・Unreleased | T29,T30,T31,T32 | 完了機能と実画像。編集：`README.md`、`CHANGELOG.md` | 公開版と候補を区別。独立定義保存非対応の旧記述を今回の範囲だけ改訂。UX効果・公開成功・製品版bumpを捏造しない |
| T34 | 実装設計・現在状態の同期 | T24,T27 | 実装された責務。編集：`DD/architecture.md`、`DD/detailed-design.md`、必要な現在状態の節だけ`DD/implementation-status.md` | Core／App、共通設定、採点draft、snapshot、checkpoint、結果の所有先が一致。履歴ADR・過去試験を改変しない |
| T35 | 要求から実装・試験への追跡を閉じる | T01,T25,T26,T27,T34 | T01の追加IDと実施結果。編集：`DD/traceability.md`、`DD/readme-claim-ledger.md`、`SystemTest-prompt.md`、`AT/Content/DocumentationContractTests.cs`の関連期待値 | 計画段階の未実施を実結果へ更新。新C／ST-UC等を追加した場合は連番・mappingを同時同期。未実施をVERIFIEDへ変えない |
| T36 | 文書・画像contractを同期 | T29,T30,T31,T32,T33,T34,T35 | §9.8。編集：`AT/Content/DocumentationContractTests.cs` | 設定ガイド／noticeをPublicDocumentPathsへ追加。08画像、UI対象版、保存境界、local link／anchorを検証。配布リストの追加assertionはT38と同時に閉じる |
| T37 | ZIP／MSIXと検証用文書リストを更新 | T12,T28,T30,T36 | §9.8。編集：`scripts/package-windows.ps1`、`scripts/package-windows-msix.ps1`、`scripts/test-windows-msix-unsigned.ps1`、`AT/Packaging/WindowsPublishPackageTests.cs`、`AT/Packaging/WindowsInstallerPackageTests.cs` | 新文書2件＋08画像を作成側／検証側へ追加。設定file・入力／final／partialは除外。MSIXは非公開の既存機構のみ。直後のT38と一組で配布整合性を検証 |
| T38 | 単一EXE・文書同梱の全経路同期 | T37 | §9.8。編集：`A/Properties/PublishProfiles/WindowsSingleFile.pubxml`、`scripts/publish-windows.ps1`、`AT/Packaging/WindowsSingleFileProfileTests.cs`、`AT/Packaging/WindowsSingleFilePublishTests.cs`、`AT/Packaging/WindowsSingleFilePackageTests.cs`、`AT/Content/DocumentationContractTests.cs`のpackage項目 | profile、publish関数、ZIP／MSIX、fixtureの期待集合が一致。集合一致と実物payloadを別検証。SDK／CLI／publish方式／公開policyは変更しない |
| T39 | 全体検証・利用者確認・記録 | T25,T26,T27,T36,T37,T38 | §10。出力：`artifacts/test/ui-settings/`の限定証跡、実施日に新規`work/<実施日>-ui-settings-execution-record.md` | required試験・native確認の実結果、未実施、残課題を区別。利用者walkthroughを行い、本人未確認を合格としない。公開判定へ流用しない |

### 9.7 実行順と共有ファイル

| Wave | 候補 | 条件 |
|---|---|---|
| 0 | G0 → T01 | 承認後に要求baselineと文書contractを一緒に閉じる |
| 1 | T02、T05、T07、T11 | データ／Design／Results／Stylesで編集先を分離 |
| 2 | T03、T04、T06、T09、T12 | 前依存完了後。T11→T12はApp.axamlを共有 |
| 3 | T08 → T10 → T22 | Input・設定VM・画面切替の契約を確定 |
| 4 | T13、T14、T15、T16、T18、T21 | 独立したView／test。T18／21はT22完了後 |
| 5 | T17 → T19／T20 → T23 | 設定組立、主画面、DataTemplate／構成の順に統合 |
| 6 | T24 → T25／T26／T27 | 結合後にaccessibility、layout、E2Eを分離 |
| 7 | T28 → T29／T30／T31。T32／T34／T35は依存成立後 | 画面から文書を作る。生成画像は単一担当 |
| 8 | T33 → T36 → T37 → T38 → T39 | 文書・配布・最終検証。T37〜38を中途で完了扱いにしない |

共有ファイルの所有順：

- Input VM：T04 → T08。Design VM：T05 → T09。App.axaml：T11 → T12。
- MainWindow VM／Navigator／既存MainWindowTests：T22。MainWindow XAML／構成処理：T23。
- UI layout契約：T01 → T26。追跡台帳／SystemTest：T01 → T35。
- DocumentationContractTests：**T01 → T35 → T36 → T38**。毎回、担当項目だけを更新する。
- ZIP変更は既存のsingle-file profile突合にも影響するため、T37→T38は連続した統合単位。T37だけの時点で全package testsの成功を主張しない。

### 9.8 忘れてはいけない同期先

| 契約 | 実在する同期先・検証 |
|---|---|
| 要求版・基準日・AC／C／ST-UC／TR対応 | `Requirements_claim_ledger_and_system_prompts_are_complete_and_current`。T01／35で対象文書、明示期待件数、`expectedRequirementMappings`、要求網羅範囲、real-data harness内の要求版metadataを同時更新 |
| 公開文書 | `PublicDocumentPaths`は現在9件。設定ガイドとnoticeを追加する場合は11件。link／anchor検査を維持 |
| UIの対象版 | `Unreleased_ui_and_screenshots_are_explicitly_distinguished_from_the_public_release`。現行表記はUNRELEASED／`0.8.4`候補／`0.8.1`。実装時の真の状態に合わせ、要求v4.6を製品版へ書かない |
| 画像 | `Screenshot_captions_and_manifest_disclose_synthetic_and_fake_state`、`DocumentationScreenshotTests.cs`、`images/README.md`。7枚から8枚へ |
| ZIP | `package-windows.ps1`の`$documentationRelativePaths`、`WindowsPublishPackageTests.cs`、文書contractのpackage項目 |
| 単一EXE | `WindowsSingleFile.pubxml`のContent、`publish-windows.ps1`の`Get-SingleFileDocumentationPaths`、Profile／Publish／Packageの3テストファイル |
| 非公開MSIX | `package-windows-msix.ps1`の`$PublicPayloadRelativePaths`、`test-windows-msix-unsigned.ps1`の`$RequiredPublicEntries`、`WindowsInstallerPackageTests.cs` |
| 横断の集合検証 | `Windows_single_file_profile_allowlist_matches_the_zip_packager`、`Single_file_documentation_allowlist_matches_the_app_profile`を維持。MSIX側も同じ公開文書集合を検証する最小のassertionを既存テストへ追加 |

新たな共通allowlist生成器・JSON schema・ビルド基盤は作らない。現在の17件に`docs/settings.md`、`docs/third-party-notices.md`、`images/08-settings.png`を加える案なら対象は20件。これは**文書・画像・LICENSE集合**の件数であり、runtimeを含むpackage総file数ではない。

リンク数の下限（25／100等）や開発文書数の下限は、今回増えたからという理由だけで変更しない。試験を通すために独立した期待値や禁止物検査を緩めない。

## 10. 受入基準と試験

### 10.1 本計画の受入ID

UX番号は計画内の識別子。要求正本のAC／TRへはT01で追加・対応付けし、番号を推測で上書きしない。

| ID | 合格条件 | 主なタスク |
|---|---|---|
| UX-01 | 4ステップと設定を行き来し、値・対象・ページを失わない。設定は第5工程にならない | T13〜25 |
| UX-02 | 通常サイズで外側scrollなし、警告全文・主操作・状態が実viewport内。clippingで代用しない | T11,T18〜23,T26 |
| UX-03 | 多数項目・最後のページ・長文・拡大でも対象と操作へ到達でき、keyboard／仮想化を維持 | T07〜09,T13〜21,T25,T26 |
| UX-04 | 各ステップで有効値と変更結果を確認でき、未実行score・未完成file・未保存状態を偽らない | T06〜10,T18〜24 |
| UX-05 | 明示保存→再起動相当の読込→明示適用で定義ID・配点・Prompt・canonical hashを維持。明示出力先も復元 | T02〜04,T06,T10,T27 |
| UX-06 | 別header／不足列／未知model／破損schema／IO拒否で無断適用・fallback・旧file破壊をしない | T03,T04,T06,T27 |
| UX-07 | 画面遷移・設定・Prompt適用はlogin／AIを開始せず、進行中runのsnapshot・停止操作・結果到着を維持 | T06,T10,T16,T22〜25,T27 |
| UX-08 | exact100、blank／zero、元本不変、別名出力、既存checkpoint admissionを維持。次回draftを過去結果へ混入しない | T19〜21,T24,T27,T39 |
| UX-09 | アイコン＋日本語、最小44 DIP、focus・contrast・Accessible Name・ID一意・Tab順を維持 | T11〜26 |
| UX-10 | docs・画像・LICENSEがZIP／EXE／非公開MSIXと検証リストで一致。利用者設定・実データ・secretを同梱しない | T12,T28〜39 |

### 10.2 最小の試験行列

| 対象 | 必須ケース・範囲 |
|---|---|
| 入力 | 未選択、picker取消、正常／不正、質問行1／2、header未再読込、空header、日本語・空白を含む長いpath |
| 設問／選択 | 0表示、1件、ページ境界前後、複数ページ、追加／複製／並替え／削除／全無効、最終ページ削除、リサイズ時のID保持 |
| 採点 | 既定60／0／0.1、合計不足／超過／exact100、均等化、Special>0の項目不足、range／weight、Custom placeholder不正 |
| 保存 | 初回fileなし、BOM、Unicode／改行／brace／decimal、定義なし、不正draft、再読込、未知版／未知項目、排他オープンによる拒否、親path不正、旧file不変・temp後始末、保存中の再編集後は未保存表示を維持 |
| 機密性 | 合成header由来の設問文と合成貼付Promptが明示保存時に含まれる。自動保存なし。回答行の自動コピー、認証・AI結果・実pathのログ混入なし |
| 明示適用 | 同入力、別header、sheet／列／行不一致、取消／失敗後無変更、保存ID保持、Imported Prompt一覧保持 |
| 出力先 | 未指定→入力Aのresult→入力Bのresult。明示先→再起動→入力Bでも同じ先。空欄へ戻す、利用不可、設定復元だけでdirectory作成なし |
| model／認証 | 未確認／必要／失敗／利用可、保存希望IDあり／なし／欠落、auto欠落、明示再確認、確認失敗で保存希望を消さない、no-auto-login/run |
| 実行／結果 | 実行中の前工程・設定編集、停止、設定表示中に完了、取消／一部失敗、final失敗、partial cleanup失敗、override不正／修正／別名出力、前回結果保持 |
| 件数 | UIは既存の100行と530行のsynthetic／fakeを再利用。20,000件のページ数・境界・最終範囲はUIを全件生成しない計算テストで確認。既存の回答行上限・attempt上限テストは別々に維持 |
| 表示 | 1024×720、1180×800、760×600 standalone、200%相当headless、native Windowsの実DPI。実ClientSize、論理／物理寸法、表示件数、scroll例外を記録 |

530行は本改修のUI fixture規模であり、新たな製品上限ではない。20,000件の境界計算成功を20,000件実画面の性能実測へ読み替えない。既存の531行workbook性能・synthetic E2Eを残し、実学生sampleを表示fixtureへ代用しない。

### 10.3 実施順と証跡

1. 各タスクのVM／store／対応headless tests。失敗したテストの意味を保ち、必要な期待値だけを更新する。
2. T24の往復統合後、accessibility／layout／設定E2Eを実施。
3. 画像を実Viewから2回生成し、同環境で一致と実寸法を確認。文書link／anchor／未実測claimを検査。
4. T37〜38を連続して同期し、配布リストの集合一致と既存package検証を実施。MSIXは非公開mechanismの既存範囲だけ。
5. 既存[CI](../../../../.github/workflows/ci.yml)と[開発者ガイド](../../../../dev/README.md)に従い、固定SDKでlocked restore → Release build → required Core／App tests → package検証。独自ビルド手順を追加しない。
6. native Windowsで主要画面、実DPI、keyboard／Narrator、実保存を確認。**保存は隔離した利用者環境**で行い、自動testの一時path注入と区別する。
7. 次項の本人walkthrough。未確認項目は未確認として残し、自動test成功で代替しない。

証跡は`artifacts/test/ui-settings/`へtest名・成否・寸法等の限定情報だけを残す。現行の`/artifacts/`はgitignore対象だが、実施時にも確認する。実ユーザーの本文・credential・環境変数値一覧・巨大生ログを記録／commitしない。

実AI smoke、実学生data、本人login、Office再計算のopt-inは勝手に有効化しない。今回のnative UI確認はfresh OS／exact artifactの公開証跡ではない。

### 10.4 利用者walkthrough

説明を先に読ませず、以下を本人に試してもらう。

1. 合成Excelを選び、対象行・設問・主回答列を確認。2問を30／10点へ変更しBase60と合わせて100を確認。
2. 設定で評価観点と出力先を変更し保存、元ステップへ復帰。変更内容と保存状態を確認。
3. 再起動相当の読込で出力先を確認し、保存定義は自動適用されないことを確認して明示適用。
4. fake結果の完成file表示を見分け、1件overrideした状態が「修正版未保存」であることを確認。

完遂の可否、迷った箇所、誤解した状態、修正要望を記録する。時間・操作数を測る場合も条件を併記し、単一利用者の結果を統計的改善率へ一般化しない。

## 11. 文書更新と変更しない領域

実装時の文書更新は必須。設定保存を非対応とする旧記述、移動した入力欄の場所、画像、privacyをUIと同時に更新する。

| 対象 | 更新する内容 | タスク |
|---|---|---|
| 要求正本・承認記録 | 通常／例外scroll、設定配置と保存、明示出力先、4ステップ、scope・AC／TR・改訂版 | T01 |
| 利用開始・設定・index | 操作場所、戻る、保存／適用、設定の場所と明示出力先の復元 | T30 |
| 機能・Custom・Prompt | 評価編集の移設、値の意味、起動順と明示適用 | T31 |
| Privacy・troubleshooting | 見出し／貼付内容の保存、明示保存のみ、破損・IO失敗・model欠落 | T32 |
| README・CHANGELOG | 完了機能、実画像、Unreleasedと公開版の区別 | T33 |
| 開発設計・追跡 | 値の所有先、同期境界、実施した試験と対応。未実施は未実施 | T01,T34〜36 |
| 画像・notice・配布 | 実View画像、出典／ライセンス、3経路と検証側の明示リスト | T12,T28〜29,T37〜38 |

過去ADR・過去の試験記録・sample profileの検証時点は書き換えない。今回のUI／設定判断は`ui-layout-contract.md`と実装設計へ集約し、同内容の新ADRを追加すること自体はタスクにしない。

変更しないもの：

- Coreの採点式、6 placeholder、blank／zero、100点exact制約。
- workbook形式、app-owned sheet名、atomic finalization、checkpoint schema／admission。
- SDK／CLI／Avalonia／.NETの固定版、bundled resolver、login子processの所有境界。
- `.NET`標準publish方式、公開workflow／matrix、署名、MSIX installer機能、macOS source foundation。
- 実在の入力sample・final・partial、CLI credential store。開発・検証作業では実利用者の既存設定も変更しない。製品利用時の利用者による明示保存は§7の契約に従う。

## 12. 過剰実装を防ぐ制約と完了ゲート

### 12.1 追加しないもの

- 新しいNuGet、`Directory.Packages.props`やlockの変更、別UI framework、WebView、新production project。
- 汎用設定provider／Strategy／Factory、filesystem抽象、plugin、event bus、router、form builder、pagination framework。
- 複数profile、cloud同期、DB、設定暗号化container、migration／backup／監視／merge／履歴engine。
- テーマ・言語・font・density・ログレベルの利用者向け切替、未使用flag。
- Undo／Redo engine、AI設定提案、実AI preview、新評価アルゴリズム、analytics dashboard。
- 全icon集、SVG renderer依存、独自title bar、必須drag-and-drop。
- 新しい製品CLI引数・必須環境変数・API key、設定からの自動login／AI実行。
- リリース公開、製品版bump、branch作成、commit／push／tag、保護設定変更。

### 12.2 ゲート

| Gate | 判定条件 |
|---|---|
| G0：着手承認 | 本書とD01〜D20を確認し、作成・実装の明示指示を受領。**現在は未通過** |
| G1：要求・契約 | T01で承認差分、保存範囲、出力先、通常／例外到達、要求と文書contractが一致 |
| G2：機能・表示 | T24〜27で設定・同期・run不変・layout・安全境界を実試験。native確認をheadlessで代替しない |
| G3：文書・配布 | T28〜38で実画像・ガイド・ライセンス・作成／検証側リスト・実物payloadを同期 |
| G4：利用者確認・終了 | T39で本人walkthrough、実施／未実施、残課題を記録。未確認や失敗をPASSへ変更しない |
| 公開：別承認 | UIやdocs同梱でEXE bytesが変わるため、既存exact artifact／clean-host公開条件を別途満たす。旧証跡を流用しない |

## 13. この更新作業の検証範囲

本更新で行うのは、計画書の見出し・ID・依存DAG、同一ファイルの編集順、既存pathと新規提案の区別、ローカルリンク、レビュー対応の確認である。旧計画書のbytesと製品ファイルの非変更も確認する。

**製品build／test、アプリ起動、画面収まりの実測、設定保存、実AI、login、配布実物の検証は実施していない。** 本書の整合性確認は、提案UIの動作合格でも、D01〜D20の承認でもない。