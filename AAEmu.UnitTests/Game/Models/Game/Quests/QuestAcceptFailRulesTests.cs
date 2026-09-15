using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;

using NLog;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestAcceptFailRulesTests
{
    [Test]
    public async Task RequirementFail_IsTheSharedUnitRequirementCode()
    {
        await Assert.That(QuestAcceptFailRules.RequirementNotMet)
            .IsEqualTo(QuestStatusFailed.UnitRequirementCheck);
        await Assert.That((byte)QuestAcceptFailRules.RequirementNotMet).IsEqualTo((byte)9);
    }

    [Test]
    public async Task Refusal_IsLoggedAtTheLevelItsOriginDeserves()
    {
        // A character below a starter sphere's level walks through it again and again: that refusal is
        // routine traffic, while one the character is waiting on is worth a warning.
        await Assert.That(QuestAcceptFailRules.RefusalLogLevel(answerClient: true)).IsEqualTo(LogLevel.Warn);
        await Assert.That(QuestAcceptFailRules.RefusalLogLevel(answerClient: false)).IsEqualTo(LogLevel.Trace);
    }

    [Test]
    public async Task BlockedGuildPublicAssignment_IsTheBlockedQuestCode()
    {
        await Assert.That(QuestAcceptFailRules.PublicAssignmentBlocked)
            .IsEqualTo(QuestStatusFailed.BlockedQuest);
        await Assert.That((byte)QuestAcceptFailRules.PublicAssignmentBlocked).IsEqualTo((byte)39);
    }

    [Test]
    public async Task MissingNpcAndDoodad_UseTheirOwnSourceCodes()
    {
        await Assert.That(QuestAcceptFailRules.MissingSource(QuestAcceptorType.Npc))
            .IsEqualTo(QuestStatusFailed.InvalidNpcOrQuest);
        await Assert.That(QuestAcceptFailRules.MissingSource(QuestAcceptorType.Sphere))
            .IsEqualTo(QuestStatusFailed.InvalidNpcOrQuest);
        await Assert.That(QuestAcceptFailRules.MissingSource(QuestAcceptorType.Doodad))
            .IsEqualTo(QuestStatusFailed.InvalidDoodad);
    }
}
