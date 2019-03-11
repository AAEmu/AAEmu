namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class GradeTemplate
    {
        public int Grade { get; set; }
        public int GradeOrder { get; set; }
        public float HoldableDps { get; set; }
        public float HoldableArmor { get; set; }
        public float HoldableMagicDps { get; set; }
        public float HoldableHealDps { get; set; }
        public float WearableArmor { get; set; }
        public float WearableMagicResistance { get; set; }
        public float Durability { get; set; }
        public int UpgradeRatio { get; set; }
        public int StatMultiplier { get; set; }
        public int RefundMultiplier { get; set; }
        public int EnchantSuccessRatio { get; set; }
        public int EnchantGreatSuccessRatio { get; set; }
        public int EnchantBreakRatio { get; set; }
        public int EnchantDowngradeRatio { get; set; }
        public int EnchantDowngradeMin { get; set; }
        public int EnchantDowngradeMax { get; set; }
        public int EnchantCost { get; set; }
        public int CurrencyId { get; set; }
    }
}
