# ADR-0003: Open XML 変更許可リストと `Eval` 投影契約

> [!WARNING]
> **HISTORICAL / SUPERSEDED:** `Eval` / `Evaluation_Config` / `Evaluation_Run`を用いる旧requirements v1.xの設計です。現行は`Quantification_Config` / `Quantification_Results` / `Quantification_Run`を使用します。現行契約は[ADR-0011](0011-dynamic-quantification-excel-formulas.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **承認済み・G-03完了・要求v1.2へG-RB反映済み** |
| 対象決定 | DEC-03、DEC-04 |
| 要求 | FR-003、FR-007〜014、FR-016、FR-087〜090、AC-002、AC-007 |
| 決定 | 値と必要最小限の静的表示だけを新規 sheet へ投影し、変更対象を閉じた OPC allowlist に限定する |
| 承認根拠 | ADR-0001 の承認証跡（実装前 decision gate は既定案） |
| 記録日 | 2026-08-31 |

## 目的

入力 package の全既存 part と sheet を保持しつつ、教員が選択した source sheet から安全な `Eval` と、`Evaluation_Config`、`Evaluation_Run` を追加する。入力由来の式、外部関係、実行能力、不可視状態、任意 XML を新規 sheet へ継承せず、変更許可外の package graph と展開 payload bytes を不変にする。

本 ADR の「不変」は raw ZIP の entry 圧縮 bytes、entry 順序、timestamp、compression method の一致を意味しない。比較単位は、各 OPC part の**展開後 payload SHA-256**、正規化前の package URI、content type、および relationship tuple である。これは DEC-04 の既定案であり、raw ZIP bytes や XML 意味比較へ読み替えない。

## 入力を直接編集しない

1. 入力は read-only handle または同じ handle から作成した不変 snapshot だけで読む。
2. 作業 package は入力全 bytes を新しい `.partial.xlsx` へコピーして作る。
3. Open XML 書込みは作業 package にだけ行う。
4. success、failure、cancel、crash の全経路で入力 path へ保存、rename、replace、delete を行わない。
5. final path と入力 file identity が同一なら開始前に拒否する。

## Package mutation allowlist

allowlist は既定拒否である。次の既存 part、新規 part、relationship だけを変更または追加できる。URI は package 内で実在を確認して割り当て、下表の例示番号を固定値として使わない。

### 変更可能な既存 package item / part

`[Content_Types].xml` は OPC part ではなく package-level content types stream である。本 ADR では、ZIP item、relationship part、通常 part をまとめて package item と呼ぶ場合があるが、fingerprint と API 上の種別は混同しない。

| Part URI | 許可する変更 | 禁止する変更 |
|---|---|---|
| `[Content_Types].xml` | 新規 app-owned worksheet part、および既存 styles part がない場合の新規 styles partに必要な `Override` を追加 | 既存 `Default`/`Override` の削除、置換、重複、content type 変更、並べ替えによる意味変更 |
| `/xl/workbook.xml` | `<sheets>` に app-owned sheet 3件を追加。既存 `<calcPr>` の `calcMode="auto"`、`fullCalcOnLoad="1"`、`forceFullCalc="1"`だけを設定し、なければ schema-valid 位置へ追加 | 既存 sheet/name/id/relationship、workbook view、defined name、external reference、protection、date system、unknown extension の削除・変更。既存 `calcId` 等、許可外属性の変更 |
| `/xl/_rels/workbook.xml.rels` | app-owned worksheet 3件への standard internal worksheet relationship を追加。styles part が存在しない場合だけ standard styles relationship を1件追加 | 既存 relationship の `Id`、`Type`、`Target`、`TargetMode` の変更・削除・重複。external relationship の追加 |
| `/xl/styles.xml` | app-owned header、date/time、number、wrap、status、warning、説明、conditional-format 用 style/dxf を末尾へ追加し、直接親 collection の count を実数へ更新。part がなければ最小有効 styles partを新規作成 | 既存 style record の削除、置換、順序変更。既存 style index の意味変更。theme/font URI、external relationship、未知 extension の変更 |

`docProps/core.xml`、`docProps/app.xml`、`xl/sharedStrings.xml`、`xl/theme/*`、`xl/calcChain.xml` は更新しない。特に新規文字列は inline string を使い、既存 shared string table を書き換えない。既存 calc chain は削除・再生成せず、`calcMode="auto"` で通常の自動計算を指定した上で、app-owned formula が既存 calc chain にないため `fullCalcOnLoad="1"` と `forceFullCalc="1"` で全再計算を要求する。この組合せが採用 Office/LibreOffice で成立するかは G-16/E-07 の実測前に保証しない。成立しなければ G-16 を FAIL とし、既存 calc chain の削除や source formula の変更を無断 fallback にせず、要求と allowlist を再審査する。

既存 styles part の安全な追記ができない、または既存 collection/count が不正な入力は修復せず拒否する。presentation のために workbook 全体を高水準ライブラリで load/save しない。

### 追加可能な新規 part

| 論理名 | URI 規則 | Exact content type | Relationship source |
|---|---|---|---|
| `Eval` | `/xl/worksheets/sheet{N}.xml` の未使用 URI | `application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml` | `/xl/workbook.xml` |
| `Evaluation_Config` | `/xl/worksheets/sheet{N+1}.xml` 相当の未使用 URI | `application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml` | `/xl/workbook.xml` |
| `Evaluation_Run` | `/xl/worksheets/sheet{N+2}.xml` 相当の未使用 URI | `application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml` | `/xl/workbook.xml` |
| styles（入力に存在しない場合だけ） | `/xl/styles.xml` | `application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml` | `/xl/workbook.xml` |

`N` は既存 worksheet URI の安全に解析できた最大数の次から決定論的に割り当て、穴を再利用しない。part identity の比較は保存された URI の exact 表現で行う。一方、安全検査では、exact 比較に加えて ASCII case-fold 後または妥当な percent triplet を1回 decode した後にも別 URI と衝突しないことを要求する。これは OPC の同一性規則を case-insensitive と称するものではなく、parser/filesystem 間の解釈差を持つ入力を fail-closed にする追加統制である。新規 URI は小文字 ASCII と未エンコード数字だけで生成する。URI traversal、duplicate、case-only collision、percent-encoding ambiguity は W-06 で拒否する。

新規 worksheet part の relationship part、table、drawing、image、chart、comment、threaded comment、VML、hyperlink、external link、custom XML、embedded object、query、connection、macro、digital signature は作成しない。したがって `/xl/worksheets/_rels/sheet{N}.xml.rels` も作らない。P1 の named range を実装する場合は、G-09 の disposition と P1-07 の承認済み契約で `/xl/workbook.xml` の追加 XPath を別途拡張する。

### Namespace と relationship type family

入力 workbook の SpreadsheetML namespace と officeDocument relationship family を検査し、同じ family の要素・relationship を追加する。既定の Transitional family は次を使う。

- worksheet: `http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet`
- styles: `http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles`
- SpreadsheetML main: `http://schemas.openxmlformats.org/spreadsheetml/2006/main`

Strict family を受理する場合は、入力が一貫して Strict であることを確認し、対応する `http://purl.oclc.org/ooxml/...` namespace/type を package 全体で混在させずに使う。family が混在、不明、または SDK が round-trip で保持できない入力は修復せず拒否する。relationship の `TargetMode` は internal worksheet/styles では省略し、Target は workbook relationship part からの相対 URI とする。「standard relationship」という説明名だけで type を選ばない。

### Relationship tuple

各 relationship は次の tuple として fingerprint する。

`(source_part_uri, relationship_id, relationship_type, target_text, target_mode)`

- `target_text` は package に保存された文字列を保持し、比較時に別 URI へ勝手に正規化しない。
- internal target は traversal や package 外解決を事前拒否する。
- app-owned relationship の `Id` は既存 ID と ordinal 一致せず、再実行で同じ入力・同じ plan なら決定論的に選ぶ。
- 変更許可外 source の relationship tuple 集合は追加も削除もしない。
- allowlist source でも、既存 tuple は完全保持し、app-owned tuple の追加だけを許す。

## XML element mutation allowlist

変更後は XML semantic diff を行い、次の XPath 相当以外の差分を拒否する。prefix は比較対象にせず namespace URI と local name で解決するが、未知要素・属性は保存する。

| Part | 許可 XPath 相当 |
|---|---|
| `[Content_Types].xml` | `/Types/Override[@PartName={new-app-part}]` の追加 |
| `xl/workbook.xml` | `/workbook/sheets/sheet[@r:id={new-app-rel}]` 3件の追加 |
| `xl/workbook.xml` | `/workbook/calcPr/@calcMode`、`@fullCalcOnLoad`、`@forceFullCalc` の追加・置換、および `calcPr` 自体がない場合の追加 |
| `xl/_rels/workbook.xml.rels` | `/Relationships/Relationship[@Id={new-app-rel}]` の追加 |
| `xl/styles.xml` | app-owned末尾 record の追加と、その直接親の `@count` 更新。新規 part の場合は最小 schema 全体 |

XPath 中の `@r:id` は、入力 family の officeDocument relationships namespace に属する local name `id` 属性を意味する。XML declaration、encoding、namespace declaration、ignorable markup、extension list、既存 child/attribute の値と順序を可能な限り保持する。allowlist part は payload byte equality の対象外だが、許可 XPath 外の semantic 差分がないことを別に検証する。semantic diff は namespace URI、local name、属性値、text、child order を比較し、prefix名と属性順だけを無視する。コメント、processing instruction、CDATA、significant whitespace を勝手に破棄できない入力は fail-closed とする。

### Styles part の最小形と index 規則

入力に styles part がない場合は、入力 family の namespace を使い、少なくとも次を持つ styles part を作る。

- `fonts count="1"`: app default font 1件。
- `fills count="2"`: `patternType="none"` と `patternType="gray125"` の既定 fill。
- `borders count="1"`: 空の left/right/top/bottom/diagonal を持つ既定 border。
- `cellStyleXfs count="1"`: `numFmtId="0" fontId="0" fillId="0" borderId="0"` の既定 `xf`。
- `cellXfs count="1"`: 上記を参照し `xfId="0"` を持つ既定 `xf`。
- `cellStyles count="1"`: `name="Normal" xfId="0" builtinId="0"`。

既存 styles part がある場合、index 0 および既存 record を変更しない。各 collection の追加 index は現在の child count から連続して割り当て、宣言 count ではなく実 child count と一致することを前後で検証する。新規 `cellXf`/`cellStyleXf`/`dxf` が参照する `fontId`、`fillId`、`borderId`、`xfId`、`numFmtId` は存在範囲と用途を検証する。custom number format が必要な場合は、既存 custom `numFmtId` と built-in reserved range を走査し、164以上の未使用 ID を決定論的に割り当てて `numFmts` の末尾へ追加する。参照切れ、重複ID、count不一致、上限超過は書込み前に拒否する。

## Sheet identity と予約名

- source sheet は表示名だけでなく既存 `sheetId` と relationship ID/targetで固定する。
- `Eval` が Unicode case-insensitive に衝突する場合は `Eval (2)`、`Eval (3)`…を提案し、禁止文字と31文字上限を満たす最初の未使用名を利用者に確認させる。
- `Evaluation_Config` または `Evaluation_Run` が既に存在する場合、既存 sheet を置換・削除しない。正本はこの2名を必須名としているため、未評価入力との名前衝突は `RESERVED_SHEET_NAME_CONFLICT` で fail-closed とし、勝手に `(2)` へ改名しない。この規則は、衝突時に一意名を提案する `Eval` とは意図的に異なる。
- schema marker は `Evaluation_Run!A1` の simple inline string `StudyReportEvaluator.Evaluation_Run/v1` とし、sheet名やfilenameだけでは marker と判定しない。marker の構造検証は O-04/O-08 で行う。
- `Evaluation_Run` がなく有効な署名済み run-manifest relationship もなければ、marker 観点では未評価候補として後続検査する。
- `Evaluation_Run` と完全な marker があれば評価済み出力として扱い、実データ modeでは再入力を拒否する。
- `Evaluation_Run` はあるが marker がない場合は、利用者作成 sheetとの予約名衝突として拒否する。marker に似た不正値、構造不正、または署名済み relationship の検証失敗は評価状態不明の unsafe package として拒否し、未評価入力へ格下げしない。

## `Eval` 投影契約

### 対象

利用者がその場で明示確認したmappingに含まれる列と、設定されたheader rowからfinal data rowまでを投影する。この確認は本人・role承認ではない。識別/管理列は出力へ保持できるがCopilot payloadとは別DTOとし、主回答・補助回答・学習者Prompt・無視列のroleを混同しない。全空列、無視列、未使用列は利用者の選択に従って`Eval`から除外できる。

### 値

| Source cell | `Eval` への投影 |
|---|---|
| text、shared string、inline string、rich text | 表示 text を単一の inline string cellとして保存。rich formatting、phonetic run、comment、hyperlink は継承しない |
| finite number | numeric valueとして保存。日付/時刻と承認された小数表示は app-owned canonical styleへ写像 |
| boolean | boolean valueとして保存 |
| empty/missing/style-only | emptyとして保存し、style-only末尾をdata rowに数えない |
| ISO 8601 date (`t="d"`) | schema-valid な値だけを date cellとして保存する。変換・timezone補完を行わない |
| error value | error tokenを inline stringとして表示し、`VALIDATION_STATUS=UNSAFE_INPUT` または明示 warning metadata を付ける。新規 error cellにはしない |
| local formula with cached value | 式の存在、式種別、cached value の有無を警告し、利用者が source risk を承認した場合だけ cached valueを上表の型で投影。`CellFormula` は作らない |
| local formula without cached value | 警告し、値を空欄として投影するか mapping から除外するまで実行確認を通さない。アプリ内で式を評価しない |
| external reference formula、DDE、data table、untrusted executable relation | package を拒否し、投影しない |

local formula には通常式、shared/array formula、dynamic array の識別情報を含めて警告する。data table formula は外部実行がなくても本 ADR の投影対象外として拒否する。external reference の判定不能、cached type/value の不整合、非有限数、上限超過は fail-closed とする。cached value は「式の評価結果を本アプリが検証した値」と表示せず、source workbook に保存されていた未検証 snapshot と明示する。数式 node は source sheet にだけ保持し、`Eval` には決して作らない。

### 必要最小限の静的表示

source の `StyleIndex` をそのまま新規 cell へ割り当てず、次だけを app-owned styleへ安全に写像する。

- date、time、date-time、integer、decimal、percentage の意味を保持する canonical number format。numeric source を date/time と判定するのは、参照先 style/number format が有効で、built-in date/time ID または閉じた custom format parser で曖昧なく分類できる場合だけとする。分類不能なら raw finite number を `General` で保持して警告し、日付へ推測変換しない。
- alignment は wrap text だけを写像し、rotation、indent、horizontal/vertical alignment は継承しない。
- column width は finite な source width を $[0,255]$、row height は finite な point 値を $[0,409]$ に clampする。欠落・不正値は app defaultを使い、hidden stateを幅/高さ0として継承しない。
- header と warning を文字・icon・style の併用で示す app-owned style。
- app が生成した status/conditional formatting。色だけを状態表現にしない。

source font、fill、border、theme、conditional formatting、protection、hidden state、merge、data validation、sparklines、drawing、image、chart、pivot、table、filter、print settings、row/column grouping、freeze pane は投影しない。custom number format は実行せず解析し、date/time/percentage等へ安全に分類できる場合だけ canonical formatへ写像する。分類不能または表示を隠す/misleadする format は既定の text/number表示へ落として警告する。

### 文字列安全境界

入力と LLM 由来の文字列は、検知結果にかかわらず明示的な inline/shared string cell として書き、`CellFormula`、DDE、hyperlink、external relationship を生成しない。本 ADR の新規 sheet は `t="inlineStr"` と単一の `<is><t>...</t></is>` に限定し、rich-text `<r>`/`<rPr>` を作らない。先頭/末尾空白または連続空白を保持する必要がある `<t>` には `xml:space="preserve"` を設定する。`= + - @`、全角対応文字、tab/CR/LF、Unicode White_Space/format control を含む FR-087 vector は危険表示を付けるが、危険文字の削除や先頭 apostrophe 追加によって元表示値を改変しない。

## 正方向の受入条件

拒否条件だけで false green にしないため、少なくとも次を正方向で検証する。

1. 危険要素のない valid Transitional `.xlsx` の1問、2問、10問 fixtureを受理する。Strictを対応対象に含める場合は同じ正方向 fixtureを別に持つ。
2. styles part がある fixtureでは既存 style record を保持して app-owned styleだけを追加し、styles part がない valid fixtureでは本 ADR の最小 styles partを作成する。
3. source sheet ID/name、header、range、typed value、途中空行、style-only末尾の期待値と投影結果が一致する。
4. 既存 `Eval` がある clean fixtureでは、利用者が承認した一意な `Eval (n)` を追加し、既存 sheetを変更しない。
5. outputを再 openでき、全既存 sheetと新規3 sheetが存在し、各 sheet relationship/target/content typeが一致する。
6. `Evaluation_Run!A1` marker、選択元/生成 sheet identity、input hash、manifest digestの所定セルが存在する。回答本文、PII、tokenは control sheetに存在しない。
7. app-owned formula/string/styleが所定位置に存在し、`Eval` の投影文字列に formula node、hyperlink、worksheet relationshipがない。
8. 変更許可外 fingerprintが一致し、allowlist partの差分が許可 XPath/tupleだけである。

## ファイル完成境界

本 ADR は package mutation を定義し、完成名への commit は ADR-0002 に従う。O-08 の package/allowlist検証後、O-09 が `.partial.xlsx` を close・durable flush・再open・検証して hashを確定し、O-10 が署名済み completion recordを確定し、O-11 が `CompletionProof` 一致後だけ同一ボリューム renameとpair再検証を行う。rename失敗、競合、proof不一致では完成名を残さず、入力を変更しない。

## package fingerprint と検証

書込み前後で全 package を走査し、各 part について次を比較する。

- exact package URI text と一意性
- content type
- 展開後 payload SHA-256
- relationship tuple 集合

判定規則:

1. allowlist 外の既存 part は payload、URI、content type、relationship tuple の全てが一致しなければ失敗。
2. allowlist 内の既存 part は URI/content type を維持し、許可 XPath/relationship 以外の semantic 差分があれば失敗。
3. 新規 part/relationship は本 ADR の app-owned allowlist と実行 plan に完全一致しなければ失敗。
4. 既存 part の欠落、既存 sheet の欠落、external relationship の新規発生、未知 app-owned part は失敗。
5. `OpenXmlValidator` 成功だけを AC-007 の証明にせず、fingerprint と semantic diff を併用する。

## 要求改版案

G-RB は少なくとも次を正本へ反映する。

- FR-009 の「part bytes」は展開後 payload bytes とし、URI、content type、relationship tuple と併せて比較する。
- `Eval` は値と必要最小限の app-owned canonical static displayだけを投影する。
- external reference式は拒否する。local式は式存在とcached valueを警告し、承認後にcached valueだけを未検証 snapshot として投影する。式nodeはコピーしない。
- `Evaluation_Config`/`Evaluation_Run` の予約名衝突は既存 sheet を変更せず fail-closed とする。
- 変更可能 part、relationship、XML element は本 ADR の閉じた allowlist に限定し、それ以外の差分を完成前検証で拒否する。

## 検証責務

- G-16: 最小 package mutation、許可外 payload/graph 不変、calc chain保持とfull recalc、Office/LibreOffice挙動を実OSで確認する。
- W-06/W-07/W-08: URI、relationship、formula、external execution、visibility/protection を書込み前に分類する。
- W-12: baseline fingerprint を作る。
- O-01〜O-08: no-op preservation、3 sheet、投影、formula injection、semantic diff、schemaを正負両方向で検証する。
- E-01/E-07: crashを含むinput不変と採用Officeでの再計算をRCに対して検証する。

これらの実測前にAC-002/AC-007や対応OSをPASSと記録しない。

## 完了判定

DEC-03/04に従い、投影する値/静的表示、変更可能URI/relationship/XML element、予約名衝突、formula/cached value、package fingerprintの契約を列挙し、要求v1.2へ反映した。実装・実測は未完了である。
