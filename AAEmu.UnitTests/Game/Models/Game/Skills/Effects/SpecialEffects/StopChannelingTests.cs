using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Type 80 stop_channeling ends the target's running channel the way an interrupt does. The setup
/// mirrors SkillCastInterruptRulesTests: a bare unit holding an EndChannelingTask.
/// </summary>
public class StopChannelingTests
{
    private static Skill ChannelSkill(Unit caster) => new()
    {
        Id = 10714,
        TlId = SkillTlIdManager.GetNextId(caster),
        Template = new SkillTemplate { Id = 10714, ChannelingTime = 12000, ChannelingTick = 1000 }
    };

    private static EndChannelingTask ChannelTask(Skill skill, Unit caster) => new(skill, caster,
        new SkillCasterUnit(caster.ObjId), caster, new SkillCastUnitTarget(caster.ObjId), new SkillObject(), null);

    private static void Execute(BaseUnit target) => new StopChanneling().Execute(
        target, null, target, null, null, null, null, DateTime.UtcNow, 0, 0, 0, 0);

    [Test]
    public async Task ChannellingTarget_HasItsChannelStopped()
    {
        var unit = new Unit { ObjId = 601, Level = 60, Hp = 1000, MaxHp = 1000 };
        var skill = ChannelSkill(unit);
        unit.SkillTask = ChannelTask(skill, unit);

        Execute(unit);

        await Assert.That(skill.Cancelled).IsTrue();
        await Assert.That(unit.SkillTask).IsNull();
        await Assert.That(skill.TlId).IsEqualTo((ushort)0);
    }

    [Test]
    public async Task TargetThatIsCasting_NotChannelling_IsLeftAlone()
    {
        // Only a channel is stopped; an ordinary cast in progress is not this effect's business.
        var unit = new Unit { ObjId = 602, Level = 60, Hp = 1000, MaxHp = 1000 };
        var skill = ChannelSkill(unit);
        var cast = new CastTask(skill, unit, new SkillCasterUnit(unit.ObjId), unit,
            new SkillCastUnitTarget(unit.ObjId), new SkillObject());
        unit.SkillTask = cast;

        Execute(unit);

        await Assert.That(skill.Cancelled).IsFalse();
        await Assert.That(unit.SkillTask).IsSameReferenceAs(cast);
    }

    [Test]
    public async Task IdleTarget_AndNoTarget_DoNothing()
    {
        var unit = new Unit { ObjId = 603, Level = 60, Hp = 1000, MaxHp = 1000 };

        Execute(unit);
        Execute(null);

        await Assert.That(unit.SkillTask).IsNull();
    }
}
