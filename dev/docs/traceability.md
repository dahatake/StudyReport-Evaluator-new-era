# Requirement traceability — v4.6 UI/settings delta on v4.5 baseline

| 項目 | 値 |
|---|---|
| Requirement | `docs/requirements-definition.md` v4.6 / 2026-09-07（AC-016／017の到達契約、追加AC-035〜039／TR-34〜38。既存AC-029〜034／TR-30〜33のdelivery境界を維持） |
| Decision | ADR-0012（機能）/ ADR-0015（target delivery。2026-10-06に一部廃止）/ ADR-0013（current public evidence）/ ADR-0016（Windows単一EXE 1操作起動） |
| Detailed design | `dev/docs/detailed-design.md` |
| Plan | [UI・設定保存プランv2](archive/work/20260907-ui-settings-redesign-plan-v2.md)。要求所有者の2026-09-07のD01〜D20デフォルト採用・全実装承認を優先 |
| UI/settings contract | [ui-layout-contract.md](ui-layout-contract.md)。T01の未実装記録は履歴。実在するstore／VM／Viewと局所試験を下記のVERIFIED_SCOPEDへ接続し、native／本人／最終packageは別判定 |
| Previous delivery plan | `dev/docs/archive/work/20260904-publication-remediation-plan.md` |
| Previous validation | delivery変更前 Release build warning/error 0、full 670/670 PASS（Core 190 / App 480）。現在の0.8.0 delivery evidenceへ流用しない |
| Recorded focused validation | 2026-09-05: InputViewTests 21、WorkbookMetadataReaderTests 11、ColumnMappingSuggesterTests 14、計46/46 PASS。DocumentationContractTests 14/14 PASS |
| Audited full required validation | 2026-09-05、source `20c8121`のbaseline: Core 190 + App deterministic 524 + sample構造 1 + Windows ZIP 3 = 718/718 PASS（App計528、failed 0、skipped 0）。opt-inの未実行経路を含む |
| Current status | UI／設定保存・復元差分と記録済み文書／実EXEの限定範囲はVERIFIED_SCOPED。T01〜T38はREVIEWED、T39は追加native FAIL・本人確認等の外部前提によりBLOCKED。製品`0.8.6`は2026-10-01に公開済み、F01はREVIEWED、前回公開版は`v0.8.1` ZIP。公開はrepository ownerの明示判断でclean-host試験CH-01〜06を実施せず、protected `publish-release.yml` workflowではなくdraft解除で行った。T36〜T39の記録済み成功は0.8.4に限定し、CH-01〜06はNOT_RUN_EXTERNAL_PREREQUISITEのまま。`20c8121`のv4.4／`0.8.3` baselineとR03／V01は別の履歴。 |

開発用 MSIX と macOS の基盤は 2026-10-06 に廃止した。対応するAC-023〜025／TR-25〜27は`RETIRED`とし、ID連番を保つため行だけを残す。

この表の`PLANNED`は未実装をPASSと称しない。task完了後にproduction symbol、direct test、gate identityへ更新する。旧v3 traceabilityはGit履歴とADR-0011に保持する。

2026-09-07のD17上書き「全タスク完了後だけUnreleased追記 → 製品PATCH `0.8.4` → `0.8.5`」と、T01で製品版・CHANGELOG・元プラン・履歴evidenceを変更しない方針は履歴として保持する。さらに後続の利用者不在時の自律続行指示により、T39をBLOCKEDのままF01／F02を進める。F01はREVIEWED、親担当の版正本PATCHは反映済み。G4・全タスクDONE・公開PASSを意味せず、最終`0.8.6`の判定は[実行記録](archive/work/20260907-ui-settings-execution-record.md)の最新F02欄へ接続する。

## Evidence integrity and storage

### 2026-09-18 ジョブコスト観測の局所検証

コスト系限定実行は58件中57 PASS・1 SKIP（symlink作成権限なし）・FAIL 0。対象はJobUsageTracker、SdkUsageAdapter、UsageProvenance、JobCostBackend、JobCostModel、JobCostSnapshot、AttemptOutcomeLogging、JobCostView、CostAttemptLifecycle。Debug buildは0警告・0エラー。fake transportと一時directoryの実fileだけを使い、実AI・実認証・実課金照合・native DPI・全suiteは未実施。AIクレジット換算は未確認のため実装していない。

### 2026-09-17 中断・再開の局所検証

`TestResults/resume-focused/resume-verified.trx`: 12/12 PASS（ResumeWorkflowTests 8、ResumeAdmissionEvaluatorTests 2、ビルド接続修正のAttemptOutcomeLoggingTests 2）。`resume-durable.trx`: 既存DurableQuantificationOrchestratorの再開とmismatch 5/5 PASS。Debug build成功。実checkpointとfake run、headless picker取消・繰返しwindow closeを確認。全suite、native DPI、本人login、実AI、別process再起動、OS shutdown、配布物・公開gateは未実施。過去の探索済みテストだけの9/9報告は新規導線の検証根拠にしない。

### 2026-09-05 baseline（履歴）

監査済みcandidateのsource identityは`20c8121c2474a13f409c0d7f0fde9d4c41f74698`、product versionは`0.8.3`である。集約記録は`artifacts/test/final-recovery-20c8121/phases.json`。以下のPASS系statusはこのsource、または明記した履歴の範囲に限定する。

- 集約記録のrestore、build、version、version-self-test、deterministic、sample、windows-zip、当時の開発用package検証（廃止済み）、release-matrixの9工程はすべてexit code 0。件数は同directoryの各phase logに基づき、718/718は上表の4群の合計である。
- test runnerのPASS集計には、opt-in未指定で`NOT_RUN`としてreturnする経路やpolicy経路の成功を含む。skipped 0でも、実際のLive AI、外部Excel再計算、RealData処理はこのbaselineで`NOT_RUN`。sample構造確認やfake/synthetic実行をそれらの実処理完了へ読み替えない。
- `artifacts/`はGit対象外の生成物で、再実行により変化し、公開checkoutでの存在を保証しない。pathはcode spanとして記録し、公開Markdownリンクや永続release evidenceの代わりにしない。source commit、製品版、実行条件、phase log、および対象artifactのbytes／SHA-256を照合して読む。
- source commitの記載とexit codeだけで、未commit差分の不存在、全実行経路、production trustを証明しない。公開`v0.8.1`のrelease identityと、`0.8.3`のlocal matrix／package検証は別の証拠である。

今回reviewの文書修正およびその後のcode/UI変更はこのbaselineの検証対象外であり、変更後のsourceで再検証が必要。既存gateやPNGを、新しい変更の検証済み証拠として扱わない。

今回の[修正記録](archive/work/20260905-adversarial-review.md)に対応する最終実行の正本は`artifacts/test/adversarial-review/final/summary.json`。`sourceCommit`一致、前後clean、全体`PASSED`、各TRXの件数／outcome／assembly／SHA-256が揃う場合だけ、そのsourceの完了と判定する。記録なし、`RUNNING`、`FAILED`は合格ではない。再実行で前回記録を上書きせず、別run directoryへ保存する。

### 2026-09-07 UI/settings scoped evidence（0.8.4記録）

出典は[親担当の実行記録](archive/work/20260907-ui-settings-execution-record.md)と要求所有者の後続引継ぎ、実装source、対応test source、以下の既存TRXである。T01〜T35はREVIEWEDだった履歴を維持し、後続T36〜T38も親担当の実装・対象検証・敵対的レビュー・必要修正確認を完了している。T39の自動回帰成功と追加native FAIL／人手未実施を分離する。以下は製品`0.8.4`の記録で、F02後の`0.8.6`文書・最終artifactの再検証結果ではない。元プランの未承認・G0未通過表記とT01の未実装表記を現在の指示・状態へ戻さない。

