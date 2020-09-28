namespace AAEmu.Game.Models.Game.AI
{
    public class AiEvents
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int NpcId { get; set; }
        public int IgnoreCategoryId { get; set; }
        public float IgnoreTime { get; set; }
        public int SkillId { get; set; }
        public double OrUnitReqs { get; set; }
    }
}
