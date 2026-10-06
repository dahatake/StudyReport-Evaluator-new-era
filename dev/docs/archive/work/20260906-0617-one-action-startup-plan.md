# OSのみのWindows端末からの1操作起動 — 調査・実装プラン

| 項目 | 内容 |
|---|---|
| 作成日 | 2026-09-06 |
| 状態 | **確認用・未承認。実装タスクは未着手** |
| 調査対象 | `main` / `26512a4a8a686134ea9bbdf4f271d04ca1fff91b` |
| 要求正本 | `docs/requirements-definition.md` v4.4 / 2026-09-05 |
| 現在の製品候補 | `Directory.Build.props` の `0.8.3`。公開版とは別 |
| 今回の成果物 | 本プランのみ。要求書・コード・設定・既存文書・公開物は変更しない |
| 実行開始条件 | 利用者が本プランを確認し、採用する選択肢と実装開始を明示指示すること |

> 指定された `docs/requirement-definition.md` は存在せず、正本は複数形の **`docs/requirements-definition.md`** だった。別名の要求書は作らない。repository内に `copilot-instructions.md` / `AGENTS.md` は調査時点で見つからなかった。依頼どおり §0 を判断根拠にしない。

## 1. 結論

**第一候補は、既存Appの.NET標準single-file publishによる、Windows 11 x64用self-contained EXEの追加配布。独自ランチャーやインストーラーは作らない。**

ただし、同梱Copilot CLIとマニフェストの配置を保持するために用いる `IncludeAllContentForSelfExtract` は、Microsoftが非推奨と明記する互換モードである。無条件の本実装ではなく、**承認後の最初の小タスクで、固定.NET 10・固定SDK/CLI・本アプリへの適合を実測する**。成立しなければ停止し、別方式を再提案する。[E01][E02][E03]

この条件付き案を第一候補にする理由:

1. 現行の「Core/Appの2 production project」を維持できる。[R03]
2. 独自の展開、キャッシュ、排他、ダウンロード、更新管理コードを持たずに済む。
3. .NET 10の実装では、全コンテンツ展開モードがアプリの基準ディレクトリを展開先にするため、既存の `AppContext.BaseDirectory` 基準のCLI検出と整合する見込みがある。**本アプリでの成功を示すものではない**。[R06][E02][E03]
4. 現行ZIPの生成・公開は残し、単一EXEを別成果物として検証できる。
5. SDK更新、Native AOT化、UI全面改修、採点処理変更を必要条件にしない。

### 1.1 提案する利用体験

- 初回: 正式配布元から取得済みの **`StudyReportEvaluator-win-x64.exe` をダブルクリック → アプリの入力画面**。
- 手動展開、terminal操作、管理者昇格、追加runtime導入は要求しない。
- 2回目以降も同じEXEから起動する。OSの標準ショートカットを利用者が作ることは可能だが、自動作成はしない。
- EXE内部のファイルは.NETがユーザーの一時領域へ展開する。「ディスク上でも常に1ファイル」「痕跡を残さない」という意味ではない。
- AI利用時の初回GitHubログインは別途本人が行う。GUI起動時にはログインもAI評価も自動開始しない。
- Windowsの保護機能による警告・ブロックは回避しない。**未署名のまま、すべての端末で文字どおり1操作になるとは保証できない。**[E04]

以上はすべて未承認の提案である。とくに「1操作」の数え方、ハッシュ確認の扱い、互換モードの採用は §4 の判断が必要。

## 2. 調査で確認した現状

### 2.1 実コード・要求・文書の事実

| ID | 確認済みの事実 | 出典 |
|---|---|---|
| F-01 | 現行正式対象はWindows 11 x64。macOS等は公開対象外 | [R01] §13・17 |
| F-02 | .NET 10 self-contained、CLI同梱、Office不要は既存要求。実行端末へのSDK導入も不要という契約 | [R01] §3・13、[R04] |
| F-03 | 現行publishはrestore/publishとも `PublishSingleFile=false`。これは現行ZIP仕様であり不具合ではない | [R04] L814・857 |
| F-04 | publish検証は外部DLL、runtimeconfig、deps、CLIの実在を前提にする。trueへの単純置換では検証側も一致しなくなる | [R04] `Assert-SafePublishLayout`、[R05] `Assert-PublishInput` |
| F-05 | 利用者向けREADME、docs、画像、LICENSEはpublish後のZIP作成時に追加される。単一EXEではbundle前に含める必要がある | [R05] L433–452 |
| F-06 | CLI検出は `AppContext.BaseDirectory` からmanifestを読み、RID・SDK版・CLI版・SHA-256を検証する。PATH fallbackはない | [R06] L30–161 |
| F-07 | SDK接続は明示したCLIへのstdio。`UseLoggedInUser=true`、`GitHubToken=null`、`LogLevel=None` | [R06] L412–425 |
| F-08 | `Program.Main` は起動引数を読みGUIを開く。入力/Promptの相対パスは起動時cwd基準 | [R07]、[R08] L51–94 |
| F-09 | 認証サービスはStart/Ping/GetAuthStatus/ListModelsによる状態確認であり、ログイン開始処理ではない | [R09] `CheckAsync`、`GetAuthenticationStatusAsync` |
| F-10 | Executionには状態確認buttonがあり、未ログイン時は「CLIでlogin」と案内する。CLI欠落時のPATH案内は現在のbundled-only実装と一致していない | [R10] L410–416・1313–1317、[R11] L99–110 |
| F-11 | 要求とADRではend-user primary setup scriptを対象外としている。採用するなら要求変更が必要 | [R01] §17.2、[R02] Rejected alternatives |
| F-12 | matrix v1はZIPとdevelopment MSIXの2行固定。公開workflowはZIPとsidecarの2 asset固定 | [R12]、[R13]、[R14] |
| F-13 | 文書テストは要求版4.4、AC 28件、claim 38件、system scenario 25件、公開版表示等を具体的に検証する | [R15] |
| F-14 | ZIP試験は開発hostで.NET関連環境変数を隔離して起動する。OSのみの新規端末を実測した証拠とは別 | [R04] `Assert-ApplicationLaunch`、[R16] |

F-10はコード上で確認できる案内の不一致である。GUIが現在起動できない、CLIが実際にPowerShell不足で失敗する、といった未再現の問題は指摘しない。

### 2.2 固定versionと公開状況

- .NET SDK: `10.0.400` / `latestPatch`（`global.json`）。
- Avalonia: `12.1.1`、GitHub.Copilot.SDK: `1.0.11`（`Directory.Packages.props`）。
- 当端末の固定NuGet package `github.copilot.sdk/1.0.11/build/GitHub.Copilot.SDK.props` はCLI `1.0.79` を指定している。
- 同packageのtargetsはCLIを `runtimes/<RID>/native/copilot.exe` へ登録する。FFI用native libraryの条件付きcopyもあるが、現行アプリはstdioを選択している。単一EXEには実際のpublish出力を基準に必要物を含める。
- 最新のSDK main資料ではruntime wrapperの構成も変わっている。**最新資料の構成を1.0.11の実装へ置き換えて推測しない。今回SDK更新はしない。**[E05][E06]
- 公開ページは `0.8.1` をLatest / Immutable releaseとして表示した。[E09] この調査ではasset本体の再ダウンロード・ハッシュ確認を行っていない。新EXEが既に公開されているとは扱わない。
- terminalはPowerShell Core `7.6.5` を確認した。これは調査・開発用hostの情報であり、一般利用者の前提ではない。

