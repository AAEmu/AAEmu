using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers;

public class TimeoutBuffTrigger(Buff owner, BuffTriggerTemplate template) : BuffTrigger(owner, template)
{
    public override void Execute(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnTimeoutArgs;
        Logger.Trace("Buff[{0}] {1} executed. Applying {2}[{3}]!", _buff?.Template?.BuffId, this.GetType().Name, Template.Effect.GetType().Name, Template.Effect.Id);
        //Template.Effect.Apply()

        if (_owner is not Unit owner)
        {
            Logger.Warn("Owner is not a Unit");
            return;
        }

        // 10.0.2.13: buff_triggers.effect_on_source / use_original_source removed (former mock-false path).
        var target = _buff.Owner;
        var source = (Unit)_buff.Owner;

        Template.Effect.Apply(source, new SkillCasterUnit(_owner.ObjId), target, new SkillCastUnitTarget(target.ObjId), new CastBuff(_buff),
            new EffectSource(_buff?.Skill), // TODO : EffectSource Type trigger 
            null, DateTime.UtcNow);
    }
}
