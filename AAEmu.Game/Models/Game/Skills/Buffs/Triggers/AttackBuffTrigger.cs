using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers
{
    class AttackBuffTrigger : BuffTrigger
    {
        public override void Execute(object sender, EventArgs eventArgs)
        {
            var args = eventArgs as OnAttackArgs;

            var caster = _owner.Caster;
            var target = _owner.Caster;
            //Template.Effect.Apply()
        }

        public AttackBuffTrigger(Effect owner, BuffTriggerTemplate template) : base(owner, template)
        {

        }
    }
}
