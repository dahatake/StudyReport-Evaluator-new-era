using System.Collections.ObjectModel;
using System.Text;
using StudyReportEvaluator.App.Workbooks.Reading;

namespace StudyReportEvaluator.App.Workbooks.Mapping;

[Flags]
public enum ColumnMappingCandidateRole
{
    None = 0,
    PrimaryAnswer = 1,
    StudentPromptPrimary = 2,
    Supporting = 4,
}

public sealed class ColumnMappingCandidate
{
    internal ColumnMappingCandidate(
        uint columnIndex,
        string columnName,
        ColumnMappingCandidateRole roles,
        IEnumerable<string> suggestedSupportingColumns)
    {
        ColumnIndex = columnIndex;
        ColumnName = columnName;
        Roles = roles;
        SuggestedSupportingColumns = Copy(suggestedSupportingColumns);
    }

    public uint ColumnIndex { get; }

    public string ColumnName { get; }

    public ColumnMappingCandidateRole Roles { get; }

    public bool IsPrimaryCandidate =>
        (Roles & ColumnMappingCandidateRole.PrimaryAnswer) != 0;

    public bool IsStudentPromptPrimaryCandidate =>
        (Roles & ColumnMappingCandidateRole.StudentPromptPrimary) != 0;

    public bool IsSupportingCandidate =>
        (Roles & ColumnMappingCandidateRole.Supporting) != 0;

    public IReadOnlyList<string> SuggestedSupportingColumns { get; }

    public override string ToString() =>
        $"{nameof(ColumnMappingCandidate)} {{ Roles = {Roles}, Content = <redacted> }}";

    private static ReadOnlyCollection<string> Copy(IEnumerable<string> values) =>
        Array.AsReadOnly(values.ToArray());
}

public sealed class WorksheetMappingSuggestion
{
    internal WorksheetMappingSuggestion(
        string worksheetName,
        uint headerRow,
        uint? firstDataRow,
        uint? lastDataRow,
        IEnumerable<ColumnMappingCandidate> candidates,
        IEnumerable<string> initiallyUnselectedColumns,
        int semanticScore)
    {
        WorksheetName = worksheetName;
        HeaderRow = headerRow;
        FirstDataRow = firstDataRow;
        LastDataRow = lastDataRow;
        Candidates = Array.AsReadOnly(candidates.ToArray());
        InitialTargetColumns = Array.AsReadOnly(Candidates
            .Select(candidate => candidate.ColumnName)
            .ToArray());
        InitiallyUnselectedColumns = Array.AsReadOnly(initiallyUnselectedColumns.ToArray());
        SemanticScore = semanticScore;
    }

    public string WorksheetName { get; }

    public uint HeaderRow { get; }

    public uint? FirstDataRow { get; }

    public uint? LastDataRow { get; }

    public uint SuggestedDataRowCount =>
        FirstDataRow is uint first && LastDataRow is uint last && last >= first
            ? last - first + 1
            : 0;

    public IReadOnlyList<ColumnMappingCandidate> Candidates { get; }

    public IReadOnlyList<string> InitialTargetColumns { get; }

    public IReadOnlyList<string> InitiallyUnselectedColumns { get; }

    public bool HasConfidentMapping =>
        SuggestedDataRowCount is >= 1 and <= ColumnMappingSuggester.MaximumSuggestedDataRows
        && Candidates.Any(candidate => candidate.IsPrimaryCandidate);

    internal int SemanticScore { get; }

    public override string ToString() =>
        $"{nameof(WorksheetMappingSuggestion)} {{ CandidateCount = {Candidates.Count}, Content = <redacted> }}";
}

public sealed class ColumnMappingSuggestionResult
{
    internal ColumnMappingSuggestionResult(
        IEnumerable<WorksheetMappingSuggestion> worksheetSuggestions,
        WorksheetMappingSuggestion? suggestedWorksheet)
    {
        WorksheetSuggestions = Array.AsReadOnly(worksheetSuggestions.ToArray());
        SuggestedWorksheet = suggestedWorksheet;
    }

