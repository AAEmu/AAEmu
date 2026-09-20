using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class LevelPackDoodadRulesTests
{
    [Test]
    public async Task ShouldAuthor_ClientNpcTypeOrTalk_NotTowerOrIgnore()
    {
        await Assert.That(LevelPackDoodadRules.ShouldAuthorPermanent(
            clientDoodad: true, npcTypeModel: true, talkOrQuestFunc: true,
            towerAlmighty: false, ignoredPermanent: false, scheduledEvent: false)).IsTrue();
        await Assert.That(LevelPackDoodadRules.ShouldAuthorPermanent(
            clientDoodad: true, npcTypeModel: false, talkOrQuestFunc: false,
            towerAlmighty: false, ignoredPermanent: false, scheduledEvent: false)).IsTrue();
        await Assert.That(LevelPackDoodadRules.ShouldAuthorPermanent(
            clientDoodad: false, npcTypeModel: true, talkOrQuestFunc: false,
            towerAlmighty: false, ignoredPermanent: false, scheduledEvent: false)).IsTrue();
        await Assert.That(LevelPackDoodadRules.ShouldAuthorPermanent(
            clientDoodad: false, npcTypeModel: false, talkOrQuestFunc: true,
            towerAlmighty: false, ignoredPermanent: false, scheduledEvent: false)).IsTrue();
        await Assert.That(LevelPackDoodadRules.ShouldAuthorPermanent(
            clientDoodad: true, npcTypeModel: true, talkOrQuestFunc: true,
            towerAlmighty: true, ignoredPermanent: false, scheduledEvent: false)).IsFalse();
        await Assert.That(LevelPackDoodadRules.ShouldAuthorPermanent(
            clientDoodad: true, npcTypeModel: true, talkOrQuestFunc: true,
            towerAlmighty: false, ignoredPermanent: true, scheduledEvent: false)).IsFalse();
        await Assert.That(LevelPackDoodadRules.ShouldAuthorPermanent(
            clientDoodad: true, npcTypeModel: true, talkOrQuestFunc: true,
            towerAlmighty: false, ignoredPermanent: false, scheduledEvent: true)).IsFalse();
        await Assert.That(LevelPackDoodadRules.ShouldAuthorPermanent(
            clientDoodad: false, npcTypeModel: false, talkOrQuestFunc: false,
            towerAlmighty: false, ignoredPermanent: false, scheduledEvent: false)).IsFalse();
    }

    [Test]
    public async Task Plan_3901Pad_TakesEhnoirFeosExtras_SkipsIgnored()
    {
        var wanted = new HashSet<uint> { 14226, 14227, 14228 };
        var catalog = new[]
        {
            new QuestTalkDoodadRules.Placement(14226, 11546.626f, 11829.929f, 110.011f, 0f),
            new QuestTalkDoodadRules.Placement(14227, 11544.631f, 11827.183f, 110.172f, 0f),
            new QuestTalkDoodadRules.Placement(14228, 11538.821f, 11828.881f, 110.356f, 0f),
            new QuestTalkDoodadRules.Placement(14228, 11547.599f, 11823.275f, 110.101f, 0f)
        };

        var planned = QuestTalkDoodadRules.Plan(wanted, catalog, []);
        await Assert.That(planned.Count(p => p.TemplateId == 14226)).IsEqualTo(1);
        await Assert.That(planned.Count(p => p.TemplateId == 14227)).IsEqualTo(1);
        await Assert.That(planned.Count(p => p.TemplateId == 14228)).IsEqualTo(2);
    }
}
