# ADR-0010: 単一Excel入力・Windows local初版scope

> [!WARNING]
> **PARTIALLY SUPERSEDED:** 単一`.xlsx`、Windows local、既存CLI login、入力不変はADR-0011へcarry forwardされています。固定preset、send-preview、PII candidate detection、mandatory human adoptionは現行v3でsupersedeされています。現行decisionは[ADR-0011](0011-dynamic-quantification-excel-formulas.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **承認済み** |
| 決定日 | 2026-08-31 |
| 要求正本 | `docs/requirements-definition.md` v2.0 |
| 実装計画 | `dev/docs/archive/work/20260831-implementation-plan.md` v3.0 |
| 対象 | 初版実装開始gate、入力、認証、platform、評価結果 |
| Supersedes | ADR-0001のv1.2 GATE-0適用、旧計画v2.1のEXT-03〜EXT-09必須依存 |

## Context

旧要求v1.2と計画v2.1は、実装開始前にGitHub組織policy、production trust、privacy、法務、教育評価baseline、署名、cross-platform runner等の外部入力を要求した。その結果、利用者がForms回答Excelからレポートを定量化するという主目的に対して、提供情報と組織依存が過大になり、production実装を開始できなかった。

要求所有者は2026-08-31に、提供すべき情報をForms回答Excelだけに限定し、提示した設問・論点・Prompt例を初期表示にしつつアプリ内で自由編集できるよう、要求定義と実装計画を変更するよう指示した。

確認質問には回答が得られなかったため、要求所有者の目的を満たし、安全性を不必要に捨てない既定を採用した。

## Decision

### 1. 唯一の利用者提供物

本プロジェクトの初版実装と利用において、利用者から開発者へ提供を要求する情報・artifactはForms回答の標準 `.xlsx` 1ファイルだけとする。

- 設問、論点、Prompt、点数rangeはbuilt-in editable presetを初期値とする。
- 実際の授業向け値はアプリ内で作成・編集でき、実装前外部入力にしない。
- Excelはrepositoryへcommitせず、利用時に選択する。

### 2. Excel構造

- 1回答者または1提出を1行として扱う。
- 設問数は可変。
- 設問ごとに必須`REPORT_ANSWER`列をmappingする。
- `STUDENT_PROMPT`と`PROMPT_CONSIDERATIONS`は独立した任意列とする。
- Prompt考慮事項が存在する場合は最優先の評価基準とし、実Promptへの反映度を評価する。
- 考慮事項だけの場合は`CONSIDERATIONS_ONLY`とし、実Promptの品質とは称しない。
- 両方ない場合、Prompt点は空欄で`NOT_APPLICABLE`とする。

### 3. 初期Prompt

要求所有者が提示した2つのレポート設問と、レポート評価／Prompt能力評価の例をbuilt-in presetとして実装する。

- レポート候補点の初期rangeは0〜30。
- Prompt候補点の初期rangeは1〜10。
- 設問、論点、Prompt、rangeは画面で自由編集可能。
- 誤字を修正する。
- 一般的な整理に合わせ、「明示ルールから結果を導くプログラミングは演繹的、例からモデルを学習する機械学習は帰納的な側面を持つ」とする。
- 学生Prompt欄に学生レポートを代入しない。

### 4. Authentication

アプリ固有OAuth App、client ID、client secret、signed organizational policyを初版要件にしない。GitHub Copilot SDK for .NETの標準logged-in user credentialsを使用する。

- 利用者は必要時にCopilot CLIの対話的loginをGitHub画面で直接行う。
- password、PAT、access token、refresh token、client secretをアプリまたは開発者へ提供しない。
- 未認証時は新規AI評価だけを停止し、Excel読込、mapping、設定編集、既存結果reviewは継続できる。
- 表示するGitHub loginをlocal launcher本人または採点者identityとはみなさない。

### 5. Data boundary

- identifier列、他行、他設問の回答を送信しない。
- 毎run、代表payloadと除外fieldをpreviewし、利用者確認後だけ送信する。
- 回答内のemail／電話番号候補を簡易検知し、warningと伏字previewを提供する。
- 検知は完全ではないと表示する。
- 回答本文、学生Prompt、考慮事項、reason、evidence、tokenをlogへ保存しない。

外部privacy／legal approval artifactをアプリが検証する機能は初版scope外とする。これは法的助言、機関内規則の免除、実データ送信権限の保証を意味しない。利用者は画面とREADMEの説明に基づき、自身が処理・送信権限を持つことを確認する。

### 6. Evaluation boundary

- AI点は常に候補点。
- `MET`／`PARTIAL`の論点には、実際に送った同一行・同一設問の指定source fieldに存在する逐語substringの根拠を必須とする。
- `NOT_FOUND`では根拠を空にする。
- 人が採用または上書きするまで確認済み点を空欄にする。
- AI候補だけで最終成績を自動確定しない。

### 7. Platform and distribution

初版の検証済み対象はWindows 11 x64だけとする。

- .NET 10 self-contained folder publishを作る。
- local zipとSHA-256を生成する。
- production code signing、signed installer、macOS notarization、Linux package signingを初版必須成果物にしない。
- macOS、Linux、Windows Arm64は実測なしに対応済みと表示しない。

### 8. Revised GATE-0

GATE-0は次だけを確認する。

1. 要求v2.0と計画v3.0がcommit済み。
2. 本ADRが承認済み。
3. current baseline、exact file map、traceabilityが新scopeへ更新済み。
4. 文書の独立reviewでblocker/high defectが0件。
5. gate判定前のproduction `src/` fileが0件。
6. hidden external blockerが0件。

EXT-03〜EXT-09は初版GATE-0条件ではない。旧artifactの`BLOCKED`状態を新scopeへ持ち越さない。

## Supersession semantics

- `docs/requirements-definition.md` v2.0と計画v3.0が現行正本である。
- ADR-0001のstrict gateは、要求v1.2／計画v2.1へ適用されたhistorical decisionであり、現行GATE-0には本ADRを適用する。
- ADR-0002〜0009は旧scopeの設計履歴として保持する。
- v2.0が明示的に採用した入力不変、string-cell出力、capability最小化、session削除、launcher identity非依存等のinvariantだけを現行実装へ引き継ぐ。
- 旧preflight、traceability、gate-resultはG0-03、G0-04、GATE-0で新scopeへ置換するまでhistoricalであり、現行判定に使用しない。
- 外部入力が不要になったことを、それらの承認や実測が存在するという意味に読み替えない。

## Consequences

### Positive

- 追加policyや組織artifactを待たず、実装を開始できる。
- 利用者はForms回答Excelだけを選択してworkflowを開始できる。
- 提示された教育用途をbuilt-in presetで直ちに利用できる。
- Windows 11 x64へtargetを絞り、実測可能なclaimだけを行える。

### Trade-offs

- アプリは機関approval、契約tier、保持条件、法的根拠を技術的に保証しない。
- formal educational validationやsubgroup fairnessをrelease gateにしないため、候補点の妥当性は利用者のreviewに依存する。
- existing CLI loginは簡単だが、managed OAuth Appやorganization enforcementを提供しない。
- unsigned local packageはpublic distribution用のproduction trustを提供しない。
- macOS、Linux、Arm64は初版非対応となる。

これらのtrade-offはUIとREADMEへ明記し、対応していない能力を黙って主張しない。

## Future change rule

次を正式scopeへ戻す場合は、要求改版、owner、evidence、test matrixを新たに定義する。

- managed organizational policy／OAuth App
- formal privacy／legal approval verification
- educational baseline／threshold／subgroup validation
- macOS／Linux／Arm64
- production signing／notarization／installer
- row printing
- protected workbook support

## Approval record

| 項目 | 値 |
|---|---|
| Approver | 本セッションの要求所有者 |
| Approval source | 2026-08-31の明示Prompt |
| Approved intent | 提供情報をForms回答Excelだけに限定し、提示Promptをeditable初期値として全実装できる要求・計画へ変更する |
| Identity semantics | セッション指示の記録であり、組織電子署名または外部法務・security承認とは称しない |

## Result

**APPROVED.** 現行初版scopeにはADR-0010を適用し、G0-03以降でbaseline、path map、traceability、GATE-0を再生成する。
