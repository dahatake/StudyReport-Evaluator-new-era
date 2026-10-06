# ADR-0017: 中断した実行の再開準備

| 項目 | 内容 |
|---|---|
| 状態 | ADOPTED |
| 決定日 | 2026-09-17 |
| 要求正本 | [requirements-definition.md](../../../docs/requirements-definition.md) §10.4〜10.6、AC-038、TR-37 |

## Decision

`.partial.xlsx`を再開状態の唯一の実体とする。中断後は同一セッションで明示的に再開準備でき、再起動後はnative pickerまたはfull pathで同じpartialを選ぶ。

再開条件は`ResumeAdmissionEvaluator`へ一元化し、実行前のread-only表示と実行時のadmissionが同じ判定を使う。入力・通常modelは利用者の明示操作でのみ合わせる。採点設計とruntimeは自動変更しない。再開準備・picker選択はAI実行を開始しない。

window closeでは新規送信を停止し、最大10秒だけ実行完了を待つ。待機後は閉じ、最後にatomic保存したpartialだけを再開元とする。

## Consequences

- 再開指定は`setting.txt`へ保存しない。
- 行途中の結果はcheckpointしない。再開時にはその行を最初から実行する。
- 出力先のpartial自動走査、checkpointの移動対応、runtime互換性の緩和は本決定に含めない。