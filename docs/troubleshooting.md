# トラブルシューティング

回答やPrompt本文、`setting.txt`の実内容をerror report、issue、chatへ貼らず、表示されたcode、field、件数、file形式だけで切り分けてください。設定には見出し由来の質問文や貼付内容、機密なpathが含まれ得ます。

> **対象版・確認範囲:** 本頁の単一EXE、login button、`setting.txt`の説明は、製品版`0.9.0`（未公開。前版の公開`v0.8.6`の後継）向けで、前版の公開`v0.8.6`と共通です。旧`v0.8.1`はZIP配布で、単一EXE、「GitHubにログイン」button、本頁の設定保存・適用機能はありません。`0.9.0`のclean-host試験CH-01〜06と本人loginはまだ実施しておらず、公開前に実施します。前版と同時に使うと同じ`setting.txt`を共有するため、片方で保存した設定がもう片方の読込にも反映されます。
>
> 版更新前の製品`0.8.4`では、P06の実EXE試験は開発hostで**7件すべてPASS・独立レビュー指摘0**です。この履歴を版更新後の最終成果物の検証結果へ流用しません。一方、**clean-host（MOTW／Windows保護の実測を含む）と本人loginは`NOT_RUN`**です。`v0.8.6`は公開済みですが、最終成果物のCH-01〜06を実施しないままrepository ownerの明示判断で公開されました。同梱文書や製品版の変更でEXEのbytesが変われば、再package・当該EXEの再検証が必要です。
>
> 一時保存先の実設定fileとfake AI境界を使うdeterministic E2Eの成功は、native WindowsのUI／DPI／Narrator確認・本人walkthroughやrelease完了の証拠ではありません。追加native確認は**NOT COMPLETE（FAIL）**で、Space／Narrator、本人walkthrough、隔離利用者環境のnative保存は`NOT_RUN`です。UIAの行特定段階の失敗をアプリ不具合と断定せず、P06の実測と追加probeの到達範囲は[検証範囲](getting-started.md#v086の検証範囲)で区別してください。

以下は症状を切り分けるための確認事項です。列挙した症状すべてを、再現済みの製品不具合として扱うものではありません。

## コスト値またはジョブログを確認できない

| 表示 | 意味・対処 |
|---|---|
| `—（未取得）` | GitHub Copilot SDKがその項目を返さなかった、または取得できなかった状態です。消費0や請求なしを意味しません。AIを再実行せず、必要なら次の明示runで再確認してください。 |
| `一部取得` | 再試行・取消・通信／RPC取得失敗等により、観測できたattemptだけの値です。画面・ログの数値を請求確定額へ換算しないでください。 |
| `保存失敗` | ジョブログの保存だけに失敗しています。画面上の観測値と採点結果は保持されます。現在の利用者が`%LOCALAPPDATA%\StudyReportEvaluator\jobs`へ書き込めるか、容量・占有を確認し、次回の明示runで再試行してください。EXE横・入力Excelの場所へログを移さないでください。 |
| `詳細記録欠落` | 1ジョブ32 MiBの記録上限または非同期書込の制約で途中詳細が省略されました。終端要約が保存済みかを確認し、画面は最新200行までである点と区別してください。 |
| ログファイルに終端レコードがない／途中行がある | 強制終了・書込中断・手動編集等が考えられます。読める範囲だけを参考にし、請求・監査の証拠とは扱わないでください。アプリ画面はメモリ上の今回履歴を表示するもので、過去ログの再取込・ファイル破損検出結果は表示しません。ファイル内容に回答・credential等を追記しないでください。 |
| 「ログを開く」または「保存先を開く」が使えない | このジョブのログが未作成または保存失敗です。アプリが表示したジョブログ以外のpathを指定して開くことはできません。 |

`nano-AI units`とpremium request消費量はSDK報告値です。AIクレジットは`nano-AI units`を1,000,000,000で割った表示時の換算値で、請求確定額ではありません。アプリは円・ドル・アカウント残量へ換算しません。結果画面の`AIクレジット —（未取得）`はSDKから値を観測できなかった状態、`—（コスト記録なし）`はその結果にコスト記録が付属していない状態、`（一部取得）`は一部の試行の値が部分取得の状態で、いずれも0を意味しません。入力／出力tokenと重複し得る推論／cache内訳を合計tokenとして加算しないでください。記録対象、保存先、保持期間は[データとprivacy](privacy-and-data-handling.md#ジョブコストログ)を確認してください。

## 単一EXEを起動できない（v0.8.6）

公開`v0.8.6`の`StudyReportEvaluator-win-x64.exe`は.NET標準hostが、通常は標準userの一時領域`%TEMP%/.net/<app>/<bundle-id>/`へ内容を展開してからGUIを起動します。**EXEの配置先と抽出先は別**で、EXEを置いたdirectoryがread-onlyであることと、抽出先へ書き込めないことを区別します。

| 状況 | 対処 |
|---|---|
| 入力画面が開く前に抽出先への書込が拒否される | 現在の標準userの一時領域への書込権限を確認。拒否が続く場合は停止して管理者へ相談し、管理者起動や保護設定変更で回避しない |
| 抽出中に空き容量不足になる | EXE保存先だけでなく、抽出先のvolumeの空き容量を確認。EXE本体に加えて展開用の容量が必要。起動のために入力／final／partialを移動・削除しない |
| 以前のcacheが欠落した、抽出が中断された | アプリとその所有CLIの終了を確認後、同じ版の完全なEXEから再起動して.NET標準hostの欠落復元を確認。再失敗時は権限・容量・Windows保護を切り分け、cacheへDLLやCLIを手動で継ぎ足さない |
| EXEのSHA-256がsidecarと違う | 実行せず、同じ版のEXEとsidecarを正式配布元から再取得。不一致のまま実行しない |
| .NETのinstallを求められる | 正しいself-contained `win-x64`配布物か確認。source build outputや別RIDと混同せず、追加runtime導入で回避しない |

アプリとその所有CLIの終了後も、cacheは残って再利用され得ます。これは**アプリ配置用file**であり、学生の入力／final／partial、利用者別の`setting.txt`やCLI credential storeとは別です。単一EXEは「ディスク上も1ファイル」「痕跡なし」を意味しません。アプリによる独自cache管理・自動掃除はなく、本頁でも任意directoryや一時領域全体の再帰削除を復旧手順にしません。結果保存先は明示指定がない場合だけ入力隣接の`result`となり、抽出cacheを既定先にはしません。保存済みの明示出力先は入力変更後も保持します。

## ZIPを起動できない

公開`v0.8.6`では単一EXEが主導線ですが、`StudyReportEvaluator-win-x64.zip`も代替経路として維持します。旧`v0.8.1`のZIPは前回公開版です。

| 状況 | 対処 |
|---|---|
| SHA-256がsidecarと違う | 起動せず、同じreleaseのZIPとsidecarを正式配布元から再取得。異なる版の組合せで比較しない |
| Windowsが発行元warningを表示 | packageはunsigned。[Windowsの警告・実行拒否](#windowsの警告実行拒否)を確認し、出所不明・hash不一致なら実行しない |
| `.exe`が見つからない | ZIP全体を新しいdirectoryへ展開し、package rootの`StudyReportEvaluator.App.exe`を確認 |
| .NETのinstallを求められる | 正しいself-contained `win-x64` packageか確認。source build outputと混同しない |
| Windows 11 x64以外 | 初版対応対象外。互換性を推測して実行しない |

## Windowsの警告・実行拒否

EXE／ZIPはunsignedです。SmartScreen、Smart App Control（SAC）、企業policyによる警告・実行拒否は、抽出先の権限・容量不足とは分けて確認してください。code signing済み、SmartScreen reputation確立済み、すべての端末で無警告・無条件に1操作で起動できるとは保証しません。

- 利用者の手動SHA-256比較は任意・推奨で、sidecarは起動依存ではありません。比較する場合は同じrelease／候補のsidecarと完全一致を確認し、不一致や入手元不明の場合は実行しないでください。同じ配布元のhash一致だけでは発行者の真正性やreputationを保証しません。
- 保護機能が実行を拒否した場合は停止し、保護機能名と表示されたcodeを確認して所属組織の管理者へ相談してください。拒否を起動成功として扱いません。
- 保護機能の無効化、MOTW除去、証明書の自動trust、execution policy変更、UAC回避は行いません。ZIP代替も保護拒否の回避策ではありません。

## 入力を読み込めない

| 表示／分類 | 意味・対処 |
|---|---|
| `UnsupportedExtension` | 標準`.xlsx`を指定 |
| `LegacyBinaryWorkbook` | `.xls` / `.xlsb`。元systemから標準`.xlsx`を再export |
| `CommaSeparatedValues` / `PortableDocumentFormat` | CSV / PDF。標準`.xlsx`を用意 |
| `MacroEnabledWorkbook` | `.xlsm`等。macroなしの標準`.xlsx`を用意 |
| `EncryptedOrRightsProtected` | appは復号しない。組織規則に従い復号済みcopyの利用可否を判断 |
| `InvalidZipSignature` / `CorruptPackage` | Open XML packageとして読めない。元systemから再export |
| `UnsafePackage` | resource上限超過。内容を切り詰めず、file構造とsizeを確認 |
| `InvalidRelationship` | external/unsafe relationship等を除いた別copyを用意 |

native pickerが開かない場合はfull pathを直接入力できます。picker取消時は現在の入力状態を変更しません。

### 起動引数の入力・Promptが見つからない

候補の`StudyReportEvaluator-win-x64.exe`とZIP内の`StudyReportEvaluator.App.exe`は、同じ`--input`／`--prompt`契約です。

- 相対pathの基準は**起動時のcwd**です。EXE配置先や.NET抽出cacheではありません。起動時の作業directoryと指定pathの組合せを確認するか、実在するfull pathを指定してください。入力やPromptをcacheへ移動する必要はありません。
- 日本語・空白を含むpathは1つのargumentとして引用します。`--input`は0または1回、`--prompt`は複数指定でき、指定順を維持します。同じPrompt pathの重複は最初の1件だけを使います。
- unknown option、重複`--input`、値なしは拒否されます。Promptはstrict UTF-8の`.txt`、1〜32,767 UTF-16 code unitsです。詳細は[Promptファイルから起動](prompt-launch.md)を確認してください。
- 起動引数はGUIの準備用です。Promptは適用先を選んで**Promptを適用**した時だけcopyされ、起動・適用からloginやAI評価を自動開始しません。

## mappingを完了できない

| Code | 対処 |
|---|---|
| `SOURCE_SHEET_NOT_FOUND` | 読込済みsheetから選択し直す |
| `HEADER_METADATA_MISMATCH` | 質問文の行を1または2から選び、見出しを再読込 |
| `SELECTED_ROW_LIMIT_EXCEEDED` | 回答rowを20,000以下へ分ける |
| `PRIMARY_COLUMN_REUSED` | 同じ項目の主列を補助列から外す |
| `DUPLICATE_SUPPORTING_COLUMN` | 重複補助列を1件にする |

候補mappingは確定値ではありません。実際の見出しと授業設計に合わせて変更してください。

## 設計がinvalid

| 状況／Code | 対処 |
|---|---|
| Base/Special/Question pointsが範囲外 | 0〜100の有限値へ戻す |
| `ALLOCATION_TOTAL_INVALID` | `Base + Special + enabled Question points = 100`へ合わせる |
| `SPECIAL_ITEMS_REQUIRED` | Special pointsを0へ戻すかenabled固有評価を追加 |
| `SIMILARITY_WEIGHT_OUT_OF_RANGE` | 0〜1へ戻す |
| `WEIGHT_MUST_BE_POSITIVE` | enabled criterion/evaluator weightを0より大きくする |
| `SCORE_RANGE_INVALID` | minimumをmaximumより小さくする |
| `ROUNDING_OUT_OF_RANGE` | 0〜6へ戻す |
| Prompt error | [Custom evaluator](custom-evaluator-guide.md#主なvalidation)を確認 |

step移動ができても、技術検証がinvalidな状態ではrunを開始できません。

## 設定を保存・読み込めない（v0.8.6）

設定は現在の利用者のLocalApplicationData配下、Windowsでは通常`%LOCALAPPDATA%\StudyReportEvaluator\setting.txt`にある**UTF-8 JSON・設定schema整数1**です。実際の場所は**設定 → 共通 → 保存定義**で確認できます。EXE／ZIPの配置先、入力、結果、抽出cache、CLI credential storeとは別で、新しい環境変数・API key・`.env`は不要です。

編集は次回用draftへ反映されますが、編集した共通設定・採点定義をdiskへ書くには**設定を保存**が必要です。画面遷移・主列変更・Prompt適用・終了ではこれらを自動保存しません。**設定から戻る**は編集を保って戻る操作で、保存でも変更破棄でもありません。未保存の変更はアプリ終了後には復元できないため、再起動後も使う場合は終了前に保存結果を確認してください。初回はfileなしで続行し、明示保存または認証・一覧取得成功後のmodelキャッシュ自動保存で初めて作成します。以下の括弧内は実装上の状態分類です。

| 表示・状態 | 意味・対処 |
|---|---|
| 「設定ファイルはまだありません」／fileなし（`Missing`） | 初回は正常。共通設定を確認し、必要なら**設定を保存**。手作業で空fileを作る必要はない |
| 「未保存の変更があります」 | 画面への反映とfile保存は別。**設定を保存**後の状態を確認する。保存中に再編集した内容は、先の保存が成功しても未保存として残り得る |
| 「設定の読込待ち」「設定を読み込んでいます」 | 未読の保存定義を空で上書きしないため、読込完了前は保存できない。完了を待つ |
| 「設定の読込を取り消しました」 | 元fileと編集内容は保持される。初回読込の取消では保存が未許可のままなので、**設定を再読込**してから保存する |
| 「ファイルの形式または値が不正」（`JsonInvalid`） | UTF-8／JSON、schemaの型、必須値・範囲・未知項目等を確認。同じschemaでも未知項目や重複項目は拒否する。自動修復・部分適用はせず、元fileを保持する |
| 「未対応の設定バージョン」（`UnsupportedVersion`） | 設定schemaが整数1以外。製品版や定義revisionとは別なので、数字だけを書き換えて回避しない。作成元の版を確認し、元fileを保持する |
| 「保存先とアクセス権を確認」（`ReadFailed`） | 現在の利用者の読取権限、保存先、他アプリによるfileの占有を確認。原因を解消して**設定を再読込**する |
| 「保存失敗: 設定が不正」（`InvalidSettings`） | 並列度1〜16、絶対出力path、採点定義・Prompt・配点の検証を確認し、現在のdraftを修正する。旧fileは変更しない |
| 「設定を書き込めませんでした」（`WriteFailed`） | 保存先の書込権限・空き容量、他アプリの占有、親フォルダー予定位置が通常fileになっていないかを確認。編集内容と旧fileは保持される。原因解消後に明示保存を再試行する |
| 「保存を取り消しました」 | 置換前なら旧fileとdraftを保持。再保存する場合は明示操作する。置換完了後は成功として扱い、後から取消したことにはしない |
| 「保存先が構成されていません」 | 利用者別の保存先を解決できず、読込・保存を行わない。EXE横やcwdへ移さず、利用者環境を管理者へ確認する |

**破損・未対応版・読込失敗でもoffline編集は継続できますが、後からの明示保存は元fileを置き換えます。** modelキャッシュの自動保存では元fileを上書きしません。自動修復・移行・backupはありません。元の設定を残したい場合は、そのfileを上書きする意図がないまま**設定を保存**を押さないでください。

モデル一覧だけの自動保存は、保存済み設定を読み直して`cachedModels`だけを更新し、未保存の共通設定・採点定義を保存しません。失敗・取消は**設定画面下部のモデル一覧保存状態**で確認し、読込状態・権限・容量等の原因を解消して**Copilot 状態を確認**を再実行してください。一覧取得に成功すれば保存を再試行し、保存済み一覧と同じなら書き直しません。形式と互換性は[設定ガイド](settings.md#保存場所と形式)を参照してください。

**設定を再読込**は保存済みの共通設定を読み直す操作です。読込前からある未保存の共通編集は保存値へ戻り得るため、先に内容を確認してください。読込中の明示編集は優先され、現在の採点定義へは自動適用しません。読込失敗では部分的な設定を反映しません。入力未読込で共通設定だけを保存する場合も、読込済みの保存定義は消しません。

Windowsのフォルダーの**ReadOnly属性だけでは書込拒否を保証しません**。実際の読込・保存結果と利用者のアクセス権を確認してください。拒否が続く場合は安全な表示情報だけで管理者へ相談し、管理者起動や保護設定の変更で回避しません。[Windowsの警告・実行拒否](#windowsの警告実行拒否)も確認してください。アプリを複数起動して保存すると最後に成功した保存が優先され、変更はmergeされません。

### 保存定義を現在のExcelへ適用できない

設定fileを読み込んだだけでは、保存定義を現在のExcelへ自動適用しません。Excelを読み込み、**設定 → 共通 → 保存定義**でsheet・設問数・質問行・回答行の概要を確認し、現在の編集を置き換える意図がある場合だけ**現在の入力に適用**を選びます。

現行UIの適用状態は一般的な失敗案内で、保存定義の具体的な失敗理由・codeは表示されません。入力側の技術検証は現在のdraftに対する結果であり、適用失敗理由ではありません。保存定義のsheet・質問文行（header）・回答行範囲・主列／補助列を、現在の入力Excelと入力画面の設定に手動で照合してください。以下のcodeは実装上の分類で、適用失敗時に画面で確認できるcodeの一覧ではありません。

| 状況・実装上の分類 | 対処 |
|---|---|
| 保存定義なし／Excel未読込／処理中で適用できない | 保存定義の有無を確認し、Excel読込・保存・適用の完了を待つ。run中の一括適用はできないため、run終了後に行う |
| `SOURCE_SHEET_NOT_FOUND`／`SOURCE_COLUMN_NOT_FOUND` | 保存定義のsheet・主列・補助列が現在のExcelに存在するか確認。対応するExcelを選ぶか、適用せず現在の入力に合わせて手動で設計する |
| `HEADER_METADATA_MISMATCH`／行範囲error | 保存時の質問行1／2、回答開始・終了行と現在のsheetを確認。適用処理は保存定義の質問行でread-only再読込し、古いheader metadataを流用しない |
| `INPUT_CHANGED` | 読込後の入力変更を検出。元本の編集を終えてExcelを読み直し、必要なら明示適用を再試行する |
| `SAVED_DEFINITION_LOAD_FAILED`／定義検証error | Excelの形式・アクセス権・破損、定義のPrompt・配点等を確認。強制適用せず、現在の入力・設計と元fileを保持する |
| `SAVED_DEFINITION_CANCELLED`／適用失敗 | 現在のmetadata・Input／Designのdraftと設定fileは変更しない。入力と現在の処理状態を確認してから明示的に再試行する |

成功時だけ保存ID・順序・質問文・Prompt・配点を保持してInput／Designへ反映します。候補の再生成や新しいheader値への暗黙置換は行いません。その後に主回答列を変更した場合は交差セルの質問文を反映し、質問行変更後の再読込前は旧行の値を使わず現在の質問文を保持します。Imported Prompt一覧・本文・順序は保存定義の適用で変わりません。

技術検証の成功は、別Excelの授業内容が同じことやcheckpoint再開可能性の保証ではありません。[既存の再開条件](#checkpointから再開できない)は別に確認します。設定保存・再読込・適用から認証確認・login・AI評価は自動開始しません。

### 指定出力先と実効出力先が違う

- **設定 → 共通 → 共通設定**の「指定出力先」は、絶対pathまたは空欄です。保存した明示指定は再起動・別Excel読込後も保持し、入力変更だけでは`result`に戻りません。
- 空欄への明示編集は未指定（`null`）です。その場合だけ、現在の入力に隣接する`result`が「実効出力先」へ表示されます。入力未選択なら「入力後に決定」で、自動算出したpathは保存しません。次回起動でも未指定にするには**設定を保存**します。
- `OUTPUT_DIRECTORY_INVALID`等では指定path・権限・作成可能性を確認し、修正してください。利用不可でも別pathへfallbackしません。設定の復元だけでは出力directoryを作りません。
- 実行中の編集は次回用です。現在runのimmutable snapshot・予約済みfinal／partial pathを変更せず、再開時のcheckpoint内pathも別の出力指定で置き換えません。

含有情報と平文保存の注意は[データとprivacy](privacy-and-data-handling.md#settingtxtの保存と機密性v086)を確認してください。詳細な操作は[設定ガイド](settings.md)を参照してください。

## Copilotを利用できない

CLIはEXE／ZIPの配布物へ同梱され、manifestでpath、RID、SDK／CLI version、SHA-256を検証します。PATH上の別`copilot.exe`は使いません。GUI起動とAI利用は別条件で、AIにはnetwork、本人login、利用可能なCopilot account／model、組織policy上の許可が必要です。

| 表示 | 確認 |
|---|---|
| CLI unavailable | 配布物の欠落を確認。ZIPを一部だけ移動していないか確認し、下記の再取得・再展開で復旧 |
| runtime確認失敗 | Start／Ping、認証・model取得、timeout、終了処理でも発生する。network・現在の処理状態・組織policyを確認し、所有CLIの終了を確認してから**Copilot 状態を確認**を明示的に再実行。不整合が確認できた場合だけ下記の再取得へ進む |
| CLIのhash／版不一致が確認された | `copilot-runtime.json`または`runtimes\win-x64\native\copilot.exe`の欠落・変更、RID／version／SHA-256不一致を切り分ける。検証を緩めず、下記の再取得・再展開で確認 |
| loginが必要 | 対象版に応じて同梱CLIの本人対話loginを完了。候補版のlogin buttonでは正常終了後に自動再確認し、失敗時は**Copilot 状態を確認**で再試行。旧公開版では手動再確認。対象版別の操作は下記を参照 |
| modelがない | accountで利用できるmodelを確認。model IDを推測入力しない |
| 保存したmodelが選択されない／`MODEL_SELECTION_REQUIRED` | 保存する希望IDと実効選択は別。明示または自動の認証・一覧再確認に成功し、希望IDが候補に存在するときだけ選択へ反映する。不在なら未選択・no fallbackなので、候補を確認し**設定 → 共通**で明示的に選び直す。認証確認の失敗だけでは希望IDを消さない |
| model容量を取得できない（上限が「SDK未公開」） | `auto`などのrouterでは正常な状態。runは開始できるが、model相対の容量検査を行わない。上限を推測して回避せず、事前検査が必要なら上限を公開しているmodelを選ぶ |

**設定 → 共通 → 診断**の認証状態・runtime情報は読取専用の表示で、保存対象外です。希望model IDが未指定の初回は、従来どおり確認成功後に初期選択を表示しますが、それだけで希望IDを保存しません。候補版では保存済み`cachedModels`がある場合（空配列を含む）だけ、起動時に一覧を表示用に復元して認証・一覧を自動再確認します。キャッシュなしの旧設定・初回起動は明示確認またはloginを待ちます。再確認失敗ではキャッシュを保持しても実行用の認証・model選択は解除され、一覧が見えても評価できません。設定の明示再読込・保存では認証確認やloginを始めません。

CLI欠落・不一致時は処理を止め、利用中と**同じ製品版の正式配布物を再取得**してください。ZIP版はpackage全体を新しいdirectoryへ展開します。.NET標準抽出は書換え済みcacheの完全性を保証せず、再取得したEXEでも同じcacheを再利用し得ます。不一致が続く場合は、提供元が同じ製品版・runtime identityのZIPを提供していれば新しいdirectoryへ全体を展開し、なければ提供元へ確認してください。公開`v0.8.6`の代わりに旧公開`v0.8.1`を使ってcheckpoint互換を推測しないでください。

PATH上の別CLIのinstall／追加、他版CLIのcopy、manifest変更やhash検証緩和で回避しないでください。

### 本人loginと完了後の再確認

**「GitHubにログイン」「ログインを取り消す」は公開`v0.8.6`のbuttonです。**旧公開`v0.8.1`にはこのbuttonがなく、従来どおり同梱CLIで本人loginを行い、既存の状態確認buttonを使います。buttonがないことだけを不具合と判断しないでください。

1. 候補版は起動直後にこのPCで利用中のGitHubアカウントのログイン（環境変数`COPILOT_GITHUB_TOKEN`／`GH_TOKEN`／`GITHUB_TOKEN`、OSの資格情報ストア、GitHub CLI `gh`の順に同梱CLIが解決）を自動確認します。手動で再確認するにはExecution画面で**Copilot 状態を確認**を選びます。
2. 利用できるログインがない場合、候補版は検証済みの同梱native CLIだけを直接起動し、ブラウザー認証を1回だけ自動で開始します。CLIのconsole／ブラウザーの案内に従って本人のGitHub accountで対話認証を完了します。取消・失敗後は自動では再試行しないので、必要なら**GitHubにログイン**を選びます。ブラウザーを開きたくない環境では環境変数`STUDY_REPORT_EVALUATOR_AUTO_COPILOT_LOGIN`を`0`または`false`にします。
3. login processが正常終了すると、認証状態とmodel一覧を自動再確認します。取消・失敗後や自動再確認に失敗した場合は、**Copilot 状態を確認**で再試行します。CLI processの起動・終了やexit codeだけを認証成功とは扱いません。
4. 状態と列挙されたmodelを確認し、候補版では必要な変更を**設定 → 共通**で行います。AI評価は**定量化を開始**の明示操作まで始まりません。

起動時の自動確認・自動login以外では、起動引数・Prompt適用・画面遷移・状態確認からloginを暗黙に開始せず、いずれの場合もAI評価を自動開始しません。環境変数`GH_TOKEN`等に無効なtoken（例: 非対応のclassic PAT `ghp_`）が設定されていると、保存済みログインより優先されるため、自動loginを完了しても認証確認が失敗し得ます。その場合は該当の環境変数を解除して再起動してください。実効modelは自動再確認の結果に従って反映し、希望modelが不在でも別modelへfallbackしません。アプリへPAT、token、password、client secret、device codeを入力しないでください。認証とcredential保管はCLI／ブラウザーに委譲し、アプリの認証処理はこれらを収集・解析・保存・log出力しません。設問textやPrompt等へ貼り付けると、明示保存した定義に平文で残り得ます。認証console／ブラウザーの秘密情報をerror reportへ貼らないでください。

### loginの失敗・取消・終了処理

次は公開`v0.8.6`のlogin専用処理です。評価runの`CLEANUP_FAILED`や、final完成後の`PARTIAL_CLEANUP_FAILED`とは区別します。

| 状況 | 対処 |
|---|---|
| login buttonを押せない | loginの二重開始や評価中の開始を防ぎます。現在の処理状態と終了を確認し、buttonを連打して別CLIを開始しない |
| console／ブラウザーが開かない、本人認証に失敗する | 同梱CLI検証、network接続、本人accountのCopilot利用権限と組織policyを確認。CLIの対話が残っていれば**ログインを取り消す**で取り消し、終了を確認してから明示的に再試行。Windowsの拒否なら保護設定を回避しない |
| login processは終了したがmodel一覧を更新できない | 正常終了後の自動再確認が失敗した状態。終了codeだけを認証成功とはせず、network・account・CLI状態を確認し、**Copilot 状態を確認**で再試行 |
| 取消後もブラウザーが開いている、既存loginが残る | 取消はlogoutではありません。下記の所有範囲に従う動作で、ブラウザーやcredentialを削除して解決しようとしない |
| 「ログイン処理の終了を確認できませんでした」等の子process終了・cleanup失敗 | login再試行・AI開始を控え、入力・既存final／partialを保持する。アプリの再起動や認証確認成功だけでは旧login CLIの終了を保証しない。旧処理の終了を確認できてから再確認し、確認できなければsafeな表示情報だけで問い合わせる。名前一致の一括強制終了は行わない |

**ログインを取り消す**またはアプリ終了時に終了・解放する対象は、**このアプリが開始・所有した当該login CLI processだけ**です。process名一致やprocess tree全体の一括killは行わず、ブラウザー・他のCLIを終了しません。credential storeの削除、logout、失効も行いません。終了失敗時も名前指定での強制終了を復旧手順にしないでください。

取消・失敗後もExcel読込、mapping、設計編集、checkpoint確認は利用できます。実行中のアプリはlogin終了未確認時に認証確認・評価を停止しますが、この状態は再起動を越えて保存されません。旧処理の終了未確認のまま、新しいloginやAI処理を開始しないでください。

## capacity error

| Code | 対処 |
|---|---|
| `COLUMN_LIMIT_EXCEEDED` / `HEADER_CELL_LIMIT_EXCEEDED` | enabled Question/evaluator/criterion数またはID長を減らす |
| `FORMULA_LENGTH_EXCEEDED` / `FUNCTION_ARGUMENT_LIMIT_EXCEEDED` | enabled child数を減らす |
| `REQUEST_SCALAR_LIMIT_EXCEEDED` | Prompt、補助列、criterionを縮小 |
| `REQUEST_CONTEXT_BUDGET_EXCEEDED` | contentを黙って切らず、定義または選択列を見直す |
| `ATTEMPT_BUDGET_TOO_LARGE` | 対象rowまたはenabled evaluatorを分割 |

これらは新しいAI送信前に検査されます。

## run status

| Status | 意味 | scoreへの影響 |
|---|---|---|
| `SUCCESS` | valid result | 数値を使用 |
| `EMPTY` | 主値が空 | AI callなし、0相当 |
| `NOT_RUN_ZERO_BUDGET` | Special pointsが0 | 固有評価callなし、Special earned 0 |
| `AI_OUTPUT_INVALID` | schema、ID、range、source等が不正 | 対象値blank |
| `AI_TIMEOUT` | retry後もtimeout（AIの応答待ちはSDK既定の60秒、起動を含む1回の試行は最大120秒） | 対象値blank |
| `NETWORK_FAILED` | retry後もnetwork failure（CLIが返す通信失敗のsession errorを含む） | 対象値blank |
| `AUTH_REQUIRED` | login利用不可 | 対象値blank |
| `CANCELLED` | 未完了operation | 対象値blank、保存済みpartialは保持 |
| `CLEANUP_FAILED` | sessionまたはfile cleanup失敗 | 表示されたcauseとpathを確認 |
| `AI_RUNTIME_FAILED` | その他runtime failure | 対象値blank |

空回答の0と技術的失敗のblankを混同しないでください。

## checkpointから再開できない

再開時は次の一致・互換条件を検証します。

- checkpoint schema
- input path、SHA-256、size、last-write time
- definition SHA-256
- normal model、reference model（normalと同じ）、run-level reasoning effort
- アプリidentity: `アプリ名/バージョン`として解釈できる場合はアプリ名とmajor版が一致。minor／patch差だけでは拒否しない。解釈できないidentityは文字列の完全一致
- CLI version、CLI SHA-256、SDK informational version: 完全一致

条件を満たさない場合はpartialを変更せず、同じ入力・設計・model・packageで再試行するか、新規runを開始します。保存済みfinalはcheckpointとして指定できません。

実行画面で`.partial.xlsx`を選択または入力し、**再開元を確認**を選ぶと、開始前に各条件を表示します。入力・modelの不一致は表示された明示操作で合わせられます。採点設計またはruntimeの不一致は自動変更されないため、中断時の条件へ戻すか新規runを開始してください。

## finalが作られない

| 表示／Code | 対処 |
|---|---|
| `TARGET_EXISTS` | 既存fileを削除せず、新規runなら次のsuffix、override出力なら別名を使う |
| `INPUT_CHANGED` | 入力編集を完了してから最初から読み直す |
| `OUTPUT_INVALID` | partialを保持し、package/formula検証のsafe codeを確認 |
| `CANCELLED` | partial pathを確認し、条件が一致する場合は再開 |
| `PARTIAL_CLEANUP_FAILED` | finalがvalidならfinalは保持される。表示されたpartialを内容を開かず管理 |

新規runのfinalは全処理成功時に自動作成されます。Resultsのoverride反映版だけが任意の別名出力です。

## outputを扱うとき

final/partialは入力全体、Prompt、Reference、AI resultを含み得ます。issueへ添付せず、必要な場合は機密本文を含まないcode・件数・basenameだけを共有してください。詳しくは[データとprivacy](privacy-and-data-handling.md)を参照してください。
