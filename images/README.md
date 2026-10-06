# Documentation screenshots

このフォルダーは、StudyReport Evaluatorの操作説明に使う実際のAvalonia viewのPNGを保存します。

> **対象版:** この画像一覧は公開`v0.8.6`のUI説明用です。**8枚のPNGは`0.8.6`のsourceから2026-09-27に再生成**したもので、旧`0.8.1`の画面を示すものではありません。旧公開版`0.8.1`は変更していません。設問text同期・固定表示の採点計算式・幅に応じたカード配置を`0.8.1`の機能として扱わないでください。既存PNGは生成後のcode/UI変更の検証証跡ではなく、clean-host試験CH-01〜06や本人loginの証跡でもありません。製品版`0.9.0`（未公開。前版の公開`v0.8.6`の後継）への版更新ではPNGを再生成しておらず、パネルの配置変更・ペルソナ・図など`0.9.0`で追加した画面は8枚に含まれません。

F02は版metadataと文書表記の更新で、その時点ではPNGを再生成していません。その後、固定`auto`表示の削除（reasoning effort表示へ置換）、並列度既定8、類似度のローカル計算化に合わせ、2026-09-27に下記の8枚生成手順で8枚を再生成しました。再生成は文書画像の同期であり、native表示・本人確認等の新たなUI検証とは扱いません。

## Architecture diagrams

次のSVGは2026-09-15時点のproduction sourceと、公式のGitHub Copilot SDK／Avalonia／Open XML資料を照合して作成した論理図です。実行時に自動収集したtopology、性能測定、live認証・AI評価、GitHub内部構成の証跡ではありません。外部画像、script、remote fontを参照しない自己完結SVGです。

| File | 対象読者 | 内容 |
|---|---|---|
| [`architecture-overview.svg`](architecture-overview.svg) | 利用者・非技術者 | 入力Excel、Windowsアプリ、同梱Copilot CLI、GitHub Copilot、結果Excelの関係 |
| [`technical-architecture.svg`](technical-architecture.svg) | ソフトウェアエンジニア | App／Core project、UI、workflow、workbook、Copilot adapter、外部境界 |
| [`evaluation-message-flow.svg`](evaluation-message-flow.svg) | ソフトウェアエンジニア | 明示login、状態確認、一時session、closed tool、checkpoint、final化の時系列 |

図の実装根拠、変更時の同期箇所、公式資料は[技術ガイド](../docs/technical-guid.md)を参照してください。

## Provenance

- 生成日: 2026-09-27（再生成日。初回生成は2026-09-07の親T28。いずれもfixture内の固定日時や出力名の日付とは別）
- 生成時の製品版: `0.8.6`。初回生成時は`0.8.4`
- renderer: Avalonia 12.1.1 Headless + Skia
- size: 1440 × 1050 pixels、8枚
- generator source: `tests/StudyReportEvaluator.App.Tests/UI/DocumentationScreenshotTests.cs`（repositoryでのみ利用。配布ZIPにはsourceを含めない）
- definition/question/evaluator/criterion IDs: fixture内で固定し、同一環境での2回描画が一致することを検証。本番のID生成は変更しない
- input state: テストが生成した一時合成workbookから読み込んだmetadata。header 1行 + 回答100行 × 2設問。`sample/`や実データは使用しない
- displayed path: `C:\Synthetic\StudyReport-100x2.xlsx`
- design state: Base 60、Special 0、Question points 20/20、Similarity penalty weight 0.1
- Copilot state: bundled CLI 1.0.79を模した合成runtime identityとfake authentication boundary。実CLIプロセス・live login・network requestは実行していない
- login UI state（05）: 状態確認前の`NotChecked`（認証未確認）・ログイン未開始。表示は「effort／未選択」（固定`auto`表示は削除済み）で、model未選択、認証確認・ログイン・runはいずれも未実施。ログインボタンとstatusは認証済み／live loginの証跡ではない
- run state（06/07のみ）: production durable orchestratorをfake row/AI/input-snapshot/checkpoint/path-planner/finalizer/output boundariesで実行。Reference 2 + 100行 ×（Normal 2 + Similarity 2）= 402 operations。SimilarityはAIを呼ばないローカル計算で、fake参照回答と合成回答の表層類似度は0。認証と完了・final出力成功もfake応答であり、実入力fileの検証や実結果workbook作成の証跡ではない
- result state: fake runnerのraw 8とローカル計算のsimilarity 0を実際の計算処理へ渡す。06はoverrideなしでFinal score 92.0。07は選択行の設問1のraw 8を保持したままoverride 9とし、Final score 94.0。AI品質や実データ結果を表さない
- output state: final/partialは合成path。07の`C:\Synthetic\result\eval-20260902-1200-reviewed.xlsx`は元のfinalとは別名の**未保存候補**で、exportは未実行。出力可能表示はfake path判定であり、実file作成の証跡ではない
- settings state（08）: fake認証確認・runより前のSettings / Common。合成デモでは`settingsStore: null`を明示し、保存・読込再試行は無効。「保存先が構成されていません。ファイルへの読込・保存は行いません。」と表示し、`setting.txt`の読込・保存は行わない。本番アプリはユーザー用の保存先を解決するため、利用者に環境変数の設定を求める状態ではない
- personal/student data・secret: なし。password・token・device codeなどの資格情報や実ユーザーの保存先を含めず、画面上のpathは合成値のみ