    public IReadOnlyList<WorksheetMappingSuggestion> WorksheetSuggestions { get; }

    public WorksheetMappingSuggestion? SuggestedWorksheet { get; }

    public string? SuggestedWorksheetName => SuggestedWorksheet?.WorksheetName;

    public override string ToString() =>
        $"{nameof(ColumnMappingSuggestionResult)} {{ HasSuggestedWorksheet = {SuggestedWorksheet is not null}, Content = <redacted> }}";
}

public sealed class ColumnMappingSuggester
{
    public const uint MaximumSuggestedDataRows = 20_000;

    private static readonly string[] ManagementMarkers =
    [
        "開始時刻",
        "完了時刻",
        "送信時刻",
        "提出日時",
        "提出者",
        "更新日時",
        "タイムスタンプ",
        "timestamp",
        "電子メール",
        "メールアドレス",
        "email",
        "e mail",
        "学籍番号",
        "student id",
        "response id",
        "回答 id",
        "応答 id",
        "ユーザー id",
        "user id",
        "合計得点",
        "合計点",
    ];

    private static readonly string[] ExactManagementHeaders =
    [
        "id",
        "氏名",
        "名前",
        "name",
        "所属",
        "クラス",
        "class",
    ];

    private static readonly string[] FeedbackMarkers =
    [
        "feedback",
        "フィードバック",
    ];

    private static readonly string[] SupportingMarkers =
    [
        "工夫",
        "観点",
        "論点",
        "考慮",
        "留意",
        "着眼",
        "意図",
        "補足",
        "補助",
        "背景",
        "rationale",
        "consideration",
        "viewpoint",
    ];

    private static readonly string[] PrimaryMarkers =
    [
        "レポート",
        "report",
        "回答",
        "解答",
        "answer",
        "response",
        "質問",
        "設問",
        "question",
        "提出内容",
        "本文",
    ];

    private static readonly string[] GenericQuestionHeaders =
    [
        "質問",
        "設問",
        "question",
    ];

    public ColumnMappingSuggestionResult Suggest(WorkbookMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        WorksheetMappingSuggestion[] suggestions = metadata.Worksheets
            .Select(worksheet => CreateWorksheetSuggestion(metadata.HeaderRowNumber, worksheet))
            .ToArray();
        WorksheetMappingSuggestion? suggestedWorksheet = SelectSuggestedWorksheet(suggestions);
        return new ColumnMappingSuggestionResult(suggestions, suggestedWorksheet);
    }

    private static WorksheetMappingSuggestion CreateWorksheetSuggestion(
        uint headerRow,
        WorksheetMetadata worksheet)
    {
        CandidateDraft[] allDrafts = worksheet.HeaderCells
            .Select(header => CreateCandidateDraft(header))
            .OrderBy(candidate => candidate.ColumnIndex)
            .ToArray();
        CandidateDraft[] drafts = InferPromptCompanionAnswers(allDrafts)
            .Where(candidate => candidate.Roles != ColumnMappingCandidateRole.None)
            .OrderBy(candidate => candidate.ColumnIndex)
            .ToArray();

        Dictionary<uint, List<string>> supportingByPrimary = PairPromptSupportingColumns(drafts);
        ColumnMappingCandidate[] candidates = drafts
            .Select(draft => new ColumnMappingCandidate(
                draft.ColumnIndex,
                draft.ColumnName,
                draft.Roles,
                supportingByPrimary.TryGetValue(draft.ColumnIndex, out List<string>? supporting)
                    ? supporting
                    : []))
            .ToArray();

        HashSet<uint> selectedIndices = drafts
            .Select(candidate => candidate.ColumnIndex)
            .ToHashSet();
        List<string> initiallyUnselected = [];
        for (uint column = worksheet.FirstColumnIndex; column <= worksheet.LastColumnIndex; column++)
        {
            if (!selectedIndices.Contains(column))
            {
                initiallyUnselected.Add(GetColumnName(column));
            }
        }

        uint? firstDataRow = null;
        uint? lastDataRow = null;
        if (headerRow >= worksheet.FirstRowIndex && headerRow < worksheet.LastRowIndex)
        {
            firstDataRow = headerRow + 1;
            lastDataRow = worksheet.LastRowIndex;
        }

        int semanticScore = candidates.Sum(candidate =>
            (candidate.IsPrimaryCandidate ? 3 : 0)
            + (candidate.IsStudentPromptPrimaryCandidate ? 2 : 0)
            + (candidate.IsSupportingCandidate ? 1 : 0)
            + (candidate.SuggestedSupportingColumns.Count * 2));
        return new WorksheetMappingSuggestion(
            worksheet.Name,
            headerRow,
            firstDataRow,
            lastDataRow,
            candidates,
            initiallyUnselected,
            semanticScore);
    }

