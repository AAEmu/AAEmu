﻿using System;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers;

internal class StartedBuffTrigger : BuffTrigger
{
    public override void Execute(object sender, EventArgs eventArgs)
    {
        var args = eventArgs as OnBuffStartedArgs;
        Logger.Trace("Buff[{0}] {1} executed. Applying {2}[{3}]!", _buff?.Template?.BuffId, this.GetType()?.Name, Template?.Effect?.GetType().Name, Template?.Effect?.Id);

        if (!(_owner is Unit owner))
        {
            Logger.Warn("AttackTrigger owner is not a Unit");
            return;
        }

        var target = _buff.Owner;
        owner = (Unit)_buff.Owner;
        if (Template.EffectOnSource)
        {
            target = (Unit)_buff.Caster;
            //do what?
        }
        if (Template.UseOriginalSource)
        {
            owner = (Unit)_buff.Caster;
        }

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

    public StartedBuffTrigger(Buff owner, BuffTriggerTemplate template) : base(owner, template)
    {

    }
}
