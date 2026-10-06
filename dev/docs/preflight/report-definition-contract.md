# Runtime ReportDefinition preflight contract

> [!WARNING]
> **HISTORICAL PREFLIGHT:** requirements v1.xのReportDefinition contractです。現行v3の`QuantificationDefinition`には`target_language`、missing flags、reason policy等を実装しません。現行decisionは[ADR-0011](../adr/0011-dynamic-quantification-excel-formulas.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **G-10完了（production実装はGATE-0待ち）** |
| Schema | `eng/schemas/report-definition-v1.schema.json` |
| Positive vectors | `eng/schemas/examples/report-definition.synthetic-ja.json`; `eng/schemas/examples/report-definition.synthetic-en.json` |
| 要求 | FR-030〜039、FR-048/049/052/053、ADR-0005、ADR-0009 |
| データ | 合成構造だけ。実在学生回答、reason/evidence実例、承認者、tokenなし |
| 記録日 | 2026-08-31 |

## 目的

アプリケーションreleaseへ特定授業のPrompt、rubric、reason/evidence本文、missing flag、言語、教育上限を埋め込まず、利用者が起動後にreport種類ごとに作成できるcontractを固定する。

このcontractは設定値そのものを事前承認しない。製品が保証するのは、設定をclosed構造として検証し、安全上限を超えるrunを送信前に拒否し、実行中に設定を差し替えないことである。

## 設定と出力値の区別

| 種類 | ReportDefinitionに保存 | 評価時に生成 |
|---|---:|---:|
| reason instruction / max scalars | Yes | No |
| 実際のreason本文 | No | Yes |
| evidence instruction / max scalars | Yes | No |
| 実際のevidence抜粋 | No | Yes |
| missing flag ID/表示名/条件 | Yes | No |
| 当該回答に付いたmissing flags | No | Yes |
| rubric/anchor/weight/Prompt | Yes | No |
| 学生回答 | No | 入力snapshotから当該sessionへだけ送信 |
| target language | Yes | reason生成規則として使用 |

## Schema validation後のsemantic validation

JSON Schema合格だけでrun可能にしない。次を順に検査する。

1. JSON admission byte/depth/property/array limits内でstrict parseし、duplicate propertyとtrailing tokenを拒否する。
2. `report_type_id`、question ID、evaluator ID、item ID、missing flag IDは各scopeで一意とする。
3. `questions[].evaluator_ids`は同じdefinition内のevaluatorだけを参照し、dangling reference、同じreferenceの重複を拒否する。
4. actual evaluator ID/item IDのUnicode scalar数は利用者設定max以下とする。UTF-16 `Length`だけで判定しない。
5. evaluatorごとのactual item数は`max_rubric_items_per_evaluator`以下とする。
6. `max_missing_flags_per_item`は定義flag数以下でなければ拒否し、flagが0種類なら0だけを許す。unknown flagは常に拒否する。
7. BCP 47 parserで`target_language`を検証する。language availabilityや回答言語を自動推測せず、evidenceを翻訳しない。
8. score/anchor/question weight/item weightはfiniteで、minimum <= maximum、anchor scoreはrange内、required weight集合の合計は正とする。`rounding_digits`はintegerかつG-16で実証したengine共通range内とし、暗黙defaultを補わない。
9. `aggregate_formula`はADR-0004のclosed structured ASTで受け、function arity、許可token、型、DAGを検査してからformula textへserializeし、8,192文字未満を検証する。`setting-ref`は`adopted-score-range`、`item-weight-range`、`question-subtotal-range`、`question-weight-range`、`rounding-digits`だけを許し、range/scalar型と参照可能性を検査する。ReportDefinitionからraw formula textを受けず、設定文字列をExcelへ直接書かない。
10. Prompt placeholderはFR-031のclosed setだけを許し、再帰展開しない。
11. reason/evidence instruction、display name、condition、Prompt等の文字列はuntrusted textとして保持し、code/markup/URI/pathとして解釈しない。
12. `reason_source_quote_rejection_threshold_scalars`はinclusiveな拒否閾値として扱い、その長さ以上のexact contiguous source一致をreason中に検出したら拒否する。`reason_max_scalars`以下でなければ設定を拒否し、大小関係を暗黙補正しない。
13. operational policy、OAuth identity、local launcher identity、承認者fieldをReportDefinitionへ追加しない。

## 最大tool instanceの実測

各evaluatorについてgenerated tool schemaを作り、同じproduction serializerで次のboundary instanceを構築する。

- `evaluator_id`: configured maximum scalar数を満たすsynthetic ID。
- `items`: `max_rubric_items_per_evaluator`件。
- `item_id`: configured maximum scalar数。
- `score`: schemaが許すfinite numberの最長serializationをcontract testで確定。
- `confidence`: 0〜1のfinite boundary。
- `reason`: `reason_max_scalars`。
- `evidence`: `evidence_max_scalars`または`NO_EVIDENCE`の長い方。
- `evidence_status`: `PRESENT`/`NOT_FOUND`の長い方。
- `missing_flags`: configured maximum件、定義された最長flag IDを使用。flagが0種類なら空array。

serialized outputをparseし直し、次を同時に満たす場合だけそのevaluatorをrun可能とする。

- tool arguments全体 <= 32,767 Unicode scalars。
- 各string <= 32,767 characters。
- JSON schema自身とrequest payloadを含むrequest <= 65,536 Unicode scalars。
- tokenized request <= 採用model context windowの80%。
- projected workbookの列 <= Excel上限、各cell/formula <= 各上限。
- evaluation unit数とworst-case attemptsがrun 20,000を超えない。

生成するresult tool schemaはtop-level、item、nested objectの全てを`additionalProperties=false`、全field required、exact item cardinality/ID const、closed flag enum、`uniqueItems=true`、configured maxへ閉じる。`evidence_status=PRESENT`なら`evidence`は`NO_EVIDENCE`以外、`NOT_FOUND`ならexact `NO_EVIDENCE`となる条件分岐をschemaとcodeの両方で検査する。

値を切り詰めたり、一部item/flagを削除して通さない。最大instanceが1 ceilingでも超える場合、そのreportは`REPORT_DEFINITION_INVALID`となりsessionを作らない。設定画面は超過したdimensionと現在値を表示するが、学生本文やPrompt本文をerror/logへ含めない。

## Run snapshot

実行確認後、ReportDefinitionをcanonical JSONへserializeし、SHA-256を計算する。snapshotは少なくとも次へ結合する。

- run IDとlogical manifest digest。
- evaluatorごとのgenerated schema digest。
- checkpoint AAD/body。
- result cache key。
- `Evaluation_Config`/`Evaluation_Run`の表示metadata。
- detached completion recordが参照するmanifest digest。

run中に保存profileを変更してもsnapshotは変えない。変更後に実行する場合はnew run IDを作り、旧checkpoint/cacheを再利用しない。同じreport type/revisionでbytes/hashが違う場合も別snapshotとする。

## Launcher identity

設定の作成、保存、実行確認、review操作にlocal OS usernameや起動者認証を要求しない。任意reviewer labelはReportDefinitionではなくreview eventの自己申告metadataであり、認証済みidentityとして扱わない。

GitHub OAuth `/user`はCopilot service accountとorg membership確認だけに使用し、設定者・起動者・reviewer本人と結び付けない。

## Positive test vectors

1. 同じappで、日本語reportと英語reportを別definition/hash/schemaとして受理する。
2. missing flagが0種類のreportと複数種類のreportを受理する。
3. item/ID/reason/evidence上限が異なる2 reportを受理し、それぞれ別最大instanceを測定する。
4. boundaryちょうどのtool arguments、request、Excel projectionを受理する。
5. run開始後にprofileを編集し、旧run snapshotが不変かつ次run ID/hashが変わる。

## Negative test vectors

1. 0/負/32,767超のpositive limit、actual ID/item count超過、flag定義数を超える`max_missing_flags_per_item`。
2. duplicate/dangling ID、invalid BCP 47、unknown placeholder、unknown AST kind/function/token/reference、invalid arity/type/cycle。
3. 最大instanceがtool/request/context/Excel/attempt ceilingを1だけ超える。
4. unknown/duplicate/別report missing flag、flag最大数超過。
5. reason/evidence超過、`PRESENT`だが非連続/翻訳されたevidence、`NOT_FOUND`だが`NO_EVIDENCE`でない値。
6. 同じreport type/revisionの別hash、旧schema/checkpoint/cacheのreuse。
7. launcher identity、approver、token、学生回答をReportDefinitionへ追加したunknown property。
8. semantic check 1〜13の各単一失敗を対応するclosed code（`REPORT_SCHEMA_INVALID`、`REPORT_ID_INVALID`、`REPORT_REFERENCE_INVALID`、`REPORT_LIMIT_INVALID`、`REPORT_LANGUAGE_INVALID`、`REPORT_SCORE_INVALID`、`REPORT_FORMULA_INVALID`、`REPORT_PROMPT_INVALID`、`REPORT_UNSAFE_TEXT`、`REPORT_FORBIDDEN_FIELD`）で拒否し、未実装checkをPASSにしない。

## 未実装事項

本書とschemaはcontractであり、production code、UI、実tool schema、serializer測定器はGATE-0後のT/L/C/UI taskで実装する。未実装をPASSと記録しない。
