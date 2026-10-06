# Sample workbook structural profile

| 項目 | 値 |
|---|---|
| Task | B-07 |
| 状態 | **HISTORICAL MEASUREMENT / CURRENT STRUCTURAL TEST POLICY** |
| Requirement | `docs/requirements-definition.md` v4.3 |
| Plan | `dev/docs/archive/work/20260902-readme-end-user-release-plan.md` |
| Scope decision | ADR-0012 / ADR-0013 |
| Sample | `sample/SampleReport.xlsx` |
| Verification date | 2026-09-04 |
| Inspection boundary | read-only metadata/header-role inspection; response bodies not recorded |

## Identity（2026-09-04の履歴）

| Property | Historical verified value |
|---|---|
| Bytes | 470,806 |
| SHA-256 | `73883CE3BBB86B93AF8825C04F596434CF82A2C6309A7F4CC5835AE8F3E542EA` |
| Container | readable ZIP / Office Open XML `.xlsx` |
| ZIP entries | 11 |
| Package relationships | 8 |
| Required structural entries | `[Content_Types].xml`; `xl/workbook.xml`; `xl/_rels/workbook.xml.rels` |

上記と以下の構造・候補は2026-09-04の履歴測定であり、現在fileの再測定結果ではない。SHA-256は当時のsampleのexact-byte identityであり、署名、作成者identity、内容の正しさ、教育的妥当性を証明しない。別の利用者workbookはこのhashと一致する必要がない。

## Workbook structure（履歴）

| Worksheet | State | Used dimension | Initial treatment |
|---|---|---|---|
| 1件（name SHA-256 `88D759EA02CEF4B82885C6C620473162757C75522805707C20E2BE76A40A2825`） | Visible | `A1:L531` | initial candidate source sheet |

このworksheetでは1行目をheader候補、2〜531行をdata row候補とする。これは初期suggestionであり、利用者はsource sheet、header row、first/last data rowを変更できる。

## Initial column-role suggestions（旧12列sampleの履歴）

| Column | Observed header-derived candidate | Initial suggestion |
|---|---|---|
| A〜E | header semanticsから評価対象候補にならない | unselected |
| F | primary semantics | primary candidate |
| G | primary + student Prompt semantics | primary / student Prompt candidate |
| H | primary + supporting semantics | primary / supporting candidate |
| I | primary semantics | primary candidate |
| J | primary + student Prompt semantics | primary / student Prompt candidate |
| K | supporting semantics | supporting candidate for J |
| L | header semanticsから評価対象候補にならない | unselected |

このprofileはheaderの意味から得たsuggestionだけを固定する。列位置やheader文字列でquestion typeを確定せず、候補外の列も利用者が任意のprimary／supporting columnとして選択できる。同じ列を複数questionへ使えるが、同一question内のprimary/supporting重複は拒否する。

`Student Prompt candidate`は、学生Prompt自体を定量化するときにその列をquestionのprimaryへ選べるという意味である。列へevaluator typeを固定する意味ではなく、Knowledge／Custom evaluatorはquestion definition上で別に構成する。

## Content handling boundary

- 回答本文、学生Prompt、考慮事項、feedback、identifier値を本profileへ転記しない。
- sample本文をunit test fixture、snapshot、log、gate artifact、live AI smokeへコピーしない。
- real-sample structural testはcanonical path、sheet/dimension、header-role suggestion、実行前後のinput不変を検証し、過去の固定bytes・SHA-256・sheet name hashとの一致は要求しない。
- sample契約はexact path `sample/SampleReport.xlsx`だけを使い、同directoryの他fileを列挙、fallback、代用しない。
- local deterministic technical E2Eも同じ`sample/SampleReport.xlsx`をread-onlyで使う。
- AI、reason、evidence、scoreを本sampleから生成してpreflight evidenceにしない。
- actual user processingでは、利用者が選択した同一行primary/supporting cellだけをPromptへ含める。

## Synthetic test replacement

実装testはfixed-seed generatorで、次の構造を持つ非実データworkbookを作る。

- minimal valid workbook
- 531行のlarge synthetic workbook
- 1／2／10 questions
- 1／2／5 evaluators per question
- 1／4／20 criteria per evaluator
- empty primary、blank supporting、formula-marker text、Unicode、cell length boundary
- corrupt ZIP、macro/protected classification、sheet collision、output fault seams

Synthetic valueはsample本文から導出せず、期待値と同時にreview可能な固定seedで生成する。

## Drift rule

**2026-09-16 利用者明示承認:** 固定sample hash一致を合否条件から外し、App.Testsの失敗を解消する指示に基づき、過去の固定bytes・SHA-256・sheet name hash・先頭128 bytesおよび11 entries／8 relationshipsの一致をstructural test／local deterministic technical E2Eのgateにしない。上記の旧測定値は書き換えない。package件数はread-onlyで取得したZIPの実`Entries.Count`と`classification.PackagePartCount`の一致、metadataのpackage／relationship件数とclassificationの一致、relationship件数が正かつclassifierの安全上限内であることを検証する。canonical path、標準`.xlsx`の安全性・external relationships 0、sheet/dimension、行列数、mapping suggestion等の構造assertは維持し、これらの不一致を許容したことにはしない。

