using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.InstantGame;

/// <summary>
/// When a dungeon copy is handed over. The client drops UI events raised while its loading screen is up,
/// so the instance load only marks the hand-over as owed and the client's post-load re-entry check is
/// answered with it — once per load, and only inside a copy.
/// </summary>
public class InstantGameHandoverRulesTests
{
    [Test]
    public async Task OwedHandover_InsideCopy_IsSent()
    {
        await Assert.That(InstantGameHandoverRules.ShouldHandOverOnReentryCheck(
            handoverPending: true, insideDungeonCopy: true)).IsTrue();
    }

    /// <summary>Every later loading screen also ends in a re-entry check; with nothing owed it gets no answer.</summary>
    [Test]
    public async Task NothingOwed_InsideCopy_IsNotSent()
    {
        await Assert.That(InstantGameHandoverRules.ShouldHandOverOnReentryCheck(
            handoverPending: false, insideDungeonCopy: true)).IsFalse();
    }

    /// <summary>A player who left the copy before the check arrived is not handed a copy they are not in.</summary>
    [Test]
    public async Task OwedHandover_OutsideCopy_IsNotSent()
    {
        await Assert.That(InstantGameHandoverRules.ShouldHandOverOnReentryCheck(
            handoverPending: true, insideDungeonCopy: false)).IsFalse();
    }

    [Test]
    public async Task OpenWorldCheck_IsNotAnswered()
    {
        await Assert.That(InstantGameHandoverRules.ShouldHandOverOnReentryCheck(
            handoverPending: false, insideDungeonCopy: false)).IsFalse();
    }

    /// <summary>
    /// The owed flag is consumed by the first check, so the re-entry check the hand-over's own events
    /// trigger does not send it again.
    /// </summary>
    [Test]
    public async Task PendingFlag_IsConsumedOnce()
    {
        var character = new Character(new UnitCustomModelParams());

        await Assert.That(character.TakeInstantGameHandoverPending()).IsFalse();

        character.MarkInstantGameHandoverPending();
        await Assert.That(character.TakeInstantGameHandoverPending()).IsTrue();
        await Assert.That(character.TakeInstantGameHandoverPending()).IsFalse();
    }

    [Test]
    public async Task DungeonLoad_DoesNotUnlockOnInstanceLoaded()
    {
        await Assert.That(InstantGameHandoverRules.CompletesArrivalOnInstanceLoaded(
            insideDungeonCopy: true)).IsFalse();
    }

    [Test]
    public async Task OverworldLoad_UnlocksOnInstanceLoaded()
    {
        await Assert.That(InstantGameHandoverRules.CompletesArrivalOnInstanceLoaded(
            insideDungeonCopy: false)).IsTrue();
    }

    [Test]
    public async Task LockedDungeon_UnlocksOnReentryCheck()
    {
        await Assert.That(InstantGameHandoverRules.CompletesArrivalOnReentryCheck(
            positionStillLocked: true, insideDungeonCopy: true)).IsTrue();
    }

    [Test]
    public async Task AlreadyUnlocked_DoesNotSnapAgain()
    {
        await Assert.That(InstantGameHandoverRules.CompletesArrivalOnReentryCheck(
            positionStillLocked: false, insideDungeonCopy: true)).IsFalse();
    }

    [Test]
    public async Task LockedOverworld_DoesNotWaitForReentry()
    {
        await Assert.That(InstantGameHandoverRules.CompletesArrivalOnReentryCheck(
            positionStillLocked: true, insideDungeonCopy: false)).IsFalse();
    }
}
