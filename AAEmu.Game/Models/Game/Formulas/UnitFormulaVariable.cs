namespace AAEmu.Game.Models.Game.Formulas
{
    public enum UnitFormulaVariableType : byte
    {
        Unknown = 1,
        NpcTemplate = 2,
        NpcKind = 3,
        NpcGrade = 4,
        MateKind = 5
    }

    public class UnitFormulaVariable
    {
        public uint FormulaId { get; set; }
        public UnitFormulaVariableType Type { get; set; }
        public uint Key { get; set; }
        public float Value { get; set; }
    }
}