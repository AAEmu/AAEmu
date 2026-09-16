using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Where a <c>formula_funcs</c> variable takes its value from.
/// </summary>
public enum FormulaFuncVariableSource
{
    /// <summary><c>attr_N</c>: unit attribute id N of the owning unit.</summary>
    Attribute,
    /// <summary><c>pc_level</c>: the owning unit's level.</summary>
    PcLevel,
    /// <summary><c>heir_level</c>: the owning unit's heir level.</summary>
    HeirLevel,
    /// <summary><c>gear_score</c>: a character's gear score.</summary>
    GearScore
}

/// <summary>
/// One variable a <c>formula_funcs</c> expression reads, resolved once when the row is compiled so
/// the attribute-read path does no parsing.
/// </summary>
public readonly record struct FormulaFuncVariable(string Name, FormulaFuncVariableSource Source, uint AttributeId);

/// <summary>
/// How a <c>formula_funcs</c> expression binds to the unit that owns the buff
/// (<c>dynamic_unit_modifiers.func_type = 'FormulaFunc'</c>).
/// </summary>
/// <remarks>
/// The identifier inventory of the 233 shipped rows (10.0.2.13) is <c>attr_&lt;N&gt;</c> (217 distinct
/// ids, each used once except a handful), the three named variables below, and the calculation
/// engine's own functions. <c>attr_&lt;N&gt;</c> is the unit attribute id, not a position: buff 28984
/// "(attr_test_func) 10 move_speed_mul" carries dynamic row 737 (unit_attribute_id 10, func_id 17),
/// formula_funcs 17 is "attr_10" and enum_unit_attribute names 10 "move_speed_mul"; buff 28975
/// "(attr_test_func) 0 str" carries attribute 0 with "attr_0". In 218 of the 233 rows attr_N is also
/// the row's own unit_attribute_id, 5 rows (945-949) read a different attribute and 10 read no
/// attribute at all; see <see cref="InFormulaEvaluation"/>.
/// </remarks>
public static class FormulaFuncRules
{
    /// <summary>Character level; the codebase also binds it as "pc_level" (Character.cs, AddExp).</summary>
    public const string PcLevelVariable = "pc_level";

    /// <summary>
    /// Heir level; NPCs and other non-characters carry 0 (Npc.cs binds "heir_level" as 0 for its own
    /// formulas).
    /// </summary>
    public const string HeirLevelVariable = "heir_level";

    /// <summary>Gear score; only characters have one (Character.GearScore).</summary>
    public const string GearScoreVariable = "gear_score";

    private const string AttributeVariablePrefix = "attr_";

    private static readonly Regex IdentifierPattern = new(@"[A-Za-z_][A-Za-z_0-9]*", RegexOptions.Compiled);

    /// <summary>Cached accessor per (unit type, attribute id); see <see cref="CreateAttributeReader"/>.</summary>
    private static readonly ConcurrentDictionary<(Type, uint), Func<Unit, double>> AttributeReaders = new();

    [ThreadStatic] private static int _evaluationDepth;

    /// <summary>
    /// True while a formula_funcs expression is being evaluated on this thread.
    /// </summary>
    /// <remarks>
    /// One counter for the thread, not one per attribute: every FormulaFunc bonus reached while any
    /// other one is being evaluated contributes nothing, not only the row that re-entered its own
    /// attribute. 218 of the 233 shipped rows read the attribute they modify, so that read arrives
    /// here again through <see cref="Unit.CalculateWithBonuses"/> while the attribute is being
    /// computed; for those rows the two are the same thing — the nested read sees the unit's value
    /// with this modifier left out, which is what stops the recursion — and 10 more rows read no
    /// attribute at all (pc_level, heir_level and gear_score only).
    ///
    /// The remaining 5 rows read an attribute other than the one they modify, and the difference
    /// shows there. Rows 945-949, all on buff 29238 "잠재능력 발현", modify attributes
    /// 249/250/251/253/254 and read attr_96, attr_98, attr_87 and attr_173 (948 and 949 both read
    /// attr_173). Each of those four attributes carries a FormulaFunc row of its own — 96 row 800,
    /// 98 row 802, 87 row 791, 173 row 868 — so holding 29238 together with one of them makes the
    /// outer row read that attribute without the inner row's contribution either, not "the value it
    /// had before this modifier". Those four rows sit on buffs 29047 "(attr_test_func) 96
    /// mainhand_dps", 29049 "(attr_test_func) 98 ranged_dps", 29038 "(attr_test_func) 87 spell_dps"
    /// and 29115 "(attr_test_func) 173 heal_dps", and buff 29238 is itself granted only by the
    /// buff_triggers on 29239 and 28916, which no enabled skill_effect grants either. Nothing a
    /// player holds reaches the combination today.
    /// </remarks>
    public static bool InFormulaEvaluation => _evaluationDepth > 0;

