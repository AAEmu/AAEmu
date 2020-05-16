using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.Game.Models.Game.Items
{
    public class Holdable
    {
        public uint Id { get; set; }
        public uint KindId { get; set; }
        public int Speed { get; set; }
        public int ExtraDamagePierceFactor { get; set; }
        public int ExtraDamageSlashFactor { get; set; }
        public int ExtraDamageBluntFactor { get; set; }
        public int MaxRange { get; set; }
        public int Angle { get; set; }
        public int EnchantedDps1000 { get; set; }
        public uint SlotTypeId { get; set; }
        public int DamageScale { get; set; }
        public Formula FormulaDps { get; set; }
        public Formula FormulaMDps { get; set; }
        public Formula FormulaArmor { get; set; }
        public Formula FormulaHDps { get; set; }
        public int MinRange { get; set; }
        public int SheathePriority { get; set; }
        public float DurabilityRatio { get; set; }
        public int RenewCategory { get; set; }
        public int ItemProcId { get; set; }
        public int StatMultiplier { get; set; }
    }
}
