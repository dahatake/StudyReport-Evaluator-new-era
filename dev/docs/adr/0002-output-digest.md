# ADR-0002: 出力 digest と detached completion record

> [!WARNING]
> **HISTORICAL / SUPERSEDED:** signed completion recordを含む旧requirements v1.xの設計です。現行実装はこの`.completion.jws` contractを実装しません。現行output契約は[ADR-0011](0011-dynamic-quantification-excel-formulas.md)と[`excel-contract.md`](../excel-contract.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **承認済み・G-02完了・要求v1.2へG-RB反映済み** |
| 対象決定 | DEC-02 |
| 要求 | FR-016、FR-017、FR-035、FR-073、FR-089、AC-001、AC-007 |
| 決定 | canonical manifest digest と署名済み detached completion record |
| 承認根拠 | ADR-0001 の承認証跡（実装前 decision gate は既定案） |
| 記録日 | 2026-08-31 |

## 問題

旧要求正本v1.1のFR-017は「出力SHA-256」を出力workbookの監査sheetへ記録するよう読めた。しかし、確定後の`.xlsx`全体のSHA-256を同じ`.xlsx`内へ書き込むと、その書込み自体でfile bytesとSHA-256が変わる。固定点が得られる保証はなく、単純な再計算では自己参照を解消できない。

次の代案にも不足がある。

- workbook 内部の自己 hash をそのまま維持する: 自己参照が解消しない。
- unsigned sidecar に SHA-256 だけを置く: 別 workbook への差替え、sidecar 欠落、run の取り違えを検出する結合情報が不足する。
- ZIP entry や XML の一部だけを hash する: 確定ファイル全体の同一性を表さない。

## 決定

FR-017 を要求改版し、出力完全性を次の2層で表す。

1. **workbook 内部**: `Evaluation_Run` に `run_id`、入力 SHA-256、選択元 sheet ID/name、生成 sheet name、および `manifest_digest` を保存する。確定ファイル全体の SHA-256 は保存しない。
2. **detached completion record**: 確定直前の検証済み workbook 全 bytes の SHA-256、将来の final filename、`run_id`、`manifest_digest`、record metadata を署名対象として、同じ出力ディレクトリへ別ファイルで保存する。

completion record の既定名は `{final-workbook-filename}.completion.jws` とする。絶対パスや利用者名は record に含めず、final filename だけを記録する。JWS profile、canonicalization、issuer/key の用途拘束は ADR-0006 と schema を正本とする。

`CompletionProof` は Core が定義する不透明かつ immutable な値で、少なくとも `run_id`、`manifest_digest`、prepared workbook SHA-256、意図した final filename を結合する。Platform の completion record writer が有効な署名済み record を durable に確定した後だけ生成し、Workbooks の committer は署名処理や Platform 型を参照せず、この値と prepared file を照合する。これにより `Platform` から `Workbooks` への project 参照を作らない。exact field 型と serialization は F-04 および ADR-0006 schema で固定する。

## Manifest digest

`manifest_digest` は workbook bytes の digest ではない。次の論理 run manifest を、ADR-0006 で承認する唯一の canonicalization 方式で bytes 化し、SHA-256 を計算した値である。

- schema version
- record type `study-report-evaluator/run-manifest`
- `run_id`
- input SHA-256
- app、SDK、CLI、model、Unicode/ICU の各 version identifier
- 選択元 sheet ID/name と生成 sheet name
- mapping、起動後に作成された`ReportDefinition` snapshot（Prompt、rubric、reason/evidence規則、missing flags、target language、item/ID/field上限、weightを含む）のhash、calibration hash
- synthetic/real mode
- 作成日時

次は manifest の入力から除外する。

- `manifest_digest` 自身
- prepared/final workbook の SHA-256
- completion record の signature
- ファイルの絶対パス
- 回答本文、理由、根拠、氏名、メール、学生番号、token、OAuth credential

同じ `run_id` と同じ logical manifest は常に同じ canonical bytes と digest にならなければならない。未知 schema、重複 JSON property、invalid Unicode、非有限数、canonicalization failure は fail-closed とする。

logical manifestは利用者が実行確認したrun planと`ReportDefinition`から送信開始前に1回だけsnapshotし、O-04より前にimmutableとする。この確認は設定者の本人・role承認ではない。実行中にapp、SDK、CLI、model、Unicode/ICU、mapping、ReportDefinition、calibration、sheet identityのいずれかが変化した場合は同じ`run_id`を更新せず、現在のrunを停止して新しいsnapshotと`run_id`を作る。

ADR-0006 と `eng/schemas/signed-record-v1.schema.json` は、実装開始前に manifest の exact JSON property 名、型、required 集合、additional property の扱い、null 禁止、日時表現、hash encoding、数値範囲、唯一の canonicalization 方式、および known-answer vector を定義しなければならない。これが未確定なら T-07、O-04、O-10 を開始しない。`calibration` hash は回答ペアや承認本文ではなく、検証済み署名校正 record の canonical payload bytes の SHA-256 とし、校正 record の変更を manifest へ結合する。

上記除外は PII detector の完全性に依存させない。manifest schema を closed allowlist とし、本文を受け取る property 自体を持たせない。識別列 DTO や自由記述値を manifest builder の入力型へ到達させず、未知 property を拒否する。別経路の PII candidate detection は不完全であり、この構造的境界の代替にしない。

## 確定順序

同じ出力ボリューム上で次の順序を変えない。

1. final filename と completion record 名が未使用であることを確認する。既存ファイルを上書きしない。
2. byte-copy した作業 package を `.partial.xlsx` として完成させる。
3. package を close し、データと必要な中間 buffer を durable flush する。
4. `.partial.xlsx` を read-only で再 open し、Open XML schema、relationship、変更許可リスト、式、上限を検証する。AC-007 として、許可リスト外の全 part payload SHA-256、URI、content type、relationship tuple が入力 fingerprint と一致することも検証する。
5. 同じ read-only handle/file identity から検証済み `.partial.xlsx` の全 bytesの SHA-256 を計算し、handle を close する。
6. 将来の final filename、`run_id`、`manifest_digest`、workbook SHA-256 を含む completion record を生成・署名し、一時名へ書く。
7. completion record を durable flush、再 open、signature/schema/payload 検証した後、既定の completion record 名へ同一ボリューム内で確定する。
8. final filename と file identity の競合を再確認する。
9. completion record と prepared workbook の結合を、同じ file identity の再確認と全 bytes の SHA-256 再計算を含めて再検証する。
10. `CompletionProof` が一致する場合だけ、`.partial.xlsx` を final workbook 名へ同一ボリューム内で rename する。
11. rename 後の final file identity と全 bytes SHA-256 を改めて確認し、final workbook と completion record の pair を read-only で再検証して完了を報告する。

`File.Move`、`File.Replace`、directory entry durability の保証範囲は OS/filesystem ごとに G-16、T-10、O-09〜O-11 で実測する。未検証 filesystem や network filesystem に、同じ原子性を推測で表明しない。

この ADR は AC-007 を満たす検証項目を決めるものであり、part byte identity の達成を実測前に主張しない。G-16、W-12、O-01、O-08、O-09 の証跡が揃うまで AC-007 は未実証である。

step 5 の close から step 10 の rename までは既知の TOCTOU 境界であり、pair の事前・事後再検証だけを排他制御と称しない。対象 OS/filesystem ごとの `IAtomicFileOps` は、可能な場合は write/delete を拒否する handle または同等の primitive と file identity を保持し、rename 直前の再 hash と identity check を単一の狭い commit 操作として実施する。その成立を G-16/T-10/O-09〜O-11 で fault injection と実測により確認する。path swap、hard link/reparse/symlink、rename race を安全に防止または検出できない filesystem では実データ mode を停止し、対応済みと表示しない。

## Pair 検証

完成扱いにするには、次をすべて満たさなければならない。

- workbook と completion record の両方が存在する。
- completion record の JWS、record type、issuer、audience、schema、key purpose、時刻、replay 条件が有効である。
- record の final filename が検証対象 workbook の basename と exact 一致する。record 値は directory component、`.`、`..`、NUL、path separator、lone surrogate を拒否した有効 Unicode string とし、UTF-8（BOMなし）bytes を逐語比較する。case folding、Unicode normalization、8.3 short-name 展開、symlink/reparse 解決を暗黙適用しない。
- record の workbook SHA-256 が、対象 workbook の全 bytes から再計算した値と一致する。
- record の `run_id` と `manifest_digest` が `Evaluation_Run` の値と一致する。
- workbook 内の logical manifest から再計算した `manifest_digest` が一致する。
- input SHA-256 と選択元/生成 sheet identity が manifest と一致する。

workbook 内の logical manifest が O-10 後に1 byteでも変われば、completion record に固定した whole-workbook SHA-256 と一致しないため、step 9または11で検出して完成扱いにしない。内部 manifest を書込み禁止と仮定して安全性を主張しない。

比較は schema で定めた表現へ厳密に従う。対象 filesystem が filename を変換する場合は G-17 の golden vector で検出し、変換後名称を暗黙受容しない。対応可能な basename 契約を正式決定できない OS/filesystem は実データ mode の対応対象から除外する。

## 失敗・回復規則

| 境界 | 許容される残骸 | 完成扱い | 回復 |
|---|---|---|---|
| O-09 完了前（prepared workbook の close・flush・再open・検証・hash のいずれか未完了） | `.partial.xlsx` | 不可 | 同じ run と fingerprint を検証後、再開または清掃 |
| O-09 完了後、O-10 完了前（署名済み record の durable flush・再検証・確定のいずれか未完了） | `.partial.xlsx`、record temp | 不可 | record temp を検証不能なら削除し再生成 |
| O-10 完了後、O-11 rename 前 | `.partial.xlsx`、確定済み completion record | 不可 | pair を検証して rename を再試行、または sidecar-only orphan を清掃 |
| O-11 rename 後 | final workbook、completion record | rename 後 pair 再検証に成功した場合だけ可 | mismatch/missing は `OUTPUT_VALIDATION_FAILED` |

- completion record の作成、flush、署名検証に失敗した場合、final workbook 名を作らない。
- step 9 で `CompletionProof`、file identity、workbook SHA-256、manifest のいずれかが不一致なら rename を行わず、`OUTPUT_VALIDATION_FAILED` として `.partial.xlsx` と app 所有 record を診断可能な状態で隔離する。自動再署名や別 run への流用はせず、同じ run の checkpoint と fingerprint が有効な場合だけ利用者確認後に O-09 から再開する。
- completion record の確定後に final workbook 名の競合を検出した場合、既存 file を上書きせず、確定済み record を sidecar-only orphan として識別する。元の pair を安全に再開できなければ app 所有確認後に record だけを清掃する。別 final 名を選ぶ場合は、古い record を流用せず、新しい filename を含む record を生成・署名・確定し直す。
- rename に失敗した場合、record は orphan として識別し、final workbook がない限り完成表示しない。
- workbook だけ、record だけ、異なる run の組合せ、署名不正、hash 不一致はすべて fail-closed とする。
- 利用者が後から片方を削除・置換した場合も、次回検証で不完全または改ざんとして検出する。
- 清掃は app 所有の run ID と命名規則で帰属を確認できる temp/orphan だけを対象にし、利用者の他ファイルを削除しない。

## 要求改版案

G-RB は FR-017 を少なくとも次の意味へ変更する。

- `Evaluation_Run` には input SHA-256、run ID、生成日時、version、sheet identity、canonical manifest digest を保存する。
- final output workbook の全 bytes の SHA-256 は、署名済み detached completion record に保存する。
- workbook と record は run ID、manifest digest、final filename、workbook SHA-256 で相互結合し、両方を検証できた場合だけ完成扱いにする。
- 回答本文、PII、token、絶対パスをどちらの監査 metadata にも含めない。
- 欠落、取り違え、署名不正、hash 不一致、確定失敗では完成扱いにしない。

## 影響

### 利点

- 自己参照なしに final workbook 全 bytes の SHA-256 を検証できる。
- sidecar 単独の差替えを signature と相互参照で検出できる。
- final 名を早期に残さず、クラッシュ境界を明示できる。

### コストと残余リスク

- 利用者は workbook と completion record を pair で保持する必要がある。
- filesystem の rename/flush 保証は API 名だけでは証明できず、対応 OS/filesystem ごとの実測が必要である。
- SHA-256 は signature ではない。authenticity は completion record の承認済み署名 profile に依存する。

## 完了判定

自己参照問題、pairの結合、欠落、取り違え、署名不正、書込み失敗、rename失敗、crash時の完成判定を定義し、要求v1.2のFR-017へ反映した。ADR-0006と後続実測依存を満たすまでO-04/O-10を完成扱いにしない。
