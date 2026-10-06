# ADR-0009: 起動後のReportDefinitionとlauncher identity境界

> [!WARNING]
> **HISTORICAL / SUPERSEDED:** `target_language`、missing flags、reason policy等を持つ旧ReportDefinitionの設計です。現行v3の`QuantificationDefinition` contractではありません。現行decisionは[ADR-0011](0011-dynamic-quantification-excel-formulas.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **要求所有者訂正を反映・承認済み** |
| 対象 | FR-030〜039、FR-041、FR-044、FR-048/049/052/053、FR-104、DEC-09 |
| 決定 | 教育内容とreport固有上限を起動後の利用者設定へ移し、local launcher identity/role認証を対象外にする |
| 根拠 | 2026-08-31の要求所有者による明示訂正 |
| 記録日 | 2026-08-31 |

## 訂正された前提

初版計画はreason/evidence例、missing flags、最大rubric item数、ID長、対象言語を実装前に提出・承認するEXT-01として扱い、field limitをrelease定数へ固定しようとしていた。これは、レポート種類と設問に応じて利用者がアプリ起動後に評価設計を作る製品目的と一致しない。

次を要求所有者の決定として採用する。

1. reason/evidenceの**実際の本文**は各学生回答に対してmodelが生成するため、実装前に作成・提出しない。利用者はreport種類ごとに、その出力規則、必要性、最大長を設定する。
2. `missing_flags`の教育上のtaxonomyはreport種類ごとに利用者が設定する。製品releaseの固定enumにしない。
3. 最大rubric item数、evaluator ID/item ID最大長、target languageはreport作成画面の設定項目とする。
4. これらの値に教員責任者・製品責任者approvalを要求しない。
5. アプリを起動したlocal人物の本人性、所属、role、承認権限はアプリ責任範囲外とする。

## 三つの境界

### TechnicalHardLimits

製品が所有する不変の安全境界である。

- 1 string property: 32,767 characters以下。
- 1 request: 65,536 Unicode scalars以下、かつ採用model context windowの80%以下。
- 1 tool arguments: 32,767 Unicode scalars以下。
- 1 run: 20,000 outbound attempts以下。
- concurrency: default 8、maximum 16。run内では1つの検証済みCopilot CLI processを共有し、attemptごとのsession分離を維持する。rate limit時はAIMDで有効並列度を下げ、quota exhaustedは自動反復しない。
- Excel/Open XMLの列、cell、formula、sheet/package物理上限。

利用者はこれを引き上げられない。report設定に対して一律の教育上限を製品が埋め込む理由には使わない。

### ReportDefinition

利用者が起動後に作成する、report type/revision単位のlocal設定である。少なくとも次を持つ。

- `report_type_id`、`revision`。
- `target_language`（BCP 47 tag）。初版はreport全体で1つとし、reason等の生成言語である。evidenceを翻訳・正規化しない。
- question、Prompt template、evaluator、rubric item、anchor、score range、weight、rounding、closed aggregate formula AST。raw Excel formula textは受けない。
- `reason_policy`: instruction、required/optional、`reason_max_scalars`、`reason_source_quote_rejection_threshold_scalars`。閾値以上の長さで主回答とexact contiguous一致するreason部分を拒否し、閾値はreason最大長以下とする。
- `evidence_policy`: instruction、exact quoteまたは`NO_EVIDENCE`の規則、`evidence_max_scalars`。
- 利用者定義`missing_flags`: stable ID、表示名、適用条件。
- `max_missing_flags_per_item`。定義flag数以下で、flagが0種類なら0とする。
- `max_rubric_items_per_evaluator`。
- `evaluator_id_max_scalars`、`item_id_max_scalars`。

ReportDefinitionは運用policyやidentity recordではない。利用者氏名、role、承認者、学生回答、OAuth account、tokenを含めない。保存profileはアクセス制御済みapp stateへ置き、report content hashを持つ。

### RunSnapshot

実行確認時点のReportDefinitionをcanonical bytesへserializeし、SHA-256とnew run IDへ結合したimmutable valueである。

- sessionごとのtool schemaはRunSnapshotとevaluatorから生成する。
- exact evaluator/item ID、item count、score range、reason/evidence max、evidence status、missing flag enum、flag countを閉じる。
- run中にlive profileを再読しない。
- profile変更後の実行はnew snapshot/new run IDとし、旧checkpoint/result/schema cacheを流用しない。
- workbook `Evaluation_Config`にはsnapshotを表示用に投影するが、出力workbookの編集を進行中runのschemaへ逆流させない。

## reason、evidence、missing flags

reason/evidenceの本文をReportDefinitionへ事前保存しない。ReportDefinitionが持つのは生成・検証規則だけである。

- `reason`は説明文。対象回答の逐語引用経路ではなく、configured target languageとmax scalarsに従う。
- `evidence`は同じ主対象回答にexact contiguousで存在する短い原文引用、または`NO_EVIDENCE`。翻訳、NFC/NFKC、case-fold、空白変換をしない。
- `evidence_status`はsystem enum `PRESENT` / `NOT_FOUND`であり、利用者taxonomyから分離する。
- `missing_flags`は教育上の不足分類で、当該RunSnapshotの利用者定義enumだけを許す。0種類/空arrayを許す。
- 別reportのflag、unknown flag、duplicate、configured maximum超過を拒否する。

## 動的schemaと資源preflight

report設定を保存できることと、その設定でrunを開始できることを分ける。run開始前に次を全て行う。

1. required field、ID uniqueness、actual count/length、score range、language tag、flag定義を検査する。
2. evaluatorごとにproductionと同じJSON serializerを使い、設定された最大item数、最大ID、reason/evidence文字列、最大flag数、最長flag IDを満たす最大instanceを構築する。
3. scalar、UTF-16 code unit、UTF-8 byte、Excel列/cell/formula、model context、run attempt worst caseを測定する。
4. TechnicalHardLimitsを1つでも超える場合は`REPORT_DEFINITION_INVALID`として送信前に停止する。
5. 切詰め、flag削除、item削除、language fallback、max値の自動引下げをしない。

最大instanceを数式上の概算だけで済ませず、actual generated schemaとserializerで検証する。model/SDK/CLI変更時はG-15 contract evidenceを更新する。

## Launcher identity とGitHub OAuth account

アプリはlocal OS account、実行fileの起動者、入力操作をした人物を本人確認しない。管理者、教員、製品責任者等のroleを自動判定しない。

GitHub `/user`で取得するaccount ID/loginは、明示tokenがどのGitHub accountに属し、対象org membershipでCopilot serviceを利用可能かを確認するためだけに使う。これをlocal launcher identity、教員資格、reviewer identity、設定approvalへ転用しない。

- signed operational policyはorg、plan、model、保持/所在/費用等の実データ運用条件を持つが、allowed local user IDやReportDefinition contentを持たない。
- review eventはexplicit UI actionとtimestampを記録する。任意のreviewer labelを許しても自己申告文字列として扱い、認証済みidentityと表示しない。
- report設定保存・実行確認を「教員責任者承認」「製品責任者承認」と呼ばない。

機関が将来launcher/reviewer本人確認を必要とする場合は、認証方式、privacy、offline、role lifecycleを含む別要求として追加し、本ADRを暗黙に拡張しない。

## Positive acceptance

1. 同じapp releaseで、異なるitem数、ID長、language、reason/evidence規則、missing flag集合を持つ2つ以上の合成ReportDefinitionを作成できる。
2. 各report/evaluatorに異なるclosed schemaが生成され、正しい最大境界payloadを受理する。
3. report変更後はsnapshot hashとrun IDが変わり、旧schema/checkpoint/cacheが使われない。
4. local launcher identityを提供せずsynthetic/offline機能を使用できる。
5. GitHub OAuth account確認はCopilot service authorizationだけに使用される。

## Negative acceptance

1. 0/負/overflowの設定、actual item/IDが設定max超過、invalid language tag、duplicate ID/flagを拒否する。
2. 最大serialized tool arguments、request/context、attempt、Excel列/式のいずれか超過を送信前に拒否する。
3. unknown/別report flag、別report schema、translated/non-contiguous evidence、reason/evidence over-limitを拒否し、切り詰めない。
4. run中のprofile変更、同名別hash profile、旧snapshot checkpointを拒否する。
5. OS username、GitHub login、任意reviewer labelを設定approvalや採用点の必要条件にしない。

## 影響する計画

- DEC-09の固定release定数・EXT-01 corpus/approval blockerを撤回する。
- `G-10`は外部評価設計hash収集ではなく、generic ReportDefinition schema/合成profile/最大instance contractの確定taskへ変更する。
- `T-03/T-06/L-02/L-03/L-07/C-01/C-02/C-03/O-04/R-04/UI-05/UI-07`へReportDefinition/RunSnapshotを反映する。
- `L-12`からEXT-01依存を外し、利用者が作ったruntime formula ASTを検証する。
- `A-03/A-10/A-14`からallowed local user/launcher roleを外し、GitHub account/org authorizationだけを残す。
- `R-10/R-11`からauthenticated actor要件を外す。

## 完了判定

教育内容を実装前入力へ固定する誤りを除去し、reportごとの利用者設定、immutable run snapshot、dynamic closed schema、actual serializer preflight、technical hard ceiling、local launcher identity対象外を一つのcontractとして確定した。実装コードはGATE-0前のため存在せず、本変更で未実装結果を主張しない。