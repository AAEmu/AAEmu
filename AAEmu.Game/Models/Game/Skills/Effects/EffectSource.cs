using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class EffectSource
{
    private readonly Action<Action> _deferUntilPlotEventProcessed;

    public Skill Skill { get; set; }
    public BuffTemplate Buff { get; set; }
    public int Amount { get; set; }
    public bool IsTrigger { get; set; }

    /// <summary>
    /// The effect was applied by a buff trigger rather than by a cast.
    /// </summary>
    /// <remarks>
    /// A marker of its own rather than <see cref="IsTrigger"/>, which answers a different question: whether
    /// the trigger row reads the event's damage amount. <c>BuffTrigger</c> sets that one only for
    /// <c>use_damage_amount</c> rows because <c>HealEffect</c> branches on it to scale a fixed heal by the
    /// hit (<c>value / 1000 * Amount</c>, HealEffect.cs:114), and 350 of the 362 enabled
    /// <c>buff_triggers</c> rows that apply a damage effect carry <c>use_damage_amount='f'</c> — so
    /// <c>IsTrigger</c> is false for almost every damage-dealing trigger and cannot say where the hit came
    /// from. This one is set on every row, and <c>DamageEffect</c> reads it to raise
    /// <c>remove_on_*_buff_trigger</c> (344/486/301/680 buffs).
    /// </remarks>
    public bool FromBuffTrigger { get; set; }

    public EffectSource()
    {
    }

    public EffectSource(Skill skill)
    {
        Skill = skill;
    }

    public EffectSource(BuffTemplate buff)
    {
        Buff = buff;
    }

    public EffectSource(Skill skill, BuffTemplate buff)
    {
        Skill = skill;
        Buff = buff;
    }

    internal EffectSource(Skill skill, Action<Action> deferUntilPlotEventProcessed)
    {
        Skill = skill;
        _deferUntilPlotEventProcessed = deferUntilPlotEventProcessed;
    }

    public bool DeferUntilPlotEventProcessed(Action action)
    {
        if (_deferUntilPlotEventProcessed == null)
            return false;

        _deferUntilPlotEventProcessed(action);
        return true;
    }
}
