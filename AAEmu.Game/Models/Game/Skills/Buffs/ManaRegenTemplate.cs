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
        // Formula for Dash seems to be 13 where ab_level is the skill level
        // Dash's skill level is always the same as Character Level (up to max level)
        // TODO: Find the link between Dash buff and Formula 13 and make a proper calculator
        var manaPerTickFormula = FormulaManager.Instance.GetFormula(13);
        var levelCurveValue = manaPerTickFormula?.Evaluate(new Dictionary<string, double> { { "ab_level", Level } }) ?? 0;
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
