# Excel / formula 契約 v4.0

| 項目 | 内容 |
|---|---|
| Normative baseline | requirements v4.0 / ADR-0012 |
| Detailed design | [`detailed-design.md`](detailed-design.md) |
| Input | standard `.xlsx` 1 file, read-only |
| Partial output | input byte-copy + `Quantification_Checkpoint` |
| Final output | input byte-copy + Config / References / Results / Run |

## 1. File identity

既定directoryは`<input directory>/result`、basenameは`eval-yyyyMMdd-HHmm`とする。finalとpartialの両方が未使用となる最小suffixを予約する。

| Candidate | Final | Partial |
|---|---|---|
| first | `eval-20260901-1530.xlsx` | `eval-20260901-1530.partial.xlsx` |
| collision 1 | `eval-20260901-1530-02.xlsx` | `eval-20260901-1530-02.partial.xlsx` |
| collision 2 | `eval-20260901-1530-03.xlsx` | `eval-20260901-1530-03.partial.xlsx` |

既存fileを上書きしない。directoryはrun開始時に作成できる。予約後のraceでtargetが作成された場合も上書きせず失敗する。

## 2. Partial workbook

partialは入力全体のbyte-copyへ`Quantification_Checkpoint`を1 sheet追加する。入力に同名sheetが既にある場合はresume sourceとの混同を防ぐためcheckpoint作成を拒否する。

### 2.1 Sheet format

| Column | Name | Contract |
|---|---|---|
| A | `RecordType` | `META` or `PAYLOAD` |
| B | `KeyOrChunkIndex` | metadata key or 0-based chunk index |
| C | `Value` | metadata value or JSON chunk |

META rows:

- `SchemaVersion`
- `PayloadSha256`
- `ChunkCount`

PAYLOADはcanonical JSONを30,000 characters以下へ分割する。readerはunknown record、duplicate key/chunk、missing chunk、index gap、invalid hash、unknown JSON propertyを拒否する。

### 2.2 Atomic update

1. partial directoryにCreateNew tempを作る。
2. inputをbyte-copyし、Checkpoint sheetを書く。
3. flush、close、read-only reopenする。
4. package、sheet、chunk order、payload hash、snapshot hashをvalidateする。
5. 初回はno-overwrite move、更新はsame-volume atomic replaceする。
6. replace前の失敗では旧partialを保持する。
7. tempはresume sourceとして列挙しない。

保存時点は各reference完了後と各student row完了後である。

## 3. Final app-owned sheets

| Sheet | 内容 |
|---|---|
| `Quantification_Config` | v4 canonical definition、mapping、points、ranges、formula input cells |
| `Quantification_References` | question text、reference answer、model、status、timestamp |
| `Quantification_Results` | normal/special/similarity literals、score formulas、reason/evidence/status |
| `Quantification_Run` | identity、paths、counts、usage、start/end、actual sheet names |

同名sheetが入力にある場合は既存sheetを変更せず、` (2)`、` (3)`の最小suffixを使う。formulaとRun sheetは実際の名前を参照する。

## 4. Config contract

既存v3 node recordに加え、次のfieldを持つ。

- root: `BasePoints`, `SpecialPoints`, `SimilarityPenaltyWeight`, `RoundingDigits`
- question: `QuestionPoints`
- special: ID、display name、Prompt、primary/supporting columns、enabled
- formula validation: `AllocationTotal`, `AllocationValid`

verified absolute referenceとして次をwriterから返す。

- base cell
- special points cell
- similarity weight cell
- rounding digits cell
- allocation valid cell
- enabled question points cells
- normal evaluator/criterion weight and range cells

$$
AllocationTotal=BasePoints+SpecialPoints+\sum EnabledQuestionPoints
$$

`AllocationValid`はtotalがexact 100なら1、それ以外は0。

## 5. References contract

