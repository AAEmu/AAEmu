using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Crafts
{
    /*
        Data relating to a craft.
    */
    public class Craft
    {
        public uint Id { get; set; }
        public int CastDelay { get; set; }
        public uint ToolId { get; set; }
        public uint SkillId { get; set; }
        public uint WiId { get; set; }
        public uint MilestoneId { get; set; }
        public uint ReqDoodadId { get; set; }
        public bool NeedBind { get; set; }
        public uint AcId { get; set; }
        public int ActabilityLimit { get; set; }
        public bool ShowUpperCraft { get; set; }
        public int RecommendLevel { get; set; }
        public int VisibleOrder { get; set; }

        public List<CraftProduct> CraftProducts { get; set; }
        public List<CraftMaterial> CraftMaterials { get; set; }
        
        public Craft()
        {
            CraftProducts = new List<CraftProduct>();
            CraftMaterials = new List<CraftMaterial>();
        }
    }
}