### 2.3 「OSだけ」の確認範囲

| 対象 | 現状から言えること | 承認後に必要な検証 |
|---|---|---|
| GUI、Excel読込、設計 | .NET/Officeを別installしない設計 | OSのみ、offline、標準userで実EXEを起動 |
| Copilot runtimeのSDK接続 | native CLIを同梱し、直接stdio接続する | pwsh、Node、Git、gh、外部CLIなしでStart/Ping/auth状態確認 |
| 初回ログイン | 本人認証が必要。現行UIには開始処理がない | 固定CLIのlogin subcommandとブラウザー遷移をfresh userで確認 |
| AI実行 | account、組織policy、network、利用可能modelが必要 | 本人の明示操作でsynthetic入力のみを用いた別試験 |
| Windows保護 | unsignedでは警告・実行拒否の可能性 | MOTW付き実ファイル、SmartScreen/SAC状態を記録し実測 |

GitHubのCLI導入資料はWindowsでPowerShell v6以上を前提としている。一方、SDK同梱資料は別CLI install不要とする。**restricted SDK利用とlogin subcommandにどこまで当該前提が適用されるかは未確認**。shell toolを無効にしているだけで不要と断定しない。[E05][E07][E08]

アプリが利用者に設定を要求する独自環境変数・API keyは確認していない。今回 `.env` を追加しない。SDK/CLIに環境変数の機能が存在しないという意味ではない。

## 3. 実装方法の比較

| 方式 | 評価 | 判断 |
|---|---|---|
| 現行ZIPのみ | runtime不要だが手動展開が残る | 回帰・代替経路として維持。今回の主導線には不足 |
| `PublishSingleFile=true` のみ | native依存・CLI・manifest・docsまで1ファイルになるとは限らない | これだけでは採用しない [E01] |
| self-contained + native/all-content self-extract | 標準機構で既存layoutを保持できる見込み。互換モード非推奨の注意がある | **固定.NET 10での適合試験を前提に第一候補** |
| 独自self-extract-and-run EXE | ZIPを再利用できるが、出荷project、展開/cleanup/引数伝達/失敗UI等が増える | 標準機構が成立しない場合だけ再承認を求める。先回り実装しない |
| `.cmd` + OS標準download/展開tool | PowerShellなしで起動scriptを提供できる可能性。network、hash検証、cmd quoting、保護機能、更新版固定が必要 | 第二候補。標準single-file案と同時実装しない |
| `setup.ps1` | PowerShell 7未導入のOSからそのまま実行できず、execution policy/起動操作も必要 | 主導線にしない。5.1 fallback、policy bypassはしない |
| Native AOT / trimming全面適用 | UI/SDK互換性検証とbuild依存を増やす | 起動1操作のためには不要。対象外 |
| MSIX/MSI/Inno Setup/Store等 | インストーラーやStore固有の工程が増える | 今回対象外 |

`.cmd` の調査では、Windows標準 `curl` / `tar` が利用できる資料を確認した。ただし `certutil` はMicrosoftがproduction codeでの利用を推奨していないため、安易にhash検証を組み合わせて「完成案」としない。[E10][E11][E12]

## 4. 不明点・選択肢・デフォルト

**デフォルトは提案であり、承認ではない。** D-02、D-03、D-04、D-06は利用者体験・リスクの判断を伴うため、実装開始指示の際に確認する。

| ID | 不明点 | 選択肢 | デフォルトの選択肢 | デフォルトを選んだ明確な理由 |
|---|---|---|---|---|
| D-01 | 対象OS | Windows 11 x64 / macOS等も追加 | Windows 11 x64のみ | 要求v4.4を優先。未実測platformを追加しない |
| D-02 | 「1操作」の範囲 | 取得済みEXE→GUI / downloadやOS警告やloginまで含む | 取得済みEXE→GUI。OS警告・本人認証は別途明示 | 配布形式で省ける操作とOS・認証の操作は異なる。後者までの一律保証は不可能 [E04][E08] |
| D-03 | 全展開互換モードの非推奨注意を許容するか | 固定.NET 10で条件付き採用 / 非採用 | 先行適合試験で成立した場合だけ採用 | 2-project制約と最小コードを維持。非推奨を隠さず、未成立時に独自launcherへ自動移行しない |
| D-04 | unsignedを続けるか | 現行同様unsigned / 信頼されたcode signingを別途用意 | 今回はunsignedの技術的1操作起動。警告なしを保証しない | 現行承認scopeを維持。証明書があると仮定しない。厳密な全端末無警告が必要なら計画を再調整 |
| D-05 | fixed CLIでpwsh等が実際に必要か | 不要 / 必要部分だけapp-local同梱 / 手動install要求 | まず未導入VMで検証。必要なら同梱案を再承認 | 手動導入を残すと依頼を満たさない。一方、未確認の依存を予防的に同梱しない |
| D-06 | 初回loginのわかりやすさ | 従来のCLI手順のみ / GUIから同梱CLIのloginを開始 | 小さな「GitHubにログイン」buttonを追加 | 展開先が内部cacheになるため利用者にCLI探索・command入力を要求しない。認証はCLI/ブラウザーに委譲 |
| D-07 | 初回GUI起動にnetworkが必要か | offline可 / download bootstrap必須 | EXE取得後のGUI起動はoffline可 | 同梱済みruntimeを活かし、network失敗を起動条件にしない |
| D-08 | 手動SHA-256確認の扱い | 全利用者必須 / sidecar公開＋必要時の確認 | sidecarは公開・CI必須確認。手動確認は推奨だが起動の必須操作にしない | 手作業のhash比較を要求すると1操作にならない。これは現行利用手順の変更なので明示承認が必要 |
| D-09 | 展開先と残存 | .NET標準cache / 独自cache・自動掃除 | .NET標準。独自掃除なし | 標準に既存抽出の再利用・欠落復元・並行起動処理がある。workbook削除riskと独自管理を増やさない [E13] |
| D-10 | 更新方法 | 新EXEを取得 / 自動更新・差分更新 | 新EXEを取得し旧EXEと併存可能 | 更新機構は依頼外。公開版の差替え禁止を維持 |
| D-11 | 新publish条件でbuild-only依存が増えるか | 現行lockで成立 / analyzer等の専用lockが必要 | spikeで確認し、必要なbuild-only lockだけ固定 | 現行RID比較を無条件に緩めない。analyzerを無効化して通すこともしない |
| D-12 | 単一EXEの圧縮 | 無効 / 有効 | 最初は無効。size/startup実測後に必要なら再判断 | 初回成立確認へ最適化を混在させない。未測定のサイズ・速度を保証しない [E01] |
| D-13 | ZIPとdevelopment MSIXの扱い | 廃止 / 保持 | ZIP保持。既存MSIX sourceと非公開回帰は保持、拡張なし | 現行動作を壊さず、installerの新規開発を持ち込まない |
| D-14 | 製品版 | 0.8.3継続 / 次MINOR | 実装開始時に再確認し、現在値なら0.9.0候補 | 配布形式・login導線の後方互換追加は現行版管理規約のMINOR [R17] |
| D-15 | 要求文書版 | v4.4追記 / v4.5 | v4.5 | 配布・受入契約の改版を履歴上分離する。製品版・checkpoint schemaとは別 |
| D-16 | docsの同梱/閲覧 | onlineのみ / bundleにも保持 | 既存公開docs/画像/LICENSEをbundleへ保持。主案内はRelease/README | 配布内容を減らさない。cacheの固定パスを利用者へ入力させる新手順や専用Help機構は追加しない |
| D-17 | clean-hostをどこで検証するか | 開発PC/hosted CIのみ / fresh VMまたは初期状態実機 | fresh Windows 11 x64・標準user | 開発PCのPATH隔離ではOS-onlyを証明できない。用意できなければその受入はNOT_RUN |
| D-18 | login取消/アプリ終了時のCLI | 継続させる / アプリが開始した当該CLIだけ終了 | 当該CLIだけ終了。ブラウザー・他CLI・credentialは触らない | 所有する子processを残さず、利用者の別作業や既存認証を壊さない。process tree全体のkillや名前一致による一括killはしない |
| D-19 | clean-host証跡の公開処理への受渡し | 新storage/workflow / 既存protected publishのJSON入力 | 既存publish dispatchへ `clean_host_evidence_json` 入力を1つ追加 | 追加基盤なしでV02結果を渡せる。metadataのみを許可し、candidate run/commit/EXE hashへの拘束と人の公開承認で検証する |

