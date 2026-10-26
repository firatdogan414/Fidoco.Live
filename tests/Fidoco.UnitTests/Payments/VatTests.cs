using Fidoco.Modules.Payments.Domain;

namespace Fidoco.UnitTests.Payments;

public sealed class VatTests
{
    [Theory]
    [InlineData(19900, 25, 3980)]
    [InlineData(10000, 25, 2000)]
    [InlineData(19900, 6, 1126)]
    [InlineData(19900, 0, 0)]
    [InlineData(1, 25, 0)]
    public void Included_vat_is_extracted_from_gross_amount(long gross, int rate, long expected)
    {
        Assert.Equal(expected, Vat.Included(gross, rate));
    }
}
