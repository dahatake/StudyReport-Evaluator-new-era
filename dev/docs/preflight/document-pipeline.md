# G-16 DocumentPipeline preflight evidence

> [!WARNING]
> **HISTORICAL PREFLIGHT:** requirements v1.xの隔離spike記録です。現在のproduction implementation statusやGATE-ACCEPTANCEを表しません。現行状態は[`dev/docs/implementation-status.md`](../implementation-status.md)を参照してください。

| 項目 | 内容 |
|---|---|
| Task | G-16 |
| 状態 | **G-16完了・実測/敵対的review PASS** |
| Decisions | ADR-0003、ADR-0004（本実測によるsentinel訂正を含む） |
| Data | fixed synthetic data only / personal dataなし |
| Network | spike実行中のnetwork APIなし。NuGet restoreとLibreOffice取得は事前準備として分離 |
| Production scope | `spikes/`だけ。`src/` production file 0 |
| 実測日 | 2026-08-31 |

## Scope

このspikeは、GATE-0前に許可された隔離技術検証として次だけを実測した。

1. synthetic `.xlsx`をbyte-copyしたworking copyへ、ADR-0003のallowlistに従って`Eval`、`Evaluation_Config`、`Evaluation_Run`を追加する。
2. allowlist外の既存ZIP entry payload、relationship tuple、content typeを保持し、Open XML schema validationを行う。
3. untrusted文字列30件をinline stringとして逐語round-tripし、formula/hyperlink/external relationshipを生成しない。
4. app-local ICUでG-11 Unicode 15 caseをNFKC、line ending、Unicode White_Space、Rune 3-shingleの順に検証する。
5. 同じ`ROUND` workbookをMicrosoft ExcelとLibreOffice Calcで再計算し、共通safe `rounding_digits` rangeを実測する。

production reader/writer、UI、checkpoint、atomic output commit、macOS/Linux supportを実装・実証したものではない。

## Source and dependency identity

| Artifact | Version / bytes | SHA-256 |
|---|---:|---|
| `spikes/DocumentPipeline/DocumentPipeline.csproj` | 830 bytes | `C6A0D53BACB24FD638A98BA792F56E46A82AF0D87FF4012AF1A9A85CD8E96D55` |
| `spikes/DocumentPipeline/Program.cs` | 54,185 bytes | `950F874AFFB509E65DD3D28A4ED79BD37B2D05BA0F65B1CE02999E1EEB98D855` |
| `DocumentFormat.OpenXml` NuGet | 3.5.1 / 15,375,355 bytes | `71375A11A53EEB554005477CE6CA127909AEBED474900E92298360B49A68307F` |
| `Microsoft.ICU.ICU4C.Runtime` NuGet | 72.1.0.3 / 33,824 bytes | `CFE928E8740A3DF636EAA09BD93D89126883F338F01B15E867F48704E6126449` |
| LibreOffice MSI | 26.8.0 / official MSI | `4AA6C6E1895F4055104EFFCB556BD3362D20C6AD707C149543304F395EF9DB95` |

`DocumentFormat.OpenXml` assembly informational versionは`3.5.1+Branch.main.Sha.3139fdfd27414548a41555f7848d5728f6e71a42.3139fdfd27414548a41555f7848d5728f6e71a42`だった。app-local ICUはruntime host option `System.Globalization.AppLocalIcu=72.1`とexact package versionで固定した。

## Execution environment

| Surface | Measured value | Binary SHA-256 |
|---|---|---|
| OS | Microsoft Windows NT 10.0.29648.0 / win-x64 | n/a |
| PowerShell | 7.6.5 Core | n/a |
| .NET SDK | 10.0.400 | n/a |
| .NET runtime | 10.0.11 | n/a |
| Microsoft Excel | 16.0.20402.20050 | `15515DC7E93DEF18C403531CA1FACFC83EA52EF5AE6C87D250EED2ADABB39B6D` (`EXCEL.EXE`) |
| LibreOffice Calc | 26.8.0.3 | `6E3E16A5C8338138EA14666F7873B8F909EEBF266FCACB4BB840906CA115C62E` (`soffice.bin`) |

LibreOfficeは端末へinstallせず、winget catalogが示したThe Document FoundationのHTTPS URLからMSIをtempへ取得し、catalog SHA-256と一致した後に`msiexec /a`でtempへ展開した。system-wide install、UAC、管理者権限は使用していない。

