# StudyReport Evaluator v4.6 システムテスト Prompt集

| 項目 | 内容 |
|---|---|
| 文書用途 | ソフトウェアエンジニアまたはテスト支援AIが、ユースケース単位でコピーして実行するPrompt集 |
| 対象 | StudyReport Evaluator v4.6 |
| 基準日 | 2026-09-07 |
| 要求正本 | `docs/requirements-definition.md` v4.6 |
| 版の区別 | v4.6は要求版。製品正本は親担当が0.8.5へPATCH済み（UNRELEASED・未公開候補）、F01はREVIEWED、公開済みはv0.8.1 ZIP。利用者不在時の自律続行指示によりT39 BLOCKEDのままF01／F02を進める。F02最終再検証は本同期時点では親担当で未完了、以後は実行記録の最新F02欄を参照 |
| 設計正本 | `dev/docs/detailed-design.md`、ADR-0012、ADR-0013、ADR-0015、ADR-0016 |
| UI/settings契約 | `dev/docs/ui-layout-contract.md`と`dev/docs/traceability.md`。T01の未実装記録は履歴。T01〜T38はREVIEWED、現行差分はVERIFIED_SCOPED。0.8.4のT36文書contract・T37実ZIP・T38実EXE・T39自動回帰は記録済み。T39追加native FAIL／人手未実施は解消せず、F02最終0.8.5の再検証と分離 |
| 対象環境 | Windows 11 x64。公開済みv0.8.1はunsigned ZIP＋sidecarの2 asset。0.8.5候補の将来主導線はunsigned single-file EXE、ZIPは代替、公開目標は各sidecarを含む4 asset。開発用 MSIX と macOS の基盤は 2026-10-06 に廃止した |
| 必須決定的fixture | `tests/fixtures/system-test/SystemTest-10Students.xlsx` |
| 注意 | 本書はテスト結果ではない。未実施、過去結果、文書上の期待値を今回のPASSとして扱わない |

> **V02境界の明記:** P06/P07の開発host観測は`PASS_DEVELOPMENT`またはpackage機構確認であり、fresh標準user clean-host（CH-01〜CH-06）の公開判定試験（V02）を充足しない。未実施項目は`NOT_RUN`のまま保持し、`PASS_REQUIRED`/`PASS_PRODUCTION`へ読み替えない。

> **捏造禁止:** 期待結果は要求、実装、固定fixture、独立oracleから導出し、実測結果は今回の実行証跡だけから記録する。過去のtest件数、性能値、package hash、Live AI結果、未対応platformの結果を今回値として流用しない。

## 1. 正本と統合境界

本書は、旧24件版PromptのTest Requirement 1〜24、セッション添付の旧実データtechnical E2E資料、およびv4.3 deliveryのTR-25〜TR-29を統合した、repositoryで唯一のシステムテストPrompt正本である。旧Prompt fileは統合検証後に廃止済みであり、実行時は本書だけを使用する。

- repositoryに実在する要求正本は`docs/requirements-definition.md`である。
- `/hve-dev/requirement-definition.md`は本repositoryに存在しないため参照しない。
- 要求定義§19のTR-01〜TR-36を欠番なく網羅する。既存ST-UC-01〜26を再番号付けせず、保存・明示適用・往復のST-UC-27〜29を末尾へ追加する。通常／例外layoutはST-UC-12へ追補する。
- 10人fixture E2Eは、Promptを小規模かつ決定的に反復できるよう追加した補助scenarioであり、要求定義を改変しない。
- 531行synthetic E2E、10人synthetic E2E、canonical SampleReport technical E2Eを相互代用しない。
- optional Live Copilotとexternal recalculationをrequired deterministic testへ代用しない。
- 実データをLive AIへ送信しない。

### 1.1 統合した旧ユースケース

| 旧分類 | 統合後の扱い |
|---|---|
| shell / warning | 4-step UI、exact warning、keyboard、200% scaleへ統合 |
| input / mapping | Forms/Google row 1/2、canonical sample、10人fixture、pickerへ分離 |
| unsupported input | picker/direct path scenarioの形式・package安全境界へ統合 |
| dynamic design | 絶対Points、Base/Special/Similarity、均等配分、validationへ統合 |
| Knowledge / Custom Prompt | Knowledge、Custom、Special、closed tool schemaへ統合 |
| auth / execution / cancel | reference-first、retry、checkpoint、resume、cancelへ再編 |
| score / override | QuestionEarned、SpecialEarned、SimilarityPenalty、FinalRaw、FinalScoreへ統合 |
| output | 自動final、4 app-owned sheets、partial、atomic commitへ統合 |
| privacy / validation | reference／normal／special／similarityの4 operationへ拡張 |
| delivery | Windows publish、ZIP integrity、platform claimを別scenarioとして維持 |
| optional smoke | Live Copilotとexternal recalculationを2つの独立scenarioへ分離 |

## 2. Fixture契約

### 2.1 10人synthetic fixture（required）

| 項目 | 固定値 |
|---|---|
| path | `tests/fixtures/system-test/SystemTest-10Students.xlsx` |
| bytes | `7,652` |
| SHA-256 | `7F473879B3C43319D842881C5DFD1D01B2A60091A5B0C7B39B70B8D574D801C8` |
| worksheet | `SystemTestInput` 1件 |
| dimension | `A1:L11` |
| rows | header 1行 + 匿名synthetic data 10行 |
| formula | 0件。`= + - @`で始まるcanaryも文字列 |
| package安全境界 | macro、external link、DDE、defined nameなし |

このfixtureは`sample/SampleReport.xlsx`の**row 1 headerだけ**を転記し、data rowは全て新規のsynthetic値から作成した。実データのrow 2〜531をコピーしていない。各primary列F〜Jには空または空白primaryを1件ずつ置き、Jが空の行でもKを入力済みにして、primaryなしではsupportingだけでAI送信しない境界を検証する。

### 2.2 Canonical repository sample（TR-02専用）

要求定義§4.4のcanonical sample契約は次のとおりであり、10人fixtureで置換しない。

以下の12列の表とF〜K候補は旧sampleの履歴であり、現行sampleの期待値は本節末尾の2026-09-16採用追補を優先する。

| 項目 | 契約／履歴測定値 |
|---|---|
| path | `sample/SampleReport.xlsx` |
| bytes | `470,806` |
| SHA-256 | `73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA` |
| package entries / relationships（履歴） | 11 / 8（現行の固定gateではない） |
| worksheet / dimension | 1件（nameはhashのみ記録） / `A1:L531` |
| 初期候補 | F/I primary、G/J primary + student Prompt、H primary + supporting、K supporting、J→K support |
| 初期target / 対象外 | F〜K / A〜E、L |

fileが存在しない場合は期待値へ合わせて作成せず、当該scenarioを`BLOCKED`とする。F〜Kの役割はheader semantic由来の候補であり、固定mappingではない。
sample契約はこのexact pathだけを使用し、`sample` directoryの他fileを列挙、fallback、代用しない。

**2026-09-16 利用者明示承認:** canonical sampleの旧bytes・SHA-256・worksheet name hash・先頭128 bytes・11 entries／8 relationships一致は非ゲートとする。旧測定値は履歴として保持し、現在の測定値へ書き換えない。実ZIPの`Entries.Count`と`classification.PackagePartCount`の一致、metadataのpackage／relationship件数とclassificationの一致、正かつclassifierの安全上限内のrelationship件数、external relationships 0を必須とする。sheet／dimension・行列数・mapping候補と実行前後SHA-256／size／last-write UTC不変も引き続き必須。opt-in、privacy、実データのLive AI送信禁止は変更しない。10人synthetic fixture、CLI、配布物の固定identity検証は対象外。

**2026-09-16のread-only観測:** 現在fileは469,976 bytes、13 entries／9 relationships（root 4〔classificationlabelsを含む〕、workbook 4、sheet1のtable 1）、external relationships 0で、classifierは`StandardXlsx`として受理した。13／9は固定gateではない。旧fileの元bytesがなく正確な差分・変更原因は証明できないため、旧fixtureの変更内容を断定しない。entry一覧は`dev/docs/preflight/sample-workbook-profile.md`を参照する。

**2026-09-16 現行sample採用追補:** 利用者不在時の自律続行指示に基づき、production `WorkbookMetadataReader`＋`ColumnMappingSuggester`の測定済みprofileを採用する。出典は`TestResults/app-fixes-20260916/sample-profile/dahatake_DAHATAKE-OFFICE_2026-09-16_13_59_37_net10.0.trx`の標準出力。1 worksheet、`A1:J531`、531行／10列、header 1、data 2〜531、target D/E/F/G/H/I、対象外 A/B/C/J。D/GはPrimaryAnswer、E/HはPrimaryAnswer + StudentPromptPrimary、F/IはSupporting、suggested supportingはE→F・H→Iだけ（他候補は空）。本追補は§2.2／2.3／3の旧12列sample-only期待値を上書きし、12列syntheticと10人fixtureの固定契約・coverageは変更しない。構造assertのskip・任意化は行わない。出典testは旧期待値との不一致でFAILであり、測定記録を修正後test／E2EのPASSへ読み替えない。本追補でテスト・ビルドは実行していない。

### 2.3 Canonical SampleReportのtechnical E2E利用

以下は旧12列sampleの履歴表。現在のtechnical E2Eには§2.2の採用追補と§3.3を適用する。

| 項目 | 構造baseline／履歴測定値 |
|---|---|
| logical path | `sample/SampleReport.xlsx` |
| bytes | `470,806` |
| local SHA-256 | `73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA` |
| historical identity comparison | size、SHA-256、先頭128 bytesの実比較は診断専用（非ゲート） |
| package entries（履歴） | 11（固定gateではない。現行契約・観測は§2.2） |
| worksheet / dimension | 1件 / `A1:L531` |
| rows | header 1行 + data 530行 |
| macro / external-link parts | 0 / 0 |

§2.2と同じ唯一のcanonical sampleを使用し、別のsample fileを列挙、作成、要求、代用しない。このworkbookのcell本文、header本文、worksheet名、学生情報をreportへ出力しない。Live AIへ送信せず、既存のlocal deterministic runnerだけでtechnical durable E2Eを行う。

### 2.4 Fixed-seed 531-row synthetic fixture

`tests/StudyReportEvaluator.App.Tests/E2E/SyntheticWorkbookFactory.cs`がseed `20260901`から実行時に作成する。`Original!A1:L531`、data 530行であり、repositoryへbinaryを追加しない。10人fixtureや実データのidentity oracleを流用しない。

## 3. Canonical SampleReport由来の設問とアプリ既定値

### 3.1 設問／source mapping

canonical `sample/SampleReport.xlsx`のrow 1 headerから得た履歴baselineの5 question definitionは次のとおりである。回答本文は抽出していない。現在fileのmapping再検証完了を示すものではない。

| Question | Primary | Supporting | Evaluator | Points |
|---|---|---|---|---:|
| ソフトウェアでの実装手段として、プログラミングと機械学習の違いについて述べる設問 | F | なし | KnowledgeCoverage | 8 |
| `[必須ではない] 設問(1) 作成のために、生成AIに入力したPrompt` | G | なし | CustomPrompt | 8 |
| `質問` | H | なし | KnowledgeCoverage | 8 |
| 実世界での機械学習（AI）の利用状況、技術、利用方法、採用理由を調査する設問 | I | なし | KnowledgeCoverage | 8 |
| `[必須ではない] 設問(4) 作成のために、生成AIに入力したPrompt` | J | K | CustomPrompt | 8 |

Kは、設問(4)(5)に関連してPrompt作成時に工夫した点、観点、論点を記すsupporting sourceである。

### 3.2 数値の出典

| 設定 | 既定値 | 出典 |
|---|---:|---|
| BasePoints | 60 | 要求定義§5.1、アプリの初期definition |
| SpecialPoints | 0 | 要求定義§5.1、アプリの初期definition |
| SimilarityPenaltyWeight | 0.1 | 要求定義§5.1、アプリの初期definition |
| RoundingDigits | 1 | 要求定義§5.1、アプリの初期definition |
| Question Points（旧sampleの履歴） | 8 × 5 | 要求定義§6.2の`(100-60-0)/5`と旧12列mapping |

**これらの数値はExcel cellから抽出した値ではない。** Excelから抽出した5つのheader候補へ、要求とアプリの初期配分規則を適用した既定値である。

### 3.3 現行sampleの既定値（2026-09-16追補）

本番`InputViewModel.CreateSuggestedQuestions`はprimary候補だけを採用し、`CreateDefaultQuestion`は学生Prompt候補をCustomPromptに設定する。`QuantificationDefinition`のbase 60・special 0と`ScoringAllocationCalculator.Equalize`から次を導出する。旧5問表は履歴として残し、10人fixtureの5問・各8点は変更しない。

| Question ordinal | Primary | Supporting | Evaluator | Points |
|---|---|---|---|---:|
| 1 | D | なし | KnowledgeCoverage | 10 |
| 2 | E | F | CustomPrompt | 10 |
| 3 | G | なし | KnowledgeCoverage | 10 |
| 4 | H | I | CustomPrompt | 10 |

Base 60、Special 0、SimilarityPenaltyWeight 0.1、RoundingDigits 1、allocation 100。normal planは530×4＝2,120件、references 4件、checkpoint update 4＋530＝534回を期待する。これは本番ロジック由来のoracleであり、今回のE2E実測成功ではない。

## 4. 使い方

1. 実行したい`ST-UC-01`〜`ST-UC-29`を1件選ぶ。
2. 直下の`text`コードブロック全体を1個だけコピーする。
3. コマンド実行可能なAI agentまたはテスト担当者へ渡す。
4. 各Promptはrepository root、fixture path、evidence directoryを実行時に解決する。原則として置換は不要。
5. Prompt内の報告順、privacy規則、status語彙を変更しない。
6. 複数Promptの一部だけを連結して1件のPASSにしない。
7. 任意testのopt-in、login、external spreadsheetはPromptが明示した場合だけ使用する。

## 5. 共通実行規則

各scenario Promptは次を自己完結的に内包する。

- `StudyReportEvaluator.slnx`を現在directoryから上位探索し、repository rootを確定する。
- Windows 11 x64、PowerShell 7+ Core、`global.json`が選択する.NET SDKを確認する。
- PowerShell処理は`pwsh.exe -NoLogo -NoProfile`だけを使い、Windows PowerShell 5.1へfallbackしない。
- 既存のsource変更をstash、reset、clean、commitしない。
- 長時間testを無出力だけを理由に重複起動しない。
- evidenceはrepository外の一意な一時directoryへ保存し、commitしない。
- 実行前後で対象inputのSHA-256、size、last-write UTCを比較する。
- workbookはread-onlyで読み、元本を変更、再保存、rename、移動、削除しない。
- cell本文、学生情報、Prompt、reference、reason、evidence、credential、token、private absolute pathをterminal、JSON、Markdown、chatへ出力しない。
- test名、技術code、件数、列記号、dimension、hash、basenameは記録できる。
- failure時に期待値を緩めず、初回結果と再実行結果を時系列で残す。
- 実行していないmanual UI、Live AI、external recalculation、package、platformをPASSにしない。
- test certificateまたはunsigned artifactの成功は`PASS_MECHANISM`、production trustとclean target OSの成功だけを`PASS_PRODUCTION`とする。

## 6. Status語彙

| Status | 使用条件 |
|---|---|
| `PASS` | Promptに書かれた必須確認を全て今回実測し、期待と一致 |
| `FAIL` | 実行できたが1件以上が期待と不一致 |
| `BLOCKED` | 必須fixture、tool、permission、対応環境等がなく開始不能 |
| `NOT_RUN` | 対象の実処理を実行していない。必須・任意を別記し、未実装・過去結果をPASSにしない |
| `NOT_RUN_UI_AUTOMATION_UNAVAILABLE` | user-visible UIを観測する手段がない |
| `NOT_RUN_POLICY_REAL_DATA` | 実データのproduction AI送信をpolicyにより実施しない固定status |
| `SKIPPED_NOT_AUTHENTICATED` | optional synthetic Live Copilotだけ、既存loginがない |
| `FAILED_ADVISORY` | optional testを実行したが期待と不一致。required gateへ影響させない |
| `SKIPPED_NOT_INSTALLED` | optional external spreadsheetが未install |
| `NOT_RUN_EXTERNAL_PREREQUISITE` | requiredなfresh OS／本人／Narrator／隔離利用者等、またはoptional external recalculationの前提がなく未実施。必須と任意を別記する |
| `PASS_MECHANISM` | test certificateまたはunsigned artifactでpackage mechanismだけを今回確認 |
| `PASS_DEVELOPMENT` | 開発hostの明記したartifact／観測範囲だけの成功。clean-host／本人認証／公開の成功へ拡張しない |
| `PASS_PRODUCTION` | production identityとclean target OSでrequired trust/journeyを今回確認 |
| `BLOCKED_EXTERNAL` | signing identity、credential、native test host等がなくproduction scopeを開始不能 |
| `RETIRED_CONFIRMED` | 廃止した要求（ST-UC-22〜24）の実装・test・workflow stepが残っていないことを今回確認 |

### 6.1 既存の局所検証と今回のscenario判定

