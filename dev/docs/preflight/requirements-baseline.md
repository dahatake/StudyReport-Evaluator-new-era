# Requirements baseline integration record

> [!NOTE]
> **BASELINE SNAPSHOT:** B-03時点のidentityとdependency boundaryです。末尾の`pending`は当時の進捗であり、現在の実装状態ではありません。現在状態は[`dev/docs/implementation-status.md`](../implementation-status.md)を参照してください。

| 項目 | 値 |
|---|---|
| Task | B-03 |
| 状態 | **CURRENT BASELINE** |
| Requirement | `docs/requirements-definition.md` v3.0 |
| Plan | `dev/docs/archive/work/20260831-implementation-plan.md` v4.0 |
| Scope decision | `dev/docs/adr/0011-dynamic-quantification-excel-formulas.md` |
| Sample profile | `dev/docs/preflight/sample-workbook-profile.md` |
| Requirement/plan commit | `adc6ad12a3c80b4a92c9453d8b7a58e2daeb346f` |
| ADR/profile commit | `502b48898411ca247543143773be8a3ee28ea7e5` |
| Previous baseline | requirement v2.0 / plan v3.0 / ADR-0010 — **SUPERSEDED** |
| 記録日 | 2026-09-01 |

## Requirement owner direction

要求所有者は、レポート結果Excelの選択内容をPromptで定量化し、アプリ内で設定した重みをExcel数式で計算し、入力を変更せず別Excel fileへ出力するよう指示した。

- 知識評価は、指定知識について単語一致ではなく説明、関係、適用が回答へ含まれる程度を数値化する。
- 知識以外は利用者が分析Promptと評価項目を入力する。
- question、evaluator、criterionは動的に追加、複製、並べ替え、無効化、削除できる。
- AIはcriterion raw scoreだけを返す。
- criterion、evaluator、questionのweightはアプリで設定し、Excel formulaがnormalized、evaluator、question、overallを計算する。
- 教育倫理warningは画面表示だけとし、同意、承認、人手確認、score gate、実行blockにしない。

ADR-0011はこの要求を現行scopeとして承認し、ADR-0010の固定候補点・必須人手確認境界をsupersedeした。

## Artifact identity

下表の旧 `work/` パスは記載commitのidentityとして保持する。[現在の保存先](../archive/work/20260831-implementation-plan.md)への移動は、当時のbytes・SHA-256を再定義しない。

| Artifact | Bytes | SHA-256 | Content commit |
|---|---:|---|---|
| `docs/requirements-definition.md` | 30,480 | `ACB7E1C524E1628C48AC2669B5B2FC730B3EF7EED96723D02F1B5AA6B6264C03` | `adc6ad12a3c80b4a92c9453d8b7a58e2daeb346f` |
| `work/20260831-implementation-plan.md` | 30,790 | `F63D0E1A9AAABFA4C0F2C0A0F032AFE5F5674A64B76723F82D68000A7A506BCD` | `adc6ad12a3c80b4a92c9453d8b7a58e2daeb346f` |
| `dev/docs/adr/0011-dynamic-quantification-excel-formulas.md` | 10,919 | `33A678396FFC9BFB7B5726B34B1874ADC9868C97E9C6EAA3E94FFB0F9C14CC65` | `502b48898411ca247543143773be8a3ee28ea7e5` |
| `dev/docs/preflight/sample-workbook-profile.md` | 4,898 | `A0BA407B55A59363649FE3CEB001B7219838B2F216D8B74FF7E74B9A3B17A2C1` | `502b48898411ca247543143773be8a3ee28ea7e5` |

SHA-256はexact repository bytesのintegrity anchorであり、組織電子署名、本人確認、法務・教育・security承認、実装完了を意味しない。

## Current input and sample boundary

### Required runtime input

| ID | Required value | Timing |
|---|---|---|
| INPUT-01 | 回答を含む標準Office Open XML `.xlsx` 1 file | 利用者がアプリで選択するとき |

question、Knowledgeポイント、Custom Prompt、criterion、range、weightはアプリ内で作成・編集でき、implementation開始前の外部artifactではない。

AI実行時は利用端末でGitHub Copilot CLIへlogin済みである必要がある。credential値はproject inputではなく、利用者がGitHubとの対話で直接扱うruntime prerequisiteである。未loginでもbuild、required fake-transport test、Excel読込、definition編集、既存結果表示を行える。

### Repository sample

- Path: `sample/機械学習 サブフィールド PBL 2025 レポート - コピー.xlsx`
- Bytes: 661,189
- SHA-256: `446386E20BB4096561CB4AFD6D74B8EAA9D50EAE53C97F984BA7F70EBEAD0DE5`
- Initial source suggestion: `Original!A1:L531`
- Initial target suggestions: F、G、H、I、J、K

sampleはruntime inputの固定schemaではない。本文をtest fixture、log、gate evidence、live smokeへコピーせず、real-sample testはmetadata、mapping suggestion、input不変だけを検証する。

## Current functional contract

### Dynamic model

`QuantificationDefinition → QuestionDefinition → EvaluatorDefinition → CriterionDefinition`

