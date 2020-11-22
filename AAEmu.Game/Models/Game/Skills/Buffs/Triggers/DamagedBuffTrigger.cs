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
            var args = eventArgs as OnDamageArgs;

            _log.Warn("Buff[{0}] {1} executed. Applying {2}[{3}]!", _effect.Template.BuffId, this.GetType().Name, Template.Effect.GetType().Name, Template.Effect.Id);

            if (!(_owner is Unit owner))
            {
                _log.Warn("AttackTrigger owner is not a Unit");
                return;   
            }

            var target = owner;
            if (Template.EffectOnSource)
                target = args.Attacker;
            
            Template.Effect.Apply(owner, new SkillCasterUnit(_owner.ObjId), target, new SkillCastUnitTarget(target.ObjId), new CastBuff(_effect),
                new EffectSource(), // TODO : EffectSource Type trigger 
                null, DateTime.Now);
        }

        public DamagedBuffTrigger(Effect owner, BuffTriggerTemplate template) : base(owner, template)
        {

        }
    }
}
