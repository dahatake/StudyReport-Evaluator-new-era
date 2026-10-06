# ADR-0011: 動的Prompt定量化・Excel数式集計

| 項目 | 内容 |
|---|---|
| 状態 | **承認済み** |
| 決定日 | 2026-09-01 |
| 要求正本 | `docs/requirements-definition.md` v3.0 |
| 実装計画 | `dev/docs/archive/work/20260831-implementation-plan.md` v4.0 |
| 対象 | 定量化model、Prompt、score、Excel出力、warning、実装境界 |
| Supersedes | ADR-0010の固定的preset／candidate-review評価境界とv2.0 GATE-0適用 |
| Carries forward | 単一`.xlsx`入力、Windows local初版、既存Copilot CLI login、入力不変、最小capability |

## Context

要求v2.0と計画v3.0は、単一Excel入力とWindows local初版へscopeを縮小し、実装可能なGATE-0を確立した。一方、評価modelはレポート候補点とPrompt候補点を中心とする固定的な構成であり、AI候補を人が採用または上書きするまで確認済み点を空欄にする設計だった。集計をExcel数式で行う契約もなかった。

要求所有者は2026-09-01に、次の目的へ変更するよう指示した。

1. レポート結果Excelの選択内容をPromptで定量化する。
2. 定量化結果を、アプリで設定した重みに基づくExcel数式で計算する。
3. 結果は入力を変更せず別Excel fileへ出力する。
4. 知識問題は指定知識の意味内容が回答へ含まれる程度を評価する。
5. 知識以外は利用者が分析Promptを入力する。
6. 質問、評価方法、評価項目を動的に増減する。
7. 教育倫理上の注意は画面表示だけとし、処理を制限しない。

この変更はADR-0010の単一入力・local platform決定を維持しつつ、評価とUIの中核契約を置き換える。

## Decision

### 1. Dynamic definition

定量化定義は次のordered hierarchyとする。

`QuantificationDefinition → QuestionDefinition → EvaluatorDefinition → CriterionDefinition`

- 各階層はstable ID、display name、enabled stateを持つ。
- question、evaluator、criterionは追加、複製、並べ替え、無効化、削除できる。
- questionは任意の主回答列1件と補助列0件以上を持つ。
- 主回答列はレポート本文へ固定せず、学生Prompt列を含む任意の選択列を使用できる。
- 実行可能snapshotには各階層で1件以上のenabled childを要求する。
- 編集中draftは一時的にこの最小数を満たさなくても保存・修正できるが、runとoutputは具体的なtechnical field errorで停止する。
- run開始時にimmutable canonical snapshotとSHA-256を作り、run中のdraft変更は次runだけへ反映する。

### 2. Evaluator types

初版のevaluator typeは次の2種類だけとする。

- `KNOWLEDGE_COVERAGE`: 利用者が知識ポイントを入力し、app-owned instructionが単語一致ではなく説明、関係、適用のsemantic coverageを評価する。
- `CUSTOM_PROMPT`: 利用者が分析Promptと評価項目を入力する。

Knowledgeのsemantic coreと全type共通のstructured-output instructionは利用者templateの外側へappが付加し、削除できない。Custom templateは許可済み6 placeholderだけを使い、挿入値を再展開しない。

### 3. AI result boundary

AIは1行×1質問×1evaluatorについて、enabled criterionごとの次だけを返す。

- raw score
- short reason
- evidence
- evidence source kind
- same-row stable source column ID

AIはevaluator、question、overallのaggregate、weight、合否を返さない。criterion集合、ID、有限range、evidence source、同一行の連続substringをclosed validationし、partial resultを採用しない。不正応答を0へ変換しない。

### 4. Excel formula ownership

アプリは入力workbookのbyte-copyへ次のsheetを追加する。

- `Quantification_Config`
- `Quantification_Results`
- `Quantification_Run`

AI raw、override、Scorable、reason、evidence、source、statusはliteral cellとする。effective raw、normalized criterion、evaluator、question、overallはExcel formula cellとする。range、weight、roundingはConfig cellを参照し、AIに集計させない。

重みはcriterion、evaluator、questionの3階層で設定し、各親内のweight合計で正規化する。normalized、evaluator、question、overallの各階層で`ROUND`し、親は丸め済みchild score cellを入力にする。

### 5. Blank and override semantics

- 空の主回答は`Scorable=0`とし、AIへ送らず、overrideを許可せず、全依存scoreをblankにする。
- 主回答が非空なら、range内の有限numeric overrideをAI rawより優先する。
- overrideが空なら、妥当なAI rawを確認待ちなく使う。
- 主回答が非空でも、非空の非numeric／range外overrideはblankとし、AI rawへfallbackしない。
- AI失敗またはblankでも、有効overrideがあればscoreを補完できる。
- blank childが1件でもあれば、enabled parent aggregateもblankにする。

これは数値整合性contractであり、人手確認gateではない。

### 6. Warning semantics

教育倫理warningは入力画面と結果画面のpersistent non-modal bannerとする。

