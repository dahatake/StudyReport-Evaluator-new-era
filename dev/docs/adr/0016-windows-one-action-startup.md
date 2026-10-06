# ADR-0016: Windows単一EXEによる1操作起動

| 項目 | 内容 |
|---|---|
| 状態 | **承認済み（設計決定）。実装進行中・新EXEの公開判定未完了** |
| 決定日 | 2026-09-06 |
| 要求正本 | [要求定義書 v4.5](../../../docs/requirements-definition.md) §3、11.3、12〜14、17〜22 |
| タスク | [承認プラン](../archive/work/20260906-0617-one-action-startup-plan.md) R02。後続承認と[実行上書き](../archive/work/20260906-one-action-startup-execution.md)を優先 |
| 固定構成 | Windows 11 x64／`win-x64`、.NET SDK `10.0.400`／self-contained runtime `10.0.11`、Avalonia `12.1.1`、GitHub Copilot SDK `1.0.11`／native CLI `1.0.79` |
| 製品版 | 現在 `0.8.4` candidate。最後のR03で`0.8.3`からPATCH済み。要求文書版とは別 |
| 改訂 | 2026-10-06: 開発用 MSIX と macOS の基盤は 2026-10-06 に廃止した。matrix v2を固定2行（EXE、ZIP）へ改訂 |

## Context / Limited supersession

既存ZIPはself-containedだが手動展開を要する。追加runtime、terminal、installer、独自launcherを要求せず、取得済みEXEを開く1起動gestureから入力画面へ到達する主導線を追加する。

- [ADR-0013](0013-windows-only-public-release.md) Decision 2と[ADR-0015](0015-windows-macos-installer-delivery.md)のZIPだけをpublic primaryとする決定・対応artifact行・必須の手動hash確認を、本ADRのEXE主導線／ZIP代替／手動hash比較は任意推奨へ**限定的にsupersede**する。新主導線は後続実装と公開gate通過後の契約であり、現時点の公開済み機能とは表示しない。
- Windows-only、unsigned、non-installer、bundled CLI検証、既存ZIP回帰と公開対象外の境界を継承する。業務・入力・採点・数式・checkpoint・privacy・Core/Appの2 production projectを変更しない。
- 旧ADR本文と過去の実測は履歴として保存する（matrix v1のschemaと検証経路は2026-10-06に削除）。旧版のPASSを新EXEへ転記しない。[ADR-0014](0014-product-versioning.md)の製品版正本・release identity・公開済みasset不変の原則も保持する。
- [S01](../preflight/windows-singlefile-feasibility.md)は固定構成の開発hostで`PASS_MECHANISM`、G1 PASS、`PASS_REVIEW`・指摘0。R01も今回の依頼で独立`PASS_REVIEW`・指摘0が引き継がれた。元プランの未承認表記や実行記録のR01未着手表記を、後続承認・レビュー完了より優先しない。

## Decision

### 1. App限定の.NET標準single-file