| Column | Name | Type |
|---:|---|---|
| A | `QuestionId` | literal string |
| B | `DisplayName` | literal string |
| C | `QuestionText` | literal string |
| D | `ModelId` | literal string (`auto`) |
| E | `ReferenceAnswer` | literal string / blank |
| F | `Status` | literal string |
| G | `GeneratedAtUtc` | invariant UTC string |

untrusted textは先頭文字に関係なくinline stringで保存する。formulaとして解釈しない。

## 6. Results contract

先頭は`SourceRow`。enabled questionの順、normal evaluator／criterionの順、special itemの順に列を生成する。

### 6.1 Normal criterion columns

既存v3の10列を維持する。

| Suffix | Type |
|---|---|
| `.Scorable` | literal 1/0 |
| `.AI_Raw` | literal number / blank |
| `.Override` | literal number / blank |
| `.Effective_Raw` | formula |
| `.Normalized` | formula 0〜100 / blank |
| `.Reason` | literal string |
| `.Evidence` | literal string |
| `.Evidence_Source` | literal string |
| `.Evidence_SourceColumn` | literal string |
| `.Status` | literal string |

normal evaluator末尾へ`.Evaluator_Score`を置く。

### 6.2 Question columns

| Suffix | Type | Empty normal answer |
|---|---|---|
| `.Answer_Present` | literal 1/0 | 0 |
| `.Question_Normalized` | formula 0〜100 / blank | blank |
| `.Question_Rate` | formula 0〜1 / blank | 0 |
| `.Question_Earned` | formula points / blank | 0 |

### 6.3 Special columns

各item:

- `.Special_AI_Raw`
- `.Special_Reason`
- `.Special_Evidence`
- `.Special_Evidence_Source`
- `.Special_Evidence_SourceColumn`
- `.Special_Status`

enabled special itemを1件以上持つquestionの末尾:

- `.Special_Question_Rate`

enabled special itemが0件のquestionにはこの列を生成せず、SpecialEarnedのquestion countへ含めない。`SpecialPoints=0`ではAIを呼ばずraw blank、status `NOT_RUN_ZERO_BUDGET`、question rate blankとする。root `Special_Earned`は0。

### 6.4 Similarity columns

- `.Similarity_AI_Raw`
- `.Similarity_Reason`
- `.Similarity_Status`
- `.Similarity_Penalty`

empty normal answerはraw literal 0、status `EMPTY`、penalty 0。reference／similarity technical failureはraw blank、penalty blank。

### 6.5 Row totals

- `Base_Points`
- `Special_Earned`
- `Final_Raw`
- `Final_Score`

## 7. Formula shapes

### 7.1 Normal effective and normalized

v3 formulaを維持する。empty時にcriterion chainはblankとし、question-levelで0へ変換する。

```text
EffectiveRaw:
=IF(ScorableCell<>1,"",IF(OverrideCell="",IF(ISNUMBER(AiRawCell),IF(AiRawCell<MinCell,"",IF(AiRawCell>MaxCell,"",AiRawCell)),""),IF(ISNUMBER(OverrideCell),IF(OverrideCell<MinCell,"",IF(OverrideCell>MaxCell,"",OverrideCell)),"")))

Normalized:
=IF(ISNUMBER(EffectiveRawCell),IFERROR(ROUND((EffectiveRawCell-MinCell)/(MaxCell-MinCell)*100,RoundingDigitsCell),""),"")
```

### 7.2 Question

```text
QuestionNormalized:
=IF(COUNT(EvaluatorScoreCells)=EnabledEvaluatorCount,IFERROR(ROUND(SUM(EvaluatorScore*EvaluatorWeight)/SUM(EvaluatorWeight),RoundingDigitsCell),""),"")

QuestionRate:
=IF(AnswerPresentCell=0,0,IF(ISNUMBER(QuestionNormalizedCell),QuestionNormalizedCell/100,""))

QuestionEarned:
=IF(ISNUMBER(QuestionRateCell),ROUND(QuestionRateCell*QuestionPointsCell,RoundingDigitsCell),"")
```

### 7.3 Special

