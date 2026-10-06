# ADR-0012: 絶対配点・類似度減点・checkpoint再開・Windows/macOS配布

| 項目 | 内容 |
|---|---|
| 状態 | **承認済み** |
| 決定日 | 2026-09-01 |
| 要求正本 | `docs/requirements-definition.md` v4.0 |
| Supersedes | ADR-0011の相対weight全体点、blank empty、後段手動export、Windows限定、PATH CLIの各決定 |
| Carries forward | 単一`.xlsx`、入力不変、selected-row-only、closed AI result、Excel formula ownership、2 production project、nonblocking warning |

## Context

ADR-0011は、任意数のKnowledge／Custom evaluatorから0〜100の相対加重平均を作る構成を確立した。現行実装はその契約を満たしているが、要求v4.0では次が追加・変更された。

1. 全学生へ既定60点のbaseを加え、残りを通常設問と固有設定へ絶対点で配分する。
2. Prompt能力等の設問固有項目へ独立した総配点を与える。
3. 設問を`auto`へ投入した参照回答を設問ごとに1件保持し、学生回答との類似度に応じて減点する。
4. 空回答は0点、AI技術障害はblankと区別する。
5. processing中から別Excelへcheckpointし、application再起動後に再開する。
6. native picker、Prompt text fileによるGUI事前入力、Windows/macOS配布を提供する。

これらはExcel、workflow、配布の中核契約を変更するため、ADR-0011への追記ではなく新しい決定として記録する。

## Decision

### 1. 既存の2-project構成を維持する

production projectは次の2件だけを維持する。

- `StudyReportEvaluator.Core`: domain、validation、Prompt rendering、score preview、formula AST
- `StudyReportEvaluator.App`: Avalonia、Open XML、Copilot SDK、workflow、checkpoint、composition

checkpoint、CLI argument、platform packagingのためにApplication／Infrastructure等のprojectを追加しない。必要な責務は既存folder内の小さな型へ分ける。

### 2. 相対question weightを絶対Pointsへ置き換える

rootに次を追加する。

- `BasePoints` default 60
- `SpecialPoints` default 0
- `SimilarityPenaltyWeight` default 0.1

questionの旧`Weight`は`Points`へ置き換え、次をrequireする。

$$
BasePoints+SpecialPoints+\sum Question.Points=100
$$

criterion／evaluatorの内部weightは、通常回答を0〜100へ定量化するため維持する。question階層だけを絶対配点へ変更する。

初期化と明示的な「均等配分」だけがquestion pointsを自動計算する。追加、削除、有効化、base／special変更で手動値を黙って変更しない。

### 3. 固有評価をquestion直下の単純なcollectionとして追加する

`SpecialEvaluationDefinition`をquestion直下へ追加する。各itemはsource column、supporting columns、Custom Prompt、enabledを持ち、AIは0〜1の単一scoreを返す。

初版はspecial item weightを持たない。同一question内のenabled special itemを等分平均し、次にenabled special itemを1件以上持つenabled questionだけを等分平均する。disabled itemだけを持つquestionは分母へ含めない。`SpecialPoints > 0`ではenabled special itemを全体で1件以上必須とし、Designとrun preflightで`SPECIAL_ITEMS_REQUIRED`として検証する。既存`EvaluatorDefinition`へsource overrideやspecial flagを詰め込まず、通常evaluatorの意味を維持する。

この追加型は要求された異なるsource列と0〜1単一scoreを表すために必要であり、汎用plugin typeは導入しない。

### 4. 参照回答と類似度を通常評価から分離する

次の明確なAI operationを持つ。

1. `ReferenceAnswer`: question text → generated answer。`auto`、1 question/runで1回。
2. `NormalQuantification`: student primary/supporting → criterion raw。利用者選択model。
3. `SpecialQuantification`: special primary/supporting → 0〜1 score。利用者選択model。
4. `SimilarityQuantification`: student answer + stored reference → 0〜1 similarity。`auto`。

operationごとにclosed result schemaとtoolを持つ。汎用「任意JSON AI operation」層は作らない。

`auto`が利用可能model一覧にない場合はrun前に停止し、他modelへfallbackしない。再開では保存済みreferenceを再利用し、再生成しない。

全enabled questionのreferenceは学生行処理より前に生成する。各referenceをvalidated checkpointへ保存してから次questionへ進み、全referenceが成功または技術statusで確定した後だけ学生行処理を開始する。referenceだけを含むcheckpointも有効な再開地点であり、未生成questionだけを続行する。

