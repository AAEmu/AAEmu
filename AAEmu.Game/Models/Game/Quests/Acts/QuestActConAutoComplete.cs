﻿using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAutoComplete : QuestActTemplate
{
    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Warn("QuestActConAutoComplete");

        return character.Quests.IsQuestComplete(quest.TemplateId);
    }
}
