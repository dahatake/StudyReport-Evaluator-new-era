# StudyReport Evaluator 公開プロセス修正 実行記録

## 0. 文書管理

| 項目 | 値 |
|---|---|
| 実行計画 | [`20260904-publication-remediation-plan.md`](20260904-publication-remediation-plan.md) |
| 実行開始日 | 2026-09-04 |
| 開始HEAD | `1fdc9ab40c9e605fa6a49a131e232563d8b351e7` |
| 開始branch | `main` |
| 状態 | `IN_PROGRESS` |
| 原則 | 未実行は`NOT_RUN`、外部入力不足は`BLOCKED_EXTERNAL`、実測成功だけを`PASS`とする |

本記録へcredential、private key、password、token、学生回答、Prompt、AI reason/evidence、private absolute pathを保存しない。

### Scope update

2026-09-04の要求所有者追加指示により、Windows MSIXは開発用`PASS_MECHANISM`までを採用し、production signed MSIX taskは廃止する。unsigned executable MSIXは一般配布せず、Windows public artifactはunsigned ZIPとsidecarを維持する。外部情報が提供されていないmacOS production taskは`BLOCKED_EXTERNAL`として正確に閉じる。

敵対的レビューで、v4.2 AC-023/AC-028と旧W3/R4 taskを残したままでは矛盾することを確認した。このfindingを採用し、計画§0.2へv4.3改版、Windows ZIP initial release、development MSIX非公開、macOS conditional release、旧production MSIX task supersessionを追加した。

再レビューでWindows ZIP flow、PATCH bump位置、v4.3対象file、active task IDと旧taskの区別が不足していたため、S1/M2/R2/V2のactive routing、Windows ZIP中心のMermaid、`V2-02` CHANGELOG、`V2-03` PATCH bumpを追加し、旧Phase 3W〜6見出しを`SUPERSEDED`とした。`docs/requirements-definition.md`のファイル名不正という指摘は、記載も実在fileも同じ複数形であるため棄却した。

active routing最終レビューの「初回公開なのでPATCH bump不要」は、要求所有者が全task完了後のPATCH incrementを明示しているため棄却した。`V2-03`の`0.8.0`→`0.8.1`を維持する。

## 1. P0-01 — デフォルトdecision承認

### 実行結果

- 要求所有者の2026-09-04指示により、計画§4のQ-001〜Q-010を全てデフォルトで採用した。
- 計画状態を`APPROVED — IN PROGRESS`へ更新した。
- 採用値: 要求正本は`docs/requirements-definition.md` v4.2、full v4.2 scope、候補版`0.8.0`、SystemTest正本はroot、production releaseはstable-only、Windows signingはPFX/certificate + SignTool、macOS build numberはrelease commit count、macOS required matrixは6行、legacy ZIPはCI regressionのみ、signing/publish Environmentを分離。
- external identity、credential、approved asset、native hostは未提供であり、推測しない。

### 敵対的レビュー

- Q-001〜Q-010を要求v4.2へ照合し、要件矛盾、存在しない問題、YAGNI違反は確認されなかった。
- Windows production identity、Apple identity、approved assets、native hostsはデフォルト値で生成できないため、後続の明示的`BLOCKED_EXTERNAL` gateとして保持する。

### 反映確認

- 計画冒頭は`APPROVED — IN PROGRESS`。
- §4は「要求所有者指示によりデフォルト採用」へ更新済み。

**Status: PASS**

## 2. P0-02 — worktree所有権確認

### 実行結果

- `main`、HEAD `1fdc9ab40c9e605fa6a49a131e232563d8b351e7`で、`git status --porcelain=v1 --untracked-files=all`と`git diff --numstat`を取得した。
- 計画作成前の調査では14 entriesだった。P0-02再測定時はADR-0014更新と計画書が加わり16 entriesだった。
- SystemTestの採用正本は計画Q-004どおりrepository rootであり、`tests/SystemTest-prompt.md`のcorrected本文をrootへ戻す。

| 分類 | File |
|---|---|
| 0.8.0再baseline | `Directory.Build.props`、`CHANGELOG.md`、ADR-0014、ADR-0015、`dev/docs/version-management.md`、`dev/version.tests.ps1`、App tests lock |
| public truth/docs | `README.md`、`dev/docs/README.md`、`dev/docs/implementation-status.md`、`dev/docs/traceability.md`、`DocumentationContractTests.cs` |
| SystemTest正本収束 | root `SystemTest-prompt.md`削除状態、`tests/SystemTest-prompt.md` untracked |
| MSIX修復 | `scripts/test-windows-msix-unsigned.ps1` |
| 計画/記録 | `work/20260904-publication-remediation-plan.md` |

所有不明・無関係fileは0件。既存内容をstash/reset/cleanせず全て保持して収束できる。

### 敵対的レビュー

採用:
- ADR-0014の0.8.0 noteは他の版正本と同じ変更目的に属するため保持する。
- 全entryに公開修正上の帰属がある。

棄却:
- 「remote READMEも公開物0件へ訂正済み」は誤り。訂正はlocal未commitである。remote `main`は`https://github.com/dahatake/StudyReport-Evaluator/releases/download/v1.0.1/StudyReportEvaluator-win-x64.zip`を案内していた。
- 「rootからtestsへの移動が修復」は誤り。現行contractはrootを正本とするため、corrected本文をtestsからrootへ戻す。
- 「次に全差分を即commit」は計画順序違反。F-001/F-003/F-004等を修正しfocused validation後にcommitする。

### 反映確認

- 後続B1-01はtests→root、B1-09はlocal訂正をremoteへ反映する前提を維持。
- 未検証差分をcommitしていない。

**Status: PASS**

## 3. P0-03 — 専用branch作成

### 実行結果

- P0-02後に本実行記録を新規作成したため、branch移行直前のstatusは17 entriesだった。
- `fix/publication-readiness-20260904`を新規作成して移動した。
- HEADは`1fdc9ab40c9e605fa6a49a131e232563d8b351e7`のまま不変。
- branch移行直前と直後のstatusは17 entriesで不変。
- stash、reset、clean、commitは実行していない。

### 敵対的レビュー

採用:
- P0-03結果と16→17の増分理由が未記載だったため、本節へ明示した。

棄却:
- 「branch移行でentryが増えた」は誤り。増分は移行前に作成した本記録であり、移行前後は17→17。

### 反映確認

- current branch、HEAD、status countを再取得し、branch=`fix/publication-readiness-20260904`、HEAD一致、17 entriesを確認した。

**Status: PASS**

## 4. P0-04 — public state再観測

### 実行結果

2026-09-04の公開APIおよび認証済みAPIで次を確認した。

| 項目 | 結果 |
|---|---|
| GitHub Releases（draft含む） | 0件 |
| Public tag | `v1.0.0` 1件、target `b68e7577df616c0e259b55697a52d26412a01208` |
| `v0.8.0` tag | 不存在（HTTP 404） |
| Immutable Releases | `enabled=true`、`enforced_by_owner=false` |
| GitHub Environments | 0件 |
| Repository Actions secret names | 0件 |
| remote `main` | local baseline HEAD `1fdc9ab40c9e605fa6a49a131e232563d8b351e7`と一致 |
| latest CI | run `33731803304`、failure |
| latest Release workflow | run `33696306818`、failure |

