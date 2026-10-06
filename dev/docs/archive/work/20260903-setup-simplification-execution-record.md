# Windows / macOS セットアップ簡素化 実行記録

## 0. 文書管理

| 項目 | 値 |
|---|---|
| 実行計画 | [`20260903-0923-windows-macos-setup-simplification-plan.md`](20260903-0923-windows-macos-setup-simplification-plan.md) |
| 実行開始日 | 2026-09-03 |
| source baseline | `5a1d1ebe5e6dcc5d934f959e58ac2582f9d9c326` |
| 作業 branch | `feature/setup-simplification-20260903` |
| 開始時 status entry 数 | 13 |
| 開始時 status SHA-256 | `80B8C755E206699FF89F29873A61F1970131D763ACA37F41C4603598C4B7B23C` |
| 状態 | `IN_PROGRESS` |
| 捏造防止 | 未実行は `NOT_RUN`、外部入力不足は `BLOCKED_EXTERNAL`、仕組みだけの検証は `PASS_MECHANISM` と記録する |

## 1. 採用する既定値

要求所有者である利用者の指示「不明点はデフォルトのプランを採用」を default decision の承認根拠とし、repository の既存方針と実装計画から次を採用する。署名 identity、秘密、実機結果は生成・推測しない。

| Decision | 採用値 | 根拠 |
|---|---|---|
| 製品版 | `1.1.0` candidate | 対応 platform / 配布形式の追加は [`dev/docs/version-management.md`](../../../../dev/docs/version-management.md) §3.1 で MINOR |
| 要求文書版 | `4.2` | 現行機能契約 v4.1 を維持する delivery / platform scope の後方互換追加として改版 |
| Windows channel | GitHub Releases から direct MSIX | 現行 GitHub Release channelを維持し、計画の MSIX 第一候補を採用 |
| Windows architecture | `win-x64` | 現行検証済み architecture を維持。Windows Arm64 は未実測のため対象外 |
| Windows signing | trusted code-signing certificate を release 必須入力にする | production certificate は repository に存在せず、test certificateを正式証跡にしない |
| macOS package | RID 別 DMG（`osx-arm64` / `osx-x64`） | 計画の第一候補。universal binary を未検証のまま採用しない |
| macOS candidate OS | 14 / 15 / 26 | vendor support matrixから試験候補を作るだけで、実測前に対応表示しない |
| macOS bundle ID | `com.github.dahatake.study-report-evaluator` | repository owner/nameから一意な reverse-DNS defaultを採用。Developer Team IDは推測しない |
| prerelease installer | 作成しない | stableと別 identity / update channelの追加設計を避ける |
| end-user setup script | primary pathにしない | 現行 PowerShell automation は PowerShell 7+ を要求し、installerより利用者前提が増えるため |

## 2. P0-01 — Baseline と既存差分保護

### 2.1 実行結果

| Check | Result |
|---|---|
| `pwsh` | Core 7.6.5 |
| `.NET SDK` | 10.0.400 |
| branch 作成 | `PASS` — `feature/setup-simplification-20260903` |
| HEAD 不変 | `PASS` — `5a1d1ebe5e6dcc5d934f959e58ac2582f9d9c326` |
| stash / reset / clean | 未実行 |
| status fingerprint | `PASS` — 13 entries / SHA-256 上記 |

### 2.2 開始時 file fingerprint

| Status | Path | Bytes | SHA-256 |
|---|---|---:|---|
| M | `CHANGELOG.md` | 1,859 | `483DDFCFDB320BA8E2CCE417BF57D9F06D9A28ECF0B6FE12012190CD4E698CF4` |
| M | `Directory.Build.props` | 645 | `C4F20DE384F07C3BD55C710336EA828DFEE4CA0761476C5B93ED2B079EC83698` |
| M | `README.md` | 9,039 | `D8D7B141FA3E141643272792208F5CE090472EC5A1E0FE16559BD4CABAB81523` |
| M | `dev/docs/README.md` | 5,875 | `6B79D1D113B9834ADE28817885A7BF89F4E81F97402D60AE9EA200204A7A2173` |
| M | `dev/docs/implementation-status.md` | 11,438 | `BC807B76D9B33FC655F3FE7EA8E3A776A736ABE5CC93241D4E05A69E7A78E9FC` |
| M | `dev/docs/version-management.md` | 19,398 | `2DFAF00D00D444D49AAC62F12FA7F17E8B22AB3831F64AD0241AD63E95EA9B4D` |
| M | `dev/version.ps1` | 19,991 | `35889B5C0E19932F661CD7273FD57F26B5290EB0009F3C577B46D0F0C06E5E95` |
| M | `dev/version.tests.ps1` | 8,546 | `DD7177440653605F5F1C333AB9E0B08B214B8B37B4FB7D48DB60B56E8F2B6653` |
| M | `scripts/publish-windows.ps1` | 34,922 | `AC906A425ED1351717E20AB0D6521C0CC891560BC545C8E46579E383593BBFA1` |
| M | `tests/StudyReportEvaluator.App.Tests/Content/DocumentationContractTests.cs` | 27,537 | `105E416732B4A127EF7E784DA1CFCD875A7AEB8E8FBF0568BCF3009F3FC4AD26` |
| M | `tests/StudyReportEvaluator.App.Tests/packages.lock.json` | 17,918 | `768A28E24C309E7A597328D87FD1DAC78E096959698249C714179EFAB2A86721` |
| ?? | `work/20260903-0923-windows-macos-setup-simplification-plan.md` | 44,697 | `2E0BA0B0D293DB02D5C43FC63EF6C452D105640E50D3E1C69337AF0BA629BB76` |
| ?? | `work/20260903-v1.0.1-release-recovery-plan.md` | 5,164 | `670E7DA57CE9D86CCDA5BE5CC754A9ACD9F133C0A7CF54E845AF5C44E7EAFA3F` |

開始時 13 entries は本タスクが上書きしてよい一覧ではない。以後、各変更は既存内容を読んで統合し、既存 release recovery 修正を削除しない。

### 2.3 敵対的レビュー

| Finding | 判定 | 反映 |
|---|---|---|
| [S08] の entry 数が不正確 | `REJECTED` | [S08] は本書作成直前の 11 modified + recovery plan 1件、本書作成後の開始baselineはそれら12件 + setup plan 1件で13件。数は整合している |
| bundle ID が未承認 | `ACCEPTED_AS_CLARIFICATION` | 利用者の default採用指示を要求所有者の承認根拠として§1へ明記。Developer Team IDや署名identityは引き続き未確定 |
| [S08] の `main` と現在branchが不一致 | `REJECTED` | [S08] は作成前historical snapshot。P0-01で専用branchへ移った時系列をplanと本記録へ明記 |

再検証:

