# Windows single-file適合試験（S01）

| 項目 | 実測値/状態 |
|---|---|
| 日付 | 2026-09-06 |
| 対象source | `26512a4a8a686134ea9bbdf4f271d04ca1fff91b` |
| 製品版 | `0.8.3`（試験時。まだPATCH変更していない） |
| .NET SDK / bundled runtime | `10.0.400` / `10.0.11` |
| Avalonia / Copilot SDK / CLI | `12.1.1` / `1.0.11` / `1.0.79` |
| 実行host | 開発用Windows x64、OS build `29648` |
| 機構試験 | `PASS_MECHANISM`。clean-host/署名/公開資格を意味しない |
| 敵対的レビュー | `PASS_REVIEW`、修正すべき指摘0件。成果物hash/source不変を再確認済み |
| G1 | 機構の適合としてPASS。clean-host等は下記のとおりNOT_RUN |

## 実施方法

承認された[実装プラン](../archive/work/20260906-0617-one-action-startup-plan.md)のS01を、`artifacts/test/singlefile-spike/source/` のdetached worktreeで実施した。通常source、canonical package locks、既存公開物は変更していない。

- 既存Appへ一時profileを適用。self-contained、single-file、native/all-content self-extractを有効化し、trimming/ReadyToRun/圧縮は無効。
- profileのproperty/content/一時検証targetは `MSBuildProjectName == StudyReportEvaluator.App` に限定した。
- CLIは既存publish中の固定 `1.0.79` バイナリを指定し、manifestと同じSHA-256を確認した。
- `PublishProfile` は指定pathを無視するため、一時profileには `PublishProfileFullPath` を使用した。[dotnet publish](https://learn.microsoft.com/dotnet/core/tools/dotnet-publish#pubxml-files)
- restore lockは各projectの `obj/S01/` へ分離した。最終publishは警告なしで成功した。
- SkiaSharp/HarfBuzzSharp由来の外部PDB 2ファイルは、既存のpublish方針と同様、所有する試験出力からだけ除外した。PDBはbundleに含まれていない。

## package実測

| 項目 | 値 |
|---|---|
| 実行に必要な配布file数 | 1 |
| EXE bytes | `282870564` |
| EXE SHA-256 | `EDFBBD4C1E60885DC5AB038BA7474E494AAC999FF401342A8EAF9142A2C22AE1` |
| CLI SHA-256 | `CDE713815A50678199250FE6D91F2A596F68D3FD2F418022CB58B8C97E78A6EE` |
| 展開file数 / 総bytes | `231` / `272489397` |
| 追加build依存 | Appだけに `Microsoft.NET.ILLink.Tasks 10.0.11`。Coreへの追加は不要 |
| ILLink package contentHash | `IBf7lbovvjGWVWXZX5cJ/cO0WXbId0Zq4BuSeT94mGZuOAP66oMeH9PTBZ9Jpp3Jb6jtK0qm/NyUbPRo1gC/wQ==` |

runtime版は展開された `StudyReportEvaluator.App.runtimeconfig.json` の `includedFrameworks` から取得した。single-fileで外部 `coreclr.dll` が存在するとは仮定していない。

## 実画面による観測

実EXEを `StudyReportEvaluator-win-x64.exe` へcopyし、EXEと異なる日本語/空白を含むcwdから起動した。PATHはWindows System32のみ、DOTNET_ROOTは不存在path、HOME/APPDATA/COPILOT_HOME等は試験専用pathとした。これは追加softwareが未導入である証明ではない。

| ケース | 実際に確認した内容 | 観測処理全体のms |
|---|---|---:|
| cold | UI AutomationでInputFilePathが存在し空。展開先App base、CLI hash、同梱docを確認。正常終了 | 16521 |
| warm | 同じbundleを再起動し、上記と同じ条件を確認。正常終了 | 8243 |
| relative-input-prompts | synthetic `.xlsx` と2つのUTF-8 Promptを相対指定。画面のinput pathが期待するabsolute pathに一致。正常終了 | 3540 |

時間には起動継続確認とUI Automation等の測定処理を含む。製品の起動時間、性能保証、cold/warm優劣を示す数値として使用しない。

- native host traceの `APP_CONTEXT_BASE_DIRECTORY` が実際の展開先と一致した。
- `copilot-runtime.json` と `runtimes/win-x64/native/copilot.exe` が元の相対配置を維持し、CLI hashも一致した。
- README/LICENSE/利用者guide/Execution画像が展開された。
- synthetic inputのSHA-256は不変だった。
- 固定CLIの `login --help` は終了code 0。`login`、`--web-flow`、`--device-code` を確認した。本人loginは実行していない。
- 元sourceの `LaunchOptionsTests` と `CopilotClientFactoryTests` は計28件成功、失敗0。

## 制限・本実装へ引き継ぐ事項

- `IncludeAllContentForSelfExtract` の公式非推奨注意は承認プランから継続する。[single-file仕様](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview)
- `WindowsSandbox.exe` は当hostに存在するが、fresh OS実行、MOTW/SmartScreen/SAC、本人login、offline network isolation、実AI評価は **NOT_RUN**。
- UI Automationの観測に開発hostのWindows Desktop runtimeを使用した。これは配布アプリの依存ではなく、clean-hostの証拠でもない。
- 2つのPrompt指定で正常起動したことは確認したが、Design画面での並び順・明示適用は既存unit testと後続P06/A系試験で別途確認する。
- 自己更新抑止optionとlogin後のruntime hash保持はA01/V02で確認する。CLIのhelp成功をSDK handshakeや本人認証成功へ読み替えない。
- P01ではApp限定profile、P02ではprofile指定とsymbols除外、P03ではAppのみのbuild依存固定を採用する。Coreの専用single-file lockや新規production projectは不要。
- 実測evidenceは `artifacts/test/singlefile-spike/run-7b757ea086224e2ea24cfaf004ddf3ae/evidence.json`。一時source/profile/probe/host traceはignored領域にあり、public packageへ含めない。

## 試験手順の修正履歴

最初のprofile指定はNETSDK1198で未適用だったため不採用。正しいフルパス指定で再publishした。GUI観測はinput-idleを画面生成完了と同一視しないよう、既存package試験と同じ有限の起動継続確認を追加した。runtime版取得を外部coreclr.dll依存からruntimeconfigへ変更した。これらは試験側の修正であり、アプリの障害と報告していない。