画像はproduction XAML / ViewModelをheadless Skiaでrenderした画面です。物理ディスプレイを撮影したものではなく、OS window chromeも含みません。UI layoutの説明には使用できますが、Windowsのfont rendering、DPI、window decorationを保証する証跡ではありません。

### 親T28の生成・検証実績

- `t28.trx`: `STUDY_REPORT_EVALUATOR_GENERATE_DOC_IMAGES=1`で通常サイズ8枚を生成し、`images/`への保存後のfile検証と画像テストが成功。
- 当時の`t28-reviewed.trx`: **216/216成功**。この中に`DocumentationScreenshotTests`の4テストを含む。
- 通常サイズ: **1440 × 1050の8フレーム**を実描画し、同一環境での2回生成のSHA-256一致が成功。
- 最小client size: **1024 × 720の8フレーム**について実際のclient size・framebufferのpixel dimensions・非単色のpixel data・保存PNGの寸法を検証。要求サイズだけからの推定ではない。一時画像のみで、`images/`へはコピーしていない。

上記は製品`0.8.4`時点の親T28の実績で、T29では生成・テストを再実行していません。T01〜T38は対象範囲でREVIEWEDとなり、T36の文書・画像contractは21/21成功です。この文書contractの成功を、native表示確認や版更新後のUI検証へ読み替えません。非opt-inの自動テストによるrepository画像との一致保証や、native DPIでの動作保証を意味しません。

### 2026-09-27の再生成

- `STUDY_REPORT_EVALUATOR_GENERATE_DOC_IMAGES=1`で`FullyQualifiedName~DocumentationScreenshotTests`の4テストを実行し4/4成功。通常サイズ8枚を`images/`へ保存した（01・03はbytes不変）。
- 同じ実行で2回生成の一致と1024 × 720の8フレーム検証も成功。headless Skiaの描画であり、native表示・本人確認の証跡ではない。

## Image index

| File | 表示内容 | 主な確認項目 |
|---|---|---|
| [`01-input-workbook.png`](01-input-workbook.png) | Input主画面 | 合成workbookのpath、100行・2設問、設問一覧とファイル選択ボタン（native pickerを開いた画像ではない） |
| [`02-input-mapping.png`](02-input-mapping.png) | Settings / Mapping | 設問2を選択、primary C・supporting D、設問表示名、read-onlyの設問text、候補概要 |
| [`03-design-knowledge.png`](03-design-knowledge.png) | DesignのKnowledge概要 | 設問1のKnowledge summary、2設問の配点20/20、Base 60、採点計算式（Prompt editorではない） |
| [`04-design-custom-prompt.png`](04-design-custom-prompt.png) | Settings / Evaluation / Prompt | 設問2の実際のCustom Prompt editor（編集可）とread-only preview |
| [`05-execution-auto.png`](05-execution-auto.png) | Executionの状態確認前 | `NotChecked`・ログイン未開始・reasoning effort未選択、状態確認／ログインボタン、concurrency 2、実行無効、read-onlyの実効output directory |
| [`06-results-review.png`](06-results-review.png) | Results一覧（fake run完了） | 402 operations、100行の結果、raw 8・similarity 0によるFinal score 92.0、overrideなし。final pathはfake完了応答で、実workbook出力ではない |
| [`07-output-export.png`](07-output-export.png) | Results詳細（override編集中） | 選択行の設問1はraw 8 → override 9、Final score 94.0。別名reviewed pathは未保存候補、export未実行 |
| [`08-settings.png`](08-settings.png) | Settings / Common（合成デモのstore未構成） | 定義名・revision・丸め桁数・実効output directory、未保存変更、保存無効、ファイル読込・保存なし。本番の保存先設定手順ではない |

## Regeneration

### 05のみ（一時生成・比較・review用）