- branch: `feature/setup-simplification-20260903`
- HEAD: `5a1d1ebe5e6dcc5d934f959e58ac2582f9d9c326`
- P0-01前から存在した11 modified + recovery planの計12fileは、§2.2のSHA-256と全件一致
- stash / reset / clean: 未実行
- blocker / high finding: 0件

**P0-01 status:** `PASS`

## 3. P0-02 — 要求と decision の改版

### 3.1 実行結果

| 対象 | Result |
|---|---|
| `docs/requirements-definition.md` | `PASS` — v4.2、AC-001〜028 |
| `dev/docs/adr/0015-windows-macos-installer-delivery.md` | `PASS` — `APPROVED — IMPLEMENTATION_IN_PROGRESS` |
| `dev/docs/architecture.md` | `PASS` — target delivery architectureを同期 |
| `dev/docs/detailed-design.md` | `PASS` — MSIX / macOS package、署名・公証、CI境界を同期 |
| `dev/docs/implementation-status.md` | `PASS` — 既存機能の過去PASSと新deliveryの未実装を分離 |
| `dev/docs/traceability.md` | `PASS` — AC-023〜028、TR-25〜29を追加 |
| `dev/docs/readme-claim-ledger.md` | `PASS` — C-033〜038を追加し、未取得証跡をBLOCKEDで保持 |
| `SystemTest-prompt.md` | `PASS` — v4.2、ST-UC-01〜25、TR-01〜29 |
| public `README.md` boundary | `PASS` — Windows unsigned ZIPの現行claimを維持し、MSIX / macOS対応済みへ早期変更していない |

検証結果:

- AC sequence: 1〜28、重複・欠落なし
- claim sequence: 1〜38、重複・欠落なし
- SystemTest heading / Test ID: 1〜25、重複・欠落なし
- SystemTest TR coverage: 1〜29
- 対象文書のlocal Markdown links: 18件、欠落・workspace外escapeなし
- `dev/version.ps1 verify`: `PASS`、version `1.1.0`
- `git diff --check`: `PASS`
- editor diagnostics: 対象8文書すべて0件

最初のSystemTest連番検証は、CRLF行末に対して `^Test ID: ...$` を使用したためTest IDを0件として誤判定した。実データを列挙して01〜25の存在を確認し、検証式を `^Test ID: ...\r?$` へ修正した。文書内容の欠陥ではないため、SystemTest本文は変更していない。

### 3.2 敵対的レビュー

| Finding | 判定 | 反映 |
|---|---|---|
| 実行記録がP0-01で終了し、P0-02の実行結果が記載されていない | `ACCEPTED` | 本節へ対象8文書、公開claim境界、直接検証結果を追記 |

再レビューでは、package format、platform matrix、external input、failure policy、acceptance、traceability、production evidence境界、README current claim、利用者のdefault承認根拠にCritical / High findingはなかった。補助レビューが報告したAC / claim 0件とTR重複は抽出条件・判定条件の誤りであり、直接列挙結果と要件（TRは網羅が条件）により文書findingではないことを確認した。

**P0-02 status:** `PASS`

## 4. P1-01 — OS / filesystem inventory

### 4.1 全走査結果

`src/**/*.cs` 81ファイルを対象に、OS / architecture判定、path比較、file move / replace / flush、process lifecycle、native UI、P/Invoke、実行file名、固定pathを走査した。

| Category | Production symbol / file | 観測した実装 | 判定 / 次の検証 |
|---|---|---|---|
| OS / RID | `BundledCopilotCliPathResolver.GetCurrentRuntimeIdentifier` / `CopilotClientFactory.cs` | Windows / macOSとx64 / Arm64を組み合わせ、`win-x64`、`win-arm64`、`osx-x64`、`osx-arm64`を返す。その他は拒否 | 分岐自体に欠陥は確認されない。macOS native processでRID一致を要検証 |
| CLI file名 | `BundledCopilotCliPathResolver.ResolveAsync` / `CopilotClientFactory.cs` | Windowsは`copilot.exe`、それ以外の対応OSは`copilot`。manifestのRID、relative path、SDK / CLI version、SHA-256を照合し、PATH fallbackしない | macOS用の意図した分岐。signed CLIの最終hashとfile version取得をP1-03で実測 |
| UI platform | `Program.BuildAvaloniaApp` / `Program.cs` | Avalonia `UsePlatformDetect()` | macOS GUI実機検証が必要 |
| path identity | `LaunchOptions`、`ResultsOutputBoundary`、`CheckpointPayloadCodec`、`CheckpointStore`、`OutputPathPlanner`、`WorkingPackage`、`AtomicOutputCommitter`、`DurableQuantificationOrchestrator`、`WorkbookDurableRunFinalizer` | filesystem pathのidentity比較はWindowsで`OrdinalIgnoreCase`、非Windowsで`Ordinal`。拡張子・Open XML識別子等のformat比較に使う`OrdinalIgnoreCase`とは区別されている | source上の逆転や固定Windows比較は確認されない。macOSのcase-sensitive / case-insensitive volume双方で実測が必要 |
| checkpoint create / update | `PhysicalCheckpointFileOperations` / `CheckpointStore.cs` | target-local tempへcopy/write、`Flush(true)`、構造・hash・input再検証。新規は`File.Move(..., overwrite:false)`、更新は`File.Replace(..., backup:null, ignoreMetadataErrors:true)` | `File.Replace`を含む成功・例外・process killをmacOS実filesystemで要検証 |
| final output commit | `PhysicalAtomicOutputFileOperations` / `AtomicOutputCommitter.cs` | target-local working fileを`Flush(true)`後に再検証し、cancellation deferred区間で`File.Move(..., overwrite:false)`。移動後のtemp/final存在も確認 | macOSでatomic rename、target race、disk full、locked file、killを要検証 |
| working copy | `WorkingPackage.CopyAndFlushToUniqueTarget` / `WorkingPackage.cs` | `CreateNew`、`FileShare.None`、`WriteThrough`、`Flush(true)`、同一directoryのunique temp | APFS上のmode / flush / cleanupを要検証 |
| process | `CopilotClientFactory.BuildOptions`、`SdkEphemeralCopilotTransport`、`SdkCopilotAuthenticationRuntime` | production codeに直接の`Process.Start` / `ProcessStartInfo`はない。SDKへabsolute bundled CLI pathを渡し、Start / Stop / Disposeをwrapperが管理 | SDK内部processのstart、cancel、stop、dispose、orphanなしをmacOS実機で要検証 |
| native picker | `NativeInputWorkbookPicker.PickAsync` / `InputView.axaml.cs` | Avalonia `OpenFilePickerAsync`、`.xlsx` pattern / MIME / Apple UTI、`TryGetLocalPath`。local pathがない場合は明示エラー | macOS pickerで選択・cancel・local path取得を要検証 |
| shell integration | 全`src/**/*.cs` | output directoryをExplorer / Finderで開く機能、clipboard APIは検出されない | 現行機能として検証対象なし |
| native interop | 全`src/**/*.cs` | `DllImport` / `LibraryImport`は検出されない | 直接P/Invokeなし |
| fixed paths | 全`src/**/*.cs` | Windows絶対path、drive letter、backslash固定によるproduction path構築は検出されない | `Path` API中心 |

