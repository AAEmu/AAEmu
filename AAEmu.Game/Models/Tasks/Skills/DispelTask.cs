using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Tasks.Skills;

public class DispelTask : Task
{
    public WeakReference Effect;

    public DispelTask(Buff buff)
    {
        Effect = new WeakReference(buff);
    }

    public override void Execute()
    {
        if (!Effect.IsAlive)
            return;

        if ((Effect.Target is not Buff eff) || eff.IsEnded() || eff.Owner == null)
            return;

        eff.ScheduleEffect(false);

        if (eff.IsEnded())
        {
            return;
        }
        EffectTaskManager.AddDispelTask(eff, eff.Tick);
    }
}