署名、PowerShell追加同梱、macOS、別方式の実装を必要とする判断になった場合、本プランへ無断で作業を追加しない。費用・所有者・ファイル差分を示した追加承認を求める。

## 5. 採用案の技術設計

### 5.1 publishとpackage

1. 通常ZIP用folder publishを残す。既存scriptの引数なし動作・出力先を変えない。
2. 同じAppにWindows専用publish profileを1つ追加する。使用する設定は `win-x64`、self-contained、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`IncludeAllContentForSelfExtract=true`。
3. `PublishTrimmed=false`、`PublishReadyToRun=false`、symbols非配布を維持する。既存solution全体をsingle-file設定にしない。
4. 既存 `publish-windows.ps1` に、実際に使用する `-SingleFile` switchだけを追加し、restoreとpublishに同一profile条件を渡す。CLI取得、host検証、version検証は既存処理を再利用する。
5. 単一EXEの出力先を `artifacts/package/publish/win-x64-singlefile/` に分離する。folder用検証を無効化するのではなく、single-file用の最終構成検証を分ける。
6. 既存ZIPの明示allowlistにあるdocs/画像/LICENSEをprofileからbundle前に追加する。repository全体のglobでsample、work、secret、testsを巻き込まない。
7. Copilotのmanifest生成を再利用する。SDKが登録したCLI/contentがbundleへ入ることを確認し、必要なmetadata補正だけをAppの既存targetへ限定する。問題がなければApp `.csproj` は変更不要。
8. 新しいpackage scriptで最終apphostを `StudyReportEvaluator-win-x64.exe` として配置し、その最終bytesのSHA-256 sidecarを作る。後からEXEを書き換えない。
9. 同版ZIPとEXEは同じsource commit・製品版・SDK/CLI版で作る。古い公開ZIPへ新しいcodeを差し込まない。

新規runtime依存package、出荷project、独自自己展開archive形式は追加しない。2つのpublishを同一worktreeで同時実行して、共通 `obj` やCLI取得領域を競合させない。

### 5.2 起動、path、cache

- .NET標準hostが抽出した後に既存 `Program.Main` が起動する。独自launcherでargvを組み直さない。
- 全展開モードでの `AppContext.BaseDirectory` とCLI相対layoutは、spikeとpackage testで確認する。[E02][E03]
- `--input` / `--prompt` のcwd基準、重複Promptの扱い、UTF-8検証、no-auto-runは現行のまま。
- 通常Windowsでは `%TEMP%/.net/<app>/<bundle-id>/` 系の領域を標準hostが利用する。固定したbundle IDや実user名を文書へ埋め込まない。[E01][E13]
- `DOTNET_BUNDLE_EXTRACT_BASE_DIR` は検証時の隔離に利用できるが、利用者に設定を要求しない。GUI内から設定して起動前抽出を制御しようとしない。
- cacheはアプリ配置用であり、利用者dataの保存先にはしない。出力は従来どおり入力隣接の `result` 等。
- appはcacheの再帰削除、旧版の掃除、workbook移動を行わない。削除説明をする場合もアプリ終了後の配布物と利用者dataを明確に区別する。
- 標準hostの欠落復元は「全cached fileの暗号学的検証」ではない。既存CLI hash検証を残し、cacheが別権限userから書換え可能な状態を対応済みとしない。[E01][E13]

### 5.3 初回ログインの小さな改善（D-06採用時）

1. 既存 `BundledCopilotCliPathResolver` で検証済みの絶対パスだけを利用する。
2. 専用serviceはfixed CLIの `login` subcommandを直接開始する。利用可能なoptionは1.0.79のhelpと実測で確定する。最新資料だけからflagを追加しない。
3. shell command文字列、`powershell.exe`、`cmd /c`、任意command実行は使わない。tokenを引数・標準入力・application logへ渡さない。
4. 認証画面/console/browserはCLIに任せ、アプリはdevice codeやtokenを解析・保存しない。独自OAuth App、WebView、callback serverを実装しない。
5. loginの二重開始と評価中の開始を防止する。失敗時もExcel読込・設計を使える状態を維持する。
6. login完了後は既存の「Copilot 状態を確認」で再確認する。自動model選択変更・評価開始は追加しない。
7. CLIの自己更新で固定manifestが不一致にならないことを確認する。必要な更新抑止optionは固定版で実在を確認したものだけを子processへ適用する。
8. CLI欠落時の案内を「配布物の再取得/展開状態の確認」へ合わせ、PATH上の別CLI導入を案内しない。[R10]
9. 利用者によるlogin取消、またはアプリ終了時は、このserviceが開始した当該CLI processだけを終了・解放する。ブラウザー、他CLI、credential storeを終了・削除しない。終了後も既存の認証情報をlogout/失効させない。

新serviceはログイン開始だけを担当する。汎用process runner、認証provider abstraction、DI frameworkは追加しない。既存 `ServiceRegistration` のdefault `ExecutionViewModel` 構築を活かせるならcomposition変更も行わない。

### 5.4 配布と安全性

- 主成果物案: `StudyReportEvaluator-win-x64.exe` / `.exe.sha256`。
- 既存代替: `StudyReportEvaluator-win-x64.zip` / `.zip.sha256`。
- 単一EXEはsidecarなしでも起動できる。sidecarは検証用で、起動dependencyにしない。
- 公開matrixは新しいv2で **EXE、ZIP、非公開development MSIXの3種だけ** を扱う。汎用任意artifact登録機能を追加しない。v1は過去契約として残し、新releaseだけv2を使用する。
- public assetsは明示したEXE/ZIP各2ファイルの計4ファイル。development MSIXや内部evidenceはpublic assetsへ含めない。
- source commit、製品版、bytes/hash、sidecar、package evidence、clean-host evidenceを同じEXEへ結び付ける。
- unsignedの内部hashや同じ配布元のsidecarは、発行者の真正性やSmartScreen reputationの代わりにはならない。
- OS保護の無効化、MOTW除去、証明書の自動trust、execution policy変更、UAC回避は行わない。
- 本プランの実装承認と、tag/push/draft/public Releaseの操作承認は分離する。公開workflowを整備しても勝手にdispatchしない。