| 対象集合 | 記録済み結果 | ローカル証跡と限界 |
|---|---|---|
| T18〜T21 | 540/540 | `artifacts/test/ui-settings/t18-21/t18-21-reviewed.trx`。主画面の対象回帰。後続のshell・最新修正・全suiteの代替ではない |
| T23 | 275/275 | `artifacts/test/ui-settings/t23/t23-fixed.trx`。SettingsComposition／MainWindowSettingsと関連回帰。T18〜21から分離した固定領域試験を含む |
| T24 | 67/67 | `artifacts/test/ui-settings/t24/t24.trx`。WorkflowStateの12ケースは内数。実shellと一時設定fileを使うが、run／出力receiptはfake |
| T25〜T27 | 74/74 | `artifacts/test/ui-settings/t25-27/t25-27-reviewed.trx`。T25の警告／keyboard／login回帰、T26の27ケース、T27の7ケースを含む。27と7を74へ再加算しない |
| T28・最新修正 | 216/216 | `artifacts/test/ui-settings/t28/t28-reviewed.trx`。入力→共通設定の実効出力先、適用失敗時の両draft保持、再読込中の入力差替え、関連E2Eと画像生成回帰。T32／T34の指摘反映確認もこの集合で、追加の216件と数えない |
| T35の対象文書試験 | 4/4 | `artifacts/test/ui-settings/t35/t35-reviewed.trx`。DocumentationContractTestsの`Requirements_claim_ledger_and_system_prompts_are_complete_and_current`／`Ui_settings_baseline_preserves_explicit_boundaries_and_pending_evidence`／`Public_documents_exist_are_nonempty_and_have_no_broken_local_links`／`Current_documents_link_to_existing_local_markdown_headings`。当時の公開文書集合による局所結果で、T36の11文書・8画像contract全体の証跡ではない |
| T36 | 21/21・REVIEWED | `artifacts/test/ui-settings/t36/t36-current.trx`。公開文書11件・画像8枚、設定境界・対象版・local links／anchorsのcontract。T35の4件とは別scopeで、自身のTRXを根拠とする |
| T37 | 9/9・REVIEWED | `artifacts/test/ui-settings/t37/t37.trx`。実ZIPのpublish／生成／再現性／展開起動（3件）と、廃止済みの開発用package静的契約（6件） |
| T38 | contract 114/114、P06 7/7、P07 PASS_DEVELOPMENT・REVIEWED | `artifacts/test/ui-settings/t38/t38.trx`と同`native/`配下。20公開ファイル・CLI・標準展開・移動／再起動／同時起動・Prompt設定の開発host検証。P02／P05生成EXEは0.8.4、283,408,986 bytes、SHA-256 `4D80FA246EB64E6984B6D8B4A5EA37C1F62BCDD28C40F6D9A63F41522C89E507` |
| T39自動回帰 | 1892/1892、skip 0 | `artifacts/test/ui-settings/t39/reviewed/`の2026-09-07（+09:00）の2 TRX。Core 190/190＋App 1702/1702、failed／error／notExecuted 0。CI同等の3クラス除外（実学生sample構造・別実行ZIP・P06）とP02 artifact検査opt-inの範囲であり、全受入gateではない |

T35同期時点では上記5つのUI／設定TRXとT35の文書TRXの`total=executed=passed`、`failed=error=notExecuted=0`をtextで照合した。集合は時点も対象も異なり重複を含むため、合算してfull gateを作らない。途中の`t23.trx`や`t25-27-fixed.trx`の失敗を最終結果へ混ぜず、元記録も書き換えない。T01の文書contract 18/18とT35の4/4は当時の結果であって、T36差分の再検証結果ではない。

T39の初回全体実行は1件FAILだった。設定済みExecution単体＋未読込Inputというfixtureの不整合を、合成Excelの実読込→通常ナビゲーションへ修正し、開始可否・出力先・no-autoの期待を維持した126/126と独立レビュー指摘0の後に、上記の全体再実行が成功した。初回FAILを消さず、無効化された実AI／外部再計算等の経路をlive実行済みとしない。

T26は実Avalonia Viewのheadless測定とページ計算に限定する。通常1024×720／1180×800のClientSize・外側非scroll・完全包含、760×600／scale 2の到達性例外、100／530合成行のvirtualizationを分ける。20,000件は計算のみで、全Controlの性能実測ではない。寸法・実表示件数の正本は[UI layout contract](ui-layout-contract.md)§5。

T27は4合成回答行、6メソッド・7ケースのVM→実filesystem E2Eで、実reader／durable orchestrator／checkpoint／writer／validator／atomic commitを使う。認証・model／runtime identity・AI応答・時刻は合成。新store／VM／orchestrator instanceによる復元・再開であり、別process再起動・native UI・本人loginの証拠ではない。

T28は親担当記録に8枚の通常画像1440×1050、全8枚の2回hash一致、最小1024×720の実frame検証がある。`DocumentationScreenshotTests`のsynthetic／fake状態による説明画像で、設定保存・workbook作成・native画面の実証ではない。T35は画像bytesの読込・目視・再生成を行っていない。`ui-layout-contract.md`の画像欄はこの0.8.4生成履歴へ同期し、F02後の0.8.6で再生成したことにはしない。

**T35の対象文書試験は4/4成功・敵対的レビュー済み（指摘0）。** T36の文書・画像contract全体は別scopeで、その21/21をAC-022／TR-22の`VERIFIED_SCOPED`へ接続する。T38のP06 7/7もAC-032／TR-31の限定範囲に接続するが、disk-full／directory ACL／抽出中断／EXE・ZIP間checkpoint再開の未実施を埋めず、全faultの`PASS_REQUIRED`にはしない。

T39の追加nativeは3試行後に停止。最新`artifacts/test/ui-settings/t39/native-final-attempt.json`は`CONTROL_ID_PREDICATE_NOT_UNIQUE`でFAIL。120 DPI、実client 1475×1000 pixel＝1180×800 DIP、合成入力読込、入力／EXE不変、実利用者setting.txtの開始前後不存在だけを観測した。4画面・5カテゴリ・1024×720・実keyboard／focusは追加検証未完了で、T26 headless測定を置き換えない。Narrator、本人walkthrough4項目（入力／30・10配点、設定保存と復帰、再起動・明示適用、override未保存の識別）、隔離利用者native保存は`NOT_RUN_EXTERNAL_PREREQUISITE`。T39をBLOCKEDのままF01／F02を進める承認は、CH-01〜06・本人login・実AI／Office再計算・candidate／protected publishや全タスクDONEの承認ではない。

## Status vocabulary

| Status | Meaning |
|---|---|
| `PLANNED` | owner/file/testを割当済み。実装またはpassing evidenceは未確認 |
| `NOT_RUN` | 対象の実処理を実行していない。test methodのPASSや過去evidenceで置き換えない |
| `NOT_RUN_CURRENT_CANDIDATE` | sourceと過去evidenceはあるが、現在のworking treeに対するrequired regressionを未実行 |
| `VERIFIED_SCOPED` | 実在sourceと記録済みdirect testsで、明記した製品版・対象集合の限定範囲を確認。AC／TR全体の受入、後続編集の再検証、未実施のnative／本人／package／公開のPASSへ自動拡張しない |
| `PASS_DEVELOPMENT` | 開発hostの明記したartifact／対象範囲だけの成功。fresh OS・本人認証・公開gateを満たさない |
| `PASS_REQUIRED` | production behaviorとdirect deterministic testが成功 |
| `PASS_EXTERNAL` | credentialed／platform external testを実行して成功 |
| `NOT_RUN_EXTERNAL_PREREQUISITE` | source/pipelineは存在するがrequired credential／runnerがなく実行していない |
| `FAIL` | required verificationが失敗 |
| `BLOCKED_REVIEW` | 再現したblocker/high review findingが未解決 |
| `NOT_REQUIRED` | v4 scope外。不要性を一般化しない |
| `BLOCKED_EXTERNAL` | required input/artifactがrepository外にあり、安全なlocal代替がない |
| `PASS_MECHANISM` | test certificateまたはunsigned artifactでpackage mechanismだけが成功。非公開であり、production readinessを意味しない |
| `PASS_PRODUCTION` | production trust pathとclean target OSで成功 |
| `RETIRED` | 要求所有者が廃止した要求。実装・testを削除済みで、ID連番の保持だけを目的に行を残す |
| `MIXED_ADVISORY` | optional evidenceを実行し、PASSとFAILED_ADVISORYが混在。required gateへ算入しない |

AC-023〜025とTR-25〜27は2026-10-06に`RETIRED`とした（開発用 MSIX と macOS の基盤は 2026-10-06 に廃止した）。これらの旧PASSを他のAC／TRへ転記しない。

AC-028／TR-29では、公開済み`v0.8.1`のdraft asset／matrix hash照合・公開後の認証なしre-downloadという履歴と、`20c8121`（`0.8.3`）のlocal matrix生成・semantic検証／workflow contract testのbaselineを区別する。後者は`0.8.3`の公開workflowや公開後re-downloadの完了を示さない。

AC-029〜034／TR-30〜33は、deterministic test・開発host観測（P07）・fresh Windows clean-host本人実測（V02）・protected publish contract（C05/C06）を分離して判定する。既存PASS（ZIP経路、fake認証、開発host観測）をfresh OS/MOTW/本人loginの新経路PASSへ転記しない。

