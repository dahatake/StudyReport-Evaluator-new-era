# v1.0.0 残タスク実行計画

## 0. 文書管理

| 項目 | 値 |
|---|---|
| 対象repository | `dahatake/StudyReport-Evaluator` |
| 計画基準時刻 | 2026-09-03T06:06:42.8889836+09:00 |
| release baseline | `b68e7577df616c0e259b55697a52d26412a01208` |
| release tag | `v1.0.0` |
| 計画目的 | 別のWindows環境から、未完了のCI確認、immutable GitHub Release作成、公開asset再検証、文書closure、最終CIまでをfail-closedで完了する |
| 捏造防止 | 未観測値は`TBD（実測必須）`とし、推測値で置換しない |

基準時刻はPowerShell 7で取得したlocal/UTC時刻と、同時に取得したGitHub Actions run状態に基づく。[S02] この文書に記載する既知値は下記「出典台帳」へ追跡し、実行時に変化し得る値は必ず再取得する。

## 1. 出典台帳

### 1.1 現在状態の一次証跡

- **[S01] Local Git / strict gate snapshot** — 2026-09-03 06:06 JSTにPowerShell 7で`git fetch origin --prune --tags`、`git status --porcelain=v1 --untracked-files=all`、`git rev-parse HEAD`、`git rev-parse origin/main`、`git cat-file -t v1.0.0`、`git rev-list -n 1 v1.0.0`、`git ls-files -- sample`を実行。観測値は、local/remote main=`b68e7577df616c0e259b55697a52d26412a01208`、status 0件、tag type=`tag`、tag commit=`b68e7577df616c0e259b55697a52d26412a01208`、tracked sample 0件。併せてlocal external summaryからrequired real-data technical E2E=`PASS`、regression=`670/670`を読み取った。
- **[S02] Timed CI snapshot** — 2026-09-03T06:06:42.8889836+09:00に`gh run view 33682636186 --repo dahatake/StudyReport-Evaluator --json status,conclusion,headSha,url`を実行。`status=in_progress`、`conclusion`未確定、`headSha=b68e7577df616c0e259b55697a52d26412a01208`を観測。
- **[S03] Local strict E2E summary** — `C:\Temp\StudyReportEvaluator-E2E-single-sample-20260903-c84fe2ab6d4f4e34a3be948c5c1f9ed2\summary.json`。required scopes、670/670、privacy/provenance、source/input不変を記録したmachine-local証跡。別環境には存在すると仮定しない。
- **[S04] GitHub Actions run API** — [CI run 33682636186](https://api.github.com/repos/dahatake/StudyReport-Evaluator/actions/runs/33682636186)。基準時点で`in_progress`、head SHAはrelease baseline。
- **[S05] GitHub Actions jobs API** — [CI run 33682636186 jobs](https://api.github.com/repos/dahatake/StudyReport-Evaluator/actions/runs/33682636186/jobs)。基準時点でsetup、privacy、locked restore、version、Release buildは成功し、`Run deterministic tests`が実行中。
- **[S06] Git tag ref API** — [refs/tags/v1.0.0](https://api.github.com/repos/dahatake/StudyReport-Evaluator/git/ref/tags/v1.0.0)。tag object SHA=`dd687611f46dbdd1e006a60f4ee1582f77151cd7`、object type=`tag`。
- **[S07] Annotated tag object API** — [tag object dd687611](https://api.github.com/repos/dahatake/StudyReport-Evaluator/git/tags/dd687611f46dbdd1e006a60f4ee1582f77151cd7)。tag=`v1.0.0`、message=`StudyReport Evaluator 1.0.0`、target type=`commit`、target SHA=`b68e7577df616c0e259b55697a52d26412a01208`。
- **[S08] GitHub Releases API** — [repository releases](https://api.github.com/repos/dahatake/StudyReport-Evaluator/releases?per_page=10)。基準時点の応答は空配列で、公開Releaseは0件。
- **[S09] Release commit** — [b68e7577](https://github.com/dahatake/StudyReport-Evaluator/commit/b68e7577df616c0e259b55697a52d26412a01208)。commit messageは`Prepare v1.0.0 release`。

### 1.2 Repository正本

- **[S10] CI workflow** — [`.github/workflows/ci.yml`](../../../../.github/workflows/ci.yml)。`main` pushを契機にPowerShell/privacy、locked restore、version、Release build、sample非依存test、integrity、TRX uploadを実行する。
- **[S11] Release workflow** — [`.github/workflows/release.yml`](../../../../.github/workflows/release.yml)。manual dispatchの`tag`/`draft`、annotated tag・clean checkout・sample非追跡、locked restore/build/test、package、hash、artifact、GitHub Release作成を定義する。
- **[S12] Version / changelog正本** — [`Directory.Build.props`](../../../../Directory.Build.props)と[`CHANGELOG.md`](../../../../CHANGELOG.md)。製品版は`1.0.0`、changelogは空の`[Unreleased]`と`[1.0.0] - 2026-09-03`を持つ。
- **[S13] 利用者download導線** — [`README.md`](../../../../README.md)。`v1.0.0` ZIPとSHA-256 sidecarの最終URL、およびhash確認手順を記載済み。
- **[S14] 版管理・公開規約** — [`dev/docs/version-management.md`](../../../../dev/docs/version-management.md)。remote tag後のfail-closed、新版による修正、tag/asset非差替え、version/tag/package/Release完了条件を定義する。
- **[S15] Publish / package正本** — [`scripts/publish-windows.ps1`](../../../../scripts/publish-windows.ps1)と[`scripts/package-windows.ps1`](../../../../scripts/package-windows.ps1)。Windows 11 x64、PowerShell Core 7+、self-contained、safe layout、bundled CLI、cold-start probe、deterministic ZIP、sidecarを検査する。
- **[S16] Direct tests** — [`WindowsPublishPackageTests.cs`](../../../../tests/StudyReportEvaluator.App.Tests/Packaging/WindowsPublishPackageTests.cs)と[`DocumentationContractTests.cs`](../../../../tests/StudyReportEvaluator.App.Tests/Content/DocumentationContractTests.cs)。package/extract/bundled runtime/cold launch/repackage、および14件の文書契約を定義する。
- **[S17] Current implementation status** — [`dev/docs/implementation-status.md`](../../../../dev/docs/implementation-status.md)。required 670/670、canonical technical E2E、package gate等を記録する一方、public release statusは`IMPLEMENTATION_IN_PROGRESS`、B-05は未完了としている。
- **[S18] README claim ledger** — [`dev/docs/readme-claim-ledger.md`](../../../../dev/docs/readme-claim-ledger.md)。C-026「正式release assetを実在URLからdownloadできる」は基準時点で`BLOCKED`。
- **[S19] Traceability** — [`dev/docs/traceability.md`](../../../../dev/docs/traceability.md)。current statusは`IMPLEMENTATION_IN_PROGRESS`、required AC/TRはpassing evidenceへ接続済み。
- **[S20] 直前のrelease計画** — [`work/20260902-readme-end-user-release-plan.md`](20260902-readme-end-user-release-plan.md)。B-05、remote release workflow、公開download再検証、文書closureを未完了としている。
- **[S21] Toolchain pin** — [`global.json`](../../../../global.json)と[`Directory.Build.props`](../../../../Directory.Build.props)。.NET SDK 10.0.400 feature band、`net10.0`、C# 14、warnings as errors、locked restoreを要求する。
- **[S22] Canonical sample / E2E契約** — [`docs/requirements-definition.md`](../../../../docs/requirements-definition.md)、[`SampleWorkbookStructuralTests.cs`](../../../../tests/StudyReportEvaluator.App.Tests/E2E/SampleWorkbookStructuralTests.cs)、[`RealDataSystemSmokeTests.cs`](../../../../tests/StudyReportEvaluator.App.Tests/E2E/RealDataSystemSmokeTests.cs)。private canonical sampleはexact path/size/hashで扱い、fallbackせず、追跡・package化しない。

### 1.3 外部仕様

- **[S23] GitHub CLI workflow/run manual** — [`gh workflow run`](https://cli.github.com/manual/gh_workflow_run)、[`gh run list`](https://cli.github.com/manual/gh_run_list)、[`gh run view`](https://cli.github.com/manual/gh_run_view)、[`gh run watch`](https://cli.github.com/manual/gh_run_watch)。dispatch入力、commit/workflow/event filter、JSON fields、`--exit-status`の現行仕様。
- **[S24] GitHub CLI release manual** — [`gh release create`](https://cli.github.com/manual/gh_release_create)、[`gh release edit`](https://cli.github.com/manual/gh_release_edit)、[`gh release view`](https://cli.github.com/manual/gh_release_view)、[`gh release download`](https://cli.github.com/manual/gh_release_download)。既存tag検証、asset upload、draft、publish、downloadの現行仕様。`gh release create`へassetを渡す場合、CLIは内部でdraft作成→asset upload→publishのAPI callを行う。
- **[S25] GitHub immutable releases** — [概要](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases)、[有効化手順](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/establish-provenance-and-integrity/prevent-release-changes)、[release/asset integrity検証](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/verify-release-integrity)。immutabilityは将来公開するReleaseにだけ適用され、公開後のtag移動・asset変更を禁止し、`gh release verify`/`gh release verify-asset`で検証できる。

## 2. 基準時点の確定状態

### 2.1 完了済み — 再実行しない項目

| 項目 | 状態 | 根拠 |
|---|---|---|
| release commit作成・remote main push | DONE | local/remote mainはいずれも`b68e7577...`。[S01][S09] |
| annotated tag作成・push | DONE | remote refはtag objectで、targetはrelease commit。[S06][S07] |
| version/changelog確定 | DONE | version=`1.0.0`、dated changelog sectionあり。[S12] |
| local required gate | DONE | machine-local strict summaryでrequired E2E=`PASS`、regression 670/670。別環境へartifact存在を仮定しない。[S01][S03] |
| private sample非追跡 | DONE | tracked sample 0件。workflowも同境界を再検査する。[S01][S10][S11][S22] |
| README最終URL記載 | DONE_CONTENT | URL文字列は記載済みだが、Release未作成のため実在確認は未完了。[S08][S13][S18] |

release baselineまたはtagを変更した場合、上記DONEは無効になる。ただしremote annotated tagは既に共有済みなので、同名tagを移動せず、新しいSemVerでやり直す。[S07][S14][S25]

### 2.2 残タスク一覧

| ID | 残タスク | 基準状態 | 依存 | 完了証跡 |
|---|---|---|---|---|
| R-01 | 実行中CIの完了確認 | IN_PROGRESS | なし | run 33682636186がrelease commitで`completed/success`、全job/step成功。[S02][S04][S05] |
| R-02 | Release immutability有効化確認 | UNVERIFIED | Release公開前 | repository Settingsで将来Releaseへのimmutabilityが有効。公開後`gh release verify`成功。[S25] |
| R-03 | dispatch前idempotency/identity gate | NOT_RUN | R-01、R-02 | Release不存在、tag/commit一致、同一release runの重複なし。[S07][S08][S11] |
| R-04 | release workflow dispatch | NOT_RUN | R-03 | `tag=v1.0.0`、`draft=false`のrun IDを一意記録。[S11][S23][S24] |
| R-05 | release workflow完了確認 | NOT_RUN | R-04 | checkout/tag/privacy/build/test/package/hash/upload/release stepがすべて成功。[S11] |
| R-06 | 公開Release metadata/attestation検証 | NOT_RUN | R-05 | stable公開、tag一致、asset 2件、immutable release verification成功。[S24][S25] |
| R-07 | 公開asset再download・deep verification | NOT_RUN | R-06 | unauthenticated download、size/hash、safe single-root、version、bundled CLI、cold launchがPASS。[S13][S15][S16] |
| R-08 | B-05/C-026とstatus文書closure | NOT_RUN | R-07 | C-026=`VERIFIED`、status=`RELEASED`、実測URL/hash/run IDのみ記録。[S17][S18][S19][S20] |
| R-09 | closure差分のlocal検証 | NOT_RUN | R-08 | documentation 14/14、version、diff、link、secret/sample gateがPASS。[S10][S14][S16] |
| R-10 | closure commit/push | NOT_RUN | R-09 | docs-only closure commitをmainへpush。`v1.0.0` tag/assetは不変。[S14][S25] |
| R-11 | closure commitの最終CI | NOT_RUN | R-10 | closure SHAに対するCIが`completed/success`。[S10][S23] |
| R-12 | 最終remote整合・証跡整理・一時物cleanup | NOT_RUN | R-11 | main/tag/Release/asset/docsが整合し、worktree clean。機微情報を含まない実測値だけ残す。[S14][S18][S20][S22] |

**現時点で未知の値:** release workflow run ID、GitHub Release ID、published-at、公開ZIP bytes/SHA-256、公開CLI hash、closure commit SHA、final CI run ID。これらはすべて`TBD（実測必須）`であり、local pre-release package値を転記してはいけない。[S08][S11][S15]

## 3. 別環境での前提と共通guardrail

### 3.1 必須環境

1. packageの展開・cold launchを行う実行者はWindows 11 x64を使用する。metadata/Actions監視だけなら他OSでも可能だが、Windows package acceptanceの代替にはならない。[S15][S16]
2. PowerShellは`pwsh.exe`のCore 7以上を使用し、Windows PowerShell 5.1へfallbackしない。[S15]
3. `.NET SDK 10.0.400` compatible feature band、Git、GitHub CLIを用意する。[S21][S23][S24]
4. GitHub CLIには対象repositoryのworkflow dispatch、Actions read、Release/contents writeに必要な権限でloginする。credential値はlogや計画書へ記録しない。[S11][S23][S24]
5. GitHub、NuGet、npmへのnetwork accessを確保する。package scriptはpinned Copilot CLIを取得し検証する。[S15]
6. この計画書は基準時点ではrelease tag後に新規作成されたlocal fileである。別環境へは安全な経路でコピーし、post-release closure commitへ含める。`v1.0.0` tagへ後付けしない。[S01][S07]

### 3.2 初期化

以下の変数は既知のremote identityであり、変更しない。[S01][S07][S09]

```powershell
$Repository = 'dahatake/StudyReport-Evaluator'
$ReleaseCommit = 'b68e7577df616c0e259b55697a52d26412a01208'
$Tag = 'v1.0.0'
$InitialCiRunId = 33682636186
```

新規cloneを使い、既存dirty workspaceを流用しない。計画書をclone外からコピーした場合、許容する未追跡fileは`work/20260903-0605-TaskExecutionPlan.md`だけとする。

```powershell
git clone https://github.com/dahatake/StudyReport-Evaluator.git
Set-Location .\StudyReport-Evaluator
git fetch origin --prune --tags
git switch main
git pull --ff-only origin main
pwsh.exe -NoLogo -NoProfile -Command '$v=$PSVersionTable; if($v.PSEdition -cne "Core" -or $v.PSVersion.Major -lt 7){throw "PowerShell 7+ Core is required"}'
dotnet --version
gh auth status
```

`dotnet --version`は`global.json`の10.0.400/latestPatch契約に適合させる。資格情報、token、学生data、workbook cell本文をoutputへ出さない。[S21][S22]

### 3.3 全task共通のfail-closed規則

- 同じworkflowを「runが見えない」という理由だけで再dispatchしない。最初にrun list/APIを再読する。[S23]
- remote `v1.0.0`を削除・移動・再作成しない。[S14][S25]
- 公開後のassetを置換・削除しない。不具合修正は新しいSemVerで行う。[S14][S25]
- `/sample/`、local E2E artifact、TRX全文、credentialをstage/uploadしない。[S10][S11][S22]
- optional live AI/Excel再計算をrequired gateへ合算しない。[S17][S19]
- command失敗時は次phaseへ進まず、観測したexit code、run URL、failed step名だけを記録する。機微なpayload/log全文は記録しない。

## 4. 詳細実装手順

## R-01 — 実行中CIを確定する

基準時点ではrun `33682636186`のdeterministic testが実行中であり、successはまだ観測されていない。[S02][S04][S05]

1. commit SHAでrunを再取得する。[S23]

```powershell
gh run list --repo $Repository --workflow ci.yml --branch main --event push --commit $ReleaseCommit --limit 10 --json databaseId,headSha,status,conclusion,createdAt,url
```

2. runが`in_progress`/`queued`なら、同じrun IDを監視する。新規CIをdispatchしない。[S23]

```powershell
gh run watch $InitialCiRunId --repo $Repository --compact --exit-status --interval 20
```

3. 完了後、JSONとjob detailsを再取得し、`headSha==$ReleaseCommit`、`status==completed`、`conclusion==success`、required stepがすべてsuccessであることをassertする。[S10][S23]

```powershell
gh run view $InitialCiRunId --repo $Repository --json status,conclusion,headSha,jobs,url --exit-status
```

4. failure時は`--log-failed`だけを一時directoryへ保存して原因を切り分ける。full raw logをrepositoryへ追加しない。
5. code/config defectなら、remote tagを移動せず、修正後に新しいSemVerを計画する。transient infrastructure failureでも、Release不存在を確認するまでrelease workflowへ進まない。[S14][S25]

**Exit gate:** run `33682636186`がrelease commitに対して`completed/success`。それ以外はBLOCKED。

## R-02 — Release immutabilityを公開前に確認する

基準時点ではimmutable settingの有効化証跡がないため、`UNVERIFIED`である。immutabilityは有効化後に公開される将来Releaseへだけ適用される。[S25]

1. repository管理権限を持つ利用者がGitHubのrepository **Settings**を開く。
2. **Releases** sectionで**Enable release immutability**が有効か確認し、無効なら有効化する。[S25]
3. 有効化の確認時刻と確認者を、機微情報を含まないexecution noteへ記録する。
4. 権限不足または設定不明の場合はBLOCKED。Releaseをdispatchしない。
5. 公開後のR-06で`gh release verify $Tag`を実行し、実際にimmutable releaseとして検証する。[S25]

**Exit gate:** future release immutability enabledを公開前に確認済み。

## R-03 — dispatch前のidentity/idempotency gate

1. remote main、tag object、tag target、local statusを再確認する。[S07][S11]

```powershell
git fetch origin --prune --tags
git rev-parse origin/main
git cat-file -t $Tag
git rev-list -n 1 $Tag
git status --short --untracked-files=all
```

`origin/main`とtag targetは`$ReleaseCommit`、tag typeは`tag`でなければならない。local statusはclean、または持込済みの本計画書1件だけを許容する。

2. Releaseが存在しないことを確認する。存在した場合はdispatchせずR-06へ移り、正規runで作られたものか調査する。[S08][S24]

```powershell
gh release view $Tag --repo $Repository --json tagName,isDraft,isPrerelease,url,assets
```

3. 既存release workflow runを検索する。[S23]

```powershell
gh run list --repo $Repository --workflow release.yml --event workflow_dispatch --commit $ReleaseCommit --limit 20 --json databaseId,headSha,status,conclusion,createdAt,url
```

既存runが1件あればそれを採用し、重複dispatchしない。複数ある場合は停止し、各runとReleaseの関係を確定する。

**Exit gate:** CI success、immutability enabled、Releaseなし、tag identity一致、採用すべき既存release runなし。

## R-04 — release workflowを一度だけdispatchする

workflow inputsは`tag`と`draft`である。[S11] `draft=false`を使用する。GitHub CLIはasset付きrelease作成時に内部でdraft作成→upload→publishを行うため、immutability有効時もassetを公開前に揃える。[S24][S25]

```powershell
gh workflow run release.yml --repo $Repository --ref main --field "tag=$Tag" --field 'draft=false'
```

dispatch直後にcommit/event/workflowでrunを一意化し、`RELEASE_RUN_ID`とURLを実測値として記録する。[S23]

```powershell
gh run list --repo $Repository --workflow release.yml --event workflow_dispatch --commit $ReleaseCommit --limit 10 --json databaseId,headSha,status,conclusion,createdAt,url
```

runが即時に見えなくても再dispatchしない。Actions APIを再読して同じrunを発見する。

**Exit gate:** 一意の`RELEASE_RUN_ID=TBD（実測必須）`を記録済み。

## R-05 — release workflowを完了させる

```powershell
gh run watch $ReleaseRunId --repo $Repository --compact --exit-status --interval 20
gh run view $ReleaseRunId --repo $Repository --json status,conclusion,headSha,jobs,url --exit-status
```

次のworkflow-owned stepがすべてsuccessであることをjob JSONから確認する。[S11]

1. tagged source checkout
2. annotated tag / HEAD / clean tree / sample非追跡 / version検証
3. locked restore
4. Release build
5. sample非依存deterministic tests
6. final Windows package
7. published binary version
8. package hash
9. workflow package/TRX artifact upload
10. GitHub Release create

失敗時は最初に`gh release view $Tag`を再実行する。Releaseが作成済みなら、同名release/assetを変更せず、新版方針へ切り替える。Release未作成ならfailed stepを修正し、tag不変で再実行可能かを[S14]に従い判断する。

**Exit gate:** release workflowが`completed/success`、Release create step成功。

## R-06 — 公開metadataとimmutabilityを検証する

1. Release metadataを取得する。[S24]

```powershell
gh release view $Tag --repo $Repository --json tagName,isDraft,isPrerelease,publishedAt,url,assets
```

assertion:

- `tagName == v1.0.0`
- `isDraft == false`
- `isPrerelease == false`
- `publishedAt`がnon-null
- asset名が`StudyReportEvaluator-win-x64.zip`と`StudyReportEvaluator-win-x64.zip.sha256`の2件
- 両assetのsizeが0より大きい

2. 公開REST APIを認証情報なしでも取得し、同じ結果を確認する。[S08]

```text
https://api.github.com/repos/dahatake/StudyReport-Evaluator/releases/tags/v1.0.0
```

3. immutable release attestationを検証する。[S25]

```powershell
gh release verify $Tag --repo $Repository
```

`gh release verify`が失敗した場合、Releaseを変更せずBLOCKEDとして原因を調べる。immutabilityは公開後にretroactive適用できると仮定しない。[S25]

**Exit gate:** stable/public metadata、exact asset set、immutable verificationがPASS。

## R-07 — 公開assetをfresh downloadしてdeep verificationする

### R-07.1 認証なしdownload

fresh temporary directoryを作り、README記載のbrowser URLから直接取得する。[S13]

```powershell
$DownloadRoot = Join-Path $env:TEMP ('StudyReportEvaluator-v1.0.0-public-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $DownloadRoot | Out-Null
$Zip = Join-Path $DownloadRoot 'StudyReportEvaluator-win-x64.zip'
$Sidecar = Join-Path $DownloadRoot 'StudyReportEvaluator-win-x64.zip.sha256'
Invoke-WebRequest -Uri 'https://github.com/dahatake/StudyReport-Evaluator/releases/download/v1.0.0/StudyReportEvaluator-win-x64.zip' -OutFile $Zip
Invoke-WebRequest -Uri 'https://github.com/dahatake/StudyReport-Evaluator/releases/download/v1.0.0/StudyReportEvaluator-win-x64.zip.sha256' -OutFile $Sidecar
```

Authorization headerを付けない。HTTP failure、redirect failure、0-byteは即時失敗とする。

### R-07.2 sidecar / attestation

sidecarは`<64桁大文字SHA-256><2 spaces>StudyReportEvaluator-win-x64.zip<LF>`の完全一致を要求する。[S15][S16]

```powershell
$ActualHash = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash
$ExpectedLine = "$ActualHash  StudyReportEvaluator-win-x64.zip`n"
$ActualLine = [IO.File]::ReadAllText($Sidecar, [Text.UTF8Encoding]::new($false, $true))
if ($ActualLine -cne $ExpectedLine) { throw 'Public ZIP sidecar mismatch.' }
gh release verify-asset $Tag $Zip --repo $Repository
gh release verify-asset $Tag $Sidecar --repo $Repository
```

この時点で`PUBLIC_ZIP_BYTES`と`PUBLIC_ZIP_SHA256`を実測し、後続文書へ同じ値だけを転記する。

### R-07.3 ZIPを展開前に検査

`System.IO.Compression.ZipArchive`でentryを列挙し、展開前に以下をassertする。[S15][S16]

- 全entryが`StudyReportEvaluator-win-x64/`配下
- top-level rootが1件
- absolute path、`..`、drive colon、backslash、symlink/reparse相当entryなし
- ordinal order、case-insensitive重複なし
- fileはnonzero
- `sample/`、`tests/`、`src/`、`artifacts/`、`.git/`、secret markerなし
- `.cs`、`.csproj`、`.ps1`、`.pdb`、`.xlsx`、`.csv`、key/certificate fileなし
- App/Core、runtime、Open XML、Avalonia、GitHub Copilot SDK、`copilot-runtime.json`、nested CLI、README/LICENSE/docs/images/release notesが存在

検査前に`Expand-Archive`しない。検査成功後だけfresh directoryへ展開する。

### R-07.4 version / bundled runtime / cold launch

1. 展開rootをversion toolへ渡す。[S14][S15]

```powershell
pwsh.exe -NoLogo -NoProfile -File .\dev\version.ps1 verify -PublishedDirectory $ExtractedPackageRoot
```

2. `StudyReportEvaluator.App.dll`とCore DLLのAssembly/File versionが`1.0.0.0`、ProductVersionが`1.0.0+$ReleaseCommit`であることを確認する。[S14]
3. `copilot-runtime.json`のschema/RID/relative path/version/hashを閉じたproperty setとして検査し、nested `runtimes/win-x64/native/copilot.exe`のSHA-256、file version、AMD64 PEと一致させる。[S15][S16]
4. `DOTNET_ROOT`/`DOTNET_ROOT_X64`を存在しない展開内path、`DOTNET_MULTILEVEL_LOOKUP=0`にしてAppを起動する。1.5秒以内に異常終了しないこと、loaded `coreclr.dll`が展開root由来であること、Office/Excel/LibreOffice moduleがないことを確認し、window closeまたはprocess-tree killでcleanupする。[S15][S16]
5. 展開root由来processが0件になったことを確認する。
6. READMEとdocsのlocal relative linksを検査する。[S16]

**Exit gate:** public download、hash、attestation、safe layout、single root、version、bundled CLI、self-contained cold launch、cleanup、linksがすべてPASS。

## R-08 — 文書closureを実装する

R-07で取得した実測値だけを使う。`RELEASE_RUN_ID`、Release URL/ID、published-at、ZIP bytes/hashを推測しない。

### 必須変更

1. [`dev/docs/readme-claim-ledger.md`](../../../../dev/docs/readme-claim-ledger.md)
   - C-026を`BLOCKED`から`VERIFIED`へ変更。
   - public Release URL、asset URL、公開ZIPの実測bytes/SHA-256、R-07結果、確認日を記録。[S18]
2. [`dev/docs/implementation-status.md`](../../../../dev/docs/implementation-status.md)
   - `Public release status`を`RELEASED — v1.0.0`へ変更。
   - source baselineをannotated tag targetへ固定。
   - B-05をclosedとし、CI/release run URL、public hash、cold launch結果を記録。[S17]
3. [`dev/docs/traceability.md`](../../../../dev/docs/traceability.md)
   - current statusをreleasedへ変更。
   - current validationへpublic immutable Release/asset verificationを追加。[S19]
4. [`work/20260902-readme-end-user-release-plan.md`](20260902-readme-end-user-release-plan.md)
   - B-05、RD-06、RD-08、acceptance checklistを実測証跡でclose。[S20]
5. 本書
   - R-01〜R-12の実行結果と実測値を反映する場合、未実行項目を完了扱いにしない。

### 条件付き変更

- [`README.md`](../../../../README.md)のURLが実在asset URLと完全一致するため、通常は変更不要。相違が実測された場合だけ修正する。[S13]
- `CHANGELOG.md`の`[1.0.0]`内容は公開tagの一部なので変更しない。追加の利用者影響が必要なら`[Unreleased]`へ記録し、公開済みsection/assetを変更しない。[S12][S14]
- `dev/docs/version-management.md`の「2026-09-02時点0件」は日付付きhistorical observationなので、事実を消さない。[S14]

`work/`の既存fileを更新する実行環境では、handoff規約に従い全文を読み、delete→createで再作成し、直後にdiffを確認する。

**Exit gate:** public releaseを未公開・BLOCKEDとするcurrent記述がなく、historical記述は日付付きで保持され、全値がR-06/R-07の実測と一致。

## R-09 — closure差分を検証する

1. version正本が変わっていないことを確認する。[S12][S14]

```powershell
pwsh.exe -NoLogo -NoProfile -File .\dev\version.ps1 verify
```

2. locked restore後、文書契約14件を実行する。[S16][S21]

```powershell
dotnet restore .\tests\StudyReportEvaluator.App.Tests\StudyReportEvaluator.App.Tests.csproj --locked-mode
dotnet test .\tests\StudyReportEvaluator.App.Tests\StudyReportEvaluator.App.Tests.csproj --configuration Release --no-restore --filter 'FullyQualifiedName~StudyReportEvaluator.App.Tests.Content.DocumentationContractTests' --logger 'console;verbosity=minimal'
```

3. diff/integrity/privacyを検査する。[S10][S11][S22]

```powershell
git diff --check
git status --short --untracked-files=all
git ls-files -- sample
git diff --stat
git diff --name-only
```

4. expected file以外の変更、0-byte file、private sample、package/TestResults/bin/obj、credential patternがないことを確認する。
5. external/public URLはHTTP 200とasset名を再確認する。broken local linksはdocumentation contractで0件を要求する。[S13][S16]

**Exit gate:** 14/14 PASS、version PASS、diff/privacy checks PASS、変更pathがclosure scopeだけ。

## R-10 — closure commitをmainへpushする

候補pathは次だけである。READMEを実測上変更した場合だけ追加する。

```text
dev/docs/implementation-status.md
dev/docs/readme-claim-ledger.md
dev/docs/traceability.md
work/20260902-readme-end-user-release-plan.md
work/20260903-0605-TaskExecutionPlan.md
```

1. `git fetch origin --prune --tags`後、remote mainに予期しないcommitがあればmergeせず停止して再評価する。
2. 上記pathだけを明示stageする。
3. `git diff --cached --check`、`git diff --cached --stat`、`git diff --cached --name-only`を確認する。
4. commit例: `Document v1.0.0 public release`。
5. mainだけをpushする。`v1.0.0` tagをpushし直さない。[S14][S25]

```powershell
git push origin main
```

**Exit gate:** `CLOSURE_COMMIT_SHA=TBD（実測必須）`がorigin/mainへ反映され、remote tag targetは引き続き`$ReleaseCommit`。

## R-11 — closure commitの最終CIを確認する

closure SHAでpush event runを一意化する。[S10][S23]

```powershell
gh run list --repo $Repository --workflow ci.yml --branch main --event push --commit $ClosureCommit --limit 10 --json databaseId,headSha,status,conclusion,createdAt,url
gh run watch $FinalCiRunId --repo $Repository --compact --exit-status --interval 20
gh run view $FinalCiRunId --repo $Repository --json status,conclusion,headSha,jobs,url --exit-status
```

CI failure時はRelease/tag/assetを変更せず、mainへ追加のdocs fix commitを作り、そのSHAのCIを再確認する。[S14][S25]

**Exit gate:** `FINAL_CI_RUN_ID=TBD（実測必須）`がclosure commitに対して`completed/success`。

## R-12 — 最終remote整合とcleanup

1. GitHub Release API、tag API、main SHA、final CIをfreshに再取得する。[S04][S06][S07][S08][S23]
2. 次をassertする。
   - origin/main=`$ClosureCommit`
   - `v1.0.0` annotated tag target=`$ReleaseCommit`
   - Release stable/public/immutable
   - asset 2件のID/bytes/hashがR-06/R-07記録と一致
   - README URLがpublic downloadへ到達
   - C-026/status/traceability/旧計画がclosed
   - worktree clean、tracked sample 0
3. public verification summaryへ、次の実測値だけを保存する。回答本文、Prompt、worksheet名、private path、credentialは保存しない。[S22]
4. 必要ならagent-local `/memories/repo/preflight-status.md`を更新する。memory機構がない別環境ではrepositoryのclosure文書を正本とする。
5. public verification値を文書へ反映・commit済みであることを確認した後だけ、一時download/extraction/logを削除する。private canonical sampleは削除・移動しない。
6. immutable Release、tag、assetへの変更操作は実行しない。[S25]

**Exit gate:** product Release、remote refs、main closure、最終CI、repository docs、local cleanlinessがすべて整合。

## 5. 分岐・rollback方針

| 事象 | 必須対応 | 禁止事項 | 根拠 |
|---|---|---|---|
| initial CI failure | Releaseを作らずfailed stepを診断。code defectは新版へ移行 | remote `v1.0.0` tag移動 | [S14][S25] |
| immutability未設定/確認不能 | 公開を停止し、管理者が有効化 | 「後でretroactive適用できる」と仮定して公開 | [S25] |
| release runが既に存在 | 既存runを採用し状態確認 | 重複dispatch | [S23] |
| workflow failure、Releaseなし | failed stepとtag不変性を確認して再実行可否を判断 | 原因未確認の連続dispatch | [S11][S14] |
| workflow failure、Releaseあり | 公開物を変更せず、integrity確認後に新版判断 | asset置換・tag削除/再利用 | [S14][S25] |
| sidecar/attestation/layout/version/cold launch failure | Releaseを変更せずBLOCKED、影響を記録し新版を作る | 公開ZIPの差替え | [S14][S25] |
| closure docs test failure | mainのdocsを修正して再test | release tag/asset変更 | [S10][S14] |
| remote main競合 | fetch後に差分をreviewし計画を再baseline | force push | [S14] |

## 6. 実行証跡テンプレート

次の値はcommand/APIから取得し、空欄のままなら完了扱いにしない。

| Key | 値 | Source |
|---|---|---|
| `release_commit` | `b68e7577df616c0e259b55697a52d26412a01208` | [S01][S07][S09] |
| `tag_object` | `dd687611f46dbdd1e006a60f4ee1582f77151cd7` | [S06][S07] |
| `initial_ci_run_id` | `33682636186` | [S02][S04] |
| `initial_ci_conclusion` | TBD（実測必須） | R-01 |
| `immutability_enabled_before_publish` | TBD（実測必須） | R-02 |
| `release_workflow_run_id` | TBD（実測必須） | R-04 |
| `release_workflow_conclusion` | TBD（実測必須） | R-05 |
| `release_id` | TBD（実測必須） | R-06 |
| `release_url` | TBD（実測必須） | R-06 |
| `published_at` | TBD（実測必須） | R-06 |
| `public_zip_bytes` | TBD（実測必須） | R-07 |
| `public_zip_sha256` | TBD（実測必須） | R-07 |
| `public_sidecar_exact_match` | TBD（実測必須） | R-07 |
| `release_attestation` | TBD（実測必須） | R-06/R-07 |
| `public_package_layout` | TBD（実測必須） | R-07 |
| `public_cold_launch` | TBD（実測必須） | R-07 |
| `closure_commit` | TBD（実測必須） | R-10 |
| `final_ci_run_id` | TBD（実測必須） | R-11 |
| `final_ci_conclusion` | TBD（実測必須） | R-11 |

## 7. 完了checklist

- [ ] R-01 initial CI successを実測した。
- [ ] R-02 immutabilityを公開前に確認した。
- [ ] R-03 Release不存在・tag identity・重複runを確認した。
- [ ] R-04 release workflowを1回だけdispatchし、run IDを記録した。
- [ ] R-05 release workflow全stepがsuccessになった。
- [ ] R-06 stable/public/immutable Releaseとexact asset setを確認した。
- [ ] R-07 public assetをfresh downloadし、hash/attestation/layout/version/CLI/cold launchを検証した。
- [ ] R-08 B-05/C-026/status/traceability/旧計画を実測値でcloseした。
- [ ] R-09 documentation 14/14とclosure差分gateがPASSした。
- [ ] R-10 closure commitだけをmainへpushし、tagを変更していない。
- [ ] R-11 closure commitの最終CIがsuccessになった。
- [ ] R-12 remote/docs/local状態が整合し、一時物を安全にcleanupした。

全checkが実測証跡へ追跡できるまで、v1.0.0 release作業を`COMPLETE`と表記しない。
