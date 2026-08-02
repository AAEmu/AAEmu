using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class EffectSource
{
    private readonly Action<Action> _deferUntilPlotEventProcessed;

    public Skill Skill { get; set; }
    public BuffTemplate Buff { get; set; }
    public int Amount { get; set; }
    public bool IsTrigger { get; set; }

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