UI／設定の追加AC-035〜037・TR-34〜36と直接影響するAC-013／016／017／018・TR-17／18は、以下の実在するproduction symbolとdirect testsへ接続した`VERIFIED_SCOPED`とする。T01時点の予定ownerを実在確認済みの試験ownerへ更新したが、従来のpath／UI／PromptのPASSを新機能へ転記したものではない。AC-022／TR-22は0.8.4のT36文書contract 21/21、AC-032／TR-31は同T38のP06 7/7に限り`VERIFIED_SCOPED`。F02後の0.8.6最終再検証とは分離し、通常／例外layout、deterministic設定E2E、追加native FAIL、Narrator／本人walkthrough、公開claimを別々に判定する。

## One-action startup owner split（D03）

| Owner | Scope | Exact automated anchors |
|---|---|---|
| deterministic package/UI/fake tests | source contract、package layout、起動契約、UI/login serviceの非本人経路 | `StudyReportEvaluator.App.Tests.Packaging.WindowsSingleFileProfileTests.Windows_single_file_profile_scopes_all_declarations_to_the_app` / `Windows_single_file_profile_declares_standard_self_contained_settings_once` / `Windows_single_file_profile_registers_only_the_exact_safe_public_allowlist` / `StudyReportEvaluator.App.Tests.Packaging.WindowsSingleFilePublishTests.Single_file_publish_script_parses_and_preserves_the_default_folder_flow` / `Publish_mode_uses_the_full_app_profile_only_when_opted_in` / `Extracted_bundle_layout_reuses_integrity_checks_without_requiring_static_host_files` / `Single_file_probe_environment_owns_cache_home_and_trace_without_inherited_secrets` / `StudyReportEvaluator.App.Tests.Packaging.WindowsSingleFileArtifactTests.Copy_unit_preserves_exact_bytes_generates_exact_hash_and_repackages_without_touching_workbooks` / `StudyReportEvaluator.App.Tests.Packaging.WindowsSingleFilePackageTests.Single_file_cold_and_warm_start_validate_payload_and_show_input` / `Single_file_relative_input_and_two_prompts_are_observed_in_GUI_order` / `Single_file_two_instances_share_cache_and_close_independently` / `Single_file_missing_cached_CLI_is_recovered_by_the_standard_host` / `Single_file_deleted_cache_is_reextracted_after_moving_the_EXE` / `Single_file_file_valued_extraction_base_fails_without_changing_data` / `Single_file_truncated_bundle_reports_a_host_error_without_changing_data` / `StudyReportEvaluator.App.Tests.Copilot.BundledCopilotLoginServiceTests.Login_uses_only_the_explicit_path_fixed_arguments_and_unredirected_console` / `Cancellation_stops_only_owned_login_and_allows_retry_even_when_wait_ignores_token` / `StudyReportEvaluator.App.Tests.UI.CopilotLoginCommandTests.Login_invalidates_old_identity_and_exit_zero_requires_explicit_authentication_check` / `Cancel_command_stops_only_owned_login_once_and_retry_remains_explicit` / `Run_and_run_cancellation_exclude_login_and_login_preserves_completed_run_state` |
| P07 development-host evidence | 開発host上の単一EXE実測を機構証跡として固定（clean-host代替不可） | `StudyReportEvaluator.App.Tests.Packaging.ReleaseMatrixBuilderTests.Candidate_generation_is_exact_and_passes_the_public_C02_candidate_CLI`（`PASS_CANDIDATE`拘束）/ 同fixtureの`CreateP07Evidence`由来7件（`WindowsSingleFilePackageTests`メソッド群） |
| V02 human fresh Windows clean-host/login tests | fresh OS、MOTW/保護観測、本人login、任意live AIの実測 | automation未接続（human-run）。`windows-singlefile-clean-host.evidence.json`の受領・検証対象で、CH-01..06は`NOT_RUN_EXTERNAL_PREREQUISITE`。任意ADV-01/02も未実施で、必須CHとは分離 |
| C05/C06 protected workflow contracts | candidate拘束、matrix v2閉集合（EXE、ZIPの2行）、public 4 assets、CH必須、publish保護 | `StudyReportEvaluator.App.Tests.Packaging.ReleaseWorkflowContractTests.Candidate_workflow_is_manual_stable_tag_only_and_never_publishes` / `Candidate_workflow_uses_exact_control_and_public_artifact_sets` / `Publish_workflow_requires_protected_environment_permissions_and_clean_host_json_via_env_only` / `Publish_workflow_binds_candidate_run_to_repository_event_workflow_path_conclusion_and_sha` / `Publish_workflow_enforces_exact_control_and_draft_asset_sets_invokes_c03_final_and_uploads_before_publish` / `Publish_workflow_revalidates_current_draft_bytes_and_asset_identity_in_the_publish_step` / `StudyReportEvaluator.App.Tests.Packaging.ReleaseMatrixContractTests.V2_rejects_broken_schema_or_semantic_closure`（`ch06-not-run`/`ch06-fail`/`ch06-na`拒否、`ADV-01/02` NOT_RUN許容） |

## UI/settings production and direct-test owners（T35）

pathは実在するsourceへの参照、メソッド名は各test fileの直接anchorである。以下は試験の担当範囲を明示し、実行結果は上の対象集合へ限定する。classの存在や手書きの期待値だけからPASSを作らない。

