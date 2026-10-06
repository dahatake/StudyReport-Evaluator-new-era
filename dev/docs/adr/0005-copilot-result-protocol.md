# ADR-0005: Copilot result tool と出力 protocol

> [!WARNING]
> **HISTORICAL / SUPERSEDED:** `submit_evaluation`、explicit token、旧ReportDefinitionを扱うrequirements v1.xの設計です。現行実装は既存CLI loginと`submit_quantification`を使用します。現行境界は[ADR-0011](0011-dynamic-quantification-excel-formulas.md)と[`architecture.md`](../architecture.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **改版承認済み・G-05 完了（DEC-07/08維持、DEC-09をruntime ReportDefinition方式で解決）** |
| 対象決定 | DEC-07、DEC-08、DEC-09 |
| 要求 | FR-036、FR-047〜052、FR-060〜064、FR-102、NFR-SEC-001〜003 |
| 決定 | `submit_evaluation.evidence`だけを学生由来tool argumentの例外とし、result toolだけpermission promptをbypassする。reason/evidence規則、missing flags、field/ID/item上限、target languageは利用者が起動後にReportDefinitionへ設定し、runごとにclosed schemaを生成する |
| 承認根拠 | ADR-0001 の承認証跡（実装前 decision gate は既定案） |
| 記録日 | 2026-08-31 |

## 現状と依存

要求正本 v1.1 は、FR-036で学生データをtool argumentへ転送しないとする一方、FR-048/FR-102で回答中の根拠抜粋を`submit_evaluation`のargumentとして受け、入力中の存在を検証するよう求めていた。また、FR-047の「全permission deny」と、GitHub Copilot SDK v1.0.11候補のcustom tool `SkipPermission`は同じ意味ではない。

本 ADR はこの2つの矛盾を要求変更案として精密化する。SDK型名・wire挙動は G-15 の source/live synthetic canaryで確認し、実測前に固定版の保証とはしない。

初版ADRはDEC-09のfield limitを、実装前に承認済みrubric/reason/evidence例からrelease定数へ固定しようとしていた。要求所有者の訂正により、この前提を撤回する。実際の教育内容、reason/evidence規則、missing flags、対象言語、構造上限は製品定義でも実装前外部入力でもなく、利用者がアプリ起動後にレポート種類ごとに設定する。

製品が固定するのは、媒体・SDK・資源保護の技術的hard ceilingと検証アルゴリズムだけである。ReportDefinitionの値はhard ceiling内で変更できるが、run開始時にimmutable snapshot/hashへ固定し、進行中runへ差し替えない。この改版でEXT-01測定blockerを撤去する。

## Capability boundary

### Client/session

- client は `CopilotClientMode.Empty` 候補を使う。
- explicit GitHub tokenを設定し、logged-in user、environment、Copilot CLI、`gh` credentialへfallbackしない。
- Empty modeが要求する per-session persistence locationと `AvailableTools` を明示し、app所有の隔離領域だけを使う。
- sessionごとに custom tool `submit_evaluation` を1件だけ定義・公開する。
- shell、filesystem、editor、Web、search、GitHub read/write、MCP、plugin、skill、memory、command、user elicitation、attachment、remote/cloud session、host Git、instruction discoveryを公開しない。
- working directory、file path、他学生データ、識別列をmodel/toolへ渡さない。
- telemetry、streaming、session store、embedding retrieval、experimentation等の無効化はA-12/C-12で明示し、Emptyという名称だけに依存しない。

production sessionの実効公開tool集合は source-qualified identityで厳密に `{custom:submit_evaluation}` だけとする。`AvailableTools`、`ExcludedTools`、Empty mode defaultsの組合せは候補SDKごとにG-15でwire/live確認し、1件以外が見える版、またはcaller設定でambient機能が復活する版を採用しない。process environmentはallowlistから新規構築し、`COPILOT_GITHUB_TOKEN`、`GH_TOKEN`、`GITHUB_TOKEN`、`COPILOT_CLI_PATH` 等のambient auth/path値を継承しない。

G-15 は、候補SDK sourceで Empty modeの既定値とoverride可能項目を列挙し、live synthetic canaryで実際の公開tool集合とprocess/network/session保存を確認する。期待と違えば版を採用しない。

### Tool handler

`submit_evaluation` handler は model出力を不信データとして一時捕捉し、schema/code validationへ渡すだけの process-local functionとする。次を禁止する。

- network、filesystem、shell、process、clipboard、browser、GitHub、MCP、database、workbook書込み
- token、path、環境変数、credential、他session、他evaluation unitへのアクセス
- argument本文、reason、evidence、通常assistant本文のlog/telemetry出力
- modelから渡されたIDをpath、command、URI、queryとして解釈すること

runnerは送信前に対象回答とexpected evaluator/item contractをimmutableなprocess-local contextとして保持し、handler closureへ直接渡す。handlerはfilesystemやworkbookを再読せず、このmemory snapshotだけでevidenceを照合する。source本文をtool argumentへ重複して含めない。

handlerは cancellationを受け取り、SDK callback metadataのsession ID/tool call IDをrunnerが保持するexpected session/call stateと不透明IDとして照合する。`evaluation_unit_id`、session ID、tool call IDはtool schemaに含めず、modelが追加propertyとして返した場合は拒否する。返却値は本文を含まない固定の成功/失敗protocol acknowledgementだけとする。tool handlerからWorkbook adapterを呼ばず、検証済みdomain resultだけをorchestratorへ返す。

## 学生由来 tool argument の限定例外

学生回答の逐語内容を入れられる property は、各rubric itemの `evidence` だけとする。

- `evidence` は対象evaluation unitの**同一の主対象回答**からの短い連続抜粋、または固定token `NO_EVIDENCE`。補助回答や学生Promptをscore判断へ送信していても、evidence sourceにはしない。
- 他設問、他学生、補助回答、学生Prompt、識別/管理列をevidence sourceにしない。
- modelが返した抜粋を、入力のUnicode scalar列に対するexact contiguous substringとしてcode検証する。比較用に暗黙のcase-fold/NFKC/空白変換をせず、表示用原文の存在を確認する。
- `NO_EVIDENCE`は引用として扱わず、system property `evidence_status=NOT_FOUND`を必須とする。実抜粋の場合は`evidence_status=PRESENT`とする。system evidence状態と、利用者がReportDefinitionで定義する教育上の`missing_flags`を混同しない。
- `missing_flags`はReportDefinitionのclosed enumからだけ選び、reportごとに空集合も許す。unknown/duplicate/同時最大数超過を拒否する。別reportのflag taxonomyを再利用しない。
- 一致しない引用は保存済み根拠として採用せず、`UNVERIFIED_EVIDENCE` として要確認にする。
- evidenceをsystem instruction、後続prompt、再試行指示、別session、別tool argumentへ再送しない。schema retryは元の最小payloadから新sessionを作り、旧tool argumentsをpromptへ貼り戻さない。
- handlerは外部I/Oを持たないため、悪意あるevidenceが能力を獲得しない。後段のExcel writerは内容にかかわらずstring cellとして保存する。

`reason`はmodelが生成する説明であり、学生原文を逐語引用する許可fieldではない。利用者はReportDefinitionでreason instructionと最大scalar数をレポート種類ごとに指定する。Prompt/schemaでevidenceとの分離を要求し、reasonに識別情報候補、または`reason_source_quote_rejection_threshold_scalars`以上の長さで主回答とexact contiguous一致する文字列がある場合はprotocol validationを失敗させる。これは「許容最大引用長」ではなくinclusiveな拒否閾値であり、`reason_max_scalars`以下でなければreport設定を拒否する。値が技術的に成立しない設定はrun開始前に拒否する。ただしPII detectorや重複検出を完全な防止と称さず、toolの無権限・無I/O境界を主要統制とする。

evidence照合は表示用主対象回答とtool argumentのUnicode scalar列を、変換なしでexact contiguous比較する。NFC/NFKC、case-fold、空白畳込み、grapheme同一視を適用しないのは意図的であり、modelが表示上同じでも別scalar表現を返した場合は引用を実在と推測せず `UNVERIFIED_EVIDENCE` とする。

この例外は `submit_evaluation.evidence` 以外へ拡張しない。添付、別tool、system message、通常assistant本文、log、telemetry、session名、usage metadataは例外対象外である。

## Permission semantics

候補SDK v1.0.11の公開資料では、custom toolの `SkipPermission=true` は、そのtoolをpermission callbackへ送らず実行させる機能である。したがって次を別々の不変条件とする。

1. `submit_evaluation` だけを custom toolとして公開し、当該toolだけ `SkipPermission=true` 候補を付ける。
2. `submit_evaluation` の呼出しでは permission callbackが0回であることをcontract testする。
3. `OnPermissionRequest` 相当のhandlerは必ず登録し、発生した全requestにRejectを返す。Approve、session/location/permanent approval、NoResult、user confirmationへ分岐しない。
4. malicious promptがshell/fs/Web/GitHub/MCP等を要求しても、公開tool 0件、実行0件、外部I/O 0件であることを確認する。SDK/runtimeが予期せずpermission requestを発生させた場合も全件Rejectされる。
5. 「callback全Reject」と「callbackを通らないresult tool 1件」を別metricとして記録する。callback回数0だけを全permission拒否の証拠にしない。

production構成では他toolが非公開のため、permission callbackが0回の正常系だけではReject実装を実証できない。G-15は隔離したnegative-control sessionで、外部I/Oを一切持たない合成permission probeをpermission対象として1件だけ公開し、同じhandlerが `Reject` を返しprobe body実行回数が0であることを確認する。production sessionではこのprobeを登録しない。unit testは `ApproveOnce`、session/location/permanent approval、`NoResult`、`UserNotAvailable` を返す分岐が存在しないことも検査する。

exact API nameは採用SDKのG-15 contract evidenceを正とする。要求正本の旧型名 `PermissionRequestResult.Denied` と候補SDK資料の `PermissionDecision.Reject` が異なる場合、compile/live確認した採用版の型名へ要求・実装文書を更新し、意味を変えない。

## Tool call lifecycle

1 evaluation attemptは新しいephemeral sessionを1件だけ使う。

1. runnerが期待する`evaluation_unit_id`、ReportDefinition snapshot/hash、evaluator ID、rubric item ID集合、範囲、reason/evidence規則、missing flag taxonomy、target language、field/structure limitをsession外のimmutable contextへ固定する。
2. modelへ送るのはL-04/L-06で許可・伏字確認した最小payloadだけとする。
3. sessionは `submit_evaluation` 以外のtoolを公開しない。
4. 最初のtool invocationだけを捕捉する。raw argumentsをlogへ出さない。
5. schema/code validationに成功した提出だけをdomain resultとして1件採用する。
6. 最初のcallが不正、2回目のcallが発生、通常assistant本文だけで終了、tool前後に追加提出が発生した場合は当該sessionをprotocol failureとして終了する。同session内で修正を求めない。
7. runnerはsessionをabort/idle待機し、`DeleteSessionAsync` 相当の明示削除とcanary確認が成功した後だけ、新しいschema retry/transport retry sessionを開始する。
8. validated result受領後のtool call、assistant本文、reasoning、stream deltaは採点値、理由、根拠、retry命令として解釈・保存しない。

SDKのterminal tool optionが存在しても、それだけに終了性を依存しない。G-15で挙動を確認し、runner側のcall count、session終了、明示削除を主要境界とする。

canary確認は、SDKの成功応答だけでなく、app専用 `BaseDirectory`/`SessionFs` 配下の対象session ID、session list API、CLI子process終了後のsession/cache/log/temp artifactを対象にする。本文canaryやsession IDの残存が1件でもあれば削除成功とみなさない。SDK管理外のOS全体を削除済みと推測せず、G-15で観測可能範囲と残余リスクを記録する。

## Result payload の論理形

C-01はReportDefinition snapshotとevaluatorごとにexact JSON Schemaを生成する。shapeは共通だが、ID const/enum、item cardinality、missing flag enum、string/maxItemsはsnapshotから閉じる。

- top level object
  - `evaluator_id`: expected IDとordinal一致するstring
  - `items`: expected rubric item数と完全一致するarray
- item object
  - `item_id`: expected集合の一意string
  - `score`: finite JSON number、当該itemのConfig min/max内
  - `confidence`: finite JSON number $0\le confidence\le1$。確率や校正済み正確度とは表示しない
  - `reason`: student原文の引用先ではないstring
  - `evidence`: source内に存在するstringまたは `NO_EVIDENCE`
  - `evidence_status`: system enum `PRESENT` / `NOT_FOUND`
  - `missing_flags`: 当該ReportDefinitionで利用者が定義したclosed enumの重複なしarray

top level/itemとも`additionalProperties=false`、全property required、`null`禁止とする。総合点、subtotal、pass/fail、review decision、file/path、student/user ID、model instruction、tool/command/URLを表すpropertyはschemaに持たせない。object/array深さ、item count、ID、number、string、serialized arguments全体をReportDefinition snapshotとtechnical hard ceilingの双方で制限する。

`AI_STATUS`、`VALIDATION_STATUS`、`REVIEW_STATUS`、`RUN_STATUS` はmodelが返すpropertyではなく、handler/validator/orchestratorがpayload外で生成するdomain stateである。したがって `additionalProperties=false` と矛盾しない。modelがこれらをargumentへ追加した場合はunknown propertyとして拒否する。

`confidence` はUI上の補助情報であり、採用点・総合点・合否へ接続しない。0または1を過度な確実性の証明と表示しない。

## Runtime ReportDefinition limit profile

### 利用者がレポート作成時に設定する値

- `max_rubric_items_per_evaluator`
- `evaluator_id_max_scalars`
- `item_id_max_scalars`
- `reason_max_scalars`
- `evidence_max_scalars`
- `max_missing_flags_per_item`
- `missing_flags`のID/表示名/条件
- `target_language`（BCP 47 tag）
- reason/evidence instructionと`reason_source_quote_rejection_threshold_scalars`

これらはReportDefinitionの設定であり、教育内容のrelease constant、signed operational policy、実装前corpus、教員/製品責任者approvalにしない。アプリはlocal launcher identity/roleを認証しない。設定保存と実行確認は操作同意であって本人・権限承認ではない。

### 製品が固定するtechnical hard ceiling

| Ceiling | 値 | 用途 |
|---|---:|---|
| 1 string property | 32,767 characters | Excel/FR-052媒体上限 |
| 1 request | 65,536 Unicode scalars以下、かつmodel contextの80%以下 | input/context admission |
| 1 tool arguments | 32,767 Unicode scalars以下 | result protocol admission |
| 1 run | 20,000 outbound attempts | resource/cost admission |
| concurrency | default 8、maximum 16 | isolation/resource admission。run内は共有Copilot CLI clientの上でsessionをattemptごとに分離し、rate limit時はadaptive limiterが有効並列度を下げる |

hard ceilingだけはruntime設定で増やせない。SDK/CLIの実測上限がこれより小さい場合はG-15で小さい方を採用し、要求を再評価する。教育用途の設定値を製品が自動縮小しない。

### 保存・検証手順

1. 利用者がReportDefinitionを作成・編集する。reason/evidence本文の代表corpusは要求しない。
2. syntax、正数、actual item/ID、BCP 47 tag、flag ID uniquenessを検査する。暗黙defaultを補わない。
3. generated evaluator schemaごとに、configured最大長・最大item数・最大flag数・最長flag IDを使う最大instanceをproduction serializerで実際にserializeする。手入力overhead式を正本にしない。
4. serialized Unicode scalar/UTF-16/UTF-8 count、Excel列/式/cell、request/context、run attempt worst caseを測定し、全hard ceiling内だけ保存可能・実行可能にする。保存可能だが実行不能なprofileは明示`INVALID_FOR_RUN`とし、送信しない。
5. run開始時にcanonical snapshot/hashを作り、generated schema cache、checkpoint、manifestへ結合する。設定変更は新snapshot/new run IDを要求し、進行中runや旧cacheへ反映しない。
6. result超過は切り詰めず`SCHEMA_INVALID`、configuration由来の不成立は送信前`REPORT_DEFINITION_INVALID`とする。

## Failure classification

| Condition | Result |
|---|---|
| tool callなし、通常本文のみ | `TOOL_PROTOCOL_VIOLATION` |
| 複数tool call/複数提出 | `TOOL_PROTOCOL_VIOLATION` |
| unknown/missing/duplicate ID/property/item | `SCHEMA_INVALID` |
| null、wrong type、NaN/Infinity、score範囲外、field/total過長 | `SCHEMA_INVALID`（0点へ変換しない） |
| evidence不一致 | `VALIDATION_STATUS=UNVERIFIED_EVIDENCE`、要確認。採用点は空欄 |
| permission request | Reject。result tool由来ならcontract violationとして版採用を停止 |
| cleanup失敗 | `RUN_STATUS=SESSION_CLEANUP_FAILED`、新規送信停止 |

schema retryとtransport retryは別budgetとして理由を分類するが、どちらもrun全体のoutbound attempt budgetを消費する。内容filter、auth、policy、permission、resource limitは自動反復しない。

`UNVERIFIED_EVIDENCE` はschema retryにせずrunを継続し、R-09のreview queueへ送る。`ACCEPT_AI` は許可せず、教員は保留、却下、escalate、または有効な教員値とcommentを伴うoverrideを選べる。override時も元のAI候補/evidenceと `ERROR_CODE=UNVERIFIED_EVIDENCE` を監査に保持する。最終 `VALIDATION_STATUS` がoverride値について `VALID` になった場合だけ採用点を出す。このbranchはADR-0004のvalidation formulaへ反映する。

cleanup失敗では完了済みdomain resultと暗号化checkpointを保持し、`RUN_STATUS=SESSION_CLEANUP_FAILED` のinterlock中はschema/transport retryを含む新規sessionを開始しない。C-07の明示再削除またはC-08 startup scavengerが対象sessionの不存在とcanary 0件を確認した後だけ、利用者操作でlockを解除する。期限経過やclient disposeだけで成功へ変更しない。

## 要求改版案

G-RB は数値未確定部分を除き、少なくとも次を正本へ反映する。

1. FR-036: 学生由来のtool argument禁止の限定例外は、外部I/Oを持たない唯一の `submit_evaluation` の `evidence`だけとする。
2. FR-047: result toolだけpermission prompt対象外とし、発生したpermission requestは全Rejectする。公開tool数、result tool callback回数、他request拒否実績を別に試験する。
3. FR-048/049: 通常assistant本文を採用せず、1 session/attemptにつき最初の1提出だけを検証し、複数提出をprotocol violationにする。
4. reasonは逐語引用の許可fieldではなく、evidenceだけをsource substring照合する。
5. field/item/ID/flag/languageはReportDefinitionへ移し、C-01はsnapshotごとのclosed schema、C-02/C-03はsnapshot validation、T-06はtechnical hard ceilingと最大instance preflightを実装する。

## 完了判定

- DEC-07: **確定**。例外範囲を `submit_evaluation.evidence` に限定した。
- DEC-08: **確定**。result tool bypassと、発生permission全Rejectを分離した。
- DEC-09: **解決**。固定release定数案を撤回し、利用者設定ReportDefinition + immutable technical hard ceiling + production serializer preflightへ変更した。
- G-05: **完了**。DEC-07/08の能力境界を維持し、DEC-09の誤った外部corpus/approval依存を除去した。
