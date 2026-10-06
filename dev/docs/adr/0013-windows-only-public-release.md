# ADR-0013: 初版正式公開をWindows 11 x64へ限定

| 項目 | 内容 |
|---|---|
| 状態 | **承認済み** |
| 決定日 | 2026-09-02 |
| 要求正本 | `docs/requirements-definition.md` v4.1 |
| Supersedes | ADR-0012 §13のplatform配布、およびmacOSを初版required scopeとする箇所 |
| Carries forward | ADR-0012の配点、AI operation、formula、checkpoint、Prompt起動、bundled CLI、2-project構成 |

## Context

初版正式公開READMEの実装計画は、実装済みかつ検証可能なplatformだけを対応環境として記載し、未実測platformを保証しないことを要求する。

2026-09-02時点でrepositoryに存在し、実行できる配布経路は次のとおりである。

- `scripts/publish-windows.ps1`: Windows 11 x64、.NET 10 self-contained、RID `win-x64`
- `scripts/package-windows.ps1`: unsigned ZIPとSHA-256 sidecar
- `tests/StudyReportEvaluator.App.Tests/Packaging/WindowsPublishPackageTests.cs`: publish、package、展開、clean launch、bundled CLI resolver、再現可能ZIP

一方、macOS用publish/package/install/sign/notary script、macOS runner workflow、Developer ID署名・notarization・launch証跡はrepositoryに存在しない。これらを未実装のまま対応済みと表示することはできない。

## Decision

1. 初版正式公開の対応環境は**Windows 11 x64だけ**とする。
2. 配布形態は.NET 10 self-containedの**unsigned ZIP**とSHA-256 sidecarとする。
3. GitHub Copilot SDK互換CLIはZIPへ同梱し、manifest、RID、version、SHA-256を検証する。PATHへfallbackしない。
4. macOS、Linux、Windows Arm64、installer、code signing、notarizationは初版の対応・required acceptance・release gateに含めない。
5. macOS等を将来scopeへ戻す場合は、要求改版、owner、platform実機またはrunner、RID別package、署名・notarizationを含むrequired evidenceを新たに定義する。現在のWindows証跡を代用しない。
6. ADR-0012のplatform以外の決定は変更しない。

## Evidence boundary

この決定はWindows以外でアプリが動作不能であることを一般化しない。正式公開として検証・保証するplatformをWindows 11 x64へ限定する。Avaloniaや.NET SDKが他platform assetを持つことは、本製品のpackage・launch・署名・運用証跡の代替ではない。

## Consequences

### Positive

- README、要求、package、testの対応platformが一致する。
- 存在しないmacOS packageや署名結果を利用者へ案内しない。
- release gateをこのWindows環境で決定的に再実行できる。

### Trade-offs

- macOS利用者向け正式packageは提供しない。
- unsigned ZIPの発行者identityやSmartScreen reputationを保証しない。
- installerを提供しないため、利用者はZIPを展開して起動する。

## Approval record

| 項目 | 値 |
|---|---|
| Source | 2026-09-02のREADME正式公開実装指示、および`dev/docs/archive/work/20260902-readme-end-user-release-plan.md` B-02 |
| Applied default | 実package・署名・runner証跡のあるplatformだけを正式対応とする分岐 |
| Identity semantics | repositoryのrelease-scope決定であり、組織の法務・教育・security承認または電子署名ではない |

## Result

**APPROVED.** 要求v4.1、architecture、詳細設計、traceability、system-test契約、利用者文書、package testをWindows 11 x64初版scopeへ同期する。