| Scope／追跡 | Production owner | Direct test anchors／限界 |
|---|---|---|
| 設定型・strict store：AC-035／TR-34／ST-UC-27／C-045 | [`ApplicationSettings`](../../src/StudyReportEvaluator.App/Settings/ApplicationSettings.cs)、[`SettingsFileStore`](../../src/StudyReportEvaluator.App/Settings/SettingsFileStore.cs)。`SettingsFileStore.LoadAsync`／`SettingsFileStore.SaveAsync` | [`ApplicationSettingsTests`](../../tests/StudyReportEvaluator.App.Tests/Settings/ApplicationSettingsTests.cs)の`Definition_round_trip_preserves_all_fields_and_canonical_hash`、[`SettingsFileStoreTests`](../../tests/StudyReportEvaluator.App.Tests/Settings/SettingsFileStoreTests.cs)の`Unicode_nested_definition_round_trip_preserves_every_field_order_and_canonical_hash`／`Exclusive_handle_refuses_read_and_replace_keeps_old_bytes_and_cleans_only_own_temp`／`Save_uses_the_supplied_record_and_last_successful_save_wins_across_store_instances`。一時pathの実IO。別process同時保存や電源断の実測ではない |
| 明示保存・初期化：AC-035／037、TR-34／36 | [`SettingsViewModel`](../../src/StudyReportEvaluator.App/ViewModels/SettingsViewModel.cs)の`InitializeAsync`／`SaveAsync`／`SynchronizeDrafts`、[`ServiceRegistration`](../../src/StudyReportEvaluator.App/Composition/ServiceRegistration.cs)の`FromStartup`／`CreateMainWindow`／`InitializeAsync` | [`SettingsViewModelTests`](../../tests/StudyReportEvaluator.App.Tests/UI/SettingsViewModelTests.cs)の`Save_freezes_values_before_notifications_blocks_duplicate_commands_and_keeps_new_edits_dirty`／`Common_only_save_without_loaded_input_keeps_saved_definition_not_placeholder_drafts`、[`SettingsCompositionTests`](../../tests/StudyReportEvaluator.App.Tests/Composition/SettingsCompositionTests.cs)の`Legacy_registration_constructors_never_resolve_or_read_user_settings`／`Production_window_with_empty_startup_reads_preferences_without_opening_settings_or_input`。production初期化を注入一時pathで確認し、実利用者fileへアクセスしない |
| 保存定義の明示適用：AC-036／TR-35／ST-UC-28／C-046 | [`InputViewModel`](../../src/StudyReportEvaluator.App/ViewModels/InputViewModel.cs)の`ApplySavedDefinitionAsync`とread-only `InputWorkbookLoader`、`SettingsViewModel.ApplySavedDefinitionAsync` | [`SavedDefinitionApplicationTests`](../../tests/StudyReportEvaluator.App.Tests/UI/SavedDefinitionApplicationTests.cs)の`Application_reloads_the_saved_header_and_preserves_ids_order_prompts_points_and_canonical_content`／`Mapping_failures_preserve_all_loaded_state_without_sheet_column_or_row_fallback`、`SettingsViewModelTests.Explicit_apply_updates_both_editors_on_success_and_preserves_instances_and_imported_prompts`。header 1／2、失敗・取消・競合、保存IDとImported Prompt保持。checkpoint admissionとは別 |
| 希望model・出力先・固定request：AC-013／035／037、TR-34／36 | [`ExecutionViewModel`](../../src/StudyReportEvaluator.App/ViewModels/ExecutionViewModel.cs)の`ApplySettings`／`Configure`／`UpdateNextDraftSummary`／`StartAsync` | [`ExecutionSettingsTests`](../../tests/StudyReportEvaluator.App.Tests/UI/ExecutionSettingsTests.cs)の`Output_override_survives_input_changes_and_restore_while_null_recomputes_result`／`Preferred_model_requires_an_exact_confirmed_match_and_never_falls_back`／`Authentication_refresh_and_failure_keep_preference_despite_binding_null_feedback`／`Running_settings_edits_preserve_immutable_request_progress_and_stop`。model確認・runはfake、実出力はT27へ分離 |
| 設定5カテゴリ・shell：AC-016／017／037、TR-17／36、C-047 | [`MainWindow`](../../src/StudyReportEvaluator.App/Views/MainWindow.axaml.cs)の`ShowCurrentEditor`／`FocusEditorAfterLayout`と[`XAML`](../../src/StudyReportEvaluator.App/Views/MainWindow.axaml)、[`SettingsView`](../../src/StudyReportEvaluator.App/Views/SettingsView.axaml.cs)の`ShowCategory`／`CreateCategory` | [`MainWindowSettingsTests`](../../tests/StudyReportEvaluator.App.Tests/UI/MainWindowSettingsTests.cs)の`Normal_shell_keeps_five_views_inside_fixed_regions_without_outer_scroll`／`Settings_close_restores_the_actual_invoker_from_each_workflow_view`／`Cached_shell_views_keep_pending_numeric_text_and_binding_instances`／`Active_run_progress_and_stop_are_fixed_in_all_five_views`、[`SettingsViewTests`](../../tests/StudyReportEvaluator.App.Tests/UI/SettingsViewTests.cs)の`Five_enum_categories_render_only_the_selected_editor_and_reuse_its_owner_and_control`。実Viewのheadless、nativeではない |
| 往復・最新draft・前回結果：AC-037／TR-36／ST-UC-29 | [`MainWindowViewModel`](../../src/StudyReportEvaluator.App/ViewModels/MainWindowViewModel.cs)の`OpenSettings`／`RefreshExecutionConfiguration`／`HandleRunCompleted`、Settingsの既存editor同期 | [`WorkflowStateTests`](../../tests/StudyReportEvaluator.App.Tests/UI/WorkflowStateTests.cs)の`Shell_round_trip_saves_the_latest_mapping_or_evaluator_edit`／`Deferred_run_uses_captured_request_and_delivers_results_without_closing_edited_settings`／`Shared_stop_cancels_captured_run_without_discarding_next_settings_or_partial_results`／`Next_draft_changes_neither_previous_result_values_nor_the_last_export_request`。T24の12ケースは関連67件の内数。設定fileは実IO、run／outputはfake receiptで実checkpoint検証ではない |
| 最新T28修正：AC-013／035／036／037、TR-34〜36 | `MainWindowViewModel.RefreshNextDraftSummary`／`RefreshExecutionConfiguration`、`InputViewModel.SetFilePath`、`ExecutionViewModel.UpdateNextDraftSummary` | [`MainWindowTests`](../../tests/StudyReportEvaluator.App.Tests/UI/MainWindowTests.cs)の`Initial_input_common_settings_preview_does_not_configure_execution`／`Common_settings_after_input_replacement_preserves_configuration_and_override`／`Failed_saved_definition_apply_preserves_divergent_drafts_and_latest_preview`／`Input_replacement_during_reload_publishes_only_coherent_execution_state`。最新216件に収録。次回表示更新だけで現在構成・両draft・保存fileを変更しない |
| 読込Prompt移設：AC-018／037、TR-18／36、ST-UC-13／29 | [`ImportedPromptSettingsView`](../../src/StudyReportEvaluator.App/Views/ImportedPromptSettingsView.axaml)、[`QuantificationDesignViewModel`](../../src/StudyReportEvaluator.App/ViewModels/QuantificationDesignViewModel.cs)の`ApplyImportedPromptCommand`、Settingsの適用可能条件 | [`ImportedPromptSettingsViewTests`](../../tests/StudyReportEvaluator.App.Tests/UI/ImportedPromptSettingsViewTests.cs)の`Imported_list_preserves_basename_original_order_and_read_only_full_text_without_copy`／`Explicit_apply_copies_only_to_selected_custom_evaluator`／`Explicit_apply_copies_only_to_selected_special_evaluation_without_changing_points`、`SettingsViewTests.Alternating_category_edits_and_imported_prompt_reach_the_latest_draft_before_explicit_save`。既存LaunchOptionsTestsと設定初期化／no-auto回帰を置き換えない |
| 警告・keyboard・login回帰：AC-016〜018／037、TR-17／18／36 | MainWindowのwarning／固定操作、Executionの明示login／状態確認、移設先の実Control | [`EthicsWarningTests`](../../tests/StudyReportEvaluator.App.Tests/UI/EthicsWarningTests.cs)の`Warning_persists_across_all_steps_and_settings_categories_without_blocking_keyboard_navigation`、[`PrimaryJourneyAccessibilityTests`](../../tests/StudyReportEvaluator.App.Tests/UI/PrimaryJourneyAccessibilityTests.cs)、[`CopilotLoginCommandTests`](../../tests/StudyReportEvaluator.App.Tests/UI/CopilotLoginCommandTests.cs)の`Execution_login_stays_explicit_and_completion_in_settings_never_navigates_checks_auth_or_starts_AI`。T25〜27の74件内。本人login（CH-06）はNOT_RUN |
| 通常／例外layout：AC-016／017、TR-17、ST-UC-12 | MainWindow／SettingsViewの有限本文と既存Input／Design／Resultsのページ一覧 | [`ResponsiveLayoutTests`](../../tests/StudyReportEvaluator.App.Tests/UI/ResponsiveLayoutTests.cs)の`Normal_shell_contains_every_step_without_body_scroll_at_actual_client_size`／`Results_rows_stay_virtualized_after_the_responsive_reflow`、[`CompactWorkflowLayoutTests`](../../tests/StudyReportEvaluator.App.Tests/UI/CompactWorkflowLayoutTests.cs)の`Long_content_in_four_steps_and_every_settings_category_keeps_normal_body_contained`／`Narrow_scaled_shell_reaches_last_operations_without_moving_warning_or_navigation`／`Twenty_thousand_item_page_math_resizes_clamps_after_deletion_and_empties_without_UI`。15メソッド・27ケースは74件の内数。通常・狭小・scale 2・20,000件計算を相互代用しない |
| 結果・override：AC-016／037、TR-17／36 | [`ResultsOutputViewModel`](../../src/StudyReportEvaluator.App/ViewModels/ResultsOutputViewModel.cs)の元Results／run snapshot、ページ・選択詳細・別名出力 | [`ResultsPresentationTests`](../../tests/StudyReportEvaluator.App.Tests/UI/ResultsPresentationTests.cs)の`Paging_and_recomputation_keep_overrides_and_do_not_recreate_selected_criterion_editors`／`Draft_edits_do_not_change_loaded_run_identity_scores_or_override_range`。legacyとdurable、empty／zero／technical blankの既存oracleを維持。実fileのoverride出力は次行のT27で確認 |
| 設定から実fileまで：AC-013／035〜037、TR-34〜36、ST-UC-27〜29 | Settings／Input／Execution／ResultsのVM、実`DurableQuantificationOrchestrator`／`CheckpointStore`／`WorkbookDurableRunFinalizer`／`ResultsOutputBoundary`／`OutputPackageValidator`／`AtomicOutputCommitter` | [`SettingsWorkflowSystemTests`](../../tests/StudyReportEvaluator.App.Tests/E2E/SettingsWorkflowSystemTests.cs)の`Saved_output_preference_survives_new_instances_and_input_change_before_real_final_output`（2ケース）、`Saved_definition_admission_rejects_missing_sheet_without_mutation_then_accepts_the_matching_input`、`Cancel_saves_a_real_partial_and_new_instances_resume_without_repeating_completed_AI_or_reference`、`Override_exports_a_separate_real_workbook_using_the_run_snapshot_not_next_settings`、`Real_final_and_preview_distinguish_missing_answer_numeric_zero_and_technical_failure`、`Real_atomic_final_validation_rejects_a_corrupted_cached_score_and_does_not_publish_it`（各1ケース）。7/7は74件の内数で最新216件にも含む。4合成行の実file E2Eであり、530行E2E・別process再起動・native・実AIの代替ではない |
| 中断からの再開準備：AC-038／TR-37 | [`ResumeAdmissionEvaluator`](../../src/StudyReportEvaluator.App/Workflow/ResumeAdmissionEvaluator.cs)、[`ResumeInspectionBoundary`](../../src/StudyReportEvaluator.App/Workflow/ResumeInspectionBoundary.cs)、[`ExecutionViewModel`](../../src/StudyReportEvaluator.App/ViewModels/ExecutionViewModel.cs)、[`ExecutionView`](../../src/StudyReportEvaluator.App/Views/ExecutionView.axaml)、[`MainWindow`](../../src/StudyReportEvaluator.App/Views/MainWindow.axaml.cs) | partialのread-only項目別検証、明示的な入力／model反映、native pickerの取消状態不変、有限close drain。実AI・別process・native OS終了の証拠は含まない |

