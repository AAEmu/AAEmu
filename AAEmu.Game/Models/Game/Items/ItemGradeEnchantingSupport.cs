namespace AAEmu.Game.Models.Game.Items
{
    public class ItemGradeEnchantingSupport
    {
        public uint Id { get; set; }
        public uint ItemId { get; set; }
        public int RequireGradeMax { get; set; }
        public int RequireGradeMin { get; set; }
        public int AddSuccessRatio { get; set; }
        public int AddSuccessMul { get; set; }
        public int AddGreatSuccessRatio { get; set; }
        public int AddGreatSuccessMul { get; set; }
        public int AddBreakRatio { get; set; }
        public int AddBreakMul { get; set; }
        public int AddDowngradeRatio { get; set; }
        public int AddDowngradeMul { get; set; }
        
        public int AddGreatSuccessGrade { get; set; }
    }
}
