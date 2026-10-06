# P1 disposition — initial release

> [!WARNING]
> **HISTORICAL / SUPERSEDED:** requirements v1.2のP1候補です。ここで`IMPLEMENT`とされたrepeated evaluation、named ranges、review copy等は現行requirements v3.0の実装済みfeatureではありません。現行scopeは[要求定義書](../../../docs/requirements-definition.md)と[ADR-0011](../adr/0011-dynamic-quantification-excel-formulas.md)を参照してください。

| 項目 | 内容 |
|---|---|
| 状態 | **既定案承認済み・G-09 完了** |
| 対象決定 | DEC-22 |
| 対象要求 | FR-015、FR-028、FR-038、FR-058、FR-065、FR-076、FR-092、FR-106、NFR-PRI-006、NFR-ACC-005 |
| 結論 | **全10件を`IMPLEMENT`。`DEFER` 0件** |
| 基準 | `docs/requirements-definition.md` v1.2 第2節、`dev/docs/archive/work/20260831-implementation-plan.md` v2.1 第19節 |
| 記録日 | 2026-08-31 |

## Decision semantics

P1は初回正式版で原則実装するSHOULDである。利用者が「全てのタスク」「実装前decision gateの既定案」を承認し、DEC-22の既定案は「原則実装。延期は個別承認」であるため、10件を個別に`IMPLEMENT`とする。

このdecisionは実装済み・試験済みを意味しない。各P1 taskはGATE-0後、表の依存位置で初めて開始でき、列挙したtest/gateがPASSするまでfeatureを完成扱いにしない。実装失敗や日程都合で`DEFER`へ暗黙変更せず、要件第2節に従う個別の理由、影響、代替統制、再検討期限、リリース責任者の正式承認を追加する。

ownerは現時点で個人を捏造せず、計画上のaccountable roleとして記録する。GATE-0前に実在担当者へ割り当てられないtaskは`BLOCKED`であり、role名だけを人の承認署名とみなさない。

## Disposition summary

| Task | Requirement | Decision | Accountable owner role | 実装期限 |
|---|---|---|---|---|
| P1-01 | FR-015 | IMPLEMENT | Mapping/Platform state責任者 | `UI-04`開始前 |
| P1-02 | FR-028 | IMPLEMENT | Mapping責任者 | `L-01`開始前 |
| P1-03 | FR-038 | IMPLEMENT | Prompt/Privacy責任者 | `L-04`開始前 |
| P1-04 | FR-058 | IMPLEMENT | Run/Validation責任者 | `UI-06`開始前 |
| P1-05 | FR-065 | IMPLEMENT | Policy/Privacy責任者 | `A-14`、`UI-01`、`UI-12`開始前 |
| P1-06 | FR-076 | IMPLEMENT | Education validation責任者 | `UI-06`開始前 |
| P1-07 | FR-092 | IMPLEMENT | Workbook/Formula責任者 | `O-05`開始前 |
| P1-08 | FR-106 | IMPLEMENT | Workbook/Review privacy責任者 | `UI-10`開始前 |
| P1-09 | NFR-PRI-006 | IMPLEMENT | Platform/Privacy責任者 | `UI-03`、`UI-12`開始前 |
| P1-10 | NFR-ACC-005 | IMPLEMENT | Accessibility/QA責任者 | `D-07`開始前 |

calendar dateは根拠がないため捏造せず、依存task開始前を拘束力のある期限とする。対応するdeadlineを越えた場合、後続taskを開始せず`P1_DISPOSITION_VIOLATION`としてrelease blockerにする。

## P1-01 — Mapping profile

| 項目 | 内容 |
|---|---|
| 要求 | FR-015 |
| Decision | IMPLEMENT |
| Owner | Mapping/Platform state責任者 |
| 理由 | 同じ帳票系列の反復設定を減らせるが、file hashや位置だけの再利用は列ずれを見逃すため、見出しとquestion IDを結合した再確認付きprofileとして実装する |
| 影響 | local stateとmigration/expiry/削除UIが増える。誤適用時は別列を送信するprivacy riskがある |
| 暫定統制 | 完成までは毎回手動mappingと明示確認を要求し、profile save/reuse UIを表示しない |
| 実装/試験 | `P1-01`: `Core/Mapping/MappingProfile.cs`; `Platform/State/MappingProfileStore.cs`; `Core.Tests/Mapping/MappingProfileTests.cs`; `Platform.Tests/State/MappingProfileStoreTests.cs`。profileはschema/version、question ID、見出し、roleだけを持ち、回答本文、sample value、path、tokenを保存しない。差分・重複・欠落時は自動適用せず再確認 |
| 期限/gate | `L-01`後、`UI-04`前。`UI-04`がsave/select/diff/reconfirmの唯一のUI owner。Core/Platform test、profile round-trip、tamper/version mismatch、本文/token canary 0 |
| 承認 | DEC-22既定案のセッション承認。実装受入は未完了 |

