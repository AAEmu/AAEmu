using System.Collections;
using System.Collections.Generic;

using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests
{
    public class QuestContextText
    {
        public uint QuestContextId { get; set; }
        public QuestContextTextKind QuestContextTextKindId { get; set; }
        public List<QuestContextTextKind> QuestContexts { get; set; }

        public QuestContextText()
        {
            QuestContexts = new List<QuestContextTextKind>();
        }
    }
}