既存testにはpath/file-operationのinjected failure testとWindows E2Eがあるが、macOS filesystem / GUI / bundled CLI processの実測証跡はない。Windows上の走査結果からAPFSやmacOS SDK挙動を推測してPASSにはしない。

### 4.2 敵対的レビュー

初回レビューには次の誤findingが含まれたため、sourceを直接読んで棄却した。

| Finding | 判定 | 根拠 |
|---|---|---|
| `copilot.exe` hard-codeによりmacOS startupが必ず失敗する | `REJECTED` | 実装は`OperatingSystem.IsWindows() ? "copilot.exe" : "copilot"`であり、macOSでは`copilot`を選ぶ |
| `OutputPathPlanner`のcase比較がWindowsで別path、macOSで同一pathとして逆転する | `REJECTED` | 実装はWindows `OrdinalIgnoreCase`、非Windows `Ordinal`で、記載が実装と逆 |
| `File.Replace` / `Flush(true)`のAPFS保証度やfailure modeが確定している | `REJECTED` | macOS実測も根拠となるplatform証跡もなく、現時点では未確認 |

直接再走査後に、production source上の確定したcross-platform blockerは0件。macOS実filesystem fault injectionはmacOS hostがないため`BLOCKED_EXTERNAL`であり、P1-01全体をproduction検証済みにはしない。

**P1-01 inventory status:** `PASS`

**P1-01 macOS filesystem evidence:** `BLOCKED_EXTERNAL` — macOS x64 / ARM64 host未提供

## 5. P1-02 — macOS RID publish foundation

### 5.1 実装結果

| 対象 | Result |
|---|---|
| `scripts/publish-macos.sh` | `PASS_MECHANISM` — native同architecture Mac限定、RID別self-contained folder publish、canonical lock保護、一時RID lock検証、必須payload・CLI manifest/hash・thin Mach-O検査、短時間process生存確認を実装 |
| `scripts/package-macos.sh` | `PASS_MECHANISM` — owner-approved `.icns`を必須入力とし、unsigned development `.app` layout、plist/version/executable/bundle ID、execute bitを検査 |
| `scripts/validate-rid-lock.cs` | `PASS_MECHANISM` — TFM graph完全一致、要求RID必須、restoreが生成した全追加RID targetのpackage identityをcanonical lockへ照合 |
| `eng/packaging/macos/Info.plist` | `PASS_MECHANISM` — approved default bundle ID、executable、icon、minimum macOS 14、version placeholderを定義 |
| `eng/packaging/macos/StudyReportEvaluator.entitlements` | `PASS_MECHANISM` — P3-02実測前に未確認権限を追加しない空dict |
| public `README.md` boundary | `PASS` — macOS対応済みclaimへ変更していない |

一時`osx-x64` restoreでは、Core lock targetは`net10.0`と`net10.0/osx-x64`、App lock targetはそれらに`net10.0/win-x64`を加えた3件だった。追加Windows targetはstale `obj`ではなくrestoreが再生成するpackage graphであるため、「target数は常に2件」というvalidator前提を廃止した。要求RIDの存在は必須のまま、追加targetを無視せず、その全packageの`type`、`resolved`、`contentHash`をcanonical TFM graphへ照合する。

Windows上で実施したmechanism / layout probe:

- `osx-x64`一時RID restore: `PASS`
- Core validator: `PASS` — required target 0 package / 2 targets
- App validator: `PASS` — required target 8 packages / 3 targets
- required target削除negative fixture: expected failure
- RID package `resolved`改ざんnegative fixture: expected failure
- canonical Core / App lock SHA-256: probe前後で不変
- fixed packageのnative assetとcross-publish layout: `libAvaloniaNative.dylib`、`libHarfBuzzSharp.dylib`、`libSkiaSharp.dylib`を確認
- Git Bash syntax、shell script LF / no BOM、plist XML parse、editor diagnostics、`git diff --check`: `PASS`
- Mach-O判定: cached macOS apphost正例が`PASS`、architecture文字列だけを含むtext負例がexpected failure
- probe生成物と一時lock: cleanup済み

上記cross-publish / cached binary検査はfile layoutとvalidator mechanismの確認に限定し、本製品のmacOS publish、GUI launch、self-contained runtime、CLI processの成功証跡へ昇格しない。

### 5.2 敵対的レビュー

| Finding | 判定 | 反映 |
|---|---|---|
| `file`出力のarchitecture部分一致だけではMach-O executableを証明しない | `ACCEPTED` | `file -b`が`Mach-O 64-bit <expected-arch> executable`で始まるthin executableであることをapphost / CLI双方へ要求 |
| 計画にあるAvalonia native assetsの明示検査がない | `ACCEPTED` | 実lock packageとlayout probeで実在名を確認し、3つのdylibをnonzero required payloadへ追加 |
| canonical lockを変更後に検出するためrollbackが必要 | `REJECTED` | 条件はcanonical lockを変更しないrestoreと変更時のfail-closed。scriptが未承認lock内容を推測してrollbackする方が危険であり、前後hash不変を直接確認済み |
| versionの`sed` injection | `REJECTED` | product versionは`Major.Minor.Patch`、build versionは正整数の正規表現を置換前に必須化しており、delimiter / XML文字を入力できない |
| signed CLI hash更新がない | `REJECTED_AS_WRONG_PHASE` | P1-02はunsigned development publish。署名順序とshipped hashはP1-03 / P3-03で実装し、現時点でproduction成功を表示しない |
| 空entitlementsではhardened runtimeが動かない | `REJECTED_AS_WRONG_PHASE` | P3-02で実測して必要最小権限を決める。未実測の`allow-jit`等をP1-02で追加しない |
| RID-specific package追加をvalidatorが見逃す | `REJECTED` | canonical lookup失敗を明示的にfailし、改ざんnegative fixtureもexpected failureを確認 |
| `File.Replace`、notarization、schema v2等 | `REJECTED_AS_OUT_OF_SCOPE` | P1-01、P1-03、P3の独立gate。Mac実測不足をP1-02の架空の実装成功・失敗へ読み替えない |

反映後のfocused再レビューで、3 native dylib、apphost / CLI thin Mach-O検査、Bash 3.2互換、要求RIDと全追加targetのcanonical照合をsource上で再確認した。採用findingの未反映は0件。

