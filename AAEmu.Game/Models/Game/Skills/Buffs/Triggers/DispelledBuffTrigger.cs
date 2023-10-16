﻿using System;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers;
public class DispelledBuffTrigger : BuffTrigger
{
    public override void Execute(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnAttackArgs;
        Logger.Trace("Buff[{0}] {1} executed. Applying {2}[{3}]!", _buff.Template.BuffId, this.GetType().Name, Template.Effect.GetType().Name, Template.Effect.Id);
        //Template.Effect.Apply()

        if (!(_owner is Unit owner))
        {
            Logger.Warn("AttackTrigger owner is not a Unit");
            return;
        }

        var target = _buff.Owner;
        owner = (Unit)_buff.Caster;
        if (Template.EffectOnSource)
        {
            target = _buff.Caster;
            //do what?
        }
        if (Template.UseOriginalSource)
        {
            owner = (Unit)_buff.Owner;
        }

        Template.Effect.Apply(owner, new SkillCasterUnit(_owner.ObjId), target, new SkillCastUnitTarget(target.ObjId), new CastBuff(_buff),
            new EffectSource(), // TODO : EffectSource Type trigger 
            null, DateTime.UtcNow);
    }

    public DispelledBuffTrigger(Buff owner, BuffTriggerTemplate template) : base(owner, template)
    {

    }
}