05だけの更新では、次のtestを完全一致で選択します。8枚生成・再現性・最小サイズtestと同じ`GenerateAsync` fixtureを使用し、既存のopt-inを維持しています。

- test: `StudyReportEvaluator.App.Tests.UI.DocumentationScreenshotTests.Execution_documentation_screenshot_renders_are_repeatable`
- filter: `FullyQualifiedName=StudyReportEvaluator.App.Tests.UI.DocumentationScreenshotTests.Execution_documentation_screenshot_renders_are_repeatable`
- optional opt-in: `STUDY_REPORT_EVALUATOR_GENERATE_EXECUTION_DOC_IMAGE=1`（比較済みPNGをreview用artifactへ残す場合だけ設定）
- artifact: `artifacts/test/documentation-execution-05/first/05-execution-auto.png` と `artifacts/test/documentation-execution-05/second/05-execution-auto.png`（gitignore対象）

testは共有fixtureの画面遷移を再利用し、別々の一時directoryへ**05のみを2回**描画します。各directoryに05以外のfileがないこと、file size、1440 × 1050 pixels、SHA-256の一致、ログインボタン／statusの表示・位置と未確認状態を検証し、fake runへは進みません。一時画像は成功・失敗を問わず削除します。専用opt-inがある場合だけ、検証成功後に上記2枚のartifactを上書き保存します。

このtestは`images/`へコピーしません。今後05だけを更新する場合は、親タスクがtest成功と2枚の一致を確認し、画像をreviewしてから、**05のPNGだけ**を`images/05-execution-auto.png`へコピーしてください。コピーするまで既存PNGは更新されません。他7枚は変更しません。8枚生成用の既存opt-in `STUDY_REPORT_EVALUATOR_GENERATE_DOC_IMAGES`は不要で、これが設定済みでも05専用testは`images/`へコピーしません。05だけの更新にclass全体のfilterや下記の8枚更新手順を使わないでください。

### 8枚すべて（未リリース候補の説明画像更新）

今後の再生成では、最新のインストール済みPowerShell 7+でrepository rootから既存のvisual documentation testを明示的にopt-inします。次のfilterは4テストを対象にしますが、`images/`へ保存するのは通常サイズの生成testだけです。

```powershell
$previousGenerateImages = $env:STUDY_REPORT_EVALUATOR_GENERATE_DOC_IMAGES
try {
	$env:STUDY_REPORT_EVALUATOR_GENERATE_DOC_IMAGES = '1'
	dotnet test tests\StudyReportEvaluator.App.Tests\StudyReportEvaluator.App.Tests.csproj --no-restore -c Release --filter 'FullyQualifiedName~DocumentationScreenshotTests'
	if ($LASTEXITCODE -ne 0) { throw 'Documentation screenshot validation failed.' }
}
finally {
	$env:STUDY_REPORT_EVALUATOR_GENERATE_DOC_IMAGES = $previousGenerateImages
}
```

### 自動検証と保存の範囲

- `Documentation_screenshots_use_only_synthetic_state_and_have_expected_dimensions`: 通常サイズ8枚を一時directoryへ描画し、file集合・file size・保存PNGのpixel dimensionsを検証。既存opt-inが`1`の場合だけ、検証済みの通常サイズ8枚を`images/`へコピーし、保存先の存在・file size・pixel dimensionsも確認します。**非opt-inではrepository画像を検査・更新しません**。
- `Synthetic_screenshot_renders_are_repeatable`: 一時directoryへ通常サイズ8枚を2回生成し、対応するPNG bytesのSHA-256を比較。比較対象は2組の一時画像で、`images/`内のPNGではありません。
- `Minimum_client_screenshots_verify_all_eight_actual_pixel_frames_without_publishing`: 一時directoryで1024 × 720の8枚を検証。共有capture処理で実client size・実framebuffer寸法・非単色のpixel dataを確認し、保存PNGを再読込して寸法も検証します。どのopt-inでも最小サイズ画像は`images/`へコピーしません。
- `Execution_documentation_screenshot_renders_are_repeatable`: 上記の05専用test。専用opt-inの保存先はreview用artifactのみです。

各testの一時画像は成功・失敗を問わず削除します。2回生成の一致や最小サイズtestの成功を、repository画像と現行UIの自動的一致確認へ拡張しません。同一環境での再現性を別OS／font／native DPIのpixel一致へ一般化しません。新しいopt-inやCLIは追加していません。

## Source

Avalonia Headlessのframe capture方法: Avalonia公式 [Setting up the headless platform](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform)（2026-09-01確認）。
