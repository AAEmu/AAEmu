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
    public async Task ExpeditionPortalUiOpen_RunsWhenTheClientSendsNoSkill()
    {
        // Guild worldgate: activate skill advances the phase, then F-use arrives with skill 0.
        await Assert.That(DoodadSkillLessUseRules.RunsOnSkillLessUse("DoodadFuncExpeditionPortalUiOpen"))
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

    [Test]
    public async Task KickoffUse_OnlyWhenFuncCarriesASkill()
    {
        await Assert.That(DoodadSkillLessUseRules.KickoffUseOnSkillLess("DoodadFuncUse", 48612)).IsTrue();
        await Assert.That(DoodadSkillLessUseRules.KickoffUseOnSkillLess("DoodadFuncUse", 0)).IsFalse();
        await Assert.That(DoodadSkillLessUseRules.KickoffUseOnSkillLess("DoodadFuncQuest", 48612)).IsFalse();
    }

    [Test]
    public async Task EffectlessSkill_DrivesDoodadUseWhenFuncSkillMatches()
    {
        // Worldgate list-open skill has zero skill_effects; func_skill_id must match.
        await Assert.That(DoodadSkillLessUseRules.ShouldDriveEffectlessSkillUse(48718, 0, 48718)).IsTrue();
        await Assert.That(DoodadSkillLessUseRules.ShouldDriveEffectlessSkillUse(48718, 1, 48718)).IsFalse();
        await Assert.That(DoodadSkillLessUseRules.ShouldDriveEffectlessSkillUse(48718, 0, 48612)).IsFalse();
        await Assert.That(DoodadSkillLessUseRules.ShouldDriveEffectlessSkillUse(0, 0, 0)).IsFalse();
    }
}
