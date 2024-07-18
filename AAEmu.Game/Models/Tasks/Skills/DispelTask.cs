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

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        if (!Effect.IsAlive)
            return System.Threading.Tasks.Task.CompletedTask;

        if ((Effect.Target is not Buff eff) || eff.IsEnded() || eff.Owner == null)
            return System.Threading.Tasks.Task.CompletedTask;

        eff.ScheduleEffect(false);

        if (eff.IsEnded())
        {
            return System.Threading.Tasks.Task.CompletedTask;
        }
        EffectTaskManager.AddDispelTask(eff, eff.Tick);

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
