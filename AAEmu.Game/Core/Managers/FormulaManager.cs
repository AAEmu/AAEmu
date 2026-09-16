using System.Globalization;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Utils.DB;
using Jace;
using Jace.Execution;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class FormulaManager : Singleton<FormulaManager>, IFormulaManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static bool _loaded = false;

    private Dictionary<FormulaOwnerType, Dictionary<UnitFormulaKind, UnitFormula>> _unitFormulas;
    private Dictionary<WearableFormulaType, WearableFormula> _wearableFormulas;
    private Dictionary<uint, Formula> _formulas;
    private readonly Dictionary<uint, FormulaFuncTemplate> _formulaFuncs = [];
    private readonly HashSet<uint> _rejectedFormulaFuncs = [];

    private Dictionary<uint, Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>>
        _unitVariables;

    private CalculationEngine _calculationEngine;

    /// <summary>
    /// Jace engine shared by every content expression (<c>formulas</c>, <c>unit_formulas</c>,
    /// <c>wearable_formulas</c>, <c>formula_funcs</c>). Built on first use so a process that never
    /// runs <see cref="Load"/> (unit tests) can still compile an expression.
    /// </summary>
    public CalculationEngine CalculationEngine => _calculationEngine ??= CreateCalculationEngine();

    /// <summary>
    /// Ids of <c>formula_funcs</c> rows dropped by <see cref="RegisterFormulaFunc"/>; reported in one
    /// line by <see cref="Load"/>.
    /// </summary>
    public IReadOnlyCollection<uint> RejectedFormulaFuncs => _rejectedFormulaFuncs;

    public UnitFormula GetUnitFormula(FormulaOwnerType owner, UnitFormulaKind kind)
    {
        if (_unitFormulas != null
            && _unitFormulas.TryGetValue(owner, out var value)
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

    /// <summary>
    /// Whether <see cref="Load"/> has built the tables. Callers that run in a process which never loads
    /// content (a unit test) check this before <see cref="GetFormula"/>, which would otherwise read a null
    /// dictionary.
    /// </summary>
    public bool Loaded => _formulas != null;

    public Formula GetFormula(uint id)
    {
        return _formulas.TryGetValue(id, out var value) ? value : null;
    }

    public FormulaFuncTemplate GetFormulaFunc(uint id)
    {
        return _formulaFuncs.TryGetValue(id, out var value) ? value : null;
    }

    /// <summary>
    /// Compiles one <c>formula_funcs</c> row and registers it under its id, or drops it.
    /// </summary>
    /// <remarks>
    /// A row is dropped when the text does not compile, and also when a probe evaluation — every
    /// bound variable at zero — shows the expression reads a variable or calls a function the engine
    /// cannot resolve. Such a row would otherwise throw on every attribute read of the unit carrying
    /// it. Note that the func_id spaces of <c>formula_funcs</c> and <c>formulas</c> overlap, so a
    /// formula_funcs row is only reachable through <see cref="GetFormulaFunc"/>.
    /// </remarks>
    public FormulaFuncTemplate RegisterFormulaFunc(uint id, string text)
    {
        var formula = new Formula { Id = id, TextFormula = text };
        if (!formula.Prepare())
        {
            _rejectedFormulaFuncs.Add(id);
            return null;
        }

        var variables = FormulaFuncRules.ParseVariables(text);
        var probe = new Dictionary<string, double>(variables.Count, StringComparer.Ordinal);
        foreach (var variable in variables)
            probe[variable.Name] = 0d;
        if (!formula.TryEvaluate(probe, out _))
        {
            _rejectedFormulaFuncs.Add(id);
            return null;
        }

        var template = new FormulaFuncTemplate { Id = id, Formula = formula, Variables = variables };
        _formulaFuncs[id] = template;
        return template;
    }

    public void Load()
    {
        if (_loaded)
            return;
        _calculationEngine ??= CreateCalculationEngine();

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
                        // 10.0.2.13 data may carry owner_type_id / kind values not in the 1.2 enums — skip
                        // unknown owners and overwrite duplicate kinds instead of crashing on load.
                        if (formula.Prepare() && _unitFormulas.TryGetValue(formula.Owner, out var ownerFormulas))
                            ownerFormulas[formula.Kind] = formula;
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
                            Id = reader.GetUInt32("id"),
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

            // 233 rows in 10.0.2.13, referenced by dynamic_unit_modifiers.func_type = 'FormulaFunc'.
            // Their ids are a SEPARATE space from formulas: 68 ids exist in both tables with different
            // text, and DynamicFunc func_id 3 exists only in formulas.
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from formula_funcs";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                        RegisterFormulaFunc(reader.GetUInt32("id"), reader.GetString("formula"));
                }
            }

            Logger.Info("formula_funcs: {0} of {1} rows compiled",
                _formulaFuncs.Count, _formulaFuncs.Count + _rejectedFormulaFuncs.Count);

            if (_rejectedFormulaFuncs.Count > 0)
                Logger.Warn("formula_funcs: dropped {0} of {1} rows the server cannot evaluate ({2})",
                    _rejectedFormulaFuncs.Count,
                    _formulaFuncs.Count + _rejectedFormulaFuncs.Count,
                    string.Join(", ", _rejectedFormulaFuncs.Order()));

            Logger.Info("Formulas loaded");
        }
        _loaded = true;
    }

    /// <summary>
    /// Jace supplies the content-used abs/floor/min/max/sqrt functions. Register the X2-specific
    /// conditionals and clamp, plus native base-10 "log" (Jace exposes only log10/loge/logn).
    /// </summary>
    private static CalculationEngine CreateCalculationEngine()
    {
        var engine = new CalculationEngine(new JaceOptions
        {
            CacheEnabled = true,
            OptimizerEnabled = true,
            CaseSensitive = true,
            ExecutionMode = ExecutionMode.Compiled,
            CultureInfo = CultureInfo.InvariantCulture,
        });
        engine.AddFunction("clamp", (a, b, c) => a < b ? b : a > c ? c : a);
        engine.AddFunction("if_negative", (a, b, c) => a < 0 ? b : c);
        engine.AddFunction("if_positive", (a, b, c) => a > 0 ? b : c);
        engine.AddFunction("if_zero", (a, b, c) => a == 0 ? b : c);
        engine.AddFunction("log", a => System.Math.Log10(a));
        return engine;
    }
}