- definition、run status、score、weightへ保存しない。
- checkbox、同意、承認、role、期限、dismiss操作を要求しない。
- mapping、AI実行、cancel、override、formula計算、出力をblockしない。
- warningへfocusまたは操作しなくても全workflowを完了できる。

file、definition、AI応答、数値、securityの技術validationは引き続きblockingであり、warningとは分離する。

### 7. Office-independent calculation evidence

Open XML SDKはformulaを計算しないため、Coreが同じhierarchyを`decimal`と`MidpointRounding.AwayFromZero`で独立計算し、formula cellのcached valueとapp previewへ使う。

- required runtime/testにExcel、Office、LibreOffice、COM automationを要求しない。
- workbookはautomatic／force full calculation／full calculation on loadへ設定する。
- required testはformula AST、参照、DAG、golden formula、cached value、独立手計算oracleで判定する。
- installed spreadsheetでの再計算smokeはadvisory optional evidenceとし、required gateを代替しない。

### 8. Architecture

初版productionは次の2 projectだけとする。

- `StudyReportEvaluator.Core`: BCL only。domain、snapshot、Prompt、validation、score preview、formula AST。
- `StudyReportEvaluator.App`: Avalonia、Open XML、Copilot adapter、workflow、composition root。

test projectもCore/Appの2つとする。Application、Infrastructure、Platform、Workbooks等の追加projectは作らない。

### 9. Output safety

- 入力はread-onlyで開き、直接変更しない。
- target directory内の一意tempへbyte-copy、write、flush、close、reopen、validateする。
- 完成fileの生成は利用者が結果・出力画面で明示的に開始し、自動exportしない。この開始操作はscoreの確認、承認、同意を意味せず、warning操作やoverride入力を前提にしない。
- final rename直前に入力のSHA-256、size、last-write timeを厳密再確認する。
- same-volume atomic renameだけで完成名を作る。
- cancel／failure時はvalid finalまたはfinalなしのどちらかにする。
- untrusted textはliteral string cellへ保存し、formulaへ連結しない。

## Supersession semantics

- 要求v3.0、計画v4.0、本ADRをcurrent scopeへ適用する。
- ADR-0010は、単一Excel入力、既存CLI login、Windows 11 x64 local初版、未実測claim禁止の根拠として有効である。
- ADR-0010の固定`REPORT_ANSWER`中心mapping、built-in候補点preset、人が採用するまで計算しない評価境界、送信前確認を処理gateにする部分は本ADRによりsupersedeする。
- ADR-0001〜0010と旧baseline／map／traceability／gate-resultは履歴として保持し、新GATE-0判定へ使用しない。
- mandatory human review、教育fairness threshold、institutional approval enforcementを初版へ含めないことは、それらの存在、承認、妥当性を主張する意味ではない。

## Consequences

### Positive

- 任意列をKnowledgeまたはCustom Promptで定量化できる。
- 質問／評価方法／評価項目を授業や分析目的に合わせて構成できる。
- AIはraw criterion値だけに限定され、weightとaggregateは監査可能なExcel式になる。
- AI失敗、空回答、不正overrideを0点へ誤変換しない。
- warningは利用者指示どおり情報表示に限定され、workflowを妨げない。
- Office未導入環境でもbuild、test、preview、output validationを行える。

### Trade-offs

- 数式列とConfig参照により出力列数とformula長が増え、実行前capacity checkが必要になる。
- AI rawを必須人手確認なしで計算へ使うため、出力は教育的妥当性や最終成績の正しさを保証しない。
- cached valueはappによる再現計算であり、外部spreadsheet engineを実行した証明ではない。
- Custom Promptの品質とcriterion設計は利用者に依存する。
- unsigned Windows local package、単一workbook、既存CLI loginという初版制約は残る。

## Rejected alternatives

| Alternative | Rejection reason |
|---|---|
| 固定2質問／固定2評価方法 | 動的増減という要求に反する |
| AIにaggregateを返させる | app設定weightとExcel式による計算要求に反する |
| override必須／人手承認後だけ計算 | warning-only、処理制限なしという要求に反する |
| 空回答を0点として集計 | 未提出／未評価と0点を混同する |
| 不正override時にAI rawへfallback | 入力された不正値を黙って無視し、結果の意味が不明確になる |
| Excel COMでrequired再計算 | Office install不要とtest再現性に反する |
| 追加projectによる過度な分割 | 初版規模に対してdependencyとgateを増やす |

## Approval record

| 項目 | 値 |
|---|---|
| Approver | 本セッションの要求所有者 |
| Approval source | 2026-09-01の明示Prompt |
| Approved intent | Excel入力を動的Promptで定量化し、app設定weightをExcel式で計算して別fileへ出力する。倫理warningは表示だけとする |
| Identity semantics | セッション要求の記録であり、組織電子署名、法務・教育・security承認とは称しない |

## Result

**APPROVED.** 要求v3.0／計画v4.0のrebaseline、37 task/gate map、15 AC traceability、新GATE-0を本決定に基づき作成する。
