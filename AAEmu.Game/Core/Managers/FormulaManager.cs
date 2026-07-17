using System.Globalization;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Utils.DB;
using Jace;
using Jace.Execution;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Менеджер формул, загружающий данные из таблиц <c>unit_formulas</c>,
/// <c>unit_formula_variables</c>, <c>wearable_formulas</c> и <c>formulas</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class FormulaManager : Singleton<FormulaManager>, IFormulaManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static bool _loaded = false;

    private Dictionary<FormulaOwnerType, Dictionary<UnitFormulaKind, UnitFormula>> _unitFormulas;
    private Dictionary<WearableFormulaType, WearableFormula> _wearableFormulas;
    private Dictionary<uint, Formula> _formulas;

    private Dictionary<uint, Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>>
        _unitVariables;

    public CalculationEngine CalculationEngine { get; private set; }

    public UnitFormula GetUnitFormula(FormulaOwnerType owner, UnitFormulaKind kind)
    {
        if (_unitFormulas.TryGetValue(owner, out var value)
            && value.TryGetValue(kind, out var kindFound))
            return kindFound;

        return null;
    }

    public float GetUnitVariable(uint formulaId, UnitFormulaVariableType type, uint key)
    {
        if (_unitVariables.TryGetValue(formulaId, out var unitFormulas)
            && unitFormulas.TryGetValue(type, out var formulaVariables)
            && formulaVariables.TryGetValue(key, out var formulaVariable))
            return formulaVariable.Value;

        return 0f;
    }

    public WearableFormula GetWearableFormula(WearableFormulaType type)
    {
        return _wearableFormulas.TryGetValue(type, out var value) ? value : null;
    }

    public Formula GetFormula(uint id)
    {
        return _formulas.TryGetValue(id, out var value) ? value : null;
    }

    /// <summary>
    /// Загружает формулы из таблиц <c>unit_formulas</c>, <c>unit_formula_variables</c>,
    /// <c>wearable_formulas</c> и <c>formulas</c>.
    /// </summary>
    /// <remarks>
    /// Схемы таблиц (проверены по compact.sqlite3):
    /// <list type="bullet">
    ///   <item><description><c>unit_formulas</c>: id (PK), formula, kind_id, owner_type_id</description></item>
    ///   <item><description><c>unit_formula_variables</c>: id (PK), unit_formula_id, variable_kind_id, key, value</description></item>
    ///   <item><description><c>wearable_formulas</c>: kind_id, formula</description></item>
    ///   <item><description><c>formulas</c>: id (PK), formula</description></item>
    /// </list>
    /// </remarks>
    public void Load()
    {
        if (_loaded)
            return;
        // TODO Funcs: min, max, clamp, if_zero, if_positive, if_negative, floor, log, sqrt
        CalculationEngine = new(new JaceOptions
        {
            CacheEnabled = true,
            OptimizerEnabled = true,
            CaseSensitive = true,
            ExecutionMode = ExecutionMode.Compiled,
            CultureInfo = CultureInfo.InvariantCulture,
        });
        CalculationEngine.AddFunction("clamp", (a, b, c) => a < b ? b : a > c ? c : a);
        CalculationEngine.AddFunction("if_negative", (a, b, c) => a < 0 ? b : c);
        CalculationEngine.AddFunction("if_positive", (a, b, c) => a > 0 ? b : c);
        CalculationEngine.AddFunction("if_zero", (a, b, c) => a == 0 ? b : c);

        _unitFormulas = [];
        foreach (var owner in Enum.GetValues(typeof(FormulaOwnerType)))
            _unitFormulas.Add((FormulaOwnerType)owner, []);
        _wearableFormulas = [];
        _unitVariables =
            [];
        _formulas = [];

        using (var connection = SQLite.CreateConnection())
        {
            Logger.Info("Loading formulas...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from unit_formulas";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var formula = new UnitFormula
                        {
                            Id = reader.GetUInt32("id"),
                            TextFormula = reader.GetString("formula"),
                            Kind = (UnitFormulaKind)reader.GetByte("kind_id"),
                            Owner = (FormulaOwnerType)reader.GetByte("owner_type_id")
                        };
                        if (formula.Prepare())
                            _unitFormulas[formula.Owner].Add(formula.Kind, formula);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from unit_formula_variables";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var variable = new UnitFormulaVariable
                        {
                            Id = reader.GetUInt32("id"),
                            FormulaId = reader.GetUInt32("unit_formula_id"),
                            Type = (UnitFormulaVariableType)reader.GetByte("variable_kind_id"),
                            Key = reader.GetUInt32("key"),
                            Value = reader.GetFloat("value")
                        };
                        if (!_unitVariables.ContainsKey(variable.FormulaId))
                            _unitVariables.Add(variable.FormulaId,
                                []);
                        if (!_unitVariables[variable.FormulaId].ContainsKey(variable.Type))
                            _unitVariables[variable.FormulaId].Add(variable.Type,
                                []);
                        _unitVariables[variable.FormulaId][variable.Type].Add(variable.Key, variable);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from wearable_formulas";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var formula = new WearableFormula
                        {
                            Type = (WearableFormulaType)reader.GetByte("kind_id"),
                            TextFormula = reader.GetString("formula")
                        };
                        if (formula.Prepare())
                            _wearableFormulas.Add(formula.Type, formula);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from formulas";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var formula = new Formula
                        {
                            Id = reader.GetUInt32("id"),
                            TextFormula = reader.GetString("formula")
                        };
                        if (formula.Prepare())
                            _formulas.Add(formula.Id, formula);
                    }
                }
            }

            Logger.Info("Formulas loaded");
        }
        _loaded = true;
    }
}