```text
SpecialQuestionRate:
=IF(SpecialPointsCell=0,"",IF(COUNT(SpecialRawCells)=EnabledSpecialCount,ROUND(SUM(SpecialRawCells)/EnabledSpecialCount,RoundingDigitsCell),""))

SpecialEarned:
=IF(SpecialPointsCell=0,0,IF(COUNT(SpecialQuestionRateCells)=SpecialQuestionCount,ROUND(SpecialPointsCell*SUM(SpecialQuestionRateCells)/SpecialQuestionCount,RoundingDigitsCell),""))
```

`COUNT(...)=expected`はnumeric completeness判定であり、0は数値として数え、blankは数えない。
`SpecialQuestionRate`は`EnabledSpecialCount >= 1`のquestionにだけ生成し、除数0や空rangeを作らない。

### 7.4 Similarity and final

```text
SimilarityPenalty:
=IF(ISNUMBER(SimilarityRawCell),ROUND(QuestionPointsCell*SimilarityRawCell*SimilarityWeightCell,RoundingDigitsCell),"")

FinalRaw:
=IF(AllocationValidCell<>1,"",IF(COUNT(QuestionEarnedCells,SpecialEarnedCell,SimilarityPenaltyCells)=ExpectedCount,ROUND(BasePointsCell+SUM(QuestionEarnedCells)+SpecialEarnedCell-SUM(SimilarityPenaltyCells),RoundingDigitsCell),""))

FinalScore:
=IF(ISNUMBER(FinalRawCell),IF(FinalRawCell<0,0,IF(FinalRawCell>100,100,FinalRawCell)),"")
```

## 8. Formula safety

formulaはCoreのclosed ASTからだけ生成する。

- allowlist: `IF`, `IFERROR`, `ISNUMBER`, `COUNT`, `SUM`, `SUMPRODUCT`, `ROUND`
- operators: comparison、addition、subtraction、multiplication、division
- operands: app-owned verified cell/range、number、blank
- external reference、defined name、DDE、macro、raw user formulaを禁止
- length最大8,191、function arguments最大255、column最大16,384、row最大1,048,576、sheet name最大31
- target uniquenessとacyclic DAGを検査

新formulaは既存allowlist内で表現し、MIN／MAX／AVERAGE／COUNTIFを追加しない。

## 9. Cached values

formula cellへCore previewのcached decimalを保存する。次の各段階でExcelと同じ`ROUND`相当、`decimal`、`MidpointRounding.AwayFromZero`を使う。

1. normal criterion normalized
2. evaluator score
3. question normalized/rate/earned
4. special question rate/earned
5. similarity penalty
6. FinalRaw
7. FinalScore clamp

technical blankはnull cached valueとする。empty-zeroはnumeric 0 cached valueとする。

## 10. Final atomic commit

1. target directoryへCreateNew tempを作る。
2. inputをbyte-copyしてdisk flushする。
3. Config／References／Results／Run、formula、calculation propertiesを書く。
4. close後にread-only reopenする。
5. package、sheet、formula、references、cached values、original sheetsをvalidateする。
6. input SHA-256／size／last-write timeを再確認する。
7. final path absenceを再確認し、same-volume no-overwrite moveする。

cancel／failure時はfinal名を作らずpartialを保持する。rename critical section開始後のcancelはvalid finalまたはfinalなしの安全点まで遅延する。

## 11. Output confidentiality

partialとfinalはinput全sheet、個人情報、回答、Prompt、reference、AI reason/evidenceを含み得る。匿名化copyではなくinputと同等以上に機密である。

- target-local tempを使用する。
- OS directory permissionを広げない。
- contentをerror／logへ出さない。
- cleanup failureではpathだけをsafe UIへ表示する。

## 12. Office-independent evidence

required pathはExcel、Office、LibreOffice、COM automationを必要としない。formula AST、serialized formula、Core cached oracle、Open XML reopenで検証する。installed spreadsheetによる再計算はoptional evidenceとして別記録し、required PASSの代替にしない。
