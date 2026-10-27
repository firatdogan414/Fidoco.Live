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

public sealed class CommissionTests
{
    [Theory]
    [InlineData(15920, 1, 15, 0, 2388)]
    [InlineData(15920, 1, 0, 500, 500)]
    [InlineData(31840, 2, 10, 500, 4184)]
    [InlineData(1000, 1, 50, 900, 1000)]
    [InlineData(15920, 1, 0, 0, 0)]
    public void Fee_is_percent_of_net_plus_fixed_per_ticket_capped_at_net(long net, int quantity, int percent, long fixedPerTicket, long expected)
    {
        Assert.Equal(expected, Commission.Fee(net, quantity, percent, fixedPerTicket));
    }
}
