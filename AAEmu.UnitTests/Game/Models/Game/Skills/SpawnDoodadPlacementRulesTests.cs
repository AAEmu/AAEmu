using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SpawnDoodadPlacementRulesTests
{
    [Test]
    public async Task LocationTarget_KeepsTheAreaStand()
    {
        var caster = new BaseUnit { ObjId = 11 };
        var location = new BaseUnit { ObjId = uint.MaxValue };

        await Assert.That(SpawnDoodadPlacementRules.Anchor(caster, location)).IsSameReferenceAs(location);
        await Assert.That(SpawnDoodadPlacementRules.NudgeOffCaster(caster, location)).IsFalse();
    }

    [Test]
    public async Task CasterTarget_StepsForward()
    {
        var caster = new BaseUnit { ObjId = 11 };

        await Assert.That(SpawnDoodadPlacementRules.Anchor(caster, caster)).IsSameReferenceAs(caster);
        await Assert.That(SpawnDoodadPlacementRules.NudgeOffCaster(caster, caster)).IsTrue();
        await Assert.That(SpawnDoodadPlacementRules.NudgeOffCaster(caster, null)).IsTrue();
    }
}
