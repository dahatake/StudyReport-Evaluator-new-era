# ADR-0014: 製品SemVerとrelease identityを明示管理する

| 項目 | 内容 |
|---|---|
| 状態 | **採用** |
| 決定日 | 2026-09-02 |
| 対象 | StudyReport Evaluatorの製品版、assembly、checkpoint、配布候補、tag、release |
| 手順正本 | [`version-management.md`](../version-management.md) |
| 版操作tool | [`dev/version.ps1`](../../version.ps1) |
| 調査 | [`20260902-version-management-investigation-report.md`](../archive/work/20260902-version-management-investigation-report.md) |

> 2026-09-04更新: 公開済み製品版・配布assetがない段階で、製品所有者の指示によりsource candidateを`0.8.0`へ再baselineした。公開済みidentityの変更ではなく、SemVerとrelease identityの管理原則は維持する。

## Context

導入前のprojectには明示的な`Version`、`VersionPrefix`、`VersionSuffix`がなく、.NET SDKの既定値から実効`1.0.0`が生成されていた。assemblyとRun/checkpoint identityにはその値が流れる一方、製品版の正本、bump規則、tag/release手順はなかった。[調査証拠](../archive/work/20260902-version-management-investigation-evidence.json)

要求文書版`4.1`、definition schema`4.0`、checkpoint schema`1`、Copilot runtime manifest schema`1`、dependency versionは異なる契約であり、製品版として相互に読み替えることはできない。

## Decision

1. 製品版は[Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html)を使用する。
2. [`Directory.Build.props`](../../../Directory.Build.props)の`VersionPrefix`と`VersionSuffix`を唯一のsource-controlled製品版正本とする。
3. 機構導入時の初期値は、導入前の実効値を変えない`1.0.0`とした。公開前の2026-09-04にsource candidateを`0.8.0`へ再baselineした。いずれの値も、それだけで公開済みとは判定せず、公開にはpassing gate、annotated tag、GitHub Releaseを別途要求する。
4. `VersionPrefix`は`MAJOR.MINOR.PATCH`、`VersionSuffix`は任意のprereleaseとし、build metadataは手入力しない。.NET SDKがsource revisionをInformationalVersionへ追加する。
5. 版変更は[`dev/version.ps1`](../../version.ps1)で表示、設定、bump、検証できる。toolはCHANGELOG、commit、tag、Releaseを自動作成しない。
6. 公開互換性はCLI、workbook入出力、checkpoint、配布platform/layout、privacy boundary、利用者文書を含む。
7. 要求、definition、checkpoint、Copilot manifest、Prompt template、dependencyの各versionは製品SemVerから独立させる。
8. Git tagは`v<SemVer>`のannotated tagとし、source版、CHANGELOG版、HEADをrelease前に完全一致検証する。
9. 公開済みtagとassetを差し替えず、修正は新しい製品版で公開する。
10. 現行ZIP名`StudyReportEvaluator-win-x64.zip`は維持し、release identityはGitHub Release tag、release notes、DLL metadata、SHA-256の組で確定する。将来asset名をversion入りへ変更する場合はscript、test、README、入手手順を同じ変更単位で更新する。
11. 現在のcheckpoint application-version parserはSemVer prereleaseを同一majorとして解析できない。prerelease間resumeを必要とする場合は、SemVer-aware判定と互換testを先に実装する。
12. repositoryにCI/release workflowが存在しない間は、Windows 11 x64上で現行のmanual final gateを必須とする。

## Consequences

### Positive

- 暗黙のSDK既定値ではなく、review可能な1箇所からApp/Core版が決まる。
- toolによりinvalid SemVer、project drift、published DLL drift、CHANGELOG/tag/HEAD driftをrelease前に拒否できる。
- 製品版とschema/component版の誤った同時更新を防げる。
- 公開版の内容を後から差し替えない運用を明文化できる。

### Trade-offs

- version toolだけではbuild/test/package/GitHub Releaseを完了しない。人による変更分類とrelease notes reviewが必要である。
- unversioned asset名を維持するため、download file名単体では製品版を識別できない。必ずRelease tag、binary metadata、hashと組み合わせる。
- prerelease間checkpoint compatibilityは、現在のapplication codeを変更するまで保証しない。
- CI automationはこのdecisionだけでは追加されない。

## Rejected alternatives

### SDK既定`1.0.0`を暗黙のまま使う

版の所在と意図をsource reviewできず、意図しないrelease driftを検出できないため採用しない。

### 要求文書版またはschema版を製品版にする

更新理由と互換性境界が異なるため採用しない。

### toolがcommit/tag/Releaseまで自動作成する

working treeに他taskの変更がある場合の誤包含、release notes未review、credential/remote操作を避けるため採用しない。toolはlocalな版変更とfail-closed検証に限定する。

### 初期導入と同時にasset名を変更する

既存README、package script/test、利用者手順の公開契約変更を版正本導入へ混ぜないため見送る。別changeでversion入りassetへ移行できる。

## Validation

- [`dev/version.tests.ps1`](../../version.tests.ps1)でshow、verify、set、bump、dry-run、invalid version拒否を一時copy上で検証する。
- `version.ps1 verify`でApp/CoreのMSBuild版とCHANGELOG baselineを検証する。
- publish後は`-PublishedDirectory`でApp/Core DLLのAssembly/File/Product versionを検証する。
- release commit/tag作成後は`-Tag vX.Y.Z -RequireClean`でCHANGELOG、annotated tag、HEAD、clean treeを検証する。
- package layout、SHA-256、bundled CLI、clean launchは既存[`WindowsPublishPackageTests.cs`](../../../tests/StudyReportEvaluator.App.Tests/Packaging/WindowsPublishPackageTests.cs)を正本とする。

## Source

- 2026-09-02のrepository所有者指示: アプリケーション版管理の開発者文書と必要なtoolを作成し、開発資材を`dev/`へ集約する。
- [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html)
- [Microsoft Learn: assembly attributes](https://learn.microsoft.com/dotnet/standard/assembly/set-attributes-project-file)
- [Microsoft Learn: Source Link](https://learn.microsoft.com/dotnet/core/compatibility/sdk/8.0/source-link)
- [Pro Git: Tagging](https://git-scm.com/book/en/v2/Git-Basics-Tagging)
- [GitHub Docs: Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)
