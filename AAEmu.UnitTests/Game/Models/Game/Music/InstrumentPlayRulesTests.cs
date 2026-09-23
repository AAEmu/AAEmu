using AAEmu.Game.Models.Game.Music;

namespace AAEmu.UnitTests.Game.Models.Game.Music;

/// <summary>
/// Which source a play resolves through: the instrument doodad the player is attached to wins over
/// an instrument item they hold, a player who may not use the placed one falls back to their own,
/// and a player with nothing of their own is refused rather than played through anything.
/// </summary>
public class InstrumentPlayRulesTests
{
    [Test]
    public async Task AnInstrumentThePlayerMayUse_WinsOverTheHeldOne()
    {
        var decision = InstrumentPlayRules.Resolve(
            placedIsInstrument: true, mayPlayPlaced: true, placedBuffId: 91001, placedBuffActive: false,
            heldIsInstrument: true, heldBuffId: 91002, heldBuffActive: false);

        await Assert.That(decision.Outcome).IsEqualTo(InstrumentPlayOutcome.Applied);
        await Assert.That(decision.Source).IsEqualTo(InstrumentSource.PlacedDoodad);
        await Assert.That(decision.BuffId).IsEqualTo(91001u);
    }

    [Test]
    public async Task APlacedInstrumentTheyMayNotUse_FallsBackToTheirOwn()
    {
        var decision = InstrumentPlayRules.Resolve(
            placedIsInstrument: true, mayPlayPlaced: false, placedBuffId: 91001, placedBuffActive: false,
            heldIsInstrument: true, heldBuffId: 91002, heldBuffActive: false);

        await Assert.That(decision.Outcome).IsEqualTo(InstrumentPlayOutcome.Applied);
        await Assert.That(decision.Source).IsEqualTo(InstrumentSource.HeldItem);
        await Assert.That(decision.BuffId).IsEqualTo(91002u);
    }

    [Test]
    public async Task APlacedInstrumentTheyMayNotUse_WithNothingOfTheirOwn_IsRefused()
    {
        var decision = InstrumentPlayRules.Resolve(
            placedIsInstrument: true, mayPlayPlaced: false, placedBuffId: 91001, placedBuffActive: false,
            heldIsInstrument: false, heldBuffId: 0, heldBuffActive: false);

        await Assert.That(decision.Outcome).IsEqualTo(InstrumentPlayOutcome.RefusedNotYours);
        await Assert.That(decision.Source).IsEqualTo(InstrumentSource.PlacedDoodad);
        await Assert.That(decision.BuffId).IsEqualTo(0u);
    }

    [Test]
    public async Task AHeldInstrumentPlaysWithoutAnythingToSitAt()
    {
        var decision = InstrumentPlayRules.Resolve(
            placedIsInstrument: false, mayPlayPlaced: false, placedBuffId: 0, placedBuffActive: false,
            heldIsInstrument: true, heldBuffId: 91002, heldBuffActive: false);

        await Assert.That(decision.Outcome).IsEqualTo(InstrumentPlayOutcome.Applied);
        await Assert.That(decision.Source).IsEqualTo(InstrumentSource.HeldItem);
        await Assert.That(decision.BuffId).IsEqualTo(91002u);
    }

    [Test]
    public async Task NothingContentCallsAnInstrument_IsRefused()
    {
        var decision = InstrumentPlayRules.Resolve(
            placedIsInstrument: false, mayPlayPlaced: true, placedBuffId: 0, placedBuffActive: false,
            heldIsInstrument: false, heldBuffId: 0, heldBuffActive: false);

        await Assert.That(decision.Outcome).IsEqualTo(InstrumentPlayOutcome.RefusedNoInstrument);
        await Assert.That(decision.Source).IsEqualTo(InstrumentSource.None);
    }

    [Test]
    public async Task TheInstrumentsBuffBeingOnThemAlready_AppliesNothingASecondTime()
    {
        var placed = InstrumentPlayRules.Resolve(
            placedIsInstrument: true, mayPlayPlaced: true, placedBuffId: 91001, placedBuffActive: true,
            heldIsInstrument: false, heldBuffId: 0, heldBuffActive: false);
        var held = InstrumentPlayRules.Resolve(
            placedIsInstrument: false, mayPlayPlaced: false, placedBuffId: 0, placedBuffActive: false,
            heldIsInstrument: true, heldBuffId: 91002, heldBuffActive: true);

        await Assert.That(placed.Outcome).IsEqualTo(InstrumentPlayOutcome.AlreadyApplied);
        await Assert.That(placed.Source).IsEqualTo(InstrumentSource.PlacedDoodad);
        await Assert.That(held.Outcome).IsEqualTo(InstrumentPlayOutcome.AlreadyApplied);
        await Assert.That(held.Source).IsEqualTo(InstrumentSource.HeldItem);
    }

    [Test]
    public async Task AnInstrumentThatCarriesNoBuff_StillPlays()
    {
        // The shipped table has percussion doodads with no buff of their own (their row carries the
        // midi only); "no buff" is not "no instrument".
        var decision = InstrumentPlayRules.Resolve(
            placedIsInstrument: true, mayPlayPlaced: true, placedBuffId: 0, placedBuffActive: false,
            heldIsInstrument: false, heldBuffId: 0, heldBuffActive: false);

        await Assert.That(decision.Outcome).IsEqualTo(InstrumentPlayOutcome.Applied);
        await Assert.That(decision.BuffId).IsEqualTo(0u);
    }
}
