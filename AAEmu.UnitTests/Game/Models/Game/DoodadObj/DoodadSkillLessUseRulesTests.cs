using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class DoodadSkillLessUseRulesTests
{
    [Test]
    public async Task BoardUiOpen_RunsWhenTheClientSendsNoSkill()
    {
        await Assert.That(DoodadSkillLessUseRules.RunsOnSkillLessUse("DoodadFuncCraftOrderBoardUiOpen"))
            .IsTrue();
    }

    [Test]
    public async Task LootFuncs_StillRunOnSkillLessUse()
    {
        await Assert.That(DoodadSkillLessUseRules.RunsOnSkillLessUse("DoodadFuncLootItem")).IsTrue();
        await Assert.That(DoodadSkillLessUseRules.RunsOnSkillLessUse("DoodadFuncLootPack")).IsTrue();
        await Assert.That(DoodadSkillLessUseRules.RunsOnSkillLessUse("DoodadFuncCutdowning")).IsTrue();
    }

    [Test]
    public async Task OtherFuncs_StayOffTheSkillLessPath()
    {
        await Assert.That(DoodadSkillLessUseRules.RunsOnSkillLessUse("DoodadFuncQuest")).IsFalse();
        await Assert.That(DoodadSkillLessUseRules.RunsOnSkillLessUse(null)).IsFalse();
    }
}
