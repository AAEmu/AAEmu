using AAEmu.Game.Models.Game.Formulas;

using Jace;

namespace AAEmu.Game.Core.Managers;

public interface IFormulaManager
{
    CalculationEngine CalculationEngine { get; }
    void Load();
    UnitFormula GetUnitFormula(FormulaOwnerType owner, UnitFormulaKind kind);
    float GetUnitVariable(uint formulaId, UnitFormulaVariableType type, uint key);
    WearableFormula GetWearableFormula(WearableFormulaType type);
    Formula GetFormula(uint id);
}