`RealDataSystemSmokeTests`のevidenceでは`sample_size_match`、`sample_sha256_match`、`sample_prefix_128_match`を実比較による履歴診断値として残し、`historical_sample_identity_gating=false`を明記する。`false`でもそれだけでFAIL／BLOCKEDにせず、旧sampleとの一致やattachment同一性が証明されたと報告しない。現在のsize・SHA-256・last-write UTCと構造は各実行で取得し、履歴と区別して記録する。変更理由や構造差分のreviewでは本文を記録せず、再profileする場合は日付付きの別測定として残す。

この承認はtest実行済み・PASSを意味せず、opt-in、実データのLive AI送信禁止、privacy、別途必要な同意条件を変更しない。

App自体は任意の標準`.xlsx`を扱うため、runtime inputへ本sample hashを要求しない。runtimeでは選択file自身の開始時snapshotを取得し、final rename直前にSHA-256、size、last-write timeの厳密一致を再確認する。

## Current read-only observation（2026-09-16）

固定SHA一致gateを外した後のtestで、package件数の実値13と旧期待値11の不一致が判明した。親担当から共有されたread-only測定は次のとおりである。

| Property | Current observed value |
|---|---|
| FileSize | 469,976 bytes |
| ZIP entries | 13 |
| Package relationships | 9（root 4〔classificationlabelsを含む〕、workbook 4、sheet1のtable 1） |
| External relationships | 0 |
| Classifier | `StandardXlsx`として受理 |

観測した13 entries（内容本文は記録しない）:

- `[Content_Types].xml`
- `_rels/.rels`
- `xl/workbook.xml`
- `xl/_rels/workbook.xml.rels`
- `xl/worksheets/sheet1.xml`
- `xl/theme/theme1.xml`
- `xl/styles.xml`
- `xl/sharedStrings.xml`
- `xl/worksheets/_rels/sheet1.xml.rels`
- `xl/tables/table1.xml`
- `docMetadata/LabelInfo.xml`
- `docProps/core.xml`
- `docProps/app.xml`

13／9は日付付き観測値であり、新たな固定件数gateではない。旧fileの元bytesがないため、どのentry／relationshipが追加・変更されたか、変更原因は証明できない。現在の`LabelInfo.xml`とclassificationlabelsの存在だけで旧fixtureとの差分を断定しない。今回の文書更新でsampleや旧fixtureを変更・差し替えていない。

### 現行sample採用追補（2026-09-16）

利用者不在時の自律続行指示に基づき、現行`sample/SampleReport.xlsx`を次の測定済みprofileで採用する。production `WorkbookMetadataReader`＋`ColumnMappingSuggester`の標準出力は`TestResults/app-fixes-20260916/sample-profile/dahatake_DAHATAKE-OFFICE_2026-09-16_13_59_37_net10.0.trx`に記録されている。test自体は旧primary期待値Fと実値Dの不一致でFAILであり、以下は構造の測定記録であって修正後test／E2EのPASSではない。

| Property | Current measured value |
|---|---|
| Worksheet / dimension | 1件 / `A1:J531` |
| Rows / columns | 531 / 10 |
| Header / first / last data row | 1 / 2 / 531（data 530行） |
| Initial target / unselected | D/E/F/G/H/I / A/B/C/J |

| Column | Measured roles | Suggested supporting columns |
|---|---|---|
| D | PrimaryAnswer | なし |
| E | PrimaryAnswer + StudentPromptPrimary | F |
| F | Supporting | なし |
| G | PrimaryAnswer | なし |
| H | PrimaryAnswer + StudentPromptPrimary | I |
| I | Supporting | なし |

`InputViewModel.CreateSuggestedQuestions`はprimary候補だけを採用し、`CreateDefaultQuestion`は学生Prompt候補だけをCustomPromptにする。`QuantificationDefinition`のbase 60・special 0と`ScoringAllocationCalculator.Equalize`から、D/E/G/Hの4問、KnowledgeCoverage／CustomPrompt／KnowledgeCoverage／CustomPrompt、各10点となる。technical E2Eの期待値はnormal 2,120件（530×4）、references 4件、checkpoint update 534回（4＋530）。これらは本番ロジックからの導出で、E2E実測値ではない。

旧`A1:L531`／12列・F〜Kの履歴値は変更せず、現行sampleだけ本追補の期待構造・候補へ更新する。構造assertを削除・skipせず、canonical path、package整合性・安全上限、external relationships 0、実行前後SHA-256／size／last-write UTC不変、旧固定identityの診断値、privacy・opt-in・Live AI送信禁止を維持する。既存12列synthetic／10人fixtureを変更・代用しない。本追補ではビルド・テストを実行していない。

## Verification result

**2026-09-04の履歴結果: PASS.** 当時のsampleは要求v4.3／B-07の構造profileとして利用できると確認した。これは現在fileの再検証、定量化結果、教育評価精度、Copilot live処理のPASSを意味しない。
