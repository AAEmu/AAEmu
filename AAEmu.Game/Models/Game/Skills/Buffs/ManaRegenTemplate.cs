using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class ManaRegenTemplate
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public Character Owner { get; set; }
    private double Tick { get; set; } // Buff tick interval in milliseconds
    private double TickLevelManaCost { get; set; } // Mana cost multiplier per tick for the used formula 
    private int Level { get; set; } // Character level

    public ManaRegenTemplate(Character owner, double tick, double tickLevelManaCost, int level)
    {
        Owner = owner;
        Tick = tick;
        TickLevelManaCost = tickLevelManaCost;
        Level = level;
    }

    // Calculation of mana consumption per tick depending on level
    private double CalculateManaCostPerTick()
    {
        // Formula for Dash seems to be 13 where ab_level is the skill level
        // Dash's skill level is always the same as Character Level (up to max level)
        // TODO: Find the link between Dash buff and Formula 13 and make a proper calculator
        var manaPerTickFormula = FormulaManager.Instance.GetFormula(13);
        var parameters = new Dictionary<string, double>
        {
            { "ab_level", Level }
        };
        var manaPerTick = manaPerTickFormula.Evaluate(parameters) * TickLevelManaCost;
        return manaPerTick;
    }

    // Method for applying a buff based on mana consumption
    public bool ApplyBuff(Character character)
    {
        var manaPerTick = CalculateManaCostPerTick();

        if (!character.Buffs.CheckBuff((uint)BuffConstants.Dash))
            return false;
        // Checking for sufficient mana
        if (character.Mp >= manaPerTick)
        {
            // Mana reduction per tick
            character.ReduceCurrentMp(null, (int)manaPerTick);
            return true;
        }

        // If there is not enough mana, the buff will not be applied
        // Logger.Debug("Not enough mana to apply the buff.");
        return false;
    }
}
