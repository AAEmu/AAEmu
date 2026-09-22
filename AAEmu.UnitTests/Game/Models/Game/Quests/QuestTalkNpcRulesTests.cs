using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestTalkNpcRulesTests
{
    [Test]
    public async Task AcceptAndReport_AreTalkNpcs()
    {
        var dest = new HashSet<uint>();
        QuestTalkNpcRules.AddTalkNpcs(TemplateWith(
            start => start.ActTemplates.Add(new QuestActConAcceptNpc(start) { NpcId = 7816 }),
            ready => ready.ActTemplates.Add(new QuestActConReportNpc(ready) { NpcId = 7816 })), dest);

        await Assert.That(QuestTalkNpcRules.IsTalkNpc(dest, 7816)).IsTrue();
        await Assert.That(QuestTalkNpcRules.IsTalkNpc(dest, 1)).IsFalse();
    }

    [Test]
    public async Task HuntNpc_IsNotTalkNpc()
    {
        var dest = new HashSet<uint>();
        var template = new QuestTemplate { Id = 1 };
        var progress = new QuestComponentTemplate(template) { Id = 2, KindId = QuestComponentKind.Progress };
        progress.ActTemplates.Add(new QuestActObjMonsterHunt(progress) { NpcId = 4175 });
        template.Components[progress.Id] = progress;
        QuestTalkNpcRules.AddTalkNpcs(template, dest);

        await Assert.That(QuestTalkNpcRules.IsTalkNpc(dest, 4175)).IsFalse();
    }

    [Test]
    public async Task TalkObjective_IsTalkNpc()
    {
        var dest = new HashSet<uint>();
        var template = new QuestTemplate { Id = 1 };
        var progress = new QuestComponentTemplate(template) { Id = 2, KindId = QuestComponentKind.Progress };
        progress.ActTemplates.Add(new QuestActObjTalk(progress) { NpcId = 4220 });
        template.Components[progress.Id] = progress;
        QuestTalkNpcRules.AddTalkNpcs(template, dest);

        await Assert.That(QuestTalkNpcRules.IsTalkNpc(dest, 4220)).IsTrue();
    }

    // quest 7823: Start quest_act_con_accept_npc_groups 1 (group 698, 78 npcs, first 758 and 879) and
    // Ready quest_act_con_report_npc_groups 1 (group 702, 9 npcs, first 15600).
    [Test]
    public async Task AcceptAndReportGroups_AddTheirMembers()
    {
        var dest = new HashSet<uint>();
        var groups = new Dictionary<uint, IReadOnlyList<uint>>
        {
            [698] = [758, 879],
            [702] = [15600]
        };
        QuestTalkNpcRules.AddTalkNpcGroups(TemplateWith(
            start => start.ActTemplates.Add(new QuestActConAcceptNpcGroup(start) { QuestMonsterGroupId = 698 }),
            ready => ready.ActTemplates.Add(new QuestActConReportNpcGroup(ready) { QuestMonsterGroupId = 702 })),
            dest,
            groupId => groups.TryGetValue(groupId, out var npcs) ? npcs : []);

        await Assert.That(QuestTalkNpcRules.IsTalkNpc(dest, 758)).IsTrue();
        await Assert.That(QuestTalkNpcRules.IsTalkNpc(dest, 879)).IsTrue();
        await Assert.That(QuestTalkNpcRules.IsTalkNpc(dest, 15600)).IsTrue();
        await Assert.That(QuestTalkNpcRules.IsTalkNpc(dest, 4220)).IsFalse();
    }

    // quest_act_con_report_npc_groups 137 (dummy quest 9146) names group 895, which has no members.
    [Test]
    public async Task EmptyGroup_AddsNothing()
    {
        var dest = new HashSet<uint>();
        QuestTalkNpcRules.AddTalkNpcGroups(TemplateWith(
            start => { },
            ready => ready.ActTemplates.Add(new QuestActConReportNpcGroup(ready) { QuestMonsterGroupId = 895 })),
            dest,
            _ => []);

        await Assert.That(dest.Count).IsEqualTo(0);
    }

    private static QuestTemplate TemplateWith(
        Action<QuestComponentTemplate> startFill,
        Action<QuestComponentTemplate> readyFill)
    {
        var template = new QuestTemplate { Id = 2386 };
        var start = new QuestComponentTemplate(template) { Id = 10256, KindId = QuestComponentKind.Start };
        startFill(start);
        var ready = new QuestComponentTemplate(template) { Id = 10255, KindId = QuestComponentKind.Ready };
        readyFill(ready);
        template.Components[start.Id] = start;
        template.Components[ready.Id] = ready;
        return template;
    }
}
