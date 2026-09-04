using AAEmu.Game.Core.Managers.World;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

/// <summary>
/// Occupied doodad clouts (duration 0) must keep ticking after the owner region goes idle,
/// otherwise leave never runs and the inside-buff sticks on the player.
/// </summary>
public class AreaTriggerManagerTests
{
    [Test]
    [Arguments(true, false, true)]
    [Arguments(true, true, true)]
    [Arguments(false, true, true)]
    [Arguments(false, false, false)]
    public async Task ShouldTick_KeepsOccupiedTriggersAliveWhenRegionIsIdle(
        bool ownerRegionHasPlayers, bool hasOccupants, bool expected)
    {
        var tick = AreaTriggerManager.ShouldTick(ownerRegionHasPlayers, hasOccupants);

        await Assert.That(tick).IsEqualTo(expected);
    }
}
