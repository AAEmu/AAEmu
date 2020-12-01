namespace AAEmu.Game.Models.Game.Skills.Buffs
{
    public class BuffTolerance
    {
        public uint Id { get; set; }
        public uint BuffTagId { get; set; }
        // In seconds, not sure how that one works
        public uint StepDuration { get; set; }
        // Immunity if triggered too often
        public uint FinalStepBuffId { get; set; }
        // Reduction in % for sleep etc.. in PVP
        public uint CharacterTimeReduction { get; set; }
    }
}
