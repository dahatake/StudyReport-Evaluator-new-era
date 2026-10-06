using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace StudyReportEvaluator.Core.Similarity;

public sealed record SurfaceSimilarityResult
{
    public decimal Score { get; init; }

    public int NGramSize { get; init; }

    public int StudentNormalizedLength { get; init; }

    public int ReferenceNormalizedLength { get; init; }

    public int StudentNGramCount { get; init; }

    public int ReferenceNGramCount { get; init; }

    public decimal NGramContainment { get; init; }

    public decimal DiceCoefficient { get; init; }

    public decimal JaccardIndex { get; init; }

    public int LongestCommonSubstringLength { get; init; }

    public decimal LongestCommonSubstringCoverage { get; init; }

    public bool IsEmpty => StudentNormalizedLength == 0 || ReferenceNormalizedLength == 0;

    public string ToReason() => IsEmpty
        ? "正規化後テキストが空のため類似度 0"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"文字{NGramSize}-gram包含率 {NGramContainment:0.00} / Dice {DiceCoefficient:0.00} / Jaccard {JaccardIndex:0.00} / 最長一致 {LongestCommonSubstringLength}文字 (被覆率 {LongestCommonSubstringCoverage:0.00})");
}

public sealed class SurfaceTextSimilarityCalculator
{
    public const int DefaultRoundingDigits = 4;
    public const int MaximumInputCharacters = 32_767;

    public SurfaceSimilarityResult Calculate(string? studentAnswer, string? referenceAnswer) =>
        Calculate(studentAnswer, referenceAnswer, DefaultRoundingDigits);

    public SurfaceSimilarityResult Calculate(string? studentAnswer, string? referenceAnswer, int roundingDigits)
    {
        if (roundingDigits is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(roundingDigits));
        }

        string student = NormalizeForComparison(studentAnswer);
        string reference = NormalizeForComparison(referenceAnswer);
        if (student.Length == 0 || reference.Length == 0)
        {
            return new SurfaceSimilarityResult
            {
                Score = 0m,
                StudentNormalizedLength = student.Length,
                ReferenceNormalizedLength = reference.Length,
            };
        }

        if (string.Equals(student, reference, StringComparison.Ordinal))
        {
            int identicalN = SelectNGramSize(student.Length, reference.Length);
            int identicalCount = CountNGrams(student.Length, identicalN);
            return new SurfaceSimilarityResult
            {
                Score = 1m,
                NGramSize = identicalN,
                StudentNormalizedLength = student.Length,
                ReferenceNormalizedLength = reference.Length,
                StudentNGramCount = identicalCount,
                ReferenceNGramCount = identicalCount,
                NGramContainment = 1m,
                DiceCoefficient = 1m,
                JaccardIndex = 1m,
                LongestCommonSubstringLength = student.Length,
                LongestCommonSubstringCoverage = 1m,
            };
        }

        int n = SelectNGramSize(student.Length, reference.Length);
        HashSet<string> studentNGrams = BuildNGramSet(student, n);
        HashSet<string> referenceNGrams = BuildNGramSet(reference, n);
        int intersection = CountIntersection(studentNGrams, referenceNGrams);
        decimal containment = Ratio(intersection, studentNGrams.Count, roundingDigits);
        decimal dice = Ratio(2 * intersection, studentNGrams.Count + referenceNGrams.Count, roundingDigits);
        decimal jaccard = Ratio(intersection, studentNGrams.Count + referenceNGrams.Count - intersection, roundingDigits);
        int lcs = LongestCommonSubstringLength(student, reference);
        decimal lcsCoverage = lcs < n ? 0m : Ratio(lcs, student.Length, roundingDigits);
        decimal nGramBlend = Round((containment * 0.70m) + (dice * 0.20m) + (jaccard * 0.10m), roundingDigits);
        decimal score = Round(Math.Max(nGramBlend, lcsCoverage), roundingDigits);

