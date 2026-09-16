using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates;

/// <summary>
/// One compiled <c>formula_funcs</c> row (<c>dynamic_unit_modifiers.func_type = 'FormulaFunc'</c>),
/// mirroring <see cref="LinearFuncTemplate"/> for the time-interpolated rows.
///
/// The expression and the variables it reads are resolved once, at load, so reading the modifier
/// performs no parsing and no lookup.
/// </summary>
public class FormulaFuncTemplate
{
    public uint Id { get; init; }
    public Formula Formula { get; init; }

    /// <summary>Variables the expression reads that the server binds, in first-appearance order.</summary>
    public IReadOnlyList<FormulaFuncVariable> Variables { get; init; }

    /// <summary>
    /// Evaluates the row for <paramref name="owner"/>. Returns false when the expression produced no
    /// value (see <see cref="Formula.TryEvaluate"/>), in which case nothing must be applied.
    /// </summary>
    public bool TryEvaluate(Unit owner, out double value)
    {
        value = 0d;
        if (Formula == null || owner == null)
            return false;

        var parameters = FormulaFuncRules.BindVariables(Variables, owner);
        return Formula.TryEvaluate(parameters, out value);
    }
}