T01で追加したAC-035〜037／TR-34〜36／ST-UC-27〜29／C-045〜047は維持する。要求所有者の2026-09-07の後続承認を優先し、元プランの承認前表記は履歴として読む。現在の実装と直接testは[traceability](../dev/docs/traceability.md)、親担当のT01〜T38 REVIEWED／T39 BLOCKEDとF02最終再検証は[実行記録](../dev/docs/archive/work/20260907-ui-settings-execution-record.md)の最新欄に接続する。以下は0.8.4の記録済み結果で、0.8.5やこのPromptの新規実行結果ではない。

| 既存対象集合 | 記録済み結果・出典 | 今回のPromptへの適用限界 |
|---|---|---|
| T18〜T21 | 540/540、`artifacts/test/ui-settings/t18-21/t18-21-reviewed.trx` | 主画面の対象回帰。後続修正や全体gateの代替ではない |
| T23 | 275/275、`artifacts/test/ui-settings/t23/t23-fixed.trx` | SettingsComposition／MainWindowSettingsと関連回帰。nativeではない |
| T24 | 67/67、`artifacts/test/ui-settings/t24/t24.trx` | WorkflowStateの12ケースを含む。実shell・実設定fileだがrun／出力receiptはfake |
| T25〜T27 | 74/74、`artifacts/test/ui-settings/t25-27/t25-27-reviewed.trx` | T26の27ケースとT27の7ケースは内数。headless layout／keyboardと実file E2Eを分ける |
| T28・最新修正 | 216/216、`artifacts/test/ui-settings/t28/t28-reviewed.trx` | 出力先表示・適用失敗時draft保持・読込競合の最新回帰と、synthetic画像生成・2回一致・最小frame検証。T32／T34の修正確認も同じ集合 |
| T35の対象文書試験 | 4/4、`artifacts/test/ui-settings/t35/t35-reviewed.trx` | 要求mapping・UI/settings baseline・当時の公開文書集合のlocal link／anchorの4件。T36の11文書・8画像contract全体の今回PASSではない |
| T36 | 21/21、`artifacts/test/ui-settings/t36/t36-current.trx` | 公開文書11件・画像8枚、設定境界、対象版、local links／anchors。T35とは別scopeでREVIEWED、F02変更後の文書再検証ではない |
| T37 | 9/9、`artifacts/test/ui-settings/t37/t37.trx` | 実ZIPのpublish／生成／再現性／展開起動（3件）と、廃止済みの開発用package静的契約（6件） |
| T38 | 114/114、`artifacts/test/ui-settings/t38/t38.trx`。同`native/`配下のP06 7/7・P07 PASS_DEVELOPMENT | 20公開ファイル・CLI・標準展開・移動／再起動／同時起動・Prompt設定の開発host検証。EXE 0.8.4、283,408,986 bytes、SHA-256 `4D80FA246EB64E6984B6D8B4A5EA37C1F62BCDD28C40F6D9A63F41522C89E507` |
| T39自動回帰 | Core 190＋App 1702＝1892/1892、skip 0。`artifacts/test/ui-settings/t39/reviewed/`の2026-09-07（+09:00）の2 TRX | CI同等の3クラス除外（実学生sample構造・別実行ZIP・P06）とP02 artifact検査opt-in。初回fixture 1失敗→実入力読込・通常ナビへ修正→126/126・レビュー指摘0→全体再実行成功。実AI等の無効経路をlive成功にしない |

集合は重複し得るため、合算してfull gateを作らない。これらは既存実行の記録で、再実行時の件数を固定するoracleでも、ST-UC全体の今回PASSでもない。実行時は対象メソッドのdiscovery、展開ケース、outcome、環境、fixtureを今回証跡から照合し、未実施caseを既存PASSで埋めない。

`VERIFIED_SCOPED`は追跡上の限定確認で、§6のscenario `PASS`とは別。T35の対象文書試験は4/4成功・敵対的レビュー済み（指摘0）。T35は画像bytesの読込・目視・再生成を行っていない。T36は別scopeの21/21でAC-022／TR-22へ接続し、T38のP06 7/7はAC-032／TR-31の限定確認とする。disk-full／directory ACL／抽出中断／EXE・ZIP間checkpoint再開は未実施で、全faultのPASS_REQUIREDではない。

C-045〜047のBLOCKEDはT36待ちではなく、T39追加native FAILと人手確認等の未達を維持する判定である。最新`artifacts/test/ui-settings/t39/native-final-attempt.json`はCONTROL_ID_PREDICATE_NOT_UNIQUEでFAIL、3試行後は再試行を停止した。120 DPI・実client 1475×1000 pixel＝1180×800 DIP・合成入力読込・入力／EXE不変・実利用者設定非作成だけを観測し、4画面・5カテゴリ・1024×720・実keyboardは未完了。Narrator、本人walkthrough4項目、隔離利用者native保存とCH-01〜06はNOT_RUN_EXTERNAL_PREREQUISITE。本人login・実AI・実学生data・公開操作は未実施で、F01／F02の自律続行をG4・全タスクDONE・公開PASSへ拡張しない。

## 7. 共通報告契約

各scenarioは、Prompt内により厳密な形式がなければ次の順で報告する。

1. Test ID、External run ID、UTC開始／終了
2. 対象commit、開始時worktree status hash
3. OS／architecture、PowerShell、.NET
4. 前提とfixture identity
5. 実施内容と実行したtest名
6. 期待結果
7. 今回の実測結果
8. 本文を含まない証跡pathとSHA-256
9. scope別Status
10. 差異、再現手順、影響
11. 確認済み範囲、未確認範囲、非保証事項

## 8. Scenario一覧

| Scenario | 内容 | Test Requirement / Acceptance Criteria |
|---|---|---|
| ST-UC-01 | Forms/Google row 1/2、10人fixture identity・mapping・設問text同期 | TR-01 / AC-001、AC-002 |
| ST-UC-02 | Canonical SampleReport identity・role候補 | TR-02 / AC-003 |
| ST-UC-03 | picker、direct path、unsupported形式 | TR-03 / AC-001、AC-002 |
| ST-UC-04 | 配点、均等化、range、validation | TR-04、TR-05 / AC-004〜AC-006、AC-008 |
| ST-UC-05 | Knowledge／Custom／Special Promptとclosed schema | TR-06 / AC-007、AC-008 |
| ST-UC-06 | reference／normal／special／similarity境界 | TR-07、TR-08 / AC-009、AC-010、AC-012 |
| ST-UC-07 | score hand oracleとConfig参照formula | TR-09、TR-10 / AC-006、AC-011、AC-012 |
| ST-UC-08 | final workbook、output path、atomic commit | TR-11、TR-12 / AC-013 |
| ST-UC-09 | checkpoint、fault、resume mismatch | TR-13、TR-14 / AC-014、AC-015 |
| ST-UC-10 | auth／timeout／network／schema／cleanup／cancel | TR-15 / AC-012、AC-016 |
| ST-UC-11 | same-row privacy、literal、no-content log | TR-16 / AC-019 |
| ST-UC-12 | 4-step＋設定、通常非scroll／ページ切替、長文・拡大例外、mapping同期、warning、accessibility | TR-17 / AC-002、AC-016、AC-017 |
| ST-UC-13 | command-line Prompt launch | TR-18 / AC-018 |
| ST-UC-14 | Windows publish、bundled CLI、clean launch | TR-19 / AC-020 |
| ST-UC-15 | ZIP integrity、sidecar、再現性、tamper | TR-20 / AC-020 |
| ST-UC-16 | Windows-only claim、docs、screenshots | TR-21、TR-22 / AC-021、AC-022 |
| ST-UC-17 | 10人fixture new→interrupt→resume E2E | 補助scenario / AC-009〜AC-016、AC-019 |
| ST-UC-18 | fixed-seed 531-row synthetic E2E | TR-23 / AC-009〜AC-016、AC-019 |
| ST-UC-19 | Canonical SampleReport no-network technical E2E | 旧実データE2E / AC-009〜AC-016、AC-019 |
| ST-UC-20 | optional authenticated synthetic Copilot | TR-24A |
| ST-UC-21 | optional external spreadsheet recalculation | TR-24B |
| ST-UC-22 | 廃止した開発用packageの削除確認（RETIRED） | TR-25 / AC-023、AC-027 |
| ST-UC-23 | 廃止したWindows以外の配布基盤の削除確認（RETIRED） | TR-26 / AC-024 |
| ST-UC-24 | 廃止した将来署名順序契約の削除確認（RETIRED） | TR-27 / AC-025 |
| ST-UC-25 | Windows ZIP E2E、release matrix、secret boundary | TR-28、TR-29 / AC-026〜AC-028 |
| ST-UC-26 | clean-host単一EXE起動・同梱CLI状態確認・本人login・公開境界 | TR-30〜TR-33 / AC-029〜AC-034 |
| ST-UC-27 | 共通＋定義1件の明示保存、平文境界、atomic失敗、明示出力先復元／null | TR-34 / AC-035 |
| ST-UC-28 | 保存定義の明示適用、別header検証、失敗・取消無変更、ID／Prompt保持 | TR-35 / AC-036 |
| ST-UC-29 | 設定往復、model希望、no-auto、現在snapshot／前回結果保持、保存→再読込→fake run E2E | TR-36 / AC-016〜AC-019、AC-035〜AC-037 |

## 9. Scenario Prompt

以降の各コードブロックは単独でコピーして実行できる。

### ST-UC-01: Forms／Google question row 1/2と10人fixture mapping

```text
あなたはStudyReport Evaluator v4.6のシステムテスト担当者です。
Test ID: ST-UC-01
Requirement: TR-01 / AC-001 / AC-002

このPromptだけで最後まで実行してください。捏造禁止。未実施、過去結果、文書記載値を今回のPASSにしません。repository source、fixture、既存変更を修正、stash、reset、clean、commitしません。学生回答、header本文、Prompt、reason、evidence、credential、token、private absolute pathをterminal、report、chatへ出力しません。Windowsの処理はpwsh.exe -NoLogo -NoProfile、PowerShell 7+ Coreだけを使います。同じ長時間commandを重複起動しません。

実行準備:
1. 現在directoryから上位へ`StudyReportEvaluator.slnx`を探索してrepository rootを確定し、そこへ移動します。見つからなければBLOCKEDです。
2. Windows 11 x64、PowerShell Core 7+、`dotnet --version`が`global.json`の選択結果であることを確認します。
3. UTC開始時刻、HEAD、`git status --porcelain=v1 --untracked-files=all`のUTF-8 SHA-256と件数を記録します。status本文はreportへ転載しません。
4. repository外に一意なevidence directoryを作ります。
5. `dotnet restore .\StudyReportEvaluator.slnx --locked-mode`を1回実行します。

対象fixture:
- `tests/fixtures/system-test/SystemTest-10Students.xlsx`
- expected bytes: 7,652
- expected SHA-256: 7F473879B3C43319D842881C5DFD1D01B2A60091A5B0C7B39B70B8D574D801C8
- expected worksheet/dimension: SystemTestInput / A1:L11
- header row 1、data rows 2〜11

実行:
1. fixtureのSHA-256、size、last-write UTCを取得します。
2. 次のtest classをReleaseで実行します。test名と今回のpassed/failed/skippedだけを記録し、TRXはrepository外へ保存します。
	- `StudyReportEvaluator.App.Tests.E2E.TenPersonSystemSmokeTests.Fixture_has_fixed_identity_structure_and_literal_formula_canaries`
	- `WorkbookMetadataReaderTests`
	- `ColumnMappingSuggesterTests`
	- `ColumnMappingValidatorTests`
	完全修飾method名はその1件だけ、末尾が`Tests`のclass名は当該classでdiscoverされた全testを意味します。別classへ拡張しません。
3. 既存test factoryによるMicrosoft Forms型とGoogle Forms型について、question text row 1と2を個別に検証します。実在データを新規作成しません。
4. fixtureをproduction read-only loaderへ通し、初期definitionを本文非表示で検査します。
5. 実行後にfixture identityを再取得します。

必須oracle:
- 標準`.xlsx`としてread-only loadできる。
- question text rowは1または2だけを選択でき、first data rowはその後である。
- timestamp、email、氏名等の管理列は初期primary候補にならない。
- header semanticからnormal answer、student Prompt、Prompt considerations候補を作る。
- 10人fixtureではprimary F/G/H/I/J、Jのsupporting K、A〜E/Lは初期対象外となる。
- candidateは利用者が変更でき、列位置だけで固定mappingしない。
- question row 1と2の各fixtureで主回答列を変更すると、同じrow・columnの交差セル値が同じ設問textへ即時反映される。
- 設問textを手入力した後に主回答列を選び直すと、新しい交差セル値で置き換わる。空または欠落headerでは代替文を生成せず、`REQUIRED`で手入力を要求する。
- question row変更後・metadata再読込前に主回答列を変更しても旧rowの値を反映せず、`HEADER_METADATA_MISMATCH`で再読込を要求する。
- fixtureにはformula、macro、external link、DDE、defined nameがなく、`= + - @` canaryはliteral stringである。
- F〜Jはそれぞれ空または空白primaryがexactly 1件ある。
- 読込前後のSHA-256、size、last-write UTCが一致する。

UIで未観測の内部候補はmetadata/mapping testの結果として報告し、画面で確認したと書きません。全必須oracleが今回一致した場合だけPASS、実行できた不一致はFAIL、環境・fixture不足はBLOCKEDです。

報告順: Test ID / External run ID・UTC開始終了 / commit・status hash・OS・architecture・PowerShell・.NET / fixture identity / 実行test / 期待 / 実測 / identity不変 / 非機密証跡 / Status / 差異と再現手順 / 未確認範囲。
```

### ST-UC-02: Canonical SampleReport identityとD〜I role候補

```text
あなたはStudyReport Evaluator v4.6のcanonical repository sampleシステムテスト担当者です。
Test ID: ST-UC-02
Requirement: TR-02 / AC-003 / docs/requirements-definition.md §4.4

捏造禁止。`sample/SampleReport.xlsx`が存在しない場合、10人fixtureで置換せずBLOCKEDと報告して終了します。期待値に合わせてfileを作成・変更しません。sampleの回答cell、氏名、email、Prompt本文、header本文を読み出し・表示・AI送信しません。構造metadata、列記号、file identityだけを扱います。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。sourceや既存変更をstash、reset、clean、commitしません。

準備:
1. `StudyReportEvaluator.slnx`を上位探索してrepository rootへ移動します。
2. Windows 11 x64、x64 process、PowerShell Core 7+、global.json選択SDKを確認します。
3. UTC開始、HEAD、worktree statusの件数とUTF-8 SHA-256を記録します。
4. `sample/SampleReport.xlsx`の存在を確認します。なければStatus=BLOCKED、code=`BLOCKED_CANONICAL_SAMPLE_MISSING`とし、存在すると偽りません。
5. repository外にevidence directoryを作ります。

履歴identity（2026-09-16利用者明示承認により固定一致は非ゲート）:
- historical bytes: 470,806
- historical SHA-256: 73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA

存在する場合の期待構造:
- StandardXlsxとして受理され、read-onlyで取得した実ZIPのEntries.Countとclassification.PackagePartCountが一致する。
- metadataのpackage／relationship件数がclassificationと一致し、relationship件数は正かつclassifierの安全上限内、external relationshipsは0。
- 旧11 entries／8 relationshipsは履歴であり固定gateではない。2026-09-16の観測は469,976 bytes、13 entries／9 relationships、external 0、StandardXlsx受理であり、新たな固定gateやdimension・mappingのPASS証跡にしない。旧fileの元bytesがなく正確な差分は証明できない。
- worksheet/dimension: 1件（nameはSHA-256だけを記録） / A1:J531
- rows/columns: 531行／10列、data 530行（2026-09-16測定済みprofileを採用。旧12列は履歴）
- initial source: single worksheet、question row 1、data rows 2〜531

期待role候補:
- A/B/C/J: 初期対象外
- D/G: PrimaryAnswer候補
- E/H: PrimaryAnswer + StudentPromptPrimary候補
- F/I: Supporting候補
- Eのinitial supportingはF、HはI。他候補のsuggested supportingは空。
- targetはD/E/F/G/H/I。header semanticから得た候補であり固定mappingではない。
- 測定出典はTestResults/app-fixes-20260916/sample-profile/dahatake_DAHATAKE-OFFICE_2026-09-16_13_59_37_net10.0.trxの標準出力。旧期待値との不一致でFAILだった実行を、今回のPASSへ代用しない。

実行:
1. 検査前のSHA-256、size、last-write UTCを取得します。
2. productionの`FileFormatClassifier`、`WorkbookMetadataReader`、`ColumnMappingSuggester`をread-only経路で検証する既存testをReleaseで実行します。必要なlocked restoreは1回だけ行います。
3. package entry、relationship、sheet、dimension、row、候補roleを本文非表示で検査します。
4. 検査後のidentityを取得し、前後比較します。

判定:
- file不足はBLOCKEDでありFAILやPASSへ変換しない。
- 旧bytes・SHA-256・sheet name hash・11 entries／8 relationshipsとの不一致だけではFAIL／BLOCKEDにしない。旧測定値は履歴のまま保持し、一致を未確認のまま主張しない。
- fileが存在し、構造・role候補が異なる場合、または実行前後のSHA-256・size・last-write UTCが異なる場合はFAIL。
- 全期待構造・role候補と元本不変を今回確認した場合だけPASS。

報告順: Test ID / UTC開始終了 / commit・環境 / file存在 / identity / package構造 / role候補 / input不変 / test実測 / 非機密証跡 / Status / 差異。sample本文とprivate pathは掲載しません。
```

