using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncEvidenceItemLootTests
{
    [Test]
    public async Task Recognizes_MatchingLootSkill_IsTrue()
    {
        // Every doodad_func_evidence_item_loots row ships the same pickup skill, 증거물 줍기 (11672).
        await Assert.That(DoodadFuncEvidenceItemLoot.Recognizes(11672, 11672)).IsTrue();
    }

    [Test]
    public async Task Recognizes_EraseTracesSkill_IsFalse()
    {
        // 15099 범죄 흔적 지우기 is the sibling gate in the same phase group, not the pickup skill.
        await Assert.That(DoodadFuncEvidenceItemLoot.Recognizes(11672, 15099)).IsFalse();
    }

    [Test]
    public async Task Recognizes_UnsetRowSkill_IsFalse()
    {
        await Assert.That(DoodadFuncEvidenceItemLoot.Recognizes(0, 11672)).IsFalse();
    }

    [Test]
    public async Task Use_MatchingSkill_AdvancesTheEvidence()
    {
        var owner = new Doodad();
        var loot = new DoodadFuncEvidenceItemLoot { SkillId = 11672 };
        loot.Use(null, owner, 11672);
        await Assert.That(owner.ToNextPhase).IsTrue();
    }

    [Test]
    public async Task Use_UnrelatedSkill_LeavesTheEvidenceStanding()
    {
        var owner = new Doodad();
        var loot = new DoodadFuncEvidenceItemLoot { SkillId = 11672 };
        loot.Use(null, owner, 15099);
        await Assert.That(owner.ToNextPhase).IsFalse();
    }
}
