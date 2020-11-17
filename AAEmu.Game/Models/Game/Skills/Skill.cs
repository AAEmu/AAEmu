using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Static;
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
        public PlotState ActivePlotState { get; set; }

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
            lock (caster.GCDLock)
            {
                if (caster.SkillLastUsed.AddMilliseconds(150) > DateTime.Now)
                    return;

                if (caster.GlobalCooldown >= DateTime.Now && !Template.IgnoreGlobalCooldown)
                    return;

                caster.SkillLastUsed = DateTime.Now;
            }

            if (skillObject == null)
            {
                skillObject = new SkillObject();
            }

            var target = GetInitialTarget(caster, targetCaster);

            if (target == null)
                return;//We should try to make sure this doesnt happen

            TlId = (ushort)TlIdManager.Instance.GetNextId();

            if (Template.Plot != null)
            {
                Task.Run(() => Template.Plot.Run(caster, casterCaster, target, targetCaster, skillObject, this));
                if (Template.PlotOnly)
                    return;
            }

            // TODO : Add check for range
            var skillRange = caster.ApplySkillModifiers(this, SkillAttribute.Range, Template.MaxRange);

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

            //Maybe we should do this somewhere else?
            if (Template.DefaultGcd)
            {
                //TODO Apply Attack Spped * GCD
                caster.GlobalCooldown = DateTime.Now.AddMilliseconds(1000);
            }
            else
            {
                //TODO Apply Attack Speed * GCD
                caster.GlobalCooldown = DateTime.Now.AddMilliseconds(Template.CustomGcd);
            }
            
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
                caster.BroadcastPacket(new SCSkillStartedPacket(Id, 0, casterCaster, targetCaster, this, skillObject), true);

                caster.AutoAttackTask = new MeleeCastTask(this, caster, casterCaster, target, targetCaster, skillObject);
                TaskManager.Instance.Schedule(caster.AutoAttackTask, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1300));
            }
            else
            {
                Cast(caster, casterCaster, target, targetCaster, skillObject);
            }
        }

        private BaseUnit GetInitialTarget(Unit caster, SkillCastTarget targetCaster)
        {
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

                if (caster.GetRelationStateTo(target) != RelationState.Friendly)
                {
                    return null; //TODO отправлять ошибку?
                }
            }
            else if (Template.TargetType == SkillTargetType.Hostile)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
                }

                if (caster.GetRelationStateTo(target) != RelationState.Hostile)
                {
                    if(!caster.CanAttack(target))
                    {
                        return null; //TODO отправлять ошибку?
                    }
                }
            }
            else if (Template.TargetType == SkillTargetType.AnyUnit)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
                }
            }
            else if (Template.TargetType == SkillTargetType.Doodad)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
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

                if (caster.ObjId == target.ObjId)
                {
                    return null; //TODO отправлять ошибку?
                }
            }
            else if (Template.TargetType == SkillTargetType.FriendlyOthers)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
                }

                if (caster.ObjId == target.ObjId)
                {
                    return null; //TODO отправлять ошибку?
                }
                if (caster.GetRelationStateTo(target) != RelationState.Friendly)
                {
                    return null; //TODO отправлять ошибку?
                }
            }
            else if (Template.TargetType == SkillTargetType.Building)
            {
                if (targetCaster.Type == SkillCastTargetType.Unit || targetCaster.Type == SkillCastTargetType.Doodad)
                {
                    target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                    targetCaster.ObjId = target.ObjId;
                }

                if (caster.ObjId == target.ObjId)
                {
                    return null; //TODO отправлять ошибку?
                }
            }
            else if (Template.TargetType == SkillTargetType.Pos)
            {
                var positionTarget = (SkillCastPositionTarget)targetCaster;
                var positionUnit = new BaseUnit();
                positionUnit.ObjId = uint.MaxValue;
                positionUnit.Position = new Point(positionTarget.PosX, positionTarget.PosY, positionTarget.PosZ);
                positionUnit.Position.ZoneId = caster.Position.ZoneId;
                positionUnit.Position.WorldId = caster.Position.WorldId;
                positionUnit.Region = caster.Region;
                target = positionUnit;
            }

            return target;
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
                    buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.Now);
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
        {;
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
                buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.Now);
            }

            var totalDelay = 0;
            if (Template.EffectDelay > 0)
                totalDelay += Template.EffectDelay;
            if (Template.EffectSpeed > 0)
                totalDelay += (int) ((caster.GetDistanceTo(target) / Template.EffectSpeed) * 1000.0f);
            if (Template.FireAnim != null && Template.UseAnimTime)
                totalDelay += Template.FireAnim.CombatSyncTime;
            
            if (totalDelay > 0) 
                TaskManager.Instance.Schedule(new ApplySkillTask(this, caster, casterCaster, target, targetCaster, skillObject), TimeSpan.FromMilliseconds(totalDelay));
            else 
                Apply(caster, casterCaster, target, targetCaster, skillObject);
                
        }

        private IEnumerable<BaseUnit> FilterAoeUnits(BaseUnit caster, IEnumerable<BaseUnit> units)
        {
            switch (Template.TargetRelation)
            {
                case SkillTargetRelation.Any:
                    //Filter Nothing
                    break;
                case SkillTargetRelation.Friendly:
                    units = units.Where(o => caster.GetRelationStateTo(o) == RelationState.Friendly && !caster.CanAttack(o));
                    break;
                case SkillTargetRelation.Hostile:
                    units = units.Where(o => caster.CanAttack(o));
                    break;
                case SkillTargetRelation.Party:
                    //todo
                    break;
                case SkillTargetRelation.Raid:
                    //todo
                    break;
                case SkillTargetRelation.Others:
                    //Does others == neutral? This might be wrong
                    units = units.Where(o => caster.GetRelationStateTo(o) == RelationState.Neutral);
                    break;
            }
            return units;
        }

        public void Apply(Unit caster, SkillCaster casterCaster, BaseUnit targetSelf, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            var targets = new List<BaseUnit>(); // TODO crutches
            if (Template.TargetAreaRadius > 0)
            {
                var units = WorldManager.Instance.GetAround<BaseUnit>(targetSelf, Template.TargetAreaRadius);
                units.Add(targetSelf);
                units = FilterAoeUnits(caster, units).ToList();

                targets.AddRange(units);
                // TODO : Need to this if this is needed
                //if (targetSelf is Unit) targets.Add(targetSelf);
            }
            else
            {
                targets.Add(targetSelf);
            }

            var packets = new CompressedGamePackets();
            var effectsToApply = new List<(BaseUnit target, SkillEffect effect)>(targets.Count * Template.Effects.Count);
            foreach (var effect in Template.Effects)
            {
                var effectedTargets = new List<BaseUnit>();
                switch (effect.ApplicationMethod)
                {
                    case SkillEffectApplicationMethod.Target:
                        effectedTargets = targets;//keep target
                        break;
                    case SkillEffectApplicationMethod.Source:
                        effectedTargets.Add(caster);//Diff between Source and SourceOnce?
                        break;
                    case SkillEffectApplicationMethod.SourceOnce:
                        effectedTargets.Add(caster);//idk
                        break;
                    case SkillEffectApplicationMethod.SourceToPos:
                        effectedTargets = targets;
                        break;
                }

                foreach (var target in effectedTargets)
                {
                    if (effect.StartLevel > caster.Level || effect.EndLevel < caster.Level)
                    {
                        continue;
                    }

                    if (effect.Friendly && !effect.NonFriendly && caster.GetRelationStateTo(target) != RelationState.Friendly)
                    {
                        continue;
                    }

                    if (!effect.Friendly && effect.NonFriendly && caster.GetRelationStateTo(target) != RelationState.Hostile)
                    {
                        if (caster.GetRelationStateTo(target) == RelationState.Friendly && !caster.ForceAttack)
                        {
                            continue;
                        }
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
                    if (casterCaster is SkillItem castItem) // TODO Clean up. 
                    {
                        var castItemTemplate = ItemManager.Instance.GetTemplate(castItem.ItemTemplateId);
                        if ((castItemTemplate.UseSkillAsReagent) && (caster is Character player))
                            player.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents, castItemTemplate.Id, effect.ConsumeItemCount,null);
                        /*
                        var itemUsed = ItemManager.Instance.Create(castItem.ItemTemplateId, 1, 1, true);
                        var isRaegent = itemUsed.Template.UseSkillAsReagent;
                        if (isRaegent) //if item is a raegent
                        {
                            if (caster is Character player)
                            {
                                var items = player.Inventory.RemoveItem(castItem.ItemTemplateId, effect.ConsumeItemCount);
                                var tasks = new List<ItemTask>();
                                foreach (var (item, count) in items)
                                {
                                    InventoryHelper.RemoveItemAndUpdateClient(player, item, count, ItemTaskType.SkillReagents);
                                }
                            }
                        }
                        ItemManager.Instance.ReleaseId(itemUsed.Id);
                        */
                    }
                    if (caster is Character character && effect.ConsumeItemId != 0 && effect.ConsumeItemCount > 0)
                    {
                        if (effect.ConsumeSourceItem)
                        {
                            if (!character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.SkillEffectConsumption, 
                                effect.ConsumeItemId, effect.ConsumeItemCount))
                                continue;
                        }
                        else
                        {
                            var inventory = character.Inventory.CheckItems(SlotType.Inventory, effect.ConsumeItemId, effect.ConsumeItemCount);
                            var equipment = character.Inventory.CheckItems(SlotType.Equipment, effect.ConsumeItemId, effect.ConsumeItemCount);
                            if (!(inventory || equipment))
                            {
                                continue;
                            }

                            if (inventory)
                                character.Inventory.Bag.ConsumeItem(ItemTaskType.SkillEffectConsumption, effect.ConsumeItemId, effect.ConsumeItemCount,null);
                            else 
                            if (equipment)
                                character.Inventory.Equipment.ConsumeItem(ItemTaskType.SkillEffectConsumption, effect.ConsumeItemId, effect.ConsumeItemCount,null);
                        }
                    }

                    effectsToApply.Add((target, effect));
                    //effect.Template?.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.Now, packets);
                } 
            }
            foreach (var item in effectsToApply)
            {
                item.effect.Template.Apply(caster, casterCaster, item.target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.Now, packets);
            }
            // Quick Hack
            if (packets.Packets.Count > 0)
                caster.BroadcastPacket(packets, true);
            
            if (Template.ConsumeLaborPower > 0 && caster is Character chart)
            {
                chart.ChangeLabor((short)-Template.ConsumeLaborPower, Template.ActabilityGroupId);
            }

            if (Template.Cost > 0 && caster is Unit unit)
            {
                var baseCost = (((caster.GetAbLevel((AbilityType)Template.AbilityId)-1) * 1.6 + 8) * 3) / 3.65;
                var cost2 = baseCost * Template.ManaLevelMd + Template.ManaCost;
                var manaCost = (int)unit.Modifiers.ApplyModifiers(this, SkillAttribute.ManaCost, cost2);
                unit.ReduceCurrentMp(null, manaCost);
            }

            caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
            TlIdManager.Instance.ReleaseId(TlId);

            if (caster is Character character1 && character1.IgnoreSkillCooldowns)
                character1.ResetSkillCooldown(Template.Id, false);
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

            if (caster is Character character && character.IgnoreSkillCooldowns)
                character.ResetSkillCooldown(Template.Id, false);
            //TlId = 0;
        }

    }
}
