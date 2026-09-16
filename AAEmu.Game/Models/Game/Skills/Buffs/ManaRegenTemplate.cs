using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class ManaRegenTemplate(Character owner, double tick, double tickLevelManaCost, int level)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public Character Owner { get; set; } = owner;
    private double Tick { get; set; } = tick; // Buff tick interval in milliseconds
    private double TickLevelManaCost { get; set; } = tickLevelManaCost; // Mana cost multiplier per tick for the used formula 
    private int Level { get; set; } = level; // Character level

    // Calculation of mana consumption per tick depending on level
    private double CalculateManaCostPerTick()
    {
        // The row is formulas 13, "(ab_level * 1.6 + 8) * 0.6", scaled by the buff's own
        // tick_level_mana_cost (buffs 2675 = 0.5, tick 200ms). All 70 formulas rows were read for this:
        // 13 is the only one that is a per-tick cost curve over the ability/character level, and Dash's
        // ability level tracks the character's, so it is the row rather than a stand-in. The row may be
        // absent from a content root; the tick then costs nothing instead of throwing.
        var manager = FormulaManager.Instance;
        var manaPerTickFormula = manager is { Loaded: true } ? manager.GetFormula(13) : null;
        if (manaPerTickFormula == null)
            return 0d;

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