## 6. 要求定義書の変更予定

採点、数式、入力保護、checkpoint形式は変更しない。v4.5では次だけを追記/置換する。[R01]

| 箇所 | 変更予定 |
|---|---|
| header・§1目的 | 1操作の対象範囲、単一EXEの追加、installer将来事項を明示 |
| §3利用前提 | 起動とAI-readyを分離。offline GUI、追加runtime不要、本人認証の例外 |
| §11実行画面 | D-06採用時のlogin開始・再確認。常時warning/no-auto-run維持 |
| §12起動 | EXE配布名を追加。現行引数意味とcwd基準を維持 |
| §13配布 | EXE主導線＋ZIP代替、標準抽出、署名/保護の非保証、hash確認の扱い |
| §14安全 | cacheとdataの分離、credential非収集、保護設定を変更しない |
| §17scope | installerを将来事項として維持。一般化/自動更新/追加platformを除外 |
| §18受入 | 既存AC-018・020〜022・026〜028を新経路へ同期。下表の追加ACを採番 |
| §19試験・§21追跡 | single-file/package/clean-host/login試験を既存回帰へ追加 |
| §20出典・§22承認 | 本調査の一次資料、今回の実際の承認指示を記録。未承認を承認済みにしない |

追加受入条件案（番号は実装開始時の重複確認後に確定）:

| ID案 | 完了条件 |
|---|---|
| AC-029 | OSのみ・標準user・取得済みEXE1個から、手動展開/追加導入なしに入力画面を表示。警告/拒否は別途記録 |
| AC-030 | .NET/native dependency/固定CLI/manifestを含み、外部PATH/SDKへ依存しない。CLI integrityを検証 |
| AC-031 | 任意cwd、日本語・空白path、複数Promptの既存起動契約を維持。起動でAI送信0件 |
| AC-032 | 初回/再起動/同時起動/抽出中断/容量・権限不足で利用者input/final/partialを変更・削除しない |
| AC-033 | D-06採用時、本人操作だけで同梱CLIのloginを開始し、credential非収集・取消/失敗時GUI継続・no-auto-runを維持 |
| AC-034 | exact EXEとclean-host evidenceが一致する場合だけ新経路を公開し、未実測のOS-only/署名/installer claimを出さない |

## 7. 小タスクとファイル所有権

### 7.1 共通の実施ルール

- 表のファイルパスはrepository root相対。**新規**は今回まだ存在しない予定ファイル。
- 各タスクは「承認済み選択肢」「関係する要求節」「自分の出力ファイルと直接依存ファイル」だけを読む。過去のwork報告全文を毎回引き継がない。
- 原則として実装1ファイル＋直接test1ファイル、または文書1ファイルに分割する。小さな一体の変更だけ複数ファイルを同じownerへまとめる。
- 同じファイルを複数タスクが同時編集しない。table外の変更が必要なら理由とownerを先に追加する。
- 実装/testを分けた箇所は、後続検証が終わるまで統合gateをPASSにしない。件数・結果を推測しない。
- 引継ぎは「完了ID、変更path、commit/差分、実行したtestと結果、残る不明点」の短い記録に限定する。生ログや機微本文をコミットしない。
- 書換え前に当該fileを読む。diff、診断、直接test、関連既存testを確認する。sourceの0-byte検査はtracked/untracked sourceからbin/objを除外する。

### 7.2 先行検証・要求・版

| ID | 入力/依存 | 所有する成果物・編集ファイル | 作業と完了条件 |
|---|---|---|---|
| S01 | G0: 本プランとD選択の承認 | **新規** `dev/docs/preflight/windows-singlefile-feasibility.md`。prototypeと結果は専用ignored `artifacts/test/singlefile-spike/` のみ | §8の先行試験をisolated作業領域で実施。実.NET/runtime版、files-to-bundle、path、lock差分、fixed CLI helpを記録。失敗ならG1で停止 |
| R01 | G1: S01適合 | `docs/requirements-definition.md` | §6の差分だけでv4.5へ。AC/試験を追跡でき、旧業務要件が欠落しない |
| R02 | R01 | **新規** `dev/docs/adr/0016-windows-one-action-startup.md` | 条件付き互換モード採用、2-project維持、ZIP併存、installer除外を決定。旧ADR本文は歴史として残す |
| R03 | R01、D-14 | `Directory.Build.props`、`CHANGELOG.md` | 実装開始時の現在版から合意したMINOR候補へ。既存Unreleased項目を保持。tag・公開は行わない |

### 7.3 publish/package（追加projectなし）

| ID | 入力/依存 | 所有する成果物・編集ファイル | 作業と完了条件 |
|---|---|---|---|
| P01 | R02、S01 | **新規** `src/StudyReportEvaluator.App/Properties/PublishProfiles/WindowsSingleFile.pubxml`、**新規** `tests/StudyReportEvaluator.App.Tests/Packaging/WindowsSingleFileProfileTests.cs` | 固定profile、docs allowlist、symbols除外を検証。通常build/ZIP/macOSに適用されないこと |
| P02 | P01、P03完了またはN/A確定 | `scripts/publish-windows.ps1`、**新規** `tests/StudyReportEvaluator.App.Tests/Packaging/WindowsSingleFilePublishTests.cs` | 実使用するswitch、出力分離、restore/publish整合、単一file検証。引数なしfolder方式とhash/版チェックを維持 |
| P03 | R02、S01のlock判断（必要時のみ） | **条件付き新規** `src/StudyReportEvaluator.App/packages.win-x64-singlefile.lock.json`、`src/StudyReportEvaluator.Core/packages.win-x64-singlefile.lock.json`。必要時のみ `Directory.Packages.props`、`tests/StudyReportEvaluator.App.Tests/SupplyChain/PackageLockTests.cs` | single-file analyzer等の実在するbuild-only差分だけを固定。不要ならファイルを作らずN/A。製品依存更新、unknown package全面許可、analyzer抑止は不可 |
| P04 | P01・P02、必要なP03 | **条件付き** `src/StudyReportEvaluator.App/StudyReportEvaluator.App.csproj` | 既存manifest/CLI contentがbundleへ入らないと実測した場合だけmetadataを最小修正。不要ならN/A。resolverのhash検証を弱めない |
| P05 | P02・P04、R03 | **新規** `scripts/package-windows-singlefile.ps1`、**新規** `tests/StudyReportEvaluator.App.Tests/Packaging/WindowsSingleFileArtifactTests.cs` | 最終EXE、正確なsidecar、版、単一file、禁止物非混入を検証。ZIP作成scriptは変更しない |
| P06 | P05 | **新規** `tests/StudyReportEvaluator.App.Tests/Packaging/WindowsSingleFilePackageTests.cs` | 実EXEの隔離起動、展開layout、native/CLI/hash、args/cwd、再起動/同時起動、data不変を確認。既存ZIP用3 testsへ混在させない |
| P07 | P06 | **新規** `scripts/test-windows-singlefile.ps1` | developer/CI向け実行とmachine-readable evidenceを生成。実際のtest結果から判定し、開発host成功をclean-host成功に変換しない |

