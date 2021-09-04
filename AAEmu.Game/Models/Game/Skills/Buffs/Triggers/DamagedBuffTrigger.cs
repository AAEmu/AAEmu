using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers
{
    class DamagedBuffTrigger : BuffTrigger
    {
        public override void Execute(object sender, EventArgs eventArgs)
        {
            var args = eventArgs as OnDamagedArgs;

            _log.Trace("Buff[{0}] {1} executed. Applying {2}[{3}]!", _buff.Template.BuffId, this.GetType().Name, Template.Effect.GetType().Name, Template.Effect.Id);

            if (!(_owner is Unit owner))
            {
                _log.Warn("AttackTrigger owner is not a Unit");
                return;   
            }

            var target = _buff.Owner;
            var source = (Unit)_buff.Owner;

            if (Template.UseOriginalSource)
            {
                source = _buff.Caster;
            }

            if (Template.EffectOnSource)
            {
                target = source;
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
                new EffectSource(_buff.Template) {Amount = args?.Amount ?? 0, IsTrigger = true}, // TODO : EffectSource Type trigger 
                null, DateTime.UtcNow);
        }

        public DamagedBuffTrigger(Buff owner, BuffTriggerTemplate template) : base(owner, template)
        {

        }
    }
}