## Acceptance criteria mapping

| AC | Requirement surface | Production task | Required test owner | Status |
|---|---|---|---|---|
| AC-001 | picker/path、immutable input、separate output | X-01/U-01/X-04 | input/path/atomic tests | PASS_REQUIRED |
| AC-002 | question row 1/2、sheet/rows/normal/special mapping、primary選択時のquestion text同期 | X-01/U-01/U-02 | metadata/mapping/Input UI tests | PASS_REQUIRED |
| AC-003 | canonical repository sample F〜K candidate、input identity | X-01 | sample structural test | PASS_REQUIRED |
| AC-004 | base 60、special 0、similarity weight 0.1 | C-01/U-02 | domain/design tests | PASS_REQUIRED |
| AC-005 | equal initial question points、explicit equalize、manual preservation | C-02/U-02 | allocation/UI tests | PASS_REQUIRED |
| AC-006 | exact allocation total 100 | C-02/C-04/X-02 | validator/formula tests | PASS_REQUIRED |
| AC-007 | dynamic Knowledge/Custom criterion raw only | C-05/A-03 | Core/Copilot tests | PASS_REQUIRED |
| AC-008 | special item and equal averages | C-01/C-03/A-03/X-03 | domain/result/formula tests | PASS_REQUIRED |
| AC-009 | one `auto` reference/question/run and References sheet | A-02/X-02/W-02 | reference/writer/resume tests | PASS_REQUIRED |
| AC-010 | 0〜1 similarity and per-question penalty | A-04/C-03/X-03 | similarity/scoring/formula tests | PASS_REQUIRED |
| AC-011 | Excel-owned earned/special/penalty/raw/clamped final | C-03/C-04/X-03 | hand oracle/writer/reopen tests | PASS_REQUIRED |
| AC-012 | empty-zero、technical-blank | C-03/A-02..04/X-03 | operation/formula/E2E tests | PASS_REQUIRED |
| AC-013 | §9.1実効出力先、null時だけ入力隣接result、four final sheets、atomic commit | X-02/X-03/X-04 + T06/T27、最新表示修正T28 | ExecutionSettingsTests／MainWindowTests／SettingsWorkflowSystemTests。TR-34／ST-UC-27の明示先・null復元と実4-sheet出力。最終packageは別 | VERIFIED_SCOPED |
| AC-014 | partial create and durable reference/row checkpoints | W-01/W-02 | checkpoint/workflow tests | PASS_REQUIRED |
| AC-015 | closed resume validation and completed-row skip | W-01/W-02 | mismatch/skip tests | PASS_REQUIRED |
| AC-016 | 実progress／状態、通常最小1024×720の外側scroll不要・完全包含、ページ切替、長文／狭小／拡大例外、keyboard／ID／virtualization | U-03 + T07〜09/T11/T18〜26 | MainWindowSettingsTests／ResponsiveLayoutTests／CompactWorkflowLayoutTests／PrimaryJourneyAccessibilityTests。TR-17／ST-UC-12のheadless範囲。T39追加nativeは120 DPI部分観測でFAIL、NarratorはNOT_RUN_EXTERNAL_PREREQUISITE | VERIFIED_SCOPED |
| AC-017 | exact warning text and nonblockingを設定画面でも常時全文維持 | U-04 + T23/T25/T26 | EthicsWarningTests／MainWindowSettingsTests／ResponsiveLayoutTests。TR-17／ST-UC-12の全step・設定のheadless警告／完全包含。native読上げはNOT_RUN | VERIFIED_SCOPED |
| AC-018 | `--input`, repeated `--prompt`, 設定の読込Promptへ移設、explicit apply、no auto-run | L-01/U-02 + T16/T17/T23/T25 | ImportedPromptSettingsViewTests／SettingsViewTests／SettingsCompositionTests／CopilotLoginCommandTests。TR-18／ST-UC-13。既存LaunchOptionsTestsのparser／startup契約を維持、最終EXE引数回帰は別 | VERIFIED_SCOPED |
| AC-019 | selected same-row payload and no-content logs | C-05/A-02..04/W-01 | capability/logger/canary tests | PASS_REQUIRED |
| AC-020 | Windows legacy self-contained ZIP regression | P-01 | packaging/launch smoke | PASS_REQUIRED |
| AC-021 | unverified platform/installer/signing claim exclusion | P-01..04/D-01..05 | documentation/package contract | PASS_REQUIRED |
| AC-022 | user/dev docs、UI/settings契約、current delivery evidence | D-01..05 + T01/T28〜36/F02 | TR-22／ST-UC-16。DocumentationContractTestsのT36は`artifacts/test/ui-settings/t36/t36-current.trx`で21/21・REVIEWED（0.8.4、公開文書11件／8画像contract）。T35の4/4とは別scope。F02の0.8.6文書再検証は実行記録の最新欄で別判定 | VERIFIED_SCOPED |
| AC-023 | 廃止（FR-059） | — | 実装・test削除済み | RETIRED |
| AC-024 | 廃止（FR-060） | — | 実装・test削除済み | RETIRED |
| AC-025 | 廃止（FR-060） | — | 実装・test削除済み | RETIRED |
| AC-026 | Windows ZIP取得/hash/展開/起動docs | P-01/D-01..05 | ZIP journey + docs contract | PASS_REQUIRED |
| AC-027 | public packageにuser workbookを含めない | P-01 | package layout + input identity | PASS_REQUIRED |
| AC-028 | publish flag/status matrixとrelease boundary | P-04 | workflow contract + release evidence | PASS_REQUIRED |
| AC-029 | fresh Windows 11 x64標準user、取得済みEXE1個からoffline入力画面（1起動gesture）。警告/拒否/操作数は別記し、無警告保証しない | V-02 | deterministic: `WindowsSingleFilePackageTests.Single_file_cold_and_warm_start_validate_payload_and_show_input`（開発host）/ protected boundary: `ReleaseMatrixContractTests.V2_rejects_broken_schema_or_semantic_closure`（`os-build`等）/ human required: CH-01, CH-02, CH-05 | NOT_RUN_EXTERNAL_PREREQUISITE |
| AC-030 | App限定single-file profile、依存/CLI/manifest/docs allowlist同梱、PATH/SDK非依存、2 production project維持 | P-01/P-02 | `WindowsSingleFileProfileTests.*`（4件）+ `WindowsSingleFilePublishTests.Single_file_publish_script_parses_and_preserves_the_default_folder_flow` / `Publish_mode_uses_the_full_app_profile_only_when_opted_in` / `Extracted_bundle_layout_reuses_integrity_checks_without_requiring_static_host_files` + `WindowsSingleFileArtifactTests.Copy_unit_preserves_exact_bytes_generates_exact_hash_and_repackages_without_touching_workbooks` | PASS_REQUIRED |
| AC-031 | EXE/ZIPでcwd・日本語/空白path・相対`--input`・複数`--prompt`・invalid入力・明示適用・no-auto-run維持 | P-06/L-01/U-02 | `WindowsSingleFilePackageTests.Single_file_relative_input_and_two_prompts_are_observed_in_GUI_order` + `WindowsSingleFilePackageTests.Single_file_two_instances_share_cache_and_close_independently`（cwd/同時起動）+ Launch options既存required regressions（TR-18） | PASS_REQUIRED |
| AC-032 | 初回/再起動/同時起動/cache欠落/抽出障害でdata保護、setting.txt分離、明示出力先復元／null時result、同版EXE/ZIP再開条件維持 | P-06/W-02 + T06/T27/T28/T38 | WindowsSingleFilePackageTestsのT38/P06実EXE 7/7・P07 PASS_DEVELOPMENT（0.8.4、`artifacts/test/ui-settings/t38/native/`）。cache欠落復元、移動、file-valued抽出先、truncated bundleとdata保護を限定確認。disk-full／directory ACL／抽出中断／EXE・ZIP間checkpoint再開は未実施で、全faultのPASS_REQUIREDではない。設定復元はTR-34／ST-UC-27の新instance実file E2E、native保存・F02最終0.8.6とは分離 | VERIFIED_SCOPED |
| AC-033 | button起点の同梱CLI login開始、shell非使用、token非収集、排他/取消/失敗後継続、所有process限定終了、既存button再確認、自動AI実行なし | A-01/A-02/A-03/V-02 | deterministic: `BundledCopilotLoginServiceTests.Login_uses_only_the_explicit_path_fixed_arguments_and_unredirected_console` / `Cancellation_stops_only_owned_login_and_allows_retry_even_when_wait_ignores_token` / `Dispose_stops_owned_process_synchronously_without_touching_another_service` + `CopilotLoginCommandTests.Login_invalidates_old_identity_and_exit_zero_requires_explicit_authentication_check` / `Cancel_command_stops_only_owned_login_once_and_retry_remains_explicit` / `Run_and_run_cancellation_exclude_login_and_login_preserves_completed_run_state`; human required: CH-06本人login | NOT_RUN_EXTERNAL_PREREQUISITE |
| AC-034 | candidate/source/version/hash拘束、CH-01..06必須、metadata限定JSON、matrix v2のEXE／ZIP 2行閉集合、public 4 assets再download照合、欠落/FAIL/NOT_RUN拒否 | C-01/C-02/C-05/C-06/V-02 | `ReleaseMatrixBuilderTests.Final_generation_projects_exact_rows_and_passes_C02` + `Final_rejects_required_clean_host_non_pass_and_preserves_prior_candidate_output` + `ReleaseMatrixContractTests.V2_rejects_broken_schema_or_semantic_closure`（`ch06-*`拒否、`missing-clean-host`拒否）+ `ReleaseWorkflowContractTests.Publish_workflow_*`（protected publish contract） | NOT_RUN_EXTERNAL_PREREQUISITE |
| AC-035 | 共通＋定義1件の明示atomic保存、schema／IO失敗保持、header／貼付内容平文、明示出力先復元・null時だけresult | T02/T03/T06/T10/T23/T27、最新修正T28 | TR-34／ST-UC-27。ApplicationSettingsTests／SettingsFileStoreTests／SettingsViewModelTests／SettingsCompositionTests／ExecutionSettingsTests／MainWindowTests／SettingsWorkflowSystemTests。一時pathの実IOと新instance復元のみ | VERIFIED_SCOPED |
| AC-036 | 保存header metadataを検証した明示適用、成功時だけ一括更新、失敗・取消無変更、ID／Prompt／Imported Prompt保持 | T04/T10/T17/T24/T27、最新修正T28 | TR-35／ST-UC-28。SavedDefinitionApplicationTests／SettingsViewModelTests／SettingsViewTests／MainWindowTests／WorkflowStateTests／SettingsWorkflowSystemTests。授業内容の意味的一致や本人確認は証明しない | VERIFIED_SCOPED |
| AC-037 | 4step外の設定、選択・ページ・編集保持、model希望ID／実効選択、no fallback／no-auto-login/run、現在snapshot／前回結果不変 | T05〜10/T13〜27、最新修正T28 | TR-36／ST-UC-29。MainWindowSettingsTests／WorkflowStateTests／ExecutionSettingsTests／ResultsPresentationTests／SettingsWorkflowSystemTests。headlessと実file E2Eを分離。T39追加native FAILと本人確認NOT_RUN_EXTERNAL_PREREQUISITEは未解消 | VERIFIED_SCOPED |
| AC-038 | 中断後の再開準備、開始前検証、明示条件適用、有限close drain | W-01/W-02/U-03 | ResumeWorkflowTests 14件、ResumeAdmissionEvaluatorTests 8件（2026-09-24時点の件数。2026-09-17の局所記録は8件・2件） | VERIFIED_SCOPED |
| AC-039 | 今回ジョブのAI使用量を画面とJSONLで確認、未取得をゼロ化しない、項目別取得元・試行番号・終端結果・内訳不一致の保持、原単位維持 | J-01 + U-03 | JobUsageTrackerTests／SdkUsageAdapterTests／UsageProvenanceTests／AttemptOutcomeLoggingTests／JobCostBackendTests／JobCostViewTests／CostAttemptLifecycleTests。2026-09-18の局所記録を参照。実AI・課金照合・AIクレジット換算・nativeは未実施 | VERIFIED_SCOPED |
| AC-040 | 起動時の既存GitHubログイン自動確認、資格情報がない場合だけ1回の自動login、取消・失敗後の非再試行、環境変数での自動login抑止、AI・model fallbackの非自動化、token非収集 | U-03 + App composition | CopilotLoginCommandTestsの起動時自動ログイン試験（`Startup_*`、`Automatic_login_is_enabled_unless_the_environment_value_opts_out`、`Opening_the_window_runs_startup_authentication_exactly_once`）。実CLI・実ブラウザー・実資格情報は未実施 | VERIFIED_SCOPED |
| AC-041 | 結果画面の詳細の設問別得点を内部IDでなくExcel由来の設問文（空白正規化）で表示、空白のみの設問文は表示名・IDへ代替、詳細の基準editorに設問文の原文全文を表示、表示が採点・overrideを変更しない、一覧は5列だけで設問別得点を表示しない（FR-RS-06） | U-04 | ResultsQuestionTextTests、および既存ResultsPresentationTests／ResultsOutputViewTestsの設問別得点assert | VERIFIED_SCOPED |
| AC-042 | 実行中の速報値表示: 学生行と1:1の全件一覧、Prompt・criterion別の生値・固有・類似度、空回答・失敗・取消・再開の区別、上限と注記、checkpoint・logへの非保存 | U-04 | LivePreviewSchedulerTests／LivePreviewOrchestratorTests／LivePreviewViewModelTests／LivePreviewViewTests。実AI・native表示は未実施 | VERIFIED_SCOPED |
| AC-043 | 利用できる全modelの選択: SDK列挙の全件（`auto`含む）を順序どおり選択可能、`disabled`と不正IDだけ除外し他modelの列挙を継続、cache上限4096件と超過時の非保存・全件選択 | U-03 + App settings | CopilotModelEnumerationTests／ModelCatalogTests。実accountでの列挙確認は未実施 | VERIFIED_SCOPED |
| AC-044 | 利用者向けガイドの「設問の詳細」画面説明の構造（見出し、画像参照1回、ラベル、注意点） | DOC | DocumentationContractTests（Getting_started_explains_the_question_details_screen_for_teachers） | VERIFIED_SCOPED |
| AC-045 | 結果Excel説明文書の存在・リンク・固定文言と公開文書一覧の整合 | DOC | ResultExcelDescriptionTests／DocumentationContractTests／Packaging系試験 | VERIFIED_SCOPED |

