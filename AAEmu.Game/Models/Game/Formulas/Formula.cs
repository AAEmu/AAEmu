using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using NLog;

namespace AAEmu.Game.Models.Game.Formulas;

public class Formula
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

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
        lock (Expression)
        {
            try
            {
                return Expression(parameters);
            }
            catch (Exception e)
            {
                var sb = new StringBuilder();
                foreach (var (key, value) in parameters)
                    sb.AppendLine(key + ": " + value);
                _logger.Error("Error in formula {0}:\n{1}", Id, TextFormula);
                _logger.Error("Parameters:\n{0}", sb.ToString());
                _logger.Error(e);
                return 0;
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
            _logger.Error("Formula {0} : {1} has syntax errors: ", Id, TextFormula);
            _logger.Error(e);
            return false;
        }

        return true;
    }
}