- Typeは`KNOWLEDGE_COVERAGE`または`CUSTOM_PROMPT`。
- 実行可能snapshotは各階層で1件以上のenabled childを持つ。
- run開始時のcanonical immutable snapshotをPrompt、expected schema、validation、formula layoutへ一貫して使う。
- draft editはcurrent runへ混入しない。

### AI and scoring

- 評価単位は1行×1question×1evaluator。
- AIはenabled criterionごとのraw、reason、evidence、same-row source IDだけを返す。
- evaluator/question/overall aggregateは返さない。
- empty primaryは`Scorable=0`、AI callなし、overrideなし、全依存score blank。
- nonempty primaryのvalid overrideはAI rawより優先し、AI失敗／blankも補完できる。
- nonempty invalid overrideはblankになり、AI rawへfallbackしない。
- blank enabled childはparent aggregateへblankを伝播する。

### Excel ownership

- Configへsnapshot、range、3-level weight、rounding、mappingを保存する。
- ResultsのScorable、AI raw、override、reason、evidence、source、statusはliteral cell。
- effective raw、normalized、evaluator、question、overallはExcel formula cell。
- 各階層でroundし、parentは丸め済みchild cellを使う。
- formulaはclosed ASTとverified app-owned cell referenceから生成する。
- Core previewとcached valueは`decimal`／away-from-zeroで独立計算する。
- required runtime/testはExcel、Office、LibreOffice、COM automationへ依存しない。

### Warning

教育倫理warningはpersistent non-modal bannerであり、checkbox、同意、承認、role、期限、dismiss、score条件を持たない。warningへ操作しなくてもmapping、run、cancel、override、outputを完了できる。technical validationだけをblockingとする。

## Carried-forward technical invariants

1. 入力workbookをread-onlyで開き、直接変更しない。
2. target-local tempをflush、close、reopen、validateし、same-volume atomic renameで完成名を作る。
3. final rename前に入力SHA-256、size、last-write timeを厳密再確認する。
4. macro、encrypted/protected、corrupt packageをfail-closedで拒否する。
5. selected same-row primary/supporting cell以外、他行、file pathをCopilotへ送らない。
6. evidenceはstable source column IDと同一行の連続substringへ拘束する。
7. untrusted textをliteral string cellとして保存し、formulaへ連結しない。
8. Copilot capabilityを1つのstructured result toolへ限定し、sessionを評価単位ごとに削除する。
9. 回答、Prompt、reason、evidence、token、credentialをlogへ保存しない。
10. 実測していないOS、署名、性能、教育精度、法的適合を主張しない。

## Architecture and dependency boundary

| Production project | Allowed responsibility | Forbidden dependency |
|---|---|---|
| `src/StudyReportEvaluator.Core/` | domain、snapshot、Prompt、validation、scoring preview、formula AST | Avalonia、Open XML、Copilot SDK |
| `src/StudyReportEvaluator.App/` | Avalonia UI、Open XML adapter、Copilot adapter、workflow、composition root | reverse reference from Core |

Test projects are exactly:

- `tests/StudyReportEvaluator.Core.Tests/`
- `tests/StudyReportEvaluator.App.Tests/`

No Application、Infrastructure、Platform、Workbooks、Desktop、separate E2E production project is part of the current plan。

## Not required for initial implementation

- app-owned OAuth registration、client ID、client secret、PAT
- managed organization policy or role approval
- privacy/legal approval verification
- educational fairness threshold、mandatory human review、automatic misconduct determination
- production signing、signed installer、notarization
- macOS、Linux、Windows Arm64 support evidence
- Excel、Office、LibreOffice、COM automation for required runtime/test
- repeated stochastic evaluation、majority vote、median aggregation
- cloud database/server、Forms/LMS API、protected workbook decryption

`NOT_REQUIRED` means outside current initial scope, not externally approved、verified、or unnecessary for every institution。

## Superseded artifacts

- Requirement v1.2/v2.0 and plan v2.1/v3.0 remain in Git history only.
- ADR-0001〜0010 remain design history; ADR-0010 carries forward only the invariants explicitly retained by ADR-0011.
- Previous `requirements-baseline.md`、72-task map、12-AC traceability、v2.0 GATE-0 PASS are superseded and must not authorize current implementation.
- Other old preflight documents remain historical unless the current 37-task exact map names them as dependencies.

## Gate readiness

| Check | Result |
|---|---|
| Requirement v3.0 committed | PASS — `adc6ad1` |
| Plan v4.0 committed | PASS — `adc6ad1` |
| ADR-0011 committed | PASS — `502b488` |
| Sample structural profile committed | PASS — `502b488` |
| Hidden external implementation input | 0 |
| Production `src/` files at this record | 0 |
| Current exact path map | `dev/docs/preflight/implementation-task-file-map.md` |
| Current 15-AC traceability | B-04 pending |
| Revised v3.0 GATE-0 result | pending |

## Completion boundary

B-03 fixes the current artifact identity, dependency boundary, and exact future file ownership. It does not prove production behavior、Copilot live success、formula recalculation by an external spreadsheet、Windows package execution、or educational validity。

B-04 and the new GATE-0 must pass before any production `src/` file is created。
