using StudyReportEvaluator.App.Workbooks.Writing;
using Xunit;

namespace StudyReportEvaluator.App.Tests.Workbooks.Writing;

// Requirements: FR-028 (AC-029)
public sealed class AppOwnedSheetNameResolverTests
{
    private readonly AppOwnedSheetNameResolver resolver = new();

    [Fact]
    public void Resolve_uses_the_four_canonical_names_when_there_are_no_collisions()
    {
        AppOwnedSheetNames names = resolver.Resolve(["Original", "Final"]);

        Assert.Equal(AppOwnedSheetNameResolver.ConfigBaseName, names.ConfigSheetName);
        Assert.Equal(AppOwnedSheetNameResolver.ReferencesBaseName, names.ReferencesSheetName);
        Assert.Equal(AppOwnedSheetNameResolver.ResultsBaseName, names.ResultsSheetName);
        Assert.Equal(AppOwnedSheetNameResolver.RunBaseName, names.RunSheetName);
        Assert.Equal(
            [names.ConfigSheetName, names.ReferencesSheetName, names.ResultsSheetName, names.RunSheetName],
            names.AllSheetNames);
    }

    [Fact]
    public void Resolve_is_case_insensitive_and_selects_each_minimum_available_suffix()
    {
        string[] existing =
        [
            "quantification_config",
            "QUANTIFICATION_CONFIG (2)",
            "quantification_references",
            "QUANTIFICATION_REFERENCES (2)",
            "Quantification_Results",
            "Quantification_Results (3)",
            "quantification_run",
            "QUANTIFICATION_RUN (2)",
        ];

        AppOwnedSheetNames names = resolver.Resolve(existing);

        Assert.Equal("Quantification_Config (3)", names.ConfigSheetName);
        Assert.Equal("Quantification_References (3)", names.ReferencesSheetName);
        Assert.Equal("Quantification_Results (2)", names.ResultsSheetName);
        Assert.Equal("Quantification_Run (3)", names.RunSheetName);
        Assert.Equal(4, names.AllSheetNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(names.AllSheetNames, name => Assert.True(AppOwnedSheetNameResolver.IsValidWorksheetName(name)));
    }

    [Fact]
    public void ResolveUniqueName_truncates_a_31_character_base_only_enough_for_the_suffix()
    {
        string baseName = new('A', AppOwnedSheetNameResolver.MaximumSheetNameLength);
        string second = new string('A', 27) + " (2)";
        string third = new string('A', 27) + " (3)";

        Assert.Equal(second, resolver.ResolveUniqueName(baseName, [baseName]));
        Assert.Equal(third, resolver.ResolveUniqueName(baseName, [baseName, second]));
        Assert.Equal(31, second.Length);
        Assert.Equal(31, third.Length);
        Assert.True(AppOwnedSheetNameResolver.IsValidWorksheetName(second));
        Assert.True(AppOwnedSheetNameResolver.IsValidWorksheetName(third));
    }

    [Fact]
    public void ResolveUniqueName_does_not_split_a_surrogate_pair_when_truncating()
    {
        string baseName = new string('B', 26) + "😀BC";

        string resolved = resolver.ResolveUniqueName(baseName, [baseName]);

        Assert.Equal(new string('B', 26) + " (2)", resolved);
        Assert.Equal(30, resolved.Length);
        Assert.DoesNotContain('\uD83D', resolved);
        Assert.DoesNotContain('\uDE00', resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("History")]
    [InlineData("history")]
    [InlineData("Invalid/Name")]
    [InlineData("Invalid:Name")]
    [InlineData("'Leading")]
    [InlineData("Trailing'")]
    public void Invalid_or_reserved_generated_base_names_are_rejected(string baseName)
    {
        Assert.False(AppOwnedSheetNameResolver.IsValidWorksheetName(baseName));
        Assert.Throws<ArgumentException>(() => resolver.ResolveUniqueName(baseName, []));
    }

    [Fact]
    public void Binding_and_input_collections_are_copy_safe_and_representations_are_redacted()
    {
        string[] existing = ["Original"];
        AppOwnedSheetNames names = resolver.Resolve(existing);
        existing[0] = AppOwnedSheetNameResolver.ConfigBaseName;

        Assert.Equal(AppOwnedSheetNameResolver.ConfigBaseName, names.ConfigSheetName);
        IList<string> mutableView = Assert.IsAssignableFrom<IList<string>>(names.AllSheetNames);
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView.Add("Injected"));
        Assert.Contains("<redacted>", names.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(names.ConfigSheetName, names.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(names.ReferencesSheetName, names.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(names.ResultsSheetName, names.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(names.RunSheetName, names.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Null_inputs_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => resolver.Resolve((IEnumerable<string>)null!));
        Assert.Throws<ArgumentNullException>(() => resolver.ResolveUniqueName("Valid", null!));
    }
}