**P1-02 repository foundation status:** `PASS_MECHANISM`

**P1-02 native macOS exit gate:** `BLOCKED_EXTERNAL` — native x64 / ARM64 macOS hostとowner-approved `.icns`未提供。native publish / GUI launch / external .NET非依存は`NOT_RUN`。

## 6. P1-03 — Bundled CLIの署名後integrity foundation

### 6.1 実装結果

| 対象 | Result |
|---|---|
| `scripts/acquire-copilot-cli-macos.sh` | `PASS_MECHANISM` — 固定SDKからCLI version / RID mappingを取得し、official npm exact-version metadataのname / version / SHA-512 SRIを照合してtarballを取得。archive SRI、thin Mach-O architecture、source CLI SHA-256を検査し、atomic outputとsource provenanceを生成 |
| `scripts/publish-macos.sh` | `PASS_MECHANISM` — 検証済みCLIを`CopilotCliBinaryPath`で明示注入し、runtime manifest・source provenance・実CLIのRID / package / version / SDK / hashを照合 |
| `scripts/package-macos.sh` | `PASS_MECHANISM` — verified source provenanceがない旧publishをbundle化しない |
| `scripts/sign-macos.sh` | `PASS_MECHANISM` — unsigned source bundleを保持したcopy上でdylib→CLI→signed CLI hash manifest更新→apphost→outer appの順にDeveloper ID署名。signingで`--deep`を使用せず、outer後にbundle / CLI / apphostをstrict検証し、CLIとmanifest全体の不変性を再検査 |
| runtime resolver | `PASS` — schema v1のclosed manifestとfinal `cliSha256`をそのまま利用し、PATH fallbackなし。既存test 14 / 14 PASS |

取得provenanceは`schemaVersion`、RID、npm package名、npm package version、`dist.integrity`、SDK package version、署名前source CLI SHA-256だけを保持する。credential、token、private pathは含めない。署名後のCLI hashはnested CLI署名直後に既存manifestへ書き戻し、その後apphostとouter appを署名するためschema v2は不要と判断した。

検証結果:

- shell syntax / LF / no BOM、editor diagnostics、`git diff --check`: `PASS`
- 非macOSでacquire / signが副作用なくfail-closed: `PASS`
- MSBuild fixed properties: SDK `1.0.11`、CLI `1.0.79`、`osx-x64 -> darwin-x64`: `PASS`
- `CopilotClientFactoryTests`: 14 / 14 `PASS`
- official `registry.npmjs.org` metadata取得: `BLOCKED_EXTERNAL` — Windows hostのPowerShell、curl、npmの3経路がTLS handshake failure
- configured Visual Studio npm mirror: name / version / tarball / SHA-1 `shasum`のみ取得。SHA-512 `dist.integrity`がないためproduction取得へ使用せず、scriptはdowngradeせずfail-closedにする
- Developer ID nested / outer signing、strict verification、signed resolver handshake: `NOT_RUN`

### 6.2 敵対的レビュー

| Finding | 判定 | 反映 |
|---|---|---|
| outer署名前後でmanifestの`cliSha256`だけでなく全内容不変を証明すべき | `ACCEPTED` | final manifest全体のSHA-256をouter署名前後で比較 |
| outer後にCLI / apphost nested signatureも個別strict verifyすべき | `ACCEPTED` | outer `--deep --strict`に加えてCLI / apphostへ個別`--strict`を追加 |
| dylibにもruntime entitlementsを付与すべき | `REJECTED` | runtime entitlementはexecutableへ適用し、libraryは個別署名のみ。必要性はP3-02実測で決め、未実測権限を追加しない |
| SDK `1.0.11 -> CLI 1.0.79`をscriptへ重複hard-codeすべき | `REJECTED` | fixed lockで取得したSDK package自身のimported propertyを正本とする。重複値はSDK更新時の不整合源になる |
| macOS curlへLinux固定CA bundleを指定すべき | `REJECTED` | macOS system trustを利用するのが正しい。Linux pathのhard-codeはportabilityを壊す |
| outer codesignが暗黙にnested codeを再署名する | `REJECTED` | signingに`--deep`はなく、nested codeは先に個別署名。outer後のCLI hashと個別signatureも再検査する |

反映後のfocused再レビューで、exact package/version/RID、SHA-512 SRI、source provenance、明示CLI path、unsigned三者hash一致、nested signing順、signed hash更新、outer後不変、strict verificationをsource上で再確認し、未反映findingは0件。

**P1-03 repository foundation status:** `PASS_MECHANISM`

**P1-03 production exit gate:** `BLOCKED_EXTERNAL` — native macOS、official npm registry接続、Developer ID Application identity未提供。source provenance、signed payload hash、bundle signature、runtime resolver handshakeが同一artifactで一致するproduction結果は`NOT_RUN`。

## 7. P2-01 — Windows MSIX mechanism PoC

### 7.1 実装結果

| 対象 | Result |
|---|---|
| `eng/packaging/windows/AppxManifest.xml` | `PASS_MECHANISM` — Windows 11 x64、packaged classic app、medium IL、`runFullTrust`、3種logo placeholderを定義 |
| `scripts/package-windows-msix.ps1` | `PASS_MECHANISM` — test certificate用`SignedTest`と証明書不要の`UnsignedDevelopment`を排他的parameter setへ分離。publish payload / public docs / images / license、XML、path / reparse、zero-byte / source / test / secret、MakeAppx pack / unpack、block map、identity、署名有無をfail-closed検査 |
| `scripts/test-windows-msix-unsigned.ps1` | `PASS_MECHANISM` — fixed BuildTools restore、Microsoft Authenticode、synthetic test PNG、policy negative 2件、実MSIX・sidecar・非機密evidenceの一貫性を検査 |
| `eng/packaging/windows/tools/WindowsSdkBuildTools.csproj` / lock | `PASS` — `Microsoft.Windows.SDK.BuildTools` `10.0.26100.4948`をexact lockし、configured proxy feedからlocked restore |
| `tests/StudyReportEvaluator.App.Tests/Packaging/WindowsInstallerPackageTests.cs` | `PASS` — manifest、signed / unsigned分離、tool lock、driver / CI境界を固定。最新focused test 5 / 5 PASS |
| output境界 | `PASS` — signed testは`.test.msix`、unsigned developmentは`.unsigned.test.msix`へ分離し、双方`PASS_MECHANISM`だけを許可 |
| public `README.md` boundary | `PASS` — MSIX対応済み・trusted installerのclaimへ変更していない |

検証結果:

