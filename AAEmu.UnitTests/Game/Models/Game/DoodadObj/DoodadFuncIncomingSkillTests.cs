using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class DoodadFuncIncomingSkillTests
{
    [Test]
    public async Task SkillHit_MatchesTemplateSkillWhenFuncSkillIdIsEmpty()
    {
        // Every SkillHit row in 10.0.2.13 has doodad_funcs.func_skill_id unset.
        var hit = new DoodadFuncSkillHit { SkillId = 21744 };
        await Assert.That(DoodadFuncIncomingSkill.TemplateAccepts(hit, 21744)).IsTrue();
        await Assert.That(DoodadFuncIncomingSkill.TemplateAccepts(hit, 21693)).IsFalse();
    }

    [Test]
    public async Task FakeUse_MatchesFakeSkillId()
    {
        var fake = new DoodadFuncFakeUse { FakeSkillId = 100 };
        await Assert.That(DoodadFuncIncomingSkill.TemplateAccepts(fake, 100)).IsTrue();
        await Assert.That(DoodadFuncIncomingSkill.TemplateAccepts(fake, 99)).IsFalse();
    }

    [Test]
    public async Task Use_MatchesSkillId()
    {
        var use = new DoodadFuncUse { SkillId = 50 };
        await Assert.That(DoodadFuncIncomingSkill.TemplateAccepts(use, 50)).IsTrue();
        await Assert.That(DoodadFuncIncomingSkill.TemplateAccepts(use, 0)).IsFalse();
    }

    [Test]
    public async Task NullOrZeroSkill_NeverMatches()
    {
        await Assert.That(DoodadFuncIncomingSkill.TemplateAccepts(null, 21693)).IsFalse();
        await Assert.That(DoodadFuncIncomingSkill.TemplateAccepts(new DoodadFuncSkillHit { SkillId = 21693 }, 0)).IsFalse();
    }
}
