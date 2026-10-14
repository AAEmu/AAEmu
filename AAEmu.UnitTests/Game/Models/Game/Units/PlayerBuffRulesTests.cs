using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// Loadout and zone-group buffs belong to player characters only, and the zone buff follows the group the
/// character stands in rather than the zone key it is reported to come from.
/// </summary>
public class PlayerBuffRulesTests
{
    private const uint CommanderBuff = 100;
    private const uint DungeonCheckBuff = 200;

    [Test]
    public async Task ACharacter_GetsLoadoutAndZoneBuffs()
    {
        var character = new Character(new UnitCustomModelParams());

        await Assert.That(PlayerBuffRules.GrantsLoadoutBuffs(character)).IsTrue();
        await Assert.That(PlayerBuffRules.ReceivesZoneGroupBuffs(character)).IsTrue();
    }

    [Test]
    public async Task AnNpcWearingGear_GetsNeither()
    {
        var npc = new Npc();

        await Assert.That(PlayerBuffRules.GrantsLoadoutBuffs(npc)).IsFalse();
        await Assert.That(PlayerBuffRules.ReceivesZoneGroupBuffs(npc)).IsFalse();
    }

    [Test]
    public async Task LeavingForAGroupWithoutABuff_DropsTheHeldOne()
    {
        var remove = PlayerBuffRules.ZoneBuffsToRemove([CommanderBuff], 0);

        await Assert.That(remove).IsEquivalentTo(new[] { CommanderBuff });
        await Assert.That(PlayerBuffRules.ShouldAddZoneBuff([CommanderBuff], 0)).IsFalse();
    }

    [Test]
    public async Task MovingBetweenGroups_SwapsTheBuff()
    {
        var remove = PlayerBuffRules.ZoneBuffsToRemove([CommanderBuff], DungeonCheckBuff);

        await Assert.That(remove).IsEquivalentTo(new[] { CommanderBuff });
        await Assert.That(PlayerBuffRules.ShouldAddZoneBuff([CommanderBuff], DungeonCheckBuff)).IsTrue();
    }

    [Test]
    public async Task GroupsSharingABuff_KeepIt()
    {
        await Assert.That(PlayerBuffRules.ZoneBuffsToRemove([DungeonCheckBuff], DungeonCheckBuff)).IsEmpty();
        await Assert.That(PlayerBuffRules.ShouldAddZoneBuff([DungeonCheckBuff], DungeonCheckBuff)).IsFalse();
    }

    [Test]
    public async Task EnteringWithNothingHeld_AddsTheGroupBuff()
    {
        await Assert.That(PlayerBuffRules.ZoneBuffsToRemove([], CommanderBuff)).IsEmpty();
        await Assert.That(PlayerBuffRules.ShouldAddZoneBuff([], CommanderBuff)).IsTrue();
    }
}
