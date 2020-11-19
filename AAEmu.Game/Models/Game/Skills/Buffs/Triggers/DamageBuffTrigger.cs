using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
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
            //Template.Effect.Apply()
        }

        public DamageBuffTrigger(Effect owner, BuffTriggerTemplate template) : base(owner, template)
        {

        }
    }
}
