# サードパーティー素材の通知

本書は`src/StudyReportEvaluator.App/Resources/WorkflowIcons.axaml`に収録した、以下のFluent System Icons 6点だけを対象とします。アプリ本体、GitHub Copilot CLI／SDK、その他の依存物のライセンス・利用条件・再配布条件を置き換えたり、その適用範囲を広げたりするものではありません。特に、同梱CLIが本書のMIT Licenseで許諾されるという意味ではありません。

今回の素材追加は作業ツリーの変更であり、現在の公開版への収録や配布物の更新を示すものではありません。

## Fluent System Icons

- 出典：Microsoftの[Fluent System Iconsリポジトリ](https://github.com/microsoft/fluentui-system-icons)。
- 固定revision：[commit `5bae3fb7771054c252a54b1d9210e9c03439fa1b`](https://github.com/microsoft/fluentui-system-icons/commit/5bae3fb7771054c252a54b1d9210e9c03439fa1b)。上流の実commitと、各素材がこのcommitに存在することを確認しています。以下の原本URLはすべて同じcommitに固定しています。
- 取得・原本確認日：**2026-09-07**。素材やライセンスの発行日・更新日を意味しません。
- 種類：24px Regular、各原本の`viewBox="0 0 24 24"`。
- ライセンス：MIT License。**Copyright (c) 2020 Microsoft Corporation**。
- 一次本文：[LICENSE原本](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/5bae3fb7771054c252a54b1d9210e9c03439fa1b/LICENSE)、[NOTICE原本](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/5bae3fb7771054c252a54b1d9210e9c03439fa1b/NOTICE)。両方の全文を後掲します。

### 採用素材とリソースキー

| リソースキー | 用途 | SVG原本（source URL） |
|---|---|---|
| `WorkflowInputIcon` | 入力 | [ic_fluent_folder_open_24_regular.svg](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/5bae3fb7771054c252a54b1d9210e9c03439fa1b/assets/Folder%20Open/SVG/ic_fluent_folder_open_24_regular.svg) |
| `WorkflowDesignIcon` | 採点設計 | [ic_fluent_document_24_regular.svg](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/5bae3fb7771054c252a54b1d9210e9c03439fa1b/assets/Document/SVG/ic_fluent_document_24_regular.svg) |
| `WorkflowExecutionIcon` | 実行 | [ic_fluent_play_24_regular.svg](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/5bae3fb7771054c252a54b1d9210e9c03439fa1b/assets/Play/SVG/ic_fluent_play_24_regular.svg) |
| `WorkflowResultsIcon` | 結果 | [ic_fluent_checkmark_24_regular.svg](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/5bae3fb7771054c252a54b1d9210e9c03439fa1b/assets/Checkmark/SVG/ic_fluent_checkmark_24_regular.svg) |
| `WorkflowSettingsIcon` | 設定 | [ic_fluent_settings_24_regular.svg](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/5bae3fb7771054c252a54b1d9210e9c03439fa1b/assets/Settings/SVG/ic_fluent_settings_24_regular.svg) |
| `WorkflowSaveIcon` | 保存 | [ic_fluent_save_24_regular.svg](https://raw.githubusercontent.com/microsoft/fluentui-system-icons/5bae3fb7771054c252a54b1d9210e9c03439fa1b/assets/Save/SVG/ic_fluent_save_24_regular.svg) |

各SVGは単色の`path`を1個持ち、`transform`はありません。その`d`の座標・命令・全サブパスを保持し、静的な`StreamGeometry`に移しています。改行・インデントと、SVG既定のNonZero塗り規則を維持するための`F1`以外はpathデータを変更していません。複数のSVG path要素を省略・結合した素材はありません。原本の固定色`#212121`はGeometryへ埋め込まず、表示サイズと色は利用側のControlで指定します。日本語ラベルを併記し、結果アイコンだけで実行成功を示すものとはしません。

### LICENSE（原文全文）

```text
MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### 上流NOTICE（原文全文）

以下は同じrevisionのFluent System Iconsに付随するNOTICEを、その著作権表示・許諾文とともに保持したものです。原文中のMicrosoftによる記述を、本アプリや同梱CLIの新たな許諾条件として適用するものではありません。この素材追加で`replace`、`avocado`、`yargs`のツール本体や、SVG renderer、アイコンパッケージを依存関係へ追加・同梱していません。

```text
This project uses third party material from the projects listed below. The original copyright notice and the license under which Microsoft received such Third Party OSS, are set forth below. Such licenses and notices are provided for informational purposes only. Microsoft licenses the Third Party OSS to you under the licensing terms for the Microsoft product or service. Microsoft reserves all other rights not expressly granted under this agreement, whether by implication, estoppel or otherwise.

------------

replace

Copyright (c) 2020 Alessandro Maclaine

Provided for Informational Purposes Only

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the Software), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

------------

avocado

Copyright (c) 2020 Alex Lockwood

Provided for Informational Purposes Only

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the Software), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

------------

yargs

Copyright (c) 2020 yargs

Provided for Informational Purposes Only

MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the Software), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

------------
```