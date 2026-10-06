# Promptファイルから起動する

StudyReport Evaluatorは、入力workbookと複数の評価Promptを起動時に事前入力できます。この機能はGUIの準備だけを行い、loginやAI処理を自動開始しません。

これは任意の高度な起動方法です。**通常のGUI起動に、利用者のterminal操作やcommand入力は不要です。**

## 対象版と確認範囲

- **製品版`0.9.0`（未公開）**: 前版（`dahatake/StudyReport-Evaluator`）の公開`v0.8.6`の後継で、EXE名（`StudyReportEvaluator-win-x64.exe`、ZIP内の`StudyReportEvaluator.App.exe`）と起動引数を引き継ぎます。このリポジトリのGitHub Releasesから配布しますが、まだ公開していません。`0.9.0`のclean-host試験CH-01〜06と本人loginはまだ実施しておらず、公開前に実施します。
- **公開`v0.8.6`（単一EXE）**: 配布名は`StudyReportEvaluator-win-x64.exe`です。通常はGitHub Releaseの`v0.8.6`から取得し、必要に応じて`StudyReportEvaluator-win-x64.exe.sha256`でSHA-256を照合します。clean-host試験CH-01〜06と本人loginは公開前に実施していません。
- **公開`v0.8.6`（ZIP代替経路）**: `StudyReportEvaluator-win-x64.zip`を展開した`StudyReportEvaluator.App.exe`も使えます。この起動名と引数は従来どおり維持します。旧`v0.8.1`は前版の前回公開版です。

以下の画面操作は、特記しない限り製品版`0.9.0`と前版の公開`v0.8.6`に共通の新UIです。Imported Promptsの本文・適用先は**設定 → 読込Prompt**、モデル・並列度・指定出力先の編集は**設定 → 共通**にあります。旧`v0.8.1`の操作は後述の「旧 v0.8.1（ZIP）の旧画面」に分けています。

> **既存P06の確認範囲（製品`0.8.4`の検証履歴）:** 開発host上の実GUIで日本語pathの入力workbookと複数Promptの指定順維持を確認済みで、P06全7件PASS・review未解決指摘0です。この履歴を版更新後の最終成果物の検証結果へ流用しません。追加ソフト未導入のfresh OS（clean-host）での試験と利用者本人のloginは`NOT_RUN`であり、その成功や`v0.8.6`公開後もclean-host成功を示しません。

## 2種類のPrompt

1. **起動依頼Prompt** — GitHub Copilot等のcommand実行可能agentへ、ローカルアプリの起動を依頼する文章
2. **評価Prompt file** — StudyReport Evaluatorが通常Custom evaluatorまたは固有評価へcopyするUTF-8 `.txt`

両者を混同しないでください。

## 起動option

公開`v0.8.6`の単一EXE:

```text
StudyReportEvaluator-win-x64.exe --input "<xlsx-path>" --prompt "<txt-path>" [--prompt "<txt-path>" ...]
```

公開`v0.8.6`のZIPを展開したEXE（代替経路）:

```text
StudyReportEvaluator.App.exe --input "<xlsx-path>" --prompt "<txt-path>" [--prompt "<txt-path>" ...]
```

| Option | 回数 | 動作 |
|---|---:|---|
| `--input` | 0または1 | Input画面へpathを設定し、safe read-only読込を開始 |
| `--prompt` | 0回以上 | 指定順でImported Promptsへ読込。候補版では設定 → 読込Promptで確認 |

- option名は大文字・小文字を区別しません。
- relative pathは起動時のworking directory（cwd）を基準にabsolute化します。基準は起動元のdirectoryのままで、EXE配置先や単一EXEの抽出cacheへ変更しません。
- absolute化後の同じPrompt pathの重複は最初の1件だけを使い、残りの指定順を維持します。Windowsではpathの大文字・小文字を区別しません。
- unknown option、重複`--input`、値なし、不正path、`--prompt`に指定した`.txt`以外のfileは起動errorです。
- `--run`や`--resume`はありません。

## 評価Prompt file

- extension: `.txt`
- encoding: strict UTF-8、BOMあり／なしを受理
- length: 1〜32,767 UTF-16 code units
- empty、invalid UTF-8、読込失敗は拒否

候補版では、採点設計の**読込Prompt (件数)**から**設定 → 読込Prompt**を1操作で開けます。設問がある場合は同じ設問ID（`questionId`）を保ちます。入力前でも設定から原文を閲覧でき、適用には設問と適用対象が必要です。

一覧は起動引数順のファイル名（basename）を表示し、選択したPromptの本文全体を読取専用で確認できます。**適用先の設問**と**適用先の種類**、通常Custom evaluatorまたは固有評価の項目を選び、適用先の概要を確認して**Promptを適用**した時だけtemplateへcopyします。Knowledgeには適用できません。filenameによる自動割当や、選択だけでの適用は行いません。

適用しても一覧の順序・ファイル名・原文を保持し、同じPromptを再利用できます。同一起動中の画面往復では、残っている対象の設問・評価方法・固有項目と適用先の種類も保持します。適用後の本文編集と技術検証は**設定 → 通常評価 → Prompt**または**設定 → 固有評価 → Prompt**で行います。previewは仮値による表示で、実学生の回答やAIによる評価結果ではありません。