    private static CandidateDraft CreateCandidateDraft(WorkbookHeaderCell header)
    {
        string normalized = NormalizeHeader(header.Value);
        if (normalized.Length == 0
            || IsManagementHeader(normalized)
            || ContainsAny(normalized, FeedbackMarkers))
        {
            return new CandidateDraft(
                header.ColumnIndex,
                header.ColumnName,
                ColumnMappingCandidateRole.None,
                IsPromptContextSupport: false,
                CanBePromptCompanionAnswer: false);
        }

        bool hasPrompt = normalized.Contains("prompt", StringComparison.Ordinal)
            || normalized.Contains("プロンプト", StringComparison.Ordinal);
        bool isGenericQuestion = GenericQuestionHeaders.Contains(normalized, StringComparer.Ordinal);
        bool isSupporting = ContainsAny(normalized, SupportingMarkers);
        bool isPrimary = ContainsAny(normalized, PrimaryMarkers);

        ColumnMappingCandidateRole roles = ColumnMappingCandidateRole.None;
        if (isGenericQuestion)
        {
            roles = ColumnMappingCandidateRole.PrimaryAnswer
                | ColumnMappingCandidateRole.Supporting;
        }
        else if (isSupporting)
        {
            roles = ColumnMappingCandidateRole.Supporting;
            if (!hasPrompt && isPrimary)
            {
                roles |= ColumnMappingCandidateRole.PrimaryAnswer;
            }
        }
        else if (hasPrompt)
        {
            roles = ColumnMappingCandidateRole.PrimaryAnswer
                | ColumnMappingCandidateRole.StudentPromptPrimary;
        }
        else if (isPrimary)
        {
            roles = ColumnMappingCandidateRole.PrimaryAnswer;
        }

        return new CandidateDraft(
            header.ColumnIndex,
            header.ColumnName,
            roles,
            IsPromptContextSupport: isSupporting && hasPrompt,
            CanBePromptCompanionAnswer: roles == ColumnMappingCandidateRole.None);
    }

    private static IReadOnlyList<CandidateDraft> InferPromptCompanionAnswers(
        IReadOnlyList<CandidateDraft> candidates)
    {
        CandidateDraft[] inferred = candidates.ToArray();
        for (int index = 1; index < inferred.Length; index++)
        {
            CandidateDraft prompt = inferred[index];
            CandidateDraft companion = inferred[index - 1];
            bool isStudentPrompt = (prompt.Roles & ColumnMappingCandidateRole.StudentPromptPrimary) != 0;
            if (isStudentPrompt
                && companion.CanBePromptCompanionAnswer
                && prompt.ColumnIndex == companion.ColumnIndex + 1)
            {
                inferred[index - 1] = companion with
                {
                    Roles = ColumnMappingCandidateRole.PrimaryAnswer,
                };
            }
        }

        return inferred;
    }

