using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers;

internal class StartedBuffTrigger(Buff owner, BuffTriggerTemplate template) : BuffTrigger(owner, template)
{
    public override void Execute(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnBuffStartedArgs;
        Logger.Trace("Buff[{0}] {1} executed. Applying {2}[{3}]!", _buff?.Template?.BuffId, this.GetType()?.Name, Template?.Effect?.GetType().Name, Template?.Effect?.Id);

        if (_owner is not Unit owner)
        {
            Logger.Warn("AttackTrigger owner is not a Unit");
            return;
        }

        // 10.0.2.13: buff_triggers.effect_on_source / use_original_source removed (former mock-false path).
        var target = _buff.Owner;
        owner = (Unit)_buff.Owner;

        if (Template.TargetBuffTagId != 0)
        {
            if (!target.Buffs.CheckBuffTag(Template.TargetBuffTagId))
                return;
        }
        if (Template.TargetNoBuffTagId != 0)
        {
            if (target.Buffs.CheckBuffTag(Template.TargetNoBuffTagId))
                return;
        }

        Template.Effect.Apply(owner, new SkillCasterUnit(_owner.ObjId), target, new SkillCastUnitTarget(target.ObjId), new CastBuff(_buff),
            new EffectSource(), // TODO : EffectSource Type trigger 
            null, DateTime.UtcNow);
    }
}
