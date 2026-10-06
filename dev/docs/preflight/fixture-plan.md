# G-11 synthetic fixture specification plan

> [!WARNING]
> **HISTORICAL PREFLIGHT:** requirements v1.2用のfixture計画です。現行v3 fixtureとproduction testの正本ではありません。現行状態は[`dev/docs/implementation-status.md`](../implementation-status.md)を参照してください。

| 項目 | 内容 |
|---|---|
| Task | G-11 |
| 状態 | **G-11完了・validation/review PASS** |
| Requirement | `docs/requirements-definition.md` v1.2 §14.1、§17.2 |
| Decisions | ADR-0003、ADR-0004、ADR-0009 |
| Fixed seed | `20260831` |
| Data class | synthetic only / personal dataなし |
| Binary generation | T-08またはG-16。G-11では`.xlsx` binaryを生成・commitしない |
| 記録日 | 2026-08-31 |

## Scope

G-11は、後続のdeterministic fixture factoryが生成する入力を5つのJSON仕様へ固定する。JSONは実行用production codeでも実在workbookの抽出物でもない。暗号化された`sample/`をfixture sourceとして読まず、回答本文、氏名、email、student ID、grade、token、credentialを含めない。

| Spec | 目的 | Case数 |
|---|---|---:|
| `tests/fixtures/specs/workbook-clean.json` | 1/2/10問、typed value、styles有無、sparse row、name collision、preservation、local formula分類、FR-006 exact boundaries | 17 |
| `tests/fixtures/specs/workbook-hostile.json` | file/package/resource/relationship/execution/namespace/reserved-nameの単一原因negative | 34 |
| `tests/fixtures/specs/unicode.json` | NFKC、line ending、Unicode whitespace、scalar、3-shingle multiset、状態優先 | 15 |
| `tests/fixtures/specs/formula.json` | untrusted string 30件、closed AST 7件、生成式validator 13件 | 50 |
| `tests/fixtures/specs/report-definition.json` | positive 6件、schema/semantic/snapshot/budgetの単一原因negative 30件 | 36 |

## Reproducibility

1. 全specは`fixed_seed=20260831`を持つ。generatorは時刻、random UUID、filesystem列挙順、OS localeを入力にしない。
2. ZIP timestamp、entry order、relationship ID、sheet ID、part URI、synthetic valueはseedから決定論的に生成する。
3. 期待比較はraw ZIP圧縮bytesではなくADR-0003の展開payload hash、exact URI、content type、relationship tupleを使う。
4. Unicode正規化はG-16で選択・記録するapp-local ICU exact versionを使う。G-11はversionを捏造しない。
5. `rounding_digits`のengine共通rangeはG-16実測値を参照し、G-11は数値を仮定しない。

## Single-cause negative rule

- `workbook-hostile.json`の各caseは`WC-001`だけをbaselineにし、宣言したmutationを1つだけ適用する。
- `report-definition.json`の各negativeは、明記した場合を除きJAまたはEN positive profileへ1つのmutationだけを適用する。
- resource boundaryは上限ちょうどのpositiveと上限+1のnegativeを別caseとして生成する。
- 複数scannerが同じ入力を拒否できる場合、testは最初に要求されるphaseと主要理由だけをassertし、後続理由の発生を必須にしない。
- 不正JSON、lone surrogate等、通常のobject modelで表せない入力はraw bytesまたはcode-unit配列から生成する。

## Workbook generation rules

- clean specは外部relationship、macro、DDE、OLE、ActiveX、query/connection、digital signatureを含めない。chart/image/table/custom XMLは内部かつ非実行のpreservation対象だけを許す。
- hostile specはclean baselineに単一要素を加え、修復、復号、外部取得、式評価を行わず停止することを要求する。
- `Evaluation_Config`/`Evaluation_Run`予約名、marker、`Eval` collisionの異なる規則を混同しない。
- local formula cached valueは未検証snapshotとして明示確認後に値だけ投影し、formula nodeを複製しない。external referenceとdata-table formulaは拒否する。

## Unicode oracle rules