## Local Open XML result

| Check | Actual result |
|---|---|
| Build | Release build、warning/error 0 |
| Input immutability | byte count、last-write UTC ticks、SHA-256がprobe前後で一致 |
| Package entries | 8 → 11 |
| Added entries | `xl/worksheets/sheet2.xml`、`sheet3.xml`、`sheet4.xml`だけ |
| Changed existing entries | `[Content_Types].xml`、`xl/_rels/workbook.xml.rels`、`xl/workbook.xml`だけ |
| Existing relationships | 5件を全保持 |
| Added relationships | workbook sourceのinternal worksheet relationship 3件だけ |
| Sheet names | `Original`、`Eval`、`Evaluation_Config`、`Evaluation_Run` |
| Original worksheet | payload hash不変 |
| Shared strings | payload hash不変 |
| Styles | payload hash不変 |
| Calculation chain | payload hash不変 |
| Calculation properties | `calcMode=auto`、`fullCalcOnLoad=1`、`forceFullCalc=1` |
| Open XML validation | Office2019 validator error 0 |
| Formula cells | 1 existing + 801 result + 801 zero sentinel = 1,603 |
| Untrusted strings | 30/30 exact round-trip、formula node 0、hyperlink 0、external relationship 0 |
| Unicode | 15/15 PASS |
| ICU mode | true、configured 72.1 |

Open XML SDKの既定XML writerは先頭CRをliteral CRLFとして書き、XML parser再読時にLFへ正規化した。このため`Eval` worksheetだけは`XmlWriterSettings.NewLineHandling=Entitize`で書き、CRをcharacter referenceとして保持した。修正後はFI-001〜FI-030が全て逐語一致した。

## Cross-engine ROUND measurement

同じworkbookへ非整数値`1.23456789012345`と`rounding_digits=-400..400`を置き、各engineでfull calculation後に保存し、Open XMLからcached resultを再読した。

| Engine | Probe count | Numeric success | Formula error | Open XML error | Successful range |
|---|---:|---:|---:|---:|---|
| Excel 16.0.20402.20050 | 801 | 801 | 0 | 0 | `-400..400` |
| LibreOffice 26.8.0.3 | 801 | 41 | 760 | 0 | `-20..20` |
| Intersection | 41 | 41 | 0 within range | 0 | **`-20..20`** |

共通41点のcached numeric valueは`double.Parse(..., InvariantCulture)`後のIEEE 754 binary64 bit patternで全件一致し、mismatchは0件だった。境界は次のとおり。

| `rounding_digits` | Excel | LibreOffice |
|---:|---|---|
| -21 | numeric | formula error |
| -20 | numeric | numeric |
| 20 | numeric | numeric |
| 21 | numeric | formula error |

このengine組合せで採用するinclusive共通rangeは**-20〜20**とする。G-17は同一version/binaryを対象platformで確認し、成立しなければrangeを狭めるか当該platformをfailにする。未試験versionへこのrangeを流用しない。

### Zero sentinel defect

ADR-0004旧案の`ROUND(0,rounding_digits)=0`は、LibreOfficeで非整数resultがerrorとなった`-400`、`-21`、`21`、`400`でもtrueだった。したがってrange検証には使用できない。production validation formulaからは削除し、G-16 workbookには欠陥を継続再現するprobeとしてだけ残した。ADR-0004を次のように訂正した。

- G-16/G-17が固定したversioned lower/upper boundをapp-owned Config cellから参照する。
- finite integerかつinclusive range内であることを外側の`IF`で確認した後だけ`ROUND`分岐へ進む。
- formula sentinelをrange認可に使わない。
- engine保存後のformula error検査はE-07で独立に行う。

## Final evidence identity

正式採用したfresh runのtemp evidence identityを以下に記録する。raw engine result 801×2件はリポジトリへcommitせず、spikeの`run`、`inspect`、`compare` commandから再生成する。