## P1-02 — Forms management-column suggestions

| 項目 | 内容 |
|---|---|
| 要求 | FR-028 |
| Decision | IMPLEMENT |
| Owner | Mapping責任者 |
| 理由 | Microsoft Formsの管理列を候補提示して初期設定を助ける一方、見出しheuristicを決定権にしない |
| 影響 | false positive/negativeがあり得る。Google Forms、local/manual workbookのmappingを妨げてはならない |
| 暫定統制 | 完成までは管理列を含め全列を`UI-04`で手動設定し、推測表示を行わない。この手動経路はP0の恒久fallbackであり、Forms候補機能へ依存しない |
| 実装/試験 | `P1-02`: `Core/Mapping/FormsManagementColumnSuggester.cs`; `Core.Tests/Mapping/FormsManagementColumnSuggesterTests.cs`。column identity、suggested management role、deterministic match codeの候補だけを返し、score/confidenceを捏造せずmappingを変更しない。locale、重複/空見出し、Google/手作業反例を試験 |
| 期限/gate | `L-01`前。`UI-04`は候補と利用者決定を別状態で表示 |
| 承認 | DEC-22既定案のセッション承認。実装受入は未完了 |

## P1-03 — Few-shot example set

| 項目 | 内容 |
|---|---|
| 要求 | FR-038 |
| Decision | IMPLEMENT |
| Owner | Prompt/Privacy責任者 |
| 理由 | anchorを具体化できるが、実回答の無断保存・再送を避けるclosed example setが必要 |
| 影響 | request/context budgetと費用を消費し、例の順序・内容が評価結果へ影響する。変更時はPrompt hashと校正を無効化する |
| 暫定統制 | 完成まではfew-shotなし。合成例も送信しない。実在学生回答を例として保存しない |
| 実装/試験 | `P1-03`: `Core/Prompting/FewShotExampleSet.cs`; `Core.Tests/Prompting/FewShotExampleSetTests.cs`。既定は明示`synthetic=true`の合成例だけ。実回答由来は署名policyのpurpose/owner/expiryに加え、EXT-04のprivacy/retention承認とEXT-05の教育評価承認を別々に検証できるまでadmission不可であり、policyだけで代替しない。例をuntrusted data境界へ置き、長さbudgetへ算入 |
| 期限/gate | `L-03`後、`L-04`前。Prompt hash、順序、budget、PII/実回答拒否を試験 |
| 承認 | DEC-22既定案のセッション承認。実データ例の機関承認を代替しない |

## P1-04 — Repeated evaluation mode

| 項目 | 内容 |
|---|---|
| 要求 | FR-058 |
| Decision | IMPLEMENT |
| Owner | Run/Validation責任者 |
| 理由 | non-determinismを単一候補で隠さず、trial別候補と中央値/範囲を分離表示する |
| 影響 | request budget、費用、session cleanup、保存列が増える。繰返し結果を最終点の自動確定に使ってはならない |
| 暫定統制 | 完成まではproduct UIは1回実行だけを提供し、NFR-REL-003の非決定性警告と再現metadataを常に表示する。P0のrelease/教育評価harness `V-04`〜`V-06`は別経路で複数回測定を行い、UI featureで置換しない |
| 実装/試験 | `P1-04`: `Core/Runs/RepeatedEvaluationPlan.cs`; `Core.Tests/Runs/RepeatedEvaluationPlanTests.cs`。trial countは2以上、上限は`floor(remaining run attempt budget / worst-case attempts per trial set)`を越えず、より小さいrelease resource limitがある場合はそれにも従う。根拠のない固定回数を既定化しない。各trial status/item候補、中央値/範囲を別型にし、全outbound attemptをFR-052 budgetへ開始前に原子的予約。trial間session共有0、cleanup失敗で後続停止 |
| 期限/gate | `V-04`後、`UI-06`前。`UI-06`が唯一のproduct UI consumer。budget境界、cancel/retry/partial failure/cleanupと単一値へのcollapse禁止を試験 |
| 承認 | DEC-22既定案のセッション承認。回数のproduction既定値は根拠なしに設定しない |

## P1-05 — Retention responsibility

