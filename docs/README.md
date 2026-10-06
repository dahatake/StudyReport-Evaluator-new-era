# StudyReport Evaluator 利用者ガイド

StudyReport Evaluatorは、標準`.xlsx`の回答をGitHub Copilotで定量化し、入力を変更せず別の`.xlsx`へ結果を作成するWindowsデスクトップアプリです。

本アプリの製品版は`0.9.0`です。`0.9.0`は前版（`dahatake/StudyReport-Evaluator`）の公開`v0.8.6`の後継で、このリポジトリのGitHub Releasesから配布しますが、まだ公開していません。`0.9.0`のclean-host試験CH-01〜06と本人loginは未実施で、公開前に実施します。前版と設定の保存先（`%LOCALAPPDATA%\StudyReportEvaluator\setting.txt`）が同じため、前版で保存した設定をそのまま読み込み、**前版と同時に使うと設定ファイルを共有します**（[前版からの移行](../README.md#前版v086からの移行と設定ファイルの共有)）。

現在入手できる公開版は、前版の`v0.8.6`です。[GitHub Releases](https://github.com/dahatake/StudyReport-Evaluator/releases/tag/v0.8.6)で取得できる配布物は、`v0.8.6`の`StudyReportEvaluator-win-x64.exe`、`StudyReportEvaluator-win-x64.exe.sha256`、`StudyReportEvaluator-win-x64.zip`、`StudyReportEvaluator-win-x64.zip.sha256`の4件です。単一EXEが主導線で、ZIPは代替経路として維持します。旧`v0.8.1`は前版の前回公開版です。

`v0.8.6`は公開済みですが、repository ownerの明示判断によりfresh Windows 11 x64のclean-host試験CH-01〜06と本人loginを実施しないまま公開しました。追加native UI確認FAIL、Narrator／本人walkthrough／隔離利用者設定保存の未実施、T39 BLOCKED、CH-01〜06 `NOT_RUN`は維持します。

> [!WARNING]
> 生成AIが行う評価には正確性が欠ける可能性があるため、必ず自分で責任をもって評点を行ってください。このツールや生成AIは評価結果に対しては一切の責任を負えません

**対象版の注意:** 4ステップの簡素化、全設問へのコンパクト一覧・ページ切替、設定5カテゴリ、明示保存・適用の説明は、製品版`0.9.0`と前版の公開`v0.8.6`に共通です。パネルの配置変更、コマンド検索、ペルソナ、結果と配点構成の図は`0.9.0`で追加したもので、`v0.8.6`にはありません。旧`v0.8.1`にはこれらの新UI・設定保存を追加していません。要求文書は**v4.6**で、製品版とは別です。

操作説明の画像は**8枚（01〜08）**で、synthetic／fake状態です。実認証・実結果出力・設定保存・native表示の成功証拠ではありません。製品`0.8.4`の時点で生成したPNGを維持しており、版表記の更新は新たなUI検証を意味しません。既存の検証履歴と未完了の確認範囲は[はじめに](getting-started.md#v086の検証範囲)を参照してください。

## 読者別ガイド

利用開始と詳細設定の手順を分けています。製品版`0.9.0`（未公開）、前版の公開`v0.8.6`、旧`v0.8.1`を区別し、配布・起動条件は製品READMEの「公開状況・SHA-256確認・起動」も参照してください。文書・版の変更を配布物へ取り込む場合は、最終EXE／ZIPとsidecarを再生成・再検証し、変更後のexact EXEでclean-host公開条件を満たす必要があります。

| 読者 | ガイド | 内容 |
|---|---|---|
| 配布・起動条件を確認する人 | [製品README](../README.md) | 公開ZIP、今後のEXE主導線、任意のSHA-256確認、Windowsの警告 |
| 初めて利用する教員・採点者 | [はじめに](getting-started.md) | 準備、起動、メイン4step、ページ切替、実行・再開、結果一覧／詳細・override |
| 詳細設定を編集・再利用する人 | [設定ガイド](settings.md) | 5カテゴリ、元画面への復帰、`setting.txt`への明示保存、保存定義の明示適用、平文保存の注意 |
| 評価方法を設計する人 | [機能と点数](features.md) | 配点、4つのAI処理、status、Excel出力 |
| 結果のExcelを読む教員・採点者 | [実行結果Excelの見方](result-excel-description.md) | 4つのsheetと各列の意味、最終点、Override欄、空欄と0、状態コード、計算例 |
| 独自の評価観点を作る人 | [Custom evaluator](custom-evaluator-guide.md) | 通常Custom評価と固有評価のPrompt |
| Promptファイルで準備する人 | [Promptファイルから起動](prompt-launch.md) | `--input`、複数`--prompt`、明示適用 |
| 情報管理・運用担当 | [データとprivacy](privacy-and-data-handling.md) | AIへ送る情報、log、final/partialの機密性 |
| 問題を解決したい人 | [トラブルシューティング](troubleshooting.md) | 入力、設計、Copilot、checkpoint、出力 |
| 保守・拡張するソフトウェアエンジニア | [技術ガイド](technical-guid.md) | Architecture図、Core／App境界、Copilot SDK／CLI、Excel、カスタマイズとtest |
| 画面を確認したい人 | [画面一覧](../images/README.md) | synthetic／fake画面8枚の生成時点・対象版と各画像の限界 |

## 対応範囲

- **OS:** Windows 11 x64
- **配布:** .NET 10 self-contained。公開EXE／ZIPともunsignedで、各形式にSHA-256 sidecar
- **入力:** 標準Office Open XML `.xlsx` 1file
- **出力:** 入力を保持した別の標準`.xlsx`
- **設定（v0.8.6以降）:** 共通設定＋任意の採点定義1件を、利用者別`setting.txt`へ明示保存。起動時は共通値を読み込み、採点定義はExcel読込後の明示操作で適用。保存先と形式は前版の公開`v0.8.6`と`0.9.0`で同じです
- **AI runtime:** 配布物へ同梱したGitHub Copilot CLI。PATH上の別CLIへfallbackしません
- **GUI起動:** .NET Runtime／SDK、PowerShell、Node.js／npm、Git、GitHub CLI（`gh`）、別Copilot CLI、Microsoft Excel／Office／LibreOffice、IDEの追加導入を要求しない設計

単一EXEの目標は、標準userがofflineで**取得済みEXEをダブルクリック → 入力画面**へ進む1起動gestureです。download、任意の手動hash比較、SmartScreen等の警告への操作、本人loginは含めません。手動hash比較は任意・推奨で、sidecarは起動の前提ではありません。

SmartScreen、Smart App Control（SAC）、企業policyによる警告・実行拒否があり得ます。無警告・無条件の起動を保証せず、拒否時に保護機能を回避しません。

配布形式は単一EXEとZIPだけです。開発用 MSIX と macOS の基盤は 2026-10-06 に廃止した。

**「GitHubにログイン」は前版の公開`v0.8.6`から提供している機能です（`0.9.0`も同じ）。**明示操作で検証済み同梱native CLIのconsole／ブラウザーへ本人認証を委譲します。旧`v0.8.1`にはこのbuttonがなく、従来の同梱CLIで本人loginを行います。どちらも完了後は既存の「Copilot 状態を確認」で再確認します。

AI処理には、本人loginに加え、利用可能なGitHub Copilot account／model、network接続、組織policy上の許可が別途必要です。GUI起動や状態確認からlogin・AI評価を自動開始しません。アプリの認証処理はPAT、password、client secret、token、device codeを入力・収集・保存・log出力しません。未認証でもアプリ起動、workbook読込、mapping、設計編集は利用できますが、新しいAI処理は開始できません。

設定の読込・保存・適用から認証確認・login・AI評価は自動開始しません。`setting.txt`はUTF-8 JSONの平文で、見出し由来の設問textや利用者が貼り付けた内容も明示保存時に含まれ得ます。秘密情報を貼り付けず、[保存範囲と注意](settings.md#保存しない情報と平文保存の注意)を確認してください。環境変数・API key・`.env`の設定は不要です。

## 対応しないもの

- `.xls`、`.xlsb`、CSV、PDF、`.xlsm`等のmacro-enabled file
- password、暗号化、rights-protected、unsafe relationshipを含むworkbook
- macOS、Linux、Windows Arm64
- installer、code signing、notarization
- 保存済みfinal workbookのアプリへの再import
- 複数のdefinition profileの管理・切替（採点定義1件の明示保存・適用とは別）
- AI品質、公平性、法的・組織policy適合性、不正行為の判定

類似度は不正行為の証明ではなく、低い類似度も回答品質を保証しません。最終的な評点と利用判断は利用者が行います。

## ライセンス

本ソフトウェアは[MIT License](../LICENSE)で提供されます。
