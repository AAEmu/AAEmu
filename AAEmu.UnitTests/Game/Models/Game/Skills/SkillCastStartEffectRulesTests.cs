using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class SkillCastStartEffectRulesTests
{
    [Test]
    public async Task SourceToPos_SpawnDoodad_IsCastStartTelegraph()
    {
        var effect = new SkillEffect
        {
            ApplicationMethod = SkillEffectApplicationMethod.SourceToPos,
            Template = new SpecialEffect { SpecialEffectTypeId = SpecialType.SpawnDoodad }
        };

        await Assert.That(SkillCastStartEffectRules.IsCastStartTelegraph(effect)).IsTrue();
        await Assert.That(SkillCastStartEffectRules.AppliesAtFire(effect)).IsFalse();
    }

    [Test]
    public async Task SourceToPos_OtherSpecial_StaysAtFire()
    {
        var effect = new SkillEffect
        {
            ApplicationMethod = SkillEffectApplicationMethod.SourceToPos,
            Template = new SpecialEffect { SpecialEffectTypeId = SpecialType.RemoveDoodad }
        };

        await Assert.That(SkillCastStartEffectRules.IsCastStartTelegraph(effect)).IsFalse();
        await Assert.That(SkillCastStartEffectRules.AppliesAtFire(effect)).IsTrue();
    }

    [Test]
    public async Task TargetMethod_SpawnDoodad_StaysAtFire()
    {
        var effect = new SkillEffect
        {
            ApplicationMethod = SkillEffectApplicationMethod.Target,
            Template = new SpecialEffect { SpecialEffectTypeId = SpecialType.SpawnDoodad }
        };

        await Assert.That(SkillCastStartEffectRules.IsCastStartTelegraph(effect)).IsFalse();
        await Assert.That(SkillCastStartEffectRules.AppliesAtFire(effect)).IsTrue();
    }
}