- host: Windows build 29648、OS / process x64
- PowerShell: Core 7.6.5、.NET SDK 10.0.400
- fresh PowerShell AST parse: 2 scriptともerror 0
- manifest / tool project XML、tool lock JSON parse: `PASS`
- focused tests: 5 / 5 `PASS`
- latest documentation / installer contract single run: 19 / 19 `PASS`
- locked BuildTools restore: `PASS`、canonical tool lock SHA-256不変
- x64 `MakeAppx.exe`: version `10.0.26100.4948`、SHA-256 `C45D313BC490875F07512750DA24516924C73BC902FF5A9171361C932EF7862F`、Microsoft Authenticode `Valid`
- unsigned development MSIX: 155,500,721 bytes、SHA-256 `C7E2A2696EC29A293D99A1674D4A46C10C971DE6E3559DE0212E4F6A39FDA684`、253 entries
- identity: `StudyReportEvaluator.UnsignedDev` / `1.1.0.0` / x64 / Publisher末尾にfixed unsigned marker `OID.2.25.311729368913984317654407730594956997722=1`
- `AppxSignature.p7x`: 0、Authenticode: `NotSigned`、block map: SHA-256
- package内のREADME / LICENSE、docs 7件、images 8件、runtime manifest、bundled CLIを実測。CLI SHA-256はmanifest / package / evidenceで一致
- sidecar exact match、evidenceのpackage bytes / hash / entry count / source / host / tool / synthetic assets / statusが一致
- invalid fixed markerとsigned Publisherへのunsigned marker混入: 2件ともexpected rejection、既存output不変
- run後のprocessと`.msix-*` temporary directory: 0
- `git diff --check`: `PASS`
- CurrentUser valid code-signing certificate with private key: 0
- repository内のowner-approved 44x44 / 150x150 / 50x50 MSIX logo: 0。既存PNG 7件はdocumentation screenshotであり転用しない
- test MSIX pack / sign / unpack: `NOT_RUN`
- unsigned MSIX install / launch / lifecycle: `NOT_RUN_REQUIRES_ELEVATED_DISPOSABLE_WINDOWS_11_HOST`
- production status: `BLOCKED_EXTERNAL`

editorのPowerShell diagnosticsは修正前のlineと既に存在しない`$manifest`代入を指し続けたが、file再読込とfresh AST parserでは該当codeがなくerror 0だった。stale editor表示を実在findingとして記録しない。

### 7.2 敵対的レビュー

| Finding | 判定 | 反映 |
|---|---|---|
| caller指定`PublishedDirectory`がrepository外を参照できる | `ACCEPTED` | full path正規化後、存在確認・列挙・copy前に`Assert-PathWithinRoot`でrepository外を拒否 |
| EKUなし証明書でnullable property chain由来の不明瞭なerrorになり得る | `ACCEPTED` | code-signing OIDの列挙結果をboolean `$hasCodeSigningEku`へ正規化してpolicy errorで拒否 |
| identity nameの`.`を禁止すべき | `REJECTED` | 現行の明示patternは英数字・`.`・`-`だけを許可しcontrol文字を許さない。reviewは`.`が不正という根拠を示していないため制約を推測変更しない |
| logo pathへparameter-binding-level validationがない | `REJECTED` | body開始時にleaf存在、reparse、PNG signature、厳密寸法を検査しており、validation欠落ではない |
| 実packageを用いたbehavioral testがない | `ACCEPTED_AS_EXIT_GATE` | 初回レビュー時点ではsystem-installed SDK tools、approved assets、certificateがなく`BLOCKED_EXTERNAL`。後続§7.3でlocked BuildToolsとsynthetic assetsによるunsigned package behaviorまで実測し、certificateを要するsigned-testは未実施 |

反映後のfocused再レビューで、repository外pathがfile read前に拒否されること、EKUなしがboolean policy rejectionになること、両契約がtestに固定されたことを確認した。採用findingの未反映、Critical / High findingは0件。

### 7.3 証明書不要mechanism follow-upと敵対的レビュー

MicrosoftのWindows 11 unsigned package仕様を再確認し、任意UUIDではなくfixed marker `OID.2.25.311729368913984317654407730594956997722=1`をPublisher最終fieldへ要求した。実行codeを含むため、installは使い捨てVMまたは復元可能snapshot上の管理者PowerShellから`Add-AppxPackage -AllowUnsigned`で行う全ユーザー操作とし、標準App Installer、非管理者setup、production trustへ代用しない。

敵対的レビューで採用し反映した実在finding:

| Finding | 反映 |
|---|---|
| 任意`OID.2.25`値ではWindowsのunsigned marker契約を満たさない | fixed markerとPublisher末尾配置へ修正。旧可変marker artifactは正式3点set作成前に無効化 |
| signed経路がPublisher途中のunsigned markerを拒否しない | markerを位置に関係なくsigned経路で拒否 |
| repository境界をancestor junctionで迂回できる | rootから対象まで既存path componentのreparse pointを拒否 |
| MSIXに共通public payloadがない | README / LICENSE、docs 7件、images 8件を追加しunpack後に再検査 |
| BuildTools / synthetic assets / evidence手順が一時操作に依存 | exact tool project / lockと専用driverを追加 |
| staleまたは不完全なpackage / sidecar / evidence setが残り得る | 3点setの完全検査、不完全set無効化、package変更後failure時の3点cleanupを追加 |
| prior evidenceのarchive / tool / source / host / assets照合が不完全 | 実archiveを再読込し、全主要evidence categoryとPNG bytes / dimensions / hashを再照合 |

反映後の正式artifactを対象に、block map、entry safety、public payload、CLI、tool、evidence、policy negative、cleanupを再レビューした。Critical / High / Medium findingは0件。3点setのSHA-256はレビュー前後で不変だった。

**P2-01 repository foundation status:** `PASS_MECHANISM`

**P2-01 unsigned development package execution:** `PASS_MECHANISM` — package / unpack / independent validation / evidenceまで成功。installは未実施。

**P2-01 signed-test package gate:** `BLOCKED_EXTERNAL` — test certificate / Publisher identityとowner-approved logo assets未提供。test-certificate MSIXのsign / verifyは`NOT_RUN`。

**P2-01 production gate:** `BLOCKED_EXTERNAL` — unsigned development artifactをproduction installer evidenceへ昇格しない。

## 8. P2-02 — 本製品固有 MSIX compatibility gate

### 8.1 実行結果

P2-02はinstall済みMSIXを必須入力とする。Windows 11開発専用unsigned MSIXは生成・検証済みだが、現processは非管理者であり、使い捨てWindows 11 VMまたは復元可能snapshotも提供されていない。全ユーザーregistrationを現在の利用者environmentへ無断導入しないためinstallを開始しない。unpackaged Windows app、legacy ZIP、package structure testをinstalled behaviorの代替証跡にしない。