P03の正確なlock対象と保存形式はS01で確定する。canonical lockと同一で成立する場合、専用lockや追加packageは作らない。P02のownerだけがpublish scriptのlock検証部分を編集する。

### 7.4 login導線（D-06採用時だけ）

| ID | 入力/依存 | 所有する成果物・編集ファイル | 作業と完了条件 |
|---|---|---|---|
| A01 | R02、S01のfixed CLI調査 | **新規** `src/StudyReportEvaluator.App/Copilot/BundledCopilotLoginService.cs`、**新規** `tests/StudyReportEvaluator.App.Tests/Copilot/BundledCopilotLoginServiceTests.cs` | 検証済み絶対CLIをlogin専用に起動。fake process境界で引数/失敗/credential非収集を検証。起動・cancel・close時の子process所有範囲を決める |
| A02 | A01 | `src/StudyReportEvaluator.App/ViewModels/ExecutionViewModel.cs`、**新規** `tests/StudyReportEvaluator.App.Tests/UI/CopilotLoginCommandTests.cs` | 明示command、二重起動防止、実行中無効、失敗後再試行、既存状態再確認。PATH案内修正。既存constructor呼出しを維持 |
| A03 | A02 | `src/StudyReportEvaluator.App/Views/ExecutionView.axaml`、`tests/StudyReportEvaluator.App.Tests/UI/ExecutionViewTests.cs` | login buttonと説明だけ追加。keyboard、Automation ID、200% scale、既存4step/常時warning/no-auto-runの回帰確認 |

標準profileだけで成立する場合、`Program.cs`、`LaunchOptions.cs`、`CopilotClientFactory.cs`、Core、workbook処理は非変更。これらのAPIを先回りして書き換えない。

### 7.5 公開契約とCI

| ID | 入力/依存 | 所有する成果物・編集ファイル | 作業と完了条件 |
|---|---|---|---|
| C01 | R02、P07のevidence仕様 | **新規** `eng/schemas/platform-release-matrix-v2.schema.json` | 3種類の固定row。EXEのartifact/sidecar/試験/clean-host記録、§8.5の必須試験IDを厳格に定義。v1を上書きしない |
| C02 | C01、P07 | `scripts/validate-platform-release-matrix.ps1`、`tests/StudyReportEvaluator.App.Tests/Packaging/ReleaseMatrixContractTests.cs` | EXE/ZIP/MSIXのexact set、source/version/hash/sidecar、必須clean-host試験の欠落・差替え・FAIL/NOT_RUNを拒否。MSIX実物はcandidate検証記録で拘束し、公開時に再検証したと偽らない |
| C03 | C02 | `scripts/build-platform-release-matrix.ps1`、**新規** `tests/StudyReportEvaluator.App.Tests/Packaging/ReleaseMatrixBuilderTests.cs` | 実検証evidenceからv2生成。clean-host結果は `-CleanHostEvidencePath` で受領。MSIXは検証済みcandidate recordからrow生成し、本体再取得・再作成を要求しない。未実測trueの生成は禁止 |
| C04 | P07 | `.github/workflows/ci.yml` | 既存ZIP回帰後にEXE検査を直列追加。機微live試験は実行しない。CI結果にclean OS保証を付けない |
| C05 | C03・C04 | `.github/workflows/release.yml` | 同じtag/commitでEXE/ZIP候補とMSIX検証記録を作成。draft assetは4個。内部control artifactに全package evidenceとMSIX artifact/sidecarのdescriptorを保存し、本体をuploadしない。clean-host待ちを明示 |
| C06 | C05 | `.github/workflows/publish-release.yml` | `clean_host_evidence_json` をenv経由で受領して一時file化。candidate runのworkflow/成功/commitとEXE hashへ拘束。4 public assetsを再download・検証し、v2最終matrixとclean-host記録をcontrol artifactへ保存後に公開可能とする |
| C07 | C04・C05・C06 | `tests/StudyReportEvaluator.App.Tests/Packaging/ReleaseWorkflowContractTests.cs` | draft/public分離、4 asset、MSIX非公開、evidence欠落時拒否、同じcandidate SHAへの拘束を回帰検証 |

candidate生成にはclean-host結果がまだないため、そこで公開可能matrixを偽造しない。**候補生成 → 対象EXEの実機検証 → protected publish時にv2最終matrix確定**の順に変更する。CIはpackage evidenceまで、公開判定は別段階とする。

証跡の具体的な受渡し:

1. C05はcandidate run ID/commitと、EXE/ZIPのbytes/hash/sidecar/evidence、development MSIXを実物検証した結果とartifact/sidecar descriptorを、既存のcandidate control artifactへ保存する。MSIX本体はGitHub ReleaseにもActions artifactにも追加しない。
2. V02の担当者は当該candidateのEXEを使用し、同じrun ID/commit/EXE hashを含むclosed JSONをローカルで作る。公開前は製品repoへcommitせず、機微情報を除いたmetadataだけにする。
3. 公開の別承認後、担当者がそのJSONをC06の `clean_host_evidence_json` へ渡す。workflow式をshell本文へ直接埋め込まず環境変数で受け、型/長さ/許可field/必須試験結果を検査する。人の試験結果であることを明記し、hash一致だけで実施事実が自動証明されたと扱わない。
4. C06は元candidate runが指定repositoryの `release.yml` による成功runであることと、tag commitの一致を確認する。EXE/ZIP実物は再取得して検証し、MSIXはそのrunの検証記録とdescriptorを照合する。既存validatorの「すべてのrowの本体が公開時にもlocalにある」という前提はC02で明示変更する。
5. 必須試験がPASSの場合だけfinal matrixと受領JSONをpublish runのcontrol artifactへ保存し、protected environmentの承認に従って公開する。live AI未実施を理由に必須GUI/login結果を捏造しない。

### 7.6 文書（ファイル単位の分担）