### ST-UC-03: Native picker、direct path、unsupported input

```text
あなたはStudyReport Evaluator v4.6のinput workflowシステムテスト担当者です。
Test ID: ST-UC-03
Requirement: TR-03 / AC-001 / AC-002

捏造禁止。実在データを使わず、既存test factoryが一時directoryへ作るsynthetic fixtureだけを使います。file本文、header本文、private pathをerrorやreportへ含めません。repository fileを変更、stash、reset、clean、commitしません。PowerShell処理はpwsh.exe -NoLogo -NoProfile、PowerShell 7+ Coreだけを使います。

準備:
1. `StudyReportEvaluator.slnx`を上位探索しrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json選択SDKを確認し、HEAD、UTC開始、worktree status hashを記録します。
3. repository外にevidence directoryを作り、locked restoreを1回行います。
4. `FileFormatClassifierTests`、`InputViewTests`、`MainWindowTests`、picker service testsをReleaseで実行します。既存の対象testがない確認項目はmanual UIまたはheadless deterministic testとして区別します。

単一原因case:
1. native picker cancel: 既存Input stateとpathが変わらず、buttonを再利用できる。
2. native picker select: 1件の`.xlsx`だけを同じread-only loaderへ渡す。
3. pickerがlocal pathを提供しない: cancelと区別したsafe案内を表示し、direct path入力を維持する。
4. direct path: 日本語と空白を含むabsolute pathを読める。
5. `.csv`、`.xls`、`.xlsb`、`.xlsm`、PDF、encrypted/protected、corrupt ZIP、external relationship等のunsafe packageを個別に拒否する。
6. file path編集時に旧metadata、snapshot、sheet、mappingをclearする。
7. picker filterをsecurity validationの代替にせず、direct pathも同じclassifierへ通す。

期待:
- unsupported fileはloaded stateにならない。
- errorはsafe technical codeだけを示し、pathや内容をechoしない。
- cancel caseはvalidation errorとして扱わない。
- 各caseは前caseのstateを戻してから実行し、原因を混在させない。
- fixtureの検査前後identityは一致する。

全caseを今回確認した場合だけPASS。不一致はFAIL。Desktop pickerを観測できず、対応headless testもない項目があれば、その項目をNOT_RUNとしてscenario全体をPASSにしません。必須tool不足はBLOCKEDです。

報告順: Test ID / 環境 / case別実施方法（manual/headless） / expected code・state / 実測 / input不変 / 証跡 / Status / 差異と再現手順。
```

### ST-UC-04: 絶対配点、均等化、range、run前validation

```text
あなたはStudyReport Evaluator v4.6の採点設計システムテスト担当者です。
Test ID: ST-UC-04
Requirement: TR-04 / TR-05 / AC-004 / AC-005 / AC-006 / AC-008

捏造禁止。決定的なCore/UI testだけを使い、Live AI scoreを使いません。入力workbookや既存成果物を変更しません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使用します。各invalid caseは単一原因にし、前caseの値をvalidへ戻してから次へ進みます。

準備:
1. repository rootを`StudyReportEvaluator.slnx`から上位探索します。
2. Windows 11 x64、PowerShell Core 7+、global.json選択SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外にevidence directoryを作り、locked restoreを1回行います。
4. `ScoringAllocationCalculator`、`QuantificationDefinitionValidator`、`QuantificationDesignViewModel`、Design view testsに対応するCore/App testをReleaseで実行します。

既定値の出典:
- BasePoints=60、SpecialPoints=0、SimilarityPenaltyWeight=0.1、RoundingDigits=1は要求定義§5.1とアプリ初期definition。
- Excel cellに埋め込まれた値ではない。
- 旧sample／10人fixtureのheader由来5問ではQuestion Pointsは各8で、60+0+5×8=100。2026-09-16採用の現行sampleは4問・各10で、60+0+4×10=100。

配分oracle:
- enabled question 2件、Base 60、Special 0: 20、20。
- enabled question 3件: 13.333333、13.333333、13.333334。6 decimal placesで最後のenabled questionへ差分を入れ、合計exact 100。
- question追加、Base/Special編集、enable/disableだけでは既存の手動Pointsを自動変更しない。
- 「設問配点を均等化」を明示実行した時だけenabled questionへ残点を再配分する。
- disabled questionのPointsは均等化で変更しない。
- snapshot作成後のdraft変更が既存snapshotを変更しない。

validation matrix:
- BasePointsとSpecialPointsは0〜100。
- SimilarityPenaltyWeightは0〜1。
- enabled Question Pointsは0以上の有限decimal。
- `Base + Special + Σ enabled Question Points`はdecimal exact 100。99.999999と100.000001を許容しない。
- SpecialPoints>0ならenabled special itemが1件以上必要。
- enabled evaluator/criterion weightは正数。
- minimum < maximum。
- rounding digitsは0〜6。
- question text rowは1または2。
- invalid中はsnapshot作成とrun開始をblockする。

期待code:
`BASE_POINTS_OUT_OF_RANGE`、`SPECIAL_POINTS_OUT_OF_RANGE`、`SIMILARITY_WEIGHT_OUT_OF_RANGE`、`QUESTION_POINTS_OUT_OF_RANGE`、`ALLOCATION_TOTAL_INVALID`、`SPECIAL_ITEMS_REQUIRED`。実装が追加の具体codeを返す場合は実測名を記録し、期待へ書換えません。

全配分・保持・snapshot・validation caseが今回一致した場合だけPASS。不一致はFAIL、実行不能はBLOCKEDです。

報告順: Test ID / 環境 / default出典 / case / deterministic oracle / expected code・field / 実測 / test件数 / 証跡 / Status / 差異。
```

### ST-UC-05: Knowledge／Custom／Special Promptとclosed tool schema

```text
あなたはStudyReport Evaluator v4.6のPrompt・structured resultシステムテスト担当者です。
Test ID: ST-UC-05
Requirement: TR-06 / AC-007 / AC-008

捏造禁止。合成Promptとfake transportだけを使い、Prompt本文をlog、error artifact、reportへ出しません。Live AIを呼びません。repositoryを変更、stash、reset、clean、commitしません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。

準備:
1. `StudyReportEvaluator.slnx`を上位探索してrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外にevidence directoryを作り、locked restore後、Prompt renderer、definition validator、schema factory、tool collector、runnerのCore/App testをReleaseで実行します。

必須case:
1. Knowledge: app-owned semantic templateはread-onlyで、知識の「説明・関係・適用」を扱い、keyword存在だけで満点にしない。利用者templateへ置換できない。
2. Custom: `{回答}`と`{評価項目}`が必須。`{設問}`、`{補助情報}`、`{最小点}`、`{最大点}`は任意。
3. Special: `{回答}`が必須で、`{評価項目}`は必須ではない。
4. `{{`と`}}`はliteral brace。挿入値はsingle-passであり再展開しない。
5. unknown、unclosed、nested、unmatched braceをrun前に拒否する。
6. 6 placeholder制約は利用者編集可能なCustom／Specialだけに適用する。app-owned Referenceは固定`{設問}`だけを限定置換し、Similarityは固定sectionへ値を直接組み立てる。`{参照回答}`を第7placeholderとして許可しない。
7. 公開toolはoperationごとにexactly 1件: normal=`submit_quantification`、reference=`submit_reference_answer`、special=`submit_special_quantification`、similarity=`submit_similarity`。
8. schemaは`additionalProperties=false`、required IDはexact enum、normal raw rangeとspecial/similarity 0〜1を閉じる。
9. duplicate tool call、missing result、unknown ID、partial resultをoperation全体で拒否する。
10. invalid Promptではinput row read、Copilot session作成、runner callが全て0件である。
11. AIはcriterion raw／special score／similarityだけを返し、配点、最終点、合否を返さない。

各operationとinvalid原因を別fixtureで実行し、call/session/tool countはtest doubleから取得します。期待に合わせて証跡を書換えません。

全必須caseが今回一致した場合だけPASS。不一致はFAIL、必要test/tool不足はBLOCKEDです。

報告順: Test ID / 環境 / operation / case / expected schema・code・count / 実測 / Prompt本文非記録確認 / 証跡 / Status / 差異。
```

### ST-UC-06: Reference-firstとnormal／special／similarity境界

```text
あなたはStudyReport Evaluator v4.6の4 AI operationシステムテスト担当者です。
Test ID: ST-UC-06
Requirement: TR-07 / TR-08 / AC-009 / AC-010 / AC-012

捏造禁止。fake transportと制御fixtureだけを使い、repository sampleや実データをAIへ送りません。reference、回答、Prompt、reason、evidence本文を証跡へ記録しません。Live AI期待値を使いません。各operationとfailureを単一原因で検証します。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。

準備:
1. repository rootを`StudyReportEvaluator.slnx`から上位探索します。
2. Windows 11 x64、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外へevidence directoryを作り、locked restore後、reference runner、special runner、similarity runner、durable orchestrator、result validatorのCore/App testsをReleaseで実行します。

Reference-first／resume:
- enabled question 2件のrunで、student rowを読む前にreference phaseを実行する。
- 1 question/runにつきreference operationはexactly 1回。
- reference/similarity model IDはliteral `auto`で、別modelへfallbackしない。
- 各reference完了後にpartial checkpointを更新する。
- SUCCESSは非空answerとexact QuestionIdを持つ。
- invalid/timeout/network/auth failureはanswer blankと技術statusを保存する。
- resumeでは保存済みreferenceを再生成せず、未生成questionだけを続行する。
- final References sheetへquestion、answer、`auto`、status、UTC timestampを保存する。

数値境界:
- normal rawはcriterion effective rangeのminimum/maximumを受理し、範囲外、NaN、Infinityをrejectする。
- special scoreとsimilarityは0と1を受理し、負数、1超、NaN、Infinityをrejectする。
- unknown ID、duplicate、partial resultをrejectする。

empty-zero:
- 空normal primaryはAI call 0、QuestionRate=0、QuestionEarned=0。
- 空special primaryはAI call 0、special score=0。
- 空student answerはsimilarity call 0、similarity=0、penalty=0。
- supportingだけが非空でもprimaryが空なら送信しない。
- SpecialPoints=0ではspecial runner call 0、status=`NOT_RUN_ZERO_BUDGET`、SpecialEarned=0。これは要求定義§8.2の「固有評価AIを実行しない」を表す既存実装`ResultsStatusCodes.NotRunZeroBudget`の技術statusであり、新しい要求や利用者設定ではない。

technical-failure-blank:
- nonempty入力のschema/timeout/network/auth/cleanup failureはraw blankで、0へ変換しない。
- reference failure時は当該similarityを送信せずblankにする。
- technical blankはQuestionEarned、SpecialEarned、SimilarityPenalty、FinalRaw、FinalScoreへblank伝播する。
- empty-zeroとtechnical-blankを同じtest caseへ混在させない。

attempt/session/call countとcheckpoint順はtest double/store記録から取得します。各cellのtypeとcached previewも確認します。全必須case一致だけをPASSとし、不一致はFAIL、前提不足はBLOCKEDです。

報告順: Test ID / environment / operation / case / expected model・range・status・call count / checkpoint順 / 実測 / cell type・cache / 非機密証跡 / Status / 差異。
```

### ST-UC-07: Score hand oracleとConfig参照formula

```text
あなたはStudyReport Evaluator v4.6のscore・Excel formulaシステムテスト担当者です。
Test ID: ST-UC-07
Requirement: TR-09 / TR-10 / AC-006 / AC-011 / AC-012

捏造禁止。次の固定合成oracleだけを使い、Live AI scoreや過去workbookの値を期待値へ流用しません。Excel結果だけを正本にせず、独立手計算、Core計算、formula cached preview、read-only reopenを比較します。repository fileを変更、stash、reset、clean、commitしません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使用します。cell本文とprivate pathを証跡へ出しません。

準備:
1. `StudyReportEvaluator.slnx`を上位探索してrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json選択SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外にevidence directoryを作り、locked restoreを1回行います。
4. `WeightedScoreCalculatorTests`、`FormulaSerializerTests`、`FormulaPreflightValidatorTests`、`FormulaCellWriterTests`、`ConfigAndRunSheetWriterTests`、`ResultsSheetWriterTests`、`OutputPackageValidatorTests`をReleaseで実行します。末尾が`Tests`の指定はそのclassでdiscoverされた全testを意味します。
5. 固定oracleのCore比較は`WeightedScoreCalculatorTests.V4_system_prompt_hand_oracle_produces_86_point_8`の実測を必須とします。別入力の87.86 oracle testを代用しません。

固定oracle:
- BasePoints=60、SpecialPoints=10、SimilarityPenaltyWeight=0.1、RoundingDigits=1。
- Q1: Points=20、normalized=80、similarity=0.5、special question rate=0.8。
- Q2: Points=10、normalized=50、similarity=0.2、special question rate=0.6。

独立期待値:
- Q1 QuestionRate=0.8、QuestionEarned=16.0、SimilarityPenalty=1.0。
- Q2 QuestionRate=0.5、QuestionEarned=5.0、SimilarityPenalty=0.2。
- SpecialEarned=10×average(0.8,0.6)=7.0。
- FinalRaw=60+16+5+7-1-0.2=86.8。
- FinalScore=86.8。
- FinalRaw<0ならFinalScore=0、FinalRaw>100ならFinalScore=100。
- midpoint roundingはAwayFromZero。各段階でroundした値を次段へ使う。

formula surface:
- Quantification_ConfigのAllocationTotalとAllocationValidだけがapp-owned formula。他のConfig cellはliteral。
- Quantification_ResultsのEffectiveRaw、Normalized、Evaluator、QuestionRate、QuestionEarned、SpecialQuestionRate、SpecialEarned、SimilarityPenalty、FinalRaw、FinalScoreをclosed ASTから生成する。
- points、range、rounding、similarity weightをformulaへliteral埋込みせず、実際のConfig sheet cellへabsolute referenceする。
- formula storage textはleading `=`を持たない。
- allowlist外function、raw user formula、external workbook、DDE、defined-name injectionをrejectする。
- target重複、unknown reference、cycleをrejectする。
- Excel上限をwrite前に検査する: formula length 8191、function arguments 255、column 16384、cell 32767 characters。
- expected formula planはConfigとResults双方をexactly once含む。
- AllocationValid≠1または必要なtechnical rawがblankならFinalRaw/FinalScoreはformula付きblank cacheとなる。
- app-owned formula以外のtextはInlineStringでCellFormulaを持たない。

検証:
1. 手計算を式と途中値付きで独立に実施します。
2. Core calculatorの各段階を手計算と比較します。
3. synthetic workbookへformula planを書き、cached previewをCore値と比較します。
4. fileをcloseしread-only reopenして、formula text、Config reference、cached type/value、OpenXmlValidator error countを検査します。
5. hostile formula caseは専用一時fixtureで単一原因ごとに行います。

全oracle、preflight、writer、reopen結果が今回一致し、Open XML error 0の場合だけPASS。不一致はFAIL、必要tool不足はBLOCKEDです。

報告順: Test ID / 環境 / 固定oracle / 手計算 / Core / formula plan / workbook cache / hostile case別code / Open XML validation / 証跡 / Status / 差異。
```

### ST-UC-08: Final workbook、output path、no-overwrite atomic commit

```text
あなたはStudyReport Evaluator v4.6のfinal workbook・output commitシステムテスト担当者です。
Test ID: ST-UC-08
Requirement: TR-11 / TR-12 / AC-013

捏造禁止。一時synthetic workbookだけをwrite対象とし、canonical sampleと10人fixtureを変更しません。既存利用者fileを削除・上書きしません。cell本文とprivate absolute pathは報告しません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使い、repository変更をstash、reset、clean、commitしません。

準備:
1. `StudyReportEvaluator.slnx`を上位探索しrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外に一意なtest/evidence directoryを作り、locked restoreを1回行います。
4. `ConfigAndRunSheetWriterTests`、`ReferenceAnswersSheetWriterTests`、`ResultsSheetWriterTests`、`OutputPathPlannerTests`、`AtomicOutputCommitterFaultTests`、`OutputPackageValidatorTests`、`WorkingPackageTests`をReleaseで実行します。

final workbook oracle:
- inputの全sheetが同じ順序、名前、part identityで保持される。
- app-owned sheetは`Quantification_Config`、`Quantification_References`、`Quantification_Results`、`Quantification_Run`のexact 4件。
- finalに`Quantification_Checkpoint`はない。
- ReferencesとRunはformula 0。
- Config formulaはAllocationTotal/AllocationValidの2件だけ。
- Results formulaはclosed expected planとexact一致する。
- untrusted textは先頭が`= + - @`でもInlineStringでありCellFormulaを持たない。
- inputに同名または大小文字違いのsheetがある場合、既存sheetを変更せず` (2)`、` (3)`の最小suffixを使う。
- Run sheetへ実際に解決した4 sheet namesを記録する。
- OpenXmlValidator error 0。

output path oracle（local minute 2026-09-02 14:35固定）:
- 明示出力先がnullの場合だけdefault directoryはinputの隣の`result`で、runの既存path準備時に必要なら作成する。設定復元だけで作成せず、再起動・入力変更後の明示出力先保持はST-UC-27で別に検証する。
- first pairは`eval-20260902-1435.xlsx`と`eval-20260902-1435.partial.xlsx`。
- finalまたはpartialのどちらかが存在すれば、次は共通suffix`-02`。
- `-02`のどちらかが存在すれば`-03`。
- fileだけでなく同名directoryもoccupiedとして扱う。
- reserve後に第三者がfinalを作ったraceでは上書きせず`TARGET_EXISTS`。

atomic final oracle:
- target-local working copyを作成し、flush、close、read-only reopen、formula plan/Open XML validationを行う。
- input identity再確認後、same-volume no-overwrite moveでfinalを作る。
- move前のwrite、flush、validation、cancel、input drift、output invalid faultではfinalを作らず、既存targetを変えない。
- move後例外でもfinal存在・temp不存在・validを確認できた場合だけSUCCESS。
- test終了時にapp-owned tempをcleanupし、今回作ったtest directoryだけを削除する。

各faultは単一原因で実行し、input、既存target、final、partialのSHA-256と存在状態を前後比較します。pathはreportではbasenameだけを使用できます。

全caseが今回一致し、inputと既存targetが不変の場合だけPASS。不一致はFAIL、環境・tool不足はBLOCKEDです。

報告順: Test ID / 環境 / input identity / case / output basename・存在状態 / sheet set / formula counts / validation / atomic fault結果 / cleanup / Status / 差異。
```