| Compatibility item | Result |
|---|---|
| packaged GUI startup / close / restart | `NOT_RUN` |
| package外 `.xlsx` picker / read-only load | `NOT_RUN` |
| user-selected directoryへのpartial / final / override output | `NOT_RUN` |
| package-local bundled CLI absolute resolution | `NOT_RUN` |
| CLI start / version / hash / login / model / cleanup | `NOT_RUN` |
| update後login state | `NOT_RUN` |
| cancel / kill / checkpoint resume / atomic finalization | `NOT_RUN` |
| uninstall後のuser workbook / output保持 | `NOT_RUN` |
| enterprise policy / sideload / signature trust failure | `NOT_RUN` |

### 8.2 敵対的レビュー

正式unsigned mechanism artifactとevidenceを再走査し、package生成成功をinstall成功へ読み替えるfalse-success claimがないことを確認した。レビューでfixed marker、全ユーザーinstall、使い捨てhost、production分離を明文化した。installed behaviorの実測がないため`BLOCKED_EXTERNAL` / `NOT_RUN`判定を維持する。

**P2-02 status:** `BLOCKED_EXTERNAL` — unsigned development MSIX candidateは存在するが、昇格済みPowerShell 7を使用できる使い捨てWindows 11 host / snapshotが未提供。9項目は全て`NOT_RUN`であり、MSIX採否分岐は未判定。

## 9. P2-03 — Windows production signing

### 9.1 実行結果

production signingへ進む入口条件を確認した。Windows distribution channel X-02は§1のdefault decisionによりGitHub direct MSIXを採用済みだが、P2-02 compatibility gateは未判定、X-03 Identity / PublisherとX-04 trusted signing methodは未提供である。`release.yml`にprotected release environment、MSIX production sign、chain / revocation / timestamp検証は存在しない。test certificate pathやunsigned ZIPをproduction pathへ転用しない。

| Gate | Result |
|---|---|
| P2-02 compatibility PASS | `BLOCKED_EXTERNAL` |
| X-02 distribution channel | `PASS_DECISION` — GitHub direct MSIX |
| X-03 production Identity / Publisher | `BLOCKED_EXTERNAL` |
| X-04 trusted production signing method | `BLOCKED_EXTERNAL` |
| protected release environment / signing integration | `NOT_RUN` — identity / method確定前に仮secret名や署名処理を作らない |
| production chain / revocation / timestamp / re-download verification | `NOT_RUN` |

### 9.2 敵対的レビュー

reviewは、test-only script、unsigned ZIP、現行workflowのいずれにもproduction成功claimがないことと、P2-03へ進めば明示gate違反になることを確認した。reviewの「X-02もTBD」はplan作成時点だけを参照したもので、§1のdefault decisionを見落としているため棄却した。X-03 / X-04とupstream gate不足による停止判定は採用した。code-level finding、未反映findingは0件。

**P2-03 status:** `BLOCKED_EXTERNAL` — production Identity / Publisher、trusted signing method、credential、P2-02 PASS未提供。production sign / verify / re-downloadは`NOT_RUN`。

## 10. P3-01 — macOS `.app` bundle assembly

### 10.1 実装結果

既存P1-02 foundationをP3-01契約として再検証し、`publish-macos.sh`へapphost / CLIに加えて3 native dylibのthin Mach-O / RID architecture検証、`package-macos.sh`へ中間・完成bundleのexecute bitと必須構造検証を追加した。

| Contract | Result |
|---|---|
| `Contents/Info.plist` / `MacOS` / `Resources` layout | `PASS_MECHANISM` |
| executable / bundle ID / product version / build version一致 | `PASS_MECHANISM` |
| owner-approved `.icns`必須化とResources配置 | `PASS_MECHANISM` |
| x64 / ARM64 separate bundle | `PASS_MECHANISM` |
| apphost / CLI thin Mach-O architecture | `PASS_MECHANISM` |
| Avalonia / HarfBuzz / Skia dylib thin Mach-O architecture | `PASS_MECHANISM` |
| apphost / CLI execute bit（中間・完成bundle） | `PASS_MECHANISM` |
| native `.app`生成 / GUI launch | `NOT_RUN` |

検証結果:

- editor diagnostics: 対象2 script / plist 0件
- Git for Windows Bash `-n`: 2 script `PASS`
- 非macOS invocationの副作用前fail-closed: `PASS`
- `git diff --check`: `PASS`
- bare `bash`はWSL shimへ解決されdistributionなしで失敗したため、Git Bash absolute pathで再実行した。script defectとして扱わない

### 10.2 敵対的レビュー

| Finding | 判定 | 反映 |
|---|---|---|
| dylibを`Contents/Frameworks`へ必ず移すべき | `REJECTED` | .NET publishのloader pathをnative実測せず変更できない。現layoutを保ち、launch / signature / notarizationが通るlayoutだけを最終採用する |
| chmod後のexecute bitを明示検証していない | `ACCEPTED` | apphost / CLIを中間・完成bundleで`-x`検証 |
| 完成bundleのplist/apphost/CLI/icon構造を再検査していない | `ACCEPTED` | atomic rename後に全必須fileを再検査 |
| 3 native dylibのarchitectureを検証していない | `ACCEPTED` | thin Mach-O、expected architecture、shared-library typeを検証 |
| `sign-macos.sh` input検証 | `DEFERRED_TO_P3-03` | signing taskで扱う |

反映後のfocused再レビューで3 dylib、execute bit、plist / apphost / CLI / icon構造をsource上で確認し、採用findingの未反映、Critical / High code findingは0件。

**P3-01 repository foundation status:** `PASS_MECHANISM`

**P3-01 native bundle gate:** `BLOCKED_EXTERNAL` — native x64 / ARM64 Mac、owner-approved `.icns`未提供。実bundle生成 / launchは`NOT_RUN`。

## 11. P3-02 — Hardened runtime / entitlement最小化

### 11.1 実装結果

`sign-macos.sh`はapphost、CLI、dylib、outer appへ`--options runtime`を指定する。必要権限を実測前に推測追加せず、entitlementsは空dictを維持した。`package-macos.sh`と`sign-macos.sh`の双方で次を署名前にfail-closed検査するよう強化した。

- UTF-8 BOMを先頭3 byteで拒否
- `plutil -lint`でmalformed plistを拒否
- `get-task-allow`を拒否
- `allow-unsigned-executable-memory`を拒否
- `disable-library-validation`を拒否

検証結果:

- Git Bash syntax: 2 / 2 `PASS`
- 現entitlements no-BOM / XML parse: `PASS`
- editor diagnostics: 3 file 0件
- `git diff --check`: `PASS`
- hardened runtime下のapp / Avalonia / CLI / network / picker / read-write: `NOT_RUN`
- `allow-jit`要否: `NOT_RUN` — source推測では決定しない

### 11.2 敵対的レビュー

