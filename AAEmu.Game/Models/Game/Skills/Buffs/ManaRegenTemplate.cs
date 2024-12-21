using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class ManaRegenTemplate
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public Character Owner { get; set; }
    private double Tick { get; set; } // Buff tick interval in milliseconds
    private double TickLevelManaCost { get; set; } // Mana cost per tick at level 1
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
        // Formula for calculating mana consumption per tick
        var manaPerTick = 3.33 * Level + 11.67;
        return manaPerTick;
    }

    // Method for applying a buff based on mana consumption
    public bool ApplyBuff(Character character)
    {
        //var manaPerTick = CalculateManaCostPerTick();
        var manaPerTick = CalculateManaCostPerTick();
        //var manaPerSecond = CalculateManaCostPerSecond();

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
        //Logger.Debug("Not enough mana to apply the buff.");
        return false;
    }
}
