using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers
{
    class DamageBuffTrigger : BuffTrigger
    {
        public override void Execute(object sender, EventArgs eventArgs)
        {
            var args = eventArgs as OnDamageArgs;

            var caster = _owner.Caster;
            var target = _owner.Caster;
            _log.Warn("Buff[{0}] damage trigger executed. Applying Effect[{1}]!", _owner.Template.BuffId, Template.Effect.Id);

            /*
            var skillCaster = new SkillCasterUnit(caster.ObjId);
            var skillCastTarget = new SkillCastUnitTarget(caster.ObjId);
            var effect = new Effect(caster, caster, skillCaster, Template.Effect, null, DateTime.Now);
            var skillObj = new SkillObject();
            Template.Effect.Apply(caster, skillCaster, caster, skillCastTarget, new CastBuff(effect), new EffectSource(_owner.Template), skillObj, DateTime.Now*/
        }

        public DamageBuffTrigger(Effect owner, BuffTriggerTemplate template) : base(owner, template)
        {

        }
    }
}
