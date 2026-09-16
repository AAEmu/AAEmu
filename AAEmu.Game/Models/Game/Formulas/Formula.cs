using System.Text;
using AAEmu.Game.Core.Managers;
using NLog;

namespace AAEmu.Game.Models.Game.Formulas;

public class Formula
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Func<Dictionary<string, double>, double> Expression { get; set; }

    public uint Id { get; set; }
    public string TextFormula { get; set; }

    public Formula()
    {
    }

    public Formula(string formula)
    {
        TextFormula = formula;
        Prepare();
    }

    public double Evaluate(Dictionary<string, double> parameters)
    {
        return TryEvaluate(parameters, out var value) ? value : 0;
    }

    /// <summary>
    /// Evaluates the compiled expression and reports whether it produced a value, instead of
    /// collapsing a failure to zero. Callers that must not apply a value on failure (the
    /// formula_funcs probe at load) use this.
    /// </summary>
    public bool TryEvaluate(Dictionary<string, double> parameters, out double value)
    {
        value = 0d;

        if (Expression == null)
        {
            // Prepare() failed or was never run; there is nothing to evaluate.
            Logger.Error("Formula {0} : {1} has no compiled expression", Id, TextFormula);
            return false;
        }

        lock (Expression)
        {
            try
            {
                value = Expression(parameters);
                return true;
            }
            catch (Exception e)
            {
                var sb = new StringBuilder();
                foreach (var (key, parameterValue) in parameters)
                    sb.AppendLine(key + ": " + parameterValue);
                Logger.Error("Error in formula {0}:\n{1}", Id, TextFormula);
                Logger.Error("Parameters:\n{0}", sb.ToString());
                Logger.Error(e);
                return false;
            }
        }
    }

    public bool Prepare()
    {
        try
        {
            Expression = FormulaManager.Instance.CalculationEngine.Build(TextFormula);
        }
        catch (Exception e)
        {
            Logger.Error("Formula {0} : {1} has syntax errors: ", Id, TextFormula);
            Logger.Error(e);
            return false;
        }

        return true;
    }
}