1. 既存AppだけにWindows専用profileを追加する。予定pathは `src/StudyReportEvaluator.App/Properties/PublishProfiles/WindowsSingleFile.pubxml`。Core、solution全体、通常build／folder／ZIPへ適用しない。既存publish scriptの引数なし動作とZIP生成・代替起動を維持し、single-file出力を分離する。
2. restore／publishに同じ`win-x64`、`SelfContained=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`、`IncludeAllContentForSelfExtract=true`を渡す。trimming、ReadyToRun、圧縮は無効、symbolsは非配布とする。S01で確認したbuild-only依存`Microsoft.NET.ILLink.Tasks 10.0.11`だけをApp側でlockし、Coreの追加lockやproduction projectを増やさず、analyzer／integrity検証を無効化しない。SDK 10.0.401ではpublish時に暗黙のILLink 10.0.12が選ばれ専用lockと不一致になったため（`work/20260916-latest-local-build.md`手順2〜3）、profileのApp限定groupで`RuntimeFrameworkVersion=10.0.11`とnet10.0の`KnownILLinkPack`（`ILLinkPackVersion=10.0.11`）を固定する。通常build・Core・canonical lockには適用しない。
3. `IncludeAllContentForSelfExtract`はMicrosoftが**非推奨**とする.NET Core 3.1互換モードであり、将来削除される可能性がある。要求v4.5 §13.5とS01に記録されたこの注意を受け入れ、S01/G1の固定構成での方式適合を条件として採用する。推奨方式・将来versionの互換保証・clean-host成功とは扱わない。
4. .NET/native依存、固定native CLI、`copilot-runtime.json`、既存ZIPの明示allowlistにあるREADME／利用者docs／画像／LICENSEを**bundle前**に含める。repository全体のglob、sample、利用者input／final／partial、work、tests、secretを含めない。最終配布名のEXEからsidecarを作り、その後bytesを変更しない。
5. 全内容展開後の`AppContext.BaseDirectory`と、manifestから`runtimes/win-x64/native/copilot.exe`への既存相対配置を維持する。既存resolverのmanifest／RID／SDK・CLI版／SHA-256検証を残し、PATH fallbackやhash検証緩和をしない。
6. .NET標準hostによる通常`%TEMP%/.net/<app>/<bundle-id>/`への抽出、cache再利用・欠落復元・並行起動処理を使う。単一ファイル配布はディスク上の1ファイル・痕跡なしを意味しない。標準抽出を全cacheの暗号学的検証とはみなさず、別権限userから書換え可能な抽出先を対応済みとしない。`DOTNET_BUNDLE_EXTRACT_BASE_DIR`は試験隔離用で、利用者の環境変数設定やAPI keyを要求しない。
7. cacheはアプリ配置専用とし、input／final／partialを保存・移動・削除しない。既定の入力隣接`result`、atomic finalization、同じ製品版・runtime identityのEXE／ZIP間のcheckpoint再開条件を維持する。独自cache／locking engine、cache管理UI、自動掃除を追加しない。

### 2. 1操作の意味と既存起動契約

- 将来の主配布名は `StudyReportEvaluator-win-x64.exe`、sidecarは `StudyReportEvaluator-win-x64.exe.sha256`。未公開download URLを作らない。取得済みEXEを開く操作（ダブルクリックは1起動gesture）からofflineの入力画面・Excel読込・mapping・設計へ到達することを要求する。
- download、任意の手動hash比較、OS警告への操作、本人loginはその1操作に含めず、実際の追加操作・警告・拒否を別記する。sidecar公開とCI／公開gateのexact hash比較は必須だが、sidecarを起動依存にしない。同一配布元のhash一致は発行者identityやSmartScreen reputationを保証しない。
- .NET Runtime／SDK、PowerShell、Node.js／npm、Git／`gh`、別Copilot CLI、Office、IDE、隣接DLL／manifest、repository、既存CLI cache／認証をGUI起動の前提にしない。手動展開、setup script、terminal入力、管理者昇格を要求しない。AIには別途network、利用可能account、本人認証と組織policy上の許可が必要である。
- `--input`と複数`--prompt`、起動時cwd基準の相対path、日本語・空白path、Promptの順序・同一pathの取扱い、invalid入力検証・明示適用を維持する。EXE配置先／抽出先へcwdを変えず、独自argv再構成や新しい起動引数を追加しない。起動・Prompt適用でlogin／AI評価を自動開始しない。
- EXE／ZIPはunsignedであり、SmartScreen／Smart App Control／企業policyによる警告・実行拒否を許容範囲と照合する。「誰でも1操作」「すべての端末で無警告」と保証しない。保護無効化、MOTW除去、証明書の自動trust、execution policy変更、UAC回避を実装・案内しない。

### 3. App所有のlogin専用service（D-06採用済み）

1. Executionの「GitHubにログイン」という本人操作だけで、App所有の小さなlogin専用serviceを開始する。認証状態確認serviceとは分離し、汎用process runner／認証providerを追加しない。
2. 既存bundled resolverが検証した絶対CLI pathの`login`を直接子processとして起動し、ブラウザーによる本人認証をCLIへ委譲する。shell、PowerShell、`cmd /c`、任意command文字列を介さない。S01で`1.0.79`の`login --help`と`--web-flow`の実在は確認済みだが、本人login成功ではない。採用option・自己更新抑止は固定版で動作確認したものだけとし、login前後のCLI版／hashを維持する。
3. 認証console／ブラウザーとcredential保管はCLIが担当する。AppはPAT、password、secret、token、device codeを入力・解析・収集・保存・log出力せず、引数／標準入力へ渡さない。独自OAuth、token入力UI、WebView、callback serverを作らない。
4. 二重開始と評価中のlogin開始を防ぐ。取消・失敗後もGUI／Excel読込／mapping／設計を使える状態を保ち、safeな理由と再試行を提示する。
5. 利用者のlogin取消またはアプリ終了時に限り、serviceが開始・所有した**当該login CLI processだけ**を終了・解放する。process tree全体や名前一致でkillせず、ブラウザー、他のCLI、利用者data、credential storeに触れない。logout、credential削除・失効を行わない。
6. 完了・取消・失敗後は利用者が既存の「Copilot 状態を確認」で再確認する。process開始・終了codeだけを認証成功とせず、自動model変更・AI開始をしない。CLI欠落・不一致時は配布物の再取得／展開状態確認を案内し、別CLIのPATH導入で回避しない。