### ST-UC-09: Checkpoint、atomic fault、resume mismatch

```text
あなたはStudyReport Evaluator v4.6のdurable checkpoint・resumeシステムテスト担当者です。
Test ID: ST-UC-09
Requirement: TR-13 / TR-14 / AC-014 / AC-015

捏造禁止。一時synthetic inputだけを使い、checkpoint payload、回答、reference、Prompt、reason、evidence本文をterminal、report、chatへ出しません。mismatchごとにcheckpoint copyを使い、正本partialを変更しません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。repositoryを変更、stash、reset、clean、commitしません。

準備:
1. `StudyReportEvaluator.slnx`を上位探索しrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外に一意なtest/evidence directoryを作り、locked restoreを1回行います。
4. `CheckpointStoreTests`と`DurableQuantificationOrchestratorTests`をReleaseで実行します。

partial create／payload oracle:
- run開始時にinput byte-copyへ`Quantification_Checkpoint`だけを追加した`.partial.xlsx`をCreateNewする。
- inputに同名sheetが大小文字違いで存在する場合は作成を拒否する。
- METAはSchemaVersion、PayloadSha256、ChunkCountだけを持つ。
- PAYLOADはcanonical UTF-8 JSONを各30,000 characters以下、0-based連続indexで保存する。
- normal、special、similarity、token usageを1つのCheckpointCompletedRowとしてround-tripする。
- unknown row/column/property、duplicate key/chunk、missing chunk、index gap、hash mismatch、unsupported schemaを個別に拒否する。

atomic update oracle:
- reference完了およびcomplete student rowごとにtarget-local tempを作り、reopen validation後にFile.Replaceする。
- replace前のwrite、flush、validation、cancel、replace faultで旧partial hashが変わらない。
- replace後例外はnew payloadが実在・validな場合だけSUCCESS。
- tempはresume sourceにならず、通常fault後にcleanupされる。
- input SHA-256、size、mtime drift時は更新しない。

resume mismatch matrix（各copyへ単一mutation）:
- checkpoint schema
- input path、SHA-256、size、last-write UTC
- definition canonical JSON／SHA-256
- normal model ID
- reference/similarity model ID `auto`
- app major compatibility
- CLI version／SHA-256
- SDK version
- preserved source worksheet identity
- completed row number、order、expected IDs
- accepted normal range／evidence source
- special/similarity IDと0〜1 range

resume期待:
- 各mismatchを具体的safe codeで拒否し、AI call、checkpoint update、final createは0。
- rejected checkpointとinputのhashは変わらない。
- valid resumeは保存済みreferenceを再生成しない。
- complete rowだけskipし、最初の未完了rowから続ける。
- row途中の結果はskipせず、そのrow全体を再実行する。
- 別orchestrator/process相当でpartialをread-only reopenしても同じ動作。
- success後だけpartialを削除する。cleanup failureはvalid finalを無効にせずwarningを返す。

全構造、fault、mismatch、valid resume caseが今回一致した場合だけPASS。不一致はFAIL、前提不足はBLOCKEDです。

報告順: Test ID / 環境 / package structure / metadata dimensions / faultまたはmismatch dimension / expected safe code / AI call・update・final count / hash不変 / resume結果 / cleanup / 証跡 / Status / 差異。
```

### ST-UC-10: Auth／timeout／network／schema／cleanup／cancel

```text
あなたはStudyReport Evaluator v4.6のAI failure・cancelシステムテスト担当者です。
Test ID: ST-UC-10
Requirement: TR-15 / AC-012 / AC-016

捏造禁止。Live障害を人為的に起こさず、fake transportとcontrolled cancellationだけを使います。exception本文、session content、Prompt、response、reason、evidenceを証跡へ出しません。repositoryを変更、stash、reset、clean、commitしません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。

準備:
1. `StudyReportEvaluator.slnx`を上位探索してrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外にevidence directoryを作り、locked restoreを1回行います。
4. retry/cleanup coordinator、normal/reference/special/similarity runner、durable scheduler/orchestrator testsをReleaseで実行します。

normal、reference、special、similarityの各operationで単一原因ごとに検証:
- schema invalidは新sessionで最大1回だけ再試行後、`AI_OUTPUT_INVALID`でblank。
- timeoutとtransient networkは新sessionで最大2回再試行後、`AI_TIMEOUT`／`NETWORK_FAILED`でblank。
- auth failureは無駄に再試行せず`AUTH_REQUIRED`でblank。
- cleanup failureは`CLEANUP_FAILED`とし、同一coordinatorで新規operationをinterlockする。
- start前cancelはattempt 0、session 0、send 0。
- in-flight cancelはabort→dispose/deleteをfinite timeoutで行う。
- cancel受付後、semaphore待機中operationはrunnerへ到達せず、新規AI sendは0。
- cancel後に新規sessionを開始しない。
- 完了済みreference/rowだけcheckpointへ残り、in-progress row全体はdurable completeにならない。
- observer/progress callback例外はrun data、status、checkpoint payloadを変更しない。
- cleanup failure後の追加retryは0。

既定attempt timeoutは120秒（起動からの外側上限。AIの応答待ちはSDK `SendAndWaitAsync`の既定60秒）だが、testはvirtual/controlled timeoutを使い実時間待機を行いません。attempt、session、send、abort、dispose、delete、checkpoint countをtest doubleから取得します。期待値に合わせてcountを書換えません。

全operation×failure/cancel caseが今回一致した場合だけPASS。不一致はFAIL、test fixture/tool不足はBLOCKEDです。

報告順: Test ID / 環境 / operation kind / failure kind / expected status / attempt・session・send・cleanup count / durable row count / 実測 / 非機密証跡 / Status / 差異。
```

### ST-UC-11: Selected same-row privacy、literal text、no-content log

```text
あなたはStudyReport Evaluator v4.6のprivacy・formula injectionシステムテスト担当者です。
Test ID: ST-UC-11
Requirement: TR-16 / AC-019

捏造禁止。合成canaryだけを使い、実在学生本文を扱いません。canary値自体を最終reportへ掲載せず、source ID、存在／不在、件数だけを記録します。repositoryを変更、stash、reset、clean、commitしません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。

準備:
1. `StudyReportEvaluator.slnx`を上位探索してrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外にevidence directoryを作り、locked restoreを1回行います。
4. Safe payload builder、row source、normal/reference/special/similarity runner、SafeLogger、UntrustedString/Config/References/Results/Checkpoint writer testsをReleaseで実行します。

payload isolation oracle:
- reference: current question textだけ。student row、workbook path、他questionを含めない。
- normal: current rowのselected primary/supporting + definition/schema metadataだけ。
- special: current rowのselected special primary/supportingだけ。
- similarity: current row primary + same question referenceだけ。
- 各payloadへ未選択列、別row、別question、workbook pathの異なるcanaryを置き、rendered Prompt、tool args、logsのいずれにもない。
- evidence sourceはsame-row selected stable source column IDだけを受理する。

literal／formula injection oracle:
- `=`, `+`, `-`, `@`で始まる合成textをConfig、References、Results、Checkpointへ渡す。
- app-owned closed formula以外はInlineStringでCellFormulaを持たない。
- formula allowlist外function、external workbook、DDE、defined name、raw formulaを拒否する。
- error、ToString、logへcanary本文やprivate pathをechoしない。

logging oracle:
- loggerのpublic surfaceにfree-textまたはException parameterがない。
- Prompt、response、reference、student value、reason、evidence、credential、path、token値をapplication logへ出さない。
- 技術code、operation kind、attempt count、elapsed、dimension等の非内容metadataだけを記録できる。
- output/partialは元本と同等以上に機密として扱う旨を確認し、test artifactをrepositoryへcommitしない。

各operationでsent source IDs、forbidden canary absent/present、cell type、formula有無、log resultをtest double/Open XMLから取得します。全必須oracle一致だけをPASS、不一致はFAIL、前提不足はBLOCKEDです。

報告順: Test ID / 環境 / operation / sent source IDs / forbidden canary absent/present（値なし） / logger surface / workbook cell type・formula count / input不変 / 証跡 / Status / 差異。
```

### ST-UC-12: 4-step＋設定、通常非scroll／ページ切替、長文・拡大例外

```text
あなたはStudyReport Evaluator v4.6のuser-visible UI・accessibilityシステムテスト担当者です。
Test ID: ST-UC-12
Requirement: TR-17 / AC-002 / AC-016 / AC-017

捏造禁止。manual UIで観測していない項目を目視PASSと書きません。headless deterministic testで補完した項目はその方法を明記します。実在回答やprivate pathをscreen captureへ含めません。repositoryを変更、stash、reset、clean、commitしません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。

T01時点の未実装・NOT_RUNは履歴です。現在はMainWindow／SettingsViewの実装、MainWindowSettingsTests、ResponsiveLayoutTests／CompactWorkflowLayoutTests等の直接ownerがあり、親担当のT25〜27合同74/74にT26の27ケースが含まれます。これはheadless／計算の既存証跡に限り、このPromptの今回PASSやnative実DPI／Narratorの成功ではありません。実行時に必要caseのdiscoveryを確認し、欠落はBLOCKEDとします。旧縦scroll試験の意図を通常非scrollと例外到達に分け、clippingで合格にしません。

0.8.4のT39追加nativeは3試行後に停止し、最新native-final-attempt.jsonはCONTROL_ID_PREDICATE_NOT_UNIQUEでFAILです。120 DPI・1180×800 DIPと入力読込等の部分観測を、4画面・5カテゴリ・1024×720・実keyboardの成功へ拡張しません。Narratorと本人確認のNOT_RUN_EXTERNAL_PREREQUISITE、T26のheadless測定、F02後の0.8.5再検証は別々に扱います。

準備:
1. `StudyReportEvaluator.slnx`を上位探索してrepository rootへ移動します。
2. Windows 11 x64、x64 process、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. `tests/fixtures/system-test/SystemTest-10Students.xlsx`のsize 7,652、SHA-256 `7F473879B3C43319D842881C5DFD1D01B2A60091A5B0C7B39B70B8D574D801C8`を確認します。
4. repository外にevidence directoryを作り、locked restore後、`EthicsWarningTests`、`MainWindowTests`、`MainWindowSettingsTests`、`SettingsViewTests`、`PrimaryJourneyAccessibilityTests`、`CopilotLoginCommandTests`、`InputViewTests`、`QuantificationDesignViewTests`、`ExecutionViewTests`、`ResultsOutputViewTests`、`ResponsiveLayoutTests`／`CompactWorkflowLayoutTests`をReleaseで実行します。class指定はそのclassだけの全discoveryを指します。exact warningのsource/docs同期は`DocumentationContractTests.Ethics_warning_is_exact_in_readme_and_user_guides`でも確認します。
5. Desktop UIを観測できる場合だけRelease apphostを10人fixture付きで起動し、render scaling 200%、keyboard中心で確認します。観測手段がなければmanual scopeを`NOT_RUN_UI_AUTOMATION_UNAVAILABLE`とし、headless scopeと分離します。

全step共通のexact warning:
`生成AIが行う評価には正確性が欠ける可能性があるため、必ず自分で責任をもって評点を行ってください。このツールや生成AIは評価結果に対しては一切の責任を負えません`

warning oracle:
- Input、Design、Execution、Resultsと設定でshell rootのpersistent bannerとして常時全文表示する。
- non-focusable、non-modalで、checkbox、dismiss、consent、role、score gate、実行blockがない。
- warning操作をsnapshot、run、cancel、resume、formula、finalizationの条件にしない。

control／navigation oracle:
- Input: native picker、direct path、question row 1/2、sheet、row範囲、mappingへTabで到達する。
- Inputの主回答列ComboBoxを操作すると、同じQuestion cardの可視設問text TextBoxが、選択中sheet・question row・columnの交差セル値へ即時更新される。別Questionは変更されない。
- Design: Base/Special/Similarity、Question Points、equalizeを主画面に残し、Knowledge/Custom、special editor、Imported Promptへ同じ対象の設定を1操作で開いて到達する。
- Execution: auth、実効model／`auto`、実効出力先、新規/resume、partial、start/cancelを主画面に残し、model／concurrency／出力先変更は設定へ到達する。
- Results: row-level QuestionEarned、SpecialEarned、SimilarityPenalty、FinalRaw、FinalScore、final/partial path、cleanup warning、output folder操作を表示する。
- 設定は同一windowの独立内容で5カテゴリとし、第5ステップにはしない。カテゴリを変えてもworkflowは進まず、終了時に元の対象とfocusへ戻る。同じ入力欄を二重配置しない。
- completed runの結果到着は維持するが、設定表示中は通知だけで強制遷移しない。起動しただけではResultsへ遷移しない。全画面で現在runの進捗入口と停止を利用できる。

progress oracle:
- stageはPreparing、GeneratingReferences、EvaluatingRows、SavingCheckpoint、FinalizingWorkbook、Completed/Cancelling。
- reference、row、operationのcompleted/total、in-flight、final/partial pathを表示する。
- countは単調増加し、Completedのin-flightは0。
- partial failure/cancelとvalid final completionを同じ表示にしない。

accessibility oracle:
- 最小1024×720 DIPと初期1180×800 DIPを引き上げず、実ClientSizeを記録する。通常画面は初期offsetで警告全文・主操作・状態・前後移動が完全包含され、外側scroll不要、horizontal overflowなし。外側ScrollViewerはExtent <= Viewportを確認し、Disabledやclippingだけを合格にしない。
- 多数設問／結果は前・次・表示範囲・全件数・結果元行への移動で全件へ到達する。空／1件／境界前後／最終ページ、追加・削除・並替え・リサイズでIDとページを補正し、有限高さのvirtualizationを維持する。
- 長文／全文path／dropdownは局所scrollを許容する。760×600 standaloneと200%はreflow・行数削減を先に行い、足りない場合だけ本文縦scrollで全操作へ到達する。固定領域でfocusを覆わず、例外の成功を通常非scrollへ算入しない。
- 本文・入力14 DIPと主操作target最小44 DIP、日本語ラベル、focus／文字／iconのcontrastを維持する。表示件数は残領域と実際の行高で測定し、5件／6行等の未測定値を固定しない。
- UIは100行／530行synthetic、20,000件は別のページ境界計算で検証し、全件実画面の性能成功へ読み替えない。実測欄は`dev/docs/ui-layout-contract.md`に従い、未実測はNOT_RUNとする。
- keyboardだけで主要操作へ移動でき、focus ringが視認できる。
- realized control内のautomation IDが重複しない。
- warningは読み上げ可能だがTab stopではない。
- synthetic content以外をcaptureしない。

apphost起動時に`Copilot 状態を確認`や`定量化を開始`を押す必要があるcaseは10人synthetic fixtureだけで行い、canonical sampleを使いません。Live AI送信はこのscenarioでは行いません。

判定は`Headless deterministic UI`と`Manual user-visible UI at 200%`を別statusで記録します。overall PASSには両scopeの実測PASSが必要です。headlessがPASSでもmanual観測手段がない場合、manualとoverallを`NOT_RUN_UI_AUTOMATION_UNAVAILABLE`とし、PASSやBLOCKEDへ読み替えません。不一致はFAIL、required build/test自体を開始できない環境不足はBLOCKEDです。

報告順: Test ID / 環境・scale / scope（manual/headless） / step / control・automation ID / 期待 / 実測 / warning exact match / capture provenance / scope別Status / 差異。
```

### ST-UC-13: `--input`／`--prompt`、explicit apply、no-auto-run