| 項目 | 内容 |
|---|---|
| 要求 | FR-065 |
| Decision | IMPLEMENT |
| Owner | Policy/Privacy責任者 |
| 理由 | 保存期間と削除責任を運用前に明示し、アプリが法的期限や原本削除を自動判断しないため |
| 影響 | 初回設定、policy表示、README/diagnosticsへの非秘密metadataが増える。未設定時は実データgateへ影響する |
| 暫定統制 | 完成/承認までは実データmodeを`POLICY_BLOCKED`。アプリは入力/出力を自動削除しない |
| 実装/試験 | `P1-05`: `Core/Policy/RetentionResponsibility.cs`; `Core.Tests/Policy/RetentionResponsibilityTests.cs`。期間、owner role/contact reference、applies-to、review/expiryをclosed valueとして検証し、期限到来時は通知だけ。原本/output自動削除APIを持たない。`A-14`の`RealDataGate`はこの値とEXT-04 approvalを別々に検査する |
| 期限/gate | `A-14`前、`UI-01`/`UI-12`前。missing/expired/invalidとauto-delete 0を試験 |
| 承認 | DEC-22既定案のセッション承認。実際の期間/責任者はEXT-04の機関承認待ち |

## P1-06 — Threshold calibration precision/recall

| 項目 | 内容 |
|---|---|
| 要求 | FR-076 |
| Decision | IMPLEMENT |
| Owner | Education validation責任者 |
| 理由 | 校正threshold候補のfalse positive/negativeを教員labelに対して可視化するため |
| 影響 | label schema、missing/abstain扱い、denominator 0、metric説明が必要。数値をthreshold承認の代替にできない |
| 暫定統制 | 完成まではprecision/recallを表示・推測せず、thresholdはFR-073の署名校正recordがなければ`MISSING` |
| 実装/試験 | `P1-06`: `Core/Validation/ThresholdCalibrationMetrics.cs`; `Core.Tests/Validation/ThresholdCalibrationMetricsTests.cs`。教員が事前作成したbinary/relevant labelだけを入力し、TP/FP/FN、分母、missingを明示。label生成・補完・default threshold 0 |
| 期限/gate | EXT-05をG-14で検証し、`V-01`が事前metric specを受理した後、`UI-06`前。EXT-05不在/不正では計算せず`BLOCKED`。golden confusion matrix、分母0、missing/invalid labelを試験 |
| 承認 | DEC-22既定案のセッション承認。metric spec/labelはEXT-05承認待ち |

## P1-07 — Config named ranges

| 項目 | 内容 |
|---|---|
| 要求 | FR-092 |
| Decision | IMPLEMENT |
| Owner | Workbook/Formula責任者 |
| 理由 | 評価項目追加時の参照追随と式可読性を確保するため、計画済みnamed-range writerを実装する |
| 影響 | workbook defined-name mutation surface、name衝突、scope、formula参照検証が増える。入力由来defined nameは変更できない |
| 暫定統制 | 完成まではP0の固定Config cell参照だけを使い、動的な評価項目追加を完成扱いにしない |
| 実装/試験 | `P1-07`: `Workbooks/Writing/ConfigNamedRangeWriter.cs`; `Workbooks.Tests/Writing/ConfigNamedRangeWriterTests.cs`。app namespaceのworkbook-scoped nameだけを追加し、既存nameを置換しない。item追加/並替/削除、case-insensitive衝突、採用するOpen XML/Excel上限、formula allowlist/graphを検証する。衝突・上限超過はoutput mutation前に全体をfail-closedとし、silent truncation/partial insertionをしない |
| 期限/gate | `O-04`後、`O-05`前。Open XML reopen、Excel/LibreOffice recalc、参照切れ/外部参照0 |
| 承認 | DEC-22既定案のセッション承認。具体的命名grammarはG-RB後のcontractで固定 |

## P1-08 — Minimized review copy

| 項目 | 内容 |
|---|---|
| 要求 | FR-106 |
| Decision | IMPLEMENT |
| Owner | Workbook/Review privacy責任者 |
| 理由 | 教員間二重reviewを支援しつつ、識別列・不要設問・他用途dataを既定除外するため |
| 影響 | 第二のoutput artifact、保存先、hash、atomic write、保持/共有責任が増える。copy生成自体が安全な共有を保証しない |
| 暫定統制 | 完成まではアプリ内reviewだけを提供し、手動exportや元workbook共有を推奨しない |
| 実装/試験 | `P1-08`: `Workbooks/Writing/MinimizedReviewCopyWriter.cs`; `Workbooks.Tests/Writing/MinimizedReviewCopyWriterTests.cs`。生成前に含有列/選択行/分類をpreviewし、識別列をdefault deny、利用者の明示選択後だけ新規fileへcopy-on-write。元入力/主output不変、選択外学生/除外列canary 0 |
| 期限/gate | `R-11`後、`UI-10`前。cancel/disk-full/path collision/atomic validation/retention warningを試験 |
| 承認 | DEC-22既定案のセッション承認。実際の共有先/受領者はEXT-04 policy待ち |

