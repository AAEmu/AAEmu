using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// What a <c>buff_effects</c> row with no <c>buffs</c> row has to do when something reaches it. Rows 307
/// (buff_id 232), 780 and 15918-15920 are the ones enabled skills 10580, 11454 and 28698-28700 bind through
/// <c>skill_effects</c>; before the guard <see cref="BuffEffect.Apply"/> dereferenced
/// <c>Buff.RequireBuffId</c> and threw NullReferenceException on every cast, and <c>BuffId</c> /
/// <c>OnActionTime</c> threw for every caller that only read them.
/// </summary>
public class BuffEffectMissingTemplateTests
{
    /// <summary>buff_effects row 307 as the loader leaves it when buff 232 is missing.</summary>
    private static BuffEffect EffectWithoutBuff() => new() { Id = 307, Chance = 100 };

    private static List<Buff> AppliedBuffs(Unit unit)
    {
        var good = new List<Buff>();
        var bad = new List<Buff>();
        var hidden = new List<Buff>();
        unit.Buffs.GetAllBuffs(good, bad, hidden, true);
        return [.. good, .. bad, .. hidden];
    }

    [Test]
    public async Task BuffId_NoBuffTemplate_IsZero()
    {
        await Assert.That(EffectWithoutBuff().BuffId).IsEqualTo(0u);
    }

    [Test]
    public async Task OnActionTime_NoBuffTemplate_IsFalse()
    {
        await Assert.That(EffectWithoutBuff().OnActionTime).IsFalse();
    }

    [Test]
    public async Task Apply_NoBuffTemplate_DoesNotThrowAndAppliesNothing()
    {
        var caster = new Unit { ObjId = 100 };
        var target = new Unit { ObjId = 101 };

        EffectWithoutBuff().Apply(caster, new SkillCasterUnit(caster.ObjId), target,
            new SkillCastUnitTarget(target.ObjId), new CastSkill(10580, 1), new EffectSource(), new SkillObject(),
            DateTime.UtcNow);

        await Assert.That(AppliedBuffs(target)).IsEmpty();
    }

    [Test]
    public async Task SkillEffectDispatch_NoBuffTemplate_KeepsTheBindingAndStillAppliesNothing()
    {
        // Skill 10580 소생의 물약: skill_effects 819 → effects 889 → buff_effects 307. The binding has to
        // survive (a null Template here would make Skill.cs log "Template not found" and drop the whole
        // effect list entry) while the effect itself stays inert.
        var inertEffect = EffectWithoutBuff();
        var skillTemplate = new SkillTemplate { Id = 10580 };
        skillTemplate.Effects.Add(new SkillEffect { EffectId = 889, Template = inertEffect });

        var caster = new Unit { ObjId = 100 };
        var target = new Unit { ObjId = 101 };
        var skill = new Skill { Id = 10580, Template = skillTemplate, TlId = 1 };

        // Skill.cs applies every bound effect with a CastSkill action and a skill-carrying EffectSource.
        foreach (var skillEffect in skillTemplate.Effects)
        {
            await Assert.That(skillEffect.Template).IsNotNull();
            skillEffect.Template.Apply(caster, new SkillCasterUnit(caster.ObjId), target,
                new SkillCastUnitTarget(target.ObjId), new CastSkill(skill.Id, skill.TlId), new EffectSource(skill),
                new SkillObject(), DateTime.UtcNow);
        }

        await Assert.That(AppliedBuffs(target)).IsEmpty();
    }
}