```text
あなたはStudyReport Evaluator v4.6のcommand-line Prompt launchシステムテスト担当者です。
Test ID: ST-UC-13
Requirement: TR-18 / AC-018

捏造禁止。合成`.txt`と10人synthetic XLSXだけを使い、Prompt本文をerror、log、reportへ出しません。アプリを起動しただけでAIを実行しません。repositoryを変更、stash、reset、clean、commitしません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。

準備:
1. `StudyReportEvaluator.slnx`を上位探索しrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外にevidence directoryと、UTF-8 BOMあり／なし、invalid UTF-8、空、valid上限32,767 UTF-16 code units、invalid上限超過32,768 UTF-16 code units、複数Promptの一時`.txt` fixtureを作ります。本文はreportへ記録しません。
4. locked restore後、`LaunchOptionsTests`（PromptFileLoaderとstartup compositionのcaseを含む）、`ImportedPromptSettingsViewTests`、`SettingsViewTests`、`SettingsCompositionTests`、`CopilotLoginCommandTests`をReleaseで実行します。実在する`ImportedPromptSettingsView`／`ApplyImportedPromptCommand`と設定のCanEditDefinitionが移設先です。直接anchorは`ImportedPromptSettingsViewTests.Explicit_apply_copies_only_to_selected_custom_evaluator`／`Explicit_apply_copies_only_to_selected_special_evaluation_without_changing_points`、`SettingsViewTests.Alternating_category_edits_and_imported_prompt_reach_the_latest_draft_before_explicit_save`、`SettingsCompositionTests.Settings_initialization_does_not_consume_the_existing_one_time_launch_input_request`です。存在しないPromptFileLoader専用classを推測せず、移設後caseの欠落を旧Design試験で代替しません。T01の未実装表記は履歴で、今回結果は新規実測からだけ記録します。

parser oracle:
- option名はordinal-ignore-caseの`--input` 0/1回と`--prompt` 0回以上だけ。
- `--input`／`--INPUT`／`--Input`と`--prompt`／`--PROMPT`／`--Prompt`が同じoptionとして動作する。
- relative pathはstartup current directory基準でabsolute化する。
- Prompt指定順を維持し、canonical duplicate pathは最初の1件だけ残す。
- unknown option、missing value、2回目以降の`--input`（値が同じでも異なっても）、non-`.txt` Promptをsafe startup errorにする。
- custom URI、daemon、background server、filename暗黙mappingを追加しない。

Prompt loader oracle:
- UTF-8 BOMあり／なしを受理する。
- invalid UTF-8、0文字、32,768 UTF-16 code units以上、read failureを拒否する。
- basenameと本文を設定の「読込Prompt」へ指定順に表示し、Designに件数と同じ適用対象の入口を残す。
- safe error windowへoption value、Prompt本文、private pathを表示しない。

explicit apply oracle:
- Promptを一覧で選択しただけではdraftを変更しない。
- selected Custom evaluatorまたはspecial itemをtargetにして「Promptを適用」した時だけtemplateをcopyする。
- 同じPromptを複数targetへ再利用できる。
- filename規則でquestionへ暗黙割当しない。
- applyはstep遷移、認証確認、loginやAI処理を開始しない。未適用の一覧・本文・順序はsetting.txtに保存せず、保存定義の明示適用でも変更しない。
- startup後のCopilot runner/session/send countは0。利用者がExecutionのstartを明示するまで0のまま。

各args caseは別process／startup stateで実行し、前caseのstateを引き継ぎません。10人fixtureのidentityを起動前後で比較し、result/final/partialが作られていないことも確認します。

全caseが今回一致した場合だけPASS。不一致はFAIL、tool不足はBLOCKEDです。

報告順: Test ID / 環境 / args case（値は秘匿） / parser result / Prompt loader result / UI prefill・apply対象 / AI runner・session・send count / input・result state不変 / 証跡 / Status / 差異。
```

### ST-UC-14: Windows x64 publish、bundled CLI、clean launch

```text
あなたはStudyReport Evaluator v4.6のWindows ZIP代替経路システムテスト担当者です。
Test ID: ST-UC-14
Requirement: TR-19 / AC-020

捏造禁止。Windows 11 x64とpwsh.exe -NoLogo -NoProfile、PowerShell 7+ Coreだけを使います。Windows PowerShell 5.1へfallbackしません。credentialを収集・表示しません。今回生成・検証していないpackage、install、launch、test件数をPASSにしません。sourceや既存変更をstash、reset、clean、commitしません。

準備:
1. `StudyReportEvaluator.slnx`を上位探索してrepository rootへ移動します。
2. OS build、OS/process architecture、PowerShell version/edition、`dotnet --version`、`global.json`を記録します。Windows 11 x64でなければBLOCKEDです。
3. UTC開始、HEAD、worktree status件数とUTF-8 SHA-256を記録します。
4. repository外にclean extract/evidence directoryを作ります。

build:
1. `global.json`のSDK feature bandとversion 2 lockを使い、operatorはsolution locked restoreを1回行います。後続のpublish script内ではapp projectのcanonical locked restoreとwin-x64 RID restoreを実行し、一時RID lockをcanonical Core/App lockへ照合してcanonical lock hashを変えないことも必須です。
2. Release buildを`--no-restore`で行い、exit code、warning、errorを今回出力から記録します。
3. solution testをRelease `--no-restore --no-build`で1回行い、今回のtotal/passed/failed/skippedを記録します。
4. restore/build/testのいずれかが失敗した場合、古いpackageを代用せずFAILまたはBLOCKEDとして停止します。

publish/package:
- `scripts/publish-windows.ps1`と`scripts/package-windows.ps1`をPowerShell 7+ Coreで実行する。
- RIDは`win-x64`、self-contained、non-trimmed、non-single-file。
- packageにapp、README/docs/images、`copilot-runtime.json`、`runtimes/win-x64/native/copilot.exe`を含む。
- manifestのschema、RID、CLI version、CLI SHA-256、SDK version、relative pathと実fileが一致する。
- Appはmanifestからpackage-relative absolute CLI pathを解決し、PATH fallbackしない。
- ZIPはsingle rootで、safe relative path、0-byte/reparse/source/test/sample/secretを含まない。
- SHA-256 sidecarが実ZIP hashと一致する。
- publish/package scriptはPowerShell 7+だけを受理し、5.1へfallbackしない。

clean launch:
1. 今回生成したZIPとsidecarを照合してからrepository外のclean directoryへ展開します。
2. installer、administrator権限、.NET Runtime/SDK追加installを要求しないことを確認します。
3. 外部.NETを利用できない環境変数境界で展開済みapphostを起動し、startup probe中に異常終了しないことを確認します。
4. 起動しただけでCopilot process、AI send、final/partial作成が発生しないことを確認し、今回起動したprocessだけを終了します。
5. bundled CLI、manifest、sidecar、必須文書の1つでも欠落すればFAILとし、旧packageをPASSにしません。
6. `PackageLockTests`、`WindowsPublishPackageTests`、`WindowsLocalApplicationTests`、`CopilotClientFactoryTests`を実行し、manifest欠落、CLI hash不一致、manifest path不正ではCopilot機能をsafe failureにしてPATH上の同名CLIへfallbackしないことを確認します。negative caseはrepository外のpackage copyまたは既存test fixtureだけを変更します。

署名済み、installer、SmartScreen reputation、非Windows対応を主張しません。package hash、size、entry count、test countは今回値だけを記録します。

報告順: Test ID / UTC開始終了 / commit・status hash / OS build・architecture・pwsh・SDK / restore・build・test結果 / publish identity / package basename・size・SHA-256 / manifest・CLI identity / clean extract rootはbasenameのみ / launch / process cleanup / Status / 差異。
```

### ST-UC-15: Unsigned ZIP integrity、sidecar、再現性、tamper

```text
あなたはStudyReport Evaluator v4.6のWindows ZIP package integrityシステムテスト担当者です。
Test ID: ST-UC-15
Requirement: TR-20 / AC-020

捏造禁止。Windows 11 x64、pwsh.exe -NoLogo -NoProfile、PowerShell 7+ Coreだけを使います。既存利用者file、sample、source、test、secretをpackageへ混入させません。今回生成した実物だけを証跡とし、過去hashを流用しません。repository変更をstash、reset、clean、commitしません。

準備:
1. `StudyReportEvaluator.slnx`を上位探索してrepository rootへ移動します。
2. OS/architecture/PowerShell/global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. ST-UC-14相当のlocked restore、Release build、win-x64 publishが今回成功したことを確認します。失敗または未実施ならBLOCKEDとし、古いpublish inputを使用しません。
4. repository外にevidence directoryを作ります。
5. `WindowsPublishPackageTests`をReleaseで実行します。このclassの全testが、再package、tamper、zero-byte、reparse point、既存出力不変を担当します。

同じ今回publish inputから2回packageし、1回目のZIP/sidecarをevidence directoryへcopyしてから2回目を作ります。

各runの必須検査:
- ZIPは単一rootを持つ。
- entry名はordinal順で、重複、大小文字衝突、absolute path、parent traversal、symlink/reparse pointがない。
- source、test、sample、symbol、credential候補、0-byte fileを含まない。
- entry timestampはpackage契約の固定値である。
- `RELEASE-NOTES.txt`はunsigned、self-contained、bundled CLIを明記する。
- sidecarは大文字64桁SHA-256、2 spaces、ZIP basename、LF終端のexact形式で、実ZIP hashと一致する。
- 2回目のZIP sizeとSHA-256が1回目と一致する。

negative case:
- valid ZIPのEOFへbyte `0xA5`を1件appendしたrepository外のtampered copyは、original sidecar検証に失敗する。
- package input copyへ0-byte fileを1件追加したcaseは失敗し、既存ZIP/sidecarを置換しない。
- package input copyへreparse pointを1件追加できる環境では同様に失敗し、既存出力を置換しない。作成permissionがなければこのnegative caseだけBLOCKEDとし、PASSにしない。
- negative fixtureはrepository外だけに作り、test後にcleanupする。

1回目と2回目のbasename、size、SHA-256、entry count、sidecar exact match、tamper rejection、invalid input時の既存出力不変を記録します。署名、installer、SmartScreen reputationがあると報告しません。

全required caseが今回一致した場合だけPASS。不一致はFAIL、必要なpublish inputや環境不足はBLOCKEDです。

報告順: Test ID / 環境 / publish input identity / first ZIP identity / second ZIP identity / sidecar形式 / layout counts / reproducibility / tamper result / zero-byte・reparse case / cleanup / Status / 差異。
```

### ST-UC-16: Windows-only claim、documentation、screenshots

```text
あなたはStudyReport Evaluator v4.6の公開scope・documentation整合システムテスト担当者です。
Test ID: ST-UC-16
Requirement: TR-21 / TR-22 / AC-021 / AC-022

捏造禁止。Avalonia/.NETの一般的なcross-platform対応を本製品の動作証跡にしません。存在しないpackage、installer、署名、notarization、AI品質を作成済みと報告しません。文書と実装が異なる場合、テスト中に文書を期待へ書換えてPASSにせず差異をFAILとして記録します。画像へ実在学生データ、private path、credentialを含めません。repositoryを変更、stash、reset、clean、commitしません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。

準備:
1. `StudyReportEvaluator.slnx`を上位探索しrepository rootへ移動します。
2. HEAD、UTC開始、worktree status hash、OS/architecture/PowerShell/global.json SDKを記録します。
3. repository外にevidence directoryを作り、locked restore後、`DocumentationContractTests`、`DocumentationScreenshotTests`、`WindowsPublishPackageTests`をReleaseで実行します。

Delivery transition oracle:
- 要求v4.6はv4.5の業務契約、ADR-0015のZIP境界、ADR-0016のApp限定unsigned single-file EXE、login導線、matrix v2を維持し、UI簡素化・明示設定保存／適用／出力先復元を追加する。
- current READMEと利用者文書は公開`v0.8.1`のZIPと、UNRELEASED `0.8.5`候補の将来EXE主導線／ZIP代替を明確に分離する。0.8.4のT28画像生成履歴とF02の現在ソース版を混同しない。
- T01の未実装・NOT_RUNは当時の履歴。T01〜T38はREVIEWED、実装と記録済み局所試験はVERIFIED_SCOPED。T35の対象文書試験4/4成功・レビュー済みとT36の21/21は別scopeで、それぞれのTRXへ接続する。C-045〜047のBLOCKEDはT39追加native FAIL／人手未実施等のためで、T36待ちを理由にしない。T28の216/216に8画像の生成・2回一致が含まれてもnative・実保存を成功扱いにしない。F01はREVIEWED、親担当は0.8.5へPATCH済み。F02最終再検証は実行記録の最新欄で確認し、T39をBLOCKEDのまま進める指示をG4・全タスクDONE・公開成功へ読み替えない。
- single-file／ZIP package testsはそれぞれの実成果物とsidecarを検証する。
- 開発hostのP06／P07、fake login、CLI helpをfresh OSのCH-01〜06や本人login成功へ読み替えず、新EXEを公開済みと記載しない。
- macOS、Linux、Windows Arm64、installer、code signing、notarization、SmartScreen結果を対応済みまたはpublic assetと記載しない。
- packageにsample、user workbook、署名済みmarkerを現在のpublic成果物として要求しない。
- framework support、cross-publish、test certificateを現在のproduction PASSへ算入しない。

documentation inventory／link oracle:
- READMEとdocs indexからgetting-started、features、custom evaluator、Prompt launch、privacy、troubleshootingへ到達できる。
- `docs/prompt-launch.md`にCopilot起動依頼Promptとアプリ評価Promptの区別、sample構造、複数の教員scenario、explicit apply、no-auto-runがある。
- Input/Design/Execution/Resultsの記述がnative picker、absolute points、special/reference/similarity、checkpoint/resume、auto final、4 final sheetsと一致する。
- exact warning文がsource、test、docsで一致する。
- Markdown local link targetが存在する。
- package契約にuser docs/imagesが含まれる。

screenshot oracle:
- screenshotsはproduction XAML/ViewModelをsynthetic dataでrenderしたものだけを正式画像とする。
- 生成手順、fake authentication/row/AI/checkpoint/output境界、synthetic provenanceを`images/README.md`へ記録する。
- `DocumentationContractTests`で`images/README.md`の存在、synthetic data、fake authentication/row/AI/checkpoint/output境界、各正式PNGの列挙を検証する。
- captureにprivate path、real answer、credential、実在個人情報がない。
- current UIのInput、mapping、Knowledge、Custom Prompt、Execution、Results、output journey、設定の8画像と説明が一致する。0.8.4での生成履歴を0.8.5での再生成・実保存の証拠へ読み替えない。
- mockupや過去UI画像を実画面証跡として扱わない。

read-onlyでdocument inventory、local link、claim文字列、script/package entry contract、image metadata/provenanceを検査します。1箇所でも現在対応と誤読できるclaim、broken link、実装との不一致、provenance不足があればFAILです。

報告順: Test ID / 環境 / docs inventory / link check / UI/requirement comparison / warning exact match / screenshot provenance・privacy / package docs/images / platform claims / test結果 / Status / 差異。
```

### ST-UC-17: 10人fixture new→interrupt→resume E2E