### 5. empty-zeroとtechnical-blankを分離する

- empty normal answer: normal rate 0、question earned 0、similarity 0。AI call 0。
- empty special input: special score 0。AI call 0。
- invalid／timeout／network／auth／cleanup等のtechnical failure: 対象score blank。
- 必須scoreがblankならFinalRaw／FinalScoreもblank。

既存の「emptyをblankにする」ADR-0011契約はこの点でsupersedeする。failureを0へ変換しない原則は維持する。

| Operation | Empty input | Technical failure | Direct dependent |
|---|---|---|---|
| Normal | rate 0 | rate blank | QuestionEarned 0 / blank |
| Reference | 該当なし | reference blank | SimilarityとFinal score blank |
| Special | score 0 | score blank | SpecialQuestion／SpecialEarned 0寄与 / blank |
| Similarity | score 0 | score blank | SimilarityPenalty 0 / blank |

1件のtechnical blankは、当該formulaの全ancestorへblankを伝播する。他のliteral resultとstatusは失わず、部分的な監査を可能にする。

### 6. Excelが最終配点を所有する

AIはraw resultだけを返す。Excel formulaは次を計算する。

$$
QuestionEarned_q=QuestionPoints_q\times QuestionRate_q
$$

$$
SpecialEarned=SpecialPoints\times average(SpecialQuestionRate)
$$

$$
SimilarityPenalty_q=QuestionPoints_q\times Similarity_q\times SimilarityPenaltyWeight
$$

$$
FinalRaw=BasePoints+\sum QuestionEarned+SpecialEarned-\sum SimilarityPenalty
$$

`FinalScore`はIF式で0〜100へclampする。`FinalRaw`を監査用に保持する。

Configの配点合計が100でない場合はformula結果をblankにし、設定validation列へ不一致を示す。range、points、係数、roundingをformulaへliteral埋込みしない。

### 7. processing開始時にoutput identityを確定する

run開始時に入力隣接の`result` directoryと次のidentityを確定する。

- final: `eval-yyyyMMdd-HHmm[-NN].xlsx`
- checkpoint: `eval-yyyyMMdd-HHmm[-NN].partial.xlsx`

既存fileを上書きしない。利用者は開始前にdirectoryを変更できる。processing完了後に別途export操作を要求せず、validated finalを自動commitして結果stepへ進む。

### 8. checkpointの正本はpartial workbookとする

checkpointは元本のbyte-copyへ`Quantification_Checkpoint` sheetを追加した標準`.xlsx`とする。別database、event store、cloud stateを追加しない。

保存時点は次である。

- 各reference生成後
- 学生1行に属する全operation完了後

学生行の途中結果はdurable completeとみなさない。resumeは完了行だけをskipし、途中行を再実行する。

checkpoint updateはtarget-local tempへ完全な次checkpointを作り、close／reopen／schema・snapshot hash・packageをvalidate後にatomic replaceする。replace前にprocessが終了した場合、既存partialは変更されず、未完成tempはresume sourceにしない。replace後は新partialだけが正本となる。正本partial自体が破損した場合は前状態を推測せず再開を拒否する。

### 9. Copilot session persistenceをjob resumeに使用しない

SDK session persistenceは会話contextの継続機能であり、input identity、definition、Excel、完了行を保証しない。本appは各attemptのephemeral sessionを維持し、job resumeをpartial workbookだけで行う。

resumeはinput、definition、model、app schema、CLI runtime identityを検証する。保存済みvalidated raw resultだけを再利用する。

### 10. Prompt file起動はGUI prefillだけとする

command lineは`--input`と反復可能な`--prompt`だけを受け付ける。Prompt textを指定順で一覧表示し、利用者が選択中のCustom evaluatorまたはspecial itemへbuttonでcopyする。

次は行わない。

- filenameによる自動mapping
- command lineからのAI自動実行
- custom protocol／VS Code extension／daemon

### 11. native pickerはAvalonia StorageProviderを使用する

入力file pickerはAvaloniaの`IStorageProvider.OpenFilePickerAsync`を使う。pickerはViewのplatform service境界からViewModelへ選択pathを渡し、既存のread-only loaderを再利用する。platform別picker abstraction frameworkは追加しない。

### 12. end-user packageはself-contained + bundled CLIとする

.NET Runtime／SDKを一般利用者端末へinstallしない。各RIDのself-contained appと、固定SDKに対応するbundled Copilot CLI runtimeをpackageする。

