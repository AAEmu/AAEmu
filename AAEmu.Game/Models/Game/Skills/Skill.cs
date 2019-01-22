using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills
{
    public class Skill
    {
        public uint Id { get; set; }
        public SkillTemplate Template { get; set; }
        public byte Level { get; set; }
        public ushort TlId { get; set; }

        public Skill()
        {
        }

        public Skill(SkillTemplate template)
        {
            Id = template.Id;
            Template = template;
            Level = 1;
        }

        public void Use(Unit caster, SkillAction casterAction, SkillAction targetAction)
        {
            var target = (BaseUnit)caster;

            if (Template.TargetType == SkillTargetType.Self)
            {
                if (targetAction.Type == SkillActionType.Unit || targetAction.Type == SkillActionType.Doodad)
                    targetAction.ObjId = target.ObjId;
            }
            else
            {
                // TODO ...
            }

            TlId = (ushort) TlIdManager.Instance.GetNextId();
            if (Template.Plot != null)
            {
                var eventTemplate = Template.Plot.EventTemplate;
                var step = new PlotStep();
                step.Event = eventTemplate;
                step.Flag = 2;

                if (!eventTemplate.СheckСonditions(caster, casterAction, target, targetAction))
                    step.Flag = 0;
                
                var res = true;
                if (step.Flag != 0)
                    foreach (var evnt in eventTemplate.NextEvents)
                        res = res && BuildPlot(caster, casterAction, target, targetAction, evnt, step);
                ParsePlot(caster, casterAction, target, targetAction, step);
                if (!res) 
                    return;
                TlIdManager.Instance.ReleaseId(TlId);
                TlId = 0;
            }
            else
            {
                if (Template.CastingTime > 0)
                {
                    caster.BroadcastPacket(new SCSkillStartedPacket(Id, TlId, casterAction, targetAction, this), true);
                    caster.SkillTask = new CastTask(this, caster, casterAction, target, targetAction);
                    TaskManager.Instance.Schedule(caster.SkillTask, TimeSpan.FromMilliseconds(Template.CastingTime));
                }
                else
                    Cast(caster, casterAction, target, targetAction);
            }
        }

        public bool BuildPlot(Unit caster, SkillAction casterAction, BaseUnit target, SkillAction targetAction, PlotNextEvent nextEvent,
            PlotStep baseStep)
        {
            if (nextEvent.Delay > 0)
            {
                baseStep.Delay = nextEvent.Delay;
                caster.SkillTask = new PlotTask(this, caster, casterAction, target, targetAction, nextEvent);
                TaskManager.Instance.Schedule(caster.SkillTask, TimeSpan.FromMilliseconds(nextEvent.Delay));
                return false;
            }

            if (nextEvent.Speed > 0)
            {
                baseStep.Speed = nextEvent.Speed;
                caster.SkillTask = new PlotTask(this, caster, casterAction, target, targetAction, nextEvent);
                TaskManager.Instance.Schedule(caster.SkillTask,
                    TimeSpan.FromMilliseconds(nextEvent.Speed * 40)); // TODO зависит от расстояния, найти формулу
                return false;
            }

            var step = new PlotStep();
            step.Event = nextEvent.Event;
            step.Flag = 2;
            step.Casting = nextEvent.Casting;
            step.Channeling = nextEvent.Channeling;
            foreach (var condition in nextEvent.Event.Conditions)
            {
                if (condition.Condition.Check(caster, casterAction, target, targetAction)) 
                    continue;
                step.Flag = 0;
                break;
            }

            baseStep.Steps.AddLast(step);
            if (step.Flag == 0)
                return true;
            var res = true;
            foreach (var e in nextEvent.Event.NextEvents)
                res = res && BuildPlot(caster, casterAction, target, targetAction, e, step);
            return res;
        }

        public void ParsePlot(Unit caster, SkillAction casterAction, BaseUnit target, SkillAction targetAction, PlotStep step)
        {
            if (step.Flag != 0)
            {
                foreach (var eff in step.Event.Effects)
                {
                    var template = SkillManager.Instance.GetEffectTemplate(eff.ActualId, eff.ActualType);
                    if (template is BuffEffect)
                        step.Flag = 6;
                    template.Apply(caster, casterAction, target, targetAction,
                        new CastPlot(step.Event.PlotId, TlId, step.Event.Id),
                        this, DateTime.Now);
                }
            }

            var time = (ushort) (step.Flag != 0 ? step.Delay / 10 : 0);
            var unkId = step.Casting || step.Channeling ? caster.ObjId : 0;
            caster.BroadcastPacket(
                new SCPlotEventPacket(TlId, step.Event.Id, Template.Id, caster.ObjId, target.ObjId, unkId, time, step.Flag),
                true);

            foreach (var st in step.Steps)
                ParsePlot(caster, casterAction, target, targetAction, st);
        }

        public void Cast(Unit caster, SkillAction casterAction, BaseUnit target, SkillAction targetAction)
        {
            caster.SkillTask = null;
            caster.BroadcastPacket(new SCSkillFiredPacket(Id, TlId, casterAction, targetAction, this), true);
            if (Template.ChannelingTime > 0)
            {
                if (Template.ChannelingBuffId != 0)
                {
                    var buff = SkillManager.Instance.GetBuffTemplate(Template.ChannelingBuffId);
                    buff.Apply(caster, casterAction, target, targetAction, new CastSkill(Template.Id, TlId), this, DateTime.Now);
                }

                caster.SkillTask = new ChannelingTask(this, caster, casterAction, target, targetAction);
                TaskManager.Instance.Schedule(caster.SkillTask, TimeSpan.FromMilliseconds(Template.ChannelingTime));
            }
            else
                Channeling(caster, casterAction, target, targetAction);
        }

        public void Channeling(Unit caster, SkillAction casterAction, BaseUnit target, SkillAction targetAction)
        {
            caster.SkillTask = null;
            if (Template.ChannelingBuffId != 0)
                caster.Effects.RemoveEffect(Template.ChannelingBuffId, Template.Id);
            if (Template.ToggleBuffId != 0)
            {
                var buff = SkillManager.Instance.GetBuffTemplate(Template.ToggleBuffId);
                buff.Apply(caster, casterAction, target, targetAction, new CastSkill(Template.Id, TlId), this, DateTime.Now);
            }

            if (Template.EffectDelay > 0)
                TaskManager.Instance.Schedule(new ApplySkillTask(this, caster, casterAction, target, targetAction),
                    TimeSpan.FromMilliseconds(Template.EffectDelay));
            else
                Apply(caster, casterAction, target, targetAction);
        }

        public void Apply(Unit caster, SkillAction casterAction, BaseUnit target, SkillAction targetAction)
        {
            foreach (var effect in Template.Effects)
            {
                if (effect.StartLevel > caster.Level || effect.EndLevel < caster.Level)
                    continue;
                if (effect.Friendly && !effect.NonFriendly && caster.Faction.GetRelationState(target.Faction.Id) != RelationState.Friendly)
                    continue;
                if (!effect.Friendly && effect.NonFriendly && caster.Faction.GetRelationState(target.Faction.Id) != RelationState.Hostile)
                    continue;
                if (effect.Front && !effect.Back && !MathUtil.IsFront(caster, target))
                    continue;
                if (!effect.Front && effect.Back && MathUtil.IsFront(caster, target))
                    continue;
                if (effect.SourceBuffTagId > 0 && !caster.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.SourceBuffTagId)))
                    continue;
                if (effect.SourceNoBuffTagId > 0 &&
                    caster.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.SourceNoBuffTagId)))
                    continue;
                if (effect.TargetBuffTagId > 0 && !target.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.TargetBuffTagId)))
                    continue;
                if (effect.TargetNoBuffTagId > 0 &&
                    target.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.TargetNoBuffTagId)))
                    continue;
                if (effect.Chance < 100 && Rand.Next(100) > effect.Chance)
                    continue;

                effect.Template?.Apply(caster, casterAction, target, targetAction, new CastSkill(Template.Id, TlId), this, DateTime.Now);
            }

            caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
            TlIdManager.Instance.ReleaseId(TlId);
            TlId = 0;
        }

        public void Stop(Unit caster)
        {
            if (Template.ChannelingBuffId != 0)
                caster.Effects.RemoveEffect(Template.ChannelingBuffId, Template.Id);
            if (Template.ToggleBuffId != 0)
                caster.Effects.RemoveEffect(Template.ToggleBuffId, Template.Id);
            caster.BroadcastPacket(new SCCastingStoppedPacket(TlId, 0), true);
            caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
            caster.SkillTask = null;
            TlIdManager.Instance.ReleaseId(TlId);
            TlId = 0;
        }
    }
}