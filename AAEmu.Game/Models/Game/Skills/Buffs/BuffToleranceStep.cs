namespace AAEmu.Game.Models.Game.Skills.Buffs
{
    public class BuffToleranceStep
    {
        public uint Id { get; set; }
        public BuffTolerance BuffTolerance { get; set; }
        public uint HitChance { get; set; }
        public uint TimeReduction { get; set; }
    }
}
