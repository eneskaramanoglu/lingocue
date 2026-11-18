using LingoCue.Core.Text;

namespace LingoCue.Core.Tests.Text;

public class TurkishTextTests
{
    [Theory]
    [InlineData("istanbul", "İSTANBUL")]
    [InlineData("ılık", "ILIK")]
    [InlineData("çğöşü", "ÇĞÖŞÜ")]
    public void ToUpper_uses_turkish_dotted_and_dotless_i(string input, string expected)
        => TurkishText.ToUpper(input).ShouldBe(expected);

    [Theory]
    [InlineData("İSTANBUL", "istanbul")]
    [InlineData("ILIK", "ılık")]
    public void ToLower_uses_turkish_dotted_and_dotless_i(string input, string expected)
        => TurkishText.ToLower(input).ShouldBe(expected);

    [Fact]
    public void Invariant_casing_would_be_wrong_which_is_why_the_helper_exists()
        => "istanbul".ToUpperInvariant().ShouldNotBe(TurkishText.ToUpper("istanbul"));
}
