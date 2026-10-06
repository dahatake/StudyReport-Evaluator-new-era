# 手順で確かめる受入基準

コマンドだけでは判定できない受入基準の手順書です。要求と受入基準の正本は [requirements-definition.md](./requirements-definition.md) です。自動テストで判定できる部分は自動テストで判定し、この文書は native の Windows・人の操作・外部環境が必要な部分だけを扱います。結果は、リリースごとに `artifacts/manual-tests/<版>/` に記録し、公開物には含めません（学生の情報・username・token を記録しない）。

| 手順 | 要求（受入基準） | 自動テストで判定する部分 | この手順で判定する部分 |
|---|---|---|---|
| MT-01 | FR-055（AC-056）、FR-058（AC-059） | 単一 EXE の構成・内容・sidecar（`WindowsSingleFile*Tests`）、公開 matrix と workflow の契約（`ReleaseMatrix*Tests`、`ReleaseWorkflowContractTests`） | fresh な Windows 11 x64 の標準ユーザーでの clean-host 試験 CH-01〜CH-06 |
| MT-02 | NFR-A11Y-001（AC-078、AC-095）、NFR-UX-010（AC-086） | headless でのキーボード到達・44 DIP・Automation ID・ハイコントラスト時の資源の切替（`PrimaryJourneyAccessibilityTests` ほか） | native の Windows での Narrator の読み上げ、実 DPI、Windows のハイコントラストの実表示、WCAG 2.2 AA の確認表 |
| MT-03 | NFR-PERF-003（AC-073） | — | 531 行の読込・書込・完成時の検証の時間、EXE の size・展開容量・初回と再起動の時間の実測記録 |
| MT-04 | FR-026（AC-027） | 数式・Config 参照・cached value（`ConfigAndRunSheetWriterTests`） | 表計算ソフトでの再計算（任意。草案 ADV-02） |
| MT-05 | §2.4 G-001 | — | 前版 v0.8.6 と同じ見本での操作数と処理時間の比較 |

## MT-01 clean-host 試験（CH-01〜CH-06）

- 事前条件: 公開候補の EXE（`StudyReportEvaluator-win-x64.exe`）と sidecar。fresh な Windows 11 x64 の実機または VM。標準ユーザー。.NET SDK／Runtime、PowerShell 6 以上、Node.js、Git、GitHub CLI、別の Copilot CLI、Office、IDE を導入していない。
- 操作と期待結果:
  1. CH-01: OS の edition・build・x64、標準ユーザー、上記の未導入を確認して記録する。
  2. CH-02: network を切り、EXE 1 個だけを開く。入力画面が表示され、合成の workbook の読込と採点設計ができる。sidecar・隣接ファイル・既存の CLI cache・認証情報を使わない。OS の保護機能による拒否を起動の成功として扱わない。
  3. CH-03: 同梱 CLI の Start・Ping・認証状態の確認を、外部の PowerShell・Node・Git・gh・CLI なしで行う。未認証の正常応答と runtime の失敗を区別して記録する。
  4. CH-04: 移動、再起動、同時起動、任意の作業フォルダ、起動引数、日本語と空白を含むパス、読取専用の配置先、展開 cache の欠落からの復元を試し、入力・完成・partial・設定が変更・削除されないことを確かめる。
  5. CH-05: 標準のブラウザーで取得した（MOTW 付きの）EXE を開き、SmartScreen・Smart App Control・企業 policy の状態、警告や拒否、実際の操作数を記録する。警告を隠して「1 操作」と記録しない。
  6. CH-06: 本人の GitHub アカウントで login し、完了後と再起動後に「Copilot 状態を確認」で状態を確かめる。取消とアプリ終了で、アプリが開始した login のプロセスだけが終了し、ほかのプロセス・データ・credential が残ることを確かめる。
- 証跡: 要求 FR-058 の metadata だけの JSON（候補の run ID・commit、EXE の basename・bytes・SHA-256・製品版、OS 情報、試験 ID ごとの結果と操作数）。
- 判定: CH-01〜CH-06 がすべて PASS の場合だけ公開する（FR-058）。

## MT-02 native のアクセシビリティ

- 事前条件: Windows 11、表示倍率 100%・150%・200%、Narrator、ハイコントラスト（「砂漠」「夜空」など）、テキストのサイズ 150%、「アニメーション効果」オフ。
- 操作と期待結果:
  1. マウスを使わずに、読込 → 採点設計 → 開始 → 結果の確認 → 出力を完了できる（AC-078）。
  2. Narrator で、各ボタン・入力欄・パネルのメニュー・図の要素が日本語の名前で読み上げられ、状態の変化（保存結果、進捗、完了）が通知される。
  3. ハイコントラストを有効にすると、全 step の文字・枠・focus が OS のハイコントラストの色で表示され、状態が色以外（文字・記号）でも区別できる（AC-086）。
  4. 1024×720、760×600、200% で、横スクロールが出ず、すべてのパネルに到達できる（AC-084 の native 確認）。
- 証跡: WCAG 2.2 レベル AA の各達成基準について、WCAG2ICT の解釈でデスクトップに当てはまるかと、当てはまるものの結果（合格・不合格・未実施）を表にする（AC-095）。適合の表明は書かない。
- 判定: 不合格の項目は、リリースノートに既知の制限として書くか、修正する。

## MT-03 性能の実測記録

- 事前条件: 対応 OS（Windows 11 x64）の実機。AI を使わない合成の 531 行 workbook（`tests` の合成 fixture を利用）。
- 操作: 読込、出力の書込、完成時の検証の時間を各 3 回測る。EXE の size、展開後の容量、初回起動と再起動の時間を測る。
- 証跡: 値、OS build、CPU とメモリ、測定日。数値の SLA は設けない（NFR-PERF-003）。

## MT-04 表計算ソフトでの再計算（任意）

- 操作: 合成データで作った出力 workbook を表計算ソフトで開き、Config のベース点を 60 から 50 に変える。
- 期待結果: 合計不一致の表示が出て `Final_Score` が空欄になり、設問配点を 10 増やすと表示が消えて再計算される（AC-027）。

## MT-05 G-001 の比較

- 事前条件: 前版 v0.8.6 と本リポジトリの版。同じ見本・同じ採点定義。
- 操作: 入力の選択から結果の確認までを同じ手順で行い、操作数（クリック・キー入力の操作単位）と、AI 待ちを除く処理時間を記録する。
- 判定: 操作数・処理時間とも前版以下（§2.4）。