```text
あなたはStudyReport Evaluator v4.6の10人synthetic durable E2Eシステムテスト担当者です。
Test ID: ST-UC-17
Requirement: supplemental deterministic scenario / AC-009〜AC-016 / AC-019

捏造禁止。repositoryの固定synthetic fixtureとlocal deterministic runnerだけを使い、Live AI、Web、外部APIを呼びません。fixtureの回答、Prompt、reference、reason、evidence本文をterminal、report、chatへ出しません。repository fileを変更、stash、reset、clean、commitしません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使い、長時間commandを重複起動しません。

対象fixture:
- `tests/fixtures/system-test/SystemTest-10Students.xlsx`
- bytes 7,652
- SHA-256 `7F473879B3C43319D842881C5DFD1D01B2A60091A5B0C7B39B70B8D574D801C8`
- `SystemTestInput!A1:L11`、header 1 + data 10
- primary F/G/H/I/J、J supporting K
- Base 60、Special 0、SimilarityPenaltyWeight 0.1、RoundingDigits 1、Question Points各8

準備:
1. `StudyReportEvaluator.slnx`を上位探索しrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. fixtureのSHA-256、size、last-write UTCを取得します。不一致なら変更せずFAILです。
4. repository外にevidence/results directoryを作ります。
5. solution locked restoreを1回行います。

実行:
1. 次の2 testをReleaseで名前指定して実行し、TRXをrepository外へ保存します。
	- `StudyReportEvaluator.App.Tests.E2E.TenPersonSystemSmokeTests.Fixture_has_fixed_identity_structure_and_literal_formula_canaries`
	- `StudyReportEvaluator.App.Tests.E2E.TenPersonSystemSmokeTests.Ten_person_uninterrupted_and_resumed_runs_have_identical_results_and_formula_cache`
2. 1件目でfixture identity、StandardXlsx、1 sheet、A1:L11、10 rows、formula/macro/external-link/DDE/defined-name 0、F〜Jの各blank 1、literal canaryを検証します。
3. 2件目でuninterrupted baselineを実行します。
4. 別output pairで、5行目のcheckpointがsuccessful updateとして保存された直後にcontrolled cancelします。
5. 新しいorchestratorとCheckpointStoreで実在partialをread-only loadし、resumeします。
6. baselineとresumed finalのaccepted status/raw、reference、similarity、Results formula text、cell type、cached valueを比較します。

固定oracle:
- planned normal evaluations: 10×5=50。
- blank primary: F〜Jに各1件、合計5。normal SUCCESS=45、EMPTY=5、failure/cancelled=0。
- reference calls=5、normal calls=45、similarity calls=45、special calls=0。
- SpecialPoints=0のためspecialは`NOT_RUN_ZERO_BUDGET`でdispatchしない。
- uninterrupted checkpoint: create 1、update 15（5 references + 10 rows）、load 0。
- interrupted phase: references 5、completed rows 5、normal/similarity calls各22、checkpoint create 1、update 10、finalなし、partialあり。
- resume phase: saved reference calls 0、remaining normal/similarity calls各23、checkpoint create 0、update 5、load 1。
- resume後のcompleted rows=10、valid finalあり、partialなし。
- finalはoriginal 1 + app-owned 4 sheets、Checkpointなし、Results data rows 10、OpenXmlValidator error 0。
- fixture identityは全phase後も不変で、test temp directoryはcleanupされる。

2 testの両方が今回PASSし、全固定oracleが一致した場合だけPASS。不一致はFAIL、fixture/environment不足はBLOCKEDです。

部分ケース ST-UC-17-R（再起動したのと同じ状態からの再開。上の固定oracleとは別に判定し、上の判定を変えません）:
1. 次の3 testをReleaseで名前指定して実行し、TRXをrepository外へ保存します。
	- `StudyReportEvaluator.App.Tests.E2E.SettingsWorkflowSystemTests.Cancel_saves_a_real_partial_and_new_instances_resume_without_repeating_completed_AI_or_reference`
	- `StudyReportEvaluator.App.Tests.UI.ResumeWorkflowTests.Real_checkpoint_preflight_requires_explicit_start_and_invalidates_after_model_change`
	- `StudyReportEvaluator.App.Tests.UI.ResumeWorkflowTests.Picker_cancel_preserves_selection_and_never_starts_AI`
2. 期待値: 新しいapp instance（同じ入力workbook・setting.txt・実在partialを引き継ぐ）で、partialのpathを指定→開始前検証（全項目OK、この時点ではrun requestなし）→明示的なStartを経て再開し、完了済みのAI呼出しとreferenceを繰り返さない。開始前検証の後もStartを押すまでrunは始まらず、model変更で検証結果が無効になる。picker取消では選択中の再開元を変えず、AIを呼ばない。
3. 3 testがすべて今回PASSした場合だけPASS。1件でも失敗ならFAILです。これはheadless・fakeの確認で、別processでの再起動、native picker、OS shutdownの確認ではありません。

報告順: Test ID / UTC開始終了 / commit・環境 / fixture identity / targeted test結果 / baseline count / interruption point・count / resume count / result・formula equality / final構造 / input・temp不変 / 証跡 / Status / 差異 / 非保証事項。ST-UC-17-Rは別のStatusとして報告します。

最後に、deterministic midpoint/reference/similarityは配線、checkpoint、formula検証用であり、学生の採点結果やAI品質の証明ではないと明記します。
```

### ST-UC-18: Fixed-seed 531-row synthetic new/resume E2E

```text
あなたはStudyReport Evaluator v4.6の531-row fixed-seed synthetic E2Eシステムテスト担当者です。
Test ID: ST-UC-18
Requirement: TR-23 / AC-009〜AC-016 / AC-019

捏造禁止。`SyntheticWorkbookFactory`がseed 20260901から一時生成する匿名synthetic workbookとfake runnerだけを使います。canonical sample、Live AI、Web、外部APIを使いません。cell本文、Prompt、reference、reason、evidence、private pathを証跡へ出しません。repositoryを変更、stash、reset、clean、commitしません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使い、同じ長時間testを重複起動しません。

準備:
1. `StudyReportEvaluator.slnx`を上位探索してrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外にevidence/results directoryを作り、solution locked restoreを1回行います。
4. 次の2 testをReleaseで完全修飾名指定して実行します。
	- `StudyReportEvaluator.App.Tests.E2E.SyntheticQuantificationJourneyTests.Fixed_seed_531_row_workbook_completes_the_local_atomic_quantification_journey`
	- `StudyReportEvaluator.App.Tests.E2E.SyntheticQuantificationJourneyTests.Fixed_seed_531_row_durable_new_and_resume_matches_uninterrupted_run`

fixture／definition:
- `Original!A1:L531`、header 1 + data 530、seed 20260901。
- 2件以上のenabled questions、normal evaluator、1件以上のenabled special item、reference、similarityを含む。
- `Base + Special + Σ enabled Question Points = 100`のexact definitionを使う。
- formula marker、blank primary、same-row evidence canaryを含むが全てsynthetic。

実行:
1. uninterrupted durable runでreference-first、row ascending、checkpoint after each reference/complete rowを確認します。
2. midwayのcomplete row checkpoint成功直後にprocess終了相当のcontrolled cancellationを発生させ、finalを作らずpartialを保持します。
3. 新しいorchestrator/CheckpointStoreでpartialをread-only loadし、saved referencesとcomplete rowsをskipしてresumeします。
4. uninterruptedとresumed runのaccepted normal/special/similarity raw、status、formula text/cache、FinalRaw/FinalScoreを比較します。
5. fixed-seed生成inputのSHA-256、size、mtimeがnew、cancel、resume、final後に一致することを確認します。

必須oracle:
- referencesはenabled questionにつき1回、resume時の保存済みreference再生成は0。
- rowsは2〜531のascending orderで、complete 530 rowsとなる。
- normal/special/similarity call countはblank primaryによるno-sendを考慮してdefinitionとinputから決定的に導出し、過去値を固定流用しない。
- uninterrupted baselineはcheckpoint create 1、successful update exactly 532（2 references + 530 complete rows）、load 0。
- interrupted phaseはcheckpoint create 1、successful update exactly 267（2 references + 265 complete rows）、load 0。
- resume phaseはcheckpoint create 0、successful update exactly 265（remaining complete rows）、load 1。
- interruptionより前のcomplete rowだけdurableで、in-progress rowはresumeで全体再実行する。
- finalはoriginal sheets + 4 app-owned sheets、Checkpointなし、Results data rows 530、formula/Open XML error 0。
- success後partialを削除する。cleanup fault caseではvalid final + warningを維持する。
- temp、partial、finalのbasenameと存在状態だけを記録し、本文を記録しない。

既存testがuninterruptedだけを検証し、531-row resumeを直接実測していない場合、その不足を別testのPASSで代用せずFAILとして記録します。性能値は本scenarioの保証にせず、測定する場合はAI待機を除く別evidenceとします。

全必須oracleを今回の531-row new/resume runで確認した場合だけPASS。不一致はFAIL、環境不足はBLOCKEDです。

報告順: Test ID / seed・rows・definition shape / commit・環境 / uninterrupted count / interruption checkpoint / resume skip・call count / final comparison / workbook構造・validation / input identity / cleanup / 証跡 / Status / 差異 / 非保証事項。
```

### ST-UC-19: Canonical SampleReport no-network technical durable E2E

```text
あなたはStudyReport Evaluator v4.6のcanonical SampleReport technical durable E2Eシステムテスト担当者です。
Test ID: ST-UC-19
Requirement: real-data technical E2E / AC-009〜AC-016 / AC-019

目的:
`sample/SampleReport.xlsx`を唯一のcanonical sampleとしてread-onlyで使用し、input load、initial mapping、definition snapshot、reference generation、row evaluation、checkpoint、formula workbook、validation、atomic final、cleanupのproduction technical pathをlocal deterministic runnerで検証します。実データをGitHub Copilot、その他の生成AI、Web、外部APIへ送信しません。これはfull user-facing Live AI E2Eでも教育的採点品質testでもありません。

絶対規則:
1. 捏造禁止。未実施、未観測、過去結果、文書値を今回のPASSへ流用しません。
2. workbook cell/header本文、worksheet名、学生情報、Prompt、reference、reason、evidence、credential、token、private absolute pathをterminal、JSON、Markdown、chatへ出しません。TRXはlocal private artifactとして扱います。
3. inputを編集、再保存、rename、移動、削除、匿名化しません。
4. Copilot認証確認、production model call、Execution UIの`定量化を開始`を実行しません。
5. repository source、test、文書、設定を修正、stash、reset、clean、commitしません。
6. PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。
7. 同じ長時間testを無出力だけを理由に重複起動しません。
8. evidenceはrepository外の一意なdirectoryへ保存します。

正本:
- `docs/requirements-definition.md` v4.6
- `tests/SystemTest-prompt.md` v4.6
- `docs/getting-started.md`
- `docs/privacy-and-data-handling.md`
- `tests/StudyReportEvaluator.App.Tests/E2E/RealDataSystemSmokeTests.cs`
- `global.json`
`work/`、`test/`、`artifacts/`の過去report、JSON、TRX、hash、件数、時間、判定を今回値へ流用しません。

input baseline:
- logical path `sample/SampleReport.xlsx`
- historical size 470,806 bytes
- historical SHA-256 `73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA`
- 2026-09-16利用者明示承認により、旧size、SHA-256、先頭128 bytesの一致は診断専用（非ゲート）。旧測定値を書き換えず、旧sample／attachmentと同一と未確認のまま主張しない。
- Open XML entriesはread-onlyで取得した実ZIPのEntries.Countとclassification.PackagePartCountが一致し、metadataのpackage／relationship件数もclassificationと一致する。relationship件数は正かつclassifierの安全上限内、external relationships 0。
- 旧11 entries／8 relationshipsは履歴であり固定gateではない。2026-09-16の観測は469,976 bytes、13 entries／9 relationships、external 0、StandardXlsx受理。新たな固定gateにせず、旧fileの元bytesがないため正確な差分・変更原因は断定しない。
- worksheet 1、dimension A1:J531、rows 531、columns 10、macro 0、external-link 0を期待する。2026-09-16のproduction reader＋suggester測定（TestResults/app-fixes-20260916/sample-profile/dahatake_DAHATAKE-OFFICE_2026-09-16_13_59_37_net10.0.trx標準出力）を現行profileとして採用し、旧12列sample期待値だけを上書きする。出典testのFAILと今回E2Eの成否は分離する。
canonical pathまたは必須構造が異なる場合、inputを変更せず`BLOCKED_INPUT_IDENTITY_MISMATCH`とします。履歴identity比較が不一致という理由だけではblockしません。実行前後のSHA-256、size、last-write UTC不変は必須です。

initial definition baseline（header/cell本文は非表示）:
- header row 1、data rows 2〜531、530 rows。
- Base 60、Special 0、SimilarityPenaltyWeight 0.1、RoundingDigits 1。
- question 4、primary D/E/G/H、2問目Eのsupporting F、4問目Hのsupporting I（1・3問目は空）。
- target D/E/F/G/H/I、対象外 A/B/C/J。D/GはPrimaryAnswer、E/HはPrimaryAnswer + StudentPromptPrimary、F/IはSupporting。
- evaluator types KnowledgeCoverage、CustomPrompt、KnowledgeCoverage、CustomPrompt。
- points各10、allocation total 100。
- special operationはSpecial=0のためdispatch 0。
数値はworkbook cellではなく要求とアプリ初期definitionの既定値です。

環境・provenance:
1. `StudyReportEvaluator.slnx`を上位探索しrepository rootへ移動します。
2. Windows 11 x64、x64 process、PowerShell Core 7+、global.json選択SDKを確認します。違えば`BLOCKED_UNSUPPORTED_ENVIRONMENT`です。
3. External run ID、UTC開始、HEAD、worktree status件数とUTF-8 SHA-256、OS、architecture、.NET、PowerShellを記録します。
4. inputのSHA-256、size、last-write UTC、package構造をread-onlyで取得しbaselineへ照合します。worksheet名はhashだけをreportへ記録できます。

locked build:
1. `dotnet restore .\StudyReportEvaluator.slnx --locked-mode`を1回実行します。
2. `dotnet build .\StudyReportEvaluator.slnx -c Release --no-restore`を1回実行します。
3. exit code、warning、errorを今回出力から記録します。失敗時はE2Eを開始せず`BLOCKED_BUILD_FAILED`です。
4. build後、次のSHA-256、size、last-write UTCを取得します。
	- `src/StudyReportEvaluator.App/bin/Release/net10.0/StudyReportEvaluator.App.exe`
	- `tests/StudyReportEvaluator.App.Tests/bin/Release/net10.0/StudyReportEvaluator.App.Tests.dll`

targeted run:
同じPowerShell 7+ processで次を設定します。値をreportへ出力しません。
- `STUDY_REPORT_EVALUATOR_REALDATA_SYSTEM_SMOKE=1`
- `STUDY_REPORT_EVALUATOR_REALDATA_INPUT=<absolute sample/SampleReport.xlsx>`
- `STUDY_REPORT_EVALUATOR_REALDATA_EVIDENCE=<external evidence directory>/realdata-e2e-evidence.json`
- `STUDY_REPORT_EVALUATOR_SOURCE_COMMIT=<actual HEAD>`
- `STUDY_REPORT_EVALUATOR_SOURCE_STATUS_SHA256=<actual status SHA-256>`

次を1回だけ実行します。
`StudyReportEvaluator.App.Tests.E2E.RealDataSystemSmokeTests.Optional_real_workbook_default_values_complete_local_v4_system_test`
Release、`--no-restore --no-build`、repository外results directory、local TRX loggerを使います。

freshness必須条件:
- test開始後に指定JSONが新規作成され、0 byteでない。
- `measured_at_utc`が今回run開始／終了内。
- source commitとstatus hashが今回指定値。
- JSON SHA-256を取得する。
- test exit code 0、対象failed 0。
- test生成final/recalculation copyはcleanup済み。
- input identityが開始前と完全一致。
opt-in忘れによるtest runner上の見かけPASSを、JSON freshnessなしでPASSにしません。

technical oracle:
1. format=`StandardXlsx`、InputViewModel read-only load成功。
2. dimension、row範囲、4 questions、mapping、evaluator、points、allocationがbaseline一致。
3. startup parserが`--input`を受理し、in-process prefillをloadする。
4. Release apphostがstartup probe中に生存し、controlled cleanupされる。
5. probe前後でinput identityとinput隣接`result` stateが不変で、新規Copilot process signal 0。
6. production row source、snapshot boundary、CheckpointStore、OutputPathPlanner、WorkbookDurableRunFinalizer、partial cleanerを通る。
7. AI boundaryは`local-deterministic-midpoint-no-network-not-for-grading`でnetwork runnerを呼ばない。
8. references 4、completed rows 530、planned normal evaluations 2,120。
9. normal SUCCESS/EMPTYは今回JSONから記録し、合計2,120、failure 0、cancelled 0。
10. reference calls 4。normal/similarity callsは今回SUCCESS件数と一致、special calls 0。
11. checkpoint create 1、update 534（4 references + 530 rows）、load 0。
12. progressにPreparing、GeneratingReferences、SavingCheckpoint、EvaluatingRows、FinalizingWorkbook、Completedを含み、count単調増加、最後in-flight 0。
13. finalization SUCCESS、success後partialなし、working/temp残存なし。
14. finalは元1 + app-owned 4 = 5 sheets、Checkpointなし。
15. Config formula 2、References/Run formula 0、Results formula >0。
16. Results data rows 530、formula error 0、Open XML error 0。
17. deterministic runner範囲でFinalScore numeric 530、blank 0。
18. calculation mode automatic、full calculation on load true。
19. external recalculation statusはadvisoryとしてrequired pathから分離する。

evidence整合性:
- JSONのsource requirements=`docs/requirements-definition.md v4.6`、system_test_prompt=`SystemTest-prompt.md v4.6`。
- `historical_sample_identity_gating=false`で、`sample_size_match`／`sample_sha256_match`／`sample_prefix_128_match`は実比較の診断値。これらのfalseをtechnical FAIL／BLOCKEDへ変換せず、trueへ書き換えない。今回inputのsize／SHA-256／last-write UTCと実行前後不変を別に検証する。
- driver固定run IDは履歴の識別値のまま維持し、その版・日付を現在の要求版・実行日時と誤認せず、`NON_UNIQUE_DRIVER_RUN_ID`として今回のunique external run IDと区別する。
- runtime CLI identityがtest value／zero hashであるため、bundled production CLI検証済みと報告しない。
- JSON privacy flagだけを信用せず、input absolute path、user profile path、16文字以上のworkbook文字列が完全一致で混入していないことを値非表示で再確認する。
- TRXを共有privacy artifactとして扱わない。

manual UI scope:
Desktop観測可能な場合だけRelease apphostを`--input`付きで起動し、exact warning、4 steps、Inputのsheet/dimension/row/4 mappings、Designの60/0/0.1/各10/total100、Execution controls、起動だけではResults遷移・AI実行・final/partial作成なしを本文非表示で確認します。`Copilot 状態を確認`と`定量化を開始`は押しません。観測不能なら`NOT_RUN_UI_AUTOMATION_UNAVAILABLE`でありPASSにしません。Full production AI E2Eは`NOT_RUN_POLICY_REAL_DATA`固定です。

regression:
targeted run後、上記5環境変数を同じprocessから必ず解除します。その後`dotnet test .\StudyReportEvaluator.slnx -c Release --no-restore --no-build`を1回実行し、今回のtotal/passed/failed/skipped/durationを記録します。環境変数を残したままreal-data testを再実行しません。

最終不変条件:
- input SHA-256、size、last-write UTCが開始前とexact一致。
- EXE/DLL SHA-256がtargeted test開始前と一致。
- 開始時からのsource status hashが一致し、今回のrepository内成果物がない。
- input隣接`result`に今回由来final/partial/tempなし。
- 今回起動したapphost/testhost/Excel processなし。既存の無関係processを終了しない。
- opt-in環境変数なし。

scope別status:
- Input identity: PASS/FAIL/BLOCKED
- Locked restore・Release build: PASS/FAIL/BLOCKED
- Required real-data technical durable E2E: PASS/FAIL/BLOCKED
- Evidence provenance/privacy: PASS/FAIL
- Manual UI: PASS/FAIL/NOT_RUN_UI_AUTOMATION_UNAVAILABLE
- Full user-facing production AI E2E: NOT_RUN_POLICY_REAL_DATA
- Live AI educational quality: NOT_EVALUATED
- External recalculation: PASS/FAILED_ADVISORY/SKIPPED_NOT_INSTALLED/NOT_RUN_EXTERNAL_PREREQUISITE
- Full regression: PASS/FAIL/BLOCKED

Required technical E2Eは全technical oracleを今回確認した場合だけPASSです。manual、production AI、external recalculationを曖昧な「full E2E PASS」「完全に問題なし」「release ready」へまとめません。

報告順: External run ID・UTC / commit・status hash・環境 / EXE・DLL identity / input identity・attachment binding / restore・build / targeted freshness・test結果・evidence hash / mapping・allocation / durable counts・stage / final workbook・cleanup / evidence metadata・privacy / manual UI・production AI・external recalculation / regression / final invariants / scope別status / finding一覧 / 確認済み・未確認・非保証。

最後に、deterministic midpoint、固定technical reference、固定similarityは配線・永続化・数式検証用で学生の採点結果ではなく、実データのLive AI正確性、公平性、教育的妥当性、法的／組織policy適合性を評価していないこと、similarityは不正行為を証明しないことを明記します。
```

