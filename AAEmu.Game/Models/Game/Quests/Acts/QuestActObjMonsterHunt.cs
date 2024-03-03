﻿using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjMonsterHunt : QuestActTemplate
{
    public uint NpcId { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActObjMonsterHunt: NpcId {NpcId}, Count {Count}, UseAlias {UseAlias}, QuestActObjAliasId {QuestActObjAliasId}, HighlightDoodadId {HighlightDoodadId}, HighlightDoodadPhase {HighlightDoodadPhase}, quest {ParentQuestTemplate.Id}, objective {objective}");

        Update(quest, questAct);

        return quest.GetQuestObjectiveStatus() >= QuestObjectiveStatus.CanEarlyComplete;
    }
}