| ID | 入力/依存 | 所有する成果物・編集ファイル | 作業と完了条件 |
|---|---|---|---|
| D01 | R02、P01〜P07 | `dev/docs/architecture.md` | .NET標準抽出とCLI解決、2-project、EXE/ZIP併存のdata flowを反映 |
| D02 | D01、A03またはD-06非採用確定、C03 | `dev/docs/detailed-design.md` | profile、path、cache、login、失敗、candidate→検証→公開の順序を実装へ同期 |
| D03 | R01、P06、A03、C07 | `dev/docs/traceability.md` | AC/TRを直接test、package試験、clean-hostのownerへ割当。既存PASSを新経路へ転記しない |
| D04 | D03 | `dev/docs/implementation-status.md`、`dev/docs/readme-claim-ledger.md` | 新claimは未検証/BLOCKEDで開始。検証された範囲だけ更新する判定欄を用意 |
| D05 | P05、A03、D-02/04/08確定 | `README.md` | 1ファイル起動を主案内、ZIP代替、保護/本人認証の例外。公開版と未公開候補を分離 |
| D06 | D05 | `docs/README.md` | 主導線・読者別guide・配布範囲を同期 |
| D07 | P06、A03、D05 | `docs/getting-started.md` | OS-only準備、EXE起動、初回login、ZIP代替、追加runtime不要の検証範囲を説明 |
| D08 | P06、A03 | `docs/troubleshooting.md` | 抽出先権限/空き容量/保護機能/CLI不一致/login失敗を区別。保護無効化を案内しない |
| D09 | P06、A01 | `docs/privacy-and-data-handling.md` | 標準cacheとuser workbook/CLI credential storeの違い、削除対象外、本文非logを説明 |
| D10 | P06 | `docs/prompt-launch.md` | 配布EXE名を追加。cwd/複数Prompt/起動後no-auto-runを維持。新引数を増やさない |
| D11 | A03採用時 | `images/05-execution-auto.png`、`images/README.md`、`tests/StudyReportEvaluator.App.Tests/UI/DocumentationScreenshotTests.cs` | synthetic/fakeで必要なExecution画像だけ同期。他画像は変更がなければ保持。実認証の証跡として使わない |
| D12 | R02、P07、D02 | `dev/README.md`、`dev/docs/README.md` | 新profile/package/試験/ADRへの開発者向け入口を追加 |
| D13 | R03、P05、C06 | `dev/docs/version-management.md` | EXE/ZIP同版、最終EXEと展開DLLの版検証、4 assetとclean-host受渡しを追加 |
| D14 | R01、P06、A03、C07 | `SystemTest-prompt.md`、`tests/StudyReportEvaluator.App.Tests/E2E/RealDataSystemSmokeTests.cs` | clean-host/login/単一EXEの独立scenarioを追加。後者は要求版provenance文字列のみ同期。実data試験を今回理由なく走らせない |
| D15 | D03〜D14 | `tests/StudyReportEvaluator.App.Tests/Content/DocumentationContractTests.cs` | 要求版/AC/TR/scenario/claim件数と現在の公開・未公開表示を同期。既存安全契約を削除してgreenにしない |

A系非採用の場合、その依存は「D-06非採用確定」で置き換え、D11はN/Aとする。文書だけを先に新機能対応済みと表示しない。

### 7.7 統合検証・完了

| ID | 入力/依存 | 成果物 | 作業と完了条件 |
|---|---|---|---|
| V01 | P/A/C/Dの必須タスク完了 | ignored `artifacts/test/singlefile/` 内の自動試験サマリー | locked restore、Release、既存Core/App、追加test、ZIP回帰、EXE package、静的macOS/MSIX、文書契約を検証。freshな結果だけを採用 |
| V02 | V01、最終EXE候補 | **新規** `dev/docs/preflight/windows-singlefile-clean-host.md`、ignored `artifacts/test/singlefile/clean-host.evidence.json` | §8のfresh OS試験。exact bytes/環境/操作回数/結果を記録。本人認証が必要なら利用者が直接操作。artifact再作成後は該当試験を再実行 |
| V03 | V01・V02 | 最終差分レビューと短い実施報告 | 機能・docs・証跡の一致、data不変、過剰実装なしを確認。D04等の各文書ownerへ結果を返し担当者が最終状態を反映 |

V03は他ownerの文書を並行編集しない。正本doc変更でbundle bytesが変わる場合は再packageとV01/V02を必要範囲で繰り返す。機械生成evidenceの巨大ログや実user情報をbundleへ入れない。

## 8. 検証計画と停止条件

### 8.1 S01 — 本実装前の最小spike

固定SDK、CLI、Avalonia、.NETを変えず、temporary profileを隔離した作業領域で使う。S01は後続の正式profile/コードを作り込む作業ではない。

1. 選択された実SDK/runtime patch版を取得。v10.0.0の公開sourceと同じ動作かを実物で確認。
2. single-file restoreで増えたdependency/lock targetを確認。現在のcanonical lock比較をそのまま適用できるかを決める。
3. publish出力で「EXE以外の実行必須fileがない」ことを確認。`FilesToBundle`、CLI/manifest/native/docsの相対名を確認。
4. `DOTNET_BUNDLE_EXTRACT_BASE_DIR` をtest専用directoryへ設定した隔離起動で、App base、CLI path、SHA-256、SDK identityを確認。製品への診断専用flag追加はしない。
5. cwdがEXE配置先と異なる場合、日本語/空白path、複数Prompt、offline GUIを確認。
6. 固定CLIの `login --help` 等の非認証操作で利用可能command/自己更新抑止を確認。通常のAI対話を起動しない。
7. サイズ、抽出容量、初回/再起動時間を測定し、未測定値を記入しない。
8. fresh OSでの最小GUI/runtime probeが準備できるかを確認。依存不足が出たら自動installではなく原因と最小同梱案を提示する。

**G1 PASS:** 独自展開コード・追加project・CLI検証緩和なしで方式が成立し、非推奨注意を承認済みであること。未準備項目はNOT_RUNとして列挙し、OS-only受入完了とはしない。

**G1 STOP:** metadata調整を超えるpath回避実装、CLI/SDK変更、追加runtime同梱、独自launcherが必要になった場合。調査結果と差分案を提示して再承認を得る。

### 8.2 自動試験

| 種類 | 必須観点 |
|---|---|
| profile/restore | Windows single-file条件限定、default ZIP維持、locked依存、symbolsなし |
| package | final EXE1個、AMD64/版、sidecar exact bytes、同梱CLI/hash、docs/LICENSE、workbook/secret/test非混入 |
| 抽出 | 初回、再起動、同時起動、欠落復元、抽出中断、書込不可/容量不足。実施できないfaultはNOT_RUN |
| 起動引数 | 任意cwd、日本語/空白、複数Prompt、相対path、従来invalid入力、no-auto-run |
| login | 検証済みCLIのみ、固定login引数、二重開始防止、評価中無効、取消/失敗、credential非log、本人操作まで未開始 |
| data保護 | synthetic input/final/partialのhash/size/時刻不変、default result位置、EXE/ZIP間の同版checkpoint互換 |
| 公開 | unknown/duplicate/missing row、MSIX公開、wrong source/version/hash、sidecar不一致、clean-host NOT_RUN/別EXEの証跡を拒否 |
| 文書 | local links、未公開表示、要求/AC/TR/claim/scenarioの対応、保護設定変更を要求しない |

SDK handshake/本人認証/AI応答は同じ意味ではない。fakeによるdeterministic検証とlive検証を混同しない。

### 8.3 V02 — OSのみの実機/VM

