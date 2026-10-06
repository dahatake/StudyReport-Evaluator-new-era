# work 保存記録

このディレクトリは、一時ファイル置場ではなく、リポジトリで正式に保持する過去の計画・調査・実行記録です。記録の移動は、当時の未完了・失敗・限定的成功を昇格させず、新たなテスト成功やリリース完了を意味しません。

- 保存日: **2026-09-15**
- 原本スナップショット（`sourceCommit`）: `77d1dd6eee858765a619e36b6ba5f27dd9255360`。現在のHEADが変わっても、この基準を使用します。
- 対象: 上記コミットの `git ls-tree -r --name-only <sourceCommit> -- work` に含まれる追跡済み26件。`work` から `dev/docs/archive/work` へ移動済みです。
- 並行作業の `work/20260915-github-actions-windows-msix-investigation-report.md` は対象外です。この台帳追加ではルートの `README.md` を変更していません。
- 現在の判断には [要求定義の正本](../../../../docs/requirements-definition.md) と [開発状況](../../implementation-status.md) を参照してください。過去の [UI設定実行記録](20260907-ui-settings-execution-record.md) などは、当時の条件・範囲に限定して読みます。

## 原本と保存先の照合

[manifest.json](manifest.json)（`schemaVersion: 1`）に、名前順の全26件のバイト数とSHA-256を記録しています。

- `originalBytes` / `originalSha256`: 固定コミットの **RAW Git blob** の値です。当時の作業ツリーのバイト列ではありません。Gitの標準出力をPythonの `subprocess` で生バイトとして取得し、文字列変換・改行正規化なしで計算しました。
- `archivedBytes` / `archivedSha256`: 保存時点の移動先ディスク上の生バイトの値です。こちらも改行正規化なしで計算しました。将来のチェックアウトで改行が変わると、この値とは異なる場合があります。
- XLSX 1件は **786,819 bytes** で原本と完全一致し、SHA-256は双方とも `913f1f0f4e94a8e47a74182c171903dc2ea2038af48c0aa214ddbd7f83a1ccd3` です。内容の解析・セルの読取りは行っていません。
- JSON 4件は内容変更なしです。ただし原本のLFが移動先ではCRLFとなり、生バイトのハッシュは異なります。CRLF→LFだけを行った比較では4件とも一致しました。

| JSON記録 | 原本 bytes | 保存先 bytes | LF→CRLFの件数 |
| --- | ---: | ---: | ---: |
| application-run-evidence | 4,576 | 4,707 | 131 |
| recalculation-evidence | 2,123 | 2,194 | 71 |
| system-test-evidence | 2,842 | 2,935 | 93 |
| version-management-investigation-evidence | 3,436 | 3,523 | 87 |

Markdown 21件も原本はLF、移動先はCRLFです。9件は改行差のみ、12件は改行差に加えて以下の参照調整を含みます。全26件のうち、生バイト完全一致は1件、改行差のみは13件、参照調整を含むものは12件です。

## 移動時の調整範囲

- Markdownは相対リンクの移動先への適応に限定し、例外としてGit管理外の生成物へのリンク4件を元パス付きのテキスト注記にしました。
- 廃止された `tests/SystemTest-prompt.md` への参照1件は当時のパスを残し、現行正本へのリンクを併記しました。
- コード、コマンド、ハッシュ表など履歴内の `work` パスは、当時の記録として意図的に保持しています。
- `#L` を含む行参照は、当時のソース行位置を示すものです。現在のMarkdown見出しアンカーや、現行ソースの同じ行の内容を保証するものではありません。
- 原本照合は保存バイトと参照調整の確認です。過去の業務テストや配布物の新規検証を意味しません。

## 移設の検証（2026-09-15）

- `DocumentationContractTests.cs`: 21件成功、失敗0件。既存の検査範囲や期待値を緩和せず、保管先を含む開発文書のリンクも検査しました。
- `dev/docs` 配下と要求定義書の59文書、ローカルリンク720件を別途確認し、参照先欠落0件。Git管理対象または今回の追加ファイルを参照していることも確認し、ローカル生成物の存在だけを成功条件にしていません。
- 台帳26件の原本・保存先のバイト数とSHA-256を再照合し、不一致0件。旧26パスは移設済みで、そこへ向く有効なMarkdownリンクは0件です。履歴のコマンド・原本ハッシュ表の旧パスは上記方針で保持しています。
- `git diff --check` は成功しました。フルソリューション試験、実AI、実利用者操作、パッケージ再生成・公開は、この移設の検証では実施していません。

## 全26件のインベントリ

以下は `manifest.json` の `files` と同じ名前順です。

1. [20260831-implementation-plan.md](20260831-implementation-plan.md)
2. [20260901-2205-SystemTest-Report.md](20260901-2205-SystemTest-Report.md)
3. [20260901-realdata-application-result.xlsx](20260901-realdata-application-result.xlsx)
4. [20260901-realdata-application-run-evidence.json](20260901-realdata-application-run-evidence.json)
5. [20260901-realdata-application-run-report.md](20260901-realdata-application-run-report.md)
6. [20260901-realdata-error-investigation-report.md](20260901-realdata-error-investigation-report.md)
7. [20260901-realdata-recalculation-evidence.json](20260901-realdata-recalculation-evidence.json)
8. [20260901-realdata-system-test-evidence.json](20260901-realdata-system-test-evidence.json)
9. [20260901-realdata-system-test-report.md](20260901-realdata-system-test-report.md)
10. [20260901-v4-implementation-plan.md](20260901-v4-implementation-plan.md)
11. [20260902-readme-end-user-release-plan.md](20260902-readme-end-user-release-plan.md)
12. [20260902-version-management-investigation-evidence.json](20260902-version-management-investigation-evidence.json)
13. [20260902-version-management-investigation-report.md](20260902-version-management-investigation-report.md)
14. [20260903-0605-TaskExecutionPlan.md](20260903-0605-TaskExecutionPlan.md)
15. [20260903-0923-windows-macos-setup-simplification-plan.md](20260903-0923-windows-macos-setup-simplification-plan.md)
16. [20260903-1655-TaskExecutionPlan.md](20260903-1655-TaskExecutionPlan.md)
17. [20260903-setup-simplification-execution-record.md](20260903-setup-simplification-execution-record.md)
18. [20260903-v1.0.1-release-recovery-plan.md](20260903-v1.0.1-release-recovery-plan.md)
19. [20260904-publication-remediation-execution-record.md](20260904-publication-remediation-execution-record.md)
20. [20260904-publication-remediation-plan.md](20260904-publication-remediation-plan.md)
21. [20260905-adversarial-review.md](20260905-adversarial-review.md)
22. [20260906-0617-one-action-startup-plan.md](20260906-0617-one-action-startup-plan.md)
23. [20260906-one-action-startup-execution.md](20260906-one-action-startup-execution.md)
24. [20260907-ui-settings-execution-record.md](20260907-ui-settings-execution-record.md)
25. [20260907-ui-settings-redesign-plan-v2.md](20260907-ui-settings-redesign-plan-v2.md)
26. [20260907-ui-settings-redesign-plan.md](20260907-ui-settings-redesign-plan.md)
