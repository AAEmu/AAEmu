using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Buffs.Triggers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills
{
    public class BuffTriggersHandler
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Effect _owner;
        private List<BuffTrigger> _triggers;

        public BuffTriggersHandler(Effect buff)
        {
            _triggers = new List<BuffTrigger>();
            _owner = buff;
        }

        public void SubscribeEvents()
        {
            uint buffId;
            if (_owner.Template is BuffEffect buffEffect)
                buffId = buffEffect.BuffId;
            else if (_owner.Template is BuffTemplate buffTemplate)
                buffId = buffTemplate.BuffId;
            else
                return;

            var triggerTemplates = SkillManager.Instance.GetBuffTriggerTemplates(buffId);

            foreach(var triggerTemplate in triggerTemplates)
            {
                BuffTrigger trigger = null;
                switch (triggerTemplate.Kind)
                {
                    case Buffs.BuffEventTriggerKind.Attack:
                        trigger = new AttackBuffTrigger(_owner, triggerTemplate);
                        _owner.Caster.Events.OnAttack += trigger.Execute;
                        _triggers.Add(trigger);
                        break;
                    case Buffs.BuffEventTriggerKind.Attacked:
                        break;
                    case Buffs.BuffEventTriggerKind.Damage:
                        trigger = new DamageBuffTrigger(_owner, triggerTemplate);
                        _owner.Caster.Events.OnDamage += trigger.Execute;
                        _triggers.Add(trigger);
                        break;
                    case Buffs.BuffEventTriggerKind.Damaged:
                        trigger = new DamagedBuffTrigger(_owner, triggerTemplate);
                        _owner.Caster.Events.OnDamaged += trigger.Execute;
                        _triggers.Add(trigger);
                        break;
                    case Buffs.BuffEventTriggerKind.Dispelled:
                        trigger = new DispelledBuffTrigger(_owner, triggerTemplate);
                        _owner.Events.OnDispelled += trigger.Execute;
                        _triggers.Add(trigger);
                        break;
                    case Buffs.BuffEventTriggerKind.Timeout:
                        trigger = new TimeoutBuffTrigger(_owner, triggerTemplate);
                        _owner.Events.OnTimeout += trigger.Execute;
                        _triggers.Add(trigger);
                        break;
                    case Buffs.BuffEventTriggerKind.DamagedMelee:
                    case Buffs.BuffEventTriggerKind.DamagedRanged:
                    case Buffs.BuffEventTriggerKind.DamagedSpell:
                    case Buffs.BuffEventTriggerKind.DamagedSiege:
                        //Todo seperate these or add switch to Damage trigger
                        trigger = new DamagedBuffTrigger(_owner, triggerTemplate);
                        _owner.Caster.Events.OnDamaged += trigger.Execute;
                        _triggers.Add(trigger);
                        break;
                    case Buffs.BuffEventTriggerKind.Landing:
                        break;
                    case Buffs.BuffEventTriggerKind.Started:
                        trigger = new StartedBuffTrigger(_owner, triggerTemplate);
                        _owner.Events.OnBuffStarted += trigger.Execute;
                        _triggers.Add(trigger);
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
                if (trigger == null)
                {
                    _log.Warn("Unimplemented BuffTrigger[\"{0}\"]", triggerTemplate.Kind);
                }
                else
                {
                    _log.Warn("Subscribed BuffTrigger[\"{0}\"]", triggerTemplate.Kind);
                }
            }
        }
        public void UnsubscribeEvents()
        {
            //TODO These invokes need to be moved to better locations
            //TODO: Make sure this is when buff time runs out?
            _owner.Events.OnTimeout(_owner, new OnTimeoutArgs());
            //Not sure if this is for expiration or for being dispelled aka Purged
            _owner.Events.OnDispelled(_owner, new OnDispelledArgs());
            foreach (var trigger in _triggers)
            {
                switch (trigger.Template.Kind)
                {
                    case Buffs.BuffEventTriggerKind.Attack:
                        _owner.Caster.Events.OnAttack -= trigger.Execute;
                        break;
                    case Buffs.BuffEventTriggerKind.Attacked:
                        break;
                    case Buffs.BuffEventTriggerKind.Damage:
                        _owner.Caster.Events.OnDamage -= trigger.Execute;
                        break;
                    case Buffs.BuffEventTriggerKind.Damaged:
                        _owner.Caster.Events.OnDamaged -= trigger.Execute;
                        break;
                    case Buffs.BuffEventTriggerKind.Dispelled:
                        _owner.Events.OnDispelled -= trigger.Execute;
                        break;
                    case Buffs.BuffEventTriggerKind.Timeout:
                        _owner.Events.OnTimeout -= trigger.Execute;
                        break;
                    case Buffs.BuffEventTriggerKind.DamagedMelee:
                    case Buffs.BuffEventTriggerKind.DamagedRanged:
                    case Buffs.BuffEventTriggerKind.DamagedSpell:
                    case Buffs.BuffEventTriggerKind.DamagedSiege:
                        _owner.Caster.Events.OnDamaged -= trigger.Execute;
                        break;
                    case Buffs.BuffEventTriggerKind.Landing:
                        break;
                    case Buffs.BuffEventTriggerKind.Started:
                        _owner.Events.OnBuffStarted -= trigger.Execute;
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

            _triggers.Clear();
        }
    }
}
