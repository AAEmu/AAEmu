using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class ManaRegenTemplate(
    Character owner,
    uint buffId,
    double tick,
    double tickLevelManaCost,
    int level,
    int flatManaCost = 0)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public Character Owner { get; set; } = owner;

    /// <summary>
    /// The <c>buffs.id</c> this registration drains for, and the buff that is dropped when the owner can
    /// no longer pay. Dash (2675) was the only row that reached here before the column was generalised, so
    /// this is the same id the manager used to hardcode — now carried per registration instead.
    /// </summary>
    public uint BuffId { get; set; } = buffId;

    private double Tick { get; set; } = tick; // Buff tick interval in milliseconds
    private double TickLevelManaCost { get; set; } = tickLevelManaCost; // Mana cost multiplier per tick for the used formula 
    private int Level { get; set; } = level; // Character level

    /// <summary>Payment cadence: the manager ticks every 200 ms, the buff pays every <c>buffs.tick</c>.</summary>
    private int TicksPerPayment { get; } = TickManaCostRules.TicksPerPayment(tick);
    private int TicksSincePayment { get; set; }

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
        var levelCurveValue = manaPerTickFormula?.Evaluate(
            new Dictionary<string, double> { { "ab_level", Level } }) ?? 0d;
        return TickManaCostRules.Payment(flatManaCost, TickLevelManaCost, levelCurveValue);
    }

    // Method for applying a buff based on mana consumption
    public bool ApplyBuff(Character character)
    {
        if (!character.Buffs.CheckBuff(BuffId))
            return false;

        // Not a payment boundary yet: the buff is up and paying on schedule, keep the registration.
        if (++TicksSincePayment < TicksPerPayment)
            return true;
        TicksSincePayment = 0;

        var manaPerTick = CalculateManaCostPerTick();
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