appはpackage-relative absolute CLI pathを優先し、PATH上の別CLIへ黙ってfallbackしない。runtime identityをrunとcheckpointへ記録する。

### 13. platform配布

> **SUPERSEDED:** この節のplatform配布は[ADR-0013](0013-windows-only-public-release.md)がsupersedeした。Windows 11 x64以外の配布は行わない。開発用 MSIX と macOS の基盤は 2026-10-06 に廃止した。以下は履歴として残す。

- Windows: `win-x64`、user-local install、admin不要。
- macOS: `osx-arm64`と`osx-x64`を別packageとし、universal binaryは作らない。
- macOS release: Developer ID署名、notary submit、staple、verificationが全部成功した場合だけpublish可能。

Apple credentialとmacOS runnerがない環境では、package layoutとfail-closed scriptまで検証し、署名済み／notarized／対応済みと称しない。repositoryのrequired成果物は署名・notarizationを実行できるpipelineと検証gateであり、実際のrelease artifactは外部前提が揃ったrunでのみ作る。未実行release gateは`NOT_RUN_EXTERNAL_PREREQUISITE`として残す。

### 14. warningは指定文言へ置換し、nonblockingを維持する

warningは要求v4.0の文面をexact表示する。ADR-0011のnon-modal、no checkbox、no state、no processing dependencyを維持する。

## Rejected alternatives

| Alternative | Rejection reason |
|---|---|
| questionの相対weightを残してbaseを後付け | 20点／30点という絶対配点を直接監査できない |
| specialを通常evaluatorのflagだけで表す | special固有source列と0〜1単一scoreの意味が混在する |
| special itemごとのweight | 要求がなく、初版設定を過剰に増やす |
| referenceを学生ごとに再生成 | 同一基準で比較できず、call数と揺らぎが増える |
| 文字列距離をAI similarityのfallbackにする | 要求されたPrompt評価と異なる値を黙って混在させる |
| technical failureを0へ変換 | 学生の無回答とsystem障害を混同する |
| JSON sidecarだけをcheckpointにする | processing中から別Excelへ書く要求と一致しない |
| SDK persistent sessionをresume正本にする | workbook、definition、completed rowの整合性を保証しない |
| SQLite／cloud database | 単一利用者local desktopの必要範囲を超える |
| auto-run CLI | 警告と設定を利用者が確認して実行するworkflowに反する |
| framework-dependent package + runtime auto-install | end-user端末を変更し、self-containedより複雑になる |
| macOS universal binary | architecture別packageで満たせ、merge工程が不要 |
| Windows PowerShell 5.1 installer | repository運用規約と安全なPowerShell 7実行に反する |

## Consequences

### Positive

- 採点内訳がbase、設問、固有、類似度減点としてExcel上で直接監査できる。
- emptyとsystem failureが混同されない。
- 参照回答が学生間で統一される。
- 長時間runを最初からやり直さず再開できる。
- end-userは.NET SDKを導入せずWindows/macOSで起動できる。
- Promptをplain textで管理し、UIへ安全に取り込める。

### Trade-offs

- reference、special、similarityによりAI operation数とoutput列数が増える。
- rowごとのfull workbook checkpointはI/Oを増やすが、durabilityと単純な復旧契約を優先する。
- similarityは教育的不正を証明せず、利用者の判断が必要である。
- macOS releaseは外部credentialとmacOS runnerなしには完了できない。
- v3.0 output／definitionとの自動migrationは提供しない。

## Compatibility

- v3.0 output workbookは履歴成果物として開けるが、v4.0 checkpointとしてresumeしない。
- v3.0 reusable definition storeは存在しないためmigration機能を追加しない。
- `Quantification_Config`のschema versionを更新し、v3/v4を明確に区別する。
- historical ADR-0001〜0011とpreflight recordsは変更しない。

## Approval record

| 項目 | 値 |
|---|---|
| Approver | 本repositoryの要求所有者 |
| Source | 2026-09-01のアプリケーション要件、および後続のdefault採用・全task実行指示 |
| Approved intent | base／question／special配点、reference similarity、Excel formula、checkpoint resume、Prompt起動、Windows/macOS配布 |
| Identity semantics | repository設計決定。組織の法務・教育・security承認または電子署名ではない |

## Result

**APPROVED.** 要求v4.0を正本とし、本ADRに基づく詳細設計、実装計画、traceability、production code、tests、delivery documentsを更新する。
