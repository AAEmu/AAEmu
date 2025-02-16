using System;

using AAEmu.Game.Models.Game.Char;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class ManaRegenTemplate
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public Character Owner { get; set; }
    private double Tick { get; set; } // Интервал тика баффа в миллисекундах
    private double TickLevelManaCost { get; set; } // Стоимость маны за тик на уровне 1
    private int Level { get; set; } // Уровень персонажа
    private double PreciseMana { get; set; } // Точное значение маны (если реализовано)

    public ManaRegenTemplate(Character owner, double tick, double tickLevelManaCost, int level, double preciseMana = 0)
    {
        Owner = owner;
        Tick = tick;
        TickLevelManaCost = tickLevelManaCost;
        Level = level;
        PreciseMana = preciseMana;
    }

    // Расчет потребления маны за тик в зависимости от уровня
    private double CalculateManaCostPerTick()
    {
        // Формула для расчета потребления маны за тик
        //var manaPerTick = TickLevelManaCost * Level;
        var manaPerTick = 3.33 * Level + 11.67;
        return manaPerTick;
    }

    // Расчет потребления маны в секунду
    private double CalculateManaCostPerSecond()
    {
        var manaPerTick = CalculateManaCostPerTick();
        var manaPerSecond = manaPerTick * 5; // Перевод миллисекунд в секунды
        return manaPerSecond;
    }

    // Метод для применения баффа с учетом потребления маны
    public bool ApplyBuff(Character character)
    {
        var manaPerTick = CalculateManaCostPerTick();
        //var manaPerSecond = CalculateManaCostPerSecond();

        // Проверка, может баффа уже нет
        if (!character.Buffs.CheckBuff((uint)BuffConstants.Dash))
            return false;
        // Проверка на достаточность маны
        if (character.Mp >= manaPerTick)
        {
            // Уменьшение маны за тик
            character.ReduceCurrentMp(null, (int)manaPerTick);
            return true;
        }

        // Если маны недостаточно, бафф не применяется
        //Logger.Debug("Not enough mana to apply the buff.");
        return false;
    }
}
