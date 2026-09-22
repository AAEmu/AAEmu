using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// NPCs the client talks to for accept, report, or talk objectives.
/// Hunt / kill starters stay off this list so combat NPCs keep their role skill.
/// </summary>
public static class QuestTalkNpcRules
{
    public static void AddTalkNpcs(IQuestTemplate template, ISet<uint> dest)
    {
        if (template?.Components == null || dest == null)
            return;

        foreach (var component in template.Components.Values)
        {
            if (component?.ActTemplates == null)
                continue;

            foreach (var act in component.ActTemplates)
            {
                switch (act)
                {
                    case QuestActConAcceptNpc accept when accept.NpcId != 0:
                        dest.Add(accept.NpcId);
                        break;
                    case QuestActConAcceptNpcEmotion emotion when emotion.NpcId != 0:
                        dest.Add(emotion.NpcId);
                        break;
                    case QuestActConReportNpc report when report.NpcId != 0:
                        dest.Add(report.NpcId);
                        break;
                    case QuestActObjTalk talk when talk.NpcId != 0:
                        dest.Add(talk.NpcId);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Members of the quest_monster_groups an accept or report group act names, resolved through
    /// the loaded quest_monster_npcs (QuestManager.GetGroupNpcIds).
    /// </summary>
    public static void AddTalkNpcGroups(IQuestTemplate template, ISet<uint> dest, Func<uint, IReadOnlyList<uint>> groupNpcs)
    {
        if (template?.Components == null || dest == null || groupNpcs == null)
            return;

        foreach (var component in template.Components.Values)
        {
            if (component?.ActTemplates == null)
                continue;

            foreach (var act in component.ActTemplates)
            {
                var groupId = act switch
                {
                    QuestActConAcceptNpcGroup accept => accept.QuestMonsterGroupId,
                    QuestActConReportNpcGroup report => report.QuestMonsterGroupId,
                    _ => 0u
                };
                if (groupId == 0)
                    continue;

                foreach (var npcId in groupNpcs(groupId) ?? [])
                {
                    if (npcId != 0)
                        dest.Add(npcId);
                }
            }
        }
    }

    public static bool IsTalkNpc(ISet<uint> talkNpcs, uint npcTemplateId)
    {
        return npcTemplateId != 0 && talkNpcs != null && talkNpcs.Contains(npcTemplateId);
    }
}
