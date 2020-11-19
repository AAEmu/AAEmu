using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Buffs.Triggers;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills
{
    public class BuffTriggersHandler
    {
        private Effect _owner;
        private List<BuffTrigger> _triggers;

        public BuffTriggersHandler(Effect buff)
        {
            _triggers = new List<BuffTrigger>();
            _owner = buff;
        }

        private void AttackTrigger(object sender, OnAttackArgs args)
        {

        }

        public void SubscribeEvents()
        {
            if (_owner.Template is BuffTemplate template)
            {
                var triggers = SkillManager.Instance.GetBuffTriggerTemplates(template.BuffId);

                foreach(var trigger in triggers)
                {
                    switch (trigger.Kind)
                    {
                        case Buffs.BuffEventTriggerKind.Attack:
                            break;
                        case Buffs.BuffEventTriggerKind.Attacked:
                            break;
                        case Buffs.BuffEventTriggerKind.Damage:
                            break;
                        case Buffs.BuffEventTriggerKind.Damaged:
                            break;
                        case Buffs.BuffEventTriggerKind.Dispelled:
                            break;
                        case Buffs.BuffEventTriggerKind.Timeout:
                            break;
                        case Buffs.BuffEventTriggerKind.DamagedMelee:
                            break;
                        case Buffs.BuffEventTriggerKind.DamagedRanged:
                            break;
                        case Buffs.BuffEventTriggerKind.DamagedSpell:
                            break;
                        case Buffs.BuffEventTriggerKind.DamagedSiege:
                            break;
                        case Buffs.BuffEventTriggerKind.Landing:
                            break;
                        case Buffs.BuffEventTriggerKind.Started:
                            break;
                        case Buffs.BuffEventTriggerKind.RemoveOnMove:
                            break;
                        case Buffs.BuffEventTriggerKind.ChannelingCancel:
                            break;
                        case Buffs.BuffEventTriggerKind.RemoveOnDamage:
                            break;
                        case Buffs.BuffEventTriggerKind.Death:
                            break;
                        case Buffs.BuffEventTriggerKind.Unmount:
                            break;
                        case Buffs.BuffEventTriggerKind.Kill:
                            break;
                        case Buffs.BuffEventTriggerKind.DamagedCollision:
                            break;
                        case Buffs.BuffEventTriggerKind.Immotality:
                            break;
                        case Buffs.BuffEventTriggerKind.Time:
                            break;
                        case Buffs.BuffEventTriggerKind.KillAny:
                            break;
                        default:
                            break;
                    }
                }
            }
        }
        public void UnsubscribeEvents()
        {
            if (_owner.Template is BuffTemplate template)
            {
                var triggers = SkillManager.Instance.GetBuffTriggerTemplates(template.BuffId);

                foreach (var trigger in triggers)
                {
                    switch (trigger.Kind)
                    {
                        case Buffs.BuffEventTriggerKind.Attack:
                            break;
                        case Buffs.BuffEventTriggerKind.Attacked:
                            break;
                        case Buffs.BuffEventTriggerKind.Damage:
                            break;
                        case Buffs.BuffEventTriggerKind.Damaged:
                            break;
                        case Buffs.BuffEventTriggerKind.Dispelled:
                            break;
                        case Buffs.BuffEventTriggerKind.Timeout:
                            break;
                        case Buffs.BuffEventTriggerKind.DamagedMelee:
                            break;
                        case Buffs.BuffEventTriggerKind.DamagedRanged:
                            break;
                        case Buffs.BuffEventTriggerKind.DamagedSpell:
                            break;
                        case Buffs.BuffEventTriggerKind.DamagedSiege:
                            break;
                        case Buffs.BuffEventTriggerKind.Landing:
                            break;
                        case Buffs.BuffEventTriggerKind.Started:
                            break;
                        case Buffs.BuffEventTriggerKind.RemoveOnMove:
                            break;
                        case Buffs.BuffEventTriggerKind.ChannelingCancel:
                            break;
                        case Buffs.BuffEventTriggerKind.RemoveOnDamage:
                            break;
                        case Buffs.BuffEventTriggerKind.Death:
                            break;
                        case Buffs.BuffEventTriggerKind.Unmount:
                            break;
                        case Buffs.BuffEventTriggerKind.Kill:
                            break;
                        case Buffs.BuffEventTriggerKind.DamagedCollision:
                            break;
                        case Buffs.BuffEventTriggerKind.Immotality:
                            break;
                        case Buffs.BuffEventTriggerKind.Time:
                            break;
                        case Buffs.BuffEventTriggerKind.KillAny:
                            break;
                        default:
                            break;
                    }
                }
            }
        }
    }
}
