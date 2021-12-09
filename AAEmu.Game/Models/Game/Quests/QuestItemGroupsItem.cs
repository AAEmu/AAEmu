using System.Collections;
using System.Collections.Generic;

using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests
{
    public class QuestItemGroupsItem
    {
        public uint ItemId { get; set; }
        public QuestItemGroups GroupId { get; set; }
        public List<uint> Items { get; set; }

        public QuestItemGroupsItem()
        {
            Items = new List<uint>();
        }
    }
}