- supported Windows 11 x64のOS buildを特定し、標準userのfresh状態で実施する。Windows機能追加や管理者権限を利用者の前提にしない。
- .NET SDK/Runtime、PowerShell 6+、Node/npm、Git/gh、別Copilot CLI、Office、IDEが未導入であることを観測する。OS付属.NET Framework等と本アプリ用.NETを混同しない。
- guestへdeveloper test SDKやPowerShellを入れてから「OSのみ」と呼ばない。OSのUI/native commandによる確認と、host側の検証を分離する。
- 取得したファイルはEXE1個のみ。隣接dll/manifest、repository、既存CLI cache/認証、sidecarを起動に利用しない。
- 標準ブラウザーで取得したMOTW付きEXEで確認し、OS警告の有無・操作回数・SAC/SmartScreen状態を記録する。警告が出た事実を成功1操作へ丸めない。
- offlineのGUI/Excel読込/設計、onlineのCLI状態確認、本人操作の初回login、再起動後の再確認を分ける。
- live AIを実施する場合は本人の承認とsynthetic workbookのみ。学生data、PAT、device codeをevidenceへ保存しない。従来のAI品質/全model保証へ拡大しない。
- fileを移動した再起動、2回起動、read-onlyなEXE配置先、日本語user/data path、cache欠落後の再起動を確認する。
- 初回/再起動の所要時間、EXE size、実際の展開容量を記録する。今回数値SLAは設定しない。

### 8.4 evidenceの最小契約

packageとclean-hostの記録を分け、公開時に照合する。新しい汎用証跡platformは作らない。

- artifactのbasename、bytes、SHA-256、製品版、source commit。
- 実SDK/runtime/CLI/SDK package版、CLI hash。
- OS edition/build/architecture、標準userか、追加依存の有無、保護機能の状態。
- 試験ID別 `PASS` / `FAIL` / `NOT_RUN` と操作回数。GUIとlogin/AIを別項目にする。
- 実施記録への参照/hash。username、token、device code、学生本文、環境変数値の一覧は含めない。
- `PASS_REQUIRED` は署名済みという意味にしない。既存 `PASS_PRODUCTION` をunsignedへ流用しない。
- 認証/live AIが未実施ならその事実を残す。OS-only CLI利用の確認が不足したまま「何も導入せずAI利用可」と公開しない。

### 8.5 公開判定で必須にする試験ID

| ID | 内容 | 公開条件 |
|---|---|---|
| CH-01 | fresh OS/architecture/標準user/追加依存未導入の確認 | 必須PASS |
| CH-02 | EXE1個からoffline GUI・Excel読込・設計を利用 | 必須PASS。保護機能が実行を拒否した環境を起動成功としない |
| CH-03 | 同梱CLIのStart/Ping/auth状態確認。pwsh/Node/Git/gh/外部CLIへ依存しない | 必須PASS。未認証という正常応答とruntime failureを区別 |
| CH-04 | 移動/再起動/同時起動/args/cwd/data保護 | 必須PASS |
| CH-05 | ブラウザー取得時のMOTW、保護状態、実際の操作数を記録しD-02/04の承認範囲と比較 | 必須PASS。無警告を一律要求する意味ではなく、警告も正しく開示する |
| CH-06 | fresh userの本人login、完了後の再確認、取消/アプリ終了時に他process/dataを傷つけない | D-06採用時は必須PASS。非採用なら理由付きN/A |
| ADV-01 | 認証済みsynthetic入力による実AI評価 | 任意。NOT_RUN可、制限を記録。必須試験の代替にしない |
| ADV-02 | Office等の外部spreadsheet再計算 | 既存同様任意。NOT_RUN可 |

C02は必須IDの欠落、FAIL、NOT_RUNを拒否し、任意IDのNOT_RUNを公開blockへ変換しない。CH-06採用/非採用は要求/ADRの決定と一致させ、JSONの任意指定だけで必須試験を外せないようにする。既存Core/App/packageのrequired testも維持する。

## 9. 直列・並列実行の設計

| Wave | 実行 | 並列化とbarrier |
|---|---|---|
| 0 | G0 → S01 → G1 | 直列。方式不成立なら全後続を停止 |
| 1 | R01 → R02。R01後にR03 | 要求変更を先に確定。版とADRは別fileだが最終値を共有 |
| 2 | P01/P03、A01 | packagingとloginは別fileで並列可。P03の判断をP02へ渡す |
| 3 | P02 → P04 → P05 → P06 → P07、A02 → A03 | 実装編集は分岐で並列可。同一worktreeのbuild/publishは直列 |
| 4 | C01 → C02 → C03、C04 | C04はP07後に独立。schema/validator/builderは入出力が依存するので直列 |
| 5 | C05 → C06 → C07、D01〜D14 | 文書は各tableの直接依存完了後に別fileごと並列。D02はD01後、D06はD05後 |
| 6 | D15 → V01 → V02 → V03 | 最終統合・対象host検証は直列。未検証claimの公開は禁止 |
| 公開 | 別の公開指示 → candidate確定 → exact EXEのV02照合 → protected publish | 実装完了だけでtag/push/公開を実行しない |

共通編集fileのownerは1人に固定する: publish script=P02、App csproj=P04、Execution VM=A02、view=A03、matrix validator=C02、builder=C03、文書契約test=D15。generated package/evidenceも同じ場所へ並行生成しない。

## 10. `/docs`調査結果と非変更範囲

`docs`の全8文書を対象に必要性を調査した。

| 文書 | 判定 | 担当/理由 |
|---|---|---|
| `requirements-definition.md` | 更新必須 | R01。主配布・利用前提・受入が変わる |
| `README.md` | 更新必須 | D06。入口がZIPからEXE主体になる |
| `getting-started.md` | 更新必須 | D07。手動展開から起動、初回loginへ |
| `troubleshooting.md` | 更新必須 | D08。抽出/保護/loginの説明 |
| `privacy-and-data-handling.md` | 更新必須 | D09。runtime cacheとcredential/dataの区別 |
| `prompt-launch.md` | 更新必須 | D10。配布EXE名だけを追加し意味は維持 |
| `features.md` | 機能本文は変更不要 | 採点機能は同じ。R03で候補版を変更した場合のみD15の文書整合ownerが対象版注記を同期する |
| `custom-evaluator-guide.md` | 変更不要 | Prompt・評価契約を変更しない |

`docs/features.md` の版注記を変更する必要がある場合、その編集権はD15に限定する。他文書ownerと同時編集しない。`LICENSE`、既存ADR群、macOS sign/notary、MSIX実装、Core/Excel domain、checkpoint schemaは本案の変更対象ではない。

## 11. オーバーエンジニアリングを防ぐ停止線

今回作らないもの:

- 新しい出荷project、独自launcher、自己展開engine、汎用package manager。
- setup.ps1/cmdの並行提供、online bootstrap、auto-update、差分patch、repair/uninstall。
- registry/PATHへの恒久変更、file association、URI handler、サービス、常駐process。
- cache管理UI、自動cleanup scheduler、独自locking/cache index/database。
- installer、Store登録、署名証明書の購入/仮設定、macOS対応。
- token入力UI、独自OAuth実装、WebView、cloud backend。
- SDK/Avalonia更新、Native AOT、trimming、業務codeの無関係なrefactor。
- 詳細log追加、ユーザーdata収集、テスト専用の製品起動flag。

対象外機能が必要になったという主張には、再現結果・一次資料・最小の追加差分・選択肢を添える。必要性を仮定して実装を続けない。

## 12. 出典

### Repository（調査基準commitに対する参照）

