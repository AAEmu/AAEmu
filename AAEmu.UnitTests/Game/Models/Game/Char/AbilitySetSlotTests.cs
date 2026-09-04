using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

/// <summary>
/// Pure loadout-match rules used by skillsaver activate no-op detection.
/// </summary>
public class AbilitySetSlotTests
{
    [Test]
    public async Task MatchesTriad_RequiresOrderedEquality()
    {
        var slot = new AbilitySetSlot
        {
            SlotIndex = 0,
            Ability1 = AbilityType.Fight,
            Ability2 = AbilityType.Illusion,
            Ability3 = AbilityType.Wild
        };

        await Assert.That(slot.MatchesTriad(AbilityType.Fight, AbilityType.Illusion, AbilityType.Wild)).IsTrue();
        await Assert.That(slot.MatchesTriad(AbilityType.Illusion, AbilityType.Fight, AbilityType.Wild)).IsFalse();
    }

    [Test]
    public async Task MatchesSkillLoadout_IsOrderIndependent()
    {
        var slot = new AbilitySetSlot { SlotIndex = 0 };
        slot.SkillIds.AddRange([10u, 20u, 30u]);
        slot.PassiveBuffIds.AddRange([100u, 200u]);

        await Assert.That(slot.MatchesSkillLoadout([30u, 10u, 20u], [200u, 100u])).IsTrue();
        await Assert.That(slot.MatchesSkillLoadout([10u, 20u], [100u, 200u])).IsFalse();
        await Assert.That(slot.MatchesSkillLoadout([10u, 20u, 30u], [100u])).IsFalse();
        await Assert.That(slot.MatchesSkillLoadout([10u, 20u, 99u], [100u, 200u])).IsFalse();
    }
}
