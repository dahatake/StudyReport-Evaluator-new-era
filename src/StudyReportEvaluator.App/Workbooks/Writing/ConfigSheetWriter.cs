using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using StudyReportEvaluator.Core.Domain;
using StudyReportEvaluator.Core.Formulas;
using StudyReportEvaluator.Core.Serialization;
using SpreadsheetText = DocumentFormat.OpenXml.Spreadsheet.Text;

namespace StudyReportEvaluator.App.Workbooks.Writing;

public sealed class ConfigCellAddressMap
{
    internal ConfigCellAddressMap(
        string sheetName,
        FormulaCellAddress roundingDigitsCell,
        FormulaCellAddress basePointsCell,
        FormulaCellAddress specialPointsCell,
        FormulaCellAddress similarityPenaltyWeightCell,
        FormulaCellAddress allocationTotalCell,
        FormulaCellAddress allocationValidCell,
        IDictionary<string, FormulaCellAddress> questionWeightCells,
        IDictionary<string, FormulaCellAddress> evaluatorWeightCells,
        IDictionary<string, FormulaCellAddress> evaluatorMinimumCells,
        IDictionary<string, FormulaCellAddress> evaluatorMaximumCells,
        IDictionary<string, FormulaCellAddress> criterionWeightCells,
        IDictionary<string, FormulaCellAddress> criterionMinimumCells,
        IDictionary<string, FormulaCellAddress> criterionMaximumCells,
        IEnumerable<FormulaCellAddress> verifiedCells,
        IEnumerable<WrittenFormulaCell> formulaCells)
    {
        SheetName = sheetName;
        RoundingDigitsCell = roundingDigitsCell;
        BasePointsCell = basePointsCell;
        SpecialPointsCell = specialPointsCell;
        SimilarityPenaltyWeightCell = similarityPenaltyWeightCell;
        AllocationTotalCell = allocationTotalCell;
        AllocationValidCell = allocationValidCell;
        QuestionPointsCells = Copy(questionWeightCells);
        EvaluatorWeightCells = Copy(evaluatorWeightCells);
        EvaluatorMinimumCells = Copy(evaluatorMinimumCells);
        EvaluatorMaximumCells = Copy(evaluatorMaximumCells);
        CriterionWeightCells = Copy(criterionWeightCells);
        CriterionMinimumCells = Copy(criterionMinimumCells);
        CriterionMaximumCells = Copy(criterionMaximumCells);
        VerifiedCells = Array.AsReadOnly(verifiedCells.ToArray());
        FormulaCells = formulaCells.ToImmutableArray();
    }

    public string SheetName { get; }

    public FormulaCellAddress RoundingDigitsCell { get; }

    public FormulaCellAddress BasePointsCell { get; }

    public FormulaCellAddress SpecialPointsCell { get; }

    public FormulaCellAddress SimilarityPenaltyWeightCell { get; }

    public FormulaCellAddress AllocationTotalCell { get; }

    public FormulaCellAddress AllocationValidCell { get; }

    public IReadOnlyDictionary<string, FormulaCellAddress> QuestionPointsCells { get; }

    public IReadOnlyDictionary<string, FormulaCellAddress> EvaluatorWeightCells { get; }

    public IReadOnlyDictionary<string, FormulaCellAddress> EvaluatorMinimumCells { get; }

    public IReadOnlyDictionary<string, FormulaCellAddress> EvaluatorMaximumCells { get; }

    public IReadOnlyDictionary<string, FormulaCellAddress> CriterionWeightCells { get; }

    public IReadOnlyDictionary<string, FormulaCellAddress> CriterionMinimumCells { get; }

    public IReadOnlyDictionary<string, FormulaCellAddress> CriterionMaximumCells { get; }

    public IReadOnlyList<FormulaCellAddress> VerifiedCells { get; }

    public ImmutableArray<WrittenFormulaCell> FormulaCells { get; }

    public override string ToString() =>
        $"{nameof(ConfigCellAddressMap)} {{ VerifiedCellCount = {VerifiedCells.Count}, Content = <redacted> }}";

    private static IReadOnlyDictionary<string, FormulaCellAddress> Copy(
        IDictionary<string, FormulaCellAddress> source) =>
        new ReadOnlyDictionary<string, FormulaCellAddress>(
            new Dictionary<string, FormulaCellAddress>(source, StringComparer.Ordinal));
}

public sealed class ConfigSheetWriter
{
    public const int MaximumCellCharacters = 32_767;
    public const int SnapshotChunkCharacters = 30_000;
    public const int MaximumExcelRows = 1_048_576;