### ST-UC-20: Optional authenticated synthetic Copilot smoke

```text
あなたはStudyReport Evaluator v4.6のoptional authenticated synthetic Copilot smoke担当者です。
Test ID: ST-UC-20
Requirement: TR-24A advisory

このtestは任意であり、required deterministic acceptanceの代替ではありません。捏造禁止。canonical sampleや実在学生データを送信しません。credential、token、Prompt、response、reason、evidence本文をreportへ出しません。credentialを収集・入力・保存しません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。

準備:
1. `StudyReportEvaluator.slnx`を上位探索しrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外にevidence directoryを作り、locked restoreを1回行います。
4. `AuthenticatedSyntheticSmokeTests.Advisory_status_set_is_exact`と`Default_policy_does_not_probe_login_or_start_network_even_if_login_would_be_available`を必ず実行し、defaultがauth probe 0、network 0、status NOT_RUNであることを確認します。

opt-in判定:
- 実行依頼にLive network testの明示許可がない場合、`STUDY_REPORT_EVALUATOR_COPILOT_LIVE_SMOKE`を設定せずoptional testを実行し、Status=`NOT_RUN`として終了します。
- 明示許可がある場合だけ同じPowerShell processで`STUDY_REPORT_EVALUATOR_COPILOT_LIVE_SMOKE=1`を設定し、既存loginを使用します。secret入力を要求された場合は入力せず`SKIPPED_NOT_AUTHENTICATED`です。

opt-in時に次の1件だけを実行します:
`StudyReportEvaluator.App.Tests.Copilot.AuthenticatedSyntheticSmokeTests.Optional_authenticated_synthetic_smoke_reports_advisory_status_honestly`

送信data classはtest codeに固定された1件のsynthetic payloadだけです。許容status:
- PASS
- SKIPPED_NOT_AUTHENTICATED
- FAILED_ADVISORY
opt-inなしはNOT_RUNです。

PASSはsession/tool/schema/cleanupの1件のtechnical smoke成功だけを意味し、教育的品質、全model互換性、real-data品質、production release readinessを証明しません。実行後にopt-in環境変数を解除します。test生成advisory JSONは本文非包含を確認し、repositoryへcommitしません。

報告順: Test ID / advisory kind / explicit opt-in有無 / environment / sent data class / auth probe・network count / observed safe status・rationale code / artifact basename・SHA-256 / env cleanup / Status / 非保証事項。
```

### ST-UC-21: Optional external spreadsheet recalculation

```text
あなたはStudyReport Evaluator v4.6のoptional external spreadsheet recalculation smoke担当者です。
Test ID: ST-UC-21
Requirement: TR-24B advisory

このtestは任意であり、required runtimeやdeterministic acceptanceの代替ではありません。捏造禁止。専用synthetic formula workbookだけを使用し、canonical sampleや10人fixtureを外部spreadsheetで保存しません。既存利用者fileを変更しません。cell本文、private path、credentialをreportへ出しません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。

準備:
1. `StudyReportEvaluator.slnx`を上位探索してrepository rootへ移動します。
2. Windows 11 x64、PowerShell Core 7+、global.json SDK、HEAD、UTC開始、worktree status hashを記録します。
3. repository外にevidence directoryを作り、locked restoreを1回行います。
4. 実行依頼にexternal spreadsheet使用の明示許可がない場合、`STUDY_REPORT_EVALUATOR_EXTERNAL_RECALC_SMOKE`を設定せずStatus=`NOT_RUN_EXTERNAL_PREREQUISITE`として終了します。
5. 明示許可がある場合だけ同じPowerShell processで`STUDY_REPORT_EVALUATOR_EXTERNAL_RECALC_SMOKE=1`を設定します。

次をReleaseで1回実行します:
`StudyReportEvaluator.App.Tests.E2E.ExternalSpreadsheetRecalculationSmokeTests.Optional_external_spreadsheet_recalculation_records_advisory_status_honestly`

oracle:
- Excel等の登録済みsupported spreadsheetがなければ`SKIPPED_NOT_INSTALLED`。
- 利用可能ならtestが作るsynthetic output copyだけをfull recalculationして保存する。
- app-owned formula 11件の再計算cacheを独立expected valuesと比較する。
- formula error 0、Open XML validation error 0。
- input synthetic workbookはtest temp内だけで、test後にcleanupする。
- external processは今回起動したinstanceだけを終了する。
- 許容statusはPASS、SKIPPED_NOT_INSTALLED、FAILED_ADVISORY。失敗をrequired pathのFAILへ変換しない。

testが`artifacts/test/external-recalculation-smoke.json`を生成した場合、開始前の存在・hashを記録し、今回生成物をrepositoryへcommitしません。共有用にはstatus、safe rationale code、artifact SHA-256だけをrepository外evidenceへ転記し、本文を転載しません。実行後にopt-in環境変数を解除します。

PASSは登録済みspreadsheet 1環境でsynthetic formulaが再計算されたことだけを意味し、external spreadsheetをrequired runtimeにせず、Live AI、実データ、全version互換性を保証しません。

報告順: Test ID / advisory kind / explicit opt-in有無 / environment / spreadsheet存在・version / synthetic data class / formula count・error count / Open XML count / process・temp cleanup / artifact basename・SHA-256 / observed status・rationale / env cleanup / 非保証事項。
```

### ST-UC-22: 廃止した開発用packageの削除確認（RETIRED）

