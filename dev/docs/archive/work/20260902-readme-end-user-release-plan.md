# README・v1.0.0正式公開計画

> **歴史資料:** この計画は完了済みの旧v1.0.0公開作業を記録したもので、現在の要求・製品版・受入状態を表しません。現在判断には[`dev/docs/implementation-status.md`](../../../../dev/docs/implementation-status.md)と[`dev/docs/traceability.md`](../../../../dev/docs/traceability.md)を使用します。

| 項目 | 内容 |
|---|---|
| 対象 | `README.md`、利用者文書、Windows x64 package、Git tag、GitHub Release |
| 要求正本 | `docs/requirements-definition.md` v4.1 |
| 製品版 | `1.0.0` |
| 対応環境 | Windows 11 x64 |
| 更新日 | 2026-09-03 |
| 現在状態 | RELEASE_CANDIDATE_VALIDATION |
| 公開ブロッカー | B-05: `v1.0.0` GitHub Release assetの実在確認 |

この文書は2026-09-02に作成したREADME再構成計画を、2026-09-03のcanonical sample統合、SystemTest Prompt統合、CI追加、Excel compatibility修正後の実行状態へ更新したものです。旧詳細はGit履歴に保持し、現在判断には本書と[`dev/docs/implementation-status.md`](../../../../dev/docs/implementation-status.md)を使用します。

## 1. 公開契約

- 配布物は`.NET 10` self-contained、unsignedの`StudyReportEvaluator-win-x64.zip`とSHA-256 sidecarです。
- GitHub Copilot CLIはZIP内`runtimes/win-x64/native/copilot.exe`へ固定し、manifest/version/hashを検証します。PATHへfallbackしません。
- macOS、Linux、Windows Arm64、installer、code signing、notarizationはv1.0.0の対象外です。
- 入力は標準`.xlsx`だけをread-onlyで扱い、別のfinal／partial workbookへatomicに出力します。
- AI品質、教育的妥当性、公平性、法的・組織policy適合性、不正行為判定、未実測時間・token・費用を保証しません。

## 2. Canonical sample境界

| 項目 | 確定値 |
|---|---|
| path | `sample/SampleReport.xlsx` |
| bytes | 470,806 |
| SHA-256 | `73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA` |
| package entries / relationships | 11 / 8 |
| worksheet dimension | `A1:L531` |
| data rows | 530 |
| 初期target columns | F〜J、supporting K |

- このfileだけをcanonical sampleとして扱い、同directoryの別fileを列挙、fallback、代用しません。
- `/sample/`はprivate local inputとしてGit追跡せず、publish/packageへ含めません。
- cell本文、header本文、worksheet名、学生情報、private pathを証跡へ出力しません。
- GitHub Actionsではsample依存の`SampleWorkbookStructuralTests`だけを除外します。正式公開前のlocal Windows gateでは同testとno-network technical E2Eを必須とし、CIで代用しません。

## 3. ブロッカー実行状況

| ID | Status | 実測・解消条件 |
|---|---|---|
| B-01 bundled CLI | RESOLVED | SDK固定CLI、manifest、RID、SHA-256、clean resolverをpackage testで検証 |
| B-02 platform scope | RESOLVED | Windows 11 x64だけを初版対象として要求・README・packageを同期 |
| B-03 screenshots | RESOLVED | production UIから7画像を再生成し、synthetic/fake境界を明記 |
| B-04 user docs | RESOLVED | `docs/`をv4.1へ同期し、repository/package両方でrelative linkを検証 |
| B-05 release asset | PENDING_EXTERNAL | annotated `v1.0.0`、ZIP、sidecar、GitHub Release、実在download URLが必要 |
| B-06 production wording | RESOLVED | 開発task文言をproduction UIから除去 |
| B-07 canonical sample | RESOLVED_LOCAL | exact identity、F〜K mapping、read-only入力不変、530-row no-network E2Eを確認 |
| B-08 performance | RESOLVED | Windows 531-row synthetic read/write/final validation中央値7.090321秒、30秒基準内 |
| B-09 Excel compatibility | RESOLVED_FOCUSED | Config row cellをExcel列順へ修正し、Excel 16.0 open/recalculate/saveとprocess cleanupを確認 |
| B-10 CI/release automation | RESOLVED_IMPLEMENTATION | `.github/workflows/ci.yml`と`.github/workflows/release.yml`を追加し、actionlintで検証 |

## 4. 実装task状況

| Task | Status | 完了条件 |
|---|---|---|
| RD-01 Claim ledger | COMPLETE | C-001〜C-032をsource/testへ追跡 |
| RD-02 User docs sync | COMPLETE | 利用者文書の既知矛盾0 |
| RD-03 UI screenshots | COMPLETE | 7画像、synthetic/fake disclosure |
| RD-04 README rewrite | COMPLETE_CONTENT | 正式download URLはB-05で有効化 |
| RD-05 Contract tests | COMPLETE | documentation contract 14/14 PASS |
| RD-06 Release validation | IN_PROGRESS | Excel修正後のfull、canonical E2E、package、remote workflowを再実行 |
| RD-07 Independent review | COMPLETE | 未解決blocker/high finding 0 |
| RD-08 Public release | PENDING_EXTERNAL | tag、GitHub Release、asset URL、download cold launchを検証 |

