﻿using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptLevelUp : QuestActTemplate
{
    public byte Level { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActConAcceptLevelUp");

        return character.Level >= Level;
    }
}
