# ADR-0008: Platform、行単位印刷、OS別package候補

> [!WARNING]
> **HISTORICAL / SUPERSEDED:** cross-platform、print、signed package候補を扱う旧requirements v1.xの設計です。現行初版はWindows 11 x64のunsigned ZIPだけを対象とします。現行状態は[`implementation-status.md`](../implementation-status.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **承認済み・G-08完了・要求v1.2へG-RB反映済み（DEC-15/16/20最終選択はG-17待ち）** |
| 対象決定 | DEC-15、DEC-16、DEC-20 |
| 要求 | FR-103、NFR-SEC-005、NFR 9.6、SET-001〜008 |
| 決定 | 候補、不変条件、試験matrixだけを固定する。native Wayland、印刷方式、package形式を実証前に採用しない |
| 承認根拠 | ADR-0001 の承認証跡（実装前 decision gate は既定案） |
| 記録日 | 2026-08-31 |

## 決定の境界

G-08で承認するのは、G-17が比較する候補と合否条件である。G-RBはこの候補matrixと未解決blockerを正本へ取り込むだけで、DEC-15/16/20を採用済みに変換しない。G-17はG-RB後、かつEXT-07のrunner/signing/Arm64/printer入力が揃った行だけを実行し、そのevidenceから最終decisionを発行する。次はまだ承認しない。

- 「Wayland」がnative Waylandだけを意味するか、Wayland desktop上のXWaylandを含むか。
- FR-103を満たすproduction印刷adapter。
- Windows、macOS、Ubuntuで正式配布する各1形式。
- 未取得のproduction code-signing identity、Apple notarization、Arm64実機、printer/spooler結果。

候補の存在、cross-publish成功、test certificate、virtual printerだけをproduction対応証拠と呼ばない。G-17の結果を受け、製品/QA、製品/プライバシー、リリース責任者がそれぞれDEC-15/16/20を決める。成立しなければ無断で範囲や意味を変えず、要求を正式改版するか当該platform/releaseを停止する。

## 2026-08-31時点の外部事実

### Avaloniaとdisplay backend

- AvaloniaのLinux既定backendはX11である。
- Wayland desktopでは既定でXWayland compatibility layerを使う。
- native `Avalonia.Wayland` backendはAvalonia 12.1.0以降のopt-in packageだが、現行公式文書でも**experimental**である。`UsePlatformDetect()`は選択せず、`UseWayland()`にautomatic fallbackはない。
- native backendはmouse/touch/keyboard、clipboard、drag-and-dropを掲げる一方、KDEのglobal menu、window icon、blur-behind等に制限がある。
- 公式support tierではUbuntu 22.04/24.x x64/Arm64、macOS 14 x64/Arm64、Windows 11 22H2 x64/Arm64はTier 2であり、要求範囲の全行がvendor Tier 1ではない。

したがって、experimental backendを正式対応と表示しない。G-17でnative routeをcharacterizeしても、それだけで正式採用しない。Wayland上XWaylandを要求適合とみなすには製品/QA責任者の明示決定が必要である。nativeを必須と決める場合は、backendが正式supportになるか、要求所有者が保守責任と残存riskを別途承認するまでLinux Wayland対応をblockする。

### 印刷API

Avalonia coreだけに依存するproduction方式は未確定である。現行資料で確認できる候補は次である。

- `Avalonia.Controls.WebView` の `NativeWebDialog.ShowPrintUI()` はWindows/macOS/Linux desktopでplatform print dialogを開く。ただしLinuxではWebKitGTKとGTK 3等の追加runtimeが必要で、Wayland sessionでもWebKitGTKはXWaylandを使う。native browser profile/cacheとpackage/license/supply-chainを検証する必要がある。
- WindowsにはGDI/XPS等のOS printing API、macOSには`NSPrintOperation`、Linuxには`GtkPrintOperation`/CUPSがある。いずれもAvalonia viewをそのまま安全に印刷できることを意味せず、platform adapter、pagination、lifetime、cancel、spooler privacyの実証が必要である。
- PDF stream生成、XLSX export、browserへのfile handoffは印刷とは別操作である。appがそれらを生成する方式をFR-103の「印刷」の代替にしない。

### 配布

.NET self-contained publishはRID別のapphost、app、依存、runtimeを含むが、OS native dependencyまでは保証しない。計画どおりfolder-based self-contained outputを共通payloadとし、single-file、trimming、Native AOTをG-08で追加しない。

appの正式候補RIDは次の6件である。

- `win-x64`, `win-arm64`
- `osx-x64`, `osx-arm64`
- `linux-x64`, `linux-arm64`

Copilot CLIに`linux-musl-*` assetがあっても、P0対象OSはglibc系Ubuntuであり、muslをappの対応RIDへ追加しない。各RIDは別artifact/hash/signature/evidenceを持ち、fallbackやarchitecture emulationを実機証拠の代用にしない。

## DEC-15: Linux display候補

### 候補

| ID | Session | Avalonia backend | 現時点の扱い |
|---|---|---|---|
| DISP-X11 | X11 | X11 | production候補 |
| DISP-WL-XW | Wayland | X11 through XWayland | production候補だが、「Wayland」適合にはownerの意味決定が必要 |
| DISP-WL-NATIVE | Wayland | `Avalonia.Wayland` | 結果にかかわらず`EXPERIMENTAL_ONLY`のcharacterization。現時点で正式対応候補に昇格しない |

applicationはbackendの選択結果を本文なしdiagnosticsへ記録する。環境変数だけでsession/backendを断定せず、G-17 runnerがcompositor、XWayland可否、実接続backendを外部観測する。XWaylandを停止したWayland sessionも別負試験とする。support matrix/UI/READMEは、DISP-WL-XWを必ず「Wayland session（XWayland使用）」、DISP-WL-NATIVEを「native Wayland experimental」と表示し、単独の「Wayland対応」へ丸めない。pure WaylandでXWaylandがない場合にDISP-WL-XWは非対応である。

### Display試験matrix

次の12行を独立して扱う。1行の成功を別行へ転用しない。

| Ubuntu | Arch | Route |
|---|---|---|
| 22.04 | x64 / Arm64 | DISP-X11、DISP-WL-XW、DISP-WL-NATIVE |
| 24.04 | x64 / Arm64 | DISP-X11、DISP-WL-XW、DISP-WL-NATIVE |

各行で少なくとも次を実測する。

1. supported-stateに近いclean imageとfresh userで、開発SDK/IDEなしに署名済みcandidateを配置し起動する。
2. OS/version/arch、desktop/compositor、`XDG_SESSION_TYPE`、`DISPLAY`/`WAYLAND_DISPLAY`の有無、実接続backend、Avalonia/runtime/native library exact版を記録する。
3. 起動、終了、再起動、single-instance policy、window close/cancel、focus、keyboard-only navigation、pointer、200% scale、file picker、clipboard、drag-and-drop、複数window/modalを合成データで確認する。
4. Orca/AT-SPI2で主要controlのname/role/state/focusを確認する。
5. vault、Copilot CLI child process、行単位print dialogを同じrouteで確認する。
6. compositor再起動、display socket切断、XWaylandなし、GPU acceleration不可、software fallback、missing native dependencyを単一原因ずつ試験し、hang/crash/silent backend switchを許さない。DISP-WL-NATIVEのupstream既知制限は観測値として列挙するが、許容したことにせず`EXPERIMENTAL_ONLY`を維持する。
7. app/CLI/cache/temp/logへ合成canaryが残らないこと、入力/output workbookを変更しないことを確認する。

DISP-X11/DISP-WL-XWは1件でも未実行、失敗、backend不明ならその組合せを対応表示しない。DISP-WL-NATIVEは全試験に成功しても`EXPERIMENTAL_ONLY`であり、正式対応表示へ寄与しない。Windows 11のexact build、macOS 14以降の具体的minor/majorもRC matrixで列挙し、`Windows 11`や`14以降`という無期限の一括claimにしない。

## DEC-16: 行単位印刷

### Privacy-first input contract

print adapterはcollection、workbook、file pathを受け取らず、R-11が生成するimmutableな単一 `RowExplanation` だけを受け取る。初版の表示/印刷fieldはFR-103の次の項目に閉じる。

- 使用した設問のID/表示文。
- 評価項目と点数anchor。
- Promptの版/hash（Prompt全文ではない）。
- model ID。
- AI候補点、理由、根拠状態。
- 教員判断、上書き点、任意comment、任意の非認証`reviewer_label`、判断時刻。local launcher/判断者の本人性・roleは表示・推測しない。

学生名、メール、学生番号、他設問、他行、他学生の候補/理由/根拠を既定で含めない。現在行の原回答/evidence抜粋を加える必要が生じた場合は、R-11で別の明示field・preview・プライバシー承認を追加するまで印刷へ渡さない。Prompt、reason、comment等はmarkupとして解釈せずtextとしてencodeする。

### 比較候補

| ID | 方式 | 必須preflight |
|---|---|---|
| PRINT-WEB | local `NavigateToString` + `NativeWebDialog.ShowPrintUI()` | WebView package/license、offline resource、private/ephemeral profile、cache削除、Linux WebKitGTK/XWayland、network 0 |
| PRINT-NATIVE | Windows print API、macOS `NSPrintOperation`、Linux `GtkPrintOperation`またはCUPSのplatform adapter | ABI/binding、OS dialog、pagination、font、cancel/result、spooler、X11/native Wayland両方 |

PRINT-WEBはJavaScript、navigation、新規window、cookie、remote URL、remote font/imageを無効にし、全resource requestをdefault denyする。shared browser profileを使用せず、印刷previewごとにaccess-controlled app-owned一時directoryへunique profileを作り、dialog close/error/cancel後にprofileを閉じて削除し、crash後は次回起動scavengerがapp-owned profileだけを削除する。このprofile location/lifetimeをexact locked package版で設定・観測できない、または終了/再起動後canary 0を実証できない場合は不採用とする。追加WebView runtimeを利用端末へ手作業で要求せず、既存OS componentまたは選択packageの検証済みdependencyとして扱えることも必要である。main branch文書にAPIがあるだけでは足りず、G-17でlockした`Avalonia.Controls.WebView` packageのAPI/版/licenseを再確認する。

PRINT-NATIVEはOSごとに別adapterとしてよいが、同じ`RowExplanation`とlayout contractを使う。Linuxでcommandを使う候補はshellを介さずabsolute executableとargument array/stdinを使い、printer/job IDを不信値として扱う。外部fileを一時作成する実装を既定にせず、必要ならaccess-controlled app temp、durable cleanup、crash scavengingを別要求として承認する。

### 印刷合否条件

各候補をWindows 11 x64/Arm64、macOS 14以降x64/Arm64、Ubuntu 22.04/24.04 x64/Arm64の各対応候補行で試す。LinuxはDISP-X11と、DEC-15で採用候補になった各Wayland routeを分ける。

1. previewは印刷へ渡すexact `RowExplanation`だけを表示し、keyboardから明示開始/取消できる。
2. OS/platform print dialogを表示し、silent/default-printer送信をしない。利用者がOS dialogでPrint to PDFを選ぶことは許すが、app自身のPDF exportを印刷成功と数えない。
3. local test queue/virtual printerへ1 jobだけ送り、rendered pageまたはaccess-controlled lab spool captureにcurrent-row canaryが存在し、other-row canary、秘密、file pathが0件である。captureは検査後に削除し、hash付き本文なしresultだけを残す。
4. OS dialogでのcancelはqueue投入前なのでjob 0、app temp/cache本文0とする。Print確定後は`QUEUED`と表示し、物理印刷完了や取消成功を主張しない。初版appはqueue job列挙/取消を提供せず、利用者をOS queueへ案内する。将来提供する場合はOSから返されたopaque job ID、所有者照合、失敗の明示、他job非干渉を別要求で定義する。
5. printerなし、queue満杯、spooler停止/途中再起動、permission拒否、paper/page設定、Unicode/RTL/長文/page break、200%表示、font fallbackを試し、成功・取消・queued・失敗を区別する。
6. app processはprinter/vendor endpointへ直接接続せず、外部通信0とする。network printer/CUPS/OS spoolerの通信と保持はapp通信と分離する。対象printer/print server、cloud relay/telemetry有無、暗号化、queue保持、access、secure releaseを機関の署名済みoperations policyが承認できない環境では実データ印刷をblockする。G-17のlocal/virtual queue成功を全printer/driver互換の保証と表示しない。
7. app-owned cache/temp/log/sessionをclose直後とapp再起動後にcanary走査し、本文残存0。`RowExplanation`はpreview lifetimeを越えてdurable cacheへ保存せず、参照を解放し、mutable bufferだけbest-effort zeroする。managed memory、swap、OS crash dump、OS spoolerを完全消去できるとは称しないため、EXT-09 dump policyとprint operations policyがない実データ印刷をblockする。
8. screen readerとkeyboardでpreview、Print、Cancel、error/actionを操作・認識できる。

全OS/arch/display行で1候補が成立するまでFR-103 printを実装へ進めない。OSごとに別候補を採用する場合も、各行のowner承認を必要とする。成立しない場合はFR-103を正式改版し、PDF/XLSX exportへ無断置換しない。

## DEC-20: OS別package候補

全候補の内側payloadはRID別folder-based self-contained publishであり、app、.NET runtime、採用CLI、app-local ICU、license/SBOMを含む。外側形式だけを比較する。

| OS | 候補 | 非特権default | Native trust検証 | 主な確認点 |
|---|---|---:|---|---|
| Windows | Authenticode済みapp executableを含む署名manifest付きZIP + per-user setup | Yes | app-owned executable/installer Authenticode、third-party signature、全payload detached manifest/hash | executable bit不要、ACL、Zone.Identifier/SmartScreen、repair/uninstall |
| Windows | signed MSIX | 原則Yes | `AppxSignature.p7x`、trusted chain、timestamp、package integrity | package identity、virtualization、child CLI、user-selected file/vault、enterprise sideload policy |
| macOS | signed/notarized/stapled `.app`を`ditto` ZIP化 | Yes（`~/Applications`候補） | nested code signatures、Developer ID、secure timestamp、notary log、app ticket | executable mode、quarantine、Gatekeeper、offline staple、repair |
| macOS | signed/notarized/stapled DMG内のstapled `.app` | Yes（利用者配置先次第） | appとDMGを個別検証 | mount、drag/drop、offline Gatekeeper、DMG tamper/unmount |
| macOS | signed/notarized flat PKG | 通常No | app/installer別Developer ID、notary/staple | elevation理由/取消、receipt、managed install、完全uninstall |
| Ubuntu | detached署名/SHA付きtar.gzまたはZIP + per-user setup | Yes | release manifest/signature/hashを展開前検証 | mode/symlink/traversal、`~/.local` integration、dependency preflight |
| Ubuntu | `.deb` + detached release signature/SHA | 通常No | setupが実行前にdetached証拠を検証 | apt elevation、arch/dependency、desktop entry、repair/remove |

Parcel等のpackaging toolは候補formatの存在根拠にすぎず、採用dependencyではない。Parcel CLIはfree community licenseで利用できないため、license、再現可能性、CI secret、SBOM、vendor lock-inをG-17/P-01前に承認できない限りbuild chainへ追加しない。

### 共通package試験

各OS/arch/candidateを独立行として次を実行する。

1. clean supported OS、fresh user、開発SDK/IDEなしで、HTTPS取得後にouter SHA-256、release signature、RID、nested payload manifestを**実行/展開前**に検証する。安全なstagingへ展開後、最初のsetup helper/app/CLI実行より前に全fileのpath/type/size/hash、許可mode/link、app-owned native signatureとvendor-owned signature/hashをmanifestへ再照合する。
2. wrong RID、1-bit tamper、truncated/extra file、unknown signer、expired/revoked signer、signature/hash mismatch、archive traversal、absolute/alternate-data-stream path、hardlink/reparse point/symlink escapeを拒否し、candidateを実行しない。Windows/Linux archive payloadのlinkは初版で全て拒否する。macOS bundle内でtoolchainが必要とするlinkは、bundle内を指すrelative linkだけをmanifestへ列挙し、code-sign検証後に許可する。
3. app `--version`、GUI起動、CLI handshake、ICU golden、vault/display/print preflightを実行する。cross-publish asset inspectionだけではPASSにしない。
4. defaultはper-user、非特権、既知のapp-owned directoryだけを変更する。elevation候補は変更先/理由を事前表示し、取消時の変更0を確認する。
5. 同版正常再実行は再download/再配置せず検証だけ、tampered/incomplete同版は安全除去後repair、upgrade/downgradeは版/hash/影響を表示して明示確認する。per-user候補の既定配置先はWindows `%LOCALAPPDATA%\Programs\StudyReportEvaluator`、macOS `~/Applications/StudyReportEvaluator.app`、Ubuntu `~/.local/lib/study-report-evaluator`とし、launcher/desktop entry以外をroaming/sync対象へ置かない。別pathは事前previewと明示選択を必要とする。
6. disk full、process kill、locked file、network切断を各atomic boundaryで注入し、旧版または新版の完全な一方だけを起動可能にする。
7. uninstallはapp binaries/registrationと利用者が選んだapp logだけを削除する。入力/出力XLSX、別app file、OS credentialは削除せず、credential削除を別確認にする。locked file/permission/disk errorで一部削除に失敗した場合は成功と表示せず、残存pathを本文なしで列挙して`UNINSTALL_INCOMPLETE`とし、次setupはその状態を検出してrepair/removeを明示選択させる。partial stateから旧cache/policyを暗黙再利用しない。
8. package/setup/uninstallのstdout/stderr、process argument、URL、tempへsecret、token、student contentを含めない。
9. production-equivalent trust pathでnative signature/notarizationを検証する。各結果へ`trust_level=TEST_MECHANISM_ONLY`または`PRODUCTION_EQUIVALENT`を機械可読で必須記録し、GATE-RC/Releaseは後者だけを受理する。test certificateの成功はmechanism evidenceに限定し、正式配布PASSにしない。
10. artifact hash、signer identity、timestamp/notary submission、OS build、arch、package manager、変更path、結果を本文なしevidenceへ記録する。

### OS固有の追加条件

#### Windows

- app-owned executable/installerのAuthenticode chain、revocation、RFC 3161 timestampをDefault Authentication policyで確認する。全payload fileはrelease manifest hashへ拘束し、Microsoft/GitHub等vendor-owned binaryを本製品鍵で再署名せず、元signatureとapproved hashを確認する。
- MSIXはpublisherとcertificate subject、package integrity、enterprise sideload policy、full-trust/package capability、virtualization、CLI child process、user-selected workbook、vault/app-state location、uninstall後package-owned stateを確認する。
- ZIP/setupはouter signatureだけに依存せず、nested executable signatureも確認する。PowerShell実処理はPowerShell 7+に限定する。
- SmartScreen reputationが即時に成立すると仮定せず、clean machineの実挙動を記録する。

#### macOS

- nested Mach-O/helperを内側から署名し、`--deep`に依存しない。hardened runtime entitlementは実際に必要な最小集合だけを採用する。
- Apple Developer ID、secure timestamp、notary acceptedだけでなくnotary logのwarning、`codesign`、`spctl`、stapler validation、quarantine付きdownload後の初回起動を確認する。
- ZIP自体へticketをstapleできないため、appへstapleしてからarchiveを作る。DMG/PKGを配布する場合はcontainerにも必要なnotarization/staplingを行う。
- x64/Arm64 bundleを別artifactとして検証し、Rosetta成功をnative architecture成功に数えない。

#### Ubuntu

- archive/debのarchitecture、glibc、Avalonia/display、Secret Service、CUPS/GTK等の実dependencyを`/etc/os-release`とpackage databaseから検査し、存在を仮定しない。
- archiveはowner/mode/symlinkを展開前manifestと照合し、desktop entry/iconを利用者領域へ限定する。
- `.deb`の直接配布では、package managerが本製品のdetached release signatureを自動検証すると仮定せず、setupがapt/dpkg実行前に検証する。
- rootでappを実行せず、elevationはpackage install/removeの明示境界だけにする。

## 形式選定規則

G-17 evidence完成後、OSごとに次の順で判定する。

1. 全対象arch/OS build/displayで共通条件とOS固有条件を満たさない候補を除外する。
2. production signing/notarization/trust、clean-machine setup、repair、uninstallのいずれかが未実証なら除外する。
3. 残る候補から、非特権default、変更surface最小、追加runtime最小、atomic repair/uninstall、機関配布経路への適合を比較し、リリース責任者が1形式を署名決定する。
4. 複数formatを「念のため」正式配布しない。managed deployment等で第2形式が必要なら別support/test matrixとして要求変更する。

候補が0なら当該OS/archをreleaseしない。1候補へ決めた後もE-09のsetup-to-uninstallが失敗した場合は対応表示を撤回する。

## G-17 evidence contract

`dev/docs/preflight/platform-matrix.md` と `eng/platform-matrix.json` は少なくとも次を各行に持つ。

- immutable case ID、candidate ID、OS edition/build、arch、physical/VM、display/compositor/backend。
- app/.NET/Avalonia/CLI/ICU exact versionとartifact SHA-256。
- package/signature/notary/test-printer identityは非秘密IDだけ。signature evidenceには`TEST_MECHANISM_ONLY`/`PRODUCTION_EQUIVALENT`を必須記録する。
- clean-state根拠、start/end UTC、実行したtest ID、result、closed failure code。
- produced evidence artifactのSHA-256。生spool、student text、token、signing secretは保存しない。
- tester、reviewer、DEC owner、承認時刻、scope、expiry。

未実行は`NOT_RUN`、外部入力待ちは`BLOCKED`とし、`PASS`へ丸めない。test-only signature、emulation、experimental backend、virtual printerはそれぞれその性質を明記する。

## 未解決blockerと要求改版候補

| Blocker | 現在値 | 解除条件 | 未解除時 |
|---|---|---|---|
| DEC-15 Wayland意味 | 未決 | 製品/QAがnativeまたはWayland上XWaylandの意味を署名決定 | G-RB/G-17/P-04とLinux Wayland対応をblock |
| DEC-16 print adapter | 未決 | 全対象行で同一privacy/layout contractを満たす候補を製品/プライバシーが承認 | R-11/UI-10をblock。exportへ置換しない |
| DEC-20 package | OSごと未決 | G-17 clean-machine/sign/repair/uninstall evidence後に各1形式をリリース責任者が承認 | P-01以降の当該OSをblock |
| OS version範囲 | Windows 11 / macOS 14以降が広義 | RCごとに具体的buildを列挙し、未試験範囲を要求改版 | 一括対応表示をblock |
| EXT-07 | signing/notarization/Arm64/printer実機なし | production-equivalent runnerとtrust input | 当該行を`BLOCKED` |

G-RBへ反映する場合も、未実証候補を採用済みと書かない。DEC-15/16/20はG-17 evidenceへの参照とowner署名が揃った時点で別decision recordによりSupersedeする。

## Source snapshot

2026-08-31に次の公式資料を参照した。これらはcandidateの存在・制約の根拠であり、本製品での成功証拠ではない。

- Avalonia Supported platforms: `https://docs.avaloniaui.net/docs/supported-platforms`
- Avalonia Desktop Linux / Wayland: `https://docs.avaloniaui.net/docs/platform-specific-guides/linux#wayland`
- Avalonia desktop deployment: `https://docs.avaloniaui.net/docs/deployment/linux`, `https://docs.avaloniaui.net/docs/deployment/macos`
- Avalonia WebView/NativeWebDialog source docs: `https://github.com/AvaloniaUI/avalonia-docs/blob/main/docs/app-development/embedding-web-content.md`, `https://github.com/AvaloniaUI/avalonia-docs/blob/main/controls/web/nativewebdialog.md`
- .NET publish/RID: `https://learn.microsoft.com/dotnet/core/deploying/`, `https://learn.microsoft.com/dotnet/core/rid-catalog`
- Windows package/sign/print: `https://learn.microsoft.com/windows/apps/package-and-deploy/`, `https://learn.microsoft.com/windows/msix/package/signing-package-overview`, `https://learn.microsoft.com/windows/win32/seccrypto/signtool`, `https://learn.microsoft.com/windows/win32/printdocs/how-to--print-using-the-gdi-print-api`
- Apple notarization/print: `https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution`, `https://developer.apple.com/documentation/security/customizing-the-notarization-workflow`, `https://developer.apple.com/documentation/appkit/nsprintoperation`
- GTK/CUPS print: `https://docs.gtk.org/gtk4/class.PrintOperation.html`, `https://www.cups.org/doc/options.html`

## 完了判定

Wayland/XWayland/nativeを混同しない候補、他学生データを構造的に渡さないOS印刷候補、OS別package候補、全対象OS/arch/displayの正負試験、sign/notarize/repair/uninstall/privacyのfail-closed条件を定義した。実証前に1形式や対応状態へ固定しておらず、DEC-15/16/20とEXT-07の未解決状態を明示した。