| ID | 出典と確認範囲 |
|---|---|
| R01 | [要求定義書][R01] v4.4。特に§3、11〜14、17〜22 |
| R02 | [ADR-0015][R02]。現行ZIP主導線、setup script不採用、MSIX/macOSの境界 |
| R03 | [architecture][R03] §1・8、および `tests/StudyReportEvaluator.Core.Tests/Architecture/DependencyRulesTests.cs`。2 production project制約 |
| R04 | [publish-windows.ps1][R04]。self-contained、folder layout、restore/lock、起動probe |
| R05 | [package-windows.ps1][R05]。CLI検証、docs allowlist、ZIP/sidecar作成 |
| R06 | [CopilotClientFactory.cs][R06]。bundled resolver、hash/版、stdio、credential指定 |
| R07 | [Program.cs][R07]。起動entry |
| R08 | [LaunchOptions.cs][R08]。cwd/Prompt/no-auto-run関連の起動処理 |
| R09 | [CopilotAuthenticationService.cs][R09]。状態確認とcleanup |
| R10 | [ExecutionViewModel.cs][R10]。状態表示、状態確認command、PATH案内 |
| R11 | [ExecutionView.axaml][R11]。現行Executionの操作 |
| R12 | [matrix v1 schema][R12]。2 row、固定artifact集合 |
| R13 | [release.yml][R13]。candidate生成、draftの2 asset固定 |
| R14 | [publish-release.yml][R14]。protected publish、matrix照合、2 asset固定 |
| R15 | [DocumentationContractTests.cs][R15]。要求/版/文書/件数の固定契約 |
| R16 | [test-windows-zip.ps1][R16]。現在のZIP試験/evidence |
| R17 | [version-management.md][R17]。MINOR規則、source/製品/公開版の分離 |

固定依存のローカル一次資料: `Directory.Packages.props`、`global.json`、`Directory.Build.props`、`C:\Users\dahatake\.nuget\packages\github.copilot.sdk\1.0.11\build\GitHub.Copilot.SDK.props` / `.targets`。NuGet cacheそのものは配布・commitしない。S01で実際の使用packageとの一致を再確認する。

### 外部一次資料（2026-09-06取得）

| ID | 出典 | 本プランで使用した事実 |
|---|---|---|
| E01 | [Microsoft: Single-file deployment][E01] | self-contained/native抽出、全展開モードの非推奨注意、抽出先、API/path、圧縮のtrade-off |
| E02 | [.NET runtime v10.0.0: Bundler.cs][E02] | BundleAllContentとnetcoreapp3CompatModeの関係 |
| E03 | [.NET runtime v10.0.0: deps_resolver.h][E03]、[hostpolicy_context.cpp](https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/native/corehost/hostpolicy/hostpolicy_context.cpp) | 互換モードの抽出先とAppContextBaseDirectory。実SDK patchの試験は別途必要 |
| E04 | [Microsoft: SmartScreen reputation][E04] | unsigned/署名付き双方のwarning、企業policy/SACの実行拒否、署名だけで無警告を保証しない |
| E05 | [GitHub SDK: Bundled CLI][E05] | CLI同梱・stdio・signed-in credential。main資料と固定versionを区別 |
| E06 | [GitHub SDK: .NET README][E06] | 最新構成/APIの確認用。1.0.11にそのまま適用しない |
| E07 | [GitHub: Installing Copilot CLI][E07] | WindowsのPowerShell前提、npm導入時のNode前提 |
| E08 | [GitHub: CLI authentication][E08]、[command reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/cli-command-reference) | 本人OAuth、login subcommand、credential保存の委譲。固定版commandは別途確認 |
| E09 | [StudyReport Evaluator public release v0.8.1][E09] | 公開版の存在、Latest/Immutable表示。asset bytesは今回未検証 |
| E10 | [Microsoft: curl on Windows][E10] | OS標準download tool |
| E11 | [Microsoft: tar on Windows][E11] | OS標準archive tool |
| E12 | [Microsoft: certutil][E12] | production codeでの利用非推奨という注意 |
| E13 | [.NET runtime v10.0.0: extractor.cpp][E13] | 標準cacheの再利用、欠落file復元、抽出途中/並行実行の処理 |

Avaloniaの最新Windows資料も確認したが、framework一般の対応を本アプリのOS-only成功証拠には使っていない。

## 13. 今回の実施範囲と承認待ち事項

- 実施済み: 要求/配布/起動/認証/CI/文書の読み取り調査、公式仕様の確認、タスク・依存関係・出典の整理、本プラン作成。
- 未実施: prototype、restore/build/test、単一EXE生成、clean-host試験、login/AI操作、要求書/code/docsの改修、署名、tag/push/公開。
- 現時点で単一EXEの動作、OS-only成功、起動時間、サイズ、AI利用可否を実測済みとは表示しない。
- 承認時に指定いただく内容: **D-01〜D-19をデフォルトで採用するか、変更するIDと選択肢、およびS01以降の実装開始指示**。
- 「まずS01の適合試験だけ」を指示することも可能。その場合は結果報告後に後続実装を待つ。

[R01]: ../../../../docs/requirements-definition.md
[R02]: ../../../../dev/docs/adr/0015-windows-macos-installer-delivery.md
[R03]: ../../../../dev/docs/architecture.md
[R04]: ../../../../scripts/publish-windows.ps1
[R05]: ../../../../scripts/package-windows.ps1
[R06]: ../../../../src/StudyReportEvaluator.App/Copilot/CopilotClientFactory.cs
[R07]: ../../../../src/StudyReportEvaluator.App/Program.cs
[R08]: ../../../../src/StudyReportEvaluator.App/Launch/LaunchOptions.cs
[R09]: ../../../../src/StudyReportEvaluator.App/Copilot/CopilotAuthenticationService.cs
[R10]: ../../../../src/StudyReportEvaluator.App/ViewModels/ExecutionViewModel.cs
[R11]: ../../../../src/StudyReportEvaluator.App/Views/ExecutionView.axaml
[R12]: ../../../../eng/schemas/platform-release-matrix-v1.schema.json
[R13]: ../../../../.github/workflows/release.yml
[R14]: ../../../../.github/workflows/publish-release.yml
[R15]: ../../../../tests/StudyReportEvaluator.App.Tests/Content/DocumentationContractTests.cs
[R16]: ../../../../scripts/test-windows-zip.ps1
[R17]: ../../../../dev/docs/version-management.md
[E01]: https://learn.microsoft.com/dotnet/core/deploying/single-file/overview
[E02]: https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/installer/managed/Microsoft.NET.HostModel/Bundle/Bundler.cs
[E03]: https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/native/corehost/hostpolicy/deps_resolver.h
[E04]: https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation
[E05]: https://github.com/github/copilot-sdk/blob/main/docs/setup/bundled-cli.md
[E06]: https://github.com/github/copilot-sdk/blob/main/dotnet/README.md
[E07]: https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli
[E08]: https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli/authenticate-copilot-cli
[E09]: https://github.com/dahatake/StudyReport-Evaluator/releases/tag/v0.8.1
[E10]: https://learn.microsoft.com/windows/curl/
[E11]: https://learn.microsoft.com/windows/tar/
[E12]: https://learn.microsoft.com/windows-server/administration/windows-commands/certutil
[E13]: https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/native/corehost/bundle/extractor.cpp