    private const string RecordTypeColumn = "A";
    private const string PathColumn = "B";
    private const string NodeIdColumn = "C";
    private const string ParentNodeIdColumn = "D";
    private const string DisplayNameColumn = "E";
    private const string RevisionColumn = "F";
    private const string ContentTextColumn = "G";
    private const string PromptTemplateColumn = "H";
    private const string BuiltInTemplateVersionColumn = "I";
    private const string EvaluatorTypeColumn = "J";
    private const string SourceSheetColumn = "K";
    private const string HeaderRowColumn = "L";
    private const string FirstDataRowColumn = "M";
    private const string LastDataRowColumn = "N";
    private const string PrimarySourceColumnColumn = "O";
    private const string SupportingSourceColumnColumn = "P";
    private const string EnabledColumn = "Q";
    private const string RawWeightColumn = "R";
    private const string EffectiveMinimumColumn = "S";
    private const string EffectiveMaximumColumn = "T";
    private const string RoundingDigitsColumn = "U";
    private const string SchemaVersionColumn = "V";
    private const string DefinitionSha256Column = "W";
    private const string ChunkIndexColumn = "X";
    private const string SnapshotChunkColumn = "Y";
    private const string BasePointsColumn = "Z";
    private const string SpecialPointsColumn = "AA";
    private const string SimilarityPenaltyWeightColumn = "AB";
    private const string QuestionPointsColumn = "AC";
    private const string AllocationTotalColumn = "AD";
    private const string AllocationValidColumn = "AE";
    private const string SpecialPrimarySourceColumn = "AF";
    private const string SpecialSupportingSourceColumn = "AG";

    private readonly FormulaCellWriter formulaCellWriter = new();

