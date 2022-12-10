namespace AAEmu.Game.Models.Game.AI.v2.Params
{
    public class AiSkill
    {
        public uint SkillType { get; set; }
        public bool Strafe { get; set; }
        public float Delay { get; set; }
        
        public uint HealthRangeMin { get; set; }
        public uint HealthRangeMax { get; set; }
    }
}
