namespace AAEmu.Game.Models.Game.Items
{
    public sealed class ItemConfig
    {
        public float DurabilityDecrementChance { get; set; }
        public float DurabilityRepairCostFactor { get; set; }
        public float DurabilityConst { get; set; }
        public float HoldableDurabilityConst { get; set; }
        public float WearableDurabilityConst { get; set; }
        public int DeathDurabilityLossRatio { get; set; }
        public int ItemStatConst { get; set; }
        public int HoldableStatConst { get; set; }
        public int WearableStatConst { get; set; }
        public int StatValueConst { get; set; }
    }
}