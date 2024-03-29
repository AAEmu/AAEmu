﻿using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActSupplyAppellation : QuestActTemplate
{
    public uint AppellationId { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActSupplyAppellation");

        character.Appellations.Add(AppellationId);
        return true;
    }
}
