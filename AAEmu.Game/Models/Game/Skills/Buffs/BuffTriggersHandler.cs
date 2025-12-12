using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Buffs.Triggers;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs;

public class BuffTriggersHandler(Buff buff)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly List<BuffTrigger> _triggers = [];

    public void SubscribeEvents()
    {
        var buffId = buff.Template.BuffId;

        var triggerTemplates = SkillManager.Instance.GetBuffTriggerTemplates(buffId);

        foreach (var triggerTemplate in triggerTemplates)
        {
            BuffTrigger trigger = null;
            switch (triggerTemplate.Kind)
            {
                case Buffs.BuffEventTriggerKind.Attack:
                    trigger = new BuffTrigger(buff, triggerTemplate);
                    buff.Caster.Events.OnAttack += trigger.Execute;
                    _triggers.Add(trigger);
                    break;
                case Buffs.BuffEventTriggerKind.Attacked:
                    break;
                case Buffs.BuffEventTriggerKind.Damage:
                    trigger = new BuffTrigger(buff, triggerTemplate);
                    buff.Caster.Events.OnDamage += trigger.Execute;
                    _triggers.Add(trigger);
                    break;
                case Buffs.BuffEventTriggerKind.Damaged:
                    trigger = new DamagedBuffTrigger(buff, triggerTemplate);
                    buff.Caster.Events.OnDamaged += trigger.Execute;
                    _triggers.Add(trigger);
                    break;
                case Buffs.BuffEventTriggerKind.Dispelled:
                    trigger = new BuffTrigger(buff, triggerTemplate);
                    buff.Events.OnDispelled += trigger.Execute;
                    _triggers.Add(trigger);
                    break;
                case Buffs.BuffEventTriggerKind.Timeout:
                    trigger = new BuffTrigger(buff, triggerTemplate);
                    buff.Events.OnTimeout += trigger.Execute;
                    _triggers.Add(trigger);
                    break;
                case Buffs.BuffEventTriggerKind.DamagedMelee:
                    trigger = new DamagedBuffTrigger(buff, triggerTemplate);
                    buff.Caster.Events.OnDamagedMelee += trigger.Execute;
                    _triggers.Add(trigger);
                    break;
                case Buffs.BuffEventTriggerKind.DamagedRanged:
                    trigger = new DamagedBuffTrigger(buff, triggerTemplate);
                    buff.Caster.Events.OnDamagedRanged += trigger.Execute;
                    _triggers.Add(trigger);
                    break;
                case Buffs.BuffEventTriggerKind.DamagedSpell:
                    trigger = new DamagedBuffTrigger(buff, triggerTemplate);
                    buff.Caster.Events.OnDamagedSpell += trigger.Execute;
                    _triggers.Add(trigger);
                    break;
                case Buffs.BuffEventTriggerKind.DamagedSiege:
                    trigger = new DamagedBuffTrigger(buff, triggerTemplate);
                    buff.Caster.Events.OnDamagedSiege += trigger.Execute;
                    _triggers.Add(trigger);
                    break;
                case Buffs.BuffEventTriggerKind.Landing:
                    break;
                case Buffs.BuffEventTriggerKind.Started:
                    trigger = new BuffTrigger(buff, triggerTemplate);
                    buff.Events.OnBuffStarted += trigger.Execute;
                    _triggers.Add(trigger);
                    break;
                case Buffs.BuffEventTriggerKind.RemoveOnMove:
                    break;
                case Buffs.BuffEventTriggerKind.ChannelingCancel:
                    break;
                case Buffs.BuffEventTriggerKind.RemoveOnDamage:
                    break;
                case Buffs.BuffEventTriggerKind.Death:
                    trigger = new BuffTrigger(buff, triggerTemplate);
                    buff.Caster.Events.OnDeath += trigger.Execute;
                    _triggers.Add(trigger);
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
                Logger.Trace("Unimplemented BuffTrigger[\"{0}\"]", triggerTemplate.Kind);
            }
            else
            {
                Logger.Trace("Subscribed BuffTrigger[\"{0}\"]", triggerTemplate.Kind);
            }
        }
    }
    public void UnsubscribeEvents()
    {
        //TODO These invokes need to be moved to better locations
        //TODO: Make sure this is when buff time runs out?
        //Not sure if this is for expiration or for being dispelled aka Purged
        buff.Events.OnDispelled(buff, new OnDispelledArgs());
        foreach (var trigger in _triggers)
        {
            switch (trigger.Template.Kind)
            {
                case Buffs.BuffEventTriggerKind.Attack:
                    buff.Caster.Events.OnAttack -= trigger.Execute;
                    break;
                case Buffs.BuffEventTriggerKind.Attacked:
                    break;
                case Buffs.BuffEventTriggerKind.Damage:
                    buff.Caster.Events.OnDamage -= trigger.Execute;
                    break;
                case Buffs.BuffEventTriggerKind.Damaged:
                    buff.Caster.Events.OnDamaged -= trigger.Execute;
                    break;
                case Buffs.BuffEventTriggerKind.Dispelled:
                    buff.Events.OnDispelled -= trigger.Execute;
                    break;
                case Buffs.BuffEventTriggerKind.Timeout:
                    buff.Events.OnTimeout -= trigger.Execute;
                    break;
                case Buffs.BuffEventTriggerKind.DamagedMelee:
                    buff.Caster.Events.OnDamagedMelee -= trigger.Execute;
                    break;
                case Buffs.BuffEventTriggerKind.DamagedRanged:
                    buff.Caster.Events.OnDamagedRanged -= trigger.Execute;
                    break;
                case Buffs.BuffEventTriggerKind.DamagedSpell:
                    buff.Caster.Events.OnDamagedSpell -= trigger.Execute;
                    break;
                case Buffs.BuffEventTriggerKind.DamagedSiege:
                    buff.Caster.Events.OnDamagedSiege -= trigger.Execute;
                    break;
                case Buffs.BuffEventTriggerKind.Landing:
                    break;
                case Buffs.BuffEventTriggerKind.Started:
                    buff.Events.OnBuffStarted -= trigger.Execute;
                    break;
                case Buffs.BuffEventTriggerKind.RemoveOnMove:
                    break;
                case Buffs.BuffEventTriggerKind.ChannelingCancel:
                    break;
                case Buffs.BuffEventTriggerKind.RemoveOnDamage:
                    break;
                case Buffs.BuffEventTriggerKind.Death:
                    buff.Caster.Events.OnDeath -= trigger.Execute;
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
