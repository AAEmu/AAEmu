﻿using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActConAcceptDoodad : QuestActTemplate
    {
        public uint DoodadId { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActConAcceptDoodad: DoodadId {0}", DoodadId);
            
            quest.QuestAcceptorType = QuestAcceptorType.Doodad;
            quest.AcceptorType = DoodadId;
            
            return true;
        }
    }
}
