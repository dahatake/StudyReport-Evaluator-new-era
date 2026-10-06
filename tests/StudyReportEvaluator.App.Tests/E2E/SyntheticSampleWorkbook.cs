using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace StudyReportEvaluator.App.Tests.E2E;

// Requirements: FR-074 (AC-098)
// Generates the committed synthetic sample. It reproduces only the approved structure of the local
// sample (FR-006): 1 worksheet, A1:J531, header row 1, 530 data rows, D-I answer/prompt/supporting
// headers and A-C/J management headers. Every value is synthetic; no real student data is used.
internal static class SyntheticSampleWorkbook
{
    internal const string GenerateEnvironmentVariable = "STUDY_REPORT_EVALUATOR_GENERATE_SYNTHETIC_SAMPLE";
    internal const int DataRowCount = 530;

    internal static readonly string[] Headers =
    [
        "ID",
        "開始時刻",
        "メールアドレス",
        "設問1 レポート本文",
        "設問1 で使ったプロンプト",
        "設問1 プロンプトの工夫",
        "設問2 レポート本文",
        "設問2 で使ったプロンプト",
        "設問2 プロンプトの工夫",
        "合計点",
    ];

    internal static string CommittedPath => Path.Combine(
        E02RepositoryLayout.FindRepositoryRoot(),
        "tests",
        "fixtures",
        "synthetic-sample",
        "SyntheticSampleReport.xlsx");

    internal static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        using (SpreadsheetDocument document = SpreadsheetDocument.Create(temporary, SpreadsheetDocumentType.Workbook))
        {
            WorkbookPart workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            SharedStringTablePart sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
            SharedStringTable sharedStrings = new();
            Dictionary<string, int> stringIndexes = new(StringComparer.Ordinal);

            WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            SheetData sheetData = new();
            uint lastRow = DataRowCount + 1;
            worksheetPart.Worksheet = new Worksheet(
                new SheetDimension { Reference = $"A1:J{lastRow}" },
                sheetData);

            sheetData.Append(CreateRow(1, Headers.Select(header => (object?)header).ToArray(), sharedStrings, stringIndexes));
            for (int index = 1; index <= DataRowCount; index++)
            {
                sheetData.Append(CreateRow((uint)(index + 1), CreateDataValues(index), sharedStrings, stringIndexes));
            }

            sharedStringPart.SharedStringTable = sharedStrings;
            Sheets sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Form1",
            });
            workbookPart.Workbook.Save();
        }

        File.Move(temporary, path, overwrite: true);
    }

    private static object?[] CreateDataValues(int index)
    {
        // Every 41st respondent leaves question 1 blank and every 53rd leaves question 2 blank,
        // so the sample also contains empty answers (FR-019/FR-023 paths).
        bool blankFirst = index % 41 == 0;
        bool blankSecond = index % 53 == 0;
        return
        [
            (double)index,
            $"2026/04/{1 + (index % 28):00} {9 + (index % 8):00}:{index % 60:00}",
            $"respondent{index:000}@example.invalid",
            blankFirst ? null : $"合成回答 {index}: 設問1 では、教材の考え方を自分の言葉で説明し、例 {index % 7 + 1} を挙げて関係を述べる。",
            blankFirst ? null : $"合成プロンプト {index}: 設問1 の論点を {index % 3 + 2} 点に整理して説明してください。",
            blankFirst ? null : $"合成メモ {index}: 条件を具体化し、出力の形式を指定した。",
            blankSecond ? null : $"合成回答 {index}: 設問2 では、方法 {index % 5 + 1} の利点と制約を比較し、適用の場面を述べる。",
            blankSecond ? null : $"合成プロンプト {index}: 設問2 の比較の観点を表にまとめてください。",
            blankSecond ? null : $"合成メモ {index}: 観点を先に示し、例を求めた。",
            null,
        ];
    }

    private static Row CreateRow(
        uint rowIndex,
        object?[] values,
        SharedStringTable sharedStrings,
        Dictionary<string, int> stringIndexes)
    {
        Row row = new() { RowIndex = rowIndex };
        for (int column = 0; column < values.Length; column++)
        {
            string reference = $"{(char)('A' + column)}{rowIndex}";
            switch (values[column])
            {
                case null:
                    break;
                case double number:
                    row.Append(new Cell
                    {
                        CellReference = reference,
                        DataType = CellValues.Number,
                        CellValue = new CellValue(number),
                    });
                    break;
                case string text:
                    if (!stringIndexes.TryGetValue(text, out int stringIndex))
                    {
                        stringIndex = stringIndexes.Count;
                        stringIndexes.Add(text, stringIndex);
                        sharedStrings.Append(new SharedStringItem(new Text(text)));
                    }

                    row.Append(new Cell
                    {
                        CellReference = reference,
                        DataType = CellValues.SharedString,
                        CellValue = new CellValue(stringIndex),
                    });
                    break;
                default:
                    throw new InvalidOperationException("Unsupported synthetic cell value.");
            }
        }

        return row;
    }
}