    public ConfigCellAddressMap Write(
        SpreadsheetDocument document,
        QuantificationSnapshot snapshot,
        AppOwnedSheetNames sheetNames)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sheetNames);
        WorkbookPart workbookPart = document.WorkbookPart
            ?? throw new InvalidDataException("The workbook part is missing.");
        return Write(workbookPart, snapshot, sheetNames.ConfigSheetName);
    }

    public ConfigCellAddressMap Write(
        WorkbookPart workbookPart,
        QuantificationSnapshot snapshot,
        string sheetName)
    {
        ArgumentNullException.ThrowIfNull(workbookPart);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        if (!snapshot.HasValidHash())
        {
            throw new InvalidDataException("The quantification snapshot hash is invalid.");
        }

        SheetData sheetData = new();
        uint rowNumber = 1;
        sheetData.Append(CreateHeaderRow(rowNumber++));

        QuantificationDefinition definition = snapshot.Definition;
        uint definitionRowNumber = rowNumber++;
        Row definitionRow = CreateDefinitionRow(definitionRowNumber, definition, snapshot);
        sheetData.Append(definitionRow);

        foreach ((int index, string chunk) in Chunk(snapshot.CanonicalJson).Select((value, index) => (index, value)))
        {
            Row row = NewRow(rowNumber);
            AppendInline(row, RecordTypeColumn, rowNumber, "CANONICAL_JSON");
            AppendInline(row, PathColumn, rowNumber, "$");
            AppendInline(row, NodeIdColumn, rowNumber, definition.Id);
            AppendNumber(row, ChunkIndexColumn, rowNumber, index + 1);
            AppendInline(row, SnapshotChunkColumn, rowNumber, chunk);
            sheetData.Append(row);
            rowNumber++;
        }

        Dictionary<string, FormulaCellAddress> questionPoints = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> evaluatorWeights = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> evaluatorMinimums = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> evaluatorMaximums = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> criterionWeights = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> criterionMinimums = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> criterionMaximums = new(StringComparer.Ordinal);
        List<FormulaCellAddress> verifiedCells =
        [
            Address(sheetName, RoundingDigitsColumn, definitionRowNumber),
            Address(sheetName, BasePointsColumn, definitionRowNumber),
            Address(sheetName, SpecialPointsColumn, definitionRowNumber),
            Address(sheetName, SimilarityPenaltyWeightColumn, definitionRowNumber),
            Address(sheetName, AllocationTotalColumn, definitionRowNumber),
            Address(sheetName, AllocationValidColumn, definitionRowNumber),
        ];

        for (int questionIndex = 0; questionIndex < definition.Questions.Length; questionIndex++)
        {
            QuestionDefinition question = definition.Questions[questionIndex];
            string questionPath = $"$.questions[{questionIndex.ToString(CultureInfo.InvariantCulture)}]";
            uint questionRowNumber = rowNumber++;
            Row questionRow = NewRow(questionRowNumber);
            AppendInline(questionRow, RecordTypeColumn, questionRowNumber, "QUESTION");
            AppendInline(questionRow, PathColumn, questionRowNumber, questionPath);
            AppendInline(questionRow, NodeIdColumn, questionRowNumber, question.Id);
            AppendInline(questionRow, ParentNodeIdColumn, questionRowNumber, definition.Id);
            AppendInline(questionRow, DisplayNameColumn, questionRowNumber, question.DisplayName);
            AppendInline(questionRow, ContentTextColumn, questionRowNumber, question.QuestionText);
            AppendInline(questionRow, PrimarySourceColumnColumn, questionRowNumber, question.PrimarySourceColumn);
            AppendBoolean(questionRow, EnabledColumn, questionRowNumber, question.Enabled);
            AppendNumber(questionRow, QuestionPointsColumn, questionRowNumber, question.Points);
            sheetData.Append(questionRow);

            FormulaCellAddress questionPoint = Address(sheetName, QuestionPointsColumn, questionRowNumber);
            questionPoints.Add(question.Id, questionPoint);
            verifiedCells.Add(questionPoint);

            for (int supportingIndex = 0; supportingIndex < question.SupportingSourceColumns.Length; supportingIndex++)
            {
                uint supportingRowNumber = rowNumber++;
                Row supportingRow = NewRow(supportingRowNumber);
                AppendInline(supportingRow, RecordTypeColumn, supportingRowNumber, "SUPPORTING_SOURCE");
                AppendInline(
                    supportingRow,
                    PathColumn,
                    supportingRowNumber,
                    $"{questionPath}.supportingSourceColumns[{supportingIndex.ToString(CultureInfo.InvariantCulture)}]");
                AppendInline(supportingRow, ParentNodeIdColumn, supportingRowNumber, question.Id);
                AppendInline(
                    supportingRow,
                    SupportingSourceColumnColumn,
                    supportingRowNumber,
                    question.SupportingSourceColumns[supportingIndex]);
                AppendNumber(supportingRow, ChunkIndexColumn, supportingRowNumber, supportingIndex + 1);
                sheetData.Append(supportingRow);
            }

            for (int evaluatorIndex = 0; evaluatorIndex < question.Evaluators.Length; evaluatorIndex++)
            {
                EvaluatorDefinition evaluator = question.Evaluators[evaluatorIndex];
                string evaluatorPath = $"{questionPath}.evaluators[{evaluatorIndex.ToString(CultureInfo.InvariantCulture)}]";
                uint evaluatorRowNumber = rowNumber++;
                Row evaluatorRow = NewRow(evaluatorRowNumber);
                AppendInline(evaluatorRow, RecordTypeColumn, evaluatorRowNumber, "EVALUATOR");
                AppendInline(evaluatorRow, PathColumn, evaluatorRowNumber, evaluatorPath);
                AppendInline(evaluatorRow, NodeIdColumn, evaluatorRowNumber, evaluator.Id);
                AppendInline(evaluatorRow, ParentNodeIdColumn, evaluatorRowNumber, question.Id);
                AppendInline(evaluatorRow, DisplayNameColumn, evaluatorRowNumber, evaluator.DisplayName);
                AppendOptionalInline(evaluatorRow, PromptTemplateColumn, evaluatorRowNumber, evaluator.CustomPromptTemplate);
                AppendOptionalInline(evaluatorRow, BuiltInTemplateVersionColumn, evaluatorRowNumber, evaluator.BuiltInTemplateVersion);
                AppendInline(evaluatorRow, EvaluatorTypeColumn, evaluatorRowNumber, GetEvaluatorTypeName(evaluator.Type));
                AppendBoolean(evaluatorRow, EnabledColumn, evaluatorRowNumber, evaluator.Enabled);
                AppendNumber(evaluatorRow, RawWeightColumn, evaluatorRowNumber, evaluator.Weight);
                AppendNumber(evaluatorRow, EffectiveMinimumColumn, evaluatorRowNumber, evaluator.Range.Minimum);
                AppendNumber(evaluatorRow, EffectiveMaximumColumn, evaluatorRowNumber, evaluator.Range.Maximum);
                sheetData.Append(evaluatorRow);

                FormulaCellAddress evaluatorWeight = Address(sheetName, RawWeightColumn, evaluatorRowNumber);
                FormulaCellAddress evaluatorMinimum = Address(sheetName, EffectiveMinimumColumn, evaluatorRowNumber);
                FormulaCellAddress evaluatorMaximum = Address(sheetName, EffectiveMaximumColumn, evaluatorRowNumber);
                evaluatorWeights.Add(evaluator.Id, evaluatorWeight);
                evaluatorMinimums.Add(evaluator.Id, evaluatorMinimum);
                evaluatorMaximums.Add(evaluator.Id, evaluatorMaximum);
                verifiedCells.Add(evaluatorWeight);
                verifiedCells.Add(evaluatorMinimum);
                verifiedCells.Add(evaluatorMaximum);

                for (int criterionIndex = 0; criterionIndex < evaluator.Criteria.Length; criterionIndex++)
                {
                    CriterionDefinition criterion = evaluator.Criteria[criterionIndex];
                    ScoreRange effectiveRange = criterion.Range ?? evaluator.Range;
                    uint criterionRowNumber = rowNumber++;
                    Row criterionRow = NewRow(criterionRowNumber);
                    AppendInline(criterionRow, RecordTypeColumn, criterionRowNumber, "CRITERION");
                    AppendInline(
                        criterionRow,
                        PathColumn,
                        criterionRowNumber,
                        $"{evaluatorPath}.criteria[{criterionIndex.ToString(CultureInfo.InvariantCulture)}]");
                    AppendInline(criterionRow, NodeIdColumn, criterionRowNumber, criterion.Id);
                    AppendInline(criterionRow, ParentNodeIdColumn, criterionRowNumber, evaluator.Id);
                    AppendInline(criterionRow, DisplayNameColumn, criterionRowNumber, criterion.DisplayName);
                    AppendInline(criterionRow, ContentTextColumn, criterionRowNumber, criterion.Description);
                    AppendBoolean(criterionRow, EnabledColumn, criterionRowNumber, criterion.Enabled);
                    AppendNumber(criterionRow, RawWeightColumn, criterionRowNumber, criterion.Weight);
                    AppendNumber(criterionRow, EffectiveMinimumColumn, criterionRowNumber, effectiveRange.Minimum);
                    AppendNumber(criterionRow, EffectiveMaximumColumn, criterionRowNumber, effectiveRange.Maximum);
                    sheetData.Append(criterionRow);

                    FormulaCellAddress criterionWeight = Address(sheetName, RawWeightColumn, criterionRowNumber);
                    FormulaCellAddress criterionMinimum = Address(sheetName, EffectiveMinimumColumn, criterionRowNumber);
                    FormulaCellAddress criterionMaximum = Address(sheetName, EffectiveMaximumColumn, criterionRowNumber);
                    criterionWeights.Add(criterion.Id, criterionWeight);
                    criterionMinimums.Add(criterion.Id, criterionMinimum);
                    criterionMaximums.Add(criterion.Id, criterionMaximum);
                    verifiedCells.Add(criterionWeight);
                    verifiedCells.Add(criterionMinimum);
                    verifiedCells.Add(criterionMaximum);
                }
            }

            for (int specialIndex = 0; specialIndex < question.SpecialEvaluations.Length; specialIndex++)
            {
                SpecialEvaluationDefinition special = question.SpecialEvaluations[specialIndex];
                string specialPath = $"{questionPath}.specialEvaluations[{specialIndex.ToString(CultureInfo.InvariantCulture)}]";
                uint specialRowNumber = rowNumber++;
                Row specialRow = NewRow(specialRowNumber);
                AppendInline(specialRow, RecordTypeColumn, specialRowNumber, "SPECIAL_EVALUATION");
                AppendInline(specialRow, PathColumn, specialRowNumber, specialPath);
                AppendInline(specialRow, NodeIdColumn, specialRowNumber, special.Id);
                AppendInline(specialRow, ParentNodeIdColumn, specialRowNumber, question.Id);
                AppendInline(specialRow, DisplayNameColumn, specialRowNumber, special.DisplayName);
                AppendInline(specialRow, PromptTemplateColumn, specialRowNumber, special.PromptTemplate);
                AppendBoolean(specialRow, EnabledColumn, specialRowNumber, special.Enabled);
                AppendInline(specialRow, SpecialPrimarySourceColumn, specialRowNumber, special.PrimarySourceColumn);
                sheetData.Append(specialRow);

                for (int supportingIndex = 0; supportingIndex < special.SupportingSourceColumns.Length; supportingIndex++)
                {
                    uint supportingRowNumber = rowNumber++;
                    Row supportingRow = NewRow(supportingRowNumber);
                    AppendInline(supportingRow, RecordTypeColumn, supportingRowNumber, "SPECIAL_SUPPORTING_SOURCE");
                    AppendInline(
                        supportingRow,
                        PathColumn,
                        supportingRowNumber,
                        $"{specialPath}.supportingSourceColumns[{supportingIndex.ToString(CultureInfo.InvariantCulture)}]");
                    AppendInline(supportingRow, ParentNodeIdColumn, supportingRowNumber, special.Id);
                    AppendNumber(supportingRow, ChunkIndexColumn, supportingRowNumber, supportingIndex + 1);
                    AppendInline(
                        supportingRow,
                        SpecialSupportingSourceColumn,
                        supportingRowNumber,
                        special.SupportingSourceColumns[supportingIndex]);
                    sheetData.Append(supportingRow);
                }
            }
        }

        FormulaCellAddress allocationTotal = Address(sheetName, AllocationTotalColumn, definitionRowNumber);
        FormulaCellAddress allocationValid = Address(sheetName, AllocationValidColumn, definitionRowNumber);
        ImmutableArray<WrittenFormulaCell> formulaCells = CreateAllocationFormulaCells(
            definition,
            sheetName,
            definitionRowNumber,
            allocationTotal,
            allocationValid,
            questionPoints);
        foreach (WrittenFormulaCell formulaCell in formulaCells)
        {
            formulaCellWriter.Write(
                definitionRow,
                formulaCell.Definition,
                formulaCell.CachedValue);
        }

        Worksheet worksheet = new(sheetData);
        WorkbookSheetWriter.AddWorksheet(workbookPart, sheetName, worksheet);
        return new ConfigCellAddressMap(
            sheetName,
            Address(sheetName, RoundingDigitsColumn, definitionRowNumber),
            Address(sheetName, BasePointsColumn, definitionRowNumber),
            Address(sheetName, SpecialPointsColumn, definitionRowNumber),
            Address(sheetName, SimilarityPenaltyWeightColumn, definitionRowNumber),
            allocationTotal,
            allocationValid,
            questionPoints,
            evaluatorWeights,
            evaluatorMinimums,
            evaluatorMaximums,
            criterionWeights,
            criterionMinimums,
            criterionMaximums,
            verifiedCells,
            formulaCells);
    }

    public long CalculateRequiredRowCount(QuantificationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.HasValidHash())
        {
            throw new ArgumentException("The quantification snapshot hash is invalid.", nameof(snapshot));
        }

        long rows = checked(2L + Chunk(snapshot.CanonicalJson).LongCount());
        foreach (QuestionDefinition question in snapshot.Definition.Questions)
        {
            rows = checked(rows + 1L + question.SupportingSourceColumns.Length);
            foreach (EvaluatorDefinition evaluator in question.Evaluators)
            {
                rows = checked(rows + 1L + evaluator.Criteria.Length);
            }

            foreach (SpecialEvaluationDefinition special in question.SpecialEvaluations)
            {
                rows = checked(rows + 1L + special.SupportingSourceColumns.Length);
            }
        }

        return rows;
    }

    public ConfigCellAddressMap CreateAddressMap(
        QuantificationSnapshot snapshot,
        string sheetName)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sheetName);
        if (!snapshot.HasValidHash())
        {
            throw new ArgumentException("The quantification snapshot hash is invalid.", nameof(snapshot));
        }

        if (!AppOwnedSheetNameResolver.IsValidWorksheetName(sheetName))
        {
            throw new ArgumentException("The Config worksheet name is invalid.", nameof(sheetName));
        }

        long requiredRows = CalculateRequiredRowCount(snapshot);
        if (requiredRows > MaximumExcelRows)
        {
            throw new InvalidDataException("The Config worksheet exceeds the Excel row limit.");
        }

        uint rowNumber = 1;
        _ = TakeRow(ref rowNumber);
        uint definitionRowNumber = TakeRow(ref rowNumber);
        foreach (string chunk in Chunk(snapshot.CanonicalJson))
        {
            _ = chunk;
            TakeRow(ref rowNumber);
        }

        Dictionary<string, FormulaCellAddress> questionPoints = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> evaluatorWeights = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> evaluatorMinimums = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> evaluatorMaximums = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> criterionWeights = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> criterionMinimums = new(StringComparer.Ordinal);
        Dictionary<string, FormulaCellAddress> criterionMaximums = new(StringComparer.Ordinal);
        List<FormulaCellAddress> verifiedCells =
        [
            Address(sheetName, RoundingDigitsColumn, definitionRowNumber),
            Address(sheetName, BasePointsColumn, definitionRowNumber),
            Address(sheetName, SpecialPointsColumn, definitionRowNumber),
            Address(sheetName, SimilarityPenaltyWeightColumn, definitionRowNumber),
            Address(sheetName, AllocationTotalColumn, definitionRowNumber),
            Address(sheetName, AllocationValidColumn, definitionRowNumber),
        ];

        foreach (QuestionDefinition question in snapshot.Definition.Questions)
        {
            uint questionRowNumber = TakeRow(ref rowNumber);
            FormulaCellAddress questionPoint = Address(sheetName, QuestionPointsColumn, questionRowNumber);
            questionPoints.Add(question.Id, questionPoint);
            verifiedCells.Add(questionPoint);

            foreach (string column in question.SupportingSourceColumns)
            {
                _ = column;
                TakeRow(ref rowNumber);
            }

            foreach (EvaluatorDefinition evaluator in question.Evaluators)
            {
                uint evaluatorRowNumber = TakeRow(ref rowNumber);
                FormulaCellAddress evaluatorWeight = Address(sheetName, RawWeightColumn, evaluatorRowNumber);
                FormulaCellAddress evaluatorMinimum = Address(sheetName, EffectiveMinimumColumn, evaluatorRowNumber);
                FormulaCellAddress evaluatorMaximum = Address(sheetName, EffectiveMaximumColumn, evaluatorRowNumber);
                evaluatorWeights.Add(evaluator.Id, evaluatorWeight);
                evaluatorMinimums.Add(evaluator.Id, evaluatorMinimum);
                evaluatorMaximums.Add(evaluator.Id, evaluatorMaximum);
                verifiedCells.Add(evaluatorWeight);
                verifiedCells.Add(evaluatorMinimum);
                verifiedCells.Add(evaluatorMaximum);

                foreach (CriterionDefinition criterion in evaluator.Criteria)
                {
                    uint criterionRowNumber = TakeRow(ref rowNumber);
                    FormulaCellAddress criterionWeight = Address(sheetName, RawWeightColumn, criterionRowNumber);
                    FormulaCellAddress criterionMinimum = Address(sheetName, EffectiveMinimumColumn, criterionRowNumber);
                    FormulaCellAddress criterionMaximum = Address(sheetName, EffectiveMaximumColumn, criterionRowNumber);
                    criterionWeights.Add(criterion.Id, criterionWeight);
                    criterionMinimums.Add(criterion.Id, criterionMinimum);
                    criterionMaximums.Add(criterion.Id, criterionMaximum);
                    verifiedCells.Add(criterionWeight);
                    verifiedCells.Add(criterionMinimum);
                    verifiedCells.Add(criterionMaximum);
                }
            }

            foreach (SpecialEvaluationDefinition special in question.SpecialEvaluations)
            {
                TakeRow(ref rowNumber);
                foreach (string column in special.SupportingSourceColumns)
                {
                    _ = column;
                    TakeRow(ref rowNumber);
                }
            }
        }

        FormulaCellAddress allocationTotal = Address(sheetName, AllocationTotalColumn, definitionRowNumber);
        FormulaCellAddress allocationValid = Address(sheetName, AllocationValidColumn, definitionRowNumber);
        ImmutableArray<WrittenFormulaCell> formulaCells = CreateAllocationFormulaCells(
            snapshot.Definition,
            sheetName,
            definitionRowNumber,
            allocationTotal,
            allocationValid,
            questionPoints);
        return new ConfigCellAddressMap(
            sheetName,
            Address(sheetName, RoundingDigitsColumn, definitionRowNumber),
            Address(sheetName, BasePointsColumn, definitionRowNumber),
            Address(sheetName, SpecialPointsColumn, definitionRowNumber),
            Address(sheetName, SimilarityPenaltyWeightColumn, definitionRowNumber),
            allocationTotal,
            allocationValid,
            questionPoints,
            evaluatorWeights,
            evaluatorMinimums,
            evaluatorMaximums,
            criterionWeights,
            criterionMinimums,
            criterionMaximums,
            verifiedCells,
            formulaCells);
    }

    private static ImmutableArray<WrittenFormulaCell> CreateAllocationFormulaCells(
        QuantificationDefinition definition,
        string sheetName,
        uint definitionRowNumber,
        FormulaCellAddress allocationTotal,
        FormulaCellAddress allocationValid,
        IReadOnlyDictionary<string, FormulaCellAddress> questionPoints) =>
        [
            new WrittenFormulaCell(
                new FormulaCellDefinition(
                    allocationTotal,
                    new FormulaIdentity("Definition", definition.Id, definition.Name, "AllocationTotal"),
                    FormulaExpressions.AllocationTotal(
                        Ref(Address(sheetName, BasePointsColumn, definitionRowNumber), absolute: true),
                        Ref(Address(sheetName, SpecialPointsColumn, definitionRowNumber), absolute: true),
                        definition.Questions.Where(question => question.Enabled)
                            .Select(question => Ref(questionPoints[question.Id], absolute: true)))),
                100m),
            new WrittenFormulaCell(
                new FormulaCellDefinition(
                    allocationValid,
                    new FormulaIdentity("Definition", definition.Id, definition.Name, "AllocationValid"),
                    FormulaExpressions.AllocationValid(Ref(allocationTotal, absolute: true))),
                1m),
        ];

    private static Row CreateHeaderRow(uint rowNumber)
    {
        Row row = NewRow(rowNumber);
        (string Column, string Heading)[] headings =
        [
            (RecordTypeColumn, "RecordType"),
            (PathColumn, "Path"),
            (NodeIdColumn, "NodeId"),
            (ParentNodeIdColumn, "ParentNodeId"),
            (DisplayNameColumn, "DisplayName"),
            (RevisionColumn, "Revision"),
            (ContentTextColumn, "ContentText"),
            (PromptTemplateColumn, "PromptTemplate"),
            (BuiltInTemplateVersionColumn, "BuiltInTemplateVersion"),
            (EvaluatorTypeColumn, "EvaluatorType"),
            (SourceSheetColumn, "SourceSheet"),
            (HeaderRowColumn, "HeaderRow"),
            (FirstDataRowColumn, "FirstDataRow"),
            (LastDataRowColumn, "LastDataRow"),
            (PrimarySourceColumnColumn, "PrimarySourceColumn"),
            (SupportingSourceColumnColumn, "SupportingSourceColumn"),
            (EnabledColumn, "Enabled"),
            (RawWeightColumn, "RawWeight"),
            (EffectiveMinimumColumn, "EffectiveMinimum"),
            (EffectiveMaximumColumn, "EffectiveMaximum"),
            (RoundingDigitsColumn, "RoundingDigits"),
            (SchemaVersionColumn, "SchemaVersion"),
            (DefinitionSha256Column, "DefinitionSha256"),
            (ChunkIndexColumn, "ChunkIndex"),
            (SnapshotChunkColumn, "SnapshotChunk"),
            (BasePointsColumn, "BasePoints"),
            (SpecialPointsColumn, "SpecialPoints"),
            (SimilarityPenaltyWeightColumn, "SimilarityPenaltyWeight"),
            (QuestionPointsColumn, "QuestionPoints"),
            (AllocationTotalColumn, "AllocationTotal"),
            (AllocationValidColumn, "AllocationValid"),
            (SpecialPrimarySourceColumn, "SpecialPrimarySourceColumn"),
            (SpecialSupportingSourceColumn, "SpecialSupportingSourceColumn"),
        ];
        foreach ((string column, string heading) in headings)
        {
            AppendInline(row, column, rowNumber, heading);
        }

        return row;
    }

    private static Row CreateDefinitionRow(
        uint rowNumber,
        QuantificationDefinition definition,
        QuantificationSnapshot snapshot)
    {
        Row row = NewRow(rowNumber);
        AppendInline(row, RecordTypeColumn, rowNumber, "DEFINITION");
        AppendInline(row, PathColumn, rowNumber, "$");
        AppendInline(row, NodeIdColumn, rowNumber, definition.Id);
        AppendInline(row, DisplayNameColumn, rowNumber, definition.Name);
        AppendInline(row, RevisionColumn, rowNumber, definition.Revision);
        AppendInline(row, SourceSheetColumn, rowNumber, definition.SourceSheet);
        AppendNumber(row, HeaderRowColumn, rowNumber, definition.HeaderRow);
        AppendNumber(row, FirstDataRowColumn, rowNumber, definition.FirstDataRow);
        AppendNumber(row, LastDataRowColumn, rowNumber, definition.LastDataRow);
        AppendNumber(row, RoundingDigitsColumn, rowNumber, definition.RoundingDigits);
        AppendInline(row, SchemaVersionColumn, rowNumber, CanonicalDefinitionSerializer.SchemaVersion);
        AppendInline(row, DefinitionSha256Column, rowNumber, snapshot.Sha256);
        AppendNumber(row, BasePointsColumn, rowNumber, definition.BasePoints);
        AppendNumber(row, SpecialPointsColumn, rowNumber, definition.SpecialPoints);
        AppendNumber(row, SimilarityPenaltyWeightColumn, rowNumber, definition.SimilarityPenaltyWeight);
        return row;
    }

    private static IEnumerable<string> Chunk(string value)
    {
        if (value.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        for (int offset = 0; offset < value.Length;)
        {
            int length = Math.Min(SnapshotChunkCharacters, value.Length - offset);
            if (offset + length < value.Length && char.IsHighSurrogate(value[offset + length - 1]))
            {
                length--;
            }

            yield return value.Substring(offset, length);
            offset += length;
        }
    }

    private static FormulaCellAddress Address(string sheetName, string columnName, uint rowNumber) =>
        new(sheetName, columnName, checked((int)rowNumber));

    private static FormulaCellReference Ref(FormulaCellAddress address, bool absolute = false) =>
        new(address, absolute, absolute);

    private static string GetEvaluatorTypeName(EvaluatorType type) => type switch
    {
        EvaluatorType.KnowledgeCoverage => "KNOWLEDGE_COVERAGE",
        EvaluatorType.CustomPrompt => "CUSTOM_PROMPT",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported evaluator type."),
    };

    private static uint TakeRow(ref uint nextRow)
    {
        if (nextRow is 0 or > MaximumExcelRows)
        {
            throw new InvalidDataException("The Config worksheet exceeds the Excel row limit.");
        }

        return nextRow++;
    }

    private static Row NewRow(uint rowNumber)
    {
        if (rowNumber is 0 or > MaximumExcelRows)
        {
            throw new InvalidDataException("The Config worksheet exceeds the Excel row limit.");
        }

        return new Row { RowIndex = rowNumber };
    }

    private static void AppendOptionalInline(Row row, string column, uint rowNumber, string? value)
    {
        if (value is not null)
        {
            AppendInline(row, column, rowNumber, value);
        }
    }

    private static void AppendInline(Row row, string column, uint rowNumber, string value) =>
        row.Append(SpreadsheetLiteral.CreateInlineStringCell(column + Invariant(rowNumber), value));

    private static void AppendNumber(Row row, string column, uint rowNumber, decimal value) =>
        row.Append(SpreadsheetLiteral.CreateNumberCell(column + Invariant(rowNumber), value));

    private static void AppendNumber(Row row, string column, uint rowNumber, int value) =>
        row.Append(SpreadsheetLiteral.CreateNumberCell(column + Invariant(rowNumber), value));

    private static void AppendBoolean(Row row, string column, uint rowNumber, bool value) =>
        row.Append(SpreadsheetLiteral.CreateBooleanCell(column + Invariant(rowNumber), value));

    private static string Invariant(uint value) => value.ToString(CultureInfo.InvariantCulture);
}

