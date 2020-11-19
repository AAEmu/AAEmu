using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Skills
{
    public class DispelTask : Task
    {
        public WeakReference Effect;

        public DispelTask(Effect effect)
        {
            Effect = new WeakReference(effect);
        }

        public override void Execute()
        {
            if (!Effect.IsAlive)
                return;
            var eff = Effect.Target as Effect;
            if (eff == null || eff.IsEnded())
                return;
            if (eff.Owner == null)
                return;

            eff.ScheduleEffect();

            if (eff.IsEnded())
            {
                if (eff.Owner is Unit unit)
                {
                    unit.Events.OnDispelled(eff, new OnDispelledArgs());
                }
                return;
            }
            EffectTaskManager.Instance.AddDispelTask(eff, eff.Tick);
        }
    }
}
