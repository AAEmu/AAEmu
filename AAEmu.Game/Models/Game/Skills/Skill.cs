﻿using System;
using System.Collections.Generic;
using System.Linq;
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
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Skills;

public class Skill
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public uint Id { get; set; }
    public SkillTemplate Template { get; set; }
    public byte Level { get; set; }
    public ushort TlId { get; set; }
    public PlotState ActivePlotState { get; set; }
    public Dictionary<uint, SkillHitType> HitTypes { get; set; }
    public BaseUnit InitialTarget { get; set; }//Temp Hack Fix. Replace this with UnitsEffected
    private bool _bypassGcd;
    public bool Cancelled { get; set; } = false;
    public Action Callback { get; set; }

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
            Level = template.LevelStep > 0 ? (byte)((owner.GetAbLevel((AbilityType)template.AbilityId) - template.AbilityLevel) / template.LevelStep + 1) : (byte)1;
        else
            Level = 1;
    }

    public SkillResult Use(BaseUnit caster, SkillCaster casterCaster, SkillCastTarget targetCaster, SkillObject skillObject = null, bool bypassGcd = false)
    {
        // Check if the source is a actual Unit
        if (caster is not Unit unit)
        {
            return SkillResult.InvalidSource;
        }

        // Cast character for future reference
        var character = caster as Character;

        unit.ConditionChance = true;

        _bypassGcd = bypassGcd;
        if (!_bypassGcd)
        {
            lock (unit.GCDLock)
            {
                // Commented out the line to eliminate the hanging of the skill
                // TODO: added for quest Id = 886 - скилл срабатывает часто, что не дает работать квесту - крысы не появляются
                var delay = 150;
                if (Id == 2 || Id == 3 || Id == 4)
                {
                    delay = character != null ? 500 : 1500;
                }

                if (unit.SkillLastUsed.AddMilliseconds(delay) > DateTime.UtcNow)
                {
                    // Will delay for 150 Milliseconds to eliminate the hanging of the skill
                    if (!caster.CheckInterval(delay))
                    {
                        Logger.Trace($"Skill: CooldownTime [{delay}]!");
                        return SkillResult.CooldownTime;
                    }
                }

                // Commented out the line to eliminate the hanging of the skill
                if (unit.GlobalCooldown >= DateTime.UtcNow && !Template.IgnoreGlobalCooldown)
                {
                    // Will delay for 50 Milliseconds to eliminate the hanging of the skill
                    if (!caster.CheckInterval(delay))
                    {
                        Logger.Trace($"Skill: CooldownTime [{delay}]!");
                        return SkillResult.CooldownTime;
                    }
                }

                unit.SkillLastUsed = DateTime.UtcNow;
            }
        }

        // Cancel buffs if Template asks for it
        if (Template.CancelOngoingBuffs)
        {
            if (caster is Units.Mate)
                caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.UseSkill, Template.CancelOngoingBuffExceptionTagId);
            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.StartSkill, Template.CancelOngoingBuffExceptionTagId);
        }

        // Create a new skillObject if needed
        skillObject ??= new SkillObject();

        // Grab current target
        var target = GetInitialTarget(caster, casterCaster, targetCaster);
        InitialTarget = target;
        if (target == null)
        {
            Logger.Debug($"Skill: SkillResult.NoTarget! - Skill {Template.Id}, Caster {caster.Name} ({caster.ObjId})");
            return SkillResult.NoTarget; // We should try to make sure this doesnt happen, but can happen with NPC skills
        }

        // Unmount character if skill asks for it
        if (character is { IsRiding: true } && Template.Unmount)
        {
            var mate = MateManager.Instance.GetActiveMate(character.ObjId);
            MateManager.Instance.UnMountMate(character, mate.TlId, AttachPointKind.Driver, AttachUnitReason.None);
        }

        // Check initial mana cost
        if (ManaCost(unit) > unit.Mp)
            return SkillResult.LackMana;

        // Get a TlId for this skill
        TlId = SkillTlIdManager.GetNextId(caster);
        // if (caster is Character)
        Logger.Trace($"Created SkillTlId {TlId} for Skill {Template.Id}, Caster {caster.Name} ({caster.ObjId}) with target {target.Name} ({target.ObjId})");

        // If skill uses Plots, then start the plot
        if (Template.Plot != null)
        {
            Task.Run(() => Template.Plot.Run(caster, casterCaster, target, targetCaster, skillObject, this));
            if (Template.PlotOnly)
                return SkillResult.Success;
        }

        // Check if target is within range
        var skillRange = caster.ApplySkillModifiers(this, SkillAttribute.Range, Template.MaxRange);
        var targetDist = unit.GetDistanceTo(target, true);

        var minRangeCheck = Template.MinRange * 1.0;
        var maxRangeCheck = skillRange;

        // HACKFIX : Used mostly for boats, since the actual position of the doodad is the boat's origin, and not where it is displayed
        // TODO: Do a check based on model size or bounding box instead

        // If weapon is used to calculate range, use that
        if (Template.WeaponSlotForRangeId > 0)
        {
            var minWeaponRange = 0.0f; // Fist default
            var maxWeaponRange = 3.0f; // Fist default
            if (unit.Equipment.GetItemBySlot(Template.WeaponSlotForRangeId)?.Template is WeaponTemplate weaponTemplate)
            {
                minWeaponRange = weaponTemplate.HoldableTemplate.MinRange;
                maxWeaponRange = weaponTemplate.HoldableTemplate.MaxRange;
            }

            minRangeCheck = minWeaponRange;
            maxRangeCheck = maxWeaponRange;
        }

        if (targetDist < minRangeCheck)
        {
            SkillTlIdManager.ReleaseId(TlId);
            TlId = 0;
            return SkillResult.TooCloseRange;
        }

        // TODO: Remove exception for doodads
        // TODO: Remove exceptions for slave initiated by Doodads (needed to fix repair points on ships)
        if ((targetDist > maxRangeCheck) && (target is not Doodad) && (target is not Slave))
        {
            SkillTlIdManager.ReleaseId(TlId);
            TlId = 0;
            return SkillResult.TooFarRange;
        }

        // Calculate casting time if needed
        var castTime = 0;
        if (Template.CastingTime > 0)
            castTime = (int)(unit.CastTimeMul * unit.SkillModifiersCache.ApplyModifiers(this, SkillAttribute.CastTime, Template.CastingTime));

        /*
        // TODO: Replace Old code
        else if (character != null && (Id == 2 || Id == 3 || Id == 4) && !caster.IsAutoAttack)
        {
            character.IsAutoAttack = true; // enable auto attack
            character.SkillId = Id;
            character.TlId = TlId;
            character.BroadcastPacket(new SCSkillStartedPacket(Id, 0, casterCaster, targetCaster, this, skillObject)
            {
                CastTime = Template.CastingTime
            }, true);
            character.AutoAttackTask = new MeleeCastTask(this, character, casterCaster, target, targetCaster, skillObject);
            TaskManager.Instance.Schedule(character.AutoAttackTask, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1300));
        }
        */

        if (castTime > 0)
        {
            // Has casting time, schedule a task for it
            caster.BroadcastPacket(new SCSkillStartedPacket(Id, TlId, casterCaster, targetCaster, this, skillObject)
            {
                CastTime = castTime
            }, true);

            unit.SkillTask = new CastTask(this, caster, casterCaster, target, targetCaster, skillObject);
            TaskManager.Instance.Schedule(unit.SkillTask, TimeSpan.FromMilliseconds(castTime));
        }
        else
        {
            // Immediate skill
            Cast(caster, casterCaster, target, targetCaster, skillObject);
        }

        return SkillResult.Success;
    }

    private BaseUnit GetInitialTarget(BaseUnit caster, SkillCaster skillCaster, SkillCastTarget targetCaster)
    {
        if (caster is not Unit unit) { return null; }
        var target = caster;
        if (targetCaster == null || skillCaster == null) // проверяем, так как иногда бывает null
        {
            return null;
        }
        // HACKFIX : Mounts and Turbulence
        if (skillCaster.Type == SkillCasterType.Mount || skillCaster.Type == SkillCasterType.Unit)
            target = WorldManager.Instance.GetUnit(skillCaster.ObjId);

        switch (Template.TargetType)
        {
            case SkillTargetType.Self:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    break;
                }
            case SkillTargetType.Friendly:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null && caster.GetRelationStateTo(target) != RelationState.Friendly)
                    {
                        return null; //TODO отправлять ошибку?
                    }

                    break;
                }
            case SkillTargetType.Hostile:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null && caster.GetRelationStateTo(target) != RelationState.Hostile)
                    {
                        if (!caster.CanAttack(target))
                        {
                            return null; //TODO отправлять ошибку?
                        }
                    }

                    break;
                }
            case SkillTargetType.AnyUnit:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    break;
                }
            case SkillTargetType.Doodad:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    break;
                }
            case SkillTargetType.Item:
                // TODO ...
                break;
            case SkillTargetType.Others:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null && caster.ObjId == target.ObjId)
                    {
                        return null; //TODO отправлять ошибку?
                    }

                    break;
                }
            case SkillTargetType.FriendlyOthers:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null && caster.ObjId == target.ObjId)
                    {
                        return null; //TODO отправлять ошибку?
                    }
                    if (caster.GetRelationStateTo(target) != RelationState.Friendly)
                    {
                        return null; //TODO отправлять ошибку?
                    }

                    break;
                }
            case SkillTargetType.Building:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? WorldManager.Instance.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null && caster.ObjId == target.ObjId)
                    {
                        return null; //TODO отправлять ошибку?
                    }

                    break;
                }
            case SkillTargetType.Pos:
                {
                    if (targetCaster is SkillCastPositionTarget positionTarget)
                    {
                        var positionUnit = new BaseUnit();
                        positionUnit.ObjId = uint.MaxValue;
                        positionUnit.Transform = caster.Transform.CloneDetached(positionUnit);
                        positionUnit.Transform.Local.SetPosition(positionTarget.PosX, positionTarget.PosY, positionTarget.PosZ);
                        caster.Region ??= WorldManager.Instance.GetRegion(caster.Transform.ZoneId, caster.Transform.World.Position.X, caster.Transform.World.Position.Y);
                        positionUnit.Region = caster.Region;
                        target = positionUnit;
                    }
                    if (target != null && caster.ObjId == target.ObjId)
                    {
                        return null; //TODO отправлять ошибку?
                    }
                    break;
                }
            case SkillTargetType.BallisticPos:
                {
                    if (targetCaster is SkillCastPositionTarget positionTarget)
                    {
                        var positionUnit = new BaseUnit();
                        positionUnit.ObjId = uint.MaxValue;
                        positionUnit.Transform = caster.Transform.CloneDetached(positionUnit);
                        positionUnit.Transform.Local.SetPosition(positionTarget.PosX, positionTarget.PosY, positionTarget.PosZ);
                        caster.Region ??= WorldManager.Instance.GetRegion(caster.Transform.ZoneId, caster.Transform.World.Position.X, caster.Transform.World.Position.Y);
                        target = positionUnit;
                    }
                    if (target != null && caster.ObjId == target.ObjId)
                    {
                        return null; //TODO отправлять ошибку?
                    }
                    break;
                }
            case SkillTargetType.Party:
                break;
            case SkillTargetType.Raid:
                break;
            case SkillTargetType.Line:
                break;
            case SkillTargetType.Pet:
                break;
            case SkillTargetType.SummonPos:
                break;
            case SkillTargetType.RelativePos:
                break;
            case SkillTargetType.SourcePos:
                break;
            case SkillTargetType.ArtilleryPos:
                {
                    if (targetCaster is SkillCastPosition3Target positionTarget)
                    {
                        var positionUnit = new BaseUnit();
                        positionUnit.ObjId = uint.MaxValue;
                        positionUnit.Transform = caster.Transform.CloneDetached(positionUnit);
                        positionUnit.Transform.Local.SetPosition(positionTarget.PosX, positionTarget.PosY, positionTarget.PosZ);
                        caster.Region ??= WorldManager.Instance.GetRegion(caster.Transform.ZoneId, caster.Transform.World.Position.X, caster.Transform.World.Position.Y);
                        positionUnit.Region = caster.Region;
                        target = positionUnit;
                    }
                    if (target != null && caster.ObjId == target.ObjId)
                    {
                        return null; //TODO отправлять ошибку?
                    }
                    break;
                }
            case SkillTargetType.CursorPos:
                break;
            default:
                throw new NotSupportedException($"SkillTargetType not supported {Template.TargetType}");
        }

        return target;
    }

    public void Cast(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (caster is not Unit unit) { return; }

        if (!_bypassGcd)
        {
            var gcd = Template.CustomGcd;
            if (Template.DefaultGcd)
                gcd = caster is Npc ? 1500 : 1000;

            unit.GlobalCooldown = DateTime.UtcNow.AddMilliseconds(gcd * (unit.GlobalCooldownMul / 100));
        }

        if (caster is Npc && Template.SkillControllerId != 0)
        {
            var scTemplate = SkillManager.Instance.GetEffectTemplate(Template.SkillControllerId, "SkillController") as SkillControllerTemplate;

            // Get a random number (from 0 to n)
            var value = Rand.Next(0, 1);
            // для skillId = 2 - for skillId = 2
            // 87 (35) - удар наотмаш, chr - overhead swing, chr
            // 2 (00) - удар сбоку, NPC - side strike, NPC
            // 3 (46) - удар сбоку, chr - side strike, chr
            // 1 (00) - удар похож на 2 удар сбоку, NPC - strike similar to 2, side strike, NPC
            // 91 - удар сверху (немного справа) - strike from above (slightly from the right)
            // 92 - удар наотмашь слева вниз направо - swing from left to right downwards
            // 0 - удар не наносится (расстояние большое и надо подойти поближе), no strike is made (distance is too great and need to get closer) f=1, c=15 
            var effectDelay = new Dictionary<int, short> { { 0, 46 }, { 1, 35 } };
            var fireAnimId = new Dictionary<int, int> { { 0, 3 }, { 1, 87 } };
            var effectDelay2 = new Dictionary<int, short> { { 0, 0 }, { 1, 0 } };
            var fireAnimId2 = new Dictionary<int, int> { { 0, 1 }, { 1, 2 } };

            var targetUnit = (Unit)target;
            var dist = MathUtil.CalculateDistance(caster.Transform.World.Position, targetUnit.Transform.World.Position, true);
            if (dist >= SkillManager.Instance.GetSkillTemplate(Id).MinRange && dist <= SkillManager.Instance.GetSkillTemplate(Id).MaxRange)
            {
                var sc = SkillController.CreateSkillController(scTemplate, caster, targetUnit);
#pragma warning disable CA1508 // Avoid dead conditional code
                if (sc != null)
                {
                    if (unit.ActiveSkillController != null)
                        unit.ActiveSkillController.End();
                    unit.ActiveSkillController = sc;
                    sc.Execute();
                }
#pragma warning restore CA1508 // Avoid dead conditional code
            }
        }
        unit.SkillTask = null;

        ConsumeMana(caster);
        unit.Cooldowns.AddCooldown(Template.Id, (uint)Template.CooldownTime);

        // if (Id == 2 || Id == 3 || Id == 4)
        // {
        //     if (caster is Character && caster.CurrentTarget == null)
        //     {
        //         StopSkill(caster);
        //         return;
        //     }
        //
        //     // Get a random number (from 0 to n)
        //     var value = Rand.Next(0, 1);
        //     // для skillId = 2
        //     // 87 (35) - удар наотмаш, chr
        //     //  2 (00) - удар сбоку, NPC
        //     //  3 (46) - удар сбоку, chr
        //     //  1 (00) - удар похож на 2 удар сбоку, NPC
        //     // 91 - удар сверху (немного справа)
        //     // 92 - удар наотмашь слева вниз направо
        //     //  0 - удар не наносится (расстояние большое и надо подойти поближе), f=1, c=15
        //     var effectDelay = new Dictionary<int, short> { { 0, 46 }, { 1, 35 } };
        //     var fireAnimId = new Dictionary<int, int> { { 0, 3 }, { 1, 87 } };
        //     var effectDelay2 = new Dictionary<int, short> { { 0, 0 }, { 1, 0 } };
        //     var fireAnimId2 = new Dictionary<int, int> { { 0, 1 }, { 1, 2 } };
        //
        //     var trg = (Unit)target;
        //     var dist = MathUtil.CalculateDistance(caster.Position, trg.Position, true);
        //     if (dist >= SkillManager.Instance.GetSkillTemplate(Id).MinRange && dist <= SkillManager.Instance.GetSkillTemplate(Id).MaxRange)
        //     {
        //         caster.BroadcastPacket(caster is Character
        //                 ? new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay[value], fireAnimId[value])
        //                 : new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay2[value], fireAnimId2[value]),
        //             true);
        //     }
        //     else
        //     {
        //         caster.BroadcastPacket(caster is Character
        //                 ? new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay[value], fireAnimId[value], false)
        //                 : new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject, effectDelay2[value], fireAnimId2[value], false),
        //             true);
        //
        //         if (caster is Character chr)
        //         {
        //             chr.SendMessage("Target is too far ...");
        //         }
        //         return;
        //     }
        // }

        // Validate cast Item
        if (caster is Character player && casterCaster is SkillItem castItem)
        {
            var castItemTemplate = ItemManager.Instance.GetTemplate(castItem.ItemTemplateId);
            if (castItemTemplate.UseSkillAsReagent)
            {
                var useItem = ItemManager.Instance.GetItemByItemId(castItem.ItemId);
                if (useItem == null)
                {
                    Logger.Warn("SkillItem does not exists {0} (templateId: {1})", castItem.ItemId, castItem.ItemTemplateId);
                    return; // Item does not exists
                }

                if (useItem._holdingContainer.OwnerId != player.Id)
                {
                    Logger.Warn("SkillItem {0} (itemId:{1}) is not owned by player {2} ({3})", useItem.Template.Name, useItem.Id, player.Name, player.Id);
                    return; // Item is not in the player's possessions
                }

                var itemCount = player.Inventory.GetItemsCount(useItem.TemplateId);
                var itemsRequired = 1; // TODO: This probably needs a check if it doesn't require multiple of source item to use, instead of just 1
                if (itemCount < itemsRequired)
                {
                    Logger.Warn("SkillItem, player does not own enough of {0} (count: {1}/{2}, templateId: {3})", useItem.Id, itemCount, itemsRequired, castItem.ItemTemplateId);
                    return; // not enough of item
                }
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

    /// <summary>
    /// Only used to stop/cancel base melee/ranged skills
    /// </summary>
    /// <param name="caster"></param>
    public async void StopSkill(BaseUnit caster)
    {
        if (caster is not Unit unit) { return; }
        await unit.AutoAttackTask.CancelAsync();
        caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
        caster.BroadcastPacket(new SCSkillStoppedPacket(unit.ObjId, Id), true);
        unit.AutoAttackTask = null;
        unit.IsAutoAttack = false; // turned off auto attack
        SkillTlIdManager.ReleaseId(TlId);
        TlId = 0;
    }

    public void StartChanneling(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (caster is not Unit unit) { return; }
        if (Template.ChannelingBuffId != 0)
        {
            var buff = SkillManager.Instance.GetBuffTemplate(Template.ChannelingBuffId);
            buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow);
        }

        if (Template.ChannelingTargetBuffId != 0)
        {
            var buff = SkillManager.Instance.GetBuffTemplate(Template.ChannelingTargetBuffId);
            buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow);
        }

        Doodad doodad = null;
        if (Template.ChannelingDoodadId > 0)
        {
            doodad = DoodadManager.Instance.Create(0, Template.ChannelingDoodadId, caster, true);
            doodad.Transform = caster.Transform.CloneDetached(doodad);
            doodad.InitDoodad();
            doodad.Spawn();
        }

        // TODO: добавил, так как для квеста 3469 нет события OnItemUse
        // TODO: added since there is no OnItemUse event for quest 3469
        if (casterCaster is SkillItem { ItemTemplateId: > 0 } item && caster is Character player)
        {
            player.Inventory.ConsumeItem(null, ItemTaskType.SkillReagents, item.ItemTemplateId, 1, null);
        }

        caster.BroadcastPacket(new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject), true);
        unit.SkillTask = new EndChannelingTask(this, caster, casterCaster, target, targetCaster, skillObject, doodad);
        TaskManager.Instance.Schedule(unit.SkillTask, TimeSpan.FromMilliseconds(Template.ChannelingTime));
    }

    public void EndChanneling(BaseUnit caster, Doodad channelDoodad)
    {
        if (caster is not Unit unit) { return; }
        unit.SkillTask = null;
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

        unit.Events.OnChannelingCancel(this, new OnChannelingCancelArgs());
    }

    public void ScheduleEffects(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (caster is not Unit unit) { return; }
        if (Template.ToggleBuffId != 0)
        {
            var buff = SkillManager.Instance.GetBuffTemplate(Template.ToggleBuffId);
            buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow);
        }

        var totalDelay = 0;
        if (Template.EffectDelay > 0)
            totalDelay += Template.EffectDelay;
        if (Template.EffectSpeed > 0)
            totalDelay += (int)(unit.GetDistanceTo(target) / Template.EffectSpeed * 1000.0f);
        if (Template.FireAnim != null && Template.UseAnimTime)
            totalDelay += (int)(Template.FireAnim.CombatSyncTime * (unit.GlobalCooldownMul / 100));


        caster.BroadcastPacket(new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject)
        {
            ComputedDelay = (short)totalDelay
        }, true);

        if (totalDelay > 0)
        {
            var thisSkillTask = new ApplySkillTask(this, caster, casterCaster, target, targetCaster, skillObject);
            TaskManager.Instance.Schedule(thisSkillTask, TimeSpan.FromMilliseconds(totalDelay));
        }
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

    public void ApplyEffects(BaseUnit caster, SkillCaster casterCaster, BaseUnit targetSelf, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (caster is not Unit unit)
            return;
        var player = caster as Character;
        var targets = new List<BaseUnit>(); // TODO crutches
        if (Template.TargetAreaRadius > 0)
        {
            var units = WorldManager.GetAround<BaseUnit>(targetSelf, Template.TargetAreaRadius, true);
            units.Add(targetSelf); // Add main target as well
            units = FilterAoeUnits(caster, units).ToList();

            targets.AddRange(units);
            // TODO : Need to check if this is needed
            //if (targetSelf is Unit) targets.Add(targetSelf);
        }
        else
        {
            targets.Add(targetSelf);
        }

        foreach (var target in targets)
        {
            if (target is Unit targetUnit && Template.TargetType == SkillTargetType.Hostile)
            {
                var diceResult = RollCombatDice(caster, targetUnit);
                if (Template.LevelRuleNoConsideration)
                {
                    var damageType = (DamageType)Template.DamageTypeId;
                    switch (damageType)
                    {
                        case DamageType.Melee:
                            diceResult = SkillHitType.MeleeHit;
                            break;
                        case DamageType.Magic:
                            diceResult = SkillHitType.SpellHit;
                            break;
                        case DamageType.Siege:
                            diceResult = SkillHitType.RangedHit; // no siege version?
                            break;
                        case DamageType.Ranged:
                            diceResult = SkillHitType.RangedHit;
                            break;
                        case DamageType.Heal:
                            diceResult = SkillHitType.SpellHit;
                            break;
                        default:
                            diceResult = SkillHitType.Invalid;
                            break;
                    }
                }
                HitTypes.TryAdd(targetUnit.ObjId, diceResult);
            }
            else if (target is Doodad doodad)
            {
                doodad.OnSkillHit(caster, Id);
            }
        }

        var packets = new CompressedGamePackets();
        var consumedItems = new List<(Item, int)>();
        var consumedItemTemplates = new List<(uint, int)>(); // itemTemplateId, amount

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
                    // TODO: HACKFIX for owner's mark
                    if (casterCaster.Type == SkillCasterType.Mount && targetSelf is Units.Mate || targetSelf is Slave)
                        effectedTargets = targets;
                    else
                        effectedTargets.Add(caster);//idk
                    break;
                case SkillEffectApplicationMethod.SourceToPos:
                    effectedTargets = targets;
                    break;
            }

            foreach (var target in effectedTargets)
            {
                var relationState = caster.GetRelationStateTo(target);
                if (effect.StartLevel > unit.Level || effect.EndLevel < unit.Level)
                {
                    continue;
                }

                if (effect.Friendly && !effect.NonFriendly && relationState != RelationState.Friendly)
                {
                    continue;
                }

                if (!effect.Friendly && effect.NonFriendly && relationState != RelationState.Hostile)
                {
                    if (relationState == RelationState.Friendly && !unit.ForceAttack || caster.ObjId == target.ObjId)
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
                    // TODO Commented out the code for the Id=2255 quest to work. Restore after finding a solution to the lack of a debuff.
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

                if (casterCaster is SkillItem castItem && player != null)
                {
                    var useItem = ItemManager.Instance.GetItemByItemId(castItem.ItemId);
                    if (effect.ConsumeSourceItem)
                        consumedItems.Add((useItem, effect.ConsumeItemCount));
                    else
                    {
                        var castItemTemplate = ItemManager.Instance.GetTemplate(castItem.ItemTemplateId);
                        if (castItemTemplate.UseSkillAsReagent)
                            consumedItems.Add((useItem, effect.ConsumeItemCount));
                    }
                }

                if (player != null && effect.ConsumeItemId != 0 && effect.ConsumeItemCount > 0)
                {
                    if (effect.ConsumeSourceItem)
                    {
                        if (!player.Inventory.Bag.AcquireDefaultItem(ItemTaskType.SkillEffectConsumption,
                            effect.ConsumeItemId, effect.ConsumeItemCount))
                            continue;
                    }
                    else
                    {
                        var inventory = player.Inventory.CheckItems(SlotType.Inventory, effect.ConsumeItemId, effect.ConsumeItemCount);
                        var equipment = player.Inventory.CheckItems(SlotType.Equipment, effect.ConsumeItemId, effect.ConsumeItemCount);
                        if (!(inventory || equipment))
                        {
                            continue;
                        }

                        consumedItemTemplates.Add((effect.ConsumeItemId, effect.ConsumeItemCount));
                    }
                }

                effectsToApply.Add((target, effect));
                //effect.Template?.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow, packets);
            }
        }

        //This will handle all items with a reagent/product
        var reagents = SkillManager.Instance.GetSkillReagentsBySkillId(Template.Id);
        var skillProducts = SkillManager.Instance.GetSkillProductsBySkillId(Template.Id);
        if (reagents.Count > 0 || skillProducts.Count > 0)
        {
            if (player != null)
            {
                if (reagents.Count > 0)
                {
                    foreach (var reagent in reagents)
                    {
                        var consumeCount = player.Inventory.Bag.ConsumeItem(ItemTaskType.SkillReagents, reagent.ItemId, reagent.Amount, null);
                        if (consumeCount < reagent.Amount)
                        {
                            player.Inventory.Equipment.ConsumeItem(ItemTaskType.SkillReagents, reagent.ItemId, reagent.Amount, null);
                        }
                    }
                }

                if (skillProducts.Count > 0)
                {
                    foreach (var product in skillProducts)
                    {
                        player.Inventory.Bag.AcquireDefaultItem(ItemTaskType.SkillEffectGainItem, product.ItemId, product.Amount);
                    }
                }
            }
        }

        foreach (var item in effectsToApply)
        {
            //Template can be null for some reason..
            if (item.effect.Template != null)
                item.effect.Template.Apply(caster, casterCaster, item.target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow, packets);
            else
                Logger.Error($"Template not found for Skill[{Template.Id}] Effect[{item.effect.EffectId}]");
        }

        // TODO Call OnItemUse() moved to the ApplyEffects() method from the effects and add trigger ConditionChance;
        // If the probability of passing the effect is greater than the chance, then run the check on the use of the item for the quest
        if (casterCaster is SkillItem skillItem && unit.ConditionChance)
        {
            if (player == null)
                return;
            player.ItemUse(skillItem.ItemId);

            // This fixes the issue where "dropping" a Portable Harpoon Cannon (item 23836) would not consume the cannon
            // Related skill Discard Portable Harpoon Cannon (skill 17735) has no reagents attached
            // The item however is marked with use_skill_as_reagent, so if it requires reagent according to the item
            // but has none attached, consume 1 of the source item instead
            // TODO: Check if this is intended behaviour, or if this is a bug in the compact.sqlite3 file
            var item = ItemManager.Instance.GetItemByItemId(skillItem.ItemId);
            if ((item?.Template.UseSkillAsReagent == true) && (reagents.Count <= 0) && (skillProducts.Count <= 0) && (consumedItems.Count <= 0))
            {
                consumedItems.Add((item, 1));
                Logger.Debug($"Consumed item template 1 x {item.TemplateId} ({item.Id}) because of missing reagent information with skill {Template.Id}");
            }
        }

        // Quick Hack
        if (packets.Packets.Count > 0)
            caster.BroadcastPacket(packets, true);

        if (!Cancelled)
        {
            if (player != null)
            {
                // Actually consume the to be consumed items
                // Specific Items
                foreach (var (item, amount) in consumedItems)
                    if (item._holdingContainer != null)
                    {
                        item._holdingContainer.ConsumeItem(ItemTaskType.SkillReagents, item.TemplateId, amount, item);
                    }

                // Doesn't matter, but by Template
                foreach (var (templateId, amount) in consumedItemTemplates)
                    player.Inventory.ConsumeItem(null, ItemTaskType.SkillEffectConsumption, templateId,
                        amount, null);
            }
        }
    }

    /// <summary>
    /// End skill in a normal way
    /// </summary>
    /// <param name="caster"></param>
    public void EndSkill(BaseUnit caster)
    {
        if (caster is not Unit unit)
            return;

        if (caster is Character character)
        {
            if (Template.ConsumeLaborPower > 0 && !Cancelled)
            {
                // Consume labor
                character.ChangeLabor((short)-Template.ConsumeLaborPower, Template.ActabilityGroupId);
            }

            // Add vocation where needed
            if ((Template.GainLifePoint > 0) && !Cancelled)
            {
                // We multiply the BASE value for server settings, not the total (although I don't think this would affect anything since we don't really have a +1 badge/action buff)
                character.ChangeGamePoints(GamePointKind.Vocation, (int)Math.Ceiling(AppConfiguration.Instance.World.VocationRate * Template.GainLifePoint));
            }
        }

        Callback?.Invoke();
        unit.OnSkillEnd(this);
        caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
        SkillTlIdManager.ReleaseId(TlId);
        TlId = 0;

        if (caster is Character character1 && character1.IgnoreSkillCooldowns)
            character1.ResetSkillCooldown(Template.Id, false);
    }

    /// <summary>
    /// Used for interrupting skills
    /// </summary>
    /// <param name="caster"></param>
    /// <param name="channelDoodad"></param>
    public void Stop(BaseUnit caster, Doodad channelDoodad = null)
    {
        if (caster is not Unit unit) { return; }
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
        Callback?.Invoke();
        unit.OnSkillEnd(this);
        unit.SkillTask = null;
        Cancelled = true;
        SkillTlIdManager.ReleaseId(TlId);
        TlId = 0;

        if (caster is Character character && character.IgnoreSkillCooldowns)
            character.ResetSkillCooldown(Template.Id, false);
    }

    public SkillHitType RollCombatDice(BaseUnit attacker, BaseUnit target)
    {
        var Attacker = attacker as Unit;
        var Target = target as Unit;
        // TODO
        //  -Calculate Hit/Miss Rates
        //  -Check for AlwaysHit?
        //  -Only Parry if sword equipped?
        var damageType = (DamageType)Template.DamageTypeId;
        if (Attacker != null)
        {
            var bullsEyeMod = Attacker.BullsEye / 1000f * 3f / 100f;

            //TODO Check immunity a better way!!!
            //if (target.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(361)))
            //return SkillHitType.Immune;

            //Idk if this is right. Double check it
            if (!MathUtil.IsFront(attacker, target))
                goto AlwaysHit;

            if (Target != null && Rand.Next(0f, 100f) < Target.DodgeRate - bullsEyeMod)
            {
                if (damageType == DamageType.Melee)
                    return SkillHitType.MeleeDodge;
                if (damageType == DamageType.Ranged)
                    return SkillHitType.RangedDodge;
            }
            if (Target != null && Rand.Next(0f, 100f) < Target.BlockRate - bullsEyeMod)
            {
                if (damageType == DamageType.Melee)
                    return SkillHitType.MeleeBlock;
                if (damageType == DamageType.Ranged)
                    return SkillHitType.RangedBlock;
            }
            if (Target != null && Rand.Next(0F, 100f) < Target.MeleeParryRate - bullsEyeMod)
            {
                if (damageType == DamageType.Melee)
                    return SkillHitType.MeleeParry;
                if (damageType == DamageType.Ranged
                    && target.Buffs.CheckBuff((uint)BuffConstants.EquipDualwield)
                    && target.Buffs.CheckBuff((uint)BuffConstants.DualwieldProficiency))
                {
                    return SkillHitType.MeleeParry;
                }
            }
            if (Target != null && Rand.Next(0f, 100f) < Target.RangedParryRate - bullsEyeMod)
            {
                if (damageType == DamageType.Ranged)
                    return SkillHitType.RangedParry;
            }
        }

AlwaysHit:
        switch (damageType)
        {
            case DamageType.Melee:
                if (Attacker != null && Rand.Next(0f, 100f) < Attacker.MeleeAccuracy)
                    return SkillHitType.MeleeHit;
                return SkillHitType.MeleeMiss;
            case DamageType.Magic:
                if (Attacker != null && Rand.Next(0f, 100f) < Attacker.SpellAccuracy)
                    return SkillHitType.SpellHit;
                return SkillHitType.SpellMiss;
            case DamageType.Ranged:
                if (Attacker != null && Rand.Next(0f, 100f) < Attacker.RangedAccuracy)
                    return SkillHitType.RangedHit;
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
        Logger.Error($"Unit[{objId}] was not found in the CbtDiceRolls.");
        return true;
    }

    /// <summary>
    /// Gets the amount of a Mana a skill would use with the caster's modifiers applied
    /// </summary>
    /// <param name="caster"></param>
    /// <returns></returns>
    public int ManaCost(Unit caster)
    {
        var baseCost = ((caster.GetAbLevel((AbilityType)Template.AbilityId) - 1) * 1.6 + 8) * 3 / 3.65;
        var cost2 = baseCost * Template.ManaLevelMd + Template.ManaCost;
        var manaCost = (int)caster.SkillModifiersCache.ApplyModifiers(this, SkillAttribute.ManaCost, cost2);
        return manaCost;
    }

    public void ConsumeMana(BaseUnit caster)
    {
        if (caster is not Unit unit)
            return;

        var manaCost = ManaCost(unit);
        unit.ReduceCurrentMp(null, manaCost);

        if (caster is not Character character)
            return;

        character.LastCast = DateTime.UtcNow;
        character.IsInPostCast = true;
    }
}
