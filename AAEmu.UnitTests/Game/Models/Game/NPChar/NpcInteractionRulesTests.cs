using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

public class NpcInteractionRulesTests
{
    [Test]
    public async Task QuestTalkNpc_UsesNpcTalk()
    {
        await Assert.That(NpcInteractionRules.PrimarySkill(new NpcTemplate(), questTalk: true))
            .IsEqualTo(SkillsEnum.NpcTalk);
        await Assert.That(NpcInteractionRules.PrimarySkill(null)).IsEqualTo(0u);
        await Assert.That(NpcInteractionRules.PrimarySkill(new NpcTemplate())).IsEqualTo(0u);
    }

    [Test]
    public async Task Merchant_UsesStore()
    {
        await Assert.That(NpcInteractionRules.PrimarySkill(new NpcTemplate { Merchant = true }))
            .IsEqualTo(SkillsEnum.UseStore);
    }

    [Test]
    public async Task Banker_UsesWarehouse()
    {
        await Assert.That(NpcInteractionRules.PrimarySkill(new NpcTemplate { Banker = true }))
            .IsEqualTo(SkillsEnum.UseWarehouse);
    }

    [Test]
    public async Task MerchantQuestTalk_UsesNpcTalk()
    {
        await Assert.That(NpcInteractionRules.PrimarySkill(new NpcTemplate { Merchant = true }, questTalk: true))
            .IsEqualTo(SkillsEnum.NpcTalk);
    }

    [Test]
    public async Task ComposeSkills_PlainNpcWithoutASetOffersNothing()
    {
        var skills = NpcInteractionRules.ComposeSkills(new NpcTemplate(), false, []);

        await Assert.That(skills).IsEmpty();
    }

    [Test]
    public async Task ComposeSkills_BankerKeepsItsServiceSkill()
    {
        var skills = NpcInteractionRules.ComposeSkills(new NpcTemplate { Banker = true }, false, []);

        await Assert.That(skills).IsEquivalentTo(new uint[] { SkillsEnum.UseWarehouse });
    }

    [Test]
    public async Task ComposeSkills_AddsTheAuthoredSetInRowOrder()
    {
        var skills = NpcInteractionRules.ComposeSkills(new NpcTemplate(), false, [11, 12, 13]);

        await Assert.That(skills).IsEquivalentTo(new uint[] { 11, 12, 13 });
    }

    [Test]
    public async Task ComposeSkills_ServiceComesFirstAndIsNotRepeated()
    {
        var skills = NpcInteractionRules.ComposeSkills(
            new NpcTemplate { Merchant = true }, false, [SkillsEnum.UseStore, 11]);

        await Assert.That(skills).IsEquivalentTo(new uint[] { SkillsEnum.UseStore, 11 });
    }

    [Test]
    public async Task ComposeSkills_QuestTalkTakesTheServiceSlot()
    {
        var skills = NpcInteractionRules.ComposeSkills(new NpcTemplate { Merchant = true }, true, [11]);

        await Assert.That(skills).IsEquivalentTo(new uint[] { SkillsEnum.NpcTalk, 11 });
    }

    [Test]
    public async Task ComposeSkills_SkipsZeroEntriesAndSurvivesANullTemplateOrSet()
    {
        await Assert.That(NpcInteractionRules.ComposeSkills(new NpcTemplate(), false, [0, 11]))
            .IsEquivalentTo(new uint[] { 11 });
        await Assert.That(NpcInteractionRules.ComposeSkills(null, false, [11]))
            .IsEquivalentTo(new uint[] { 11 });
        await Assert.That(NpcInteractionRules.ComposeSkills(new NpcTemplate(), false, null)).IsEmpty();
    }
}