```text
あなたはStudyReport Evaluator v4.6の廃止要求削除確認の担当者です。
Test ID: ST-UC-22
Requirement: TR-25 / AC-023 / AC-027

捏造禁止。開発用 MSIX と macOS の基盤は 2026-10-06 に廃止した。repositoryの既存差分を変更、stash、reset、clean、commitしません。

1. repository root、HEAD、status hash、UTCを記録します。
2. 廃止したpackaging script、manifest、tool project、静的契約test、CI／release workflow step、release matrixの行がrepositoryに残っていないことをsource検索で確認します（`dev/docs/archive/**`と要求管理文書は履歴として除外）。
3. public ZIP／EXEがsample/user workbookを含めないAC-027の確認はST-UC-25で行います。

TR-25／AC-023は`RETIRED`です。残存が見つかった場合だけ`FAIL`とし、削除済みであれば`RETIRED_CONFIRMED`と記録します。

報告順: Test ID / UTC / source / 検索範囲 / 残存件数 / Status。
```

### ST-UC-23: 廃止したWindows以外の配布基盤の削除確認（RETIRED）

```text
あなたはStudyReport Evaluator v4.6の廃止要求削除確認の担当者です。
Test ID: ST-UC-23
Requirement: TR-26 / AC-024

捏造禁止。Windows 11 x64以外の配布物を生成・起動したと報告しません。

1. Windows以外のpublish／bundle／署名script、manifest、静的契約test、CI jobがrepositoryに残っていないことをsource検索で確認します（`dev/docs/archive/**`と要求管理文書は除外）。
2. 利用者文書がmacOS、Linux、Windows Arm64を対応しないと記載し続けていることを確認します。

TR-26／AC-024は`RETIRED`です。残存または対応済み表示が見つかった場合だけ`FAIL`とし、そうでなければ`RETIRED_CONFIRMED`と記録します。

報告順: Test ID / source / 検索範囲 / 残存件数 / 非対応記載 / Status。
```

### ST-UC-24: 廃止した将来署名順序契約の削除確認（RETIRED）

```text
あなたはStudyReport Evaluator v4.6の廃止要求削除確認の担当者です。
Test ID: ST-UC-24
Requirement: TR-27 / AC-025

捏造禁止。credentialやnative hostを使用せず、sourceだけを確認します。

1. 廃止した署名・公証順序のscriptとその静的契約testがrepositoryに残っていないことを確認します。
2. 未実測platform、installer、signingを対応済みと記載していないことを確認します。

TR-27／AC-025は`RETIRED`です。残存が見つかった場合だけ`FAIL`とし、そうでなければ`RETIRED_CONFIRMED`と記録します。

報告順: Test ID / source / 検索範囲 / 残存件数 / Status。
```

### ST-UC-25: Windows ZIP E2Eとrelease matrix

```text
あなたはStudyReport Evaluator v4.6のdeterministic platform release gate担当者です。
Test ID: ST-UC-25
Requirement: TR-28 / TR-29 / AC-026 / AC-027 / AC-028

捏造禁止。実在する今回artifactとevidenceだけを使用し、未実測artifactをpublic artifactへ昇格しません。private sample、student content、Prompt、response、reason、credential、private pathを共有evidenceへ含めません。

1. Windows単一EXEとZIPを同じsource／versionから今回build/packageし、各sidecar、safe layout、bundled CLI、sample/user workbook不在を確認します。ZIPのclean extract/launch回帰を維持します。
2. matrix v2をclosed schemaで検証します。single-file EXEとZIPの2行だけで、どちらも`publish=true`かつ`PASS_REQUIRED`であり、未知・重複・欠落行が拒否されることを要求します。
3. draft／public assetのexact setがEXE／ZIPと各sidecarの4件で、control JSONや未実測artifactが含まれないことを確認します。
4. candidate workflowがPR/forkでwrite権限やsecretを使用せず、verified annotated tagのdraftへ4 assetだけを添付し、clean-host待ちのcandidate recordを内部control artifactへ保存することを確認します。
5. publish workflowが成功candidate run／tag commit、metadata限定clean-host JSON、4 assetの再download、C03 final matrix、C02再検証を要求し、protected approval後の最後のwrite操作だけで公開することを確認します。
6. live candidateが存在する場合だけ、そのrun ID／commitとfresh downloadした4 assetを再検証します。存在しないURL、hash、run ID、CH結果を作りません。
7. documentationが未公開EXE主候補と公開`v0.8.1` ZIP代替を分離し、廃止した配布形式を一般利用者へ案内せず、CH-01〜06未実施を開示することを確認します。

全deterministic oracleが一致した場合だけlocal contract scopeを`PASS_REQUIRED`とします。GitHub draft/public candidateがまだ存在しない段階ではfresh-download scopeを`NOT_RUN_NO_CANDIDATE`、CH-01〜06を`NOT_RUN`とし、公開完了とは報告しません。

報告順: Test ID / UTC / source / ZIP identity・launch / matrix rows / excluded assets / workflow boundary / docs / fresh-download status / evidence / Status / 非保証事項。
```

### ST-UC-26: clean-host単一EXE・同梱CLI状態確認・本人login・公開境界

```text
あなたはStudyReport Evaluator v4.6のclean-host公開判定システムテスト担当者です。
Test ID: ST-UC-26
Requirement: TR-30 / TR-31 / TR-32 / TR-33 / AC-029 / AC-030 / AC-031 / AC-032 / AC-033 / AC-034

このscenarioは、P06/P07（開発host観測）と独立したV02公開判定用です。捏造禁止。P06/P07のPASS_DEVELOPMENTや過去reportをclean-host PASSへ転記しません。未実施はNOT_RUNのまま保持します。

絶対規則:
1. 候補EXE identity（candidate run/commit/productVersion/bytes/SHA-256）に**完全一致**する実物だけを対象にする。別EXE・再build・類似版を代用しない。
2. fresh Windows 11 x64標準userで実施する。事前に.NET SDK/Runtime、PowerShell 6+、Node/npm、Git/gh、外部Copilot CLI、Office、IDEを追加導入しない。
3. 取得済みEXE 1個からofflineでGUI起動、Input/Design利用を確認する。手動展開、昇格、setup script、terminal入力を要求しない。
4. 標準ブラウザー取得のMOTW、SmartScreen/SAC/企業policy状態、警告/拒否、**実際の操作数**をそのまま記録する。無警告へ丸めない。
5. 同梱CLIのStart/Ping/auth状態確認を分離し、正常な未認証応答とruntime failureを区別する。CLI help成功をauth済みやAI-readyへ読み替えない。
6. EXEの移動、再起動、同時起動、任意cwd、相対`--input`、複数`--prompt`、日本語/空白path、read-only配置先、cache欠落復元で契約が維持されることを確認する。
7. input/final/partial/workbook data不変を検証する。cacheと利用者dataの領域を混同しない。
8. 本人loginは「GitHubにログイン」の明示操作だけで開始する。cancelとアプリ終了時は、当該login processだけを終了し、ブラウザー/他CLI/credentialへ干渉しない。
9. secret、token、device code、学生data、Prompt本文、生ログ、環境変数一覧を証跡へ含めない。metadata限定JSONのみを作成する。
10. optional authenticated live AI（ADV-01）はsynthetic入力だけで別scenarioとして扱う。CH-01〜CH-06の必須判定へ代用しない。

準備:
1. `StudyReportEvaluator.slnx`を上位探索してrepository rootへ移動し、HEADとworktree status hashを記録する。
2. candidate control artifact（release-candidate-record、EXE/ZIP evidence）と受領したclean-host JSONの整合を検証する。
3. EXEのbasename/bytes/SHA-256、sidecar、source commit、candidate run IDが一致しない場合は`FAIL_CANDIDATE_IDENTITY_MISMATCH`で停止する。
4. 試験中にsource、package、evidenceを改変しない。再packageや再publishが必要ならこのscenarioを中断して別runとして扱う。

実行（必須CH）:
- CH-01: fresh OS/architecture/標準user/追加依存未導入を確認。
- CH-02: EXE1個からoffline GUI起動、Input/Design利用、no-auto-run、追加導入不要。
- CH-03: bundled CLIのStart/Ping/auth状態確認（未認証とfailureの区別）。
- CH-04: 移動/再起動/同時起動/args/cwd/path/cacherecovery/data不変。
- CH-05: MOTW・保護状態・警告/拒否・実操作数を記録し承認範囲と比較。
- CH-06: 本人login、完了後/再起動後の既存buttonで再確認、cancel/close時の所有process限定終了。

任意ADV:
- ADV-01（optional authenticated live AI）は、本人が明示承認したsynthetic入力のみ。`NOT_RUN`可。
- ADV-02（optional external recalculation）は従来どおり任意。`NOT_RUN`可。

判定:
- CH-01〜CH-06が全て今回PASSし、candidate拘束（run/commit/version/hash/sidecar）とpublic 4 assets再照合が一致した場合のみ`PASS_REQUIRED`。
- CHの欠落、`FAIL`、`NOT_RUN`、別candidate証跡、機微field混入、想定外fileの公開混入、public asset差替えはFAIL。
- ADV-01/ADV-02の`NOT_RUN`は許容し、必須CHの失敗へ変換しない。

証跡契約（metadata only）:
- 含める: run ID、timestamp、candidate/source/productVersion、artifact basename/bytes/SHA-256、host依存有無フラグ、CH/ADV status、operation count、記録参照hash。
- 含めない: username、secret、token、device code、学生本文、Prompt本文、response本文、生ログ、private absolute path。

報告順: Test ID / candidate identity binding / host前提(CH-01) / offline GUI(CH-02) / bundled CLI state(CH-03) / lifecycle invariants(CH-04) / MOTW-protection-operations(CH-05) / personal login-cancel-close(CH-06) / optional ADV status / metadata-only evidence / public 4 assets再照合 / Status / 差異 / 非保証事項。

最後に、P06/P07はdevelopment-host試験でありV02 clean-host判定を代替しないこと、CH未実施はNOT_RUNのままであることを明記します。
```

### ST-UC-27: 共通＋定義1件の明示保存、atomic失敗、出力先復元

```text
あなたはStudyReport Evaluator v4.6の設定保存・復元システムテスト担当者です。
Test ID: ST-UC-27
Requirement: TR-34 / AC-035

T01時点の未実装・NOT_RUNは履歴です。現在はApplicationSettings／SettingsFileStore、SettingsViewModel、ServiceRegistration、ExecutionViewModelの実装と直接testがあります。§6.1の局所証跡をこのPromptの今回PASSへ転記しません。捏造禁止。実利用者のsetting.txt・credential・canonical sampleを読み書きせず、合成dataと注入した一時absolute pathだけを使用します。production AI、実認証確認、本人login、branch、commit、公開を実行しません。E2Eの明示確認／実行はtest内のfake境界だけです。sourceや既存差分を変更、stash、reset、cleanしません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使用します。

準備:
1. StudyReportEvaluator.slnxを上位探索してrootを確定し、要求§9.1／11.6とdev/docs/ui-layout-contract.mdを読みます。HEAD、status hash、UTC、Windows 11 x64、PowerShell、global.json選択SDKを記録します。
2. 実在owner ApplicationSettingsTests／SettingsFileStoreTests（Tests/Settings）、SettingsViewModelTests／ExecutionSettingsTests／MainWindowTests（Tests/UI）、SettingsCompositionTests（Tests/Composition）、SettingsWorkflowSystemTests（Tests/E2E）のdiscoveryを確認します。直接anchorはSettingsFileStoreTests.Unicode_nested_definition_round_trip_preserves_every_field_order_and_canonical_hash／SettingsFileStoreTests.Exclusive_handle_refuses_read_and_replace_keeps_old_bytes_and_cleans_only_own_temp、SettingsViewModelTests.Save_freezes_values_before_notifications_blocks_duplicate_commands_and_keeps_new_edits_dirty、SettingsCompositionTests.Legacy_registration_constructors_never_resolve_or_read_user_settings、ExecutionSettingsTests.Output_override_survives_input_changes_and_restore_while_null_recomputes_resultです。Testsはtests/StudyReportEvaluator.App.Tests配下を指します。必要case未接続ならBLOCKEDとし、旧path testを代用しません。
3. repository外の一意な一時directoryをstoreへ注入します。既定コンストラクターやproduction構成が実利用者pathへ触れる経路は使いません。単一runnerで必要なlocked restore／Release build後、対象testを名前指定し、今回のcase・件数を記録します。

保存oracle:
- UTF-8 JSON、schema整数1、共通設定＋任意の採点定義1件。通常model希望ID、並列度1〜3（既定1）、absolute出力override／nullを保持する。
- fileなしで既定値を使い、最初の明示保存までfileを作らない。読込完了前に未読定義を空で上書きせず、入力未読込で共通設定だけ保存しても読込済み保存定義を保つ。
- 有効編集・主列変更・遷移・終了だけではdiskを書かない。定義ID、順序、decimal、Unicode、改行、brace、Prompt、canonical hashは保存→別storeの読込で一致する。
- BOMあり／なしを受理する。schema欠損／不正型／未知版、同schema未知項目、不正enum／範囲／Prompt／配点は安全に拒否する。
- 保存時点を固定し同directoryの一意tempへwrite／flush／close後にatomic置換。旧file先行削除・直接切り詰めなし。同一画面二重保存なし、別processは最後に成功した保存が残る。
- 保存中再編集した現在draftは保存成功後も未保存。失敗時は旧fileと現在draftを保持し、保存失敗を表示、自分のtempだけを後始末する。
- 破損・未知版・読込拒否でもfileを保持し通知してoffline継続。自動修復・削除なし。

単一原因のIO拒否:
- 一時既存fileをFileShare.None等で保持した読込／置換拒否、親directory予定pathが通常fileである作成失敗等を使い、実際の失敗を確認する。WindowsのReadOnlyディレクトリだけをアクセス拒否の根拠にしない。
- handle／属性はfinallyで戻し、解放後に旧bytes／hashを比較する。無関係なfolderやcredentialをcleanupしない。

実証範囲の分離:
- SettingsFileStoreTests.Save_uses_the_supplied_record_and_last_successful_save_wins_across_store_instancesは同processの別storeによる順次保存です。別process同時保存・電源断・network filesystemの実測成功とは書きません。
- SettingsWorkflowSystemTests.Saved_output_preference_survives_new_instances_and_input_change_before_real_final_outputは明示先／nullの2ケースで、新store／VMへの復元と入力変更後の実finalを扱います。別process再起動やnative操作とは区別します。
- 最新の表示修正はMainWindowTests.Initial_input_common_settings_preview_does_not_configure_execution／Common_settings_after_input_replacement_preserves_configuration_and_overrideで確認します。Settingsへ移動した時の実効先を確認し、表示更新だけでExecutionを再構成・保存・認証しません。

平文・非保存oracle:
- 合成header由来の設問textと利用者貼付相当の合成Promptは、定義の明示保存時に平文で含まれる。主列変更だけの自動保存はない。
- 入力xlsx path／bytes、回答行の自動コピー、AI結果／reason／evidence／参照回答、run／checkpoint、credential／login／CLI hash、未適用Prompt一覧、選択ID／ページ／履歴を保存しない。
- log・共有evidenceには本文やprivate pathを含めず、canaryの値ではなく存在／不在とhashだけを報告する。暗号化済みとは主張しない。

出力先oracle（D20）:
- 今回の明示編集 → 保存済み明示指定 → null。nullのときだけ入力A隣接result、入力Bへ変更後はB隣接resultを算出する。自動算出値を明示指定として保存しない。
- 明示absolute pathを保存→再起動相当読込→入力Bでも同じ指定先。主画面の実効先が一致する。
- 空欄はnullへの明示変更として古い保存値より優先し、明示保存・再読込後も既定へ戻る。
- 利用不可指定はno fallbackで修正を要求し、復元だけでdirectoryを作らない。checkpointの既存予約pathを変更しない。

全必須caseの今回実測一致だけをPASS、不一致をFAIL、実装・観測手段不足をBLOCKEDとします。未実施はNOT_RUNのままで、過去PASSや要求承認を根拠にしません。
報告順: Test ID / source・環境 / test実在・前提 / case別期待・実測 / 保存・復元・IO拒否 / 旧file・draft保持 / privacy・cleanup / 証跡hash / Status / 未確認点。
```

### ST-UC-28: 保存定義の明示適用と失敗・取消時の無変更

```text
あなたはStudyReport Evaluator v4.6の保存定義明示適用システムテスト担当者です。
Test ID: ST-UC-28
Requirement: TR-35 / AC-036

T01の未実装・NOT_RUNは履歴で、現在はInputViewModel.ApplySavedDefinitionAsync、SettingsViewModel.ApplySavedDefinitionAsyncと親shellの同期を実装済みです。既存局所証跡は§6.1と分離し、今回結果を捏造しません。合成workbookと一時pathの設定storeだけを使い、実利用者設定・canonical sample・credentialへ触れません。sourceを変更、stash、reset、clean、commitせず、production AI／実認証確認／本人login／公開を実行しません。E2Eの明示確認／実行はfake境界だけです。本文、Prompt、header、private pathをlogやreportへ出しません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使います。

準備:
1. StudyReportEvaluator.slnxを上位探索してrootを確定し、要求§11.7とdev/docs/ui-layout-contract.mdを確認します。HEAD、status hash、UTC、Windows 11 x64、PowerShell、global.json SDKを記録します。
2. SavedDefinitionApplicationTests／SettingsViewModelTests／SettingsViewTests／MainWindowTests／WorkflowStateTests（tests/StudyReportEvaluator.App.Tests/UI）とSettingsWorkflowSystemTests（同E2E）のdiscoveryを確認します。直接anchorはSavedDefinitionApplicationTests.Application_reloads_the_saved_header_and_preserves_ids_order_prompts_points_and_canonical_content／Mapping_failures_preserve_all_loaded_state_without_sheet_column_or_row_fallback、SettingsViewModelTests.Explicit_apply_updates_both_editors_on_success_and_preserves_instances_and_imported_prompts、MainWindowTests.Failed_saved_definition_apply_preserves_divergent_drafts_and_latest_preview、SettingsWorkflowSystemTests.Saved_definition_admission_rejects_missing_sheet_without_mutation_then_accepts_the_matching_inputです。存在しないcaseはBLOCKEDとし、旧mapping試験だけでPASSにしません。
3. 単一runnerで必要なlocked restore／Release build後に対象testを名前指定します。fixtureのinput／settings hashとメモリ内metadata／draft／Design／Imported Promptの比較基点を取得します。本文は報告しません。

単一原因caseとoracle:
- 起動時の保存定義は保持だけ。入力未読込では適用できず、Excel読込後も明示操作前はInput／Designを変更しない。
- 保存済みsheet・header・行範囲・mapping・設問数を表示し、明示適用時に保存headerのmetadataをread-only loaderで取得する。異なるheaderの古いmetadataを流用しない。
- 同入力／別headerで全定義を検証し、成功時だけInput metadata・選択値・draftとDesignを一緒に更新する。
- sheet欠落、列欠落、行範囲不一致、invalid定義、取消の各caseで現在状態と保存fileを変更しない。先に一部を適用してから失敗する状態を残さない。
- 成功時のID・順序・設問text・Prompt・配点・canonical hashが保存定義と一致する。候補再生成でIDを変えたり、別header textで黙って置換したりしない。
- 適用後の利用者による主回答列変更は従来どおり交差セルtextへ同期する。空header・metadata不一致の既存検証も維持する。
- --promptの取込一覧・本文・順序を変更／消去せず、未適用Promptを定義へcopyしない。
- run中の一括適用は禁止して理由を表示し、現在request／snapshot／checkpointを変えない。適用で認証確認・login・AI送信を発生させない。
- sheet／列の存在だけを授業内容の意味的一致としない。resumeは既存checkpoint admissionで判断し、不一致checkpointを変更しない。

全必須case一致だけをPASS、不一致をFAIL、実装・test・前提不足をBLOCKEDとします。未実施はNOT_RUNです。
報告順: Test ID / source・環境 / test実在 / case / metadata・ID・hash比較（本文なし） / 失敗・取消無変更 / Imported Prompt保持 / AI等call count / input・file不変 / 証跡 / Status / 未確認点。
```

### ST-UC-29: 設定往復、model希望、現在run不変とdeterministic設定E2E

```text
あなたはStudyReport Evaluator v4.6の設定・workflow結合システムテスト担当者です。
Test ID: ST-UC-29
Requirement: TR-36 / AC-016 / AC-017 / AC-018 / AC-019 / AC-035 / AC-036 / AC-037

T01の未実装・NOT_RUNは履歴で、現在はMainWindow／SettingsView、既存Input／Design／Execution／ResultsとSettingsViewModelによる往復・固定requestの実装があります。§6.1の既存局所検証を今回PASSへ転記しません。捏造禁止。合成workbook、一時absolute pathの設定store、fake認証／AI／制御runだけを使います。実利用者setting.txt・credential・実学生dataを使わず、本人login・Live AI・外部spreadsheetを実行しません。sourceや既存変更を修正、stash、reset、clean、commitせず、branchや公開操作を行いません。PowerShellはpwsh.exe -NoLogo -NoProfile、7+ Coreだけを使い、本文・private pathを報告しません。

準備:
1. StudyReportEvaluator.slnxを上位探索し、要求§9.1／11.4〜11.8／12.2とdev/docs/ui-layout-contract.mdを読みます。HEAD、status hash、UTC、Windows 11 x64、PowerShell、global.json SDKを記録します。
2. MainWindowSettingsTests／MainWindowTests／SettingsViewModelTests／SettingsViewTests／WorkflowStateTests／ExecutionSettingsTests／ResultsPresentationTests（tests/StudyReportEvaluator.App.Tests/UI）とSettingsWorkflowSystemTests（同E2E）のdiscoveryを確認します。headless往復の直接anchorはWorkflowStateTests.Shell_round_trip_saves_the_latest_mapping_or_evaluator_edit／Deferred_run_uses_captured_request_and_delivers_results_without_closing_edited_settings／Next_draft_changes_neither_previous_result_values_nor_the_last_export_request、固定runはMainWindowSettingsTests.Active_run_progress_and_stop_are_fixed_in_all_five_viewsです。最新競合回帰はMainWindowTests.Input_replacement_during_reload_publishes_only_coherent_execution_stateへ接続します。必要testなしはBLOCKEDとし、既存E2EのPASSで代用しません。
3. 単一runnerで必要なlocked restore／Release build後、対象testを名前指定します。現在runと次回draftを別々に観測できるfake boundaryを使い、実利用者保存先には構成しません。

往復oracle:
- Input→Design→Settings→Input→Executionとカテゴリ間の交互編集→保存で、最新draftを1回同期し、古いDesign参照で上書きしない。
- 設定は4ステップ外の独立内容で、5カテゴリと共通内のDesign定義情報を扱う。対象を保ち1操作で開き、値・対象ID・ページ・カテゴリ・入力途中のtextを保持して元位置へ戻る。
- 同じ入力／定義の往復は新規／再開、partial、進捗、直前runを初期化しない。入力／定義変更時だけ再開指定を再確認または解除し理由を示す。新規／再開・partialは設定fileへ保存しない。
- 保存希望modelあり／なし／欠落、明示確認失敗、auto欠落を別caseにする。希望IDは明示確認の候補にある場合だけ実効選択とし、不在は未選択でno fallback。確認失敗だけで保存希望を消さない。希望なし初回の従来選択とauto検証を分離する。
- 遷移、Prompt適用、設定読込／保存／適用から認証確認／login／runのcall countは0。login完了時は認証状態・model一覧の再確認だけが自動で1回行われ、login再開始／runのcall countは0（要求定義書§11.3の6、§11.8）。明示開始だけが検証済み有効値でrequestを作る。

現在run／前回結果oracle:
- 実行中の前工程・設定編集は次回用で、現在request／immutable snapshot／予約pathを変更しない。保存定義の一括適用は禁止する。
- 全画面で進捗入口・停止を使い、取消後は新規送信なし、既存checkpointを保持する。設定中に完了しても通知だけで強制移動しない。
- 対象数・配点exact合計・実効値、予定名／予約済み／保存中／完成、未保存／保存済みを実データから区別し、経過時間だけで進捗・scoreを作らない。
- 前回結果を次回draftへ混ぜず、一覧ページ移動後も元Results collectionのoverrideを保持する。0とblank、完成版と未保存修正版、別名出力済みを区別し、元finalを上書きしない。

deterministic E2E:
1. 共通設定と有効な定義を明示保存し、新VM／storeで再読込します。設定復元だけでinput／final／partialやoutput directoryを作成しません。
2. 合成Excelをread-onlyで読み、保存定義を明示適用してID／canonical hashを比較します。明示出力先ありとnullを別caseにします。
3. SettingsWorkflowSystemTestsのtest専用adapterで認証・model／runtime identity・AI応答・時刻だけを合成にし、実durable orchestrator→実checkpoint→中断→新instanceで再開→実final／別名override出力を実行します。既存identity admission、参照回答再利用、完了行AIのskip、元本不変、exact100、zero／blank、formula／cached previewを既存独立oracleで確認します。RunSummary／writer成功receiptだけのfakeをこの実file経路へ代用しません。
4. 未適用Prompt一覧・本文・順序と現在snapshotが保存／適用／往復で壊れないことを確認します。今回の一時fileだけをcleanupします。

実file E2Eの直接anchor（SettingsWorkflowSystemTests、4合成回答行、6メソッド・7ケース）:
- Saved_output_preference_survives_new_instances_and_input_change_before_real_final_output（明示出力先／nullの2ケース）
- Saved_definition_admission_rejects_missing_sheet_without_mutation_then_accepts_the_matching_input
- Cancel_saves_a_real_partial_and_new_instances_resume_without_repeating_completed_AI_or_reference
- Override_exports_a_separate_real_workbook_using_the_run_snapshot_not_next_settings
- Real_final_and_preview_distinguish_missing_answer_numeric_zero_and_technical_failure
- Real_atomic_final_validation_rejects_a_corrupted_cached_score_and_does_not_publish_it
後5メソッドは各1ケースです。既存74件と最新216件への収録は過去実行の記録であり、今回のdiscovery・outcomeを別に確認します。WorkflowStateTestsのrun／outputはfake receiptで、Imported Prompt保持等のheadless／VM試験を担当します。T27の新instance復元を別process再起動・native操作とせず、既存530行E2Eも置き換えません。

layout／keyboardはST-UC-12、store障害はST-UC-27、明示適用の失敗はST-UC-28の今回結果へ個別接続し、未実施をまとめてPASSにしません。0.8.4のT39追加nativeは部分観測でFAIL、Narrator・本人walkthrough・隔離利用者native保存はNOT_RUN_EXTERNAL_PREREQUISITEという別scopeの記録を保持し、headless/fake成功で代替しません。本人walkthrough4項目は入力／30・10配点、設定保存と復帰、再起動・明示適用、override未保存の識別です。全必須caseの今回一致だけをdeterministic scopeのPASS、不一致をFAIL、実装・test・前提不足をBLOCKEDとします。
報告順: Test ID / source・環境 / test実在 / 往復・model case / request・snapshot不変 / call count / 前回結果・override / save-read-apply-fake-run-resume結果 / input・privacy・cleanup / scope別Status / 未確認点。
```
