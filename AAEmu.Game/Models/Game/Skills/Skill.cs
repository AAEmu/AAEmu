using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
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

        //public bool isAutoAttack;
        //public SkillTask autoAttackTask;

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
            //if (caster is Character chr)
            //{
            //    var dist = MathUtil.CalculateDistance(chr.Position, chr.CurrentTarget.Position, true);
            //    if (dist > SkillManager.Instance.GetSkillTemplate(Id).MaxRange)
            //    {
            //        chr.SendMessage("Target is too far ...");
            //        return;
            //    }
            //}

            if (skillObject == null)
            {
                skillObject = new SkillObject();
            }
            var effects = caster.Effects.GetEffectsByType(typeof(BuffTemplate));
            foreach (var effect in effects)
            {
                if (((BuffTemplate)effect.Template).RemoveOnStartSkill || ((BuffTemplate)effect.Template).RemoveOnUseSkill)
                {
                    effect.Exit();
                }
            }
            effects = caster.Effects.GetEffectsByType(typeof(BuffEffect));
            foreach (var effect in effects)
            {
                if (((BuffEffect)effect.Template).Buff.RemoveOnStartSkill || ((BuffEffect)effect.Template).Buff.RemoveOnUseSkill)
                {
                    effect.Exit();
                }
            }

            var target = (BaseUnit)caster;

            if (Template.TargetType == SkillTargetType.Self)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    targetCaster.ObjId = target.ObjId;
                }
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
                {
                    return; //TODO отправлять ошибку?
                }
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
                {
                    return; //TODO отправлять ошибку?
                }
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
                {
                    return; //TODO отправлять ошибку?
                }
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
                {
                    return; //TODO отправлять ошибку?
                }
                if (caster.Faction.GetRelationState(target.Faction.Id) != RelationState.Friendly)
                {
                    return; //TODO отправлять ошибку?
                }
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
                {
                    return; //TODO отправлять ошибку?
                }
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
                {
                    step.Flag = 0;
                }

                var res = true;
                if (step.Flag != 0)
                {
                    var callCounter = new Dictionary<uint, int>();
                    callCounter.Add(step.Event.Id, 1);
                    foreach (var evnt in eventTemplate.NextEvents)
                    {
                        res = res && BuildPlot(caster, casterCaster, target, targetCaster, skillObject, evnt, step, callCounter);
                    }
                }

                ParsePlot(caster, casterCaster, target, targetCaster, skillObject, step);
                if (!res)
                {
                    return;
                }
                TlIdManager.Instance.ReleaseId(TlId);
                //TlId = 0;
            }
            else
            {
                if (Template.CastingTime > 0)
                {
                    caster.BroadcastPacket(new SCSkillStartedPacket(Id, TlId, casterCaster, targetCaster, this, skillObject), true);
                    caster.SkillTask = new CastTask(this, caster, casterCaster, target, targetCaster, skillObject);

                    TaskManager.Instance.Schedule(caster.SkillTask, TimeSpan.FromMilliseconds(Template.CastingTime));
                }
                else if (caster is Character && (Id == 2 || Id == 3 || Id == 4) && !caster.IsAutoAttack)
                {
                    caster.IsAutoAttack = true; // enable auto attack
                    caster.SkillId = Id;
                    caster.TlId = TlId;
                    caster.BroadcastPacket(new SCSkillStartedPacket(Id, TlId, casterCaster, targetCaster, this, skillObject), true);

                    caster.AutoAttackTask = new MeleeCastTask(this, caster, casterCaster, target, targetCaster, skillObject);
                    TaskManager.Instance.Schedule(caster.AutoAttackTask, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1300));
                }
                else
                {
                    Cast(caster, casterCaster, target, targetCaster, skillObject);
                }
            }
        }

        public bool BuildPlot(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject, PlotNextEvent nextEvent, PlotStep baseStep, Dictionary<uint, int> counter)
        {
            if (counter.ContainsKey(nextEvent.Event.Id))
            {
                var nextCount = counter[nextEvent.Event.Id] + 1;
                if (nextCount > nextEvent.Event.Tickets)
                {
                    return true;
                }
                counter[nextEvent.Event.Id] = nextCount;
            }
            else
            {
                counter.Add(nextEvent.Event.Id, 1);
            }

            if (nextEvent.Delay > 0)
            {
                baseStep.Delay = nextEvent.Delay;
                caster.SkillTask = new PlotTask(this, caster, casterCaster, target, targetCaster, skillObject, nextEvent, counter);
                TaskManager.Instance.Schedule(caster.SkillTask, TimeSpan.FromMilliseconds(nextEvent.Delay));
                return false;
            }

            if (nextEvent.Speed > 0)
            {
                baseStep.Speed = nextEvent.Speed;
                caster.SkillTask = new PlotTask(this, caster, casterCaster, target, targetCaster, skillObject, nextEvent, counter);
                var dist = MathUtil.CalculateDistance(caster.Position, target.Position, true);
                TaskManager.Instance.Schedule(caster.SkillTask, TimeSpan.FromSeconds(dist / nextEvent.Speed));
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
                {
                    continue;
                }
                step.Flag = 0;
                break;
            }

            baseStep.Steps.AddLast(step);
            if (step.Flag == 0)
            {
                return true;
            }
            var res = true;
            foreach (var e in nextEvent.Event.NextEvents)
            {
                res = res && BuildPlot(caster, casterCaster, target, targetCaster, skillObject, e, step, counter);
            }
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
                    {
                        step.Flag = 6;
                    }
                    template.Apply(caster, casterCaster, target, targetCaster, new CastPlot(step.Event.PlotId, TlId, step.Event.Id, Template.Id), this, skillObject, DateTime.Now);
                }
            }

            var time = (ushort)(step.Flag != 0 ? step.Delay / 10 : 0);
            var unkId = step.Casting || step.Channeling ? caster.ObjId : 0;
            caster.BroadcastPacket(new SCPlotEventPacket(TlId, step.Event.Id, Template.Id, caster.ObjId, target.ObjId, unkId, time, step.Flag), true);

            foreach (var st in step.Steps)
            {
                ParsePlot(caster, casterCaster, target, targetCaster, skillObject, st);
            }
        }

        public void Cast(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            caster.SkillTask = null;

            if (Id == 2 || Id == 3 || Id == 4)
            {
                if (caster is Character && caster.CurrentTarget == null)
                {
                    StopSkill(caster);
                    return;
                }

                // Get a random number (from 0 to n)
                var value = Rand.Next(0, 1);
                // для skillId = 2
                // 87 (35) - удар наотмаш, chr
                //  2 (00) - удар сбоку, NPC
                //  3 (46) - удар сбоку, chr
                //  1 (00) - удар похож на 2 удар сбоку, NPC
                // 91 - удар сверху (немного справа)
                // 92 - удар наотмашь слева вниз направо
                //  0 - удар не наносится (расстояние большое и надо подойти поближе), f=1, c=15
                var effectDelay = new Dictionary<int, short> { { 0, 46 }, { 1, 35 } };
                var fireAnimId = new Dictionary<int, int> { { 0, 3 }, { 1, 87 } };
                var effectDelay2 = new Dictionary<int, short> { { 0, 0 }, { 1, 0 } };
                var fireAnimId2 = new Dictionary<int, int> { { 0, 1 }, { 1, 2 } };

                var trg = (Unit)target;
                var dist = MathUtil.CalculateDistance(caster.Position, trg.Position, true);
                if (dist >= SkillManager.Instance.GetSkillTemplate(Id).MinRange && dist <= SkillManager.Instance.GetSkillTemplate(Id).MaxRange)
                {
                    caster.BroadcastPacket(caster is Character
                            ? new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay[value], fireAnimId[value])
                            : new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay2[value], fireAnimId2[value]),
                        true);
                }
                else
                {
                    caster.BroadcastPacket(caster is Character
                            ? new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay[value], fireAnimId[value], false)
                            : new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay2[value], fireAnimId2[value], false),
                        true);

                    if (caster is Character chr)
                    {
                        chr.SendMessage("Target is too far ...");
                    }
                    return;
                }
            }
            else
            {
                caster.BroadcastPacket(new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject), true);
            }

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
            {
                Channeling(caster, casterCaster, target, targetCaster, skillObject);
            }
        }

        public async void StopSkill(Unit caster)
        {
            await caster.AutoAttackTask.Cancel();
            caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
            caster.BroadcastPacket(new SCSkillStoppedPacket(caster.ObjId, Id), true);
            caster.AutoAttackTask = null;
            caster.IsAutoAttack = false; // turned off auto attack
            TlIdManager.Instance.ReleaseId(TlId);
        }

        public void Channeling(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            caster.SkillTask = null;
            if (Template.ChannelingBuffId != 0)
            {
                caster.Effects.RemoveEffect(Template.ChannelingBuffId, Template.Id);
            }
            if (Template.ToggleBuffId != 0)
            {
                var buff = SkillManager.Instance.GetBuffTemplate(Template.ToggleBuffId);
                buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), this, skillObject, DateTime.Now);
            }

            if (Template.EffectDelay > 0)
            {
                TaskManager.Instance.Schedule(new ApplySkillTask(this, caster, casterCaster, target, targetCaster, skillObject), TimeSpan.FromMilliseconds(Template.EffectDelay));
            }
            else
            {
                Apply(caster, casterCaster, target, targetCaster, skillObject);
            }
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
            {
                targets.Add(targetSelf);
            }

            foreach (var effect in Template.Effects)
            {
                foreach (var target in targets)
                {
                    if (effect.StartLevel > caster.Level || effect.EndLevel < caster.Level)
                    {
                        continue;
                    }

                    if (effect.Friendly && !effect.NonFriendly && caster.Faction.GetRelationState(target.Faction.Id) != RelationState.Friendly)
                    {
                        continue;
                    }

                    if (!effect.Friendly && effect.NonFriendly && caster.Faction.GetRelationState(target.Faction.Id) != RelationState.Hostile)
                    {
                        continue;
                    }

                    if (effect.Front && !effect.Back && !MathUtil.IsFront(caster, target))
                    {
                        continue;
                    }

                    if (!effect.Front && effect.Back && MathUtil.IsFront(caster, target))
                    {
                        continue;
                    }

                    if (effect.SourceBuffTagId > 0 && !caster.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.SourceBuffTagId)))
                    {
                        continue;
                    }

                    if (effect.SourceNoBuffTagId > 0 && caster.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.SourceNoBuffTagId)))
                    {
                        continue;
                    }

                    if (effect.TargetBuffTagId > 0 && !target.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.TargetBuffTagId)))
                    {
                        continue;
                    }

                    if (effect.TargetNoBuffTagId > 0 && target.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.TargetNoBuffTagId)))
                    {
                        continue;
                    }

                    if (effect.Chance < 100 && Rand.Next(100) > effect.Chance)
                    {
                        continue;
                    }
                    if (caster is Character character && effect.ConsumeItemId != 0 && effect.ConsumeItemCount > 0)
                    {
                        if (effect.ConsumeSourceItem)
                        {
                            var item = ItemManager.Instance.Create(effect.ConsumeItemId, effect.ConsumeItemCount, 0);
                            var res = character.Inventory.AddItem(item);
                            if (res == null)
                            {
                                ItemIdManager.Instance.ReleaseId((uint)res.Id);
                                continue;
                            }

                            var tasks = new List<ItemTask>();
                            if (res.Id != item.Id)
                            {
                                tasks.Add(new ItemCountUpdate(res, item.Count));
                            }
                            else
                            {
                                tasks.Add(new ItemAdd(item));
                            }
                            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillEffectConsumption, tasks, new List<ulong>()));
                        }
                        else
                        {
                            var inventory = character.Inventory.CheckItems(SlotType.Inventory, effect.ConsumeItemId, effect.ConsumeItemCount);
                            var equipment = character.Inventory.CheckItems(SlotType.Equipment, effect.ConsumeItemId, effect.ConsumeItemCount);
                            if (!(inventory || equipment))
                            {
                                continue;
                            }

                            var tasks = new List<ItemTask>();

                            if (inventory)
                            {
                                var items = character.Inventory.RemoveItem(effect.ConsumeItemId, effect.ConsumeItemCount);
                                foreach (var (item, count) in items)
                                {
                                    if (item.Count == 0)
                                    {
                                        tasks.Add(new ItemRemove(item));
                                    }
                                    else
                                    {
                                        tasks.Add(new ItemCountUpdate(item, -count));
                                    }
                                }
                            }
                            else if (equipment)
                            {
                                var item = character.Inventory.GetItemByTemplateId(effect.ConsumeItemId);
                                character.Inventory.RemoveItem(item, true);
                                tasks.Add(new ItemRemove(item));
                            }

                            character.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillEffectConsumption, tasks, new List<ulong>()));
                        }
                    }

                    effect.Template?.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), this, skillObject, DateTime.Now);
                }
            }

            if (Template.ConsumeLaborPower > 0 && caster is Character chart)
            {
                chart.ChangeLabor((short)-Template.ConsumeLaborPower, Template.ActabilityGroupId);
            }

            caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
            TlIdManager.Instance.ReleaseId(TlId);
            //TlId = 0;

            //if (Template.CastingTime > 0)
            //{
            //    caster.BroadcastPacket(new SCSkillStoppedPacket(caster.ObjId, Template.Id), true);
            //}
        }

        public void Stop(Unit caster)
        {
            if (Template.ChannelingBuffId != 0)
            {
                caster.Effects.RemoveEffect(Template.ChannelingBuffId, Template.Id);
            }

            if (Template.ToggleBuffId != 0)
            {
                caster.Effects.RemoveEffect(Template.ToggleBuffId, Template.Id);
            }
            caster.BroadcastPacket(new SCCastingStoppedPacket(TlId, 0), true);
            caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
            caster.SkillTask = null;
            TlIdManager.Instance.ReleaseId(TlId);
            //TlId = 0;
        }
    }
}
