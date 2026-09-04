using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncSkillHitTests
{
    [Test]
    public async Task AdvancesPhase_MatchingSkill_IsTrue()
    {
        await Assert.That(DoodadFuncSkillHit.AdvancesPhase(21693, 21693)).IsTrue();
    }

    [Test]
    public async Task AdvancesPhase_OtherChumSkill_IsFalse()
    {
        // Idle freshwater 26362 lists 21693 then 21744. The wrong row must not advance.
        await Assert.That(DoodadFuncSkillHit.AdvancesPhase(21693, 21744)).IsFalse();
    }

    [Test]
    public async Task AdvancesPhase_UnsetHitSkill_IsFalse()
    {
        await Assert.That(DoodadFuncSkillHit.AdvancesPhase(0, 21693)).IsFalse();
    }

    [Test]
    public async Task Use_MatchingSkill_SetsToNextPhaseWithoutRecast()
    {
        var owner = new Doodad();
        var hit = new DoodadFuncSkillHit { SkillId = 21693 };
        hit.Use(null, owner, 21693);
        await Assert.That(owner.ToNextPhase).IsTrue();
    }

    [Test]
    public async Task Use_Mismatch_DoesNotAdvance()
    {
        var owner = new Doodad();
        var hit = new DoodadFuncSkillHit { SkillId = 21693 };
        hit.Use(null, owner, 21744);
        await Assert.That(owner.ToNextPhase).IsFalse();
    }
}
