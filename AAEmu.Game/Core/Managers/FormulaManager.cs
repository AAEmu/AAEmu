using System;
using System.Collections.Generic;
using System.Globalization;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Utils.DB;
using Jace;
using Jace.Execution;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class FormulaManager : Singleton<FormulaManager>
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

    public void Load()
    {
        if (_loaded)
            return;
        // TODO Funcs: min, max, clamp, if_zero, if_positive, if_negative, floor, log, sqrt
        // Functions can be used inside the mathemical formulas.
        // Jace.NET currently offers four functions accepting one argument (sin, cos, loge and log10) and one function accepting two arguments (logn).
        CalculationEngine = new(new JaceOptions
        {
            CacheEnabled = true,
            OptimizerEnabled = true,
            CaseSensitive = true,
            ExecutionMode = ExecutionMode.Compiled,
            CultureInfo = CultureInfo.InvariantCulture,
        });
        CalculationEngine.AddFunction("clamp", (a, b, c) => a < b ? b : (a > c ? c : a));
        CalculationEngine.AddFunction("if_negative", (a, b, c) => a < 0 ? b : c);
        CalculationEngine.AddFunction("if_positive", (a, b, c) => a > 0 ? b : c);
        CalculationEngine.AddFunction("if_zero", (a, b, c) => a == 0 ? b : c);

        _unitFormulas = new Dictionary<FormulaOwnerType, Dictionary<UnitFormulaKind, UnitFormula>>();
        foreach (var owner in Enum.GetValues(typeof(FormulaOwnerType)))
            _unitFormulas.Add((FormulaOwnerType)owner, new Dictionary<UnitFormulaKind, UnitFormula>());
        _wearableFormulas = new Dictionary<WearableFormulaType, WearableFormula>();
        _unitVariables =
            new Dictionary<uint, Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>>();
        _formulas = new Dictionary<uint, Formula>();

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
                            FormulaId = reader.GetUInt32("unit_formula_id"),
                            Type = (UnitFormulaVariableType)reader.GetByte("variable_kind_id"),
                            Key = reader.GetUInt32("key"),
                            Value = reader.GetFloat("value")
                        };
                        if (!_unitVariables.ContainsKey(variable.FormulaId))
                            _unitVariables.Add(variable.FormulaId,
                                new Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>());
                        if (!_unitVariables[variable.FormulaId].ContainsKey(variable.Type))
                            _unitVariables[variable.FormulaId].Add(variable.Type,
                                new Dictionary<uint, UnitFormulaVariable>());
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
                            //formula.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
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
