# G-13 governance input metadata register

> [!WARNING]
> **HISTORICAL / OUTSIDE CURRENT SCOPE:** requirements v1.xのreal-data governance gateです。本文の`BLOCKED`は当時の旧scopeに対する状態であり、現行v3初版のruntime gateではありません。法的・組織的承認が存在するという意味にも読み替えません。

| 項目 | 内容 |
|---|---|
| Task | G-13 |
| 状態 | **BLOCKED — EXT-04未提供、EXT-06適用可否未提供** |
| Requirement | AC-006、NFR-PRI-001/002、第14.3節、第17節4/6 |
| Plan | `dev/docs/archive/work/20260831-implementation-plan.md` v2.1 G-13 |
| Storage rule | external artifactの本文・承認文・署名値・個人名を保存せず、検証metadataだけを記録 |
| 記録日 | 2026-08-31 |

## Purpose

実在学生データmodeのprivacy/legal inputについて、外部成果物の存在、owner role、exact bytesのSHA-256、validity/review期限、適用jurisdiction/region、external reference、検証状態だけを追跡する。本台帳は通知文、法的根拠、法務見解、承認本文そのものではなく、それらを承認済みと推測しない。

ReportDefinitionの設定approvalやlocal launcher identityは対象外である。G-13の機関privacy/legal governanceは、起動後のreport設定とは別の実データ運用gateとして維持する。

## Status vocabulary

| Status | 意味 | Gate effect |
|---|---|---|
| `NOT_PROVIDED` | external artifact/referenceを受領していない | BLOCK |
| `RECEIVED_UNVERIFIED` | artifactは受領したがhash、owner、scope、validityのいずれか未検証 | BLOCK |
| `VERIFIED` | required metadataとexternal evidenceを全て検証した | 当該rowだけPASS候補 |
| `NOT_APPLICABLE_VERIFIED` | 適用外という外部法務判断のscope/hash/owner/validityを検証した | 当該conditional rowだけPASS候補 |
| `EXPIRED` | `now >= expires_at_utc`またはreview期限超過 | BLOCK |
| `MISMATCH` | external bytes/hash、scope、region、owner authority、referenceの不一致 | BLOCK |

`NOT_APPLICABLE_VERIFIED`をlocal timezone、IP、OS locale、Copilot data region、利用者自己申告だけから生成しない。EUで提供・利用するか、その場合の分類・義務はEXT-06の法務成果物が決める。外部判断が未提供なら適用・非適用のどちらも仮定せずBLOCKする。

## Required metadata schema

各rowは次を全て持つ。本文は持たない。

| Field | Rule |
|---|---|
| `artifact_id` | 本台帳内で一意なstable ID |
| `external_input` | `EXT-04`または`EXT-06` |
| `purpose` | closed purpose label。自由な承認本文ではない |
| `expected_owner_role` | plan/requirementが要求するrole。実在人物名・本人認証ではない |
| `provided_by_role_ref` | 外部directory/approval systemのrole reference。未提供時`NOT_PROVIDED` |
| `sha256` | external artifact exact bytesのuppercase 64-hex。未提供時`NOT_PROVIDED` |
| `valid_from_utc` | UTC秒精度。未提供時`NOT_PROVIDED` |
| `expires_at_utc` | UTC秒精度のexclusive upper bound。sourceに失効日がない場合も外部ownerがreview期限を指定する。未提供時`NOT_PROVIDED` |
| `jurisdiction_or_region` | external ownerが明示した適用scope。推測しない。未提供時`NOT_PROVIDED` |
| `external_reference` | 機関管理repository/registryのopaque reference。本文、secret、credential入りURLを保存しない。未提供時`NOT_PROVIDED` |
| `status` | 上記status vocabularyの1つ |
| `verified_at_utc` | 検証時刻。未検証時`NOT_PROVIDED` |
| `verification_method` | `SIGNED_RECORD`、`HASHED_EXTERNAL_ARTIFACT`等のclosed method。未検証時`NOT_PROVIDED` |

## EXT-04 privacy governance

