using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public sealed class CalculationPropertiesWriter
{
    public void Write(SpreadsheetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The workbook part is missing.");
        Write(workbookPart);
    }

    public void Write(WorkbookPart workbookPart)
    {
        ArgumentNullException.ThrowIfNull(workbookPart);
        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("The workbook root is missing.");
        CalculationProperties properties = workbook.CalculationProperties
            ?? new CalculationProperties();
        properties.CalculationMode = CalculateModeValues.Auto;
        properties.FullCalculationOnLoad = true;
        properties.ForceFullCalculation = true;
        if (properties.Parent is null)
        {
            workbook.CalculationProperties = properties;
        }

        workbook.Save();
    }
}
