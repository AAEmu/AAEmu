using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class UnitStateGroundRulesTests
{
    [Test]
    public async Task ShouldSnap_NullAndZoneMirror_AreSkipped()
    {
        await Assert.That(UnitStateGroundRules.ShouldSnapWorldNpcToHeightmap(null)).IsFalse();

        var mirror = new Npc { IsZoneMirror = true };
        await Assert.That(UnitStateGroundRules.ShouldSnapWorldNpcToHeightmap(mirror)).IsFalse();
    }

    [Test]
    public async Task ShouldSnap_OrdinaryWorldNpc_Snaps()
    {
        await Assert.That(UnitStateGroundRules.ShouldSnapWorldNpcToHeightmap(new Npc())).IsTrue();
    }

    [Test]
    public async Task ShouldSnap_PortalUnits_KeepAuthoredHeight()
    {
        await Assert.That(UnitStateGroundRules.ShouldSnapWorldNpcToHeightmap(new Portal())).IsFalse();
        await Assert.That(UnitStateGroundRules.ShouldSnapWorldNpcToHeightmap(
            new Portal { IsExit = true })).IsFalse();
    }
}
