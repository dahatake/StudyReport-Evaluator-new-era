# ADR-0007: 実行時ネットワーク境界と endpoint manifest

> [!WARNING]
> **HISTORICAL / SUPERSEDED:** signed endpoint manifestを含む旧requirements v1.xの設計です。現行実装のCopilot boundaryを表す文書ではありません。現行境界は[`architecture.md`](../architecture.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **承認済み・G-07完了・要求v1.2へG-RB反映済み（G-12/G-15実manifest待ち）** |
| 要求 | AC-003、FR-042、FR-044、FR-045、FR-064、NFR-SEC-006/007/009 |
| 決定 | 公式sourceとlive canaryから目的別最小集合を作り、ADR-0006で署名したrelease endpoint manifestだけを許可する |
| 承認根拠 | ADR-0001 の承認証跡（実装前 decision gate は既定案） |
| 記録日 | 2026-08-31 |

## Source provenance

次の公開sourceを個別に取得した。本文copyはリポジトリへ保存せず、exact HTTPS response bytesのidentityだけを記録する。`retrieved_at` は各response bodyの受信完了直後のUTC時刻であり、2 sourceの同時snapshotを装わない。

| Source | Final URI | `retrieved_at` | Bytes | SHA-256 | Response metadata |
|---|---|---|---:|---|---|
| GitHub Copilot allowlist reference | `https://docs.github.com/en/copilot/reference/copilot-allowlist-reference` | `2026-08-31T05:45:13Z` | 741,357 | `CBF4213E8A89167328370C938ABAA9DFB9A3E9837711DABAE80A66E4B8557717` | HTTP 200、`Cache-Control: public, max-age=60`、ETag/Last-Modifiedなし |
| GitHub Meta API | `https://api.github.com/meta` | `2026-08-31T05:45:14Z` | 158,100 | `6FA92491880C87BB373B3E44CA24D91764A76B80334A93B622651582CE36DC2C` | HTTP 200、weak ETagは同じlowercase SHA-256、`Cache-Control: public, max-age=60, s-maxage=60`、User-Agent明示 |

Meta APIの当該応答で抽出した集合は次のとおりだった。

- `domains.website`: `*.github.com`, `*.github.dev`, `*.github.io`, `*.githubassets.com`, `*.githubusercontent.com`
- `domains.copilot`: `*.github.com`, `*.githubusercontent.com`, `default.exp-tas.com`, `*.githubcopilot.com`

これは各取得時点の事実であり、release manifestそのものではない。GitHubはMeta APIを常に直接queryして最新値を得るよう案内し、同APIのdomain一覧は包括的とは保証していない。raw HTML hashはnavigation等の非endpoint変更でも変わり得るため、hash差分だけでendpoint追加・削除を推測しない。各RCでこの2 sourceを各1回再取得し、公式表の意味差分、Meta API集合、採用SDK/CLIのprocess trafficを再確認する。sourceごとの時刻を保持し、古い方の時刻を新しい方へ丸めない。

## 境界の範囲

本manifestは `scope=installed-app-runtime` のインストール済みappの**実行時**だけを対象とする。初回setup、署名済みartifact取得、明示更新、CI/package restoreは別境界であり、runtime manifestへNuGet/CDN/release download先を混ぜない。schemaにsetup phaseを追加して混在を許さず、setupは別process・別証跡として扱う。appに自動更新通信を実装しない。

runtime外部通信の候補purposeは次だけである。

1. GitHub OAuth Device Flow code取得。
2. GitHub OAuth token poll/refresh。
3. `/user` identity確認。
4. 対象org membership確認。
5. Copilot model catalog/control plane。
6. 利用者が明示開始したCopilot inference。

Excel、Open XML、類似度、review、checkpoint、log、diagnostics、価格scrape、font/image、Web検索、MCP、telemetry、crash report、experimentation、voice model、public-code detection、usage-report download、update checkの通信は許可しない。価格情報が承認済みAPIから得られない場合は見積を非表示にし、外部scraperを追加しない。

## Manifest形式と署名

on-disk JWS envelopeはADR-0006、decoded payloadは `eng/schemas/endpoint-manifest.schema.json` に従う。

- `record_type` は `endpoint-manifest`。
- `schema` は `urn:study-report-evaluator:record:endpoint-manifest:v1`。
- `iss/aud/kid/key_purpose` をproduction trust storeへ拘束する。
- bodyはruntime scope、source snapshot、対象service origin/plan、対象RID、SDK package/CLI artifactのexact版・hash、G-15 evidence bundle、検証済みmodel ID、endpoint entry、明示拒否host、feature flags、署名済みapproval record IDを持つ。
- manifestの `exp` を過ぎた場合、新規OAuth/Copilot通信を停止する。既存結果のreview、類似度、Excel再出力は継続できる。

固定manifestは手書きの恒久正本にせず、各RCの公式sourceとG-15 captureから生成し、情報管理/セキュリティ責任者が差分を承認して署名する。production private keyはリポジトリへ置かない。

1 manifestは1つのRID、1つのSDK package bytes、1つのCLI artifact bytes、1つのservice origin/plan、G-15で実測したmodel ID集合だけを対象にする。別RID、別CLI build、別model集合へ流用せず、それぞれ別manifest/evidenceを発行する。`approval_record_ids` はcase-sensitiveな人名自己申告ではなく、別trust purposeで検証済みの署名approval recordのlowercase UUIDである。JWS signerはartifact publisherであり、列挙されたapprover本人であるとはみなさない。

## Endpoint entry

entryは `(process, purpose, scheme, host_pattern, port, methods, path_rules, query_policy, request_data_classes)` の積として評価する。hostだけのallowlistにしない。

- `scheme` は `https`、portは443だけ。
- hostはlowercase ASCII/Punycode FQDNのexact値、またはleft-most labelだけが`*`のwildcard。wildcardはapexを含まない。
- IP literal、userinfo、fragment、別port、unknown schemeを拒否する。
- pathはabsolute pathとしてpercent-encodingをstrict parseし、empty segment、dot segment、encoded dot/separator、backslash、invalid percent、`%25`による二重decode差、query/fragment混入を拒否する。`exact` は完全一致、`segment-prefix` は末尾`/`を必須としsegment境界でだけ一致する。
- queryは `none` またはpurposeごとのclosed parameter builderだけが作り、token、device code、学生本文をURL/logへ含めない。OAuth secret類は必要なPOST body/headerに限定する。
- `request_data_classes` はそのentryで送信し得るdata classの上限であり、`student-content` を含むentryだけ `sends_student_data=true` にできる。これは送信許可そのものではなく、SafePayloadBuilder、redaction preview、実行確認を別途通す。
- methodはentryに列挙したGET/POSTだけ。v1は全purposeでredirectを禁止する。G-15でredirectが必須と判明した場合は、credential strippingとhopごとの再検証を定義した新schema/ADRへ明示改版し、v1を緩和しない。
- TLS certificate/hostname validationを無効化せず、custom trustはOS管理者が正しいrootを導入する。証明書pinningや固定IPを本manifestの代替にしない。

GitHub.comを対象とするapp processの既知候補は次だが、最終entryはEXT-03/G-15で実証して署名する。GHE.comへこの表を転用しない。

| Purpose | Candidate endpoint | Method | Student data |
|---|---|---|---|
| device code | `https://github.com/login/device/code` | POST | 送らない |
| token poll/refresh | `https://github.com/login/oauth/access_token` | POST | 送らない |
| identity | `https://api.github.com/user` | GET | 送らない |
| org membership | `https://api.github.com/user/memberships/orgs/{approved-org}` | GET | 送らない |
| browser launch target | `https://github.com/login/device` | GET | 送らない |

`{approved-org}` はmanifestのwildcard文字列ではなく、署名policyのorg slugを1 segmentとして厳密encodeし、展開後のexact pathをmanifestへ格納する。path prefixだけで別org照会を許可しない。`process=browser` はこのverification URLをOSへ渡す許可だけを表し、browser内の後続通信をappのallowlist証跡へ含めない。

Copilot CLIのmodel/inference/control-plane endpointは候補を先に断定しない。公式referenceには `api.github.com/copilot_internal/*`、`copilot-proxy.githubusercontent.com`、plan別 `*.business.githubcopilot.com` / `*.enterprise.githubcopilot.com` 等が記載されるが、選択plan、subscription-based routing、GHE.com data residencyで必要集合が異なる。EXT-03 policyとG-15 process captureで実際に必要なpurposeだけを残し、Individual plan endpointを実データmanifestへ入れない。

## 公式allowlistからの除外

公式referenceは複数client/feature向けの必要先を含むため、掲載されているだけで本appへ許可しない。少なくとも次を明示denyし、entryとdenyが衝突したmanifestを拒否する。

| Host/pattern | 理由 |
|---|---|
| `collector.github.com` | analytics telemetry |
| `copilot-telemetry.githubusercontent.com` | Copilot client telemetry |
| `default.exp-tas.com` | client experimentation |
| `origin-tracker.githubusercontent.com` | public-code detectionを使用しない |
| `copilot-reports.github.com` | usage report downloadを使用しない |
| `*.b01.azurefd.net` | usage report fallbackを含むAzure Front Doorをruntimeで使用しない |
| `*.blob.core.windows.net` | usage report/voice model fallbackを含むAzure Blobをruntimeで使用しない |
| `ai.azure.com`、`*.azureml.ms` | voice featureを使用しない |

GitHub wildcard sourceがdeny hostを包含しても、denyを優先する。allow entryとdeny patternが1 hostでも交差するmanifestは設定矛盾として丸ごと拒否する。`*.githubusercontent.com`、`*.github.com`、plan非限定の`*.githubcopilot.com`をproduction entryへ採用せず、G-15で観測したexact hostまたは公式に複数subdomainが必須と明記されたplan-specific wildcardへ狭める。必要性を実証できないhostは削除する。

## Schema後の必須semantic validation

JSON Schemaは配列間参照、host patternの集合交差、実通信との対応を表現できない。schema合格をmanifest受理とみなさず、ADR-0006 verification pipeline step 8で次を全件検査する。

1. `official_sources[].id`、`contract_evidence[].id`、`entries[].id`、各entryのeffective tupleがそれぞれ一意である。methods/path/data classは順序を正規化してduplicate tupleを検出する。
2. 全`official_source_refs`と`contract_evidence_refs`がそれぞれ同じbody内の対応型IDを参照し、各entryに公式reference 1件以上と、同じRID/SDK package ID/version/hash、CLI version/hash、plan/origin/model集合のG-15 evidence 1件以上がある。
3. official source referenceは取得済みbodyのsemantic extractionへ実際に存在し、live evidenceはhash対象bundle内の観測entry/process/purposeへ一致する。自己申告の参照IDだけでは通さない。
4. `process=app` はOAuth/identity/org、`copilot-cli` はmodel/inference/control/session-delete、`browser` はverification URIだけとするschema制約に加え、実際のrequest process identityをenforcerが照合する。
5. allow/deny host patternの言語が交差しない。denyを先に評価し、unknown hostはdefault denyする。required deny closureが欠けるmanifestを拒否する。
6. declared plan/originと異なるplan suffix、Individual plan、別GHE slug、別RID/CLI hash/modelのentry/evidenceを拒否する。official source全体にあるという理由だけで許可しない。
7. entry IDはrelease内でstableなlowercase identifierとし、capture/負試験/error evidenceはこのIDを参照する。ID自体を認可情報やtuple hashとはみなさない。
8. `sends_student_data` と`request_data_classes`の整合、purposeごとのmethod/path/query/data class closed profile、path canonicalizationを検査する。
9. `approval_record_ids`の全recordを別trust purposeで検証し、対象release/RID/plan/origin/manifest digest、承認役割、期限が一致することを確認する。

どれか1件でも失敗した場合はverified DTOを返さず `ENDPOINT_MANIFEST_INVALID` とする。schemaだけ、署名だけ、deny優先による部分的な救済で受理しない。

## Process別 enforcement

### App process

- OAuth/REST用の注入可能な `HttpMessageHandler` の送信直前にmanifestを評価する。
- proxy CONNECT先、redirect先、SNI/Hostの不一致を拒否する。
- response本文・headerにtoken/PIIをlogしない。
- manifest invalid/expired/rollback時はhandlerを構築せず `POLICY_BLOCKED` とする。

### Copilot CLI child process

SDK設定だけでOS egressを完全強制できるとは主張しない。次を組み合わせる。

- sanitised environment、Empty mode、telemetry/remote/experimentation無効、tool capability最小化。`NODE_TLS_REJECT_UNAUTHORIZED`、`NODE_EXTRA_CA_CERTS`、SSL bypass系変数を継承せずOS trust storeだけを使う。
- app専用proxy/firewall policyまたは機関のprocess-aware egress control。
- app+CLI process treeのpacket/proxy capture。
- G-15でmanifest外通信0、telemetry endpoint接続試行0を確認する。

CLIがmanifest外endpointを必須とする、deny endpointへ接続を試みる、またはprocess別制御を実証できない場合、そのSDK/CLI版を採用しない。DNS/CDNの動的IPをリポジトリへ固定せず、hostname/TLSと組織network policyで制御する。Copilot CLIが優先順に読む`HTTPS_PROXY`等は、EXT-03で承認されたHTTP proxy URLだけをcredentialなしで子processへ明示設定する。ambient proxy変数は継承せず、proxy credentialはOS/機関管理の認証経路を使い、URL・引数・logへ含めない。

### Browser

Device Flowのverification URIは利用者へ表示し、OS既定browserを開く場合はGitHubから受領した値を盲信せず、exact `https://github.com/login/device` だけを許す。browser全体の通信をappのAC-003証跡と混同せず、app/CLI process captureと分離する。

## Loading とfail-closed

manifestはreleaseに同梱されたlocal signed artifactとして読み、runtimeでdownloadしない。release metadataが指定したexpected manifest digestとlocal bytesを照合し、ADR-0006の全pipelineを通したimmutable verified DTOだけをmemoryへ公開する。embedded default、sample、test key、last-known-goodという名前の未検証copyへfallbackしない。

明示更新はcandidateを別fileで全検証し、sequence protected stateとdurable flush後にatomic replaceする。candidateが失敗した場合だけ、現在activeなmanifestがvalidかつ未期限ならそれを変更せず継続する。active fileの欠落・改変・期限切れ、protected state不一致ではnetwork機能を停止し、同梱された古いmanifestや低sequenceへ戻らない。起動時だけでなく、各request dispatch直前とCLI session開始/継続前にtime/RID/binary hash/model bindingを再確認する。

## Expiry、更新、rollback

- manifestはcommon payloadの `iat/nbf/exp/sequence/id` を必須とする。期限なしmanifestを許可しない。
- 各RC、SDK/CLI/model/plan/service origin変更、公式source hash/意味差分、allowlist/security advisory変更時に再生成・再承認する。
- runtimeで公式ページを自動取得してmanifestを書き換えない。更新は明示的な署名済みrelease artifactだけで行う。
- ADR-0006のhighest-sequence/id/digest規則でrollback、same-sequence別payload、revoked signerを拒否する。
- 緊急縮小は、より高いsequenceの署名manifestまたは専用revocation recordで行う。古いmanifestへ戻して復旧しない。
- 更新失敗時は最後のvalidかつ未期限manifestを継続できるが、期限切れ後のgrace/overrideは設けない。

全entryはroot `nbf/exp`を共有し、entry単位の延長を設けない。`now >= exp` をrequest送信直前に検出したら、そのrequest、retry、refresh、次のstream operationを開始せず、active CLI sessionをcancelして削除へ進む。既にOSへdispatch済みのbytesを取り消せるとは主張しない。期限後も新規networkを伴わない既存結果のreview、類似度、Excel再出力、manifest診断表示は許可する。

## Validation と evidence

schema/signatureだけでは通信境界の成立を証明しない。各RCで次を行う。

1. source response identityとsemantic endpoint差分を記録する。
2. manifest entryが公式sourceとG-15 observed control-planeの両方へtraceでき、RID/SDK/CLI/plan/origin/model bindingが一致することを検査する。
3. synthetic canaryでOAuth、identity/org、model list、1 inference、session deleteを実行する。
4. app+CLI process tree captureをentryと照合し、unknown egress、telemetry、experimentation、voice/public-code/update通信が0件であることを確認する。
5. manifestから各required entryを1件ずつ削除した負試験で、そのpurposeだけがfail-closedになることを確認する。
6. duplicate ID/effective tuple、dangling source ref、allow/deny交差、required deny欠落、wildcard過大、HTTP、port違い、redirect、encoded path、expired/rollback/wrong signer/RID/CLI/model/plan/orgの各負試験を行う。

通信payload本文を証跡へ保存せず、timestamp、process、purpose、host、port、path template、status class、bytes count、manifest entry IDだけを記録する。token、device code、query、学生本文を保存しない。

## 要求改版案

G-RB は少なくとも次を正本へ反映する。

1. runtime許可先は公式allowlistの丸写しではなく、署名release manifestのpurpose/process/host/path/method最小集合とする。
2. telemetry、experimentation、voice、public-code detection、usage report、updateは公式掲載先でも明示denyする。
3. manifestはsourceごとの取得時点/hash、RID別SDK/CLI artifact hash、model/plan/origin、G-15 evidence、expiry、sequence、署名approval record参照を持ち、RCごとに差分再承認する。
4. manifest invalid/expired/rollback、deny衝突、unknown egressでは新規OAuth/Copilot通信を停止し、offline機能は維持する。
5. TLS bypass、unchecked redirect、runtime自動allowlist更新、IP固定を設けない。

## 完了判定

公式sourceごとの取得時点/identity、用途別runtime境界、RID/model/evidenceを拘束したsigned manifest、schema後semantic validation、loading、expiry/update/rollback、telemetry等の明示除外、process別 enforcementとRC evidenceを定義した。production endpoint集合・署名・組織planはG-12/G-15で実証するまで未確定であり、存在を主張しない。
