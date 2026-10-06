# 敵対的レビューと修正記録 — 2026-09-05

## 対象・目的

- レビュー前のsource: `20c8121c2474a13f409c0d7f0fde9d4c41f74698`、製品`0.8.3`候補。公開済み`0.8.1`とは別。
- 目的: 標準.xlsxの元本を変更せず、選択したsheetと質問文行に忠実にmappingすること。手動配点を保ち、文字数・設問数・window resizeで編集操作を失わず、現在sourceのテスト／配布検証を正確に提示すること。
- 規範: `docs/requirements-definition.md` v4.4 §4.3、§6.3、AC-002、AC-005、AC-016、AC-022、AC-026。未実測のplatformやLive AIの成功を主張しない。
- UIと文書／証跡を並列に独立レビューした。同期処理の並列調査は通信エラーになったため、main側で実sourceを直接確認し、回帰テストを作成した。過去の推測を含むレビュー用メモは根拠に採用しない。
- 下表は修正前の問題を示す。行番号は編集で変わるため、fileとsymbol／節で位置を示す。修正中に追加したテストの不備や一時的な実装失敗を、修正前の別問題として水増ししない。

## 指摘一覧

| No. | 軸 | 重大度 | 指摘箇所 | 問題の説明 | 修正案 |
|-----|-----|--------|---------|-----------|--------|
| 1 | 目的適合性 | Critical | `src/StudyReportEvaluator.App/Views/QuantificationDesignView.axaml` — FormulaGuide | 固定部の全設問チップが無制限に伸びる。40問・256文字名・760×600で本文viewportが0 DIPとなり、AC-016の編集到達性を満たさない。 | 修正済み。固定部を短い式とBase/Special/Wに限定。説明は本文の展開欄、配点は各cardへ置く。末尾操作へ到達するテストを追加。 |
| 2 | 目的適合性 | Critical | `InputViewModel.cs` — RefreshHeaderAsync / ApplyLoadResult | 見出し再読込が初回読込と同じ候補再適用を行い、選択sheet・質問ID・手入力text・配点・評価設定を破棄する。テストでFinal→Originalを再現。 | 修正済み。metadata更新時はmappingを保持し、候補一覧更新に伴う一時的な空選択を書き戻さない。VMと実viewの両方で確認。 |
| 3 | 内容の妥当性 | Critical | `InputViewModel.cs` — CreateSuggestedQuestions / CreateDefaultQuestion / AddQuestion | 空／欠落headerに「評価する設問を入力してください。」等の非空代替文を保存し、質問文必須検証を回避できる。§4.3の空セル忠実性と不一致。 | 修正済み。raw headerまたは空を使い、REQUIREDで利用者の補完を求める。stale metadataから新規質問へ旧headerを写さないテストも追加。 |
| 4 | 目的適合性 | Major | `MainWindow.axaml` — ShellScrollViewer / Design DataTemplate | 固定式ガイドも外側scroll内にあり、window高をMaxHeightにしても実表示領域を越える。720 DIP高でscroll後のガイドY座標384→−64を再現。 | 修正済み。Designだけ外側縦scrollを無効化し、残余の有限高を本文へ渡す。実shellで縮小・scroll後の可視性を確認。 |
| 5 | 品質・運用性 | Major | `InputView.axaml` — WorksheetComboBox | 長いsheet名＋範囲＋状態を一行表示し、選択中値の折り返しや全文tooltipを指定していない。末尾だけ異なるsheetの識別を補助できない。 | 修正済み。有限幅にstretchし、折り返しと全文tooltipを指定。31文字名のsheet選択を検証。元画面で選択操作自体が不能だったとは断定しない。 |
| 6 | 品質・運用性 | Major | `InputView.axaml` — SupportingColumns CheckBox | 長いheaderをCheckBox.Contentへ直接渡し、折り返し／全文参照を明示していない。補助列の意味が読み切れない可能性がある。 | 修正済み。折り返すTextBlockと全文tooltipへ変更。仮想化項目を表示して256文字labelと選択結果を確認。 |
| 7 | 内容の妥当性 | Major | `QuantificationDesignView.axaml` — 丸め桁数の説明 | 「丸め桁数は表示用」と書くが、WeightedScoreCalculatorは正規化・加重平均・獲得点・減点・最終点などでRoundする。設定が計算結果に影響することを隠す説明。 | 修正済み。「計算の各段階と最終点に影響」に訂正。tooltipも同じ意味にし、UIテストで検査。計算ロジック自体は変更しない。 |
| 8 | 根拠性・不確実性管理 | Major | `DocumentationScreenshotTests.cs` — 通常実行 | 生成flag未指定時は既存PNGの存在・寸法しか検査せず、現行XAMLに対する位置アサーションを実行しない。古い画像で検証を通過できる。 | 修正済み。通常実行でも一時directoryへ現行UIを描画・検証する。repository画像へのcopyだけをopt-inとする。 |
| 9 | 品質・運用性 | Major | `DocumentationScreenshotTests.cs` — fixture IDs | productionのランダムIDを画像へ表示し、同じsourceでも画像02/06/07が変化していた。差分の原因判定と再生成の再現性を妨げる。 | 修正済み。合成definition/question/evaluator/criterion IDだけを固定し、既存の設計→入力同期経路で適用。2回生成した全7PNGの一致を検証。本番ID生成は維持。 |
| 10 | 整合性 | Major | `README.md`、`docs/getting-started.md`、`docs/features.md`、画像案内 | 公開版0.8.1の手順と、CHANGELOGでUnreleasedの設問text同期・新しい設計画面・画像が区別されていなかった。 | 修正済み。公開版の取得手順とUNRELEASED 0.8.3候補の操作・画像を明示的に分離。注記を契約テストへ追加。 |
| 11 | 根拠性・不確実性管理 | Major | `dev/docs/implementation-status.md`、`traceability.md`、`readme-claim-ledger.md` — 718件PASS | current PASSの対象source／実行記録が明記されず、今回変更にも旧結果が適用されるように読めた。 | 修正済み。718件を20c8121のbaselineへ限定し、今回の最終summaryとsource一致で別に判定する。 |
| 12 | 根拠性・不確実性管理 | Major | 同文書のrequired／skipped 0集計、opt-in smoke | Live AI・外部Excel・RealDataの未実行return／policy経路もPassedに含まれる。runnerのskipped 0は実処理実施の証拠ではない。 | 修正済み。runner件数と実処理statusを分離し、今回の3種をNOT_RUNと明記。既存のpolicyテストを不必要に削除しない。 |
| 13 | 根拠性・不確実性管理 | Major | `artifacts/test/final-recovery-20c8121/verify.ps1` — deterministic / sample | 終了コードだけでPASSを保存し、TRX欠落・対象0件・assembly誤りを検査しない。今回0件だったという指摘ではなく、false-passを許す検証構造。 | 修正済み。今回専用のfinal-verify.ps1でTRXの存在、期待件数、assembly、outcome、counter、SHA-256を確認。正常／空／失敗／件数不一致／別assemblyのguard試験を実施。旧記録は保存。 |
| 14 | 品質・運用性 | Major | 同verify.ps1 — 最終HEAD/clean/diff検査 | 最終検査がphase保存の外にあるため、その検査が失敗してもJSONは各工程exitCode=0のまま。会話再開時に完了か判定できない。 | 修正済み。全体RUNNING/FAILED/PASSEDと現在phase、source不変、終了時刻をfinallyで保存。dirty checkoutを実際に拒否しFAILED永続化を検証。 |
| 15 | 整合性 | Major | `docs/getting-started.md` — PowerShell 7前提 | 利用者へPowerShell 7を必要とする手順が、要求§13.2／詳細設計のengineering-only前提に反する。 | 修正済み。PowerShell 7の確認例を任意とし、Windows標準certutilの代替を案内。追加導入が不要であることをテスト。 |
| 16 | 整合性 | Minor | `dev/docs/README.md`、`version-management.md`、`implementation-status.md` — local anchors | 存在しないevidence節、旧章番号など計6箇所への参照。既存リンクテストはfragmentを除去して見逃していた。 | 修正済み。evidence節を設け、現行見出しへ更新。current Markdownのheading anchorを検証し、コード内の見出し風文字列は除外。 |
| 17 | 根拠性・不確実性管理 | Major | `dev/docs/detailed-design.md` §13.5 | macOS CIがRID publish/bundle structureを検査すると書くが、ci.ymlとMacOsPublishPackageTestsはstatic source／resolver契約の検証だけ。 | 修正済み。実際の範囲に限定し、bundle生成・署名・公証・実起動は未実行と明記。scope外のmacOS配布機構は追加しない。 |
| 18 | 整合性 | Major | `implementation-status.md` Release matrix／claim C-038 | 公開v0.8.1の記録と、matrix NOT_RUN／公開承認待ちという記述が同居し、対象候補を区別していない。 | 修正済み。公開0.8.1の履歴と0.8.3のlocal matrix検証を分離。今回0.8.3の公開完了とは主張しない。 |
| 19 | 整合性 | Minor | `dev/docs/detailed-design.md` §10.1 | pickerが存在しないSelectFileAsyncを呼ぶと記載。実装はSetFilePathAsync。 | 修正済み。実在するsymbolへ訂正。 |
| 20 | 品質・運用性 | Minor | `images/README.md` — Regeneration | 生成flagを設定したまま戻さず、同じshellの後続testも画像更新を継続してclean checkoutを汚し得る。失敗codeの検査もない。 | 修正済み。try/finallyで以前の値を復元し、dotnet終了codeを検査する例に変更。 |
| 21 | 整合性 | Minor | `dev/docs/README.md` — 履歴図 | v4.4の実装から、別文書でhistorical v3と明記する69e4b99／3f4227eのgateへ矢印がつながり、検証時系列が誤っている。 | 修正済み。旧gateをV3分岐へ移し、current sourceの証跡とは分離。 |

