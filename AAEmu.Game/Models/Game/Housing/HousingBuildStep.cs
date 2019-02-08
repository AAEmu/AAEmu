namespace AAEmu.Game.Models.Game.Housing
{
    public class HousingBuildStep
    {
        public uint Id { get; set; }
        public uint HousingId { get; set; }
        public short Step { get; set; }
        public uint ModelId { get; set; }
        public uint SkillId { get; set; }
        public int NumActions { get; set; }
    }
}