起動引数やPrompt適用からloginやAI処理を自動開始しません。Promptの適用は設定fileへの保存やステップ移動も行いません。AI評価の開始にはExecution画面の**定量化を開始**を利用者自身が選ぶ必要があります。候補版の**GitHubにログイン**も別の明示操作です。

## PowerShellから起動する例

既にPowerShell 7がある場合の任意の例です。通常のGUI起動のためにPowerShellを導入したり、terminalへcommandを入力したりする必要はありません。

公開`v0.8.6`の単一EXE:

```powershell
& "C:\配布 アプリ\StudyReportEvaluator-win-x64.exe" --input "C:\授業 データ\回答.xlsx" --prompt "C:\授業 データ\評価 Prompt\内容 評価.txt" --prompt "C:\授業 データ\評価 Prompt\学生 Prompt.txt"
```

公開`v0.8.6`のZIPを展開したEXE:

```powershell
& "C:\配布 アプリ\StudyReportEvaluator-win-x64\StudyReportEvaluator.App.exe" --input "C:\授業 データ\回答.xlsx" --prompt "C:\授業 データ\評価 Prompt\内容 評価.txt" --prompt "C:\授業 データ\評価 Prompt\学生 Prompt.txt"
```

日本語や空白を含むpathは1つのargumentとして引用します。relative pathを使う場合も起動元のcwdを維持し、EXEのdirectoryや抽出cacheへ移動しません。起動しただけではCopilot session、login、AI送信を開始しません。

## GitHub Copilotへ貼る起動依頼例

`<...>`を実在pathへ置き換え、引用符を残します。アプリには公開`v0.8.6`の単一EXE、または同じ`v0.8.6`のZIPを展開したEXEのどちらか1つを指定します。

```text
ローカルのStudyReport Evaluatorを、次の入力と評価Promptを事前入力して起動してください。

アプリ:
"<StudyReportEvaluator-win-x64.exe または ZIP内 StudyReportEvaluator.App.exe のabsolute path>"

入力workbook:
"<標準 .xlsx のabsolute path>"

評価Prompt file（順序を維持）:
1. "<UTF-8 .txt のabsolute path>"
2. "<UTF-8 .txt のabsolute path>"

条件:
- fileの存在とextensionだけを確認し、workbook本文を開いたりchatへ表示したりしないでください。
- 入力workbookと既存Prompt fileを変更しないでください。
- argumentは--inputを1回、--promptを上記順に渡し、各pathを引用して1つのargumentとして扱ってください。
- 起動元のworking directory（cwd）を維持し、EXEのdirectoryや抽出cacheへ移動しないでください。
- --inputと--prompt以外のoptionを追加しないでください。
- GUIを起動したら停止し、login、画面clickやAI評価を自動実行しないでください。
- fileが見つからない場合は別pathを推測せず、不足しているpathだけを知らせてください。
```

## 通常Custom evaluator用Prompt例

`content-quality.txt`としてUTF-8保存できます。

```text
次の回答を、指定された評価項目ごとに分析してください。
同じ行の回答と補助情報だけを根拠にし、未記載の事実を補わないでください。

### 設問
{設問}

### 回答
{回答}

### 補助情報
{補助情報}

### 評価項目
{評価項目}
```

## 固有評価用Prompt例

`student-prompt-quality.txt`としてUTF-8保存できます。

```text
学生が作成した次のPromptについて、目的、入力、制約、期待出力、検証方法が第三者にも明確かを評価してください。
表現の華やかさだけで判断せず、同じ行の情報だけを使ってください。

### 関連する設問
{設問}

### 学生Prompt
{回答}

### 考慮事項
{補助情報}
```

通常Customでは`{回答}`と`{評価項目}`が必須です。固有評価では`{回答}`が必須で、`{評価項目}`は任意です。許可される6個は`{設問}`、`{回答}`、`{補助情報}`、`{評価項目}`、`{最小点}`、`{最大点}`のままです。固有評価の`{評価項目}`は空文字、`{最小点}`／`{最大点}`は0／1になります。literal braceの`{{`／`}}`とsingle-pass展開を含む詳細は[Custom evaluator](custom-evaluator-guide.md)を参照してください。

## 保存設定と起動引数の関係（候補版）

- **設定を保存**で、共通設定と任意の採点定義1件を利用者別の`setting.txt`へ明示保存できます。複数profileの管理やfinal workbookからの定義importではありません。
- 起動時の読込、または**設定を再読込**で共通設定を復元します。保存定義は保持するだけで、`--input`で指定したworkbookへ自動適用しません。Excel読込後、**設定 → 共通 → 保存定義 → 現在の入力に適用**で検証成功時だけ反映します。
- 保存定義の適用は、`--prompt`の取込一覧・順序・ファイル名・原文を変更せず、未適用Promptを自動適用しません。適用済みtemplateは定義の保存対象ですが、未適用Promptのfile一覧は保存対象外です。次の起動でもその一覧が必要なら、既存の`--prompt`で指定します。
- `setting.txt`はUTF-8 JSONの設定fileで、評価Prompt用`.txt`とは別物です。`--prompt`へ渡して設定を読み込むものではなく、設定用の新しいCLI optionもありません。保存・再読込・明示適用からloginやAI評価を開始しません。

