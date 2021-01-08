using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
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
        public Dictionary<uint, SkillHitType> HitTypes { get; set; }
        public BaseUnit InitialTarget { get; set; }//Temp Hack Fix. Replace this with UnitsEffected
        private bool _bypassGcd;
        public bool Cancelled { get; set; } = false;

        //public bool isAutoAttack;
        //public SkillTask autoAttackTask;

        public Skill()
        {
            HitTypes = new Dictionary<uint, SkillHitType>();
        }

        public Skill(SkillTemplate template, Unit owner = null)
        {
            HitTypes = new Dictionary<uint, SkillHitType>();
            Id = template.Id;
            Template = template;
            if (owner != null)
                Level = (template.LevelStep > 0 ? (byte)(((owner.GetAbLevel((AbilityType)template.AbilityId) - (template.AbilityLevel)) / template.LevelStep) + 1) : (byte)1);
            else
                Level = 1;
        }
        public void Use(Unit caster, SkillCaster casterCaster, SkillCastTarget targetCaster, SkillObject skillObject = null, bool bypassGcd = false)
        {
            _bypassGcd = bypassGcd;
            if (!_bypassGcd)
            {
                lock (caster.GCDLock)
                {
                    if (caster.SkillLastUsed.AddMilliseconds(150) > DateTime.Now)
                        return;

                    if (caster.GlobalCooldown >= DateTime.Now && !Template.IgnoreGlobalCooldown)
                        return;

                    caster.SkillLastUsed = DateTime.Now;
                }
            }

            if (Template.CancelOngoingBuffs)
                caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.StartSkill, Template.CancelOngoingBuffExceptionTagId);

            if (skillObject == null)
            {
                skillObject = new SkillObject();
            }

            var target = GetInitialTarget(caster, casterCaster, targetCaster);
            InitialTarget = target;
            if (target == null)
                return;//We should try to make sure this doesnt happen

            TlId = SkillManager.Instance.NextId();
            if (Template.Plot != null)
            {
                Task.Run(() => Template.Plot.Run(caster, casterCaster, target, targetCaster, skillObject, this));
                if (Template.PlotOnly)
                    return;
            }

            // TODO : Add check for range
            var skillRange = caster.ApplySkillModifiers(this, SkillAttribute.Range, Template.MaxRange);

            // var effects = caster.Buffs.GetEffectsByType(typeof(BuffTemplate));
            // foreach (var effect in effects)
            // {
            //     if (((BuffTemplate)effect.Template).RemoveOnStartSkill || ((BuffTemplate)effect.Template).RemoveOnUseSkill)
            //     {
            //         effect.Exit();
            //     }
            // }
            // effects = caster.Buffs.GetEffectsByType(typeof(BuffEffect));
            // foreach (var effect in effects)
            // {
            //     if (effect.Template.RemoveOnStartSkill)
            //     {
            //         effect.Exit();
            //     }
            // }

            //Maybe we should do this somewhere else?
            if (Template.DefaultGcd)
            {
                //TODO Apply Attack Spped * GCD
                if (!_bypassGcd)
                    caster.GlobalCooldown = DateTime.Now.AddMilliseconds(1000 * (caster.GlobalCooldownMul / 100));
            }
            else
            {
                //TODO Apply Attack Speed * GCD
                if (!_bypassGcd)
                    caster.GlobalCooldown = 
                        DateTime.Now.AddMilliseconds(Template.CustomGcd * (caster.GlobalCooldownMul / 100));
            }
            
            if (Template.CastingTime > 0)
            {
                // var origTime = Template.CastingTime * caster.Cas
                var castTime = (int)(caster.CastTimeMul *
                    caster.SkillModifiersCache.ApplyModifiers(this, SkillAttribute.CastTime, Template.CastingTime));

                if (caster is Character chara)
                {
                }
                
                if (castTime < 0)
                    castTime = 0;
                
                caster.BroadcastPacket(new SCSkillStartedPacket(Id, TlId, casterCaster, targetCaster, this, skillObject)
                {
                    CastTime = castTime
                }, true);
                
                caster.SkillTask = new CastTask(this, caster, casterCaster, target, targetCaster, skillObject);
                TaskManager.Instance.Schedule(caster.SkillTask, TimeSpan.FromMilliseconds(castTime));
            }
            else if (caster is Character && (Id == 2 || Id == 3 || Id == 4) && !caster.IsAutoAttack)
            {
                caster.IsAutoAttack = true; // enable auto attack
                caster.SkillId = Id;
                caster.TlId = TlId;
                caster.BroadcastPacket(new SCSkillStartedPacket(Id, 0, casterCaster, targetCaster, this, skillObject)
                {
                    CastTime = Template.CastingTime
                }, true);

                caster.AutoAttackTask = new MeleeCastTask(this, caster, casterCaster, target, targetCaster, skillObject);
                TaskManager.Instance.Schedule(caster.AutoAttackTask, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1300));
            }
            else
            {
                Cast(caster, casterCaster, target, targetCaster, skillObject);
            }
        }

        private BaseUnit GetInitialTarget(Unit caster, SkillCaster skillCaster, SkillCastTarget targetCaster)
        {
            var target = (BaseUnit)caster;
            // HACKFIX : Mounts and Turbulence
            if (skillCaster.Type == SkillCasterType.Unk3 || ((caster == null) && (skillCaster.Type == SkillCasterType.Unit)))
                target = WorldManager.Instance.GetUnit(skillCaster.ObjId);

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
            } else if (Template.TargetType == SkillTargetType.BallisticPos)
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
            
            ConsumeMana(caster);

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

            if (Template.ChannelingTime > 0)
            {
                StartChanneling(caster, casterCaster, target, targetCaster, skillObject);
            }
            else
            {
                ScheduleEffects(caster, casterCaster, target, targetCaster, skillObject);
            }
        }

        public async void StopSkill(Unit caster)
        {;
            await caster.AutoAttackTask.Cancel();
            caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
            caster.BroadcastPacket(new SCSkillStoppedPacket(caster.ObjId, Id), true);
            caster.AutoAttackTask = null;
            caster.IsAutoAttack = false; // turned off auto attack
            SkillManager.Instance.ReleaseId(TlId);
        }

        public void StartChanneling(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            if (Template.ChannelingBuffId != 0)
            {
                var buff = SkillManager.Instance.GetBuffTemplate(Template.ChannelingBuffId);
                buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.Now);
            }
            
            if (Template.ChannelingTargetBuffId != 0)
            {
                var buff = SkillManager.Instance.GetBuffTemplate(Template.ChannelingTargetBuffId);
                buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.Now);
            }

            Doodad doodad = null;
            if (Template.ChannelingDoodadId > 0)
            {
                doodad = DoodadManager.Instance.Create(0, Template.ChannelingDoodadId, caster);
                doodad.Position = caster.Position.Clone();
                doodad.Spawn();
            }
            
            caster.BroadcastPacket(new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject), true);
            caster.SkillTask = new EndChannelingTask(this, caster, casterCaster, target, targetCaster, skillObject, doodad);
            TaskManager.Instance.Schedule(caster.SkillTask, TimeSpan.FromMilliseconds(Template.ChannelingTime));
        }
        
        public void EndChanneling(Unit caster, Doodad channelDoodad)
        {
            caster.SkillTask = null;
            if (Template.ChannelingBuffId != 0)
            {
                caster.Buffs.RemoveEffect(Template.ChannelingBuffId, Template.Id);
            }
            if (Template.ChannelingTargetBuffId != 0)
            {
                InitialTarget.Buffs.RemoveEffect(Template.ChannelingTargetBuffId, Template.Id);
            }

            channelDoodad?.Delete();

            EndSkill(caster);

            caster.Events.OnChannelingCancel(this, new OnChannelingCancelArgs());
        }
        
        public void ScheduleEffects(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
        {
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
                totalDelay += (int)(Template.FireAnim.CombatSyncTime * (caster.GlobalCooldownMul / 100));
            
            
            caster.BroadcastPacket(new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject)
            {
                ComputedDelay = (short) totalDelay
            }, true);
            
            if (totalDelay > 0) 
                TaskManager.Instance.Schedule(new ApplySkillTask(this, caster, casterCaster, target, targetCaster, skillObject), TimeSpan.FromMilliseconds(totalDelay));
            else
            {
                ApplyEffects(caster, casterCaster, target, targetCaster, skillObject);
                EndSkill(caster);
            }

        }

        private IEnumerable<BaseUnit> FilterAoeUnits(BaseUnit caster, IEnumerable<BaseUnit> units)
        {
            units = SkillTargetingUtil.FilterWithRelation(Template.TargetRelation, caster, units);
            return units;
        }

        public void ApplyEffects(Unit caster, SkillCaster casterCaster, BaseUnit targetSelf, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            var targets = new List<BaseUnit>(); // TODO crutches
            if (Template.TargetAreaRadius > 0)
            {
                var units = WorldManager.Instance.GetAround<BaseUnit>(targetSelf, Template.TargetAreaRadius, true);
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

            foreach(var target in targets)
            {
                if (target is Unit trg && Template.TargetType == SkillTargetType.Hostile)
                {
                    HitTypes.TryAdd(trg.ObjId, RollCombatDice(caster, trg));
                }
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
                    var relationState = caster.GetRelationStateTo(target);
                    if (effect.StartLevel > caster.Level || effect.EndLevel < caster.Level)
                    {
                        continue;
                    }

                    if (effect.Friendly && !effect.NonFriendly && relationState != RelationState.Friendly)
                    {
                        continue;
                    }

                    if (!effect.Friendly && effect.NonFriendly && relationState != RelationState.Hostile)
                    {
                        if ((relationState == RelationState.Friendly && !caster.ForceAttack) || caster.ObjId == target.ObjId)
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

                    if (effect.SourceBuffTagId > 0 && !caster.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.SourceBuffTagId)))
                    {
                        continue;
                    }

                    if (effect.SourceNoBuffTagId > 0 && caster.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.SourceNoBuffTagId)))
                    {
                        continue;
                    }

                    if (effect.TargetBuffTagId > 0 && !target.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.TargetBuffTagId)))
                    {
                        continue;
                    }

                    if (effect.TargetNoBuffTagId > 0 && target.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.TargetNoBuffTagId)))
                    {
                        continue;
                    }

                    if (effect.Chance < 100 && Rand.Next(100) > effect.Chance)
                    {
                        continue;
                    }

                    if (casterCaster is SkillItem castItem) // TODO Clean up. 
                    {
                        var player = (Character)caster;
                        if(effect.ConsumeSourceItem)
                            player.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents, castItem.ItemTemplateId, effect.ConsumeItemCount, null);

                        var castItemTemplate = ItemManager.Instance.GetTemplate(castItem.ItemTemplateId);
                        if ((castItemTemplate.UseSkillAsReagent) && player != null)
                            player.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents, castItemTemplate.Id, effect.ConsumeItemCount,null);
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

            if(casterCaster is SkillItem skillItem) //This will handle all items with a reagent/product
            {
                var reagents = SkillManager.Instance.GetSkillReagentsBySkillId(Template.Id);
                var skillProducts = SkillManager.Instance.GetSkillProductsBySkillId(Template.Id);
                var player = (Character)caster;

                if(reagents.Count > 0)
                {
                    foreach (var reagent in reagents)
                    {
                        player.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents, reagent.ItemId, reagent.Amount, null);
                    }
                }

                if(skillProducts.Count > 0)
                {
                    foreach (var product in skillProducts)
                    {
                        player.Inventory.Bag.AcquireDefaultItem(ItemTaskType.SkillEffectGainItem, product.ItemId, product.Amount);
                    }
                }
            }

            foreach (var item in effectsToApply)
            {
                item.effect.Template.Apply(caster, casterCaster, item.target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.Now, packets);
            }
            // Quick Hack
            if (packets.Packets.Count > 0)
                caster.BroadcastPacket(packets, true);
        }

        public void EndSkill(Unit caster)
        {
            if (Template.ConsumeLaborPower > 0 && caster is Character chart)
            {
                chart.ChangeLabor((short)-Template.ConsumeLaborPower, Template.ActabilityGroupId);
            }

            caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
            SkillManager.Instance.ReleaseId(TlId);

            if (caster is Character character1 && character1.IgnoreSkillCooldowns)
                character1.ResetSkillCooldown(Template.Id, false);
        }

        public void Stop(Unit caster, Doodad channelDoodad = null)
        {
            if (Template.ChannelingTime > 0)
            {
                EndChanneling(caster, channelDoodad);
            }

            if (Template.ToggleBuffId != 0)
            {
                caster.Buffs.RemoveEffect(Template.ToggleBuffId, Template.Id);
            }
            caster.BroadcastPacket(new SCCastingStoppedPacket(TlId, 0), true);
            caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
            caster.SkillTask = null;
            Cancelled = true;
            SkillManager.Instance.ReleaseId(TlId);

            if (caster is Character character && character.IgnoreSkillCooldowns)
                character.ResetSkillCooldown(Template.Id, false);
            //TlId = 0;
        }

        public SkillHitType RollCombatDice(Unit attacker, Unit target)
        {
            // TODO
            //  -Calculate Hit/Miss Rates
            //  -Check for AlwaysHit?
            //  -Only Parry if sword equipped?
            var damageType = (DamageType)Template.DamageTypeId;
            var bullsEyeMod = (((attacker.BullsEye / 1000f) * 3f) / 100f);

            if (target.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(361)))
                return SkillHitType.Immune;

            //Idk if this is right. Double check it
            if (!MathUtil.IsFront(attacker, target))
                goto AlwaysHit;

            if (Rand.Next(0f, 100f) < (target.DodgeRate - bullsEyeMod))
            {
                if (damageType == DamageType.Melee)
                    return SkillHitType.MeleeDodge;
                else if (damageType == DamageType.Ranged)
                    return SkillHitType.RangedDodge;
            }
            if (Rand.Next(0f, 100f) < (target.BlockRate - bullsEyeMod))
            {
                if (damageType == DamageType.Melee)
                    return SkillHitType.MeleeBlock;
                else if (damageType == DamageType.Ranged)
                    return SkillHitType.RangedBlock;
            }
            if (Rand.Next(0F, 100f) < (target.MeleeParryRate - bullsEyeMod))
            {
                if (damageType == DamageType.Melee)
                    return SkillHitType.MeleeParry;
                if(damageType == DamageType.Ranged 
                    && target.Buffs.CheckBuff((uint)BuffConstants.EQUIP_DUALWIELD_BUFF)
                    && target.Buffs.CheckBuff((uint)BuffConstants.DUALWIELD_PROFICIENCY))
                {
                    return SkillHitType.MeleeParry;
                }
            }
            if (Rand.Next(0f, 100f) < (target.RangedParryRate - bullsEyeMod))
            {
                if (damageType == DamageType.Ranged)
                    return SkillHitType.RangedParry;
            }

            AlwaysHit:
            switch (damageType)
            {
                case DamageType.Melee:
                    if (Rand.Next(0f, 100f) < attacker.MeleeAccuracy)
                        return SkillHitType.MeleeHit;
                    else
                        return SkillHitType.MeleeMiss;
                case DamageType.Magic:
                    if (Rand.Next(0f, 100f) < attacker.SpellAccuracy)
                        return SkillHitType.SpellHit;
                    else
                        return SkillHitType.SpellMiss;
                case DamageType.Ranged:
                    if (Rand.Next(0f, 100f) < attacker.RangedAccuracy)
                        return SkillHitType.RangedHit;
                    else
                        return SkillHitType.RangedMiss;
                case DamageType.Siege:
                    return SkillHitType.RangedHit;//No siege type?
                default:
                    return SkillHitType.Invalid;
            }
        }

        public bool SkillMissed(uint objId)
        {
            if (HitTypes.TryGetValue(objId, out var hitType))
            {
                return hitType == SkillHitType.MeleeDodge
                    || hitType == SkillHitType.MeleeParry
                    || hitType == SkillHitType.MeleeBlock
                    || hitType == SkillHitType.MeleeMiss
                    || hitType == SkillHitType.RangedDodge
                    || hitType == SkillHitType.RangedParry
                    || hitType == SkillHitType.RangedBlock
                    || hitType == SkillHitType.RangedMiss
                    || hitType == SkillHitType.Immune;
            }
            _log.Error($"Unit[{objId}] was not found in the CbtDiceRolls.");
            return true;
        }

        public void ConsumeMana(Unit caster)
        {
            var baseCost = (((caster.GetAbLevel((AbilityType)Template.AbilityId)-1) * 1.6 + 8) * 3) / 3.65;
            var cost2 = baseCost * Template.ManaLevelMd + Template.ManaCost;
            var manaCost = (int)caster.SkillModifiersCache.ApplyModifiers(this, SkillAttribute.ManaCost, cost2);
            caster.ReduceCurrentMp(null, manaCost);
            if (caster is Character character)
            {
                character.LastCast = DateTime.Now;
                character.IsInPostCast = true;
            }
        }

    }
}