新しいRelease、候補tag、第三者によるremote main変更は検出されず、停止条件に該当しない。過去記録は[`20260903-v1.0.1-release-recovery-plan.md`](20260903-v1.0.1-release-recovery-plan.md)を参照した。

### 敵対的レビュー

採用:
- 過去記録はv1.0.1不存在中心だったため、今回v1.0.0存在、v0.8.0不存在、draft、Environment、secret名、remote mainを同時に再測定した。

棄却:
- 「tag存在とRelease不存在は矛盾」は誤り。Git tagとGitHub Releaseは独立objectであり、実際にv1.0.0 workflowはRelease作成前に失敗している。
- 「過去workflow failureはproduction signing不足の可能性」はログと不一致。Release #1は複数Git path、CI #3はempty status parameter bindingが直接原因として確認済み。
- v0.8.0不存在確認のHTTP 404は期待結果である。正常終了として再検証した。

### 反映確認

- remote main一致、候補tag不存在、専用branch、17 status entriesを1回の正常終了commandで再確認した。

**Status: PASS**

## 5. B1-01 — SystemTest正本をrootへ収束

### 実行結果

- corrected本文を`tests/SystemTest-prompt.md`からroot `SystemTest-prompt.md`へ移動した。
- 移動前後SHA-256は`B355466C8C781FA7642C4FE380F05CD1EFC215ECC4B2655162A1E6A6CB7EB08E`で一致した。
- root file 1件、`tests/` copy 0件、ST-UC-01〜25、TR-01〜29を確認した。

### 敵対的レビュー

- 文字化け、内容欠落、scenario重複、TR欠番、正本重複は確認されなかった。
- TR-24A/Bはoptional smoke 2種をTR-24内で区別する既存契約で、contract regexも対応済みのため変更しない。
- ST-UC-02の要求§4.4参照は実在するcanonical sample契約へ追跡できるため変更しない。

### 反映確認

- editor diagnostics 0件。
- `DocumentationContractTests`はrootを読み、`tests/` copy不存在を要求している。

**Status: PASS**

## 6. B1-02 — v4.2 evidence identityへ同期

### 実行結果

- `RealDataSystemSmokeTests.cs`が生成するrequirementsとSystemTest identityをv4.1からv4.2へ更新した。
- `CURRENT STRUCTURAL PROFILE`である`sample-workbook-profile.md`のRequirementもv4.2へ同期した。
- `DocumentationContractTests`へprofile版の回帰assertionを追加した。

### 敵対的レビュー

採用:
- current profileのv4.1は実在する不整合だったため修正した。

棄却:
- ADR-0013、developer文書の履歴図、旧work計画のv4.1は決定・計画当時の正本を示すhistorical valueであり、current evidenceではないため変更しない。

### 反映確認

- 対象current evidence/profileのv4.1参照は0件。
- 対象3fileのdiagnosticsは0件。
- 再レビューでcurrent-contract不一致0件。

**Status: PASS**

## 7. B1-03 — 製品版正本を確定

### 実行結果

- `dev/version.ps1 show -Json`: `PASS`、version/prefix=`0.8.0`、suffix空。
- `dev/version.ps1 verify -Json`: `PASS`、App/Core 2 projectともversion/prefix=`0.8.0`、suffix空。

### 敵対的レビュー

- `Directory.Build.props`、CHANGELOG、README、ADR-0014/0015、version management、implementation statusの非historical sourceに0.8.0との矛盾は確認されなかった。
- `bin/`、`obj/`、`work/`の過去snapshot、dependency versionは製品版判定から除外した。

### 反映確認

- source変更は不要。
- diagnostics 0件。

**Status: PASS**

## 8. B1-04 — version self-testを候補版へ同期

### 実行結果

- `dev/version.tests.ps1`をPowerShell Core 7で1回実行し、14 assertionsが全て成功した。
- 実repositoryの`Directory.Build.props`と`CHANGELOG.md`は実行前後SHA-256が一致した。

### 敵対的レビュー

棄却:
- 実CHANGELOGにdated 0.8.0 sectionがない点は、公開時まで作らない計画どおり。temporary cloneのsynthetic release entryはtag検証を可能にし、production履歴を偽装しない。
- `git config`個別exit check追加は、後続commitが失敗を検出し、本taskの品質向上に対して過剰なerror分岐となるため追加しない。
- 実repositoryのfile lock、arbitrary version、prerelease checkpoint、異なるGit binary間整合は本self-testの契約外であり、要求のない拡張をしない。
- 2つのPATH shimは過去障害である複数`Get-Command` resultを直接再現し、修正版が先頭applicationだけを使うことを検証している。

### 反映確認

- source変更なし。
- diagnostics 0件、14 assertions再現、正本2file不変。

**Status: PASS**

## 9. B1-05 — unsigned MSIX証跡のclean-tree修正と版一元化

### 実行結果

- `Get-Utf8Sha256Hex.Value`へ`AllowEmptyString`を追加し、clean checkoutの空statusをSHA-256化できるよう修正した。
- 製品版を`dev/version.ps1 show -Json`から取得し、MSIX 4-part版を導出した。`0.8.0`/`0.8.0.0`の4 hard-codeを除去した。
- version toolは使用前に存在確認し、同一PowerShell sessionのstale `$LASTEXITCODE`へ依存しないようにした。
- development MSIX mechanismを1回実行した。

| 項目 | 実測値 |
|---|---|
| Status | `PASS_MECHANISM` |
| Production status | `BLOCKED_EXTERNAL`（owner decision後はproduction MSIX scopeをsupersede） |
| Install status | `NOT_RUN_REQUIRES_ELEVATED_DISPOSABLE_WINDOWS_11_HOST` |
| Package | `StudyReportEvaluator-win-x64.unsigned.test.msix` |
| Version | `0.8.0.0` |
| Bytes | 155,500,648 |
| SHA-256 | `4D89DAC5716EFA8FBD43E3795374E3B58DA9A20CF710CEF7200A792F9F589FE1` |
| Package/evidence/sidecar hash | 一致 |
| Signature entry | 0 |
| Negative policy checks | 2件 |
| Temporary MSIX directories | 0 |
| Relevant residual processes | 0 |

### 敵対的レビュー

採用:
- version toolの存在確認が使用後だったため、使用前へ移動した。
- `.ps1`は同一sessionで実行されるため、staleになり得る`$LASTEXITCODE`判定を除去した。

棄却:
- prerelease拒否はstable-only release decisionどおり。
- package identity、fixed OID、tool lock、evidence schemaを追加変更する必要は確認されなかった。

### 反映確認

- diagnostics 0件。
- package、evidence、sidecarのSHA-256一致。
- cleanup完了。

**Status: PASS_MECHANISM**

## 10. B1-06 — MSIX回帰契約を追加

### 実行結果

- `WindowsInstallerPackageTests`へempty status許可、version tool参照、4-part版導出、0.8.0 hard-code禁止を追加した。
- 対象classは5/5 PASS。

### 敵対的レビュー

- assertionは実装のexact contractへ一致し、BuildTools固定版を製品版hard-codeとして誤検出しない。
- clean-status integrityとproduct version一元化の回帰を固定し、自己充足・過剰検査は確認されなかった。

### 反映確認

- 追加修正なし。
- diagnostics 0件。

**Status: PASS**

## 11. B1-07 — lockを正規生成

### 実行結果

