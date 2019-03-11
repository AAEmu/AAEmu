using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;
using NLog;

namespace AAEmu.Game.Models.Game.Skills
{
    public class Skill
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        
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

        public void Use(Unit caster, SkillCaster casterCaster, SkillCastTarget targetCaster, SkillObject skillObject = null)
        {
            if (skillObject == null)
                skillObject = new SkillObject();
            var effects = caster.Effects.GetEffectsByType(typeof(BuffTemplate));
            foreach (var effect in effects)
                if (((BuffTemplate)effect.Template).RemoveOnStartSkill || ((BuffTemplate)effect.Template).RemoveOnUseSkill)
                    effect.Exit();
            effects = caster.Effects.GetEffectsByType(typeof(BuffEffect));
            foreach (var effect in effects)
                if (((BuffEffect)effect.Template).Buff.RemoveOnStartSkill || ((BuffEffect)effect.Template).Buff.RemoveOnUseSkill)
                    effect.Exit();

            var target = (BaseUnit)caster;

            if (Template.TargetType == SkillTargetType.Self)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                    targetCaster.ObjId = target.ObjId;
            }
            else if (Template.TargetType == SkillTargetType.Friendly)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
                }
                else
                {
                    // TODO ...
                }

                if (caster.Faction.GetRelationState(target.Faction.Id) != RelationState.Friendly)
                    return; //TODO отправлять ошибку?
            }
            else if (Template.TargetType == SkillTargetType.Hostile)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
                }
                else
                {
                    // TODO ...
                }

                if (caster.Faction.GetRelationState(target.Faction.Id) != RelationState.Hostile)
                    return; //TODO отправлять ошибку?
            }
            else if (Template.TargetType == SkillTargetType.AnyUnit)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
                }
                else
                {
                    // TODO ...
                }
            }
            else if (Template.TargetType == SkillTargetType.Doodad)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
                }
                else
                {
                    // TODO ...
                }
            }
            else if (Template.TargetType == SkillTargetType.Item)
            {
                // TODO ...
            }
            else if (Template.TargetType == SkillTargetType.Others)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
                }
                else
                {
                    // TODO ...
                }

                if (caster.ObjId == target.ObjId)
                    return; //TODO отправлять ошибку?
            }
            else if (Template.TargetType == SkillTargetType.FriendlyOthers)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
                }
                else
                {
                    // TODO ...
                }

                if (caster.ObjId == target.ObjId)
                    return; //TODO отправлять ошибку?
                if (caster.Faction.GetRelationState(target.Faction.Id) != RelationState.Friendly)
                    return; //TODO отправлять ошибку?
            }
            else if (Template.TargetType == SkillTargetType.Building)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
                }
                else
                {
                    // TODO ...
                }
                
                if (caster.ObjId == target.ObjId)
                    return; //TODO отправлять ошибку?
            }
            else if (Template.TargetType == SkillTargetType.Pos)
            {
                var positionTarget = (SkillCastPositionTarget)targetCaster;
                var positionUnit = new BaseUnit();
                positionUnit.Position = new Point(positionTarget.PosX, positionTarget.PosY, positionTarget.PosZ);
                positionUnit.Position.ZoneId = caster.Position.ZoneId;
                positionUnit.Position.WorldId = caster.Position.WorldId;
                positionUnit.Region = caster.Region;
                target = positionUnit;
            }
            else
            {
                // TODO ...
            }

            TlId = (ushort)TlIdManager.Instance.GetNextId();
            if (Template.Plot != null)
            {
                var eventTemplate = Template.Plot.EventTemplate;
                var step = new PlotStep();
                step.Event = eventTemplate;
                step.Flag = 2;

                if (!eventTemplate.СheckСonditions(caster, casterCaster, target, targetCaster, skillObject))
                    step.Flag = 0;

                var res = true;
                if (step.Flag != 0)
                    foreach (var evnt in eventTemplate.NextEvents)
                        res = res && BuildPlot(caster, casterCaster, target, targetCaster, skillObject, evnt, step);
                ParsePlot(caster, casterCaster, target, targetCaster, skillObject, step);
                if (!res)
                    return;
                TlIdManager.Instance.ReleaseId(TlId);
                TlId = 0;
            }
            else
            {
                if (Template.CastingTime > 0)
                {
                    caster.BroadcastPacket(new SCSkillStartedPacket(Id, TlId, casterCaster, targetCaster, this, skillObject), true);
                    caster.SkillTask = new CastTask(this, caster, casterCaster, target, targetCaster, skillObject);
                    TaskManager.Instance.Schedule(caster.SkillTask, TimeSpan.FromMilliseconds(Template.CastingTime));
                }
                else
                    Cast(caster, casterCaster, target, targetCaster, skillObject);
            }
        }

        public bool BuildPlot(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject,
            PlotNextEvent nextEvent, PlotStep baseStep)
        {
            if (nextEvent.Delay > 0)
            {
                baseStep.Delay = nextEvent.Delay;
                caster.SkillTask = new PlotTask(this, caster, casterCaster, target, targetCaster, skillObject, nextEvent);
                TaskManager.Instance.Schedule(caster.SkillTask, TimeSpan.FromMilliseconds(nextEvent.Delay));
                return false;
            }

            if (nextEvent.Speed > 0)
            {
                baseStep.Speed = nextEvent.Speed;
                caster.SkillTask = new PlotTask(this, caster, casterCaster, target, targetCaster, skillObject, nextEvent);
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
                if (condition.Condition.Check(caster, casterCaster, target, targetCaster, skillObject))
                    continue;
                step.Flag = 0;
                break;
            }

            baseStep.Steps.AddLast(step);
            if (step.Flag == 0)
                return true;
            var res = true;
            foreach (var e in nextEvent.Event.NextEvents)
                res = res && BuildPlot(caster, casterCaster, target, targetCaster, skillObject, e, step);
            return res;
        }

        public void ParsePlot(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject, PlotStep step)
        {
            if (step.Flag != 0)
            {
                foreach (var eff in step.Event.Effects)
                {
                    var template = SkillManager.Instance.GetEffectTemplate(eff.ActualId, eff.ActualType);
                    if (template is BuffEffect)
                        step.Flag = 6;
                    template.Apply(caster, casterCaster, target, targetCaster,
                        new CastPlot(step.Event.PlotId, TlId, step.Event.Id, Template.Id),
                        this, skillObject, DateTime.Now);
                }
            }

            var time = (ushort)(step.Flag != 0 ? step.Delay / 10 : 0);
            var unkId = step.Casting || step.Channeling ? caster.ObjId : 0;
            caster.BroadcastPacket(
                new SCPlotEventPacket(TlId, step.Event.Id, Template.Id, caster.ObjId, target.ObjId, unkId, time, step.Flag),
                true);

            foreach (var st in step.Steps)
                ParsePlot(caster, casterCaster, target, targetCaster, skillObject, st);
        }

        public void Cast(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            caster.SkillTask = null;
            caster.BroadcastPacket(new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject), true);
            if (Template.ChannelingTime > 0)
            {
                if (Template.ChannelingBuffId != 0)
                {
                    var buff = SkillManager.Instance.GetBuffTemplate(Template.ChannelingBuffId);
                    buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), this, skillObject, DateTime.Now);
                }

                caster.SkillTask = new ChannelingTask(this, caster, casterCaster, target, targetCaster, skillObject);
                TaskManager.Instance.Schedule(caster.SkillTask, TimeSpan.FromMilliseconds(Template.ChannelingTime));
            }
            else
                Channeling(caster, casterCaster, target, targetCaster, skillObject);
        }

        public void Channeling(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            caster.SkillTask = null;
            if (Template.ChannelingBuffId != 0)
                caster.Effects.RemoveEffect(Template.ChannelingBuffId, Template.Id);
            if (Template.ToggleBuffId != 0)
            {
                var buff = SkillManager.Instance.GetBuffTemplate(Template.ToggleBuffId);
                buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), this, skillObject, DateTime.Now);
            }

            if (Template.EffectDelay > 0)
                TaskManager.Instance.Schedule(new ApplySkillTask(this, caster, casterCaster, target, targetCaster, skillObject),
                    TimeSpan.FromMilliseconds(Template.EffectDelay));
            else
                Apply(caster, casterCaster, target, targetCaster, skillObject);
        }

        public void Apply(Unit caster, SkillCaster casterCaster, BaseUnit targetSelf, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            var targets = new List<BaseUnit>(); // TODO crutches
            if (Template.TargetAreaRadius > 0)
            {
                var obj = WorldManager.Instance.GetAround<BaseUnit>(targetSelf, Template.TargetAreaRadius);
                targets.AddRange(obj);
            }
            else
                targets.Add(targetSelf);

            foreach (var effect in Template.Effects)
            {
                foreach (var target in targets)
                {
                    if (effect.StartLevel > caster.Level || effect.EndLevel < caster.Level)
                        continue;
                    if (effect.Friendly && !effect.NonFriendly &&
                        caster.Faction.GetRelationState(target.Faction.Id) != RelationState.Friendly)
                        continue;
                    if (!effect.Friendly && effect.NonFriendly &&
                        caster.Faction.GetRelationState(target.Faction.Id) != RelationState.Hostile)
                        continue;
                    if (effect.Front && !effect.Back && !MathUtil.IsFront(caster, target))
                        continue;
                    if (!effect.Front && effect.Back && MathUtil.IsFront(caster, target))
                        continue;
                    if (effect.SourceBuffTagId > 0 &&
                        !caster.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.SourceBuffTagId)))
                        continue;
                    if (effect.SourceNoBuffTagId > 0 &&
                        caster.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.SourceNoBuffTagId)))
                        continue;
                    if (effect.TargetBuffTagId > 0 &&
                        !target.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.TargetBuffTagId)))
                        continue;
                    if (effect.TargetNoBuffTagId > 0 &&
                        target.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.TargetNoBuffTagId)))
                        continue;
                    if (effect.Chance < 100 && Rand.Next(100) > effect.Chance)
                        continue;

                    effect.Template?.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId),
                        this, skillObject, DateTime.Now);
                }
            }

            if (Template.ConsumeLaborPower > 0) {
                if (caster is Character character) {
                    character.ChangeLabor((short)-Template.ConsumeLaborPower, Template.ActabilityGroupId);
                }
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