| AC-046 | 実行準備部品（Copilot認証・再開・技術エラー）の入力画面への集約と、step状態文「設定済み」 | UI | PreparationOnInputTests／ExecutionViewTests／MainWindowTests／CopilotLoginCommandTests | VERIFIED_SCOPED |
| AC-047 | 共通設定へのCopilotログイン操作の追加 | UI | SettingsViewTests | VERIFIED_SCOPED |
| AC-048 | 通常評価の設問タブ（ComboBoxからの切替手段変更） | UI | EvaluatorSettingsViewTests／CompactWorkflowLayoutTests／MainWindowSettingsTests | VERIFIED_SCOPED |
| AC-049 | ログイン後の全model一覧、モデル別思考レベルとContext Size、対応値・容量・既定表示、明示保存と無効選択・再確認復旧、44 DIP・keyboard | UI + App settings | ModelOptionsTests／CopilotModelEnumerationTests／ModelCatalogTests／SettingsViewTests／SettingsAccessibilityTests。実accountは未実施 | VERIFIED_SCOPED |
| AC-050 | 同一runの全session・retryのeffort/tier統一、選択tier容量検査、実行中分離、checkpointのtier不一致拒否と明示復旧、Run/override出力のtier継承 | Copilot + Workflow + Workbook | EphemeralEvaluationRunnerTests／AuxiliaryEvaluationRunnerTests／CheckpointStoreTests／ResumeAdmissionEvaluatorTests／ResumeWorkflowTests／ResultsOutputViewTests／ResultExcelDescriptionTests | VERIFIED_SCOPED |
| AC-051 | 結果画面の詳細で、学生の回答（入力identity確認つきの選択行1行の再読込）と、AIの点・理由・引用・根拠の場所・評価項目の説明を表示。失敗文言・再試行・古い読込の破棄・非保存・採点不変 | UI + App workbook adapter | ResultsAnswerReviewTests、および既存ResultsQuestionTextTests／ResultsOutputViewTests／ResponsiveLayoutTests／PrimaryJourneyAccessibilityTests | VERIFIED_SCOPED |
| AC-052 | 評価項目の説明の初期値を評価方法の種類（Knowledge Cover／Prompt 分析）で決める。追加・評価方法追加・新規定義・入力画面の提案設問、種類変更・編集済みの説明の不変 | UI + Core | DefaultCriterionDescriptionTests | VERIFIED_SCOPED |
| AC-053 | 結果画面の見出し行に今回ジョブの総実行時間（`HH:MM:SS`）とAIクレジット（nano-AI units ÷ 1,000,000,000、小数4桁）。記録なし・AI送信なし・未取得・一部取得の区別、コスト要約・詳細・ジョブログ表示の換算値、通貨非表示、JSONL schema不変、1024×720での全文表示 | App Usage + UI | ResultsRunMetricsTests、JobUsageTrackerTests、MainWindowSettingsTests（1024×720・1180×800の実shell）、および既存ResultsOutputViewTests／ResponsiveLayoutTests／JobCostBackendTests | VERIFIED_SCOPED |
## Test requirement mapping

