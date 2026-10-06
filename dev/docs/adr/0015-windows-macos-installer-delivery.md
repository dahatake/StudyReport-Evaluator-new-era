# ADR-0015: Platform delivery foundationと初回公開境界

| 項目 | 内容 |
|---|---|
| 状態 | **一部廃止**（2026-10-06。Windows以外の配布基盤と非公開の開発用package検証を廃止） |
| 決定日 | 2026-09-03 |
| 廃止日 | 2026-10-06 |
| 要求正本 | `docs/requirements-definition.md` v4.3（廃止要求: FR-059、FR-060） |
| 製品版 | `0.8.1` candidate（2026-09-04、初回公開前に再baselineしPATCHを1つ進めた） |
| Supersedes | ADR-0013の将来platform target。既存Windows ZIPの実測記録は保持 |
| Superseded by | Windows 11 x64の公開境界は[ADR-0013](0013-windows-only-public-release.md)と[ADR-0016](0016-windows-one-action-startup.md)が正本 |
| Carries forward | ADR-0012の機能契約、ADR-0014のSemVer/release identity、入力不変・privacy・2-project構成 |

> 2026-10-06更新: 開発用 MSIX と macOS の基盤は 2026-10-06 に廃止した。packaging script、manifest／entitlement、専用tool project、静的契約test、CI job、release matrixの行を削除し、release matrix v2はWindows x64の単一EXEとZIPの2行だけに限定した。

## Context

2026-09-03時点で実装・検証済みの配布経路はWindows 11 x64向け.NET 10 self-contained unsigned ZIPとSHA-256 sidecarだけだった。要求所有者は当初Windows以外を含むsetup簡素化を指示したが、2026-09-04にscopeを改版し、外部production入力なしで初回公開を進めることにした。

.NET self-contained publishは対象端末への.NET runtime事前installを不要にするが、RID別artifactとOS native dependencyの検証は必要である。[Microsoft Learn: .NET application publishing](https://learn.microsoft.com/dotnet/core/deploying/)、[RID catalog](https://learn.microsoft.com/dotnet/core/rid-catalog)

## Decision（2026-10-06時点で有効な部分）

1. 初回公開前の製品candidateを`0.8.0`、要求文書をv4.3とする。初回公開後に対応platformや配布形式を後方互換追加する場合はSemVer MINORを適用する。[ADR-0014](0014-product-versioning.md)、[`version-management.md`](../version-management.md)
2. Windowsの初回public artifactはGitHub Releasesから直接配布する`StudyReportEvaluator-win-x64.zip`とSHA-256 sidecarとする。Microsoft Store submissionは追加しない。単一EXEの追加はADR-0016で決定した。
3. Windows ZIPはunsigned、non-installer、SmartScreen reputation非保証を明示する。
4. Linux、Windows Arm64、macOS、Mac App Store、Microsoft Storeは対応対象外とする。frameworkのcross-platform supportだけで対応済みと表示しない。

旧Decision 3〜9（非公開の開発用package検証と、Windows以外のsource foundation）は2026-10-06に廃止し、実装も削除した。

## Artifact contract

| Platform | Artifact | Required evidence |
|---|---|---|
| Windows 11 x64 public | `StudyReportEvaluator-win-x64.zip` + `.sha256` | `PASS_REQUIRED`。self-contained、safe layout、version、package hash、clean extract/launch、bundled CLI identity |

単一EXEと`.sha256`はADR-0016の契約に従う。release matrix v2はEXE行とZIP行の2行からなる閉じた集合で、未知・重複・欠落行を拒否する。

## Acceptance vocabulary

| Status | 意味 |
|---|---|
| `PASS_REQUIRED` | OS非依存の決定的code/testが成功 |
| `PASS_PRODUCTION` | production trust pathとclean target OSで成功 |
| `BLOCKED_EXTERNAL` | signing identity、credential、実機等の外部入力不足 |
| `NOT_RUN` | 未実行。PASSへ変換しない |
| `FAIL` | 必須結果と不一致 |

## Consequences

- 外部production signing入力なしで、検証済みWindows ZIPを初回公開できる。
- .NET、Copilot CLI、Officeの別installを要求しない既存契約を維持できる。
- Windows ZIPはinstaller、Publisher identity、SmartScreen reputationを提供しない。
- Windows 11 x64以外の配布物は提供しない。

## Rejected alternatives

### End-user `setup.ps1`だけを提供する

PowerShell 7+導入、execution policy、terminal操作が追加され、非技術利用者のsetup簡素化にならないためprimary pathには採用しない。

### frameworkのcross-platform supportだけで対応platformを広げる

本製品のbundle、CLI、filesystem、signing、launchを証明しないため採用しない。

## Result

**APPROVED — V4.3 SCOPE（2026-10-06に一部廃止）.** 初回public artifactはWindows 11 x64 unsigned ZIPとsidecar。未実測platformや廃止した配布形式を公開済み・対応済みと表示しない。
