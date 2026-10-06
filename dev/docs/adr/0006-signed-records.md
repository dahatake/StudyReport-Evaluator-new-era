# ADR-0006: 署名 record と checkpoint 暗号 profile

> [!WARNING]
> **HISTORICAL / SUPERSEDED:** signed recordとencrypted checkpointを含む旧requirements v1.xの設計です。production trust inputは当時も未確定で、現行初版scopeには含まれません。現行状態は[`implementation-status.md`](../implementation-status.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **承認済み・G-06完了・要求v1.2へG-RB反映済み（G-12実運用入力待ち）** |
| 対象決定 | DEC-12、DEC-13、DEC-14 |
| 要求 | FR-017、FR-035、FR-041、FR-063、FR-073、NFR-SEC-003 |
| 決定 | strict JWS ES256 + RFC 8785 JCS。checkpoint は一回使用data keyによる AES-256-GCM |
| 承認根拠 | ADR-0001 の承認証跡（実装前 decision gate は既定案） |
| 記録日 | 2026-08-31 |

## 適用範囲と外部依存

機関PKIが別方式を指定していないため、DEC-12 の fallback候補である strict JWS ES256を初版profileとして採用する。これはproduction trust root、issuer、鍵保管、発行権限が存在するという意味ではない。G-12で機関指定profileが提供された場合は、本ADRとの互換性を確認し、非互換なら要求所有者・機関PKI・セキュリティ責任者が本ADRをSupersededにしてから実装する。

test用鍵とproduction鍵を混在させない。リポジトリへ置けるのは明示的なtest-only公開鍵・既知テスト秘密鍵だけであり、production private key、token、証明書秘密鍵、vault exportは保存しない。

## 唯一の canonicalization

署名/hash対象のJSONは **RFC 8785 JSON Canonicalization Scheme (JCS)** だけを使う。exact stored bytes方式、独自key順、pretty JSON、別のcanonical JSONをfallbackにしない。

JCS入力はI-JSONへ制限する。

- duplicate propertyを拒否する。
- lone surrogate、invalid Unicode、NaN、Infinity、JSON末尾の余分なtokenを拒否する。
- stringをNFC/NFKC等で変更せず、受領したUnicode scalar列を保持する。
- propertyはRFC 8785どおりUTF-16 code unit順で再帰的にsortする。array順は変えない。
- token間whitespaceを出力せず、canonical bytesはUTF-8（BOMなし）とする。
- 64-bitを超えるsequence、decimal精度、日時、hash、IDはJSON numberにせず規定形式のstringにする。body固有のJSON numberはfinite IEEE 754 binary64でlosslessに扱える値だけを許す。

consumerはJSONをparseしてJCS再serializeし、decoded header/payload bytesがcanonical bytesと完全一致することを要求する。意味が同じでも非canonicalなbytesは受理しない。

## JWS envelope

on-disk形式は `eng/schemas/signed-record-v1.schema.json` に従う **Flattened JWS JSON Serialization** のclosed objectとする。

- required propertyは `protected`、`payload`、`signature` の3件だけ。
- unprotected `header` とunknown propertyを禁止する。
- 各値はpaddingなしbase64urlとし、空、whitespace、`=`、base64標準の`+`/`/`を拒否する。
- `protected` はJCS canonical protected header bytes、`payload` はJCS canonical record payload bytesをencodeする。
- signing inputはASCIIの `protected + "." + payload`。
- `signature` はES256のIEEE P1363 fixed-field concatenation `R || S` 64 bytesをbase64urlした86文字とし、ASN.1 DERを受け付けない。

### Protected header

decoded protected headerは次の3 propertyだけを持つ。

| Property | 値 |
|---|---|
| `alg` | `ES256` |
| `kid` | trust store内のcase-sensitive opaque key ID |
| `typ` | `sre+jws` |

`alg=none`、別algorithm、`jku`、`jwk`、`x5u`、`x5c`、unprotected `kid`、unknown/duplicate headerを拒否する。consumerはheaderの値だけでalgorithmを選ばず、application profileとしてES256/P-256/SHA-256/64-byte P1363を先に固定し、headerが完全一致することを検査する。

### Common signed payload

payloadは次を全て署名対象にする。

| Property | 規則 |
|---|---|
| `schema` | record固有schemaのURN。trust store bindingと一致 |
| `record_type` | schemaで許可されたclosed type |
| `id` | lowercase canonical UUID |
| `iss` | case-sensitive issuer ID |
| `aud` | case-sensitive audience ID |
| `iat` | UTC、秒精度のRFC 3339文字列 |
| `nbf` | UTC、秒精度のRFC 3339文字列 |
| `exp` | UTC、秒精度のRFC 3339文字列 |
| `sequence` | leading zeroなしの非負10進integer文字列 |
| `body` | record固有schemaに従うclosed object |

初版のgeneric envelopeが認識する `record_type` は、`operational-policy`、`calibration-record`、`endpoint-manifest`、`completion-record`、`key-revocation-list`、`offline-recovery-package`、`operations-approval-record`、`independent-review`、`release-manifest` である。generic schemaの認識だけでbodyを受理せず、record typeごとのclosed schema validationを必須にする。

日時は文字列自体のschema検査に加え、calendar-validであること、`iat <= nbf < exp` または発行profileが明示する許可関係、`nbf <= now < exp`、`iat`が許容未来時刻を超えないことをBCL `TimeProvider`で検証する。暗黙のclock skewは0秒とし、機関trust profileが署名対象外のlocal trust設定として明示した非負上限だけを適用する。payloadがclock skewを拡張できないようにする。

## Trust binding と鍵運用

trust storeのlookup keyは少なくとも `(iss, record_type, schema, kid, key_purpose)` とし、`kid`だけで鍵を選ばない。各entryはP-256 public key、許可audience、not-before/not-after、用途、statusを持つ。

- policy keyをcompletion、calibration、revocation等へ横断利用できないよう用途を分離する。
- test trust rootとproduction trust rootを別store/別build inputにし、test keyをproduction modeで常に拒否する。
- current/next keyの重複有効期間を許すが、issuer/type/schema/purpose bindingは同じ厳格さで検査する。
- revocation listは専用offline revocation keyで署名し、対象鍵自身によるunrevokeを許さない。
- unknown、expired、not-yet-valid、revoked、wrong-purpose、wrong-issuer/audience/schema keyは全てfail-closed。
- remote `jku`/certificate downloadでtrustを追加しない。trust/recovery bundleは署名済みlocal importだけとする。

## Replay と rollback

signature verificationを繰り返すこと自体は許すが、state-changing importとfile bindingを区別する。

- policy、calibration、endpoint manifest、revocation、recovery、release approvalのimportは、issuer/type/scopeごとの最高 `sequence` と `id`/payload digestをlocal protected stateへ保持する。
- 同じ`id`・同じcanonical payload/signatureの再importはidempotent no-opとしてよい。同じ`id`でbytesが違うrecordは拒否する。
- 最高sequenceより小さいrecordはrollbackとして拒否する。同じsequenceで別id/payloadも拒否する。
- completion recordはinstallせず、ADR-0002のfinal filename、run ID、manifest digest、whole-workbook SHA-256 bindingで別file/runへのreplayを拒否する。同一pairのread-only再検証は許す。
- `id`、sequence、bindingの検査はsignature、type/schema/issuer/audience/time検証後に行い、未署名値でprotected stateを更新しない。

## Verification pipeline

順序を次に固定する。

1. admission byte/depth limit内でouter JSONをstrict parseし、duplicate/trailing tokenを拒否する。
2. envelope schemaを検証し、3つのbase64urlをstrict decodeする。
3. protected header/payloadをstrict I-JSON parseし、JCS再serializeとのbyte一致を確認する。
4. protected headerとcommon payload schemaを検証する。
5. `(iss, record_type, schema, kid, key_purpose)` からlocal trust entryを取得し、audience/status/algorithm/curveを検証する。
6. 64-byte P1363 ES256 signatureをOS/.NETの検証済みcryptographic primitiveで検証し、独自ECDSA演算を実装しない。失敗時はbodyを適用せず、原因の詳細を外部へ分岐表示しない。
7. time、revocation、sequence/replay/rollbackを検証する。
8. record固有body schemaとsemantic bindingを検証する。
9. 全て成功したimmutable verified DTOだけをconsumerへ返す。

errorは外部入力値をmessageへ埋め込まず、`SIGNATURE_INVALID`、`TYPE_MISMATCH`、`ISSUER_UNTRUSTED`、`AUDIENCE_MISMATCH`、`SCHEMA_UNSUPPORTED`、`KEY_UNTRUSTED`、`KEY_REVOKED`、`RECORD_EXPIRED`、`RECORD_NOT_YET_VALID`、`REPLAY_REJECTED`、`ROLLBACK_REJECTED`、`CANONICALIZATION_INVALID` 等のclosed codeで返す。

## ES256/JCS known-answer vector

次は合成データだけのtest vectorである。private keyの由来はRFC 7515 Appendix A.3の公開テスト鍵であり、production用途に使用しない。時刻semantic testでは `TimeProvider` を2026-09-01T00:00:00Zへ固定する。

Protected header canonical JSON:

`{"alg":"ES256","kid":"test-key-2026","typ":"sre+jws"}`

- UTF-8 SHA-256: `3B58EB4EF0FE76773FE45430FC6A440BCECD01F40082D4DF93096B2CD481EA7D`
- base64url: `eyJhbGciOiJFUzI1NiIsImtpZCI6InRlc3Qta2V5LTIwMjYiLCJ0eXAiOiJzcmUrandzIn0`

Payload canonical JSON:

`{"aud":"study-report-evaluator","body":{"final_filename":"synthetic_evaluated.xlsx","manifest_digest":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","run_id":"00000000-0000-4000-8000-000000000002","workbook_sha256":"BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"},"exp":"2030-01-01T00:00:00Z","iat":"2026-08-31T00:00:00Z","id":"00000000-0000-4000-8000-000000000001","iss":"test-issuer","nbf":"2026-08-31T00:00:00Z","record_type":"completion-record","schema":"urn:study-report-evaluator:record:completion:v1","sequence":"1"}`

- UTF-8 SHA-256: `FB1C81FBF9CF942BE71BC95A06424CB3C5518FE251E3677A7937961546F3F61E`
- base64url: `eyJhdWQiOiJzdHVkeS1yZXBvcnQtZXZhbHVhdG9yIiwiYm9keSI6eyJmaW5hbF9maWxlbmFtZSI6InN5bnRoZXRpY19ldmFsdWF0ZWQueGxzeCIsIm1hbmlmZXN0X2RpZ2VzdCI6IkFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUEiLCJydW5faWQiOiIwMDAwMDAwMC0wMDAwLTQwMDAtODAwMC0wMDAwMDAwMDAwMDIiLCJ3b3JrYm9va19zaGEyNTYiOiJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCQkJCIn0sImV4cCI6IjIwMzAtMDEtMDFUMDA6MDA6MDBaIiwiaWF0IjoiMjAyNi0wOC0zMVQwMDowMDowMFoiLCJpZCI6IjAwMDAwMDAwLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDAwMSIsImlzcyI6InRlc3QtaXNzdWVyIiwibmJmIjoiMjAyNi0wOC0zMVQwMDowMDowMFoiLCJyZWNvcmRfdHlwZSI6ImNvbXBsZXRpb24tcmVjb3JkIiwic2NoZW1hIjoidXJuOnN0dWR5LXJlcG9ydC1ldmFsdWF0b3I6cmVjb3JkOmNvbXBsZXRpb246djEiLCJzZXF1ZW5jZSI6IjEifQ`

Signing input SHA-256:

`3ED3975714AB2ADE2AC50ED2009C7ABDCE7A96A55013C04F6991573F095A7EDA`

Public JWK coordinates:

- `x`: `f83OJ3D2xF1Bg8vub9tLe1gHMzV76e8Tus9uPHvRVEU`
- `y`: `x_FEzRu9m36HLN_tue659LNpXW6pCyStikYjKIWI5a0`

P1363 signature base64url:

`XmWR4hhiHBrHMCJLDrEHvVRBc8-Bu3R3fcvCOR3igc4FKPaHGEQVQ8FtgXA6JM4PDyRS2Ggs2x0fMqYvL1DyUw`

このsignatureは64 bytesであり、2026-08-31にPowerShell 7.6.5上の.NET ECDsaとNode.js v24.19.0 `crypto.verify` の別実装で `true` を確認した。環境情報は再現性の記録であり、他OSでの成立を代替しない。

## Negative test matrix

各caseは他のfieldをvalid vectorと同じにして、単一理由で拒否する。

| Case | 期待 |
|---|---|
| payload/signatureを1 bit変更 | `SIGNATURE_INVALID` |
| `alg=none`、ES384、DER signature、長さ63/65 bytes | algorithm/signature format拒否 |
| wrong/missing `typ`、duplicate/unknown protected property | header拒否 |
| wrong `record_type` | `TYPE_MISMATCH` |
| wrong `schema` | `SCHEMA_UNSUPPORTED` |
| wrong `iss` / `aud` | `ISSUER_UNTRUSTED` / `AUDIENCE_MISMATCH` |
| unknown/revoked/wrong-purpose/expired key | key codeで拒否 |
| `now < nbf`、`now >= exp`、未来`iat` | time codeで拒否 |
| lower sequence、same sequence別payload | `ROLLBACK_REJECTED` |
| same id別payload、別recordへのcompletion binding | `REPLAY_REJECTED` |
| duplicate JSON property、noncanonical key順/number/string、padding付きbase64url、lone surrogate | parse/canonicalization拒否 |
| body tamper、body schema unknown/additional property | signatureまたはbody schema拒否 |

## G-12 test-only operational-policy fixtures

G-12のproduction bundleとは分離して、次のraw Flattened JWS JSONをtest-only fixtureとして固定した。

- `tests/fixtures/policy/test-only/valid.json`
- `tests/fixtures/policy/test-only/expired.json`
- `tests/fixtures/policy/test-only/wrong-signature.json`
- `tests/fixtures/policy/test-only/revoked-key.json`

全payloadは合成値だけを持つ`operational-policy`であり、実在機関、GitHub org、OAuth App、model、価格、地域、承認者、local launcher、学生data、token、secretを表さない。body shapeはA-03へ入力する署名・time・trust negative fixture用のtest contractであり、EXT-03 production policy body schemaの承認や実値を代替しない。body内の`schema=urn:study-report-evaluator:operational-policy-body:v1`はrecord固有body用の識別子であり、outer payloadの`schema=urn:study-report-evaluator:record:operational-policy:v1`やenvelope schema patternへ適用しない。signature/common payload検証後のpipeline step 8で別schemaとして扱う。

### Test trust entry

| 項目 | 値 |
|---|---|
| curve / algorithm | P-256 / ES256 / SHA-256 / 64-byte IEEE P1363 |
| issuer | `study-report-evaluator-test-fixtures` |
| audience | `study-report-evaluator-test` |
| record type / schema | `operational-policy` / `urn:study-report-evaluator:record:operational-policy:v1` |
| active kid | `g12-active-test-key-2026` |
| revoked kid | `g12-revoked-test-key-2026`（同じ公開鍵をtest trust storeでrevoked扱い） |
| public JWK `x` | `jTQ7exubZt-CP1Rr_w-pYHehG6gDgDw_GazGOnmpUp0` |
| public JWK `y` | `qW8r69ET00JKANkBa5B5k_xWKjUNBWaXa7UGFDZwNNY` |
| fixed verification time | `2026-09-01T00:00:00Z` |

P-256 private componentはfixture生成時にprocess memory内だけで作り、exportまたはfile書込みを行わず、4 signature生成後に`ECDsa` objectをdisposeした。repositoryとtemp outputへprivate `d`、PEM、PFX、secretを保存していない。このtest公開鍵をproduction trust storeへ登録してはならない。production build inputは`iss=study-report-evaluator-test-fixtures`、`aud=study-report-evaluator-test`、`kid`中の`test-key`を1件も含めず、F-08/F-10/P-08のpublish/supply-chain検査は`tests/fixtures/policy/test-only/`を配布物から除外する。path名だけで安全性を主張せず、production trust lookupに対応tupleが存在しないことをA-01/A-03でもnegative testする。

### Expected outcomes and artifact identity

| Fixture | Bytes | SHA-256 | Cryptographic verification | Expected final disposition |
|---|---:|---|---|---|
| `valid.json` | 1,194 | `BBB8F78A99C61908554C1861F92B0D94390FE9E8B9BA78C81523A9567B3854AC` | valid | accept in test trust profile |
| `expired.json` | 1,194 | `196D5A1118069D3856894C7C6D1973A8BCF8649C5B245890B2FDC654E1E050EA` | valid | `RECORD_EXPIRED` because `now == exp` |
| `wrong-signature.json` | 1,194 | `107FECE08021EE5BAF68A9480D4C67724C26B36949DF098C1C4407222AD3F387` | invalid; valid signatureから1 bitだけ変更 | `SIGNATURE_INVALID`。valid fixtureと同じpayload/id/sequenceを意図的に使い、pipeline step 6がstep 7のreplay/rollback state lookup・更新より先に失敗することを検査 |
| `revoked-key.json` | 1,195 | `EF92C31CC295C71FECD2D7967D3116D6CF0AC8905F4CA595A4271A7B35033854` | valid | `KEY_REVOKED` after trust lookup |

独立verifierは4 envelopeのroot schema、base64url、64-byte P1363、decoded header/payloadの再帰key sort canonical bytes、P-256 signature、fixed-time expiry、kid、1-bit差、禁止field不在を検査した。JCS再serializeのnumber処理一般を実装したとは主張せず、このfixture payloadがstring、Boolean、array、objectだけで構成される範囲のcanonical byte一致を確認した。

### G-12 status boundary

Test-only fixturesは完了したが、EXT-03のproduction issuer/trust root、OAuth client ID/scope、GitHub org、Business/Enterprise条件、許可model、保持/所在/費用、署名済みproduction policyは未提供である。したがってG-12全体は**BLOCKED**であり、test key/fixtureをproduction bundle、機関承認、実データmode許可として扱わない。

## Checkpoint encryption profile

checkpointは署名recordではなく、local authenticated-encryption envelopeとする。

### Cryptographic parameters

- algorithm: AES-256-GCM
- data key: cryptographic RNGで生成する32 bytes。**checkpoint generationごとに新規生成し、暗号化へ1回だけ使用する**
- nonce: cryptographic RNGで生成する12 bytes（96 bits）。同じdata keyを再利用しないため、key/nonce pairを再利用しない
- authentication tag: 16 bytes（128 bits）固定
- .NET API: tag sizeを明示する `AesGcm(key, 16)` 相当。obsoleteなtag-size未指定constructorを使わない
- plaintext/ciphertext length: 同一。admission limitを暗号化前に検査する

data keyはOS credential vaultに、random opaque `key_id` を名前として保存する。envelopeへkeyを入れず、file/plaintext fallbackを設けない。vaultのput-if-absentとkey ID collision検査に失敗したら新key IDを生成し、既存keyを上書きしない。

### Closed checkpoint envelope と AAD

checkpoint fileは次のexact top-level propertyだけを持つstrict JSON objectとする。

- `schema`: `urn:study-report-evaluator:checkpoint-envelope:v1`
- `metadata`: 下記のcleartext closed object
- `nonce`: 12 bytesをpaddingなしbase64url化した16文字
- `ciphertext`: 暗号化済みcheckpoint payloadのpaddingなしbase64url。空を許さない
- `tag`: 16 bytesをpaddingなしbase64url化した22文字

unknown/duplicate property、padding、invalid base64url、長さ不一致、trailing JSON tokenを復号前に拒否する。`metadata` はkey選択とrollback routingのためcleartextで保存するが、認証前は不信データであり、状態更新やplaintext選択の根拠にしない。

次のmetadataをclosed JSON objectとしてRFC 8785 canonical bytes化し、AADへ渡す。

- schema `urn:study-report-evaluator:checkpoint:v1`
- record type `checkpoint`
- checkpoint ID
- run ID
- input SHA-256
- ADR-0002のrun manifest digest。design/Prompt/rubric/model/app/SDK/CLI/ICUはこのdigestへ結合する
- generation（非負10進string）
- key ID

hash/digestはuppercase 64桁hex SHA-256、IDはlowercase canonical UUID、key IDはcase-sensitive opaque ASCIIとする。暗号化plaintext内にもschema、checkpoint ID、run ID、input SHA-256、manifest digest、generationを重複保持し、認証後にmetadataとexact一致することを検査する。

envelopeはmetadata、nonce、ciphertext、tagを保持する。metadata、nonce、ciphertext、tagの改変、wrong key、別run/input/designへのcopyは同じ `CHECKPOINT_AUTH_FAILED` 経路へ送る。AES-GCM decrypt/tag verificationが成功するまで出力bufferをparse・返却・logせず、AAD mismatchの詳細を外部へ分岐表示しない。

### Rollback guard と commit

OS vaultにrunごとの `checkpoint-head` を置き、`current`、`previous`、`pending` のgeneration/key ID/checkpoint IDを保持する。fileとvaultの完全な単一transactionを仮定せず、次のtwo-phase recoveryを使う。

1. 一回使用data keyを新しいkey IDでvaultへput-if-absentし、成功応答とread-back一致を確認する。vault missing/locked/read-only、write/read-back不一致なら後続stepへ進まず、memory keyをzeroして停止する。
2. vault headの`pending`へ新generation/key/checkpoint IDを予約する。
3. 暗号化envelopeをtempへwrite、durable flush、read-back、tag/AAD/body検証する。
4. checkpoint fileをT-10のatomic replace primitiveで置換する。
5. fileを再openしてpending tupleと一致することを確認し、vault headを`current`へ進め、旧currentを`previous`へ移す。
6. final head/fileを再検証し、不要になったolder key/tempだけをapp所有確認後に清掃する。

candidate fileの「完全」は、(a)strict envelope parse、(b)metadata tupleとvault head entryのexact一致、(c)対応keyの存在、(d)nonce/tag長、(e)AES-GCM tag/AAD検証、(f)plaintext schema、(g)plaintext内tuple/hashとmetadataの一致、の全成功を意味する。mtime、filename順、size、CRCをtie-breakerにしない。

起動時に`pending`があれば、main checkpoint、app-owned temp、current/previous/pending keyを照合する。pending tupleの完全なcandidateがちょうど1件なら、current candidateも有効であってもpendingを意図した次generationとしてstep 4または5から完了する。pending candidateが0件でcurrent candidateだけが完全なら、pending key/tempを清掃して旧currentを維持する。同じpending tupleに異なるciphertextの完全candidateが複数ある、またはどのcandidateも一意に認証できない場合は自動選択せずfail-closedにする。finalized headより古いgeneration、headと違うkey/checkpoint ID、same generation別ciphertextをrollback/tamperとして拒否する。

`previous` を使うatomic recoveryは、current/pendingが完全でないときに、利用者へgenerationと失われる進捗件数だけを提示して明示確認を得た場合に限る。previousの全認証・run/input/manifest一致後に新しいgenerationとして再commitし、headを単純に巻き戻さない。通常resume、期限経過、file欠落だけでpreviousへ自動fallbackしない。

vault key entry名はapp namespace、run ID、generation、key IDを含むclosed形式とし、headが参照できる `current`、`previous`、`pending` は各1件、最大3件に制限する。step 1後・step 2前のcrashで生じる未参照keyを回収するため、startup scavengerはapp namespace内のkeyを列挙し、全headとapp-owned temp/envelopeのmetadataから参照されないkeyだけを削除する。列挙・所有確認ができないvault backendは実データmodeで採用しない。既存keyや別app namespaceを削除しない。

vault missing/locked、key not found、tag failure、AAD mismatch、rollback、head破損では `CHECKPOINT_AUTH_FAILED` または内部の詳細closed errorを返し、実データmodeを停止する。復号途中bufferはcryptographic zero APIでbest effort消去するが、swap/core dumpからの消去保証とは称しない。実データmodeはEXT-09のOS/app/CLI dump・swap policy attestationも要求し、未承認環境では停止する。plaintext、key、nonce/tag値をlogへ出さない。

このtwo-phaseとvault atomicityはOSごとにG-17/T-10/R-01/R-02でfault injection/実測する。成立しないvault/filesystemではplaintext fallbackやrollback無視を行わず、実データmodeを非対応にする。

## 要求改版案

G-RB は少なくとも次を正本へ反映する。

1. signed recordはstrict flattened JWS JSON、ES256/P-256/SHA-256/P1363、protected header only、RFC 8785 canonical UTF-8 payloadを使う。
2. `typ/record_type/iss/aud/schema/id/iat/nbf/exp/sequence/body` を署名対象にし、trustをissuer/type/schema/key purpose/audienceへ拘束する。
3. duplicate/noncanonical/unknown algorithm/key/schema/type、time/replay/rollback、tamperをfail-closedにする。
4. checkpointはAES-256-GCM、generationごとの一回使用32-byte key、12-byte nonce、16-byte tag、run/input/design/typeを含むAADを使う。
5. vault headとのtwo-phase commitでrollbackを検出し、認証失敗、vault不在、atomicity未実証では実データmodeを停止する。

## 完了判定

strict JWS profile、唯一のcanonicalization、record/type/key用途拘束、time/replay/rollback、known-answer/negative vectors、AES-GCM key/nonce/tag/AAD/vault/commit/fail-closedを定義した。production issuer/trust root、実鍵運用、OS vault/filesystem evidenceはG-12/G-17の外部入力・実測待ちであり、存在を主張しない。