| TR | Requirement | Required evidence owner | Status |
|---|---|---|---|
| TR-01 | Forms synthetic row 1/2とprimary columnからquestion textへの同期 | X-01/U-01 tests | PASS_REQUIRED |
| TR-02 | sample structure/identity | X-01 E2E | PASS_REQUIRED |
| TR-03 | picker and format | U-01/input tests | PASS_REQUIRED |
| TR-04 | allocation | C-02/U-02 | PASS_REQUIRED |
| TR-05 | definition validation | C-02 | PASS_REQUIRED |
| TR-06 | normal/special Prompt/schema | C-05/A-03 | PASS_REQUIRED |
| TR-07 | reference once/auto/resume | A-02/W-02 | PASS_REQUIRED |
| TR-08 | score boundaries/empty/failure | C-03/A-03/A-04 | PASS_REQUIRED |
| TR-09 | independent score oracle | C-03 | PASS_REQUIRED |
| TR-10 | formula/ref/DAG/cached | C-04/X-03 | PASS_REQUIRED |
| TR-11 | final sheet set/preservation | X-02/X-03 | PASS_REQUIRED |
| TR-12 | path naming/atomic | X-04 | PASS_REQUIRED |
| TR-13 | checkpoint save/fault | W-01 | PASS_REQUIRED |
| TR-14 | resume mismatch/skip | W-02 | PASS_REQUIRED |
| TR-15 | AI failure/retry/cancel | A-02..04/W-02 | PASS_REQUIRED |
| TR-16 | selected-row/literal/no-content | C-05/A-02..04/X-03 | PASS_REQUIRED |
| TR-17 | 4step＋設定の通常非scroll／完全包含・ページ切替、長文／狭小／拡大例外、warning／focus／ID／virtualizationとInput binding同期 | MainWindowSettingsTests／PrimaryJourneyAccessibilityTests／EthicsWarningTests／ResponsiveLayoutTests／CompactWorkflowLayoutTests、T23/T25/T26／ST-UC-12。T26の27ケースは74件の内数。T39追加nativeは部分観測でFAIL、NarratorはNOT_RUN_EXTERNAL_PREREQUISITE | VERIFIED_SCOPED |
| TR-18 | launch options、設定へ移設したPrompt一覧／明示適用、no auto-run | 既存LaunchOptionsTests + ImportedPromptSettingsViewTests／SettingsViewTests／SettingsCompositionTests／CopilotLoginCommandTests、T16/T17/T23/T25／ST-UC-13。最終package起動は別判定 | VERIFIED_SCOPED |
| TR-19 | Windows publish/package/bundled CLI/clean launch | P-01 | PASS_REQUIRED |
| TR-20 | unsigned ZIP/hash/safe layout/reproducibility | P-01 | PASS_REQUIRED |
| TR-21 | unsupported platform/installer/signing claim exclusion | P-01/D-01..05 | PASS_REQUIRED |
| TR-22 | docs／UI設定契約／screenshots | T01/T28〜36/F02／ST-UC-16。T28の8枚生成・2回一致は0.8.4の記録。DocumentationContractTestsのT36は`artifacts/test/ui-settings/t36/t36-current.trx`で21/21・REVIEWED（公開文書11件／8画像contract）。T35対象4/4とは別scope。F02後の0.8.6文書再検証は実行記録の最新欄で別判定 | VERIFIED_SCOPED |
| TR-23 | fixed-seed new/resume E2E | E-01/E-02 | PASS_REQUIRED |
| TR-24 | optional live/recalculation | E-03 advisory（2026-09-02〜03の履歴はMIXED_ADVISORY） | NOT_RUN |
| TR-25 | 廃止（FR-059） | — | RETIRED |
| TR-26 | 廃止（FR-060） | — | RETIRED |
| TR-27 | 廃止（FR-060） | — | RETIRED |
| TR-28 | Windows ZIP launch/input不変 | P-01 | PASS_REQUIRED |
| TR-29 | publish matrix/secret/public re-download | P-04 | PASS_REQUIRED |
| TR-30 | App限定single-file profile/lock/publishと最終EXE package integrity（AC-029/030） | deterministic package tests（`WindowsSingleFileProfileTests.*`、`WindowsSingleFilePublishTests.*`、`WindowsSingleFileArtifactTests.*`） | PASS_REQUIRED |
| TR-31 | 実EXEの再起動/同時起動/cache欠落復元/移動/cwd/日本語path/data保護、未実施faultをPASS扱いしない（AC-031/032） | WindowsSingleFilePackageTestsのT38/P06 7/7、P07 PASS_DEVELOPMENT（0.8.4、`artifacts/test/ui-settings/t38/native/`）。disk-full／directory ACL／抽出中断／EXE・ZIP間checkpoint再開は未実施で、全faultのPASS_REQUIREDではない。fresh OS・本人login・native保存・F02最終0.8.6を成功扱いにしない | VERIFIED_SCOPED |
| TR-32 | login service/UI deterministic contract。本人login実測はCH-06で分離（AC-033） | deterministic UI/fake tests（`BundledCopilotLoginServiceTests.*`、`CopilotLoginCommandTests.*`） | NOT_RUN_EXTERNAL_PREREQUISITE |
| TR-33 | exact candidate EXE × CH-01..06 × matrix v2/public境界（AC-028/029/034） | C05/C06 protected workflow contracts（`ReleaseWorkflowContractTests.*`、`ReleaseMatrixContractTests.*`、`ReleaseMatrixBuilderTests.*`）+ V02 human clean-host evidence | NOT_RUN_EXTERNAL_PREREQUISITE |
| TR-34 | 設定明示保存・再読込、平文／非保存境界、schema・実IO拒否、旧bytes保持、明示出力先復元／null（AC-035） | ApplicationSettingsTests／SettingsFileStoreTests／SettingsViewModelTests／SettingsCompositionTests／ExecutionSettingsTests／MainWindowTests／SettingsWorkflowSystemTests、ST-UC-27。一時absolute pathの実IO。別process・nativeは未測定 | VERIFIED_SCOPED |
| TR-35 | 同入力／別header／不足sheet・列・行、明示適用成功と失敗・取消無変更、ID／canonical hash／Imported Prompt保持（AC-036） | SavedDefinitionApplicationTests／SettingsViewModelTests／SettingsViewTests／MainWindowTests／WorkflowStateTests／SettingsWorkflowSystemTests、ST-UC-28。read-only synthetic、最新216件の失敗時保持回帰。意味的適合・本人確認は別 | VERIFIED_SCOPED |
| TR-36 | 往復・交互編集・model希望、no-auto、現在run固定・停止・設定中完了、前回結果／override、保存→再読込→明示適用→fake run／resume E2E（AC-035〜037） | MainWindowSettingsTests／WorkflowStateTests／ExecutionSettingsTests／ResultsPresentationTests／SettingsWorkflowSystemTests、ST-UC-29。T24の12ケースとT27の7ケースは各集合の内数。T39追加native FAIL、本人walkthroughはNOT_RUN_EXTERNAL_PREREQUISITE | VERIFIED_SCOPED |
| TR-37 | 中断後の再開準備、partial picker取消、開始前の項目別検証、明示入力／model反映、有限close drain（AC-038） | ResumeWorkflowTests／ResumeAdmissionEvaluatorTests。実AI・別process・OS shutdownは別境界 | VERIFIED_SCOPED |
| TR-38 | 使用量の取得成功／一部欠落／全欠落、明示0と未取得、イベント重複・順序逆転・final複数通知、再試行と4 operation、項目別取得元、内訳不一致、下方訂正、overflow／負数、JSONLのcanary非記録・容量上限・保存失敗時の観測値保持（AC-039） | JobUsageTrackerTests／SdkUsageAdapterTests／UsageProvenanceTests／AttemptOutcomeLoggingTests／JobCostBackendTests／JobCostViewTests／CostAttemptLifecycleTests。symlink保護は通常のhostでは権限不足でSKIP、Windows Sandboxの管理者userではPASS（2026-09-24）、実AI・課金照合は別境界 | VERIFIED_SCOPED |
| TR-39 | 既存資格情報あり／なし、`CliUnavailable`等での非login、取消後の非再試行、進行中確認への合流、dispose後、確認失敗の封じ込め、環境変数解釈、Opened時の1回実行（AC-040） | CopilotLoginCommandTests。fake認証境界・fake login processのみで実CLI・実ブラウザーは別境界 | VERIFIED_SCOPED |
| TR-40 | 結果画面の詳細の設問別得点が正規化した設問文を使いID・表示名を含まないこと、一覧が5列だけで設問別得点の見出し・値・tooltipを含まないこと（10設問でも同じ）、空白正規化、空白のみの設問文の代替、未確定`—`、複数設問の順序と同一設問文の非統合、詳細の原文全文表示、表示による採点・出力・入力hashの不変（AC-041） | ResultsQuestionTextTests、および既存ResultsPresentationTests／ResultsOutputViewTests | VERIFIED_SCOPED |
| TR-41 | 速報値: 全行の待機中一覧、項目単位の更新順序、Promptと`RenderedPrompt`の一致、空回答・固有配点0・AI失敗・取消・行read失敗、再開前に完了した行、取消後の中断・未処理、run間・入力変更・破棄での消去、2,000／200文字上限と注記、20,000行の全件保持、canary非漏洩・checkpoint非保存・追加AI呼出し0、1180×800／1024×720の収まりと仮想化（AC-042） | LivePreviewSchedulerTests／LivePreviewOrchestratorTests／LivePreviewViewModelTests／LivePreviewViewTests。fake行source・fake AI・fake boundaryのみ。実AI・native表示は別境界 | VERIFIED_SCOPED |
| TR-42 | 全model一覧とモデル別思考レベル・Context Size: 対応metadata、全件と4096件境界、無効設定、保存・読込競合、全sessionとretry、checkpointのtier一致、出力と実行中の分離（AC-043／049／050） | ModelOptionsTests／CopilotModelEnumerationTests／ModelCatalogTests／EphemeralEvaluationRunnerTests／AuxiliaryEvaluationRunnerTests／CheckpointStoreTests／ResumeWorkflowTests | VERIFIED_SCOPED |
| TR-43 | 結果Excelの解説文書の列・項目・リンクと公開文書allowlist（AC-045） | ResultExcelDescriptionTests／DocumentationContractTests／Packaging系試験 | VERIFIED_SCOPED |
| TR-44 | 実行準備部品の入力画面集約、共有VM、focusとstep状態文（AC-046） | PreparationOnInputTests／MainWindowTests／ExecutionViewTests | VERIFIED_SCOPED |
| TR-45 | 通常評価の設問タブの表示・双方向選択・空状態・寸法・keyboard（AC-048） | EvaluatorSettingsViewTests／CompactWorkflowLayoutTests／MainWindowSettingsTests | VERIFIED_SCOPED |
| TR-46 | 結果画面の学生の回答と評価内容: 回答の書式・空欄・改行、詳細表示時だけの1行読込と行単位の記憶、読込前後のidentity確認、不一致・例外の文言と再試行、古い読込の破棄、理由・引用・根拠の場所・説明の全分岐、採点・override・出力の不変、canary非漏洩、合成`.xlsx`の実読込とbytes・更新日時の不変、1024×720の2欄配置（AC-051） | ResultsAnswerReviewTests。fake identity境界・fake行source・一時directoryの合成workbookのみ | VERIFIED_SCOPED |
| TR-47 | 評価項目の説明の初期値: 種類別本文の完全一致、評価項目・評価方法の追加、新規定義、入力画面の提案設問、種類変更と編集済みの説明の不変（AC-052） | DefaultCriterionDescriptionTests | VERIFIED_SCOPED |
| TR-48 | 結果画面の総実行時間とAIクレジット: 時間の書式（秒未満切捨て・100時間以上・未計測）、AIクレジットの換算・丸め・3桁区切り・`<0.0001`、見出しの5分岐とToolTip・Accessible Name・未読込時の非表示・再読込、コスト要約・詳細・ジョブログ表示、JSONLのcredit項目なし（AC-053） | ResultsRunMetricsTests、JobUsageTrackerTests。fakeの観測値と一時directoryのみ。実課金APIは使わない | VERIFIED_SCOPED |

