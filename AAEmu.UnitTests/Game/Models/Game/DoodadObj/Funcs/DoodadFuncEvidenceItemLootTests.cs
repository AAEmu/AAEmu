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
    public async Task Use_LeavesTheEvidenceOnItsPhaseSoTheReportCanReadIt()
    {
        // The phase that carries this func carries the crime kind and value the report is recorded with.
        // Advancing on the pickup skill would move the evidence off it first, and the report would file
        // an empty crime, so the func waits for the report instead.
        var owner = new Doodad();
        var loot = new DoodadFuncEvidenceItemLoot { SkillId = 11672 };

        loot.Use(null, owner, 11672);

        await Assert.That(owner.ToNextPhase).IsFalse();
    }

    [Test]
    public async Task Use_IsCompletedByTheClientReportNotByTheCast()
    {
        await Assert.That(new DoodadFuncEvidenceItemLoot().CompletesFromClientPacket).IsTrue();
        await Assert.That(Doodad.ShouldApplyPhaseAfterSuccessfulFunc(
            completesFromClientPacket: true, toNextPhase: true)).IsFalse();
    }
}