- operandはquestion text $q$ とlearner prompt $p$であり、answer本文ではない。
- status順は`NOT_APPLICABLE`→`INSUFFICIENT_DATA`→`TOO_SHORT`→`COMPUTED`。
- 3 scalar未満はmetricを計算しない。絵文字caseは3 scalarになる文字列を使う。
- coverage/Jaccardは浮動小数の丸め値ではなく分子/分母を保存し、実装側でexact比較する。
- invalid UTF-16はJSON文字列に直接埋めずcode-unit配列からnegative inputを生成する。

## Formula oracle rules

- untrusted input/LLM stringはdanger検知結果にかかわらず常にinline stringであり、先頭apostrophe追加や文字削除をしない。
- display warningはASCII/fullwidth formula marker、tab/CR/LF、Unicode White_Space、format control、combining markを個別に覆う。
- ReportDefinition formulaはschemaのclosed ASTだけを使う。raw formula textは生成後validatorのnegative fixtureに限定する。
- app-owned formulaは`Eval`と`Evaluation_Config`のverified addressだけを参照し、source sheet、external workbook、defined name、whole row/column、3D referenceを拒否する。
- formula textはADR-0004どおり8,192文字未満を許し、8,192文字をnegative boundaryとする。

## ReportDefinition oracle rules

- shape schema validationとmandatory semantic validationを別結果として記録する。
- positive JA/EN profileは既存のschema exampleを正本とし、G-11で教育内容のproduction defaultへ昇格させない。
- ID uniqueness/reference、Unicode scalar上限、item/flag count、BCP 47、finite score/range/weight、placeholder、formula AST、quote thresholdをsemantic validatorで検査する。
- 最大instanceはproductionと同じserializerで構築する。tool argument、request/context、Excel、attemptのどれか1つでも超えたら切詰めず送信前に拒否する。
- launcher identity、approver、OAuth/token、student answerはReportDefinitionへ追加しない。
- profile変更後はnew snapshot/hash/run IDとし、旧schema/checkpoint/cacheを流用しない。

## Validation procedure for G-11

1. 5 JSONをstrict JSON parserでparseし、root format、seed、synthetic flag、PII flagを検査する。
2. file内と全file横断でcase IDが一意であることを検査する。
3. hostile caseがbaselineとmutationを1つずつ持つことを検査する。
4. ReportDefinition positive example 2件を`eng/schemas/report-definition-v1.schema.json`で再検証する。
5. specのpath、case count、requirement referenceが空でないことを検査する。
6. password、secret、token value、実在個人識別子がないことをcontent reviewする。
7. `src/`、`.csproj`、production/test codeが増えていないことを確認する。

## Downstream ownership

- T-08: specからdeterministic `.xlsx`/raw package fixtureを生成し、manifest/hashを固定する。
- W-04〜W-12: file/package/scanner/fingerprint caseを消費する。
- L-08〜L-14: Unicode、similarity、formula caseを消費する。
- T-03/T-06/C-01/C-02: ReportDefinition、最大instance、dynamic schema caseを消費する。
- E-01/E-03/E-07: RCに対して同じcase IDをE2E再利用する。

## Completion boundary

G-11の完了はJSON仕様と本計画のparse、整合、敵対的reviewまでである。binary生成、production validator、Office/LibreOfficeの計算結果、app-local ICU version、実データ妥当性は後続taskの証跡なしにPASSとしない。

## Validation and adversarial review record

- 5 JSONをPowerShell 7 / `System.Text.Json`相当のstrict parse経路で読み取り、152 case IDの全file横断一意性を確認した。
- JA/ENのbase ReportDefinition 2件は`eng/schemas/report-definition-v1.schema.json`へ適合した。
- Unicode 13 text caseはcase-sensitive ordinal shingle辞書で正規化・包含・coverage・multiset Jaccardの分子/分母を再計算し、全期待値と一致した。
- 初回reviewで確認した圧縮率exact値、FR-006 positive boundary、cached formula表示、LF/CR whitespace正規化、未定義classification、empty instruction error codeを修正した。
- 修正後の独立敵対的reviewは、resource boundary 11対、WH-008、UC-006、全明示fraction、formula 8,191/8,192境界、validation code、case数、single-cause、synthetic/PII policyを再確認し、残存するevidence-backed defect 0件と判定した。
- このPASSはfixture**仕様**に限定し、binary、production code、Office、ICU、cross-OSの実証を含まない。