- solution restoreを`--force-evaluate`、続けて`--locked-mode`で実行し、両方成功した。
- 4 lock中の変更はApp tests lock内のApp→Core project dependency `1.0.1`→`0.8.0`だけだった。
- 外部packageの`resolved`/`contentHash`変更は0件。

### 敵対的レビュー

棄却:
- 「project dependency versionは手編集」という指摘は、NuGet force-evaluateの実測と矛盾する。
- workspace外のfresh temporary cloneへ現在の`Directory.Build.props`だけを適用してrestoreした結果、同じJSON pathに`[0.8.0, )`が生成された。
- temporary cloneは削除済みで残存0。

### 反映確認

- `dotnet restore --locked-mode`は成功。
- lock diagnostics 0件。
- tool生成差分以外なし。

**Status: PASS**

## 12. S1-01 — 要求正本をv4.3へ改版

### 実行結果

- 機能契約を維持し、delivery scopeだけをv4.3へ改版した。
- Windows public artifactはself-contained unsigned ZIPとsidecar。
- development MSIXはnon-public `PASS_MECHANISM`。
- macOS production deliveryは現版scope外、source/static contractだけを維持。
- AC-001〜028とTR-01〜29のIDを保持した。

### 敵対的レビュー

- 初回レビューで内部矛盾、番号欠落、production MSIX残存、required macOS残存は確認されなかった。
- 機械検索で§17.1見出しだけv4.2だったため採用し、v4.3へ修正した。

### 反映確認

- 文書版4.3、§17.1 v4.3、AC 28件、TR 29件を確認。
- current scopeのv4.2見出し0件。
- diagnostics 0件、最終レビュー未反映0件。

**Status: PASS**

## 13. S1-02 — ADR／architecture／detailed designをv4.3へ同期

### 実行結果

- ADR-0015をv4.3の初回Windows ZIP公開、development MSIX non-public、macOS source foundationへ改版した。
- architectureの本文とMermaidを同じdelivery flowへ同期した。
- detailed designのheader、設計目標、§13、delivery tests、file map、Definition of Doneを同期した。

### 敵対的レビュー

採用:
- ADR Decision #1、Consequences、Transitionに残ったv4.2／3操作／旧production gateを修正した。
- detailed design冒頭、delivery test、Definition of Doneの旧production MSIX/macOS required記述を修正した。
- architecture図へ`publish=true`/`publish=false`とRelease filterを明示した。

棄却:
- Windows direct MSIXの署名要件説明は、development-only判断の根拠となるContextであり保持する。
- macOS signing orderは将来production scopeの安全contractで、source foundationの一部として保持する。
- development MSIXを別matrixへ分離する案は、v4.3 AC-028/TR-29が同一matrixのnon-public rowを要求するため採用しない。

### 反映確認

- ADR、architecture、detailed designの再レビューで未反映0件。
- 3fileのdiagnostics 0件。

**Status: PASS**

## 14. S1-03 — Traceability／SystemTest／public docsをv4.3へ同期

### 実行結果

- traceabilityとclaim ledgerをWindows ZIP public、development MSIX non-public、macOS current scope外へ同期した。
- SystemTestをv4.3／2026-09-04へ更新し、ST-UC-22〜25をdevelopment MSIX mechanism、macOS static contract、Windows ZIP release matrixへ置換した。
- RealData evidenceとsample profileをv4.3へ同期した。
- README、user guide index、getting-startedへ公開asset 0件、Releases index、development MSIX非公開を同期した。
- architecture header、developer index、version-management、implementation-statusをv4.3へ同期した。
- DocumentationContractTestsは最終14/14 PASS。

### 敵対的レビュー

採用:
- sample profileのverification date/conclusionに残ったv4.1をv4.3／2026-09-04へ修正した。
- traceabilityのAC-023へ`unsigned`とinstall非required／未実行を明記した。
- current validation前のAC-021/TR-21をPLANNEDへ戻した。
- claim ledgerの更新日を2026-09-04へ修正した。
- architecture header、developer index、version-management、implementation-statusの旧current scopeを修正した。

棄却:
- 同じACを複数scenarioで検証することは重複欠陥ではない。
- ST-UC-25が後続workflow/matrixを検証すること、candidate前はfresh downloadを`NOT_RUN_NO_CANDIDATE`とすることは実行段階を正確に分ける契約である。
- READMEから要求定義書へ直接リンクする要件はない。
- 旧ADR、work計画、履歴図のv4.1/v4.2はhistorical valueとして保持する。
- 横断レビューの`READY_FOR_RELEASE`表現はmatrix/workflow未完了のため採用しない。

### 反映確認

- current evidence/SystemTestのv4.1/v4.2残存0件。
- AC-001〜028、TR-01〜29、C-001〜038の連番を維持。
- public docsにversioned download URL 0件、development MSIX一般利用案内0件。
- 対象文書diagnostics 0件、DocumentationContractTests 14/14 PASS。

**Status: PASS**

## 15. B1-15 — CIの最小修正

### 実行結果

- CIの`actions/setup-dotnet`をv6へ更新した。
- version tool self-testをrequired stepへ追加した。
- deterministic testがskipされた場合のTRX upload二次errorを防ぎ、MSIX evidenceはmechanism step成功時だけuploadするようにした。
- workflow contract testを追加し、WindowsInstallerPackageTestsは最終5/5 PASS。

### 敵対的レビュー

採用:
- solution force-evaluateでWindows SDK BuildTools lockのexact requested rangeが`[v]`からNuGet 10正規形`[v, v]`へ変わり、test 1件が失敗した。
- driverとtestを生成形式へ同期した。source PackageReference、resolved version、contentHashは同じexact versionを維持する。

棄却:
- macOS artifact upload条件の統一は今回の実在failureではなく、facts fileを常に先に生成する既存設計なので変更しない。
- exact rangeはmin=maxであり、version rangeを緩和していない。

### 反映確認

- WindowsInstallerPackageTests 5/5 PASS。
- solution locked restore成功。
- workflow/script/test/lock diagnostics 0件、`git diff --check`成功。

**Status: PASS**

## 16. B1-16 — baseline focused validation

### 実行結果

| 検証 | 実測結果 |
|---|---|
| solution locked restore | exit 0 |
| version self-test | 14 assertions PASS |
| Release build | exit 0、warning 0、error 0 |
| DocumentationContractTests | 14/14 PASS |
| WindowsInstallerPackageTests | 5/5 PASS |
| MacOsPublishPackageTests | 2/2 PASS |
| focused 3-class aggregate | 21/21 PASS |
| RealData evidence contract | v4.3 requirements／SystemTest source identity assertion PASS |
| workspace diagnostics | 0件 |
| `git diff --check` | exit 0 |

opt-in `RealDataSystemSmokeTests`とLive AIはB1-02および計画§5.1どおりrequired gateへ昇格せず、実行していない。

### 敵対的レビュー

採用:
- 「RealData evidence contract」がopt-in E2Eかsource assertionか不明確だったため、計画B1-16へsource identity assertionであることと非実行範囲を明記した。
- 現版のmacOS scopeはstatic source contractなので、追加で`MacOsPublishPackageTests` 2件を実行範囲へ明記した。