初回reviewは「hardened runtimeには最低1 entitlementが必須」「static sourceからJIT entitlementを確定できる」としたが、根拠がなく、実測前に権限を追加しないP3-02契約にも反するため棄却した。一方、契約にあるBOM拒否の欠落を直接確認して採用し、危険な3 entitlementの明示拒否も追加した。

反映後のfocused再レビューでBOM、malformed plist、3 forbidden entitlementの拒否を確認し、採用findingの未反映、Critical / High code findingは0件。空dictは「権限不要を証明済み」ではなくmeasurement待ちを意味する。

**P3-02 mechanism status:** `PASS_MECHANISM`

**P3-02 runtime measurement gate:** `BLOCKED_EXTERNAL` — native Mac、signed bundle、Developer ID、approved icon未提供。`allow-jit`を含む必要最小集合とruntime 6機能は`NOT_RUN`。

## 12. P3-03 — Developer ID signing

### 12.1 実装結果

`sign-macos.sh`の既存signing順を維持し、native RID host、source / signing-copy bundle構造、全Mach-O architecture、署名後execute bit、pre-notarization `spctl` assessmentを追加した。

| Contract | Result |
|---|---|
| native host architecture = RID | `PASS_MECHANISM` |
| dylib → CLI → signed CLI hash manifest → apphost → outer app | `PASS_MECHANISM` |
| nested / outer hardened runtime + secure timestamp | `PASS_MECHANISM` |
| signingで`--deep`不使用 | `PASS_MECHANISM` |
| source / copy plist、icon、apphost、CLI、manifest、provenance、全dylib | `PASS_MECHANISM` |
| pre/post-sign apphost / CLI、全dylib expected thin architecture | `PASS_MECHANISM` |
| outer後strict verify、hash不変、pre-notary `spctl` assessment | `PASS_MECHANISM` |
| Developer ID実署名 / strict verify / assessment | `NOT_RUN` |

pre-notarization `spctl`は実行して結果を明示するが、notarization前の拒否を署名artifact生成の循環blockerにはしない。最終Gatekeeper acceptanceはP3-04のnotarize / staple後にfail-closedで評価する。

検証結果: Git Bash syntax `PASS`、非macOS fail-closed `PASS`、editor diagnostics 0件、`git diff --check` `PASS`。

### 12.2 敵対的レビュー

| Finding | 判定 | 反映 |
|---|---|---|
| `spctl` assessment欠落 | `ACCEPTED` | outer署名・strict verify・不変性検査後にpre-notary assessmentを実行し、最終判定がP3-04であることを表示 |
| native host / RID、source plist / icon検証不足 | `ACCEPTED` | sign開始前に検査 |
| dylib architecture、post-sign apphost / CLI architecture / execute bit不足 | `ACCEPTED` | expected thin Mach-Oと`-x`を検査 |
| dylib 3件だけのallowlistを要求 | `REJECTED` | 全native dependencyを署名する必要があり、将来の正当なdylibを未署名にする固定allowlistは危険 |
| `codesign`がCPU sliceを書換える前提 | `REJECTED_AS_RATIONALE` | 根拠はない。ただし最終artifact contractのdefense-in-depthとしてarchitecture再検査は採用 |
| manifest / provenanceがparseされていない | `REJECTED` | 必須keyの`plutil -extract`成功と値検証を既に要求し、manifest更新後もextractしている |

反映後のfocused再レビューで全採用findingを確認し、未反映、Critical / High code findingは0件。

**P3-03 repository mechanism status:** `PASS_MECHANISM`

**P3-03 production signing gate:** `BLOCKED_EXTERNAL` — native Mac、approved Developer ID Application identity、ephemeral keychain、approved icon未提供。実署名結果は`NOT_RUN`。

## 13. P3-04 — Notarization / staple / DMG

### 13.1 実装結果

`scripts/notarize-package-macos.sh`を追加した。approved keychain profile名だけを入力し、credential値をargument / evidenceへ渡さない。

1. signed appをstrict verify
2. `ditto --keepParent` ZIP作成、`notarytool submit --wait`
3. submission / logが`Accepted`、issuesが空であることを検査
4. app staple / validate / strict verify / Gatekeeper assess
5. stapled appと`/Applications` symlinkからUDZO DMG作成
6. DMG verify / Developer ID sign / strict verify / submit / log検査 / staple / validate / Gatekeeper assess
7. final bytes / SHA-256 / sidecar / submission IDs・statuses / non-secret evidence / notary logsを保存
8. auxiliary evidenceを先に配置し、DMGをrelease setのcommit markerとして最後にmove

検証結果: Git Bash syntax `PASS`、非macOS fail-closed `PASS`、editor diagnostics 0件、`git diff --check` `PASS`。Apple notary serviceへのsubmit、staple、DMG生成は`NOT_RUN`。

### 13.2 敵対的レビュー

| Finding | 判定 | 反映 |
|---|---|---|
| submission ID regexがdash位置を固定しない | `ACCEPTED` | 8-4-4-4-12 hex UUID shapeへ厳格化 |
| evidence statusがliteral `Accepted` | `ACCEPTED` | gate通過済みsubmission JSONから実値を再抽出 |
| final DMG move後に補助file moveが失敗すると不完全setが残る | `ACCEPTED_SELF_REVIEW` | sidecar / evidence / logsを先、DMGを最後にcommit |

反映後のfocused再レビューでcore order、secret境界、厳密UUID、実status、commit順を確認し、未反映、Critical / High code findingは0件。

**P3-04 repository mechanism status:** `PASS_MECHANISM`

**P3-04 production notarization gate:** `BLOCKED_EXTERNAL` — P3-03 signed app、native Mac、Developer ID、notary keychain profile、Apple service access、approved icon未提供。notary / staple / DMG結果は`NOT_RUN`。

## 14. P3-05 — Quarantine clean-machine test

### 14.1 実行結果

`artifacts/package/macos-release/`にDMGはなく、P3-04 production gateも未通過である。quarantine付きpublic-like downloadの必須入力がないため、次のexact matrixを推測実行しない。

| OS candidate | `osx-arm64` | `osx-x64` |
|---|---|---|
| macOS 14 | `NOT_RUN` | `NOT_RUN` |
| macOS 15 | `NOT_RUN` | `NOT_RUN` |
| macOS 26 | `NOT_RUN` | `NOT_RUN` |

download / quarantine、DMG open / drag、Finder launch、Gatekeeper、picker / I/O / checkpoint、CLI login / model / cleanup、update、app removal、negative casesは全て`NOT_RUN`。credential、device code、token、Prompt、responseは採取していない。

### 14.2 敵対的レビュー

repository / artifact / work evidenceを再走査し、DMG、clean-machine result、quarantine evidence、false-success claimが0件であることを確認した。reviewがP3-04の2 findingを未反映とした点は修正前snapshotであり、§13で反映・再レビュー済みのため棄却した。P3-05固有の未反映findingは0件。

