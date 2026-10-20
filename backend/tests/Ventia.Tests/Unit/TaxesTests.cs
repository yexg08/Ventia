using Ventia.Domain.Entities;

namespace Ventia.Tests.Unit;

public class TaxesTests
{
    [Theory]
    [InlineData(4000, TaxType.Inc8, 3703.70, 296.30)]
    [InlineData(18000, TaxType.Inc8, 16666.67, 1333.33)]
    [InlineData(11900, TaxType.Iva19, 10000.00, 1900.00)]
    [InlineData(10500, TaxType.Iva5, 10000.00, 500.00)]
    [InlineData(2500, TaxType.None, 2500.00, 0.00)]
    [InlineData(0, TaxType.Iva19, 0.00, 0.00)]
    public void Split_separates_base_and_tax(decimal gross, TaxType tax, decimal expectedBase, decimal expectedTax)
    {
        var split = Taxes.Split(gross, tax);
        Assert.Equal(expectedBase, split.Base);
        Assert.Equal(expectedTax, split.Tax);
    }

    [Fact]
    public void Base_plus_tax_is_always_exactly_the_price()
    {
        for (var price = 0m; price <= 50_000m; price += 37.5m)
        foreach (var tax in Enum.GetValues<TaxType>())
        {
            var split = Taxes.Split(price, tax);
            Assert.Equal(price, split.Base + split.Tax);
            Assert.True(split.Tax >= 0);
        }
    }
}
