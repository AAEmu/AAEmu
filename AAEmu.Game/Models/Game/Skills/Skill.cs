using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
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

#pragma warning disable IDE0079 // Remove unnecessary suppression

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
    /// <summary>ZoneAuthority: avoid double WZSkillStarted (cast-time relays at Use, instant at Cast).</summary>
    private bool _zoneSkillStartedRelayed;
    private bool _zoneSkillFiredRelayed;
    private bool _zoneSkillEndedRelayed;
    private SkillCaster _zoneSkillCaster;
    public bool Cancelled { get; set; } = false;
    public Action Callback { get; set; }

    /// <summary>
    /// Multiplier that can be added as an additional modifier to casting times
    /// </summary>
    public float CastTimeMultiplier { get; set; } = 1f;

    /// <summary>Counter for auto-attack animation cycling (incremented each attack)</summary>
    public int AutoAttackIndex { get; set; }

    public Skill()
    {
        HitTypes = [];
    }

    public Skill(SkillTemplate template, Unit owner = null)
    {
        if (template == null)
            return;
        HitTypes = [];
        Id = template.Id;
        Template = template;
        if (owner != null)
            Level = template.LevelStep > 0 ? (byte)((owner.GetAbLevel(template.AbilityId) - template.AbilityLevel) / template.LevelStep + 1) : (byte)1;
        else
            Level = 1;
    }

    /// <summary>
    /// Runs the skill and returns it's error code if any
    /// </summary>
    /// <param name="caster"></param>
    /// <param name="casterCaster"></param>
    /// <param name="targetCaster"></param>
    /// <param name="skillObject">null by default</param>
    /// <param name="bypassGcd">false by default</param>
    /// <param name="skillResultValueUInt">Additional 32-bit skill error data</param>
    /// <returns></returns>
    public SkillResult Use(BaseUnit caster, SkillCaster casterCaster, SkillCastTarget targetCaster, SkillObject skillObject, bool bypassGcd, out uint skillResultValueUInt)
    {
        return Use(caster, casterCaster, targetCaster, skillObject, bypassGcd, out _, out skillResultValueUInt);
    }

    /// <summary>
    /// </summary>
    public SkillResult Use(
        BaseUnit caster,
        SkillCaster casterCaster,
        SkillCastTarget targetCaster,
        SkillObject skillObject,
        bool bypassGcd,
        out ushort skillResultValueUShort,
        out uint skillResultValueUInt)
    {
        skillResultValueUShort = 0;
        skillResultValueUInt = 0;
        // Check if the source is an actual Unit
        if (caster is not Unit unit)
        {
            return SkillResult.InvalidSource;
        }

        // Every line below dereferences Template. A Skill built from a missing template (item procs did
        // exactly that) used to NRE on the first Template.Id read, and callers inside a plot effect turned
        // that into a lost target list rather than a visible failure.
        if (Template == null)
        {
            Logger.Warn("Skill.Use called with no template (caster {0})", caster.ObjId);
            return SkillResult.InvalidSkill;
        }

        // Cast character for future reference
        var character = caster as Character;

        // The dismount skill carries no effects of its own: const_skill_types names it "detached_unit"
        // and the rider is expected to come off whatever it is attached to when the skill is used.
        if (character != null && SkillManager.Instance.IsDetachSkill(Template.Id))
        {
            character.ForceDismount(AttachUnitReason.SlaveBinding);
            Logger.Debug("Detach skill {0} used by {1}", Template.Id, character.Name);
        }

        unit.ConditionChance = true;

        var requirementResult = UnitRequirementsGameData.Instance.CanUseSkill(
            Template,
            caster,
            casterCaster,
            targetCaster);
        if (requirementResult.ResultKey != SkillResultKeys.ok)
        {
            if (character != null)
                Logger.Warn($"{character.Name} ({character.Id}) failed requirements to use skill {Template?.Id} - {requirementResult.ResultKey}");
            Cancelled = true;
            skillResultValueUShort = requirementResult.ResultUShort;
            skillResultValueUInt = requirementResult.ResultUInt;
            return SkillResultHelper.SkillResultErrorKeyToId(requirementResult.ResultKey);
        }

        _bypassGcd = bypassGcd;
        _zoneSkillStartedRelayed = false;
        _zoneSkillFiredRelayed = false;
        _zoneSkillEndedRelayed = false;
        _zoneSkillCaster = null;
        if (!_bypassGcd)
        {
            lock (unit.GcdLock)
            {
                // Basic attacks: short anti-spam only. 500ms blocked the client auto-attack
                // retry storm and made the hotbar feel unresponsive (CooldownTime).
                // Zone-driven NPC melee needs a hard cooldown gate; the interval fallback permits
                // duplicate swings when a key has not yet been recorded.
                var delay = 150;
                if (Id == 2 || Id == 3 || Id == 4)
                    delay = character != null ? 100 : 1500;

                if (unit.SkillLastUsed.AddMilliseconds(delay) > DateTime.UtcNow)
                {
                    Logger.Trace($"Skill: CooldownTime [{delay}]!");
                    return SkillResult.CooldownTime;
                }

                // Instant combo hits (e.g. Fireball 24894/24895 custom_gcd=10) must not be blocked by
                // the parent's cast GCD — they fire at the same moment as plot cast-end.
                var comboBypassGcd = Template.CastingTime <= 0 && Template.CustomGcd > 0 && Template.CustomGcd <= 50;
                if (unit.GlobalCooldown >= DateTime.UtcNow && !Template.IgnoreGlobalCooldown && !comboBypassGcd)
                {
                    Logger.Trace($"Skill: GlobalCooldown active for {Template.Id}");
                    return SkillResult.CooldownTime;
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
            Logger.Trace($"Skill: SkillResult.NoTarget! - Skill {Template.Id}, Caster {caster.Name} ({caster.ObjId})");
            return SkillResult.NoTarget; // We should try to make sure this doesn't happen, but can happen with NPC skills
        }

        // Unmount character if skill asks for it
        if (character is { IsRiding: true } && Template.Unmount)
        {
            var mateList = character.ParentWorld.MateManager.GetActiveMates(character.Id);
            foreach (var mate in mateList)
            {
                // TODO: Handle this better so it works for passengers as well
                if (mate.Passengers.GetValueOrDefault(AttachPointKind.Driver)?._objId == character.ObjId)
                    character.ParentWorld.MateManager.UnMountMate(character, mate.TlId, AttachPointKind.Driver, AttachUnitReason.None);
            }
        }

        // Check initial mana cost
        if (ManaCost(unit) > unit.Mp)
            return SkillResult.LackMana;

        // Get a TlId for this skill
        TlId = SkillTlIdManager.GetNextId(caster);
        // if (caster is Character)
        Logger.Debug($"Created SkillTlId {TlId} for Skill {Template.Id}, Caster {caster.Name} ({caster.TemplateId}:{caster.ObjId}) with target {target.Name} ({target.TemplateId}:{target.ObjId})");

        // If skill uses Plots, then start the plot
        if (Template.Plot != null)
        {
            if (Template.PlotOnly)
            {
                // plot_only returns before Cast() — apply start costs here. GCD for cast-time plot_only
                // is applied when the plot leaves its casting edge (PlotNode → ApplyPlotOnlyFireCosts).
                // Zone needs WZSkillStarted now (Cast never runs).
                RelayZoneSkillStartedIfNeeded(casterCaster, targetCaster, skillObject);
                ConsumeMana(caster);
                if (Template.CastingTime <= 0)
                    ApplyPlotOnlyFireCosts(unit);
                Task.Run(() => Template.Plot.RunAsync(caster, casterCaster, target, targetCaster, skillObject, this));
                return SkillResult.Success;
            }

            Task.Run(() => Template.Plot.RunAsync(caster, casterCaster, target, targetCaster, skillObject, this));
        }

        // Check if target is within range
        var skillRange = caster.ApplySkillModifiers(this, SkillAttribute.Range, Template.MaxRange);
        var targetDist = unit.GetDistanceTo(target, true);

        var minRangeCheck = Template.MinRange * 1.0;
        var maxRangeCheck = skillRange;

        // HackFix: for quest Unblock the Spring ( 3707 ), unable to use the boulder because of being "too close"
        // The range of skill Remove Stone ( 16462 ) is defined as 100~200 which can't possibly be correct 
        if (Template.TargetType == SkillTargetType.Doodad && Template.MinRange >= 100)
        {
            minRangeCheck = Template.MinRange / 100.0;
        }

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

        // World mirror transforms can lag ZWUnitMovements by a tick, which rejected every
        // Zone melee (skill 2) as TooFarRange while the NPC visually swung and dealt no SC damage.
        var zoneNpcCast = WorldIntegration.ZoneAuthority && caster is Npc;

        if (!zoneNpcCast && targetDist < minRangeCheck)
        {
            SkillTlIdManager.ReleaseId(TlId);
            TlId = 0;
            Logger.Info($"TooCloseRange targetDist={targetDist}, minRangeCheck={minRangeCheck}, SkillTlId {TlId} for Skill {Template.Id}, Caster {caster.Name} ({caster.TemplateId}:{caster.ObjId}) with target {target.Name} ({target.TemplateId}:{target.ObjId})");
            return SkillResult.TooCloseRange;
        }

        // A position-targeted skill is cast at a spot on the ground rather than at a unit, and its
        // template carries max_range 0 because the client decides where the placement is legal.
        // Measuring the distance to that spot and comparing it against 0 rejects every cast:
        // summoning a boat (skill 15802, target type SummonPos) failed as TooFarRange at 3.4m with
        // the client left holding the cooldown it had already started.
        var placementTarget = targetCaster is SkillCastPositionTarget
            or SkillCastPosition2Target
            or SkillCastPosition3Target;
        var unboundedPlacement = placementTarget && Template.MaxRange <= 0;

        // TODO: Remove exception for doodads
        // TODO: Remove exceptions for slave initiated by Doodads (needed to fix repair points on ships)
        if (!zoneNpcCast && targetDist > maxRangeCheck && !unboundedPlacement && target is not Doodad && target is not Slave)
        {
            SkillTlIdManager.ReleaseId(TlId);
            TlId = 0;
            Logger.Info($"TooFarRange targetDist={targetDist}, maxRangeCheck={maxRangeCheck}, SkillTlId {TlId} for Skill {Template.Id}, Caster {caster.Name} ({caster.TemplateId}:{caster.ObjId}) with target {target.Name} ({target.TemplateId}:{target.ObjId})");
            return SkillResult.TooFarRange;
        }

        if (character is { AccessLevel: < 100 })
        {
            Portal trp = null;
            // copy Return.cs
            if (Template.Effects.Count > 0 && Template.Effects.First()?.Template is SpecialEffect specialEffect)
            {
                if (specialEffect.SpecialEffectTypeId == SpecialType.Return)
                {
                    if (specialEffect.Value1 > 0)
                    {
                        // Worldgates
                        trp = PortalManager.Instance.GetWorldGatesById((uint)specialEffect.Value1);
                    }
                    else
                    {
                        var returnPointId =
                            PortalManager.Instance.GetDistrictReturnPoint(character.ReturnDistrictId,
                                character.Faction.Id);
                        trp = PortalManager.Instance.GetRecallById(returnPointId);
                    }
                }
            }

            if (Template.Effects.Count > 0 && Template.Effects.First()?.Template is OpenPortalEffect)
            {
                if (WorldManager.DefaultInstanceId != caster.Transform.InstanceId)
                {
                    return SkillResult.InvalidLocation;
                }

                // copy OpenPortalEffect.cs
                var portalInfo = (SkillObjectUnk1)skillObject;
                trp = character.Portals.GetPortalInfo((uint)portalInfo.Id);
            }

            if (trp != null)
            {
                var zone = ZoneManager.Instance.GetZoneByKey(trp.ZoneId);
                if (zone is null or { Closed: true })
                {
                    // No more appropriate error type has been found yet
                    return SkillResult.NoPerm;
                }
            }
        }

        // Calculate casting time if needed
        var castTime = 0;
        if (Template.CastingTime > 0)
            castTime = (int)(unit.CastTimeMul * unit.SkillModifiersCache.ApplyModifiers(this, SkillAttribute.CastTime, Template.CastingTime));
        castTime = (int)Math.Round(castTime * CastTimeMultiplier);

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
            // Abort any in-flight cast on this unit. Client often StopCastings first, but a second
            // StartSkill without a matching stop (or a previously-ignored ZoneAuthority stop) would
            // leave the old CastTask scheduled — SpawnSlave then fires for both timelines.
            if (unit.SkillTask?.Skill != null && unit.SkillTask.Skill != this)
            {
                var previous = unit.SkillTask;
                previous.Cancel();
                previous.Skill.Cancelled = true;
                previous.Skill.Stop(unit);
            }

            // Has casting time, schedule a task for it
            // ZoneAuthority: cast begins now — Started before cast-end Cast()/EndSkill.
            RelayZoneSkillStartedIfNeeded(casterCaster, targetCaster, skillObject);
            caster.BroadcastPacket(new SCSkillStartedPacket(Id, TlId, casterCaster, targetCaster, this, skillObject)
            {
                BaseCastTimeDiv10 = (ushort)(castTime / 10),
                RealCastTimeDiv10 = (ushort)(castTime / 10), // calculate with adjustments
            }, true);

            unit.SkillTask = new CastTask(this, caster, casterCaster, target, targetCaster, skillObject);
            TaskManager.Instance.Schedule(unit.SkillTask, TimeSpan.FromMilliseconds(castTime));
        }
        else
        {
            // Immediate skill
            if (caster is Character ch && Template.Id is 2 or 3 or 4)
                ch.IsAutoAttack = true;
            Cast(caster, casterCaster, target, targetCaster, skillObject);
        }

        return SkillResult.Success;
    }

    private BaseUnit GetInitialTarget(BaseUnit caster, SkillCaster skillCaster, SkillCastTarget targetCaster)
    {
        if (caster is not Unit)
            return null;

        var target = caster;
        if (targetCaster == null || skillCaster == null) // проверяем, так как иногда бывает null
            return null;

        // HACKFIX : Mounts and Turbulence
        if (skillCaster.Type == SkillCasterType.Mount || skillCaster.Type == SkillCasterType.Unit)
            target = caster.ParentWorld.GetUnit(skillCaster.ObjId);

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
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null)
                    {
                        var relation = caster.GetRelationStateTo(target);
                        if (relation != RelationState.Friendly && relation != RelationState.Neutral)
                            return null; // Target isn't friendly
                    }

                    break;
                }
            case SkillTargetType.Hostile:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null)
                    {
                        var relation = caster.GetRelationStateTo(target);
                        if (relation != RelationState.Hostile && relation != RelationState.Neutral)
                            if (!caster.CanAttack(target))
                            {
                                return null; // Target isn't hostile
                            }
                    }

                    break;
                }
            case SkillTargetType.AnyUnit:
            case SkillTargetType.AnyUnitAlways:
            case SkillTargetType.IgnoreProtected:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
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
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
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
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
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
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
                        if (target != null)
                        {
                            targetCaster.ObjId = target.ObjId;
                        }
                    }

                    if (target != null && caster.ObjId == target.ObjId)
                    {
                        return null; // Not allowed on self
                    }

                    var relation2 = caster.GetRelationStateTo(target);
                    if (relation2 != RelationState.Friendly && relation2 != RelationState.Neutral)
                        return null; // Target isn't friendly

                    break;
                }
            case SkillTargetType.GeneralUnit:
            case SkillTargetType.ChildSlave:
            case SkillTargetType.MySlave:
                {
                    if (targetCaster.Type is SkillCastTargetType.Unit or SkillCastTargetType.Doodad)
                    {
                        target = targetCaster.ObjId > 0 ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId) : caster;
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
                    target = SetInitialTarget(caster, targetCaster);
                    if (caster.ObjId == target.ObjId)
                        return null; //TODO отправлять ошибку?
                    break;
                }
            case SkillTargetType.BallisticPos:
                {
                    target = SetInitialTarget(caster, targetCaster);
                    if (caster.ObjId == target.ObjId)
                        return null; //TODO отправлять ошибку?
                    break;
                }
            case SkillTargetType.Party:
            case SkillTargetType.Raid:
            case SkillTargetType.Line:
            case SkillTargetType.Pet:
                target = targetCaster.ObjId > 0
                    ? caster.ParentWorld.GetBaseUnit(targetCaster.ObjId)
                    : caster;
                break;
            case SkillTargetType.SummonPos:
            case SkillTargetType.CommanderPos:
                if (targetCaster is SkillCastPositionTarget or SkillCastPosition2Target or SkillCastPosition3Target)
                    target = SetInitialTarget(caster, targetCaster);
                break;
            // Ship harpoon Launch Harpoon (13749) uses target_type_id 13 = RelativePos with a world Position from the client.
            case SkillTargetType.RelativePos:
                {
                    if (targetCaster is SkillCastPositionTarget or SkillCastPosition2Target or SkillCastPosition3Target)
                    {
                        target = SetInitialTarget(caster, targetCaster);
                        if (caster.ObjId == target.ObjId)
                            return null;
                    }

                    break;
                }
            case SkillTargetType.SourcePos:
                target = caster;
                break;
            case SkillTargetType.ArtilleryPos:
                {
                    target = SetInitialTarget(caster, targetCaster);
                    if (caster.ObjId == target.ObjId)
                        return null; //TODO отправлять ошибку?
                    break;
                }
            case SkillTargetType.CursorPos:
                if (targetCaster is SkillCastPositionTarget or SkillCastPosition2Target or SkillCastPosition3Target)
                    target = SetInitialTarget(caster, targetCaster);
                break;
            case SkillTargetType.Parent:
            case SkillTargetType.PetOwner:
                target = ResolveOwnerTarget(caster) ?? caster;
                targetCaster.ObjId = target.ObjId;
                break;
            default:
                throw new NotSupportedException($"SkillTargetType not supported {Template.TargetType}");
        }

        return target;
    }

    private static BaseUnit ResolveOwnerTarget(BaseUnit caster)
    {
        uint ownerObjId = caster switch
        {
            global::AAEmu.Game.Models.Game.Units.Mate mate => mate.OwnerObjId,
            Slave slave => slave.OwnerObjId,
            Unit unit => unit.OwnerId,
            _ => 0u
        };

        return ownerObjId > 0 ? caster.ParentWorld?.GetBaseUnit(ownerObjId) : null;
    }

    private static BaseUnit SetInitialTarget(BaseUnit caster, SkillCastTarget targetCaster)
    {
        var positionUnit = new BaseUnit { ObjId = uint.MaxValue };
        positionUnit.Transform = caster.Transform.CloneDetached(positionUnit);
        switch (targetCaster)
        {
            case SkillCastDoodadTarget doodadTarget:
                break;
            case SkillCastItemTarget itemTarget:
                break;
            case SkillCastUnitTarget unitTarget:
                break;
            case SkillCastPositionTarget positionTarget:
                {
                    if (caster is Npc { CurrentTarget: not null } npc)
                        positionUnit.Transform.Local.SetPosition(npc.CurrentTarget.Transform.Local.Position.X, npc.CurrentTarget.Transform.Local.Position.Y, npc.CurrentTarget.Transform.Local.Position.Z);
                    else if (positionTarget.ObjId1 != 0)
                    {
                        var worldInst = caster.ParentWorld ?? WorldManager.Instance.GetWorld(caster.Transform.InstanceId);
                        if (worldInst?.GetBaseUnit(positionTarget.ObjId1) is BaseUnit basisUnit)
                        {
                            // Hit in basis unit's local frame (e.g. harpoon on hull); Pos* are not world meters.
                            var basisRot = basisUnit.Transform.World.ToQuaternion();
                            var basisScale = basisUnit.Scale;
                            var localHit = new Vector3(positionTarget.PosX, positionTarget.PosY, positionTarget.PosZ);
                            var worldHit = Vector3.Transform(localHit * basisScale, basisRot) + basisUnit.Transform.World.Position;
                            positionUnit.Transform.Local.SetPosition(worldHit.X, worldHit.Y, worldHit.Z);
                        }
                        else
                            positionUnit.Transform.Local.SetPosition(caster.Transform.World.Position.X, caster.Transform.World.Position.Y, caster.Transform.World.Position.Z);
                    }
                    else
                        positionUnit.Transform.Local.SetPosition(positionTarget.PosX, positionTarget.PosY, positionTarget.PosZ);
                    break;
                }
            case SkillCastPosition2Target position2Target:
                {
                    positionUnit.Transform.Local.SetPosition(position2Target.PosX, position2Target.PosY, position2Target.PosZ);
                    break;
                }
            case SkillCastPosition3Target position3Target:
                {
                    positionUnit.Transform.Local.SetPosition(position3Target.PosX, position3Target.PosY, position3Target.PosZ);
                    break;
                }
        }

        positionUnit.Region = WorldManager.Instance.GetRegion(positionUnit);

        return positionUnit;
    }

    public void Cast(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (caster is not Unit unit) { return; }
        if (Cancelled)
        {
            if (TlId != 0)
            {
                RelayZoneSkillEndedIfNeeded();
                SkillTlIdManager.ReleaseId(TlId);
                TlId = 0;
            }
            return;
        }

        if (!_bypassGcd)
        {
            ApplyGlobalCooldown(unit);
        }

        // Instant ZoneAuthority casts: WZSkillStarted before ScheduleEffects/EndSkill (melee 2
        // clears TlId immediately). Cast-time / plot_only already relayed at Use() entry.
        RelayZoneSkillStartedIfNeeded(casterCaster, targetCaster, skillObject);

        if (caster is Npc && Template.SkillControllerId != 0)
        {
            var scTemplate = SkillManager.Instance.GetEffectTemplate(Template.SkillControllerId, "SkillController") as SkillControllerTemplate;

            // Get a random number (from 0 to n)
            var value = Random.Shared.Next(0, 1);
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

            //var targetUnit = (Unit)target; // unnecessary type cast
            var dist = MathUtil.CalculateDistance(caster.Transform.World.Position, target.Transform.World.Position, true);
            if (dist >= SkillManager.Instance.GetSkillTemplate(Id).MinRange && dist <= SkillManager.Instance.GetSkillTemplate(Id).MaxRange)
            {
                var sc = SkillController.CreateSkillController(scTemplate, caster, target);
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
    public void StopSkill(BaseUnit caster)
    {
        if (caster is not Unit unit) { return; }

        if (unit.AutoAttackTask != null)
            unit.AutoAttackTask.Cancelled = true;

        // await unit.AutoAttackTask.Cancel();
        caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
        caster.BroadcastPacket(new SCSkillStoppedPacket(unit.ObjId, Id), true);
        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelaySkillStoppedToZone?.Invoke(unit.ObjId, (int)Id);
        //unit.AutoAttackTask = null;
        //unit.IsAutoAttack = false; // turned off auto attack
        RelayZoneSkillEndedIfNeeded();
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
            doodad = DoodadManager.Instance.Create(unit.ParentWorld, 0, Template.ChannelingDoodadId, caster, true);
            doodad.Transform = caster.Transform.CloneDetached(doodad);
            doodad.InitDoodad();
            doodad.Spawn();
        }

        caster.BroadcastPacket(new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject), true);
        RelayZoneSkillFiredIfNeeded(casterCaster, targetCaster, skillObject);
        unit.SkillTask = new EndChannelingTask(this, caster, casterCaster, target, targetCaster, skillObject, doodad);
        TaskManager.Instance.Schedule(unit.SkillTask, TimeSpan.FromMilliseconds(Template.ChannelingTime));
    }

    public void EndChanneling(BaseUnit caster, Doodad channelDoodad, SkillCaster casterCaster)
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

        // TODO: добавил, так как для квеста 3469 нет события OnItemUse
        // TODO: added since there is no OnItemUse event for quest 3469 and other quests that require the use on non-consuming items
        if (Cancelled == false && casterCaster is SkillItem { ItemTemplateId: > 0 } item && caster is Character player)
        {
            player.ItemUse(item.ItemId);
        }

        unit.Events.OnChannelingCancel(this, new OnChannelingCancelArgs());
    }

    public void ScheduleEffects(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (caster is not Unit unit) { return; }
        if (Cancelled)
        {
            if (TlId != 0)
            {
                RelayZoneSkillEndedIfNeeded();
                SkillTlIdManager.ReleaseId(TlId);
                TlId = 0;
            }
            return;
        }
        // toggle_buff_id (e.g. Dash 16287 → buff 2675): second use cancels. BuffTemplate.Apply
        // early-returns when the buff is already present, so toggle-off must RemoveBuff here.
        if (Template.ToggleBuffId != 0)
        {
            if (caster.Buffs.CheckBuff(Template.ToggleBuffId))
            {
                caster.Buffs.RemoveBuff(Template.ToggleBuffId);
            }
            else
            {
                var buff = SkillManager.Instance.GetBuffTemplate(Template.ToggleBuffId);
                buff.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId),
                    new EffectSource(this), skillObject, DateTime.UtcNow);
            }
        }

        var totalDelay = 0;
        if (Template.EffectDelay > 0)
            totalDelay += Template.EffectDelay;
        if (Template.EffectSpeed > 0)
            totalDelay += (int)(unit.GetDistanceTo(target) / Template.EffectSpeed * 1000.0f);
        if (Template.FireAnim != null && Template.UseAnimTime)
            totalDelay += (int)(Template.FireAnim.CombatSyncTime * (unit.GlobalCooldownMul / 100));

        // Auto-attacks use the equipped holdable animation. Other ranged skills can carry a
        // separate shotgun fire animation in the skill row; select it from the actual ranged
        // holdable instead of always sending the bow/default fire_anim_id.
        var weaponAnimId = GetWeaponAttackAnimId(caster);
        var fireAnimId = weaponAnimId > 0 ? weaponAnimId : GetRangedSkillFireAnimId(caster);
        var firedPacket = new SCSkillFiredPacket(Id, TlId, casterCaster, targetCaster, this, skillObject)
        {
            ComputedDelay = (short)totalDelay
        };
        if (fireAnimId > 0)
            firedPacket.FireAnimId = fireAnimId;
        caster.BroadcastPacket(firedPacket, true);

        // ZoneAuthority: bridge fire to Zone at the same moment as SC SkillFired (not for plot_only — Use never reaches here).
        RelayZoneSkillFiredIfNeeded(casterCaster, targetCaster, skillObject);

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

    /// <summary>
    /// Get the weapon-based attack animation ID for auto-attack skills (2/3/4).
    /// Returns 0 for non-auto-attack skills (packet will use FireAnim from template).
    /// NPCs cycle between melee animation IDs 1 and 2 so AI mobs always have a visible swing.
    /// </summary>
    private uint GetWeaponAttackAnimId(BaseUnit caster)
    {
        if (Template.Id is not (2 or 3 or 4))
            return 0;

        if (caster is NPChar.Npc)
        {
            // NPCs cycle between two melee attack animations (side strikes).
            // Without this, NPC auto-attacks would inherit the skill template's
            // FireAnim — often null or wrong, causing the "AI feels broken" symptom.
            var npcAnim = (uint)((AutoAttackIndex % 2) + 1); // animation IDs 1 and 2
            AutoAttackIndex++;
            return npcAnim;
        }

        if (caster is not Character character)
            return 0;

        var slot = Template.Id switch
        {
            3 => EquipmentItemSlot.Offhand,
            4 => EquipmentItemSlot.Ranged,
            _ => EquipmentItemSlot.Mainhand
        };

        var weapon = character.Equipment?.GetItemBySlot((int)slot);
        if (weapon?.Template is WeaponTemplate wt && wt.HoldableTemplate != null)
        {
            var leftHand = Template.Id == 3; // Offhand = left hand
            var animId = wt.HoldableTemplate.GetAttackAnimId(AutoAttackIndex, leftHand);
            AutoAttackIndex++;
            return animId;
        }

        // No weapon equipped — fist animations (cycle between 1 and 2)
        var fistAnim = (AutoAttackIndex % 2 == 0) ? 1u : 2u;
        AutoAttackIndex++;
        return fistAnim;
    }

    /// <summary>
    /// Resolves the optional shotgun-specific animation carried by a ranged skill template.
    /// A zero result keeps the ordinary <c>fire_anim_id</c> selected by the packet.
    /// </summary>
    private uint GetRangedSkillFireAnimId(BaseUnit caster)
    {
        if (Template.ShotGunFireAnimId == 0 || caster is not Unit unit)
            return 0;

        var shotgunHoldableId = ItemManager.Instance.GetConstHoldableId("shot_gun");
        if (shotgunHoldableId == 0)
            return 0;

        var rangedWeapon = unit.Equipment?.GetItemBySlot((int)EquipmentItemSlot.Ranged);
        return rangedWeapon?.Template is WeaponTemplate rangedTemplate &&
               rangedTemplate.HoldableTemplate?.Id == shotgunHoldableId
            ? Template.ShotGunFireAnimId
            : 0;
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
        var possibleTargets = new List<BaseUnit>(); // TODO crutches
        // Get a list of all possible targets
        // 10.0.2.13: skills.target_siege removed; the former ship-skill hack (TargetSiege + Source + Slave) no
        // longer has a data source, so AoE skills fall through to the standard target-area handling below.
        if (Template.TargetAreaRadius > 0)
        {
            var units = WorldManager.GetAround<BaseUnit>(targetSelf, Template.TargetAreaRadius, true);
            if (Template.TargetSelection == SkillTargetSelection.Source)
                units.Add(targetSelf); // Add main target as well
            units = FilterAoeUnits(caster, units).ToList();

            possibleTargets.AddRange(units);
            // TODO : Need to check if this is needed
            //if (targetSelf is Unit) targets.Add(targetSelf);
        }
        else
        {
            possibleTargets.Add(targetSelf);
        }

        ShipSiegeAoEHit.AppendHostileShipsHitBySiegeHullAoE(caster, Template, targetSelf, targetCaster, possibleTargets);

        // Filter out duplicate entries and non-existing
        possibleTargets = possibleTargets.Distinct().ToList();
        // Add origin in case of no targets and using a target position cast
        if (possibleTargets.Count <= 0 && targetCaster is SkillCastPositionTarget)
        {
            possibleTargets.Add(caster);
        }

        if (Template.TargetAreaCount > 0 && possibleTargets.Count > Template.TargetAreaCount)
        {
            possibleTargets = possibleTargets
                .OrderBy(t => t.GetDistanceTo(targetSelf))
                .Take(Template.TargetAreaCount)
                .ToList();
        }

        foreach (var target in possibleTargets)
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

        CompressedGamePackets packets = null;
        var consumedItems = new List<(Item, int)>();
        var consumedItemTemplates = new List<(uint, int)>(); // itemTemplateId, amount

        var effectsToApply = new List<(BaseUnit target, SkillEffect effect)>(possibleTargets.Count * Template.Effects.Count);
        SkillEffect lastAppliedEffect = null;

        // Loop Skill Effects
        foreach (var effect in Template.Effects)
        {
            // Get targets for this effect
            var effectedTargets = new List<BaseUnit>();
            switch (effect.ApplicationMethod)
            {
                case SkillEffectApplicationMethod.Target:
                    effectedTargets = possibleTargets;//keep target
                    break;
                case SkillEffectApplicationMethod.Source:
                    effectedTargets.Add(caster);//Diff between Source and SourceOnce?
                    break;
                case SkillEffectApplicationMethod.SourceOnce:
                    // Owner's mark used to redirect Mount→Mate onto the mate target. That catch-all
                    // also matched Sail→Hull fold casts (targetSelf is Slave), so SourceOnce anim
                    // buffs landed on the hull instead of the equipment sail — fold never showed.
                    if (casterCaster.Type == SkillCasterType.Mount && targetSelf is Units.Mate)
                        effectedTargets = possibleTargets;
                    else
                        effectedTargets.Add(caster);
                    break;
                case SkillEffectApplicationMethod.SourceToPos:
                    effectedTargets = possibleTargets;
                    break;
            }

            // Loop targets for this effect
            foreach (var target in effectedTargets)
            {
                var targetNpc = target as Npc;
                var relationState = caster.GetRelationStateTo(target);
                // Level range check
                if (effect.StartLevel > unit.Level || effect.EndLevel < unit.Level)
                {
                    continue;
                }

                // Relations checks
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

                // Position check
                if (effect.Front && !effect.Back && !MathUtil.IsFront(caster, target))
                {
                    continue;
                }

                if (!effect.Front && effect.Back && MathUtil.IsFront(caster, target))
                {
                    continue;
                }

                // Blocking buffs and tags checks 
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

                if (effect.TargetNpcTagId > 0)
                {
                    if (targetNpc == null || !TagsGameData.Instance
                            .GetIdsByTagId(TagsGameData.TagType.Npcs, effect.TargetNpcTagId)
                            .Contains(targetNpc.TemplateId))
                        continue;
                }

                // Dice
                if (effect.Chance < 100 && Random.Shared.Next(100) > effect.Chance)
                {
                    continue;
                }

                // prevents an NPC Spawn Skill to be duplicated 
                if (lastAppliedEffect != null &&
                    effect.Template is NpcSpawnerSpawnEffect &&
                    effect.EffectId == lastAppliedEffect.EffectId &&
                    (effect.Template as NpcSpawnerSpawnEffect).SpawnerId == (lastAppliedEffect.Template as NpcSpawnerSpawnEffect).SpawnerId)
                {
                    continue;
                }

                // Apply the effect
                effectsToApply.Add((target, effect));
                lastAppliedEffect = effect;
                //effect.Template?.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow, packets);
            }
        }

        // Handle consumption of items from effects (once per cast — scan ALL queued effects).
        // Using only lastAppliedEffect breaks multi-effect skills: farmer's pouch (23136) applies
        // GainLootPack (consume_source_item=t) then a conditional BuffEffect (consume=f). With a
        // life-skill buff active the buff is last → loot granted, purse never removed.
        if (effectsToApply.Count > 0 && player != null)
        {
            var consumeSource = false;
            var sourceConsumeCount = 0;
            foreach (var (_, effect) in effectsToApply)
            {
                if (!effect.ConsumeSourceItem || effect.ConsumeItemCount <= 0)
                    continue;
                consumeSource = true;
                sourceConsumeCount = Math.Max(sourceConsumeCount, effect.ConsumeItemCount);
            }

            if (casterCaster is SkillItem castItem)
            {
                var useItem = ItemManager.Instance.GetItemByItemId(castItem.ItemId)
                              ?? player.Inventory.Bag.GetItemByItemId(castItem.ItemId);
                if (consumeSource)
                {
                    // GainLootPackItemEffect already ConsumeItem's the SkillItem (stack-safe).
                    // Queuing it here again burns a second unit from the same stack.
                    var lootPackHandlesSource = effectsToApply.Any(e =>
                        e.effect.Template is GainLootPackItemEffect);
                    if (lootPackHandlesSource)
                    {
                        // Effect owns source consumption.
                    }
                    else if (useItem is { _holdingContainer: not null })
                    {
                        consumedItems.Add((useItem, sourceConsumeCount));
                    }
                    else if (useItem == null)
                    {
                        if (castItem.ItemTemplateId != 0)
                        {
                            // ItemId missing from world map — still burn a bag stack by template.
                            consumedItemTemplates.Add((castItem.ItemTemplateId, Math.Max(1, sourceConsumeCount)));
                            Logger.Warn(
                                "Skill {0}: consume_source_item itemId={1} missing from ItemManager; consuming tpl {2} from bag",
                                Template.Id, castItem.ItemId, castItem.ItemTemplateId);
                        }
                        else
                        {
                            Logger.Warn("Skill {0}: consume_source_item but item {1} not found", Template.Id, castItem.ItemId);
                        }

                        // Clear client bag ghost for this instance id (server object already gone).
                        if (castItem.ItemId != 0)
                            player.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.SkillReagents, [], [castItem.ItemId]));
                    }
                }
                else
                {
                    var castItemTemplate = ItemManager.Instance.GetTemplate(castItem.ItemTemplateId);
                    if (castItemTemplate is { UseSkillAsReagent: true } && useItem != null)
                        consumedItems.Add((useItem, Math.Max(1, lastAppliedEffect?.ConsumeItemCount ?? 1)));
                }
            }

            foreach (var (_, effect) in effectsToApply)
            {
                if (effect.ConsumeItemId == 0 || effect.ConsumeItemCount <= 0)
                    continue;
                if (effect.ConsumeSourceItem)
                {
                    consumedItemTemplates.Add((effect.ConsumeItemId, effect.ConsumeItemCount));
                    continue;
                }

                var inventory = player.Inventory.CheckItems(SlotType.Inventory, effect.ConsumeItemId, effect.ConsumeItemCount);
                var equipment = player.Inventory.CheckItems(SlotType.Equipment, effect.ConsumeItemId, effect.ConsumeItemCount);
                if (inventory || equipment)
                    consumedItemTemplates.Add((effect.ConsumeItemId, effect.ConsumeItemCount));
            }
        }

        // This will handle all items with a reagent/product
        var reagents = SkillManager.Instance.GetSkillReagentsBySkillId(Template.Id);
        var skillProducts = SkillManager.Instance.GetSkillProductsBySkillId(Template.Id);
        if (reagents.Count > 0 || skillProducts.Count > 0)
        {
            if (player != null)
            {
                if (reagents.Count > 0)
                {
                    var foundValidReagents = false;
                    foreach (var reagent in reagents)
                    {
                        player.Inventory.Bag.GetAllItemsByTemplate(reagent.ItemId, -1, out _, out var totalCount);
                        if (totalCount >= reagent.Amount)
                        {
                            consumedItemTemplates.Add((reagent.ItemId, reagent.Amount));
                            foundValidReagents = true;
                            if (Template.FirstReagentOnly)
                                break;
                        }
                        else
                        if (!Template.FirstReagentOnly)
                        {
                            // Not enough reagent items
                            Cancelled = true;
                            return;
                        }
                    }

                    if (!foundValidReagents)
                    {
                        // Not enough reagent items
                        Cancelled = true;
                        return;
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

        // Check if any of the effects use Weight, and pick a random value
        var weightedTotal = 0;
        var selectedWeight = -1;
        foreach (var (_, effect) in effectsToApply)
            weightedTotal += effect.Weight;
        if (weightedTotal > 0)
            selectedWeight = Random.Shared.Next(weightedTotal);
        var currentWeight = 0;
        // (caster as Character)?.SendMessage($"Effect Random {selectedWeight+1}/{weightedTotal}");

        // Apply the effects that need to happen
        foreach (var (target, effect) in effectsToApply)
        {
            // If this item uses Weight, handle the random selector
            // For example NPC /useskill 13834 has multiple bubble chat effects that need to be picked from
            // Probably used for some combat and loot skills as well
            if (effect.Weight > 0)
            {
                // Check if we already have a result
                if (selectedWeight == -1)
                    continue;

                // If selection is outside the current range, then skip this effect
                currentWeight += effect.Weight;
                if (selectedWeight >= currentWeight)
                {
                    continue;
                }

                // (caster as Character)?.SendMessage($"Selected Effect {effect.EffectId} ({currentWeight}) using {selectedWeight} / {weightedTotal} - Buff {effect.Template.BuffId}");
                selectedWeight = -1;
            }

            // Template can be null for some reason.
            if (effect.Template != null)
            {
                var thisTargetCaster = target.ObjId == targetCaster.ObjId
                    ? targetCaster
                    : new SkillCastUnitTarget(target.ObjId);

                if (effect.Template is KillNpcWithoutCorpseEffect nsse)
                {
                    // для квеста 3478, требуется чтобы caster был Npc
                    // для квеста 3993 должен выполняться эффект, а он прерывался из-за неправильного сравнения!
                    var npc = caster.ParentWorld.GetNpcByTemplateId(nsse.NpcId);
                    var effectiveNpc = npc ?? target as Npc;

                    // If we have an effective NPC and it is dead, skip the effect - KillNPCWithoutCorpse happens before death
                    if (effectiveNpc != null && effectiveNpc.IsDead)
                    {
                        // Logger.Warn("Effective NPC is dead, skipping KillNpcWithoutCorpseEffect.");
                    }
                    else
                    {
                        effect.Template.Apply(npc ?? caster, casterCaster, target, thisTargetCaster, new CastSkill(Template.Id, TlId),
                            new EffectSource(this), skillObject, DateTime.UtcNow, packets);
                    }
                }
                else
                {
                    effect.Template.Apply(caster, casterCaster, target, thisTargetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow, packets);

                    if (player is { SkillCancelled: true }) { Cancelled = true; }
                }

                // Implement consumption of item sets
                if (effect.ItemSetId > 0)
                {
                    // TODO: Check what KindId does (only 1 used in 1.2)
                    var itemSet = ItemManager.Instance.GetItemSet(effect.ItemSetId);
                    if (itemSet != null)
                    {
                        foreach (var itemSetItem in itemSet.Items)
                        {
                            consumedItemTemplates.Add((itemSetItem.Value.ItemId, itemSetItem.Value.Count));
                            // player.Inventory.ConsumeItem(null, ItemTaskType.SkillEffectConsumption, itemSetItem.Value.ItemId, itemSetItem.Value.Count, null);
                        }
                    }
                }
            }
            else
                Logger.Error($"Template not found for Skill[{Template.Id}] Effect[{effect.EffectId}]");
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
            if (item?.Template.UseSkillAsReagent == true && reagents.Count <= 0 && skillProducts.Count <= 0 && consumedItems.Count <= 0 && Template.Effects.Count == 0)
            {
                consumedItems.Add((item, 1));
                Logger.Debug($"Consumed item template 1 x {item.TemplateId} ({item.Id}) because of missing reagent information with skill {Template.Id}");
            }
        }

        // Quick Hack
        if (packets is { Packets.Count: > 0 })
            caster.BroadcastPacket(packets, true);

        // Hack to consume TreasureMap items (don't know how else to add this)
        if (player != null && Template.Id == SkillsEnum.DigUpTreasureChestMarkedOnMap)
        {
            var treasureMapToUse = UnitRequirementsGameData.Instance.GetTreasureMapWithCoordinatesNearbyItem(
                player, Template.MaxRange);
            if (treasureMapToUse != null)
            {
                consumedItems.Add((treasureMapToUse, 1));
            }
            else
            {
                Logger.Error($"Unable to find a treasure map to take from user {player.Name} ({player.Id}) when digging up treasure");
            }
        }

        if (!Cancelled)
        {
            if (player != null)
            {
                // Actually consume the to be consumed items
                // Specific Items
                foreach (var (item, amount) in consumedItems)
                    if (item?._holdingContainer != null)
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
            var laborCost = Template.ConsumeLaborPower;
            // Adjust labor cost if needed
            if (character.Actability.Actabilities.TryGetValue((byte)Template.ActabilityGroupId, out var actAbility))
            {
                laborCost = (int)Math.Round(laborCost * actAbility.GetLaborCostMultiplier());
            }

            // Lower cap at 1
            if (Template.ConsumeLaborPower > 0 && laborCost < 1)
                laborCost = 1;

            // Both pools pay, so both have to be counted here. ChangeLabor charges the account pool
            // first and the local pool for the rest; gating on the account pool alone let a skill whose
            // cost exceeded it run without being charged at all, while the player still held plenty of
            // Online Labor. The other labor gates - crafting, the auction fee, specialty selling, exp
            // recovery - all read the combined balance.
            if (laborCost > 0 && !Cancelled && character.LaborPower + character.LocalLaborPower >= laborCost)
            {
                // Consume labor only if there is enough of it
                character.ChangeLabor((short)-laborCost, Template.ActabilityGroupId);
            }

            // Add vocation where needed
            if (Template.GainLifePoint > 0 && !Cancelled)
            {
                // We multiply the BASE value for server settings, not the total (although I don't think this would affect anything since we don't really have a +1 badge/action buff)
                character.ChangeGamePoints(GamePointKind.Vocation, (int)Math.Ceiling(AppConfiguration.Instance.World.VocationRate * Template.GainLifePoint));
            }
        }

        Callback?.Invoke();
        unit.OnSkillEnd(this);
        // Basic attacks (2/3/4): while auto-attacking, skip SCSkillEnded so a Started/Fired
        // swing doesn't immediately clear client combat UI. Stop clears via SCSkillStopped.
        if (Template.Id is not (2 or 3 or 4) || caster is not Character { IsAutoAttack: true })
            caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
        RelayZoneSkillEndedIfNeeded();
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
    public void Stop(BaseUnit caster, Doodad channelDoodad = null, SkillCaster casterCaster = null)
    {
        if (caster is not Unit unit) { return; }
        if (Template.ChannelingTime > 0)
        {
            EndChanneling(caster, channelDoodad, casterCaster);
        }

        if (Template.ToggleBuffId != 0)
        {
            caster.Buffs.RemoveEffect(Template.ToggleBuffId, Template.Id);
        }
        caster.BroadcastPacket(new SCCastingStoppedPacket(TlId, 0), true);
        caster.BroadcastPacket(new SCSkillEndedPacket(TlId), true);
        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayCastingStoppedToZone?.Invoke(unit.ObjId, (short)TlId, 0, 0);
        Callback?.Invoke();
        unit.OnSkillEnd(this);
        unit.SkillTask = null;
        Cancelled = true;
        RelayZoneSkillEndedIfNeeded();
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

            if (Target != null && Random.Shared.Next(0f, 100f) < Target.DodgeRate - bullsEyeMod)
            {
                if (damageType == DamageType.Melee)
                    return SkillHitType.MeleeDodge;
                if (damageType == DamageType.Ranged)
                    return SkillHitType.RangedDodge;
            }
            if (Target != null && Random.Shared.Next(0f, 100f) < Target.BlockRate - bullsEyeMod)
            {
                if (damageType == DamageType.Melee)
                    return SkillHitType.MeleeBlock;
                if (damageType == DamageType.Ranged)
                    return SkillHitType.RangedBlock;
            }
            if (Target != null && Random.Shared.Next(0F, 100f) < Target.MeleeParryRate - bullsEyeMod)
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
            if (Target != null && Random.Shared.Next(0f, 100f) < Target.RangedParryRate - bullsEyeMod)
            {
                if (damageType == DamageType.Ranged)
                    return SkillHitType.RangedParry;
            }
        }

AlwaysHit:
        switch (damageType)
        {
            case DamageType.Melee:
                if (Attacker != null && Random.Shared.Next(0f, 100f) < Attacker.MeleeAccuracy)
                    return SkillHitType.MeleeHit;
                return SkillHitType.MeleeMiss;
            case DamageType.Magic:
                if (Attacker != null && Random.Shared.Next(0f, 100f) < Attacker.SpellAccuracy)
                    return SkillHitType.SpellHit;
                return SkillHitType.SpellMiss;
            case DamageType.Ranged:
                if (Attacker != null && Random.Shared.Next(0f, 100f) < Attacker.RangedAccuracy)
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
        var baseCost = ((caster.GetAbLevel(Template.AbilityId) - 1) * 1.6 + 8) * 3 / 3.65;
        var cost2 = baseCost * Template.ManaLevelMd + Template.ManaCost;
        var manaCost = (int)caster.SkillModifiersCache.ApplyModifiers(this, SkillAttribute.ManaCost, cost2);
        return manaCost;
    }

    /// <summary>
    /// </summary>
    public void ApplyPlotOnlyFireCosts(Unit unit)
    {
        if (unit == null || !Template.PlotOnly || _bypassGcd)
            return;
        ApplyGlobalCooldown(unit);
        // Skill cooldown is also applied in DoPlotEnd; applying early matches Cast() and blocks re-cast spam.
        if (Template.CooldownTime > 0)
            unit.Cooldowns.AddCooldown(Template.Id, (uint)Template.CooldownTime);
    }

    /// <summary>
    /// ZoneAuthority WZSkillStarted once per Use. Instant skills must call this from Cast()
    /// before EndSkill zeroes TlId; cast-time/plot_only call it when the cast begins.
    /// </summary>
    private void RelayZoneSkillStartedIfNeeded(SkillCaster casterCaster, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (_zoneSkillStartedRelayed || TlId == 0)
            return;
        if (!WorldIntegration.ZoneAuthority)
            return;
        if (Environment.GetEnvironmentVariable("AAEMU_FORCE_LOCAL_SKILLS") == "1")
            return;
        if (WorldIntegration.RelaySkillStartedToZone == null)
            return;

        if (WorldIntegration.RelaySkillStartedToZone(
                Id, TlId, casterCaster, targetCaster, 0, skillObject ?? new SkillObject()))
        {
            _zoneSkillCaster = casterCaster;
            _zoneSkillStartedRelayed = true;
        }
    }

    private void RelayZoneSkillFiredIfNeeded(
        SkillCaster casterCaster,
        SkillCastTarget targetCaster,
        SkillObject skillObject)
    {
        if (!_zoneSkillStartedRelayed || _zoneSkillFiredRelayed || TlId == 0)
            return;
        if (!WorldIntegration.ZoneAuthority ||
            Environment.GetEnvironmentVariable("AAEMU_WZ_SKILL_FIRED") == "0" ||
            WorldIntegration.RelaySkillFiredToZone == null)
            return;

        _zoneSkillFiredRelayed = WorldIntegration.RelaySkillFiredToZone(
            Id, TlId, casterCaster, targetCaster, skillObject ?? new SkillObject());
    }

    /// <summary>
    /// Plot-only skills call this directly because they do not use <see cref="EndSkill"/>.
    /// </summary>
    public void RelayZoneSkillEndedIfNeeded()
    {
        if (!_zoneSkillStartedRelayed || _zoneSkillEndedRelayed || TlId == 0 || _zoneSkillCaster == null)
            return;
        if (!WorldIntegration.ZoneAuthority || WorldIntegration.RelaySkillEndedToZone == null)
            return;

        _zoneSkillEndedRelayed = WorldIntegration.RelaySkillEndedToZone(TlId, _zoneSkillCaster);
    }

    private void ApplyGlobalCooldown(Unit unit)
    {
        // Basic attacks are weapon-speed paced (StartAutoSkill / UseAutoAttackSkillTask), not GCD.
        // Skill 2 has default_gcd in DB; applying it made the hotbar feel dead and blocked the
        // auto-attack loop (task ticks skip while GlobalCooldown is active). Skill 4 already
        // ignore_global_cooldown in DB — match that for 2/3.
        if (Template.Id is 2 or 3 or 4)
            return;

        // A skill flagged ignore_global_cooldown neither waits for the GCD nor arms it. The wait side was
        // already honoured in Use(); arming it here anyway meant 8659 skills put every OTHER skill on a
        // cooldown they themselves are declared to sit outside of — Backdraft (44200) among them.
        if (Template.IgnoreGlobalCooldown)
            return;

        // NOTE: default_gcd overriding custom_gcd is deliberate and matches the data — 29054 of the 29669
        // skills with default_gcd set carry custom_gcd 0, i.e. "use the server default". The 619 that carry
        // both are ambiguous and are left on the default rather than guessed at.
        var gcd = Template.CustomGcd;
        if (Template.DefaultGcd)
            gcd = unit is Npc ? 1500 : 1000;
        if (gcd <= 0)
            return;
        unit.GlobalCooldown = DateTime.UtcNow.AddMilliseconds(gcd * (unit.GlobalCooldownMul / 100));
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