## 5. 最終実行順

1. `CHANGELOG.md`へ`[1.0.0] - 2026-09-03`を確定する。
2. `dev/version.ps1 verify`、locked restore、Release buildを実行する。
3. Excel compatibilityのfocused testとoptional external recalculationを実行する。
4. canonical sample structural test、530-row no-network technical E2E、全Core/App test、documentation contractを実行する。
5. `scripts/package-windows.ps1`でfinal ZIPとsidecarを生成する。
6. package test、published DLL version、ZIP layout/hash、clean launch、bundled resolverを検証する。
7. README記載手順だけでlocal ZIPのhash確認、展開、cold launchを行う。
8. 全変更をrelease commitへまとめ、working treeをcleanにする。
9. annotated tag `v1.0.0`を作成し、`dev/version.ps1 verify -Tag v1.0.0 -RequireClean`を実行する。
10. release commitとtagをremoteへpushする。
11. release workflowを既存tagに対して実行し、ZIPとsidecarをGitHub Releaseへ添付する。
12. 公開assetを再downloadし、size/hash、single-root layout、cold launchを検証する。
13. 実在download URLをREADME、claim ledger、implementation statusへ反映し、B-05を閉じる。

## 6. CI/CD境界

### CI

[`.github/workflows/ci.yml`](../../../../.github/workflows/ci.yml)は次を実行します。

- `main` push／pull request／manual dispatch
- PowerShell Core 7+と.NET SDK 10.0.400
- locked restore、version検証、Release build
- canonical sample structural testを除く決定的test
- `git diff --check`、zero-byte source検査
- TRX artifactの保存

### Release

[`.github/workflows/release.yml`](../../../../.github/workflows/release.yml)は次をfail-closedで実行します。

- manual dispatchで指定した既存annotated tagをcheckout
- tag／HEAD／CHANGELOG／製品版／clean treeの一致検証
- locked restore、Release build、sample非依存test
- PowerShell正本scriptによるpublish/package
- published DLL versionとSHA-256 sidecar検証
- workflow artifact保存
- 既存Releaseがない場合だけGitHub Release作成

Live Copilot、canonical sample、Microsoft ExcelはGitHub-hosted runnerのrequired gateへ含めません。各local／external statusを決定的CI結果へ読み替えません。

## 7. v1.0.0受入条件

- [ ] `CHANGELOG.md`、製品版、release commit、annotated tagが一致する。
- [ ] local canonical sample identityと入力不変が一致する。
- [ ] Excel compatibility修正後のrequired full testが全件PASSする。
- [ ] documentation contractとpackage testがPASSする。
- [ ] final ZIPとsidecarが一致し、sample/source/test/secretを含まない。
- [ ] clean展開先からself-contained appが起動し、bundled CLIを解決する。
- [ ] GitHub ReleaseにZIPとsidecarが存在する。
- [ ] 公開downloadを再取得してhashとcold launchを確認する。
- [ ] READMEとclaim ledgerが実在URLだけを案内する。
- [ ] 公開済みtag／assetを差し替えない。

## 8. 証跡正本

- [`docs/requirements-definition.md`](../../../../docs/requirements-definition.md)
- [`SystemTest-prompt.md`](../../../../tests/SystemTest-prompt.md)
- [`dev/docs/traceability.md`](../../../../dev/docs/traceability.md)
- [`dev/docs/readme-claim-ledger.md`](../../../../dev/docs/readme-claim-ledger.md)
- [`dev/docs/version-management.md`](../../../../dev/docs/version-management.md)
- [`tests/StudyReportEvaluator.App.Tests/E2E/SampleWorkbookStructuralTests.cs`](../../../../tests/StudyReportEvaluator.App.Tests/E2E/SampleWorkbookStructuralTests.cs)
- [`tests/StudyReportEvaluator.App.Tests/E2E/RealDataSystemSmokeTests.cs`](../../../../tests/StudyReportEvaluator.App.Tests/E2E/RealDataSystemSmokeTests.cs)
- [`tests/StudyReportEvaluator.App.Tests/E2E/ExternalSpreadsheetRecalculationSmokeTests.cs`](../../../../tests/StudyReportEvaluator.App.Tests/E2E/ExternalSpreadsheetRecalculationSmokeTests.cs)
- [`tests/StudyReportEvaluator.App.Tests/Packaging/WindowsPublishPackageTests.cs`](../../../../tests/StudyReportEvaluator.App.Tests/Packaging/WindowsPublishPackageTests.cs)
