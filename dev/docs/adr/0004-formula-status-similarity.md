# ADR-0004: 数式、状態、類似度区分の契約

> [!WARNING]
> **HISTORICAL / SUPERSEDED:** mandatory review status、similarity、calibrationを含む旧requirements v1.xの設計です。現行v3ではvalid AI rawをoptional overrideなしでも使用し、similarityを実装しません。現行契約は[ADR-0011](0011-dynamic-quantification-excel-formulas.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **承認済み・G-04完了・要求v1.2へG-RB反映済み（ADR-0009 launcher訂正、G-16 ROUND sentinel訂正を含む）** |
| 対象決定 | DEC-05、DEC-06、DEC-21 |
| 要求 | FR-071〜075、FR-080〜086、第15節、AC-002、AC-005 |
| 決定 | `ROUND` だけを関数 allowlistへ追加し、既存5軸を維持する。類似度 metric/数値閾値に製品既定を持たず、署名済み校正 recordで選択する |
| 承認根拠 | ADR-0001 の承認証跡（実装前 decision gate は既定案） |
| 記録日 | 2026-08-31 |

## 解消する矛盾

FR-081 は丸めを `Evaluation_Config` 参照にする一方、FR-085 の関数 allowlist に丸め関数がない。このままでは「丸めを設定可能」と「allowlist 外関数禁止」を同時に満たせない。また、第15節の `REVIEW_STATUS` と Excel の `教員判断` token、`VALIDATION_STATUS=RECALC_REQUIRED` から `VALID` への遷移、FR-072 の高/中/低が使う metric と境界包含規則が未定義である。

本ADRの変更案はG-RBが要求v1.2へ反映した。ただしstrict GATE-0を満たすまでL-10〜L-14とproduction Formula Builderを開始しない。

## Formula AST と serializer

生成数式は文字列連結で自由生成せず、Core の closed algebraic AST だけから SpreadsheetML formula textへ serializeする。AST が表現できるものを次に限定する。

### 関数 allowlist

- `IF`
- `IFERROR`
- `AND`
- `OR`
- `ISNUMBER`
- `SUM`
- `SUMPRODUCT`
- `COUNT`
- `ROUND` — 本 ADR で追加する唯一の関数

`ROUND`の第2引数は`Evaluation_Config`の検証済みReportDefinition丸め桁数cellへのabsolute referenceとし、数式へ運用固有の桁数を直書きしない。桁数cellはfinite integer、Excelが受理する範囲、およびrun snapshotとしてcode validationする。本人・role承認は要求しない。未設定・非整数・範囲外ならvalidation formulaは`INCOMPLETE`を返し、採用点・小計・総合点・採否を空欄にする。具体的な業務桁数に製品既定を設けない。

### 演算子と値

- 比較: `=`, `<>`, `<`, `<=`, `>`, `>=`
- 算術: `+`, `-`, `*`, `/`
- group: parenthesis
- cell/range reference: app-owned `Eval` と `Evaluation_Config` 内の検証済み A1 referenceだけ
- literal: 状態/区分の固定token、空文字列、Boolean `TRUE`/`FALSE`、AST構造に必要な`0`と`1`、および検証済みReportDefinition snapshotから導出した非負schema cardinalityだけ

schema cardinality は rubric item 数等の構造値であり、点数、weight、threshold、丸め桁数、合否基準ではない。serializer は由来を型で区別し、任意数値を structural literal として注入できないようにする。`SUMPRODUCT` 内では同じ長さの検証済みrangeへの比較と、Booleanを数えるための `1 * (comparison)` だけを許す。

source sheet、external workbook、URI、DDE、defined name、volatile function、dynamic array operator、union/intersection、3D reference、whole-row/whole-column reference は AST で表現不能にする。P1 の named range を採用する場合も、G-09/P1-07 の別契約と再検証なしに追加しない。

OOXML formula text は invariant な英語関数名、`.` decimal separator、`,` argument separator、sheet name の必要な quoting を使う。formula text は 8,192文字未満とし、参照 graph は生成前後に DAG であることを検証する。`IFERROR` は禁止 AST を隠す手段にせず、inner expression も同じ allowlist/参照検査を通す。

## 設定参照

次は`Evaluation_Config`の固定cellまたはcode validation済みapp-owned table/rangeを参照し、式へ数値を埋め込まない。

- rubric item の minimum/maximum
- item weight と question weight
- 丸め桁数
- subtotal/total/pass の業務条件
- similarity metric ID、高閾値、中閾値
- `threshold_approval_status`
- calibration record digest、course ID、design hash

formula serializer は logical setting ID から実 cell addressへの1つの verified mapを入力とし、任意文字列を addressとして受け取らない。address 欠落、重複、別 sheet、範囲外、循環は formula 生成前に拒否する。この生成前検査は参照構造を保証する。一方、教員が後から編集できる Config cellの値は各再計算時にformulaで型・範囲を検査する。両者は重複統制ではなく、address構造とeditable valueを分担する。

## 5つの状態軸

軸を追加・統合せず、要求第15節の5軸を維持する。

1. `AI_STATUS`
2. `VALIDATION_STATUS`
3. `REVIEW_STATUS`
4. `SIMILARITY_STATUS`
5. `RUN_STATUS`

低レベル原因は別の `ERROR_CODE` とし、軸の token を自由文字列化しない。各 code は closed enum、case-sensitive ASCIIで保存し、未知値は `VALID` や成功へ丸めず validation failure とする。

## `教員判断` から `REVIEW_STATUS` への mapping

`教員判断` は利用者の操作 token、`REVIEW_STATUS` は検証後の永続状態である。次の mapping だけを許す。

| `教員判断` token | 永続 `REVIEW_STATUS` | 前提 |
|---|---|---|
| 空欄または `PENDING` | `PENDING` | 初期状態または保留へ戻す明示操作 |
| `ACCEPT_AI` | `ACCEPTED` | AI候補が存在し、対象evaluation unitのcode validationが成功し、利用者のexplicit actionとtimestampが記録される |
| `OVERRIDE` | `OVERRIDDEN` | 教員上書き値がfinite numberかつConfigの範囲内で、explicit actionとtimestampが記録される |
| `REJECT` | `REJECTED` | explicit actionとtimestampが記録される。採用点は空欄 |
| `ESCALATE` | `ESCALATED` | explicit action、timestamp、理由/commentが記録される。採用点は空欄 |

FR-083 の `教員判断` 列の token 一覧へ `ESCALATE` を追加する。これは新しい状態軸ではなく、第15節に既存の `REVIEW_STATUS=ESCALATED` へ到達する入力 token の明確化である。

`ACCEPT_AI`/`OVERRIDE` の前提を満たさない操作は status を成功側へ変更せず、利用者へ理由を返す。`REVIEW_STATUS` が `ACCEPTED`/`OVERRIDDEN` でも、Excel の採用点は `AI_STATUS=SUCCESS` かつ `VALIDATION_STATUS=VALID` が同時成立しなければ必ず空欄とする。

全操作はexplicit actionとtimestampを必須とし、`ESCALATE`は空白でない理由またはcommentも必須とする。監査eventのdurable appendに成功した後だけ同じtransaction/commit境界で`REVIEW_STATUS`を更新し、記録失敗時は旧状態を維持する。アプリはlocal launcher/判断者の本人性・roleを認証しない。任意の`reviewer_label`を記録しても自己申告文字列であり、遷移や採用点の必要条件、認証済みidentityにはしない。

### Review transition

- 初期値は `PENDING`。
- `PENDING` から上表の任意状態へ、前提を満たす明示操作で遷移できる。
- 既決状態から別状態への変更も利用者の明示操作で可能だが、旧候補、旧判断、timestamp、任意reviewer label/commentを監査履歴に保持する。
- candidate、Prompt/rubric/model/input/sheet identity の変更または rerun で evaluation unit identity が変わる場合、旧判断を新 unitへ流用せず、新 unitは `PENDING` から開始する。
- system errorだけで教員判断を `ACCEPTED`/`OVERRIDDEN` にしない。

## `RECALC_REQUIRED → VALID`

app が生成する `VALIDATION_STATUS` formula cell は、保存時の cached valueを `RECALC_REQUIRED` とする。formula 自体は code validationの結果を保持する app-owned literal `PRECALC_VALIDATION_CODE`、candidate、override、range、required field、status token、editable Config値を検査し、採用 Office/LibreOffice が再計算した後にだけ `VALID` または具体的な非成功 codeを返す。`PRECALC_VALIDATION_CODE` は新しい状態軸ではなく、`VALID` / `OUT_OF_RANGE` / `UNVERIFIED_EVIDENCE` / `UNSAFE_INPUT` / `INCOMPLETE` の closed inputである。

この型とclosed alphabetは T-01 の domain contractが定義し、実runでは C-02 `EvaluationOutputValidator` が workbook生成前に各 evaluation unitへ1回生成する。O-05 はこのdomain valueを app-owned literal helper cell `{QID}.{EvaluatorID}.PRECALC_VALIDATION_CODE` へ書き、任意文字列を受け取らない。test fixtureは同じdomain factoryだけを使う。利用者編集対象ではなく、手動変更や未知値は O-08/L-14 の再検証で `INCOMPLETE` として拒否する。

validation formula の優先構造は次に固定し、全体を `IFERROR(...,"FORMULA_ERROR")` で囲む。

1. `PRECALC_VALIDATION_CODE` がclosed alphabet外なら `INCOMPLETE`。
2. `AI_STATUS<>"SUCCESS"` なら `INCOMPLETE`。要求第15節に従い、overrideでもこの条件を迂回しない。
3. `REVIEW_STATUS="OVERRIDDEN"`の場合、候補固有の`OUT_OF_RANGE`または`UNVERIFIED_EVIDENCE`は`ERROR_CODE`/監査に保持したまま、教員上書き値を独立検証する。overrideが数値かつConfig min/max内でexplicit action/timestamp/comment要件を満たす場合だけ後続Config検証へ進む。`UNSAFE_INPUT`、`INCOMPLETE`、未知codeはoverrideで迂回しない。
4. override以外で `PRECALC_VALIDATION_CODE<>"VALID"` なら、その許可済み非成功 codeを返す。
5. selected candidate/override、required Config count、required cellが不足または非数値なら `INCOMPLETE`。
6. selected candidate/override が Config min/max外なら `OUT_OF_RANGE`。
7. 丸め桁数が非数値なら`INCOMPLETE`。数値の場合だけ内側の`IF`で`ROUND(round_digits,0)=round_digits`とG-16で実証済みのengine共通rangeを検査し、不正なら`INCOMPLETE`。
8. required weightが非数値、負、またはweight合計が0以下なら `INCOMPLETE`。個別weight 0は許すが、全体を0点として確定する根拠にはしない。
9. 全条件成立時だけ `VALID`。

各段階は scalar guardを外側の `IF` に置き、非数値を `ROUND` や比較へ渡さない。rangeの完全性は `COUNT(range)=schema_cardinality`、負weightは `SUMPRODUCT(1*(weight_range<0))>0`、合計0以下は `SUM(weight_range)<=0` で検査する。range長は生成時に一致を検証する。

丸め桁数の supported minimum/maximum は教育上の既定値ではなく、採用する Excel/LibreOffice versionで共通に安全評価できる範囲である。G-16 の cross-engine known-answerで境界内・境界外・巨大値を実測し、`eng/platform-matrix.json` の versioned release constantとして1組だけ固定する。G-16 が共通rangeを確定できなければ GATE-0を通さず、L-10を開始しない。validation formula はその上下限を app-owned Config cellから参照し、数値かつ整数かつ範囲内であることを外側の`IF`で確認してから`ROUND`分岐へ進む。

G-16ではMicrosoft Excel `16.0.20402.20050`とLibreOffice Calc `26.8.0.3`に同一の非整数値を与え、`rounding_digits=-400..400`を再計算した。Excelは801件すべて数値、LibreOfficeは`-20..20`の41件だけ数値で、その41件はIEEE 754 binary64でbit-exact一致した。したがって、このengine組合せの共通range候補はinclusive `-20..20`である。最終release constantはG-17のplatform matrixが同じbinary/versionを対象に確認して固定し、未試験engine/versionへ流用しない。

旧案の`ROUND(0,round_digits)=0` sentinelはproductionのvalidation/adoption formulaから削除する。G-16 spikeには欠陥再現用probeとして残す。LibreOfficeでは非整数値の`ROUND`が`-21`、`21`、`-400`、`400`でerrorになる一方、zero sentinelは同じ全点でtrueとなり、範囲外を検出しなかった。固定witness値に対するengineの特例をrange認可と誤認しないため、formula probeをrange認可へ使わない。range認可はG-16/G-17で固定したversioned boundsとの比較だけで行い、engine保存後のerror検査は別のE-07証跡とする。

`VALID` へ進める条件は次の全てである。

- formula text/hashがversion固定・test済みbuilderの出力と一致する。
- formula AST、reference、DAG、長さ、function allowlist の検証が成功する。
- workbook が Automatic/full-calculation-on-load を要求する。
- 採用済み Office/LibreOffice 環境が当該 cellを再計算し、保存する。
- 再open後の cell type/cached value と期待式の独立検証が成功し、新規 formula errorがない。

利用者が literal `VALID` を入力する、cached valueだけを変更する、calculation modeを変更する、未試験 engineで保存するだけでは検証済み遷移とみなさない。`OUT_OF_RANGE`、`UNVERIFIED_EVIDENCE`、`FORMULA_ERROR`、`UNSAFE_INPUT`、`INCOMPLETE` は原因解消と再生成・再計算なしに `VALID` へ昇格しない。採用環境ごとの成立は G-16/E-07 前に保証しない。

dependency graph は `PRECALC/input/config → VALIDATION_STATUS → adopted score → subtotal → total → pass` の一方向とする。したがって初回 full recalculation でも validation cellが adopted scoreより先に評価され、同じ計算 cycleで cached `RECALC_REQUIRED` から結果へ更新されることを期待する。この挙動は採用 engineごとに G-16/E-07 で実測し、成立しなければ PASS にしない。

## 採用点 formula

採用点が非空である必要十分条件は要求第15節をそのまま使う。

`AI_STATUS=SUCCESS ∧ VALIDATION_STATUS=VALID ∧ REVIEW_STATUS∈{ACCEPTED, OVERRIDDEN}`

- `ACCEPTED` では、AI候補が `ISNUMBER` かつ Config の min/max内の場合だけ `ROUND(AI候補, Config丸め桁数)` を返す。
- `OVERRIDDEN` では、教員上書き値が `ISNUMBER` かつ Config の min/max内の場合だけ `ROUND(上書き値, Config丸め桁数)` を返す。
- 上記以外、未知 token、式errorでは空文字列を返し、0へ変換しない。
- AI候補と教員上書き値は別 literal cellとし、採用/上書きでAI候補を消さない。

formula の外側は `IFERROR(IF(AND(AI_STATUS="SUCCESS",VALIDATION_STATUS="VALID"), review分岐, ""), "")` とする。review分岐は `ACCEPTED` と `OVERRIDDEN` を別々に検査し、それ以外は空文字列を返す。`VALIDATION_STATUS=VALID` は候補/overrideの型、範囲、Configを検証済みであることを含むため、状態3条件は有効workbookで十分条件になる。内側の `ISNUMBER`/min/max再検査は破損・手動不整合に対する防御であり、3条件成立時にそれが失敗した場合は通常の空欄ケースではなく invariant violation として output validationを失敗させる。`VALIDATION_STATUS<>VALID` の場合は `ROUND` 分岐へ到達しない。

invariant violation は特殊なExcel errorを生成して表現しない。O-08/L-14 の row validatorが、(a)状態3条件、(b)選択されたcandidate/overrideの型・範囲、(c)round Config、(d)採用点formula AST、(e)再計算後cached valueを独立に評価する。状態3条件が成立するのに防御predicateが偽、採用点が空、または期待値と不一致なら `OUTPUT_VALIDATION_FAILED` として package確定を拒否する。500行以上のL-11直積にはこの不整合注入caseを含める。

Core test は少なくとも500行で `AI_STATUS × VALIDATION_STATUS × REVIEW_STATUS` と candidate/override の空、0、負、境界、範囲外、非数値を直積し、正方向の `ACCEPTED`/`OVERRIDDEN` と負方向の全空欄を検査する。

## subtotal、total、pass

- 未確認または不正な必須 adopted score が1つでもあれば、その集計段階は空欄にする。required score rangeについて `COUNT(score_range)=schema_cardinality` を外側で満たす場合だけ集計分岐へ進むため、Excelが空文字列を算術0へcoerceしても集計結果へ到達しない。
- weight が空、非数値、負、または対象 weight 合計が0なら、数値を返さず対応する validation codeを出す。
- 有効な adopted score と正の weightだけを、Config参照の `SUM`/`SUMPRODUCT` と四則演算で集計する。
- subtotal、total、pass の順に一方向参照し、逆参照や循環を作らない。
- pass条件を製品が作らず、利用者が起動後のReportDefinitionで作成した総合式をclosed ASTへ変換できる場合だけ生成する。run開始時にsnapshot/hashへ固定し、allowlistで表現不能またはExcel/resource上限を超える場合はreport設定を変更するまで送信前に停止する。外部承認済み総合式を実装前入力として要求しない。

weighted aggregateの構造は、全体を`IFERROR`で囲み、`COUNT(score_range)=N`、`COUNT(weight_range)=N`、`SUMPRODUCT(1*(weight_range<0))=0`、`SUM(weight_range)>0`を確認した後だけ`SUMPRODUCT(score_range,weight_range)/SUM(weight_range)`を評価する。$N$はrun開始時に検証・固定したReportDefinition由来のschema cardinalityで1以上とする。個別weight 0は有効だが、全weight 0、negative、text、blank weight、score空欄のいずれでも数値0へ黙って集計しない。対応するvalidation helperは`INCOMPLETE`を返す。

境界caseを一意にするため、weights `[0, 1]` と有効score 2件は有効集計、weights `[0, 0]` は `INCOMPLETE` と空欄、weights `[-1, 2]` は合計が正でも `INCOMPLETE` と空欄にする。

## 類似度 metric

### 製品既定なし

高/中/低を包含率 $C$ だけで決める根拠はなく、multiset Jaccard $J$ も一律の正解ではない。製品は active metric、数値閾値、course共通値を埋め込まない。DEC-06 の「未決」を、次の closed protocol と fail-closed 状態として実装する。

署名済み calibration record が選べる metric ID は初版では次のいずれか1つだけとする。

- `QUESTION_SHINGLE_COVERAGE` — FR-070 の $C(q,p)$
- `MULTISET_JACCARD` — FR-070 の $J(q,p)$

複合 metric、最大/平均、AI推定 metric、完全包含を数値化した metric は初版 schemaで受け付けない。追加には metric specification、known-answer、教育評価承認、schema/version変更が必要である。

`完全包含` は threshold formulaが計算する値ではない。L-08 の正規化後に L-09 が Unicode scalar配列を比較し、正規化した学習者Prompt $p$ が正規化した設問文 $q$ の全scalar列を連続部分列として含む場合だけ Boolean literal `TRUE` を出力する。空/片空/3 scalar未満は状態判定が先であり、完全包含を `TRUE` にしない。$C=1$ や $J=1$ を完全包含の代用にしない。

O-05 はこの Booleanを各行の app-owned literal cell `{QID}.完全包含` へ、$C$ を `{QID}.設問包含率`、$J$ を `{QID}.MultisetJaccard` へ書く。L-13 は logical column IDから検証済みaddressを受け取り、完全包含をinline計算しない。FR-071 の出力列一覧へ `{QID}.完全包含` を明記する変更は G-RB が行う。

### Calibration record と境界

有効な record は少なくとも次を署名対象にする。

- record/schema ID、issuer、audience、key ID、record ID、issued/expiry time
- course ID、design hash、normalization/ICU version、metric ID
- `high_threshold`、`medium_threshold`
- calibration pair-set hash、標本数、false-positive/false-negativeの承認済み定義と測定値
- approver ID、approval timestamp

数値境界は $0 \le medium\_threshold \le high\_threshold \le 1$ の finite numberだけを許す。同値を許すが、その場合は中類似区間が空になることをUI/Excel説明へ表示する。record の metric、course、design、ICU、threshold hash、期限、署名のいずれかが不一致なら `threshold_approval_status` は `INVALID`、欠落なら `MISSING` とし、`VALID` を利用者が直接入力できないようにする。

境界包含規則は次に固定する。

- metric value $\ge high\_threshold$: `高類似`
- それ以外で metric value $\ge medium\_threshold$: `中類似`
- それ以外: `低類似`

閾値の具体値は記載しない。値は結果を見る前に教育評価責任者が対象courseの既知pairで承認する。

## 類似度区分 formula の優先順位

次の順序を変えない。

1. `SIMILARITY_STATUS=NOT_APPLICABLE` → `対象なし`
2. `SIMILARITY_STATUS=INSUFFICIENT_DATA` → `情報不足`
3. `SIMILARITY_STATUS=TOO_SHORT` → `短すぎる`
4. `threshold_approval_status<>VALID`、metric ID不明、metric/threshold非数値・範囲不正 → `閾値未校正`
5. `完全包含=TRUE` → `完全包含`
6. signed record が選択した列の metric valueを上記境界で比較 → `高類似` / `中類似` / `低類似`

`SIMILARITY_STATUS=COMPUTED` 以外が step 4以降へ到達しないよう検査する。`UNCONFIGURED` は `閾値未校正` とする。署名recordが無効なときは完全包含も強い区分として表示せず、常に `閾値未校正` とする。これは FR-073 の「record欠落/不正時は区分が閾値未校正」に従う。

serializer はこの順序を nested `IF` の外側から内側へそのまま表現し、全体を `IFERROR(...,"閾値未校正")` で囲む。step 4 は `ISNUMBER(high)`、`ISNUMBER(medium)`、$0\le medium\le high\le1$、既知metric ID、および選択対象 metric cell の `ISNUMBER` を内側の比較より先にguardする。metric ID は nested `IF` で `QUESTION_SHINGLE_COVERAGE` なら $C$ cell、`MULTISET_JACCARD` なら $J$ cellを選び、未知値を数値比較へ渡さない。これにより `INVALID + 完全包含=TRUE` は必ず `閾値未校正`、非数値 metric は error値ではなく `閾値未校正` になる。

## 採点との非接続

- adopted score、subtotal、total、pass、weight、review transition の formula AST は similarity status、完全包含、$C$、$J$、区分、thresholdを参照できない。
- similarity formula は score/statusを変更せず、review queue の表示候補だけに使う。
- generated formula graph に similarity node から採点nodeへのedgeが1本でもあれば output validationを失敗させる。
- UI、Excel、README に FR-075 の注意文を表示する。高類似を不正、AI利用、減点の証拠と表現しない。

教員がExcelで式を手動変更する自由は維持するが、その式を app-generated formula として保証しない。再検証時は AST、DAG、参照、非接続を再検査し、循環や similarity→score参照を検出した workbookを検証済み結果と表示しない。手動変更を黙って元へ戻さない。

graph edge は「formula cellから、そのformulaが直接参照するcell/range memberへ」の向きとする。similarity node集合は `SIMILARITY_STATUS`、完全包含、$C$、$J$、metric ID/selected metric、threshold、approval status、類似度区分の全cellである。adoption-critical node集合は `VALIDATION_STATUS`、`REVIEW_STATUS`、採用点、subtotal、total、pass、要確認/採否formulaの全cellである。adoption-critical nodeからsimilarity nodeへの直接または推移的pathを禁止する。逆向きも本製品の生成式には不要なため禁止し、review queueでの表示利用はExcel formula graph外のread-only consumerとして実装する。

## 要求改版案

G-RB は少なくとも次を正本へ反映する。

1. FR-085 の関数 allowlistへ `ROUND` だけを追加し、第2引数を Config参照に限定する。
2. `教員判断` tokenへ `ESCALATE` を追加し、本 ADR の `REVIEW_STATUS` mappingとtransitionを採用する。
3. `VALIDATION_STATUS=RECALC_REQUIRED` は、検証済み formulaの採用Office再計算・保存・再open検証後だけ `VALID` または具体的errorへ遷移する。
4. similarity active metricに製品既定を設けず、署名校正 recordの closed metric IDで $C$ または $J$ を1つ選ぶ。
5. 高/中境界の具体値は recordだけが供給し、包含規則を `>= high`、次に `>= medium` とする。
6. calibration無効/欠落時は完全包含を含め区分を `閾値未校正` とし、similarityを採点へ接続しない。

## 検証責務

- L-10: closed AST、serializer、Config固定参照、`ROUND`、禁止構文を検査する。
- L-11/L-12: adopted score直積とaggregate正負条件を検査する。
- L-13: status優先、calibration/metric/boundary、採点非接続を検査する。
- L-14/O-08: formula parser、reference graph、DAG、8,192文字、external/禁止関数0を再検証する。
- O-05/E-07: cached `RECALC_REQUIRED`、採用Office再計算後のstatus/value、式保持、新規error 0を検査する。

実測前に Office/LibreOffice の同一計算結果、具体的閾値、教育上の妥当性をPASSと記録しない。

## 完了判定

丸め矛盾、review mapping、既存軸内transition、recalc遷移、metric enum、数値境界制約と包含規則、calibration invalid時のfail-closed、類似度の採点非接続を定義した。要求正本への反映は G-RB だけが行う。