## Mandatory safety surfaces

| Surface | Owner | Required negative evidence | Status |
|---|---|---|---|
| Input never modified | X-01/X-04/W-01 | success/failure/cancel/resume hash unchanged | PASS_REQUIRED |
| Empty is 0, failure is blank | C-03/X-03 | no runner for empty; no failure-to-zero | PASS_REQUIRED |
| Allocation exact 100 | C-02/X-02/X-03 | mismatch run blocked and formula blank | PASS_REQUIRED |
| Reference once/question/run | A-02/W-02 | no duplicate across resume | PASS_REQUIRED |
| `auto` no fallback | A-01/A-02/A-04 | auto absent causes preflight error | PASS_REQUIRED |
| Unpublished model limit | A-01/A-04 | null SDK limit starts the run, skips only the model-relative budget, and keeps the app-owned scalar ceiling | PASS_REQUIRED |
| Closed AI tools | A-02..04 | unknown/missing/duplicate/range/evidence rejected | PASS_REQUIRED |
| Selected same row only | C-05/A-02..04 | other row/column/path absent | PASS_REQUIRED |
| No content logs | all runtime | canary text absent | PASS_REQUIRED |
| Formula allowlist/ref/DAG | C-04/X-03 | raw/external/cycle/limits rejected | PASS_REQUIRED |
| Literal untrusted strings | X-02/X-03/W-01 | formula markers remain strings | PASS_REQUIRED |
| Atomic partial update | W-01 | old partial survives every pre-replace fault | PASS_REQUIRED |
| Closed resume identity | W-01/W-02 | each mismatch rejects without write | PASS_REQUIRED |
| Atomic final output | X-04 | valid final or no final | PASS_REQUIRED |
| Warning no dependency | U-04 | zero acknowledgement/gate state | PASS_REQUIRED |
| No CLI auto-run | L-01 | runner/session call count 0 after startup | PASS_REQUIRED |
| Bundled CLI pinning | A-01/P-01 | PATH fallback and hash mismatch rejected | PASS_REQUIRED |
| Platform claims | P-01/P-04/D-01..05 | Windows 11 x64のEXE／ZIPだけを対応表示し、他platformを公開しない | PASS_REQUIRED |

## Review protocol

各task review recordは少なくとも次を記録する。

- reviewed task IDとfile list
- target test/build result
- finding severity、reproduction、disposition
- rejected findingの明確な根拠
- applied fixとfollow-up result
- content data included = false

レビュー本文へ学生回答、Prompt、reference、AI reason/evidenceを含めない。

## Final gate prerequisites

1. AC-001〜037が`PASS_REQUIRED`、`PASS_MECHANISM`、`PASS_PRODUCTION`または正確な限定検証／未実施／block statusへ更新済み。`VERIFIED_SCOPED`は限定範囲だけで、NOT_RUNの残る受入条件を満たしたことにしない。
2. TR-01〜36がrequired evidenceまたは正確な限定検証／未実施／external statusへ接続済み。0.8.4の設定・T36文書・T38実EXEの限定成功と、T39追加native FAIL／本人未実施、F02後の0.8.6最終再検証を分ける。
3. 全required test、Release build、package testがPASS。
4. sample input identity不変。
5. unresolved reproducible blocker/high finding 0。
6. Windows ZIP regression、sidecar、bundled CLI、clean extract/launchが`PASS_REQUIRED`。
7. 廃止したAC-023〜025／TR-25〜27が`RETIRED`で、対応する実装・test・workflow stepが残っていない。
8. macOS、Linux、Windows Arm64、未実測artifactを対応済みと表示していない。
9. optional live Copilot／spreadsheet recalculationをrequired evidenceへ算入しない。
10. docsとscreenshotsが公開`v0.8.6` UI、旧公開`v0.8.1` ZIPとの境界、実在assetへ同期する。0.8.4のT28画像生成／T36文書contractの記録を保持し、CH-01..06とprotected publish workflowの未実施を成功へ読み替えない。
11. 公開済みpublic releaseは`v0.8.6`であり、単一EXEを含む4 assetを公開済み。ただしCH-01..06とprotected publish workflowは未実施のまま維持する。
