using System.Collections.Generic;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests
{
    public class QuestComponent
    {
        public uint Id { get; set; }
        public QuestComponentKind KindId { get; set; }
        public List<QuestActTemplate> ActTemplates { get; set; } = new List<QuestActTemplate>();
        public uint NextComponent { get; set; }
        public QuestNpcAiName NpcAiId { get; set; }
        public uint NpcId { get; set; }
        public uint SkillId { get; set; }
        public bool SkillSelf { get; set; }
        public string AiPathName { get; set; }
        public PathType AiPathTypeId { get; set; }
        public uint NpcSpawnerId { get; set; }
        public bool PlayCinemaBeforeBubble { get; set; }
        public uint AiCommandSetId { get; set; }
        public bool OrUnitReqs { get; set; }
        public uint CinemaId { get; set; }
        public uint BuffId { get; set; }
    }
}
