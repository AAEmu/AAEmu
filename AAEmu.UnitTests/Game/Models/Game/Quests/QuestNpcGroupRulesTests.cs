using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestNpcGroupRulesTests
{
    // quest_monster_npcs of group 698 (quest_act_con_accept_npc_groups 1, quest 7823): 78 npcs, the
    // first and last of them here.
    private static readonly HashSet<uint> Group698 = [758, 879, 880, 1506, 1528, 16220, 16229];

    // quest_monster_npcs of group 702 (quest_act_con_report_npc_groups 1, quest 7823): 9 npcs.
    private static readonly HashSet<uint> Group702 = [15600, 15602, 15603, 15609, 15610, 16475, 16474, 15611, 15612];

    [Test]
    public async Task Accepts_AnNpcOfTheGroup()
    {
        await Assert.That(QuestNpcGroupRules.Accepts(QuestAcceptorType.Npc, 758, Group698.Contains)).IsTrue();
        await Assert.That(QuestNpcGroupRules.Accepts(QuestAcceptorType.Npc, 16229, Group698.Contains)).IsTrue();
    }

    [Test]
    public async Task Accepts_RefusesAnNpcOutsideTheGroup()
    {
        await Assert.That(QuestNpcGroupRules.Accepts(QuestAcceptorType.Npc, 15600, Group698.Contains)).IsFalse();
        await Assert.That(QuestNpcGroupRules.Accepts(QuestAcceptorType.Npc, 0, Group698.Contains)).IsFalse();
    }

    [Test]
    public async Task Accepts_RefusesOtherAcceptorKinds()
    {
        await Assert.That(QuestNpcGroupRules.Accepts(QuestAcceptorType.Doodad, 758, Group698.Contains)).IsFalse();
        await Assert.That(QuestNpcGroupRules.Accepts(QuestAcceptorType.Unknown, 758, Group698.Contains)).IsFalse();
        await Assert.That(QuestNpcGroupRules.Accepts(QuestAcceptorType.Npc, 758, null)).IsFalse();
    }

    [Test]
    public async Task Matches_ReportNpcOfTheGroup()
    {
        await Assert.That(QuestNpcGroupRules.Matches(15600, Group702.Contains)).IsTrue();
        await Assert.That(QuestNpcGroupRules.Matches(15612, Group702.Contains)).IsTrue();
        await Assert.That(QuestNpcGroupRules.Matches(15601, Group702.Contains)).IsFalse();
        await Assert.That(QuestNpcGroupRules.Matches(0, Group702.Contains)).IsFalse();
    }

    // Rows 137 to 156 (dummy quests 9139 to 9156) name groups 894 to 897 with no members.
    [Test]
    public async Task EmptyGroup_NeverMatches()
    {
        var empty = new HashSet<uint>();
        await Assert.That(QuestNpcGroupRules.Matches(15600, empty.Contains)).IsFalse();
        await Assert.That(QuestNpcGroupRules.Accepts(QuestAcceptorType.Npc, 758, empty.Contains)).IsFalse();
    }
}