### 4. Matrix v2とcandidate-bound公開gate

matrix v2は次の**固定2行**だけとし、unknown／duplicate／missing rowを拒否する（2026-10-06改訂。旧3行目は廃止）。draft／publicの配布assetはEXE／ZIPと各sidecarの**計4個だけ**とする。

| Row | Artifact / sidecar | publish | Required evidence |
|---|---|---|---|
| Windows x64 single-file | `StudyReportEvaluator-win-x64.exe` / `StudyReportEvaluator-win-x64.exe.sha256` | `true` | package required tests＋exact EXEのCH-01〜06 |
| Windows x64 ZIP | `StudyReportEvaluator-win-x64.zip` / `StudyReportEvaluator-win-x64.zip.sha256` | `true` | 既存package／clean extract／apphost起動／CLI identity／data保護のrequired tests |

1. 順序は**候補生成 → exact EXEのclean-host試験 → protected publishでv2最終matrix確定**。同じsource commit・製品版・SDK／CLI版でEXE／ZIPを作り、candidateのpackage required tests成功後だけ4 assetをdraftへ添付する。未実施のclean-host結果や公開可能matrixをcandidate段階で生成しない。
2. candidate run ID／commit、artifact basename／bytes／SHA-256／製品版、sidecar、package evidence、clean-host evidenceを同じ成果物へ拘束する。doc同梱・最終PATCH等でEXE bytesが変われば旧結果を流用せず、再package・必要な試験とexact EXEのclean-host試験を再実行する。
3. （2026-10-06廃止。旧3行目の非公開package記録はcandidate control artifactから削除した。installer機能やrequired install試験を増やさない。）
4. clean-host担当者が同candidateのEXEで試験し、sanitized metadata限定のclosed JSONを供給する。run ID／commit、EXE名／bytes／SHA-256／製品版、OS edition／build／architecture、標準user・追加依存・保護状態、実build SDK／bundled runtime／Copilot SDK・CLI版／CLI hash、試験ID別結果・操作数、実施記録の参照／hashを含める。username、credential／device code、学生本文、Prompt、環境変数値一覧、生ログは含めず、公開前に製品repositoryへcommitしない。
5. 既存protected publish workflowの`clean_host_evidence_json`入力を環境変数経由で一時file化し、型・長さ・許可field・必須試験IDを検証する。workflow式をshell本文へ直接埋め込まない。人の試験記録であり、hash一致だけで実施事実が自動証明されたとはしない。新storage／workflow／証跡基盤は追加しない。
6. protected publishは指定repositoryの`release.yml`による成功candidate runとtag commitの一致を確認する。EXE／ZIPの4 assetを再downloadし、version／bytes／hash／sidecar、package evidenceを照合する。v2最終matrixと受領JSONを内部control artifactへ保存する。これらをpublic assetに含めない。
7. 既存Core／App／package required testsと必須CHすべてのPASSを公開条件とする。欠落・FAIL・NOT_RUN、別candidate／source／version／hash、sidecar不一致を拒否する。unsignedの`PASS_REQUIRED`を署名／installer等の`PASS_PRODUCTION`へ読み替えない。tag／push／draft／public Release操作は実装承認と別承認とし、public化にはprotected environmentの公開承認を要求する。

## Validation boundary

要求v4.5 §13.7／TR-30〜33を後続実装・試験の正本とする。fresh Windows 11 x64実機またはVMの標準userで、最終candidateと一致するEXEを使用する。開発hostのPATH／.NET環境変数隔離、hosted CI、fake、CLI helpをOS-only／本人認証の証拠にしない。guestへ検証用SDK／PowerShellを入れてからOS-onlyとは呼ばない。