        return new SurfaceSimilarityResult
        {
            Score = score,
            NGramSize = n,
            StudentNormalizedLength = student.Length,
            ReferenceNormalizedLength = reference.Length,
            StudentNGramCount = studentNGrams.Count,
            ReferenceNGramCount = referenceNGrams.Count,
            NGramContainment = containment,
            DiceCoefficient = dice,
            JaccardIndex = jaccard,
            LongestCommonSubstringLength = lcs,
            LongestCommonSubstringCoverage = lcsCoverage,
        };
    }

    public SimilarityPeerAnalysisResult CalculatePeerMaximums(
        IEnumerable<SimilarityPeerInput> answers,
        int roundingDigits = DefaultRoundingDigits)
    {
        ArgumentNullException.ThrowIfNull(answers);
        if (roundingDigits is < 0 or > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(roundingDigits));
        }

        ImmutableArray<PreparedPeerAnswer> prepared = answers
            .Where(answer => answer is not null)
            .Select(answer => PreparePeerAnswer(
                answer.SourceRowNumber,
                answer.QuestionId ?? string.Empty,
                answer.Answer))
            .ToImmutableArray();
        ImmutableArray<SimilarityPeerResult>.Builder results = ImmutableArray.CreateBuilder<SimilarityPeerResult>(prepared.Length);
        foreach (PreparedPeerAnswer answer in prepared)
        {
            decimal best = 0m;
            int? bestRow = null;
            if (answer.Normalized.Length > 0)
            {
                foreach (PreparedPeerAnswer other in prepared)
                {
                    if (answer.SourceRowNumber == other.SourceRowNumber
                        || !string.Equals(answer.QuestionId, other.QuestionId, StringComparison.Ordinal)
                        || other.Normalized.Length == 0)
                    {
                        continue;
                    }

                    decimal score = CalculatePrepared(answer, other, roundingDigits);
                    if (score > best || (score == best && (bestRow is null || other.SourceRowNumber < bestRow.Value)))
                    {
                        best = score;
                        bestRow = other.SourceRowNumber;
                    }
                }
            }

            results.Add(new SimilarityPeerResult(answer.SourceRowNumber, answer.QuestionId, best, bestRow));
        }

        return new SimilarityPeerAnalysisResult(results.ToImmutable());
    }

    public static string NormalizeForComparison(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string bounded = value.Length > MaximumInputCharacters ? value[..MaximumInputCharacters] : value;
        string normalized = bounded.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        StringBuilder builder = new(normalized.Length);
        foreach (char ch in normalized)
        {
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (char.IsWhiteSpace(ch) || IsIgnoredCategory(category))
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static PreparedPeerAnswer PreparePeerAnswer(int sourceRowNumber, string questionId, string? answer)
    {
        string normalized = NormalizeForComparison(answer);
        return new PreparedPeerAnswer(
            sourceRowNumber,
            questionId,
            normalized,
            BuildNGramSet(normalized, 1),
            BuildNGramSet(normalized, 2),
            BuildNGramSet(normalized, 3),
            normalized.Length == 0 ? null : new SuffixAutomaton(normalized));
    }

    private static decimal CalculatePrepared(
        PreparedPeerAnswer student,
        PreparedPeerAnswer reference,
        int roundingDigits)
    {
        if (student.Normalized.Length == 0 || reference.Normalized.Length == 0)
        {
            return 0m;
        }

        if (string.Equals(student.Normalized, reference.Normalized, StringComparison.Ordinal))
        {
            return 1m;
        }

        int n = SelectNGramSize(student.Normalized.Length, reference.Normalized.Length);
        HashSet<string> studentNGrams = student.NGrams(n);
        HashSet<string> referenceNGrams = reference.NGrams(n);
        int intersection = CountIntersection(studentNGrams, referenceNGrams);
        decimal containment = Ratio(intersection, studentNGrams.Count, roundingDigits);
        decimal dice = Ratio(2 * intersection, studentNGrams.Count + referenceNGrams.Count, roundingDigits);
        decimal jaccard = Ratio(intersection, studentNGrams.Count + referenceNGrams.Count - intersection, roundingDigits);
        int lcs = reference.Automaton?.LongestCommonSubstringLength(student.Normalized) ?? 0;
        decimal lcsCoverage = lcs < n ? 0m : Ratio(lcs, student.Normalized.Length, roundingDigits);
        return Round(Math.Max(Round((containment * 0.70m) + (dice * 0.20m) + (jaccard * 0.10m), roundingDigits), lcsCoverage), roundingDigits);
    }

    private static int SelectNGramSize(int studentLength, int referenceLength)
    {
        int shortest = Math.Min(studentLength, referenceLength);
        return shortest >= 3 ? 3 : shortest >= 2 ? 2 : 1;
    }

    private static int CountNGrams(int length, int n) => length < n || n <= 0 ? 0 : length - n + 1;

    private static HashSet<string> BuildNGramSet(string value, int n)
    {
        HashSet<string> grams = new(StringComparer.Ordinal);
        if (n <= 0 || value.Length < n)
        {
            return grams;
        }

        for (int index = 0; index <= value.Length - n; index++)
        {
            grams.Add(value.Substring(index, n));
        }

        return grams;
    }

    private static int CountIntersection(HashSet<string> first, HashSet<string> second)
    {
        if (first.Count > second.Count)
        {
            (first, second) = (second, first);
        }

        int count = 0;
        foreach (string item in first)
        {
            if (second.Contains(item))
            {
                count++;
            }
        }

        return count;
    }

    private static int LongestCommonSubstringLength(string pattern, string text)
    {
        SuffixAutomaton automaton = new(text);
        int length = 0;
        int best = 0;
        int state = 0;
        foreach (char ch in pattern)
        {
            while (state != 0 && !automaton.HasTransition(state, ch))
            {
                state = automaton.Link(state);
                length = automaton.Length(state);
            }

            if (automaton.TryTransition(state, ch, out int next))
            {
                state = next;
                length++;
            }
            else
            {
                state = 0;
                length = 0;
            }

            if (length > best)
            {
                best = length;
            }
        }

        return best;
    }

    private static bool IsIgnoredCategory(UnicodeCategory category) => category is
        UnicodeCategory.SpaceSeparator
        or UnicodeCategory.LineSeparator
        or UnicodeCategory.ParagraphSeparator
        or UnicodeCategory.Control
        or UnicodeCategory.Format
        or UnicodeCategory.ConnectorPunctuation
        or UnicodeCategory.DashPunctuation
        or UnicodeCategory.OpenPunctuation
        or UnicodeCategory.ClosePunctuation
        or UnicodeCategory.InitialQuotePunctuation
        or UnicodeCategory.FinalQuotePunctuation
        or UnicodeCategory.OtherPunctuation
        or UnicodeCategory.MathSymbol
        or UnicodeCategory.CurrencySymbol
        or UnicodeCategory.ModifierSymbol
        or UnicodeCategory.OtherSymbol;

    private static decimal Ratio(int numerator, int denominator, int roundingDigits) => denominator <= 0
        ? 0m
        : Round((decimal)numerator / denominator, roundingDigits);

    private static decimal Round(decimal value, int digits) => Math.Round(
        Math.Clamp(value, 0m, 1m),
        digits,
        MidpointRounding.AwayFromZero);

    private sealed record PreparedPeerAnswer(
        int SourceRowNumber,
        string QuestionId,
        string Normalized,
        HashSet<string> NGrams1,
        HashSet<string> NGrams2,
        HashSet<string> NGrams3,
        SuffixAutomaton? Automaton)
    {
        internal HashSet<string> NGrams(int n) => n switch
        {
            1 => NGrams1,
            2 => NGrams2,
            _ => NGrams3,
        };
    }

    private sealed class SuffixAutomaton
    {
        private readonly State[] states;
        private int size = 1;
        private int last;

        internal SuffixAutomaton(string text)
        {
            states = new State[Math.Max(2, text.Length * 2)];
            for (int index = 0; index < states.Length; index++)
            {
                states[index] = new State();
            }

            foreach (char ch in text)
            {
                Extend(ch);
            }
        }

        internal int Link(int state) => states[state].Link;

        internal int Length(int state) => states[state].Length;

        internal bool HasTransition(int state, char ch) => states[state].Transitions.ContainsKey(ch);

        internal bool TryTransition(int state, char ch, out int next) => states[state].Transitions.TryGetValue(ch, out next);

        internal int LongestCommonSubstringLength(string pattern)
        {
            int length = 0;
            int best = 0;
            int state = 0;
            foreach (char ch in pattern)
            {
                while (state != 0 && !HasTransition(state, ch))
                {
                    state = Link(state);
                    length = Length(state);
                }

                if (TryTransition(state, ch, out int next))
                {
                    state = next;
                    length++;
                }
                else
                {
                    state = 0;
                    length = 0;
                }

                if (length > best)
                {
                    best = length;
                }
            }

            return best;
        }

        private void Extend(char ch)
        {
            int current = size++;
            states[current].Length = states[last].Length + 1;
            int previous = last;
            while (previous >= 0 && !states[previous].Transitions.ContainsKey(ch))
            {
                states[previous].Transitions[ch] = current;
                previous = states[previous].Link;
            }

            if (previous == -1)
            {
                states[current].Link = 0;
            }
            else
            {
                int next = states[previous].Transitions[ch];
                if (states[previous].Length + 1 == states[next].Length)
                {
                    states[current].Link = next;
                }
                else
                {
                    int clone = size++;
                    states[clone].Length = states[previous].Length + 1;
                    states[clone].Transitions = new Dictionary<char, int>(states[next].Transitions);
                    states[clone].Link = states[next].Link;
                    while (previous >= 0 && states[previous].Transitions.TryGetValue(ch, out int target) && target == next)
                    {
                        states[previous].Transitions[ch] = clone;
                        previous = states[previous].Link;
                    }

                    states[next].Link = clone;
                    states[current].Link = clone;
                }
            }

            last = current;
        }

        private sealed class State
        {
            internal int Length { get; set; }

            internal int Link { get; set; } = -1;

            internal Dictionary<char, int> Transitions { get; set; } = [];
        }
    }
}

public sealed record SimilarityPeerInput(int SourceRowNumber, string QuestionId, string? Answer);

public sealed record SimilarityPeerResult(int SourceRowNumber, string QuestionId, decimal PeerMax, int? PeerRow);

public sealed record SimilarityPeerAnalysisResult(ImmutableArray<SimilarityPeerResult> Results)
{
    public SimilarityPeerResult? Find(int sourceRowNumber, string questionId) => Results.FirstOrDefault(result =>
        result.SourceRowNumber == sourceRowNumber
        && string.Equals(result.QuestionId, questionId, StringComparison.Ordinal));
}
