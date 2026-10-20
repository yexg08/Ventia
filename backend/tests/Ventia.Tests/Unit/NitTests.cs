using Ventia.Application.Common;

namespace Ventia.Tests.Unit;

public class NitTests
{
    [Theory]
    [InlineData("800197268", 4)] // NIT de la DIAN
    [InlineData("900123456", 8)]
    [InlineData("860002964", 4)]
    public void Check_digit_follows_the_dian_algorithm(string number, int expected) =>
        Assert.Equal(expected, Nit.CheckDigit(number));

    [Theory]
    [InlineData("800197268-4", "800197268-4")]
    [InlineData("800.197.268-4", "800197268-4")]
    [InlineData(" 900123456 - 8 ", "900123456-8")]
    public void Valid_nits_are_normalized(string input, string expected)
    {
        Assert.True(Nit.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("800197268-5")] // digito equivocado
    [InlineData("800197268")]   // sin digito
    [InlineData("8001972684")]
    [InlineData("12345-6")]     // muy corto
    [InlineData("80019726A-4")]
    [InlineData("800197268-44")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_nits_are_rejected(string? input) => Assert.False(Nit.TryNormalize(input, out _));
}