棄却:
- source identity assertion未実装という指摘は、`DocumentationContractTests`へv4.3の2 literalを検査するassertionが存在し、同class 14/14 PASSという実測と矛盾する。
- `--no-build`が古いbinaryを使うという指摘は、同一sourceに対するRelease build成功後にfocused testsを実行し、assertion追加後もtest runnerによる再buildを伴う単独testが14/14 PASSしているため該当しない。
- 本節未記載という指摘はタスク完了前のレビュー時点を観測したもので、実装欠陥ではない。本節で実測結果を記録した。

### 反映確認

- RealData evidence contractの意味と非実行範囲が計画に明記されている。
- required検証と追加static contractは全てPASSし、failure/error 0。

**Status: PASS**

## 17. B1-17 — implementation status同期

### 実行結果

- current branch、HEAD、0.8.0、B1-16のlocked restore／build／focused test／diff-check実測値を記載した。
- current candidateで未実行のWindows ZIP、full required regression、canonical technical E2Eを`NOT_RUN_CURRENT_CANDIDATE`とした。
- 2026-09-02／03のUI、CLI、ZIP、sample、E2E、system smoke、optional evidenceを`Previous ... baseline`へ改称し、current gateへ未算入と明記した。
- root SystemTest 25 scenario／TR-01〜29をcurrent documentation contractへ接続した。

### 敵対的レビュー

採用:
- current表内の旧Windows ZIP 3/3や旧E2E結果は過去値との境界が弱かったため、各行を`Previous`化し、delivery statusへcurrent未実行行を追加した。

棄却:
- B1-17実行記録がないという指摘は、完了記録を再レビュー後に作る手順を観測したもので実装欠陥ではない。
- 25 scenario／TR-29が未反映という指摘は、documentation contract行の明記と14/14 PASSに反する。

### 反映確認

- 再レビュー: current／previous境界、未実行3行、branch／HEAD／version、25 scenario／TR-29を確認し、unresolved Critical/High 0。
- content data included = false。

**Status: PASS**

## 18. B1-18 — traceability同期

### 実行結果

- active planを2026-09-04 remediation planへ更新し、B1-16実測値を記載した。
- AC-020／TR-19／TR-20は`NOT_RUN_CURRENT_CANDIDATE`、AC-023／TR-25はnon-public `PASS_MECHANISM`とした。
- AC-021／022、TR-21／22はcurrent documentation evidenceにより`PASS_REQUIRED`とした。
- AC-024／025、TR-26／27はsource-only static contract 2/2に限定して`PASS_REQUIRED`とし、production artifact／署名／公証／clean-host実測ではないと明記した。
- AC-026〜028、TR-28〜29は後続task未完了のため`PLANNED`を維持した。AC-001〜028、TR-01〜29の欠番はない。

### 敵対的レビュー

採用:
- `PASS_MECHANISM`へ非公開・非productionの意味を追加した。
- macOS static `PASS_REQUIRED`をproduction `PASS_PRODUCTION`へ誤読しないsource-only境界を追加した。
- TR-25へnon-public unsignedを明記した。

棄却:
- AC-020が`PASS_REQUIRED`、AC-024／025が`PLANNED`という指摘は、レビュー時の実fileが既にそれぞれ`NOT_RUN_CURRENT_CANDIDATE`／`PASS_REQUIRED`であり、現内容と一致しない。
- AC-026〜028を現版scope外にする指摘は誤り。Windows ZIP setup／package exclusion／release matrixはv4.3 active scopeであり、未完了なので`PLANNED`が正しい。
- docs evidence専用statusの追加は不要。直接deterministic documentation test成功は既存語彙`PASS_REQUIRED`で表す。

### 反映確認

- 再レビュー: AC／TR全件、static／mechanism／production境界、false PASSを確認し、unresolved Critical/High 0。
- content data included = false。

**Status: PASS**

## 19. B1-19 — claim ledger同期

### 実行結果

- SystemTest v4.3 ST-UC-01〜25／TR-01〜29、2026-09-04 public Release／asset 0件、B1-16 focused evidenceをheaderへ記載した。
- C-026／027／036〜038は`BLOCKED`、C-029／033は`VERIFIED`、C-034／035は`EXCLUDED`とした。
- C-033の`VERIFIED`はdevelopment mechanism成功と非公開境界だけを指し、production readinessではないと定義した。
- 2026-09-03の670件は0.8.0 current candidate required release gateへ算入しないと明記した。
- 計画B1-19の旧「C-033〜038 BLOCKED」をv4.3実態へ修正した。

### 敵対的レビュー

採用:
- claim単位の`VERIFIED`とproduction readinessの区別、および過去670件の非算入境界を強化した。

棄却:
- C-033を`BLOCKED`にする指摘は、claim自体が「development mechanismは成功し一般配布しない」であり、その限定事実を実測済みなので不正確。
- READMEの「配布開始後」条件形は現在のasset存在を断定せず、「現在、公開済みの配布物はありません」と併記されるためC-026と矛盾しない。
- macOS将来scopeの記述は現在の提供を断定せず、C-034／035を`EXCLUDED`に保持している。

### 反映確認

- 再レビュー: C-001〜038連番、指定status、public asset 0、25 scenario／TR-29、過去/current境界を確認し、unresolved Critical/High 0。
- content data included = false。

**Status: PASS**

## 20. B1-20 — baseline checkpoint commit/push

### 実行結果

- 27 fileの明示allowlistだけをstageした。allowlist delta 0、unstaged 0、untracked 0、secret／private-path候補0。
- baseline checkpoint commit `268efde00e66fafefc457766120fbebea608df4a`（`fix: align publication readiness baseline`）を作成した。
- 専用branch `fix/publication-readiness-20260904`だけをoriginへpushした。main、tag、Releaseは変更していない。
- push直後のlocal／remote SHAは上記commitで一致し、worktree statusは0件だった。

### 敵対的レビュー

採用:
- staged `git diff --check`がuntrackedだった計画書の末尾空白3件を検出したため、commit前に除去して再検証した。
- lock差分への懸念を受け、実変更2 fileを再度`--force-evaluate`した。file SHA-256とdiff SHA-256は前後一致し、続くlocked restoreとdiff-checkはexit 0だった。
- commit後のtree取得で未引用`HEAD^{tree}`がPowerShellに解釈されvalidation commandだけ失敗した。quoted revisionでcommit `268efde...`、tree `e9c5cd209444f2a398f454e1d969313ab1190c46`、status 0を再取得した。commit自体への影響はない。

棄却:
- 4 lock fileを変更したというレビュー記述は実statusと不一致。変更は`eng/packaging/windows/tools/packages.lock.json`と`tests/StudyReportEvaluator.App.Tests/packages.lock.json`の2件だけである。
- App Tests lockのCore rangeは手編集ではない。fresh cloneの過去再現に加え、今回もforce-evaluate前後で同一だった。

### 反映確認

- commit object敵対レビューとpush完了後レビューはともにunresolved Critical/High 0。
- remote SHA一致、content data included = false。

**Status: PASS**

## 21. B1-21 — hosted CI確認

### 実行結果