| ID | 公開必須の観測／判定 |
|---|---|
| CH-01 | fresh OS edition／build／x64・標準user、追加.NET SDK／Runtime、PowerShell 6+、Node／npm、Git／gh、別CLI、Office、IDEの未導入。OS付属.NET Framework等と区別 |
| CH-02 | sidecar／隣接file／repository／既存CLI cache・認証なしのEXE1個でoffline GUI・Excel読込・設計。保護機能による拒否を成功としない |
| CH-03 | 外部PowerShell／Node／Git／gh／CLIなしの同梱CLI Start／Ping／auth状態確認。正常な未認証応答とruntime failureを区別 |
| CH-04 | 移動・再起動・同時起動、args／任意cwd、日本語・空白path、read-only EXE配置先、cache欠落復元、data保護 |
| CH-05 | 標準ブラウザー取得のMOTW付きEXE、SmartScreen／SAC／企業policy、警告・拒否と実操作数を承認範囲と比較。追加操作を1操作へ丸めない |
| CH-06 | fresh user本人login、完了後・再起動後の既存buttonでの再確認、取消／app終了時の所有login processだけの終了、他process／data／credential保護。D-06採用済みのため必須PASS、JSONで任意化・N/A化しない |

CH-01〜06はすべて必須PASS。実AI評価`ADV-01`は本人の明示承認とsynthetic入力で行う任意試験、Office等の外部再計算`ADV-02`も任意とし、両者は`NOT_RUN`可。必須GUI／CLI／login試験を代替しない。EXE size、展開容量、初回／再起動時間は実測を記録し、数値SLAを追加しない。

**現時点でclean-host、MOTW／Windows保護、本人loginは`NOT_RUN`。本ADRはそれらの成功を主張しない。** S01は機構適合だけであり、新EXEの公開資格を与えない。

## Approval record / Version override

[実行記録](../archive/work/20260906-one-action-startup-execution.md)にある2026-09-06の承認・上書き文言を採録する。

> 2026-09-06、要求所有者が `20260906-0617-one-action-startup-plan.md` のデフォルト案と全タスク実行を承認。

> 全タスクで、実施・直接検証 → 敵対的レビュー → 根拠のある指摘の反映・再検証 → 次の依存タスク、の順序を守る。

> **最新指示による変更:** D-14/R03のMINOR更新を取り消し、全タスク完了後にPATCHを1つ増やす。開始版は `0.8.3`、予定版は `0.8.4`。版とCHANGELOGの更新は最後に行う。

デフォルト案・全タスク実行・各タスク後の敵対的レビューを採用済みとし、D-06も採用する。提案時のD-14／R03のMINOR候補`0.9.0`は最新の明示PATCH指示で取り消した。開発中は[製品版正本](../../../Directory.Build.props)の`0.8.3`を維持し、実装・文書task完了後のR03で`0.8.4`へPATCHした。同時に既存Unreleased項目を保持し、Keep a Changelog形式で概要・実装済み変更を追加した。R03後のexact EXE／ZIPはまだ最終再検証前であり、版変更だけをpackage／clean-host／公開成功へ読み替えない。

承認はrepositoryの設計決定であり、実装完了、R02の独立レビュー合格、試験成功、release存在、tag／push／公開操作、組織の法務・教育・security承認や電子署名を意味しない。

## Consequences / Stop conditions

- 手動展開を主導線から除き、既存2-projectとCLI integrityを維持できる一方、非推奨互換モード、抽出容量・残存cache、unsignedの警告／拒否を受け入れる。ZIPは変更しない代替経路として残す。
- 新installer、custom launcher／自己展開engine、online bootstrap、自動／差分更新、cache cleaner、registry／PATH恒久変更、file association、常駐service、他OS対応、SDK／CLI／Avalonia更新、Native AOT／trimming全面適用は追加しない。
- 標準方式で成立せず追加runtime同梱・独自launcher・resolver緩和等が必要なら停止し、実測と最小差分を示して再承認を求める。clean-host証跡が用意できなければ`NOT_RUN`のまま新経路の公開を止め、成功を補完しない。

**APPROVED — V4.5 SCOPE.** 条件付き標準single-file主配布・既存ZIP代替・App所有login・candidate拘束公開gateを採用する。旧ADRを改変せず、未実測のclean-host／本人認証／署名／installer claimを追加しない。