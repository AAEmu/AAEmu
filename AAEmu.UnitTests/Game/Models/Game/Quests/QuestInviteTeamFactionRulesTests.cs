using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestInviteTeamFactionRulesTests
{
    // quest_act_obj_invite_team_factions 1..4: quests 6801..6804, invite type 1 (expedition), buff 13921.
    [Test]
    public async Task Counts_NeedsTheInviteTypeAndTheBuff()
    {
        await Assert.That(QuestInviteTeamFactionRules.Counts(
            QuestActObjInviteType.Expedition, QuestActObjInviteType.Expedition, 13921, true)).IsTrue();
        await Assert.That(QuestInviteTeamFactionRules.Counts(
            QuestActObjInviteType.Expedition, QuestActObjInviteType.Expedition, 13921, false)).IsFalse();
        await Assert.That(QuestInviteTeamFactionRules.Counts(
            QuestActObjInviteType.Expedition, (QuestActObjInviteType)2, 13921, true)).IsFalse();
        await Assert.That(QuestInviteTeamFactionRules.Counts(0, 0, 13921, true)).IsFalse();
    }

    [Test]
    public async Task Counts_NoBuffOnTheRowAcceptsAnyInvitee()
    {
        await Assert.That(QuestInviteTeamFactionRules.Counts(
            QuestActObjInviteType.Expedition, QuestActObjInviteType.Expedition, 0, false)).IsTrue();
    }

    // Row 4 (quest 6804) needs four invites.
    [Test]
    public async Task ObjectiveMet_FollowsTheRowCount()
    {
        await Assert.That(QuestProgressActRules.ObjectiveMet(3, 4)).IsFalse();
        await Assert.That(QuestProgressActRules.ObjectiveMet(4, 4)).IsTrue();
    }
}