| artifact_id | purpose | expected_owner_role | provided_by_role_ref | sha256 | valid_from_utc | expires_at_utc | jurisdiction_or_region | external_reference | status | verified_at_utc | verification_method |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `EXT-04-NOTICE` | `STUDENT_NOTICE` | privacy責任者 | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` |
| `EXT-04-LAWFUL-BASIS` | `LAWFUL_BASIS_CONFIRMATION` | privacy責任者 | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` |
| `EXT-04-RETENTION-ACCESS` | `RETENTION_AND_ACCESS_RULES` | privacy責任者 | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` |
| `EXT-04-CONTACT-REDRESS` | `CONTACT_CORRECTION_APPEAL` | privacy責任者 | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` |

The row set covers the requirement’s notice, legal basis, retention, access, contact, correction and appeal surfaces without storing their text. A single external package may satisfy multiple rows only when its exact hash and scope are referenced independently for each purpose; one verified row does not imply the others.

## EXT-06 legal / EU applicability

| artifact_id | purpose | expected_owner_role | provided_by_role_ref | sha256 | valid_from_utc | expires_at_utc | jurisdiction_or_region | external_reference | status | verified_at_utc | verification_method |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `EXT-06-APPLICABILITY` | `EU_APPLICABILITY_DECISION` | 法務担当 | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` |
| `EXT-06-CLASSIFICATION` | `CURRENT_AI_ACT_CLASSIFICATION` | 法務担当 | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` |
| `EXT-06-RESPONSIBILITIES` | `PROVIDER_DEPLOYER_RESPONSIBILITIES` | 法務担当 | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` |
| `EXT-06-CONFORMITY` | `CONFORMITY_PLAN_OR_VERIFIED_NOT_APPLICABLE` | 法務担当 | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` | `NOT_PROVIDED` |

`EXT-06-APPLICABILITY`が`VERIFIED`になった後だけ、残り3 rowのrequired/conditional判定を外部decisionのscopeに従って行う。法務がEU適用外を示した場合でも、その判断artifactのmetadataは`EXT-06-APPLICABILITY=NOT_APPLICABLE_VERIFIED`として必要であり、他rowをlocal codeが自動的に`VERIFIED`へ昇格させない。

## Validation rules

1. `artifact_id`と`purpose`はそれぞれ一意で、unknown purposeを受け付けない。
2. `VERIFIED`/`NOT_APPLICABLE_VERIFIED`では、`NOT_PROVIDED` fieldが0件でなければならない。
3. SHA-256はexternal artifactをbinaryで読み取ったexact bytesから計算し、転記された表示本文やdownload後に再serializeしたJSONから推測しない。
4. `valid_from_utc < expires_at_utc`、`valid_from_utc <= now < expires_at_utc`を要求する。equality at expiryは`EXPIRED`である。
5. `jurisdiction_or_region`はartifact scopeとapplication release scopeの双方に適合することを検証する。文字列一致だけで法的適用を決めない。
6. `external_reference`からartifactを取得できない、credentialが必要で検証不能、hash不一致、owner authority不明なら`RECEIVED_UNVERIFIED`または`MISMATCH`でBLOCKする。
7. row間のstatusを伝播しない。noticeがvalidでもlawful basis、retention、redress、EU legal decisionを自動PASSにしない。
8. local launcher、GitHub OAuth login、任意reviewer labelをowner/approver identityへ流用しない。
9. 本台帳にexternal artifact本文、学生data、個人名、email address、電話番号、token、secret、signature bytes、private keyを保存しない。
10. status変更時は以前のmetadataと変更理由codeをaudit recordへ保持し、値を上書きして履歴を消さない。実装はA-14/REL-03で行う。

## Current evaluation

| Check | Actual result |
|---|---|
| EXT-04 required rows | 4/4 `NOT_PROVIDED` |
| EXT-06 applicability | `NOT_PROVIDED` |
| External SHA-256 values | 0 |
| Verified owner references | 0 |
| Validity/expiry values | 0 |
| Jurisdiction/region values | 0 |
| Stored external bodies | 0 |
| Stored personal identities | 0 |
| G-13 disposition | **BLOCKED** |
| Real-data mode | **BLOCKED** |

## Completion condition

G-13 becomes complete only when every required EXT-04 row is `VERIFIED` and EXT-06 applicability plus all rows it makes required are `VERIFIED`, or a specific conditional row has external-evidence-backed `NOT_APPLICABLE_VERIFIED`. Every passing row must have owner role reference, SHA-256, valid-from, expiry/review deadline, jurisdiction/region and external reference. This document deliberately records absence rather than fabricating those values.
