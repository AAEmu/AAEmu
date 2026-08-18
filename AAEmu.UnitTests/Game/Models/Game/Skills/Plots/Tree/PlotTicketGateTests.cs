using AAEmu.Game.Models.Game.Skills.Plots.Tree;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots.Tree;

public class PlotTicketGateTests
{
    [Test]
    public async Task TicketsOne_SelfLoopStopsAfterFirstVisit()
    {
        await Assert.That(PlotTicketGate.IsExhausted(1, 1, selfLoop: true)).IsFalse();
        await Assert.That(PlotTicketGate.IsExhausted(2, 1, selfLoop: true)).IsTrue();
    }

    [Test]
    public async Task TicketsOne_WithoutSelfLoop_AllowsMergedVisits()
    {
        await Assert.That(PlotTicketGate.IsExhausted(1, 1, selfLoop: false)).IsFalse();
        await Assert.That(PlotTicketGate.IsExhausted(5, 1, selfLoop: false)).IsFalse();
    }

    [Test]
    public async Task TicketsTwo_AllowsTwoVisits()
    {
        await Assert.That(PlotTicketGate.IsExhausted(1, 2)).IsFalse();
        await Assert.That(PlotTicketGate.IsExhausted(2, 2)).IsFalse();
        await Assert.That(PlotTicketGate.IsExhausted(3, 2)).IsTrue();
    }

    [Test]
    public async Task TicketsZero_Uncapped()
    {
        await Assert.That(PlotTicketGate.IsExhausted(99, 0)).IsFalse();
    }
}
