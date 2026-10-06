# Custom evaluator ガイド

> [!IMPORTANT]
> **利用者向け正本は[`docs/custom-evaluator-guide.md`](../../docs/custom-evaluator-guide.md)へ移動しました。** 以下はD-01時点のcompatibility snapshotです。新しい説明、既知差分、実装出典は利用者向け正本を参照してください。

## 1. Knowledge と Custom の使い分け

| 種別 | 適した用途 | Prompt ownership |
|---|---|---|
| Knowledge (`KNOWLEDGE_COVERAGE`) | 知識ポイントが回答内で説明され、他概念と関係付けられ、具体的に適用されている程度の評価 | semantic instructionはapp-owned。利用者は知識ポイント、description、range、weightを編集 |
| Custom (`CUSTOM_PROMPT`) | 論理性、具体性、調査の深さ、文章品質、Prompt設計など、知識含有以外の観点 | 利用者が分析templateを編集。structured-output contractはapp-owned |

Knowledgeは単語数やkeyword一致だけの評価へ置換できません。別の分析方法が必要ならCustomを使います。どちらもAIへaggregateを要求せず、criterion別raw scoreとreason / evidence / sourceだけを受け取ります。

## 2. 任意のprimary / supporting columns

1つのQuestionは、利用者が選んだ任意の1列をprimary column、0件以上の列をsupporting columnsとして持ちます。レポート本文だけでなく、学生Prompt列もprimaryにできます。header名や列位置で列種別を固定しません。

- primaryが空白なら `EMPTY` とし、AIへdispatchせず、overrideとscore chainをblankにします。
- supportingが空白でもprimaryがあれば評価を続けます。
- 同じQuestion内ではprimaryとsupportingの重複、およびsupporting同士の重複を拒否します。
- Promptへ挿入するのは対象と同じ行の選択済みprimary / supportingだけです。他行、非選択列、file pathは含めません。

## 3. placeholder 契約

### 許可リスト（6個のみ）

| Placeholder | 展開値 |
|---|---|
| `{設問}` | Question text |
| `{回答}` | 選択した同じ行のprimary value |
| `{補助情報}` | 選択した同じ行のsupporting valuesとstable source column IDs |
| `{評価項目}` | enabled criteriaのID、display name、description、criterion effective range |
| `{最小点}` | evaluator default rangeのminimum |
| `{最大点}` | evaluator default rangeのmaximum |

Custom templateは空白にできず、`{回答}` と `{評価項目}` をそれぞれ1回以上含める必要があります。他の4個は任意です。

literalの `{` と `}` は、それぞれ `{{` と `}}` でescapeします。例えば `{{"形式":"例"}}` はplaceholderではなく `{"形式":"例"}` というliteral textになります。片側だけのbrace、未知placeholder、nested opening brace、未閉鎖placeholderは実行前に拒否されます。

placeholderの展開はsingle-passです。回答、補助情報、評価項目へ `{回答}` や `{{` に似た文字列が含まれていても、挿入値は **opaque** として扱い、再走査しません。したがって、入力値が別placeholderへ再展開されることはありません。

## 4. range の意味

evaluatorは必ず `evaluator default range` を持ちます。criterionは独自rangeを省略でき、その場合は親evaluatorのrangeを使います。criterion独自rangeがある場合、それが `criterion effective range` です。

- `{最小点}` / `{最大点}` は、criterion独自rangeの有無に関係なく常にevaluator default rangeへ展開します。
- `{評価項目}` はcriterionごとのeffective minimum / maximumを含みます。
- AI raw、override、result validation、normalizationはcriterion effective rangeを使います。
- minimumとmaximumは有限数で `minimum < maximum`、rounding digitsは0〜6です。

この区別により、template全体の既定尺度を説明しつつ、criterionごとに異なる採点rangeを安全に指定できます。

## 5. criteria とweights

各enabled evaluatorには1件以上のenabled criterionが必要です。criterionにはstable ID、display name、description、effective range、正のweightを設定します。questionとevaluatorにも正のweightがあります。

weightを手作業で100へ揃える必要はありません。criterion / evaluator / questionの各階層で `個別weight / weight合計` を使って正規化し、UIへ実効percentageを表示します。weight 0を無効化に使わず、不要なnodeはdisableまたは削除します。AIはweight、evaluator score、question score、overall score、合否を返しません。これらはConfig参照のExcel formulaだけが計算します。

## 6. 安全なtemplate例

### 最小template

```text
次の回答を、指定された評価項目ごとに分析してください。

### 回答
{回答}

### 評価項目
{評価項目}
```

### supporting informationとliteral braceを使うtemplate

```text
設問と同じ行の回答・補助情報だけを使い、根拠のない推測を避けてください。
説明用のliteral object例: {{"source":"selected-row-only"}}

### 設問
{設問}

### 回答
{回答}

### 補助情報
{補助情報}

### 評価項目
{評価項目}

親evaluatorの既定範囲: {最小点} から {最大点}
```

templateへJSON schemaやtool call手順を追加する必要はありません。appがrender後に `submit_quantification` 用のclosed structured-output contract、期待evaluator / criterion IDs、許可source IDsを付加します。

## 7. validation errors

| Code | 原因 | 修正 |
|---|---|---|
| `TEMPLATE_REQUIRED` | templateがnull、空、空白だけ | `{回答}` と `{評価項目}` を含む本文を入力 |
| `ANSWER_PLACEHOLDER_REQUIRED` | `{回答}` がない | 評価対象を挿入する位置へ追加 |
| `CRITERIA_PLACEHOLDER_REQUIRED` | `{評価項目}` がない | criterion一覧を挿入する位置へ追加 |
| `UNKNOWN_PLACEHOLDER` | 許可リスト外または空のplaceholder | 6個の許可placeholderのいずれかへ修正 |
| `UNCLOSED_PLACEHOLDER` | opening braceに対応するclosing braceがない | `}` を補うかliteral braceとしてescape |
| `MALFORMED_PLACEHOLDER` | placeholder内にnested opening braceがある | nested braceを取り除く |
| `UNMATCHED_CLOSING_BRACE` | 単独の `}` がある | literalなら `}}` にする |

これらはPromptを安全かつ決定的にrenderするための技術的validationです。errorには回答本文やPrompt本文をechoしません。invalid templateはdispatch前に止めますが、教育倫理warningを処理条件へ変えるものではありません。

## 8. AI出力と人手確認

AIへaggregateを要求しないでください。required resultは、すべてのexpected criterionについて1件ずつのraw score、短いreason、同じ行の連続substring evidence、evidence source、stable source column IDです。partial、duplicate、unknown criterion、range外、出所不一致はpayload全体を不正として扱い、部分採用・clamp・0点化をしません。

> AIによる定量値には誤りや偏りが含まれる可能性があります。利用目的に応じて結果を確認してください。

このwarningはpersistentかつ **nonblocking** です。結果確認とrange内overrideは利用できますが、mandatory human review、承認checkbox、倫理gate、合否gateはありません。warningへ応答しなくてもrun、cancel、formula計算、exportを実行できます。