    private static Dictionary<uint, List<string>> PairPromptSupportingColumns(
        IReadOnlyList<CandidateDraft> candidates)
    {
        Dictionary<uint, List<string>> supportingByPrimary = [];
        foreach (CandidateDraft supporting in candidates.Where(candidate => candidate.IsPromptContextSupport))
        {
            CandidateDraft? nearestPrompt = null;
            foreach (CandidateDraft candidate in candidates)
            {
                if (candidate.ColumnIndex >= supporting.ColumnIndex
                    || (candidate.Roles & ColumnMappingCandidateRole.StudentPromptPrimary) == 0)
                {
                    continue;
                }

                if (nearestPrompt is null || candidate.ColumnIndex > nearestPrompt.Value.ColumnIndex)
                {
                    nearestPrompt = candidate;
                }
            }

            if (nearestPrompt is CandidateDraft primary)
            {
                if (!supportingByPrimary.TryGetValue(primary.ColumnIndex, out List<string>? columns))
                {
                    columns = [];
                    supportingByPrimary.Add(primary.ColumnIndex, columns);
                }

                columns.Add(supporting.ColumnName);
            }
        }

        return supportingByPrimary;
    }

    private static WorksheetMappingSuggestion? SelectSuggestedWorksheet(
        IReadOnlyList<WorksheetMappingSuggestion> suggestions)
    {
        WorksheetMappingSuggestion[] confident = suggestions
            .Where(suggestion => suggestion.HasConfidentMapping)
            .ToArray();
        if (confident.Length == 0)
        {
            return null;
        }

        WorksheetMappingSuggestion[] originalNamed = confident
            .Where(suggestion => string.Equals(
                NormalizeHeader(suggestion.WorksheetName),
                "original",
                StringComparison.Ordinal))
            .ToArray();
        if (originalNamed.Length == 1)
        {
            return originalNamed[0];
        }

        int highestScore = confident.Max(suggestion => suggestion.SemanticScore);
        WorksheetMappingSuggestion[] highest = confident
            .Where(suggestion => suggestion.SemanticScore == highestScore)
            .ToArray();
        if (highest.Length == 1)
        {
            return highest[0];
        }

        return null;
    }

    private static bool IsManagementHeader(string normalized) =>
        ExactManagementHeaders.Contains(normalized, StringComparer.Ordinal)
        || ContainsAny(normalized, ManagementMarkers);

    private static bool ContainsAny(string value, IEnumerable<string> markers) =>
        markers.Any(marker => value.Contains(marker, StringComparison.Ordinal));

    private static string NormalizeHeader(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder widthFolded = new(value.Length);
        foreach (char character in value)
        {
            if (character == '\u3000')
            {
                widthFolded.Append(' ');
            }
            else if (character is >= '\uFF01' and <= '\uFF5E')
            {
                widthFolded.Append((char)(character - 0xFEE0));
            }
            else
            {
                widthFolded.Append(character);
            }
        }

        string compatible;
        try
        {
            compatible = widthFolded.ToString().Normalize(NormalizationForm.FormKC);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }

        StringBuilder normalized = new(compatible.Length);
        bool pendingSeparator = false;
        foreach (char character in compatible)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && normalized.Length > 0)
                {
                    normalized.Append(' ');
                }

                normalized.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return normalized.ToString();
    }

    private static string GetColumnName(uint columnIndex)
    {
        Span<char> characters = stackalloc char[3];
        int position = characters.Length;
        uint value = columnIndex;
        while (value > 0)
        {
            value--;
            characters[--position] = (char)('A' + (value % 26));
            value /= 26;
        }

        return new string(characters[position..]);
    }

    private readonly record struct CandidateDraft(
        uint ColumnIndex,
        string ColumnName,
        ColumnMappingCandidateRole Roles,
        bool IsPromptContextSupport,
        bool CanBePromptCompanionAnswer);
}