| Artifact | Bytes | SHA-256 |
|---|---:|---|
| `g16-local-evidence.json` | 3,486 | `84014A9AC954D9784366F786C90CC11EE5B9047EC0740F0EC3E49FB58F354215` |
| `g16-excel-evidence.json` | 115,572 | `B3BF156B13C4C8ABFBB8DDBEC954B80AD0910C75BCFFCA6C5D971A9700292945` |
| `g16-libreoffice-evidence.json` | 113,541 | `6A4F473604DC00E4F7D47CB8D79B15C63C1CEB096F3AA1015DD2447F9C0C7912` |
| `g16-cross-engine-evidence.json` | 1,334 | `07A5F00AE48DE074FB03CF12C3750449464D2F5461FCFCA7F507A6089F60CBBC` |
| synthetic input workbook | 2,764 | `A6D16B2C78682E00D753181560D11FD11D53CA8EE8B240FBF13C2A042A9B6CAA` |
| app output workbook | 25,324 | `470270FF853A62593128991A4587462865EE82E1C1E059F6DE389F5F4FB1575C` |
| Excel-saved workbook | 45,965 | `B9E56746F5D1011150B326926D2734970A1E48D2DEA6A2233248627FD249631A` |
| LibreOffice-saved workbook | 37,521 | `58A3AEBF5BA1A9AC466AADD0ADFF038A0F8580C73FE4D1D20B56A779F8CDE911` |

Evidence root at measurement time: `%TEMP%/StudyReportEvaluator-G16-final`. Path is diagnostic provenance, not a durable repository artifact or support claim. Evidence JSON contains synthetic values only; no token, credential, student content, username field, or network payload is recorded.

## Reproduction contract

- `run <repository-root> <work-directory>` generates the synthetic input, copy-on-write output and local evidence.
- Open/save a copy of output with the exact measured engine and request full calculation. Excel automation used links update off, macro automation security force-disabled, hidden UI and no alert interaction. LibreOffice used a fresh `UserInstallation` profile and headless OOXML conversion.
- `inspect <workbook> <engine> <engine-version>` records all 801 result/type pairs and validates the engine-saved package.
- `compare <first-engine-evidence> <second-engine-evidence>` requires equal digit sets, derives contiguous success ranges, compares the common range bit-exactly and records the four boundary points.

## Remaining boundaries

- G-16 does not establish macOS/Linux support, vault, print, package signing or notarization; those remain G-17/EXT-07 blockers.
- LibreOffice was measured on Windows only. Ubuntu/macOS engine/platform evidence remains G-17 work.
- Existing `calcChain` was preserved through app mutation and full recalculation succeeded in both measured engines. This does not guarantee every workbook graph; E-07 must use release golden workbooks.
- NTFS atomic rename/durability, crash and network filesystem behavior remain T-10/O-09〜O-11/G-17 work.
- The protected `sample/` was not opened, decrypted or used.

## Adversarial review record

- 初回source reviewで、Config/Run worksheetのCR保持writer未適用、content-type PartName未照合、relationship ID/Target未照合、calcChainの暗黙検査、formula 1-cell長未検査を確認した。
- 3新規worksheetすべてを`NewLineHandling=Entitize`で保存し、3つのexact relationship ID/relative Target、3つのexact content-type PartName、calcChain payload hash、formula 8,192文字未満を明示assertするよう修正した。
- Open XML SDKが新規worksheet relationshipを`/xl/worksheets/...`として生成する実挙動を検出し、document close後に`xl/_rels/workbook.xml.rels`のapp-owned 3 relationshipだけをADR-0003所定の`worksheets/...`へ変更した。既存relationship tupleには触れない。
- 修正後fresh chainでbuild warning/error 0、local validation error 0、inline 30/30、Unicode 15/15、Excel 801/801、LibreOffice 41/801、共通`-20..20`、bit mismatch 0を再確認した。
- evidence reviewで確認した「sentinel削除」の曖昧さは、production validation/adoption formulaから削除しG-16欠陥再現probeにだけ残す、とADR-0004と本書へ明記した。
- Windows限定とtemp evidenceの非永続性は既存`Remaining boundaries`と`Reproduction contract`で明示している。macOS/Linuxを本taskのPASSとして扱わない。

## Completion boundary

The executable spike demonstrated the three G-16 criteria on the measured Windows environment: minimal synthetic XLSX mutation with a closed package delta, app-local ICU normalization, and Excel/LibreOffice recalculation. The source and evidence claims passed adversarial review after the recorded fixes, so G-16 is complete. This does not generalize platform support or remove G-17/E-07 work. GATE-0 remains blocked by G-12〜G-15、G-17、G-18 and applicable external inputs.