internal static class SpreadsheetLiteral
{
    internal static Cell CreateInlineStringCell(string reference, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > ConfigSheetWriter.MaximumCellCharacters)
        {
            throw new InvalidDataException("A Config or Run string exceeds the Excel cell limit.");
        }

        return new Cell
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(
                new SpreadsheetText(value) { Space = SpaceProcessingModeValues.Preserve }),
        };
    }

    internal static Cell CreateNumberCell(string reference, decimal value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.Number,
            CellValue = new CellValue(value.ToString("G29", CultureInfo.InvariantCulture)),
        };

    internal static Cell CreateNumberCell(string reference, int value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.Number,
            CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)),
        };

    internal static Cell CreateNumberCell(string reference, long value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.Number,
            CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)),
        };

    internal static Cell CreateBooleanCell(string reference, bool value) =>
        new()
        {
            CellReference = reference,
            DataType = CellValues.Boolean,
            CellValue = new CellValue(value ? "1" : "0"),
        };
}

internal static class WorkbookSheetWriter
{
    internal static WorksheetPart AddWorksheet(
        WorkbookPart workbookPart,
        string sheetName,
        Worksheet worksheet)
    {
        ArgumentNullException.ThrowIfNull(workbookPart);
        ArgumentNullException.ThrowIfNull(worksheet);
        if (!AppOwnedSheetNameResolver.IsValidWorksheetName(sheetName))
        {
            throw new ArgumentException("The worksheet name is invalid or reserved.", nameof(sheetName));
        }

        Workbook workbook = workbookPart.Workbook
            ?? throw new InvalidDataException("The workbook root is missing.");
        Sheets sheets = workbook.GetFirstChild<Sheets>()
            ?? throw new InvalidDataException("The workbook sheet collection is missing.");
        Sheet[] existingSheets = sheets.Elements<Sheet>().ToArray();
        if (existingSheets.Any(sheet => string.Equals(
            sheet.Name?.Value,
            sheetName,
            StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The worksheet name is already in use.");
        }

        uint maximumSheetId = 0;
        foreach (Sheet existingSheet in existingSheets)
        {
            uint sheetId = existingSheet.SheetId?.Value
                ?? throw new InvalidDataException("An existing worksheet ID is missing.");
            maximumSheetId = Math.Max(maximumSheetId, sheetId);
        }

        uint newSheetId = checked(maximumSheetId + 1);
        WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        Sheet? newSheet = null;
        try
        {
            worksheetPart.Worksheet = worksheet;
            worksheetPart.Worksheet.Save();
            newSheet = new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = newSheetId,
                Name = sheetName,
            };
            sheets.Append(newSheet);
            workbook.Save();
            return worksheetPart;
        }
        catch
        {
            newSheet?.Remove();
            workbookPart.DeletePart(worksheetPart);
            throw;
        }
    }
}