    /// <summary>Marks the current thread as evaluating a formula_funcs row until the scope is disposed.</summary>
    public static EvaluationScope BeginFormulaEvaluation()
    {
        _evaluationDepth++;
        return default;
    }

    public readonly struct EvaluationScope : IDisposable
    {
        public void Dispose() => _evaluationDepth--;
    }

    /// <summary>
    /// Variables of <paramref name="expression"/> the server binds, in first-appearance order.
    /// Identifiers that are not one of them are left to the calculation engine: either one of its
    /// functions or a row the load-time probe rejects.
    /// </summary>
    public static List<FormulaFuncVariable> ParseVariables(string expression)
    {
        var variables = new List<FormulaFuncVariable>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in IdentifierPattern.Matches(expression ?? string.Empty))
        {
            var name = match.Value;
            if (!seen.Add(name))
                continue;

            if (TryParseAttributeId(name, out var attributeId))
                variables.Add(new FormulaFuncVariable(name, FormulaFuncVariableSource.Attribute, attributeId));
            else if (name == PcLevelVariable)
                variables.Add(new FormulaFuncVariable(name, FormulaFuncVariableSource.PcLevel, 0));
            else if (name == HeirLevelVariable)
                variables.Add(new FormulaFuncVariable(name, FormulaFuncVariableSource.HeirLevel, 0));
            else if (name == GearScoreVariable)
                variables.Add(new FormulaFuncVariable(name, FormulaFuncVariableSource.GearScore, 0));
        }

        return variables;
    }

    /// <summary>
    /// Builds the parameter dictionary for <paramref name="variables"/> from <paramref name="owner"/>.
    /// </summary>
    public static Dictionary<string, double> BindVariables(IReadOnlyList<FormulaFuncVariable> variables, Unit owner)
    {
        var parameters = new Dictionary<string, double>(variables?.Count ?? 0, StringComparer.Ordinal);
        if (variables == null)
            return parameters;

        foreach (var variable in variables)
        {
            parameters[variable.Name] = variable.Source switch
            {
                FormulaFuncVariableSource.Attribute => ReadAttribute(owner, variable.AttributeId),
                FormulaFuncVariableSource.PcLevel => owner?.Level ?? 0,
                FormulaFuncVariableSource.HeirLevel => owner?.HeirLevel ?? 0,
                FormulaFuncVariableSource.GearScore => (owner as Character)?.GearScore ?? 0,
                _ => 0d
            };
        }

        return parameters;
    }

    /// <summary>
    /// Current value of unit attribute <paramref name="attributeId"/> for <paramref name="unit"/>,
    /// read through the property the class marks with <see cref="UnitAttributeAttribute"/> — the same
    /// lookup <see cref="Unit.GetAttribute(UnitAttribute)"/> performs, but returning the number.
    /// </summary>
    /// <remarks>
    /// The property's own scale is what a row reads, so the ratio-style attributes come back divided
    /// by their engine baseline (Unit.MoveSpeedMul is the 1000-based accumulator over 1000, attribute
    /// 10, which is why the shipped row on buff 28984 reads 1 rather than 1000). Every other attribute
    /// in the catalogue is carried 1:1.
    /// </remarks>
    public static double ReadAttribute(Unit unit, uint attributeId)
    {
        if (unit == null)
            return 0d;

        return AttributeReaders.GetOrAdd((unit.GetType(), attributeId), CreateAttributeReader)(unit);
    }

    private static Func<Unit, double> CreateAttributeReader((Type UnitType, uint AttributeId) key)
    {
        var property = FindAttributeProperty(key.UnitType, (UnitAttribute)key.AttributeId);
        if (property != null)
            return unit => Convert.ToDouble(property.GetValue(unit), CultureInfo.InvariantCulture);

        // No property on this unit type carries the attribute id. The 10.0.2.13 catalogue is wider
        // than the classes (249 melee_dps_inc_anti_npc, 254 heal_dps_inc_only_heal, ...), so fall
        // back to the accumulator every modifier row for that attribute is added to.
        return unit => unit.CalculateWithBonuses(0d, (UnitAttribute)key.AttributeId);
    }

    private static PropertyInfo FindAttributeProperty(Type unitType, UnitAttribute attribute)
    {
        foreach (var property in unitType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (UnitAttributeAttribute marker in
                     property.GetCustomAttributes(typeof(UnitAttributeAttribute), true))
            {
                if (marker.Attributes.Contains(attribute))
                    return property;
            }
        }

        return null;
    }

    private static bool TryParseAttributeId(string identifier, out uint attributeId)
    {
        attributeId = 0;
        return identifier.StartsWith(AttributeVariablePrefix, StringComparison.Ordinal)
               && uint.TryParse(identifier.AsSpan(AttributeVariablePrefix.Length), NumberStyles.None,
                   CultureInfo.InvariantCulture, out attributeId);
    }
}