- CI run: [`33833693768`](https://github.com/dahatake/StudyReport-Evaluator/actions/runs/33833693768)、run number 4、`workflow_dispatch`。
- evaluated head SHA: `855fec259104b772fc56e79b0696c622b54c7c7f`。
- run全体は`completed/success`。Windows、`macos-15`、`macos-15-intel`の3/3 jobが`completed/success`、annotations 0。
- Windows deterministic testsは679/679 PASS、version self-testは14 assertions PASS、development MSIXは`PASS_MECHANISM`。
- macOS contractは各runner 17/17 PASS。
- artifactsはtest results、unsigned MSIX mechanism、macOS 2 runnerの計4件。tempへdownloadしてnonzero file 8件、zero-byte 0、private-looking filename 0を確認後、tempを削除した。

### 敵対的レビュー

採用:
- run／job／step／artifact APIとdownloaded artifact集合を照合し、uploadだけのfalse-greenでないことを確認した。

棄却:
- hosted deterministic testsを670件とするレビュー記述は旧delivery baselineの件数であり、今回runの実測679件と一致しない。
- failed testでもTRXをuploadする条件は一次failure証跡を残す意図であり、step自体のfailureをsuccessへ変えないためfalse-greenではない。
- Windows deterministic stepは9分12秒で成功し、job timeout 60分内。成功実測だけからperformance defectを作らない。

### 反映確認

- exact SHA、3 job、全required step、4 artifactが一致。
- completion conditionを満たし、unresolved Critical/High 0、content data included = false。

**Status: PASS**

## 22. M2-01 — closed JSON Schema

### 実行結果

- `eng/schemas/platform-release-matrix-v1.schema.json`をdraft 2020-12で追加した。
- root、row、nested descriptorを全てclosedとし、Windows ZIP 1行とdevelopment MSIX 1行だけを要求した。
- ZIPは`publish=true`／`PASS_REQUIRED`、MSIXは`publish=false`／`PASS_MECHANISM`へ固定した。macOS rowは追加していない。
- product version、source commit、exact Windows environment、artifact／sidecar／evidenceのbasename・bytes・SHA-256をrequiredにした。

### 出典

- repository: `docs/requirements-definition.md` v4.3 AC-028／TR-29、`SystemTest-prompt.md` ST-UC-25、`dev/docs/detailed-design.md` §13.5。
- Microsoft Learn: [`Test-Json` PowerShell 7.6](https://learn.microsoft.com/powershell/module/microsoft.powershell.utility/test-json?view=powershell-7.6)。7.4以降はstrict JSON parsingとJsonSchema.NETを使用し、`-SchemaFile`でschema適合時だけ`$true`を返す。

### 敵対的レビュー

- PowerShell 7.6の実validatorで正例`True`、unknown root property`False`、missing row`False`を確認した。
- schema／計画diagnostics 0件。
- 再レビューはdraft、closure、row cardinality、publish/status/basename、macOS非追加、後続semantic境界を確認し、unresolved Critical/High 0。

### 反映確認

- 計画M2-01〜03の旧production-only記述をv4.3 initial Windows scopeへ同期した。
- content data included = false。

**Status: PASS**

## 23. M2-02 — semantic validator

### 実行結果

- `scripts/validate-platform-release-matrix.ps1`を追加した。
- PowerShell Core 7.4+／strict UTF-8／duplicate JSON property拒否／closed schemaを要求した。
- expected product version／source commit、2 rowの一意性、publish/status/environment、safe basename、reparse point、実file bytes/SHA-256、sidecar exact bytes、evidence bytes/SHA-256を検証する。
- ZIP evidenceをsource、product、host、package、8 required checkへbindした。
- development MSIX evidenceをsource、clean status、host、exact BuildTools、MakeAppx trust、identity/publisher/version、block map、CLI、negative policy、limitations、synthetic assetへbindした。
- outputのpublishable assetはWindows ZIPとsidecarの2件だけである。

### 敵対的レビュー

採用:
- 初版はevidenceをvalid JSONとhashだけで受理し、`{}`へdescriptorを追随させるfalse PASSが可能だった。artifact-kind別closed property setとsemantic bindingを追加した。
- PowerShell 7.6の`ConvertFrom-Json`がISO日時を型変換したため、`-DateKind String`で原文を保持した。
- shapeだけだったMSIX tool／identity fieldを既知のexact契約へ固定した。

棄却:
- artifact、evidence、descriptor、trusted workflow sourceを全て同時改ざんする攻撃者に対するcustom attestation追加は、repository write compromiseの別境界であり、計画§5.1の非追加項目である。M2 validatorはpackage実行を重複せず、upstream package testと後続protected workflowが同一hashへbindする。
- 4-byte synthetic artifactはmatrix/file identity testの隔離fixtureであり、production package evidenceではない。

### 反映確認

- parser／diagnostics 0件。
- 正例、artifact hash改ざん、descriptor追随CRLF sidecar、descriptor追随evidence status改ざんを含むcontract tests 9/9 PASS。
- 最終再レビューは責務境界と単一file/evidence改ざんを確認し、unresolved Critical/High 0。
- content data included = false。

**Status: PASS**

## 24. M2-03 — matrix validator tests

### 実行結果

- `ReleaseMatrixContractTests.cs`を追加した。
- valid、unknown property、duplicate artifact kind、missing row、non-PASS、artifact hash mismatch、descriptor追随sidecar mismatch、wrong basename、descriptor追随evidence tamperの9 caseを独立testにした。
- 各testは一意なOS temp directoryだけを使用し、終了時に削除をassertする。workspace artifactは変更しない。

### 敵対的レビュー

採用:
- 初回compileでnullable `string?[]`とexpected `string[]`の不一致を検出し、nullを明示拒否して修正した。
- evidence hashだけを追随させる負例を、M2-02 adversarial findingの回帰testとして追加した。

棄却:
- test fixtureのsynthetic bytesをproduction artifactとみなす指摘は、test名・temp境界・evidence値がproduction claimを作らず、semantic validatorだけをisolated testする設計と一致しない。

### 反映確認

- 9/9 PASS、failed/skipped/warning/error 0。
- 再レビューはcase独立性、temp cleanup、argument safety、required negative coverageを確認し、unresolved Critical/High 0。
- content data included = false。

**Status: PASS**

## 25. M2-04 — detailed design link

### 実行結果

- `dev/docs/detailed-design.md` §13.5.1へschema、validator、direct testの正本pathを追加した。
- exactly 2 row、artifact kindごとのpublish/status、Windows environment、file/evidence binding、exact public asset setを記載した。
- validatorの`PASS`はmatrix／file／evidence identity一致だけを意味し、package execution、production trust、custom attestationの代替ではないと明記した。
- macOS row、自由form status、任意metadata、未実測artifactをv1へ追加しない境界を記載した。

### 敵対的レビュー

- 要求AC-028／TR-29、ST-UC-25、schema、validator、9 direct testsへ照合した。
- 実装していないpackage実行を保証する記述、path誤り、row/status矛盾、macOS production claimは確認されなかった。
- 再レビューはunresolved Critical/High 0。

### 反映確認

- matrix 9件＋DocumentationContractTests 14件、aggregate 23/23 PASS。
- warning/error 0、diagnostics 0件、`git diff --check` exit 0。
- content data included = false。

**Status: PASS**

## 26. R2-01 — CIをWindows ZIP + development MSIX regressionへ確定

### 実行結果

- `scripts/test-windows-zip.ps1`を追加し、clean Windows 11 x64 checkoutだけで`WindowsPublishPackageTests` 3件を実行するrequired regressionとした。
- driverはTRXのexact 3 PASS、ZIP／sidecarのnonzero・reparse point・exact LF sidecar、safe archive layout、required entry、bundled CLI hash、利用者workbook除外と外部sentinel不変を検証する。
- closed evidenceは`windows-zip-required`／`PASS_REQUIRED`、exact source／product／host／package identityと8 required checkだけを記録する。`PASS_PRODUCTION`は生成しない。
- CIのgeneral deterministic suiteから同classを除外し、専用stepで1回だけ実行する。step成功時だけZIP、sidecar、evidenceのexact 3 fileを独立Actions artifactへuploadする。
- development MSIXは既存の別step／別control artifactで`PASS_MECHANISM`を維持し、MSIX本体をpublic Release assetにしていない。

### 敵対的レビュー

採用:
- 初回レビューで、dirty sourceやsentinel cleanup失敗時に前runの`PASS_REQUIRED` evidenceが残り得るHigh findingを確認した。既存evidence削除をclean-source gateより前へ移し、atomic evidence commitをsentinel cleanup確認より後へ移した。
- `WindowsInstallerPackageTests`へ「evidence削除 < clean gate < sentinel cleanup < evidence commit」の順序と、CI分離／conditional exact artifact pathを固定するstatic contractを追加した。
- synthetic stale evidenceを置いたdirty checkoutでdriverを実行し、exit 1、stale evidence不存在を実測した。

棄却:
- 例外表示の行折り返しに依存する全文文字列照合はproduct contractではない。fail-closed判定は非zero exitとevidence不存在で確認した。
- GitHub Actions artifact uploadをpublic GitHub Releaseとみなす解釈は誤り。R2-01はsecret-free CI control artifactだけを生成し、公開は後続R2-02／03の別境界で行う。
- 初回hosted失敗を受けて「ローカルでもdirty treeのままclean gateを迂回して全経路を実行する」案は採用しない。clean-source要求はevidenceの前提そのものであり、迂回はevidenceの意味を失わせる。checkpoint commitでworktreeをcleanにしてから実行する運用へ変更した。

### 反映確認

- Windows ZIP package 3件＋Windows installer/static contract 6件、aggregate 9/9 PASS、failed/skipped 0。
- workflow／PowerShell／C# diagnostics 0件、`git diff --check` exit 0。
- post-fix read-only adversarial reviewはstale evidence、exactly-once実行、exact 3-file conditional upload、MSIX非公開境界、static contractを再確認し、unresolved Critical/High 0。
- 初回hosted CI run [`33837919361`](https://github.com/dahatake/StudyReport-Evaluator/actions/runs/33837919361)はZIP stepで失敗した。原因は`Assert-SafeArchiveEntry`の`$Seen`が空`HashSet`で`[Parameter(Mandatory)]`にbindできない実装欠陥である。最小再現で`Cannot bind argument to parameter 'Seen' because it is an empty collection.`を再現し、`[AllowEmptyCollection()]`追加で解消することを確認して修正し、順序をstatic contractへ固定した。dirty local treeではclean-source gateで早期終了するため、この経路はhosted CIまで到達していなかった。
- 修正後はcommit `efc2aee...`のclean worktreeでdriverをローカル完全実行し、exit 0、ZIP 156,214,400 bytes、sidecar exact bytes一致、evidence `PASS_REQUIRED`／sourceCommit一致／checks 8件true、実行後もworktree cleanを実測した。
- 同一SHAのhosted CI run [`33844318251`](https://github.com/dahatake/StudyReport-Evaluator/actions/runs/33844318251)は`success`。Windows／macOS 15／macOS 15 Intelの3/3 jobと全stepが成功した。
- artifactは5件、全12 fileがnonempty、zero-byte 0、private-looking filename 0。ZIP artifactはZIP／sidecar／evidenceのexact 3 file、MSIX artifactはMSIX本体を含まないsidecar／evidenceの2 fileで`PASS_MECHANISM`かつ`PASS_PRODUCTION`不在だった。
- content data included = false。

**Status: PASS**

## 27. R2-02 — draft-only Windows ZIP candidate workflow

### 実行結果

- `scripts/build-platform-release-matrix.ps1`を追加した。ZIP 3 fileとdevelopment MSIX 3 fileを単一のmatrix directoryへ集約し、実bytes/SHA-256からmatrixを生成して`scripts/validate-platform-release-matrix.ps1`で自己検証する。
- writerはclean source、両evidenceのsourceCommit一致、両evidenceが同一Windows hostで測定されたことを検証し、失敗時はmatrixを残さない。
- `.github/workflows/release.yml`を`Release candidate`へ改版した。`draft` inputを削除し、stable annotated tagだけを受理する。
- tag/annotated/clean checkout/version tool/CHANGELOG dated sectionを事前検証し、deterministic tests、Windows ZIP regression、published binary version、development MSIX mechanism、matrixの順で検証する。
- Actions control artifactへmatrixと2 evidenceを保存し、公開asset用artifactはZIPとsidecarだけとする。
- GitHub Releaseは常に`--draft --verify-tag`で作成し、作成直後にdraft状態とexact 2 assetを再確認する。`actions/setup-dotnet`はv6へ更新した。

### 敵対的レビュー

採用:
- 旧workflowは`draft=false`入力で即時公開でき、F-010に該当したため入力自体を削除した。
- 旧workflowはprerelease tagを受理しながら`--prerelease`を付けずstable Releaseを作り得たため、tag regexをstable-onlyへ固定した。
- 旧workflowは`package-windows.ps1`を直接呼び、R2-01のsafe layout／bundled CLI／sentinel検証を経由していなかったため、検証済みdriverへ統一した。
- CHANGELOG dated sectionの不足をRelease作成時点ではなく識別step時点で検出するようにした。

棄却:
- development MSIXをdraft assetへ含める案は、unsigned executable MSIXを一般配布しないという§0.1決定に反するため採用しない。control artifactに限定する。
- matrixをRelease assetへ添付する案は要求にない。publish workflowはActions artifactから取得できるため追加しない。

### 反映確認

- workflow／script diagnostics 0件。
- 実tag不存在のためworkflowはdispatchしていない。実行はV2-04で行う。
- content data included = false。

**Status: PASS_STATIC_PENDING_RELEASE_TAG**

## 28. R2-03 — protected publish workflowとcontract tests

### 実行結果

- `.github/workflows/publish-release.yml`を追加した。入力はexisting draft tagとcandidate run IDだけで、`environment: publish`のapproval後に実行される。
- candidate runのconclusionとhead SHAがtag commitと一致することを確認してからcontrol artifactをdownloadする。
- matrixのsourceCommit／productVersion、`publish=true`の1行が`PASS_REQUIRED`、`publish=false`の1行が`PASS_MECHANISM`であることを検証する。
- draftからassetをfresh downloadし、exact asset set、bytes、SHA-256、sidecarのexact bytesをmatrixへ照合する。
- 最終write stepは`gh release edit TAG --draft=false`だけで、artifact生成・置換・`--clobber`・tag作成・Release削除を行わない。
- GitHub Environment `publish`をrequired reviewers付きで作成した。
- `ReleaseWorkflowContractTests.cs`を追加し、candidate 3件、publish 3件、matrix writer 1件の計7 testでこれらの境界を固定した。

### 敵対的レビュー

採用:
- 公開前検証と公開の順序が入れ替わる回帰を防ぐため、asset照合の位置が`--draft=false`より前であることと、`--draft=false`がfile内で1箇所だけであることをtestで固定した。
- candidate workflow側にも`gh release edit`と`--draft=false`が現れないことをtestで固定し、公開能力を1 workflowへ限定した。

棄却:
- publish workflowで再度package buildを行う案は、draft assetと異なるbytesを生む可能性があり、公開対象の同一性を弱めるため採用しない。buildしないことをtestで固定した。
- signing environmentの分離は現版scopeにproduction signingが存在しないため作成しない。Q-010の2環境案のうちpublish側だけを実装する。

### 反映確認

- workflow／test diagnostics 0件。
- `ReleaseWorkflowContractTests`、`ReleaseMatrixContractTests`、`WindowsInstallerPackageTests`のaggregate 22/22 PASS、failed/skipped 0。
- GitHub Environment `publish`の`protection_rules`に`required_reviewers`が設定されていることを実測した。
- draft不存在のためworkflowはdispatchしていない。実行はV2-04で行う。
- content data included = false。

**Status: PASS_STATIC_PENDING_DRAFT**

## 29. V2-01 — current candidateのfull required gate

### 実行結果

- optional smokeを全て`0`に固定し、`SampleWorkbookStructuralTests`だけを除外したfull required deterministic gateを実行した。

| 検証 | 実測結果 |
|---|---|
| Release build | exit 0、warning 0、error 0 |
| Core tests | 190/190 PASS |
| App tests | 506/506 PASS（Windows ZIP package 3件を含む、16 m 6 s） |
| 合計 | 696/696 PASS、failed 0、skipped 0 |
| version self-test | 15 assertions PASS |
| locked restore | exit 0 |
| `git diff --check` | exit 0 |

- Live Copilot、external recalculation、RealData system smokeはrequired gateへ算入していない。

### 敵対的レビュー

採用:
- 初回実行はversion self-testの期待値hard-codeで失敗した。V2-03のbumpに追随できない設計欠陥であるため、期待値を`Directory.Build.props`のXMLから独立して読み、stable semverであることも検証する方式へ変更した。assertionは14から15になった。
- 2回目の実行はtest DLLが再buildされておらず旧`0.8.0`期待値で失敗した。残存`testhost`／test host processがDLLをlockしていたためで、processを停止して再buildした。lockが原因のbuild失敗を成功と誤認しないよう、build後にDLLとsourceのtimestamp順序を確認した。

棄却:
- `ReleaseMatrixContractTests`のfixture `ProductVersion = "0.8.0"`をbumpに追随させる指摘は不要。validatorは`-ExpectedProductVersion`を明示的に受け取り、fixture内で自己完結するため版非依存である。
- dirty treeのまま`--no-build`で結果を採用する運用は、source変更が反映されない実失敗を起こしたため採用しない。

### 反映確認

- 696/696 PASS、build warning/error 0、diff-check exit 0。
- content data included = false。

**Status: PASS**

## 30. V2-02 — CHANGELOGをv4.3実態へ同期

### 実行結果

- `[Unreleased]`のdelivery記述をv4.3のscopeへ同期した。
- Windows公開物をself-contained ZIPとSHA-256 sidecarとし、draft作成とprotected publishの分離を追加した。
- 「MSIXとDMGを公開するpipelineを追加中」「WindowsはMSIXのInstall、macOSはDMG配置」という旧v4.2記述を、development MSIXの非公開化とmacOS現版scope外へ置換した。
- 要求参照をv4.2からv4.3へ更新した。

### 敵対的レビュー

- 公開済みと誤読させる断定、未実測platformの対応済み表示、存在しないasset URLは確認されなかった。
- 過去の再baseline経緯はhistorical valueとして保持し、現在の提供内容と混同しない表現へ限定した。

### 反映確認

- `DocumentationContractTests`を含むfull gateで696/696 PASS。
- content data included = false。

**Status: PASS**

## 31. V2-03 — PATCH bumpと版・lock・package再検証

### 実行結果

- `dev/version.ps1 bump -Part patch`で`0.8.0`→`0.8.1`へ更新した。`Changed=true`、`Status=PASS`。
- `--force-evaluate`と`--locked-mode`のrestoreを順に実行し、両方exit 0。lock差分はApp tests lockのproject dependencyだけだった。
- `CHANGELOG.md`へ`## [0.8.1] - 2026-09-04`を作成し、空の`[Unreleased]`を先頭に残した。
- current版記述をREADME、developer index、implementation status、version management、ADR-0015、documentation contract testで`0.8.1`へ同期した。
- `dev/version.ps1 verify`はApp/Core 2 projectとも`0.8.1`で`PASS`。

### 敵対的レビュー

採用:
- version self-testの`0.8.0` hard-codeを正本XMLから読む方式へ変更した。同種のdriftはB1-05でMSIX driverに対して既に除去済みであり、self-testだけが残存していた。

棄却:
- `version-management.md`のprerelease例`0.8.0-rc.1`やtag例を一括置換する案は、手順の汎用例示であり現在の製品版を断定していないため変更しない。
- ADR-0014や過去work記録の`0.8.0`はhistorical decisionの記録であり書き換えない。

### 反映確認

- 版`0.8.1`、locked restore exit 0、full gate 696/696 PASS、diff-check exit 0。
- content data included = false。

**Status: PASS**

## 32. V2-04 — release commit、tag、draft作成とpublish引き継ぎ

### 実行結果

- release commit `d0b03b9201d397b6c3333dafbb816b13b4dc003c`を作成し、専用branchから`main`へfast-forward mergeしてpushした。merge baseは`1fdc9ab...`で、main側の先行commitは0だった。
- main CI run [`33847521532`](https://github.com/dahatake/StudyReport-Evaluator/actions/runs/33847521532)はexact SHAで`success`。Windows、macOS 15、macOS 15 Intelの3/3 jobと全stepが成功し、artifactは5件だった。
- annotated tag `v0.8.1`を同commitへ作成し、`dev/version.ps1 verify -Tag v0.8.1 -RequireClean`が`PASS`、`Annotated=True`であることを確認してからpushした。既存`v1.0.0`は変更していない。
- candidate workflow run [`33848620387`](https://github.com/dahatake/StudyReport-Evaluator/actions/runs/33848620387)は`success`。identity検証、deterministic tests、Windows ZIP regression、published binary version、development MSIX mechanism、matrix生成、control artifact、draft作成の全stepが成功した。
- draft Release `v0.8.1`が作成され、`isDraft=true`、assetはZIPとsidecarのexact 2件だった。

### draft検証（fresh download）

| 項目 | 実測値 |
|---|---|
| Draft asset集合 | `StudyReportEvaluator-win-x64.zip`、`StudyReportEvaluator-win-x64.zip.sha256`のみ |
| ZIP bytes | 156,214,373 |
| ZIP SHA-256 | `75F030CBE8979E0EE814041C985225CE785ECC138BFADA6BF8F2BBB86F545F1C` |
| Sidecar exact bytes | 一致 |
| Matrix | schemaVersion 1、productVersion `0.8.1`、sourceCommit `d0b03b9...`、rows 2 |
| `windows-zip` row | `publish=true`、`PASS_REQUIRED`、artifact hash/bytes一致、sidecar hash一致 |
| `windows-development-msix` row | `publish=false`、`PASS_MECHANISM`、非公開 |
| ZIP evidence | `windows-zip-required`／`PASS_REQUIRED`／commit・version一致 |
| MSIX evidence | `PASS_MECHANISM`／`BLOCKED_EXTERNAL`／install未実行 |
| `PASS_PRODUCTION`出現 | 0件 |

一時downloadは全て削除した。

### 敵対的レビュー

採用:
- `version.ps1 verify -Tag`は既存annotated tagを前提とするため、tag作成前の事前検証では必ず失敗する。tag作成直後に同じ検証を実行して`PASS`を確認する順序へ修正した。
- draft assetはworkflow内の確認だけで信用せず、fresh downloadでbytes/hash/sidecarをmatrixへ再照合した。

棄却:
- draftのZIP hashがR2-01でローカル生成したZIPと異なる点をdefectとする指摘は誤り。両者は異なるsource commit（`efc2aee...`と`d0b03b9...`）から生成されており、各evidenceとmatrixは自分のcommitに対して整合している。

### 未実行と引き継ぎ

- **公開（publish）は実行していない。** `v0.8.1`はdraftのままである。
- protected publish workflow run [`33854574594`](https://github.com/dahatake/StudyReport-Evaluator/actions/runs/33854574594)をdispatch済みで、`status=waiting`のまま`publish` environmentのapprovalを待っている。pending deploymentのreviewerは`dahatake`である。
- 承認を代行しない理由: このrequired reviewer gateはR2-03で「自動化から人間の判断を分離する」ために設計したものであり、実行主体が自ら承認すると保護が無効化する。加えてImmutable Releasesにより公開後はtag／assetを変更・取り消しできない。
- 残手順: GitHub ActionsのRun画面で`Review deployments`から`publish`を承認する。workflowはtag／source／version／CHANGELOG／draft状態／asset集合／hash／matrixを再検証してから`--draft=false`だけを実行する。
- 公開後に残る作業: unauthenticated fresh re-downloadによる最終確認と、実在URLを記載するpublic docs closure（README、`docs/README.md`、`docs/getting-started.md`、C-026）。

### 反映確認

- main／tag／draftの実測値を上表へ記録した。
- content data included = false。

**Status: PASS_DRAFT_READY_PUBLISH_PENDING_APPROVAL**

## 33. V2-05 — 状態文書をR2／V2実測へ同期

### 実行結果

- `implementation-status.md`のsource baselineをrelease commit `d0b03b9...`とtag `v0.8.1`へ、public release statusを`DRAFT_CREATED_UNPUBLISHED`へ更新した。
- full required gate 696/696、version self-test 15 assertions、development MSIX `0.8.1.0`を実測値で記載した。
- delivery statusのWindows ZIP regressionとfull required regressionを`NOT_RUN_CURRENT_CANDIDATE`から`PASS_REQUIRED`へ更新し、canonical technical E2Eはrequired gate外の未実行として維持した。
- `traceability.md`のAC-020／026／027、TR-19／20／28を`PASS_REQUIRED`、AC-028を`PASS_REQUIRED`、TR-29を新語彙`PASS_REQUIRED_EXCEPT_PUBLIC_REDOWNLOAD`とした。
- `readme-claim-ledger.md`のC-036／037／038を`VERIFIED`へ更新し、C-026はdraft段階であることを明記して`BLOCKED`を維持した。

### 敵対的レビュー

採用:
- TR-29を単純な`PASS_REQUIRED`にすると、公開後にしか実行できないunauthenticated re-downloadまで実測済みと誤読される。実測範囲だけを表す語彙を追加し、定義を明記した。
- C-026を`VERIFIED`にする案は、公開済みURLが存在しない現状で虚偽になるため採用しない。draft asset照合済みという事実だけを追記した。

棄却:
- AC-026をdocs journeyの実機操作未実施として`PLANNED`へ戻す指摘は、当該ACのrequired testがZIP journeyとdocs contractであり、両方が実測PASSしているため採用しない。
- canonical technical E2Eを`PASS`扱いにする案は、opt-in E2Eをrequired gateへ昇格しない計画§5.1に反するため採用しない。

### 反映確認

- documentation contract testを含むfocused検証で回帰なし。
- content data included = false。

**Status: PASS**

## 34. V2-06 — protected publishによる公開とclosure

### 実行結果

- 要求所有者が公開を承認したため、`publish` environment（ID 21223204280）のpending deploymentへ`approved`を送信した。
- 初回run [`33854574594`](https://github.com/dahatake/StudyReport-Evaluator/actions/runs/33854574594)はasset検証stepで`failure`となり、`Publish the verified draft release`は`skipped`だった。公開は行われず、fail-closedが機能した。
- 原因は`actions/upload-artifact`が複数pathの共通root（`artifacts/package`）を基準に相対構造を保持するため、control artifact内でmatrixが`matrix/platform-release-matrix.json`に配置されていたことである。publish workflowはartifact直下だけを探していた。
- publish workflowをartifact構造に依存しない再帰検索へ変更し、matrixがちょうど1件であることを要求するようにした。contract testへ同条件を固定した。
- 修正後のrun [`33855507321`](https://github.com/dahatake/StudyReport-Evaluator/actions/runs/33855507321)は承認後に全step成功し、`--draft=false`が実行された。

### 公開結果と認証なし再取得

| 項目 | 実測値 |
|---|---|
| Release | `v0.8.1`、`isDraft=false`、公開時刻 2026-09-04T08:52:49Z |
| Asset集合 | `StudyReportEvaluator-win-x64.zip`、`StudyReportEvaluator-win-x64.zip.sha256`のみ |
| ZIP bytes | 156,214,373（API値と一致） |
| ZIP SHA-256 | `75F030CBE8979E0EE814041C985225CE785ECC138BFADA6BF8F2BBB86F545F1C`（draft検証時と一致） |
| Sidecar exact bytes | 一致 |
| ZIP entry数 | 249、application実行fileあり |
| workbook entry | 0件 |

認証なしの`releases/download/v0.8.1/`から取得して上記を実測し、一時領域は削除した。

### closure

- README、`docs/README.md`、`docs/getting-started.md`、`version-management.md`を公開済み表記へ更新した。
- documentation contract testを公開状態へ更新し、testメソッド名も`Platform_and_published_package_claims_...`へ改名した。
- claim ledgerのC-026を`VERIFIED`、implementation statusを`PUBLISHED`、traceabilityのTR-29を`PASS_REQUIRED`へ更新し、一時的に追加した`PASS_REQUIRED_EXCEPT_PUBLIC_REDOWNLOAD`語彙を削除した。

### 敵対的レビュー

採用:
- ローカルのdraft検証で`Get-ChildItem -Recurse -Filter`を使ったため、artifactの相対構造の不一致を検出できなかった。artifact検証では相対path構造そのものを確認する必要がある。
- 承認はrunごとに必要であり、workflow修正後の再実行では再度承認が要る。

棄却:
- READMEへversioned direct download URLを記載する案は採用しない。次版公開時にstaleとなり、F-005で実際に404を案内した経緯がある。Releases indexのみを案内し、testで`/releases/download/`の不在を固定した。
- 公開失敗時にdraftを削除して作り直す案は採用しない。draftは保持され、workflow修正だけで同じassetを公開できた。

### 反映確認

- documentation contract 14/14、workflow contract 7/7、aggregate 21/21 PASS。
- `git diff --check` exit 0。
- content data included = false。

**Status: PASS**
