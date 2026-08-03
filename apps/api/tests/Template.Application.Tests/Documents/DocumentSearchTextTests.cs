using Template.Application.Documents;

namespace Template.Application.Tests.Documents;

public sealed class DocumentSearchTextTests
{
    [Fact]
    public void Normalize_UsesInvariantUnicodeWordSemantics()
    {
        Assert.Equal("еж api", DocumentSearchText.Normalize("  Ёж, API!  "));
        Assert.Equal("café 42 東京", DocumentSearchText.Normalize("CAFÉ\t42—東京"));
    }

    [Fact]
    public void Normalize_UsesFullDefaultLowercaseForDottedCapitalI()
    {
        Assert.Equal("i", DocumentSearchText.Normalize("İ"));
        Assert.Equal("ai b", DocumentSearchText.Normalize("AİB"));
    }

    [Fact]
    public void Normalize_UsesContextualFinalGreekSigma()
    {
        Assert.Equal("ος οσα", DocumentSearchText.Normalize("ΟΣ ΟΣΑ"));
    }

    [Fact]
    public void CreateQueryVariants_AddsTheExactOppositeKeyboardLayout()
    {
        Assert.Equal(["фзш м1", "api v1"], DocumentSearchText.CreateQueryVariants("фзш м1"));
        Assert.Equal(["api v1", "фзш м1"], DocumentSearchText.CreateQueryVariants("API V1"));
        Assert.Equal(["руддщ", "hello"], DocumentSearchText.CreateQueryVariants("руддщ"));
    }

    [Fact]
    public void CreateQueryVariants_MapsEnglishBacktickToNormalizedRussianYo()
    {
        Assert.Equal(["krf", "елка"], DocumentSearchText.CreateQueryVariants("`krf"));
    }

    [Fact]
    public void CreateQueryVariants_DoesNotGuessMixedOrUnsupportedLetterLayouts()
    {
        Assert.Equal(["api ф"], DocumentSearchText.CreateQueryVariants("api ф"));
        Assert.Equal(["café"], DocumentSearchText.CreateQueryVariants("café"));
        Assert.Empty(DocumentSearchText.CreateQueryVariants(" ! "));
    }

    [Theory]
    [InlineData("api", 0)]
    [InlineData("docs", 1)]
    [InlineData("search", 1)]
    [InlineData("abcdefgh", 2)]
    [InlineData("😀abc", 1)]
    public void GetAllowedTypoDistance_UsesUtf16TokenLength(string token, int expected)
    {
        Assert.Equal(expected, DocumentSearchText.GetAllowedTypoDistance(token));
    }

    [Theory]
    [InlineData("search", "search", 0)]
    [InlineData("search", "seacrh", 1)]
    [InlineData("search", "searched", 2)]
    [InlineData("поиск", "пиоск", 1)]
    public void GetDamerauLevenshteinDistance_CountsAdjacentTranspositionAsOne(
        string left,
        string right,
        int expected)
    {
        Assert.Equal(expected, DocumentSearchText.GetDamerauLevenshteinDistance(left, right));
    }
}