保存時に含まれる設問文・適用済みPrompt等の平文情報、明示保存とアプリ内反映の違いは[設定の保存と適用](settings.md)を確認してください。

## 起動後の確認

以下は候補版の4ステップで確認する内容です。

### Input

- 目的の`.xlsx`がread-onlyで読み込まれた
- sheet、質問文の行1/2、回答行が正しい
- 主画面で対象設問・有効状態・主回答列・設問文を確認した。主列変更で見出しの交差セル値が反映されるため、必要なら設問文を手動修正した
- 通常の補助列は**設定 → 入力詳細**、固有評価の主対象列・補助列は**設定 → 固有評価 → 項目設定**で確認した

### Design

- 主画面でBase、Special、enabled Question pointsの合計が**正確に100**で、不足／超過がない。ページ外の有効設問も合計に含む
- 選択設問の絶対配点と評価方法・評価項目の概要を確認した。Knowledge／Custom、criterionのrange・weightは**設定 → 通常評価**で編集する。Knowledgeのinstructionは読取専用
- **設定 → 読込Prompt**でImported Promptを正しい対象へ明示適用し、Custom／固有評価の必須placeholderとコピー後の本文を確認した
- Similarity penalty weightを確認した。**係数0はSimilarity処理を無効にしない**。一方、**Special pointsが0なら固有評価AIを実行しない**
- 技術エラーは一覧・**全文**で確認し、**問題箇所へ**で入力欄や設定を修正した

### Execution

- **Copilot 状態を確認**でlogin、利用可能model、次回runのreasoning effortを確認した。必要な場合のみ**GitHubにログイン**し、完了後も自分で状態を再確認した
- **変更 → 共通**で通常モデル・concurrency 1〜16・指定出力先を編集し、実行画面の読取専用表示で次回の実効値を確認した
- 明示出力先は入力Excelを変えても保持する。空欄（未指定）のときだけ入力隣接`result`となり、入力未選択なら「入力後に決定」。再開の場合は別途partial pathを確認した
- 今回／前回runの固定条件と次回の設定を区別した。実行中の編集は進行中runへ反映しない
- 利用者自身が**定量化を開始**を選んだ

### Results

- 予約名を作成完了とみなさず、結果のfinal／partialと保存状態・cleanup warningを確認した
- 行一覧のページ切替や元の行番号への移動から**詳細・override**を開き、選んだ1 criterionのraw・range・適用値を確認した
- overrideが不正なら**エラー n 件・次へ**で該当行・設問・評価方法・criterionへ移動して修正した。必要な修正版だけを別名出力し、元の完成版と未保存のoverrideを区別した
- 完了済み空回答の0と、取消・主値未確認・未処理や技術的失敗のblank（画面の**—**）を区別した。空回答の獲得点0はFinal scoreが必ず0という意味ではない
- 次回の入力・設定を編集しても、前回の結果は自動再評価されないことを確認した
- Similarityだけで不正行為と判定しない

## checkpoint再開

command lineにresume optionはありません。通常どおり`--input`と必要な`--prompt`で起動し、候補版のExecution画面で**checkpoint から再開**をonにして`.partial.xlsx`を指定します。入力／定義が変わると再開指定は解除されるため、実効設定とcheckpointを確認し直してください。単に画面を往復しただけでは再開指定を初期化しません。

checkpointのinput、definition、modelが一致しない場合、またはruntimeの互換条件を満たさない場合は再開を拒否します。アプリidentityを`アプリ名/バージョン`として解釈できる場合は、**アプリ名とmajor版の一致**を求め、minor／patchの差だけでは拒否しません。解釈できないidentityは文字列の完全一致が必要です。**SDK informational version、CLI version、CLI SHA-256は完全一致**を求めます。partialを編集、rename、copyして一致を回避しないでください。

## 旧 v0.8.1（ZIP）の旧画面

この節だけは旧`v0.8.1`の旧配置です。上記の候補版の設定画面と混同しないでください。

- Imported Promptsは**Design画面内**で確認し、対象を選んで**Promptを適用**します。選択だけでの適用やfilenameによる自動割当はしません。
- モデル・並列度・出力directoryは**Execution画面の編集欄**で指定します。
- checkpoint再開は旧Execution画面の**既存checkpointから再開**で`.partial.xlsx`を指定します。新しいresume引数はありません。
- このガイドで説明する設定画面と`setting.txt`への共通設定・採点定義1件の保存は、旧`v0.8.1`の機能ではありません。起動引数の順序、Promptの明示適用、no-auto-runの契約は従来どおりです。

## privacy

起動依頼Promptへ学生の回答、氏名、email、credentialを貼らないでください。agentへworkbook本文を開かないよう明示してください。final/partialは入力全体と評価情報を含むため、入力と同等以上に機密です。

詳しくは[データとprivacy](privacy-and-data-handling.md)を参照してください。

