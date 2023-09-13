namespace AAEmu.Game.Models.Game.AI.v2.Params
{
    public class AiEvents
    {
        public uint Id { get; set; }
        public string Name { get; set; } // name
        public uint NpcId { get; set; } // npc_id
        public uint IgnoreCategoryId { get; set; } // ignore_category_id
        public float IgnoreTime { get; set; } // ignore_time
        public uint SkillId { get; set; } // skill_id
        public bool OrUnitReqs { get; set; } // or_unit_reqs
    }
}