## P1-09 — Shared/sync folder risk detector

| 項目 | 内容 |
|---|---|
| 要求 | NFR-PRI-006 |
| Decision | IMPLEMENT |
| Owner | Platform/Privacy責任者 |
| 理由 | 検出可能な同期/共有pathを保存前に警告し、意図しないcloud/他user共有を減らすため |
| 影響 | OS/provider差とfalse negativeが避けられず、「警告なし=安全」と誤認させるriskがある |
| 暫定統制 | 完成までは全output先に一般警告とaccess-control確認を表示し、自動で安全判定しない |
| 実装/試験 | `P1-09`: `Platform/Storage/SharedFolderRiskDetector.cs`; `Platform.Tests/Storage/SharedFolderRiskDetectorTests.cs`。known folder/provider marker/mount情報等のdocumented signalだけを`DETECTED`/`UNKNOWN`で返し、本文、directory listing、network probeを行わない。pathをlogへ出さない |
| 期限/gate | `W-11`後、`UI-03`/`UI-12`前。OneDrive/iCloud等の合成path、UNC/mount、unknown/permission errorで安全断定0 |
| 承認 | DEC-22既定案のセッション承認。OS暗号化/ACLは推奨だけで自動変更しない |

## P1-10 — Accessibility conformance checklist

| 項目 | 内容 |
|---|---|
| 要求 | NFR-ACC-005 |
| Decision | IMPLEMENT |
| Owner | Accessibility/QA責任者 |
| 理由 | WCAG 2.2 AA/WCAG2ICTの各criteriaをdesktopへ適用/非適用/証拠に分解し、曖昧な「準拠」claimを避けるため |
| 影響 | manual assistive-technology evidence、例外理由、更新reviewが必要。checklist完了だけで第三者認証を意味しない |
| 暫定統制 | P0のkeyboard/focus/screen-reader/200% testsを維持し、文書完成前はWCAG AA適合を表示しない |
| 実装/試験 | `P1-10`: `dev/docs/accessibility-conformance.md`; `E2E.Tests/Accessibility/ConformanceChecklistTests.cs`。全criterionにApplicable/Not applicable/Not tested、根拠、OS/AT、artifact hash、ownerを要求し、空欄や自己申告だけをPASSにしない |
| 期限/gate | `E-08`後、`D-07`前。`dev/docs/accessibility-conformance.md`; `E2E.Tests/Accessibility/ConformanceChecklistTests.cs`がPASSするまで、GATE-ACCEPTANCEのP0結果にかかわらずReleaseをblockする。README/release claimは実結果行だけを参照 |
| 承認 | DEC-22既定案のセッション承認。独立適合認証は主張しない |

## Integration rules

1. `P1-01`〜`P1-10`を計画第19.1節の位置へ追加依存として挿入する。共有file ownerを変えず、UI fileは対応`UI-*`だけが編集する。
2. P1 taskのためにP0 security/privacy/gateを弱めない。P1失敗をP0の成功へ丸めず、初回正式版から外す場合は個別`DEFER` decisionを新規作成する。
3. P1 featureは既定無効のstubで「実装済み」とせず、正方向・負方向test、該当phase gate、E2E evidenceを要求する。
4. P1-04の利用者向け繰返しmodeと、P0の教育評価harness `V-04`〜`V-06`を統合・置換しない。
5. P1-08のreview copyはmain output/completion pairとは別artifactとして識別し、入力または正式評価済みoutputと誤認させない。
6. P1-09の検出結果は`DETECTED`/`UNKNOWN`であり、`SAFE`を返さない。
7. `DEFER` 0件なので、延期理由・再検討日・代替統制のrelease承認recordは現在不要である。ただし上記暫定統制は各feature完成前のfail-closed behaviorとして必須である。

## Approval record

| 項目 | 値 |
|---|---|
| Decision | 全10件`IMPLEMENT`、`DEFER` 0件 |
| Approver | 本セッションの利用者（project decision approver） |
| Basis | ADR-0001に記録された「全てのタスク」「既定案」承認 |
| Approval text SHA-256 | `98A684F528BDDD79E68973DD4B31AD72902CF6EDD6F1E36D73D3A951D299D826` |
| Approval semantics | セッション指示のintegrity anchor。組織電子署名、実装完了、機関privacy/education approvalではない |
| Release acceptance | 各task/test/gateとREL-03/REL-04待ち |

## 完了判定

P1全10件を個別に`IMPLEMENT`または`DEFER`へ分類し、今回は全件`IMPLEMENT`とした。各項目のaccountable role、理由、影響、完成前の代替統制、task/file/test、依存挿入点、期限、承認意味を記録した。個人owner、実装結果、外部承認、性能値は存在しないため捏造していない。