## サマリー

- 修正前: Critical **3件**、Major **14件**、Minor **4件**、計**21件**。
- 修正前合格判定: **FAIL**（Critical > 0）。
- 全Critical、全Major、全Minorへ上表の修正を適用。Majorの修正見送りはなし。
- 修正後のCritical判定: 再現した3件は回帰テストで解消。製品全体に未知の問題がないという意味ではない。最終受入は下記summaryとの照合が必要。

## 検証記録と適用限界

- `artifacts/test/adversarial-review/before.trx`: 追加した4再現ケースが修正前4/4失敗（上表No.1〜4）。
- `artifacts/test/adversarial-review/ui-final.trx`: 追加回帰を含むUI＋文書102/102成功。後から追加した版区別テストはこの102件には含めない。
- `artifacts/test/adversarial-review/ui-reviewed.trx`: 版区別テストを含むUI＋文書103/103成功。failed／skipped 0、assemblyとcounterを実TRXで再確認。
- `artifacts/test/adversarial-review/rejected-dirty-preflight/summary.json`: dirty sourceをpreflightで拒否し、phase実行0、全体FAILED、終了時刻を保存した負試験。
- `artifacts/test/adversarial-review/final-verify.ps1`: 今回専用の検証実行。製品やCIの新しい汎用frameworkにはせず、old runの証拠を上書きしない。ソースからの通常test手段は既存dotnet testのまま。
- 最終runの正本: `artifacts/test/adversarial-review/final/summary.json`。全体`PASSED`、対象source commit一致、開始／終了時のclean検査、各TRXのcount／outcome／assembly／SHA-256を確認する。未生成・RUNNING・FAILEDは未完了。
- 配布物は最終runと同一sourceのZIP・開発用MSIX・matrixを検証する。production trust、MSIX install、macOS native、Live AI、外部Excel再計算、実機DPIの追加測定は実施対象外であり、成功を捏造しない。
- PNGはHeadless＋Skiaの合成データ描画。Windowsの実画面／字体／実機200% DPI適合の認証ではない。
- `artifacts/`はGit対象外であり、公開checkoutにこれらの証跡が存在するとは保証しない。本記録とcommitされた回帰テストを恒久的な修正根拠とする。

## 変更の境界

本番への変更は入力VMと3つのXAMLのみ。schema、score calculation、CLI option、依存package、production project、汎用Factory／Strategyは追加・変更していない。試験は合成データを使い、元本の個人情報・回答・credentialをレビュー記録へ載せない。製品版は未公開0.8.3候補の修正として維持し、公開0.8.1のtag／assetを変更しない。
