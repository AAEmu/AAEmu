using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// Pins which Zone skills World treats as the NPC leash reset. Getting this wrong makes NPCs
/// unkillable — the Kraken (npc 7607) casts 14079 크라켄의 먹구름 every five seconds, and while
/// any heal-bearing skill counted, World restored it to full HP faster than it could be damaged.
/// </summary>
public class ZoneAuthorityCombatTests
{
    private static SkillTemplate Skill(params EffectTemplate[] effects)
    {
        var template = new SkillTemplate { Id = 1 };
        foreach (var effect in effects)
            template.Effects.Add(new SkillEffect { Template = effect });
        return template;
    }

    [Test]
    public async Task IsLeashResetSkill_HealAndManaOnly_IsTheLeashReset()
    {
        // 11503 NPC 회귀 — the real leash skill.
        var template = Skill(new HealEffect(), new RestoreManaEffect());

        await Assert.That(ZoneAuthorityCombat.IsLeashResetSkill(template)).IsTrue();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task IsLeashResetSkill_HealOrManaAlone_StillCounts(bool heal)
    {
        var template = heal ? Skill(new HealEffect()) : Skill(new RestoreManaEffect());

        await Assert.That(ZoneAuthorityCombat.IsLeashResetSkill(template)).IsTrue();
    }

    [Test]
    public async Task IsLeashResetSkill_HealAlongsideOtherContent_IsNotALeashReset()
    {
        // 14079 크라켄의 먹구름 — InteractionEffect + HealEffect. This is the case that made the
        // Kraken immortal.
        var template = Skill(new InteractionEffect(), new HealEffect());

        await Assert.That(ZoneAuthorityCombat.IsLeashResetSkill(template)).IsFalse();
    }

    [Test]
    public async Task IsLeashResetSkill_NoRestoreAtAll_IsNotALeashReset()
    {
        await Assert.That(ZoneAuthorityCombat.IsLeashResetSkill(Skill(new DamageEffect()))).IsFalse();
    }

    [Test]
    public async Task IsLeashResetSkill_EmptyOrNull_IsNotALeashReset()
    {
        await Assert.That(ZoneAuthorityCombat.IsLeashResetSkill(Skill())).IsFalse();
        await Assert.That(ZoneAuthorityCombat.IsLeashResetSkill(null)).IsFalse();
    }
}
