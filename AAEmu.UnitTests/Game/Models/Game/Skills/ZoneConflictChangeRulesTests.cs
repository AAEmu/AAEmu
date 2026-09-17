using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class ZoneConflictChangeRulesTests
{
    [Test]
    public async Task TheShippedValues_NameTheirStates()
    {
        // The 22 type-170 rows all put the state in value4 and read straight off ZoneConflictType:
        // 37734/41472/51166 전쟁 선포 pass 6, 37795/41471/41198 분쟁 선포 pass 5,
        // 37796/40261/41469 평화 선포 pass 7 and 37798/41470 위험 선포 pass 0.
        await Assert.That(ZoneConflictChangeRules.ResolveState(6)).IsEqualTo(ZoneConflictType.War);
        await Assert.That(ZoneConflictChangeRules.ResolveState(5)).IsEqualTo(ZoneConflictType.Conflict);
        await Assert.That(ZoneConflictChangeRules.ResolveState(7)).IsEqualTo(ZoneConflictType.Peace);
        await Assert.That(ZoneConflictChangeRules.ResolveState(0)).IsEqualTo(ZoneConflictType.Tension);
    }

    [Test]
    public async Task EveryStateOfTheEnumIsReachable()
    {
        for (var value = (int)ZoneConflictType.Tension; value <= (int)ZoneConflictType.Peace; value++)
            await Assert.That(ZoneConflictChangeRules.ResolveState(value)).IsNotNull();
    }

    [Test]
    public async Task AValueOutsideTheEnum_IsRefused()
    {
        // Tension is 0 and Peace 7, so 8 and up name nothing. -1 keeps the bounds check honest: a
        // range written as "value <= Peace" alone would let it through as (ZoneConflictType)(-1).
        await Assert.That(ZoneConflictChangeRules.ResolveState(8)).IsNull();
        await Assert.That(ZoneConflictChangeRules.ResolveState(-1)).IsNull();
    }

    [Test]
    public async Task DeclaringTheStateTheGroupIsAlreadyIn_IsNotAChange()
    {
        // SetStateLocked returns early on the same state, so asking for it would only add log noise.
        await Assert.That(ZoneConflictChangeRules.IsChange(ZoneConflictType.Peace, ZoneConflictType.Peace))
            .IsFalse();
        await Assert.That(ZoneConflictChangeRules.IsChange(ZoneConflictType.Peace, ZoneConflictType.War)).IsTrue();
        await Assert.That(ZoneConflictChangeRules.IsChange(ZoneConflictType.Tension, ZoneConflictType.War)).IsTrue();
    }
}
