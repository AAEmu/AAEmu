namespace AAEmu.Game.Models.Game.Formulas
{
    public enum WearableFormulaType : byte
    {
        MaxBaseArmor = 0,
        MaxBaseMagicResistance = 1
    }

    public class WearableFormula : Formula
    {
        public WearableFormulaType Type { get; set; }
    }
}