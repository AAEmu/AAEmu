﻿using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptDoodad(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint DoodadId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActConAcceptDoodad: DoodadId {DoodadId}");

        quest.QuestAcceptorType = QuestAcceptorType.Doodad;
        quest.AcceptorType = DoodadId;

        return true;
    }
}