**P3-05 status:** `BLOCKED_EXTERNAL` — notarized/stapled DMGとnative clean Mac matrix未提供。6行すべて`NOT_RUN`、`PASS_PRODUCTION`は0行。

## 15. P4-01 — Platform test contract

### 15.1 実装結果

- legacy `WindowsPublishPackageTests`をunsigned ZIP regressionとして維持
- `WindowsInstallerPackageTests`をtest MSIX manifest / fail-closed mechanism契約として分離
- fixed BuildTools / unsigned driver /実artifact・sidecar・evidence契約を追加
- `MacOsPublishPackageTests`を追加し、plist / minimal entitlement、RID、Mach-O検査、unsigned packageとsign/notaryの非結合、notary/staple/secret境界を固定
- `CopilotClientFactoryTests`へwrong-RID manifestの`InvalidDataException` fail-closed negative testを追加
- `DocumentationContractTests`の現行Windows unsigned ZIP / macOS未提供claimを削除してgreenにしていない

初回platform focused tests: 19 / 19 `PASS`。unsigned follow-up後のWindows installer focused tests: 5 / 5 `PASS`、最新documentation / installer contract single run: 19 / 19 `PASS`。fresh PowerShell AST error 0、`git diff --check` `PASS`。

### 15.2 敵対的レビュー

macOS unsigned packageがsign/notaryを暗黙呼出ししない契約とwrong-RID negative testを採用した。初回wrong-RID testは`null`を期待して1件失敗したが、source再読でresolverは既にRID照合し`InvalidDataException`を返すことを確認してtestを修正した。production defectではなかった。

初回時点でMSIX実署名behaviorをsystem-installed tool / certificateなしhostへ必須化する提案、ZIPとMSIXの同居を禁止する提案、`.gitignore`規約追加は要件外または将来の複数channelを壊すため棄却した。後続ではlocked BuildToolsによるunsigned mechanismを追加したが、certificateを要するsigned behaviorは別gateのままである。再レビューが同じ棄却済み2件を再掲した部分も採用しない。採用findingの未反映は0件。

**P4-01 repository test status:** `PASS_MECHANISM`

**P4-01 native / installed behavior:** `BLOCKED_EXTERNAL` — P2-02 / P3-05の実機gateを継承。

## 16. P4-02 — CI matrix

### 16.1 実装結果

`.github/workflows/ci.yml`へsecret-freeな`macos-contract` matrixを追加した。

- `macos-15`と`macos-15-intel`を`fail-fast: false`で分離
- exact OS version / build、architecture、.NET SDKをnon-production factsとしてartifact化
- locked restore / Release build
- macOS packaging contract + Copilot resolver tests
- permissionはworkflow全体の`contents: read`のみ、production secret / sign / notaryなし
- 既存Windows deterministic / legacy ZIP validationを維持
- Windows jobでfixed BuildToolsからunsigned MSIX driverを実行し、package本体を公開せずsidecar / 非機密evidenceだけを14日保持

editor diagnostics 0件、required static contractと`git diff --check`は`PASS`。GitHub-hosted runner実行は`NOT_RUN`。

### 16.2 敵対的レビュー

reviewはYAML semantics、secret-free fork safety、locked restore、Intel / ARM intent、facts upload、filter、permission、timeout、非production境界を確認した。`-cne 'Core'`がWindows PowerShellを誤受理するというfindingは論理的に誤りで、安全側の厳密比較を維持した。runner将来deprecationは未発生の予測、既存Windows TRX auto-nameは本変更外かつ現行契約であるためfindingにしない。採用すべき未反映finding、Critical / High code findingは0件。

**P4-02 repository workflow status:** `PASS_MECHANISM`

**P4-02 hosted matrix run:** `NOT_RUN` — branch push / GitHub Actions実行は本local sessionで未実施。

## 17. P4-03 — Release workflow

### 17.1 実行結果

`.github/workflows/release.yml`は現行検証済みchannelであるWindows unsigned ZIPとSHA-256だけを公開する。P2-02 installed MSIX、P2-03 production signing、P3-05 clean-machine DMGが未成立のため、MSIX / DMG production jobsとasset setを追加しない。placeholder identity / secret名、test certificate、unsigned macOS artifactをrelease pathへ接続していない。

**P4-03 implementation:** `BLOCKED_EXTERNAL`

**P4-03 current legacy release regression:** `PASS` — workflow変更なし、false multi-platform claimなし。

### 17.2 敵対的レビュー

reviewは現行workflowにMSIX / DMG / trusted productionのfalse claimがなく、今production pathを追加するとgateを迂回するため維持が正しいと確認した。「macOS/MSIX/sign/notary scriptが存在しない」「P4-01/P4-02未実装」という記述はstaleで現在sourceに反するため棄却した。実在する停止条件はP2-02 / P2-03 / P3-05の外部gateである。current workflowのCritical / High finding、未反映findingは0件。

## 18. P4-04 — Secret boundary

### 18.1 実行結果

- tracked sensitive-name file: 0
- PR CI `secrets.*`参照: 0
- test certificate thumbprintは公開metadataとしてCurrentUser store lookupにだけ使用し、trust storeへinstallしない
- macOS signingはDeveloper ID identity名、notaryはkeychain profile名だけを受け取る
- password、Apple ID、API key、private key、tokenをprocess argument / evidenceへ渡さない
- production workflow integrationはP2-03 / P3-04 gate未成立のため未実装

### 18.2 敵対的レビュー

PR/fork、certificate trust、macOS keychain、notary profile、evidence、release workflowを確認し、credential leak、unsafe argument / logging、Critical / High findingは0件。採用すべき未反映findingも0件。

**P4-04 repository boundary status:** `PASS_MECHANISM`

**P4-04 production secret integration:** `BLOCKED_EXTERNAL` — approved signing methods / protected environments / credentials未提供。

## 19. Phase 5 — End-user documentation switch

### 19.1 実行結果

README、getting started、docs index、troubleshooting、privacy、package release notesのprimary pathは現行Windows unsigned ZIPのまま維持した。MSIX download URL、signer、supported Windows build、macOS DMG / OS matrixを推測追加していない。

**Phase 5 status:** `BLOCKED_EXTERNAL` — P2-02 / P2-03 / P3-05 `PASS_PRODUCTION`が0件のため文書切替は`NOT_RUN`。

### 19.2 敵対的レビュー

公開手順6文書にfalse MSIX / DMG availability claimがないことを確認した。reviewは`CHANGELOG [Unreleased]`の「追加中」「candidate」を提供済みclaimと解釈したが、実際の未公開進行状態を示す文言であり、primary setup変更ではなく、P0 baseline前からの既存差分でもあるため棄却した。公開URL / signer / support matrixの捏造、採用すべき未反映findingは0件。
