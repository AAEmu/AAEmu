using AAEmu.Game.Models.Game.Skills.Plots.Tree;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots.Tree;

public class PlotTicketLedgerTests
{
    [Test]
    public async Task TwoChains_EachGetTheirOwnVisits()
    {
        var ledger = new PlotTicketLedger();
        var first = ledger.Allocate();
        var second = ledger.Allocate();

        await Assert.That(ledger.NextSelf(10, first)).IsEqualTo(1);
        await Assert.That(ledger.NextSelf(10, second)).IsEqualTo(1);
        await Assert.That(ledger.NextSelf(10, first)).IsEqualTo(2);
        await Assert.That(ledger.NextSelf(10, second)).IsEqualTo(2);
    }
}
