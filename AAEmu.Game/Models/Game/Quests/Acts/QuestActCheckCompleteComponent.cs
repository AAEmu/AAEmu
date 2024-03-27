﻿using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActCheckCompleteComponent(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint CompleteComponent { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActCheckCompleteComponent: Complete Component {CompleteComponent}");
        return true;
    }
}
