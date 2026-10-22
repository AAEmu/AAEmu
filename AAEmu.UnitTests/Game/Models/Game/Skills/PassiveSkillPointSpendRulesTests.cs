using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class PassiveSkillPointSpendRulesTests
{
    [Test]
    public async Task PoolCost_ZeroWhenColumnZero()
    {
        var template = new PassiveBuffTemplate { SkillPoints = 0, Active = true, ReqPoints = 0 };
        await Assert.That(PassiveSkillPointSpendRules.PoolCost(template)).IsEqualTo(0);
    }

    [Test]
    public async Task PoolCost_SystemActive_IgnoresColumnCost()
    {
        // Compact passive_buffs id 369 (shipwreck treasure start): active=t, req_points=0, skill_points=1.
        var template = new PassiveBuffTemplate { SkillPoints = 1, Active = true, ReqPoints = 0 };
        await Assert.That(PassiveSkillPointSpendRules.PoolCost(template)).IsEqualTo(0);
    }

    [Test]
    public async Task PoolCost_ClickLearnedGeneral_ChargesColumn()
    {
        var template = new PassiveBuffTemplate { SkillPoints = 1, Active = false, ReqPoints = 99 };
        await Assert.That(PassiveSkillPointSpendRules.PoolCost(template)).IsEqualTo(1);
    }

    [Test]
    public async Task PoolCost_Null_IsZero()
    {
        await Assert.That(PassiveSkillPointSpendRules.PoolCost(null)).IsEqualTo(0);
    }
}
