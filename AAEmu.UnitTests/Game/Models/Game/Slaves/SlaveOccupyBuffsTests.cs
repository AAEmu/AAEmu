using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Spheres;

namespace AAEmu.UnitTests.Game.Models.Game.Slaves;

public class SphereBuffTargetsTests
{
    [Test]
    public async Task HarborHullBuffsDoNotSitOnTheCharacter()
    {
        await Assert.That(SphereBuffTargets.ApplyToCharacter(slaveApplicable: true)).IsFalse();
        await Assert.That(SphereBuffTargets.ApplyToSlave(slaveApplicable: true)).IsTrue();
    }

    [Test]
    public async Task ShipyardAllowedSitsOnTheCharacter()
    {
        await Assert.That(SphereBuffTargets.ApplyToCharacter(slaveApplicable: false)).IsTrue();
        await Assert.That(SphereBuffTargets.ApplyToSlave(slaveApplicable: false)).IsFalse();
    }
}

public class SlaveOccupyBuffsTests
{
    [Test]
    public async Task BuffIdsFromSkill_SkipsNonBuffEffects()
    {
        var template = new SkillTemplate
        {
            Id = 28472,
            Effects =
            [
                new SkillEffect { Template = new InteractionEffect() },
                new SkillEffect { Template = new BuffEffect { Buff = new BuffTemplate { Id = 4690 } } },
                new SkillEffect { Template = new BuffEffect { Buff = new BuffTemplate { Id = 11064 } } }
            ]
        };

        var ids = SlaveOccupyBuffs.BuffIdsFromSkill(template).ToList();

        await Assert.That(ids).IsEquivalentTo(new uint[] { 4690, 11064 });
    }

    [Test]
    public async Task BuffIdsFromSkill_EmptyWhenOccupyHasNoBuffs()
    {
        var template = new SkillTemplate { Id = 12076, Effects = [] };

        await Assert.That(SlaveOccupyBuffs.BuffIdsFromSkill(template).Any()).IsFalse();
    }
}
