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

using Microsoft.Extensions.DependencyInjection;
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
    /// <summary>
    /// The item a <see cref="SkillTargetType.Item"/> cast names, resolved from the client's
    /// <see cref="SkillCastItemTarget"/>. Null for every other target type.
    /// </summary>
    public Item TargetItem { get; set; }
    private bool _bypassGcd;
    /// <summary>ZoneAuthority: avoid double WZSkillStarted (cast-time relays at Use, instant at Cast).</summary>
    private bool _zoneSkillStartedRelayed;
    /// <summary>Plot-only GCD / cooldown armed at Use so hold-repeat cannot stack a new cast every 150 ms.</summary>
    private bool _plotOnlyFireCostsApplied;
    private bool _zoneSkillFiredRelayed;
    private bool _zoneSkillEndedRelayed;
    private bool _laborConsumed;
    /// <summary>Charges left after this cast spent one; -1 until the cast spends one.</summary>
    private int _chargesAfterCast = -1;
    /// <summary>Per-tick mana drain of a running channel; null when the skill charges nothing per tick.</summary>
    private ChannelingTickTask _channelingTickTask;
    /// <summary>The channel's own target/targetCaster/skillObject, needed when its effects land at the end.</summary>
    private BaseUnit _channelingTarget;
    private SkillCastTarget _channelingTargetCaster;
    private SkillObject _channelingSkillObject;
    private Doodad _channelingDoodad;
    /// <summary>The running cast's arguments, kept so a delay can reschedule the same cast.</summary>
    private SkillCaster _activeCasterCaster;
    private BaseUnit _activeTarget;
    private SkillCastTarget _activeTargetCaster;
    private SkillObject _activeSkillObject;
    private SkillCaster _zoneSkillCaster;
    private bool _cancelled;
    internal event Action<Skill> CancellationRequested;
    public bool Cancelled
    {
        get => _cancelled;
        set
        {
            if (_cancelled == value)
                return;
            _cancelled = value;
            if (value)
                CancellationRequested?.Invoke(this);
        }
    }
    public Action Callback { get; set; }

    /// <summary>
    /// When true, skip WZSkillStarted/Fired/Ended relay. Used for World-authored OnSpawn plots
    /// under ZoneAuthority so the zone process does not dual-run the same skill graph.
    /// </summary>
    public bool SuppressZoneSkillRelay { get; set; }

    /// <summary>
    /// World OnSpawn fill: run the plot graph and return without Cast(), same as plot_only.
    /// Lusca stage skills have a plot with plot_only false and empty skill_effects.
    /// </summary>
    public bool ForcePlotGraphOnly { get; set; }

    /// <summary>
    /// How many times over the skill's labor cost applies to this cast.
    /// </summary>
    /// <remarks>
    /// <c>skills.consume_lp</c> is the price of one unit of work, and for most skills a cast is one
    /// unit. Where a single cast does the work several times over - synthesis takes up to six
    /// infusions at once, and the window prices it at the skill's cost per infusion - the effect sets
    /// this to the count it actually processed. Left at 1 the charge is unchanged.
    /// </remarks>
    public int LaborUnits { get; set; } = 1;

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

        // use_condition_bits and the caster's state: dead, stunned, slept, silenced, swimming. The
        // client greys out what it can see, but a forged or stale press still reached Cast() before
        // this gate existed, and silence had no server-side effect at all.
        var useConditionFailure = SkillUseConditionRules.Evaluate(
            Template.UseConditionBits,
            SkillUseConditionRules.ReadState(unit));
        if (useConditionFailure.HasValue)
        {
            Logger.Trace("Skill {0} blocked for {1}: {2}", Template.Id, caster.Name, useConditionFailure.Value);
            return useConditionFailure.Value;
        }

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
        _plotOnlyFireCostsApplied = false;
        _laborConsumed = false;
        _chargesAfterCast = -1;
        _zoneSkillCaster = null;
        var skillTags = SkillManager.Instance.GetSkillTags(Template.Id);
        var fishingHold = character != null &&
                          SportFishCombat.ShouldBypassSharedGcd(Template.CastingTime, Template.TargetType, skillTags);
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

                // Instant combo hits skip the 150 ms anti-spam and must not write SkillLastUsed
                // (that blocked the next parent press). They still wait for the shared GCD the
                // first hit armed — the client starts them when that GCD is up.
                var comboHit = SkillCastOverlapRules.BypassesSharedCastGate(Template.CastingTime, Template.CustomGcd);
                if (!fishingHold && !comboHit && unit.SkillLastUsed.AddMilliseconds(delay) > DateTime.UtcNow)
                {
                    Logger.Trace($"Skill: CooldownTime [{delay}]!");
                    return SkillResult.CooldownTime;
                }

                if (unit.GlobalCooldown >= DateTime.UtcNow && !Template.IgnoreGlobalCooldown && !fishingHold)
                {
                    Logger.Trace($"Skill: GlobalCooldown active for {Template.Id}");
                    return SkillResult.CooldownTime;
                }

                // The skill's own cooldown is armed on cast (Cast / plot-only fire edge) but was never
                // consulted on the player path: a 15 s skill could be fired again as soon as the gate
                // above allowed it. SkillCooldownGateRules lists the casts that keep their own pacing.
                // The skill's cooldown tags are checked too, so using one variant of an action greys out
                // its siblings (295 ability skills carry a tag).
                var cooldownBlocks = SkillCooldownGateRules.CooldownBlocksCast(
                    Template.SwitchToSkillCooldown,
                    unit.Cooldowns.CheckCooldown(Template.Id),
                    unit.Cooldowns.CheckTagCooldown(Template.CooldownTags));
                var accountCooldownBlocks = Template.AccountCooldown &&
                                            character != null &&
                                            AccountCooldowns.IsActive(character.AccountId, Template.Id);
                // A charge skill with an empty pool is on cooldown even when it declares no
                // cooldown_time at all — 13281 다발 사격 is 5 charges on a 22 s recharge and 0 ms.
                var chargesExhausted = Template.ChargeCount > 1 &&
                                       unit.Cooldowns.GetCharges(
                                           Template.Id, Template.ChargeCount, Template.ChargeCooldownTime) <= 0;
                if (SkillCooldownGateRules.ShouldWaitForCooldown(
                        _bypassGcd, fishingHold, Template.Id,
                        cooldownBlocks || accountCooldownBlocks || chargesExhausted))
                {
                    Logger.Trace($"Skill: CooldownTime [{Template.CooldownTime}] for {Template.Id}");
                    return SkillResult.CooldownTime;
                }

                if (!comboHit)
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

        // stop_channeling_on_start_skill: the running channel yields to the new cast. Stop() is the
        // cancelled path, so the old channel applies no effects; its tick drain and its TlId are released.
        if (unit.SkillTask is EndChannelingTask runningChannel &&
            runningChannel.Skill != this &&
            runningChannel.Skill.Template?.StopChannelingOnStartSkill == true)
        {
            Logger.Debug("Skill {0} cancels channel {1} on {2}", Template.Id, runningChannel.Skill.Id, caster.Name);
            runningChannel.Skill.Stop(caster, runningChannel._channelDoodad);
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

        if (SportFishCombat.IsUnusableTarget(target))
        {
            Logger.Trace($"Skill: SkillResult.InvalidTarget! - Skill {Template.Id} vs dropped-line fish {target.ObjId}");
            return SkillResult.InvalidTarget;
        }

        // skill_reqs: a buff or buff tag on the caster or on the target that forbids or requires the cast
        // (rooted/stunned/fear "cannot use while X", gliding "only while X"). Checked once the target is
        // resolved, because 91 of the 338 rows read the target.
        if (!SkillRequirementRules.AllowsCast(
                SkillManager.Instance.GetSkillRequirements(Template.Id),
                requirement => requirement.BuffId > 0 &&
                               (requirement.OnTarget ? target.Buffs.CheckBuff(requirement.BuffId)
                                   : caster.Buffs.CheckBuff(requirement.BuffId)),
                requirement => requirement.BuffTagId > 0 &&
                               (requirement.OnTarget ? target.Buffs.CheckBuffTag(requirement.BuffTagId)
                                   : caster.Buffs.CheckBuffTag(requirement.BuffTagId)),
                out var requirementMessage))
        {
            Logger.Trace("Skill {0} blocked by a skill_reqs row for {1}: {2}",
                Template.Id, caster.Name, requirementMessage);
            if (character != null && !string.IsNullOrEmpty(requirementMessage))
                character.SendMessage(requirementMessage);
            return SkillResult.SkillReqFail;
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

        // Labor, before anything is committed. The charge itself stays in EndSkill, which is where the
        // actability multiplier and LaborUnits are known; this only refuses a cast the character cannot
        // pay for, which the old code let through and then quietly did not charge.
        if (character != null && Template.ConsumeLaborPower > 0 && !CanAffordLabor(character))
        {
            Logger.Trace("Skill {0} blocked for {1}: need labor power", Template.Id, character.Name);
            return SkillResult.NeedLaborPower;
        }

        // Combat resource band: the caster's pool named by combat_resource_id must be inside
        // min_combat_resource..max_combat_resource. 16 rows carry a band; the siege and test ones are
        // real (43711/43712 "fire the cannon" needs exactly one shell), the 외침 family names the pool's
        // own ceiling.
        if (Template.CombatResourceId > 0 &&
            !SkillCombatResourceRules.AllowsCast(
                Template.MinCombatResource,
                Template.MaxCombatResource,
                unit.GetCombatResource(Template.CombatResourceId)))
        {
            Logger.Trace("Skill {0} blocked for {1}: combat resource {2} outside {3}..{4}",
                Template.Id, caster.Name, Template.CombatResourceId,
                Template.MinCombatResource, Template.MaxCombatResource);
            return SkillResult.LackCombatResource;
        }

        // Get a TlId for this skill
        TlId = SkillTlIdManager.GetNextId(caster);
        // if (caster is Character)
        Logger.Debug($"Created SkillTlId {TlId} for Skill {Template.Id}, Caster {caster.Name} ({caster.TemplateId}:{caster.ObjId}) with target {target.Name} ({target.TemplateId}:{target.ObjId})");

        // Hold / reel kit ships a plot but is not flagged plot_only. Cast() after the plot
        // starts EndSkill's the TlId while the graph is still on the bar.
        if (Template.Plot != null
            && !Template.PlotOnly
            && !ForcePlotGraphOnly
            && character != null
            && SportFishCombat.ShouldRunPlotGraphOnly(
                true,
                true,
                false,
                skillTags))
        {
            ForcePlotGraphOnly = true;
        }

        // Check if target is within range
        // The check runs before the plot branch below, not after it: a plot_only skill returned from
        // Use() before ever reaching this code, so 315 of the 534 ability skills — every plot_only one —
        // could be cast from any distance at all.
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
        // A plot_only skill with max_range 0 makes the same statement as a placement cast — the plot
        // decides how far it reaches — so it keeps the old permissive behaviour rather than acquiring a
        // 0 m limit it never had.
        var unboundedPlotOnly = (Template.PlotOnly || ForcePlotGraphOnly) && Template.MaxRange <= 0;

        // TODO: Remove exception for doodads
        // TODO: Remove exceptions for slave initiated by Doodads (needed to fix repair points on ships)
        if (!zoneNpcCast && targetDist > maxRangeCheck && !unboundedPlacement && !unboundedPlotOnly && target is not Doodad && target is not Slave)
        {
            SkillTlIdManager.ReleaseId(TlId);
            TlId = 0;
            Logger.Info($"TooFarRange targetDist={targetDist}, maxRangeCheck={maxRangeCheck}, SkillTlId {TlId} for Skill {Template.Id}, Caster {caster.Name} ({caster.TemplateId}:{caster.ObjId}) with target {target.Name} ({target.TemplateId}:{target.ObjId})");
            return SkillResult.TooFarRange;
        }

        // If skill uses Plots, then start the plot
        if (Template.Plot != null)
        {
            if (Template.PlotOnly || ForcePlotGraphOnly)
            {
                // plot_only (and World OnSpawn fill) returns before Cast() — apply start costs here.
                // GCD for cast-time plot_only is applied when the plot leaves its casting edge
                // (PlotNode → ApplyPlotOnlyFireCosts). Zone needs WZSkillStarted now (Cast never runs).
                RelayZoneSkillStartedIfNeeded(casterCaster, targetCaster, skillObject);
                ConsumeMana(caster);
                // Arm GCD on press (including 10752's 1000 ms). Waiting until the plot fire-edge
                // left a 850 ms window where hold-repeat started a new Flamebolt and cancelled
                // the one that had not Fired yet.
                ApplyPlotOnlyFireCosts(unit);
                // Do not send SCSkillStarted here. Plot-only Flamebolt (and the rest of that
                // family) already drive the cast bar from SCPlotEvent. SkillStarted with a
                // 1 s RealCastTime locks the whole hotbar, and plot-only never EndSkill's, so
                // hold-to-repeat dies on the first press.
                Task.Run(() => Template.Plot.RunAsync(caster, casterCaster, target, targetCaster, skillObject, this));
                return SkillResult.Success;
            }

            Task.Run(() => Template.Plot.RunAsync(caster, casterCaster, target, targetCaster, skillObject, this));
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
                        var returnId = (uint)specialEffect.Value1;
                        trp = PortalManager.Instance.GetReturnPoint(returnId);
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
            _activeCasterCaster = casterCaster;
            _activeTarget = target;
            _activeTargetCaster = targetCaster;
            _activeSkillObject = skillObject;
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
                {
                    // 457 skills name an item as their target — enchant, dye, socket, extract. The
                    // effects that consume such a cast read the item off targetObj themselves, so what
                    // was missing here is the resolved instance: without it the skill kept the caster as
                    // its target and had no way to say which item the cast was about.
                    if (targetCaster is SkillCastItemTarget itemTarget && itemTarget.Id != 0)
                    {
                        TargetItem = ItemManager.Instance.GetItemByItemId(itemTarget.Id);
                        if (TargetItem == null)
                            Logger.Warn("SkillTargetType.Item: item {0} not found for skill {1}", itemTarget.Id, Template.Id);
                    }

                    break;
                }
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
        ArmCooldowns(unit);

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

        // Per-tick mana drain (channeling_mana), one tick per channeling_tick, cancelled at channel end.
        var tickCount = ChannelingRules.TickCount(Template.ChannelingTime, Template.ChannelingTick, Template.ChannelingMana);
        if (tickCount > 0)
        {
            _channelingTickTask = new ChannelingTickTask(this, caster);
            TaskManager.Instance.Schedule(_channelingTickTask,
                TimeSpan.FromMilliseconds(Template.ChannelingTick),
                TimeSpan.FromMilliseconds(Template.ChannelingTick),
                tickCount);
        }

        _channelingDoodad = doodad;
        _channelingTarget = target;
        _channelingTargetCaster = targetCaster;
        _channelingSkillObject = skillObject;
        unit.SkillTask = new EndChannelingTask(this, caster, casterCaster, target, targetCaster, skillObject, doodad);
        TaskManager.Instance.Schedule(unit.SkillTask, TimeSpan.FromMilliseconds(Template.ChannelingTime));
    }

    /// <summary>
    /// Ends a channel. <paramref name="completedNaturally"/> is what separates a channel that ran its
    /// full <c>channeling_time</c> from one that CSStopCastingPacket, a stun or a death stopped: only the
    /// first applies the skill's effects, which were previously applied by nothing at all.
    /// </summary>
    public void EndChanneling(BaseUnit caster, Doodad channelDoodad, SkillCaster casterCaster, bool completedNaturally = false)
    {
        if (caster is not Unit unit) { return; }
        unit.SkillTask = null;
        CancelChannelingTicks();

        // The channel's own target: StartChanneling records it, and a Skill built without going through
        // it (a plot, a test) still has InitialTarget from Use.
        var channelTarget = _channelingTarget ?? InitialTarget ?? caster;
        var channelTargetCaster = _channelingTargetCaster ?? new SkillCastUnitTarget(channelTarget.ObjId);
        var channelSkillObject = _channelingSkillObject ?? new SkillObject();

        if (Template.ChannelingBuffId != 0)
        {
            caster.Buffs.RemoveEffect(Template.ChannelingBuffId, Template.Id);
        }
        if (Template.ChannelingTargetBuffId != 0)
        {
            channelTarget.Buffs.RemoveEffect(Template.ChannelingTargetBuffId, Template.Id);
        }

        (channelDoodad ?? _channelingDoodad)?.Delete();

        if (ChannelingRules.AppliesEffectsOnEnd(completedNaturally))
        {
            // The channel ran out: hand off to the ordinary fire path, which applies the effects and
            // calls EndSkill itself (directly, or from the ApplySkillTask it schedules). Calling
            // EndSkill here as well would release the TlId twice and end the skill twice. This is the
            // call EndChannelingTask carried commented out.
            ScheduleEffects(caster, casterCaster, channelTarget, channelTargetCaster, channelSkillObject);
        }
        else
        {
            EndSkill(caster);
        }

        // TODO: добавил, так как для квеста 3469 нет события OnItemUse
        // TODO: added since there is no OnItemUse event for quest 3469 and other quests that require the use on non-consuming items
        if (Cancelled == false && casterCaster is SkillItem { ItemTemplateId: > 0 } item && caster is Character player)
        {
            player.ItemUse(item.ItemId);
        }

        unit.Events.OnChannelingCancel(this, new OnChannelingCancelArgs());
    }

    private void CancelChannelingTicks()
    {
        if (_channelingTickTask == null)
            return;

        TaskManager.Instance.Cancel(_channelingTickTask);
        _channelingTickTask = null;
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
        {
            // attack_anim_speed_mul (119) is the animation's own view of the rating that already paced this
            // delay through global_cooldown_mul; a unit without one keeps that factor, and a unit with
            // neither keeps 1.0.
            var animFactor = SpeedMultiplierRules.AnimationFactor(unit.AttackAnimSpeedRating, unit.GlobalCooldownMul / 100.0);
            totalDelay += (int)(Template.FireAnim.CombatSyncTime * animFactor);
        }

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

    private IEnumerable<BaseUnit> FilterAoeUnits(BaseUnit caster, BaseUnit targetSelf, IEnumerable<BaseUnit> units)
    {
        units = SkillTargetingUtil.FilterWithRelation(Template.TargetRelation, caster, units);
        return FilterAoeShape(caster, targetSelf, units);
    }

    /// <summary>
    /// Applies the skill's area shape to the gathered units: the <c>target_area_angle</c> /
    /// <c>front_angle</c> cone, and the corridor a <c>Line</c> selection describes. See
    /// <see cref="SkillAreaRules"/> for the readings and their open questions.
    /// </summary>
    private IEnumerable<BaseUnit> FilterAoeShape(BaseUnit caster, BaseUnit targetSelf, IEnumerable<BaseUnit> units)
    {
        var list = units as List<BaseUnit> ?? units.ToList();

        var halfAngle = SkillAreaRules.ConeHalfAngle(Template.TargetAreaAngle, Template.FrontAngle);
        if (halfAngle > 0d)
        {
            // The bearing is measured from the caster's facing, so a cleave leaves out what is behind it.
            list = list.Where(unit =>
                unit != null &&
                (unit.ObjId == caster.ObjId || SkillAreaRules.IsInsideCone(MathUtil.CalculateAngleFrom(caster, unit), halfAngle)))
                .ToList();
        }

        if (SkillAreaRules.UsesCorridor(Template.TargetSelection) && targetSelf != null)
        {
            var casterPosition = caster.Transform.World.Position;
            var targetPosition = targetSelf.Transform.World.Position;
            var halfWidth = Template.TargetAreaRadius > 0 ? Template.TargetAreaRadius / 2.0 : 0d;
            list = list.Where(unit =>
                unit != null &&
                (unit.ObjId == caster.ObjId || SkillAreaRules.IsWithinCorridor(
                    (casterPosition.X, casterPosition.Y),
                    (targetPosition.X, targetPosition.Y),
                    (unit.Transform.World.Position.X, unit.Transform.World.Position.Y),
                    halfWidth)))
                .ToList();
        }

        return list;
    }

    /// <summary>
    /// Whether a cast is made up purely of special effects that this build has no implementation for.
    /// Such a cast changes nothing at all, so charging the player its reagents would be a straight
    /// loss - awakening scrolls, for one, burn five at a time.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow: as soon as one queued effect does something - any non-special effect, or
    /// a special type that is implemented - the normal consumption path runs, so this cannot be used
    /// to farm an effect for free.
    /// </remarks>
    private static bool IsPureNoOpCast(List<(BaseUnit target, SkillEffect effect)> effectsToApply)
    {
        var sawSpecial = false;

        foreach (var (_, effect) in effectsToApply)
        {
            if (effect.Template is not SpecialEffect special)
                return false;
            if (SpecialEffect.IsImplemented(special.SpecialEffectTypeId))
                return false;
            sawSpecial = true;
        }

        return sawSpecial;
    }

    public void ApplyEffects(BaseUnit caster, SkillCaster casterCaster, BaseUnit targetSelf, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        using var inventoryEffect = (caster as Character)?.Inventory?.EnterSkillEffect();
        ApplyEffectsCore(caster, casterCaster, targetSelf, targetCaster, skillObject);
    }

    private void ApplyEffectsCore(BaseUnit caster, SkillCaster casterCaster, BaseUnit targetSelf, SkillCastTarget targetCaster, SkillObject skillObject)
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
            units = FilterAoeUnits(caster, targetSelf, units).ToList();

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
        // Add origin in case of no targets and using a target position cast. Utility effects (spawn,
        // doodad, ...) need a position to act on; damage and debuffs must not follow this origin, see
        // SkillSelfHitRules - a ground cast that found nobody is not a self-cast.
        var originFallbackOnly = false;
        if (possibleTargets.Count <= 0 && targetCaster is SkillCastPositionTarget)
        {
            possibleTargets.Add(caster);
            originFallbackOnly = true;
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
            if (target is Unit targetUnit && CombatDiceRules.RollsForCast(
                    Template.TargetType == SkillTargetType.Hostile,
                    HasDamageEffect()))
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

                // skill_effects.always_hit: the effect lands whatever the dice said. The result is
                // stored per target, so one always-hit effect on a skill lifts the whole skill's
                // outcome for that target — that is the granularity DamageEffect can read back.
                if (SkillMissedFor(diceResult) && HasAlwaysHitDamageEffect())
                    diceResult = CombatDiceRules.HitTypeFor(Template.DamageTypeId);

                // Auto-attack tasks reuse their Skill instance, so each swing must replace the
                // previous result for this target instead of latching the first hit or miss forever.
                HitTypes[targetUnit.ObjId] = diceResult;
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

                // target_alive / target_dead, the same reading the plot target filter uses. A direct
                // cast had no such filter at all, so an AoE could still land its effects on a corpse.
                if (target is Unit aliveStateUnit &&
                    !SkillUseConditionRules.AllowsTarget(Template.TargetAlive, Template.TargetDead, aliveStateUnit.IsDead))
                {
                    continue;
                }

                // skill_synergy_buff_tags: a synergy-flagged damage effect only lands on a target that
                // carries one of the skill's synergy tags. Every one of the 21 skills with such effects
                // also carries an un-flagged damage effect, so an untagged target still takes the base
                // damage and a tagged one takes the extra rows.
                if (!SkillSynergyRules.AllowsSynergyEffect(
                        effect.Template is DamageEffect { Synergy: true },
                        Template.SynergyBuffTags.Length > 0,
                        TargetHasSynergyTag(target)))
                {
                    continue;
                }
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

                if (effect.TargetBuffTagId > 0)
                {
                    // check_target_tag_src redirects this half of the pair at the caster: the chain skills
                    // (10534 빛과 어둠, 14929 연속 회복, 35717 근접 공격) carry one effect row for the target's
                    // tag and a sibling row for the caster's.
                    var tagOwner = effect.CheckTargetTagSrc ? caster : target;
                    if (!tagOwner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.TargetBuffTagId)))
                        continue;
                }

                if (effect.TargetNoBuffTagId > 0)
                {
                    var noTagOwner = effect.CheckNoTargetTagSrc ? caster : target;
                    if (noTagOwner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(effect.TargetNoBuffTagId)))
                        continue;
                }

                // Buff stack bands: one effect row per band of the caster's or the target's stacks of the
                // corresponding tag (49770/49864/49943/50072 each carry 1..4, 5..15 and 10..15 siblings).
                if (!SkillCombatResourceRules.AllowsStackBand(
                        caster is Unit casterUnit ? casterUnit.Buffs.GetStackCountByTagId(effect.SourceBuffTagId) : 0,
                        effect.SourceBuffStackCountMin, effect.SourceBuffStackCountMax))
                    continue;

                if (!SkillCombatResourceRules.AllowsStackBand(
                        target.Buffs.GetStackCountByTagId(effect.TargetBuffTagId),
                        effect.TargetBuffStackCountMin, effect.TargetBuffStackCountMax))
                    continue;

                if (!SkillCombatResourceRules.AllowsStackBand(
                        caster is Unit exceptCasterUnit ? exceptCasterUnit.Buffs.GetStackCountExceptTagId(effect.SourceBuffTagId) : 0,
                        effect.SourceExceptBuffStackCountMin, effect.SourceExceptBuffStackCountMax))
                    continue;

                if (!SkillCombatResourceRules.AllowsStackBand(
                        target.Buffs.GetStackCountExceptTagId(effect.TargetBuffTagId),
                        effect.TargetExceptBuffStackCountMin, effect.TargetExceptBuffStackCountMax))
                    continue;

                // The target's combat resource band for this effect, named by target_combat_resource_id.
                if (!SkillCombatResourceRules.AllowsEffect(
                        effect.StartCombatResource,
                        effect.EndCombatResource,
                        (int)effect.TargetCombatResourceId,
                        target is Unit resourceTarget ? resourceTarget.GetCombatResource((int)effect.TargetCombatResourceId) : 0))
                    continue;

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

                // start_casting_use_chance..end_casting_use_chance: the shipped rows are the default 1..100
                // except four, and effects land after the cast, so the end value is the one that applies.
                if (!SkillCombatResourceRules.AllowsCastingUseChance(
                        Template.CastingTime, effect.EndCastingUseChance, Random.Shared.NextDouble() * 100d))
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

                // A ground cast that found no unit only carries the caster as its origin; harmful
                // effects stop here so the caster is not hit by their own aimed-at-the-ground skill.
                if (!SkillSelfHitRules.AllowsOriginFallbackTarget(originFallbackOnly, caster.ObjId, target.ObjId, effect))
                    continue;

                // Apply the effect
                effectsToApply.Add((target, effect));
                lastAppliedEffect = effect;
                //effect.Template?.Apply(caster, casterCaster, target, targetCaster, new CastSkill(Template.Id, TlId), new EffectSource(this), skillObject, DateTime.UtcNow, packets);
            }
        }

        // Weighted effects are alternatives. Resolve the same one-roll selection before collecting
        // costs so an unselected branch cannot require or consume its item.
        var weightedTotal = effectsToApply.Sum(entry => entry.effect.Weight);
        if (weightedTotal > 0)
            RetainSelectedWeightedEffect(effectsToApply, Random.Shared.Next(weightedTotal));
        lastAppliedEffect = effectsToApply.LastOrDefault().effect;

        var reagents = SkillManager.Instance.GetSkillReagentsBySkillId(Template.Id);
        var skillProducts = SkillManager.Instance.GetSkillProductsBySkillId(Template.Id);
        var hasExternalItemRows = reagents.Count > 0 || skillProducts.Count > 0;
        if (TryHandleButlerConsumable(
                player,
                casterCaster,
                effectsToApply,
                SingletonContainer.ServiceProvider?.GetService<IButlerChargeService>(),
                hasExternalItemRows))
            return;

        // Handle consumption of items from effects (once per cast — scan ALL queued effects).
        // Using only lastAppliedEffect breaks multi-effect skills: farmer's pouch (23136) applies
        // GainLootPack (consume_source_item=t) then a conditional BuffEffect (consume=f). With a
        // life-skill buff active the buff is last → loot granted, purse never removed.
        if (effectsToApply.Count > 0 && player != null && !IsPureNoOpCast(effectsToApply))
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

            if (!TryQueueEffectItemConsumption(
                    effectsToApply.Select(entry => entry.effect),
                    itemId => checked(
                        player.Inventory.GetItemsCount(SlotType.Inventory, itemId) +
                        player.Inventory.GetItemsCount(SlotType.Equipment, itemId)),
                    consumedItemTemplates))
                return;
        }

        // This will handle all items with a reagent/product
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

        // Apply the effects that need to happen
        foreach (var (target, effect) in effectsToApply)
        {
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
            //
            // Recipe items are excluded: they are use_skill_as_reagent and their link skill (11144) carries no
            // effects either, but whether the item is spent depends on whether it actually taught something -
            // ItemUseActions takes it only when a craft was learned, so an already-known recipe stays in the bag.
            var item = ItemManager.Instance.GetItemByItemId(skillItem.ItemId);
            if (item?.Template.UseSkillAsReagent == true && item.Template.ImplId != ItemImplEnum.Recipe && reagents.Count <= 0 && skillProducts.Count <= 0 && consumedItems.Count <= 0 && Template.Effects.Count == 0)
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
    /// Handles the two paid farmhand consumables whose Butler state and exact source stack must
    /// commit together. Returning true means the cast was wholly handled, including a rejected cast.
    /// </summary>
    internal bool TryHandleButlerConsumable(
        Character player,
        SkillCaster casterCaster,
        IReadOnlyList<(BaseUnit target, SkillEffect effect)> effectsToApply,
        IButlerChargeService service,
        bool hasExternalItemRows)
    {
        var hasButlerEffect = effectsToApply.Any(entry =>
            entry.effect.Template is SpecialEffect special &&
            special.SpecialEffectTypeId is SpecialType.ButlerProductionCostCharge or SpecialType.ButlerAddExp);
        if (!hasButlerEffect)
            return false;

        if (player?.Inventory?.Bag == null || casterCaster is not SkillItem castItem ||
            effectsToApply.Count != 1 || hasExternalItemRows || castItem.ItemId == 0)
        {
            Cancelled = true;
            return true;
        }

        var (target, effect) = effectsToApply[0];
        if (!ReferenceEquals(target, player) || effect.Template is not SpecialEffect specialEffect ||
            specialEffect.Value1 <= 0 || effect.ConsumeItemCount <= 0 || effect.ConsumeItemId != 0)
        {
            Cancelled = true;
            return true;
        }

        var sourceItem = player.Inventory.Bag.GetItemByItemId(castItem.ItemId);
        if (sourceItem == null || sourceItem.Id != castItem.ItemId || sourceItem.TemplateId == 0 ||
            castItem.ItemTemplateId != sourceItem.TemplateId || sourceItem.Template?.UseSkillId != Template.Id ||
            sourceItem.OwnerId != player.Id || !ReferenceEquals(sourceItem._holdingContainer, player.Inventory.Bag) ||
            sourceItem.SlotType != SlotType.Inventory || sourceItem.Count < effect.ConsumeItemCount || service == null)
        {
            Cancelled = true;
            return true;
        }

        var success = specialEffect.SpecialEffectTypeId switch
        {
            // Type 185 owns its paid consumable even though its generic consume_source_item flag is false.
            SpecialType.ButlerProductionCostCharge => service.ChargePaidProductionCost(
                player,
                castItem.ItemId,
                checked((uint)specialEffect.Value1),
                effect.ConsumeItemCount).Success,
            SpecialType.ButlerAddExp when effect.ConsumeSourceItem => service.AddExperience(
                player,
                castItem.ItemId,
                specialEffect.Value1,
                effect.ConsumeItemCount).Success,
            _ => false
        };

        if (!success)
            Cancelled = true;
        return true;
    }

    /// <summary>
    /// Keeps every unweighted effect and the one weighted alternative selected by <paramref name="roll"/>.
    /// </summary>
    internal static void RetainSelectedWeightedEffect(
        List<(BaseUnit target, SkillEffect effect)> effects,
        int roll)
    {
        var cumulativeWeight = 0;
        var selectedIndex = -1;
        for (var i = 0; i < effects.Count; i++)
        {
            var weight = effects[i].effect.Weight;
            if (weight <= 0)
                continue;

            cumulativeWeight += weight;
            if (roll < cumulativeWeight)
            {
                selectedIndex = i;
                break;
            }
        }

        for (var i = effects.Count - 1; i >= 0; i--)
        {
            if (effects[i].effect.Weight > 0 && i != selectedIndex)
                effects.RemoveAt(i);
        }
    }

    /// <summary>
    /// Adds effect item costs only when the player owns the full aggregate amount that the
    /// eventual inventory consumer can remove.
    /// </summary>
    internal bool TryQueueEffectItemConsumption(
        IEnumerable<SkillEffect> effects,
        Func<uint, int> getAvailableCount,
        ICollection<(uint templateId, int amount)> destination)
    {
        var required = new Dictionary<uint, int>();
        var sourceCosts = new List<(uint templateId, int amount)>();

        foreach (var effect in effects)
        {
            if (effect.ConsumeItemId == 0 || effect.ConsumeItemCount <= 0)
                continue;

            if (effect.ConsumeSourceItem)
            {
                // Source-item handling has its own instance checks above; retain its existing cost path.
                sourceCosts.Add((effect.ConsumeItemId, effect.ConsumeItemCount));
                continue;
            }

            required.TryGetValue(effect.ConsumeItemId, out var amount);
            required[effect.ConsumeItemId] = checked(amount + effect.ConsumeItemCount);
        }

        foreach (var (templateId, amount) in required)
        {
            if (getAvailableCount(templateId) >= amount)
                continue;

            Cancelled = true;
            return false;
        }

        foreach (var cost in sourceCosts)
            destination.Add(cost);
        foreach (var cost in required)
            destination.Add((cost.Key, cost.Value));
        return true;
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
            TryConsumeLabor(character);

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

    public int GetLaborCost(Character character)
    {
        // Synthesis charges the skill's labor cost per infusion handled by the effect.
        var laborCost = Template.ConsumeLaborPower * Math.Max(1, LaborUnits);
        if (character?.Actability?.Actabilities.TryGetValue((byte)Template.ActabilityGroupId, out var actAbility) == true)
            laborCost = (int)Math.Round(laborCost * actAbility.GetLaborCostMultiplier());

        laborCost = Math.Min(laborCost, short.MaxValue);

        return Template.ConsumeLaborPower > 0 && laborCost < 1 ? 1 : laborCost;
    }

    public bool TryConsumeLabor(Character character)    {
        if (character == null)
            return false;

        lock (character.StateSyncRoot)
        {
            if (_laborConsumed)
                return true;
            if (Cancelled)
                return false;

            var laborCost = GetLaborCost(character);
            if (laborCost <= 0)
            {
                _laborConsumed = true;
                return true;
            }
            if (laborCost > short.MaxValue || character.LaborPower + character.LocalLaborPower < laborCost)
                return false;

            character.ChangeLabor(checked((short)-laborCost), Template.ActabilityGroupId);
            _laborConsumed = true;
            return true;
        }
    }

    /// <summary>
    /// Whether the character can pay this cast's labor from both pools. See
    /// <see cref="SkillLaborRules"/> for why the cast asks before <see cref="EndSkill"/> debits.
    /// </summary>
    public bool CanAffordLabor(Character character)
    {
        if (character == null)
            return false;

        return SkillLaborRules.CanAfford(
            GetLaborCost(character),
            character.LaborPower,
            character.LocalLaborPower);
    }

    /// <summary>
    /// A hit landed on the unit while this skill was casting or channelling. <c>stop_casting_on_big_hit</c>,
    /// <c>stop_channeling_on_big_hit</c>, <c>casting_cancelable</c>, <c>casting_delayable</c> and formulas
    /// 2/3 decide what happens; <see cref="SkillCastInterruptRules"/> holds the rule.
    /// </summary>
    internal void OnDamageTakenWhileCasting(Unit victim, int damage)
    {
        if (victim == null || Cancelled)
            return;

        var damagePercent = SkillCastInterruptRules.DamagePercent(damage, victim.MaxHp);
        var bigHit = SkillCastInterruptRules.IsBigHit(damagePercent);

        // A channel is not a cast with a cast time: only the channel's own big-hit flag applies, and it
        // cancels the channel rather than delaying it. The tick drain and the TlId are released by Stop.
        if (Template.ChannelingTime > 0)
        {
            if (Template.StopChannelingOnBigHit && bigHit)
            {
                Logger.Debug("Channel {0} on {1} broken by a {2:0.#}% hit", Template.Id, victim.Name, damagePercent);
                Stop(victim, _channelingDoodad);
            }

            return;
        }

        if (Template.CastingTime <= 0)
            return;

        var tolerance = SkillCastInterruptRules.ReadCastingTolerance(victim);
        var cancelPercent = SkillCastInterruptRules.CancelPercent(
            SkillCastInterruptRules.Evaluate(
                damagePercent, tolerance, SkillCastInterruptRules.CastingCancelPercentFormulaId));
        var delayMs = SkillCastInterruptRules.DelayMilliseconds(
            SkillCastInterruptRules.Evaluate(
                damagePercent, tolerance, SkillCastInterruptRules.CastingDelayTimeFormulaId));

        var decision = SkillCastInterruptRules.Decide(
            Template.StopCastingOnBigHit,
            Template.CastingCancelable,
            Template.CastingDelayable,
            damagePercent,
            cancelPercent,
            delayMs,
            Random.Shared.NextDouble() * 100d);

        if (decision.Cancel)
        {
            Logger.Debug("Cast {0} on {1} broken by a {2:0.#}% hit", Template.Id, victim.Name, damagePercent);
            Stop(victim);
            return;
        }

        if (decision.DelayMilliseconds > 0)
        {
            Logger.Debug("Cast {0} on {1} delayed {2} ms by a {3:0.#}% hit",
                Template.Id, victim.Name, decision.DelayMilliseconds, damagePercent);
            DelayActiveCast(victim, decision.DelayMilliseconds);
        }
    }

    /// <summary>
    /// Pushes the running <see cref="CastTask"/> back by <paramref name="delayMilliseconds"/>. The old
    /// task is cancelled and a fresh one scheduled: TaskManager has no reschedule.
    /// </summary>
    private void DelayActiveCast(Unit victim, int delayMilliseconds)
    {
        if (victim.SkillTask is not CastTask running || running.Skill != this)
            return;

        running.Cancel();
        victim.SkillTask = new CastTask(this, victim, _activeCasterCaster, _activeTarget, _activeTargetCaster,
            _activeSkillObject);
        TaskManager.Instance.Schedule(victim.SkillTask, TimeSpan.FromMilliseconds(delayMilliseconds));
    }

    /// <summary>
    /// Used for interrupting skills
    /// </summary>
    /// <param name="caster"></param>
    /// <param name="channelDoodad"></param>
    public void Stop(BaseUnit caster, Doodad channelDoodad = null, SkillCaster casterCaster = null)
    {
        if (caster is not Unit unit) { return; }
        Cancelled = true;
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
        //  -Only Parry if sword equipped?
        var damageType = (DamageType)Template.DamageTypeId;
        // combat_dice_id (8 kinds) says which rolls this cast makes; damage_type_id still says which
        // hit-type flag the client is told. Rows that leave the column at 0 keep the damage-type
        // fallback they had before it was read.
        var diceKind = CombatDiceRules.Kind(Template.CombatDiceId, Template.DamageTypeId);

        // Avoidance (dodge / parry / block), skipped entirely for the undefendable, always-hit and heal
        // kinds — and when the blow comes from behind the target, which cannot see it coming.
        if (Attacker != null && CombatDiceRules.RollsAvoidance(diceKind) && MathUtil.IsFront(attacker, target))
        {
            var bullsEyeMod = Attacker.BullsEye / 1000f * 3f / 100f;

            //TODO Check immunity a better way!!!
            //if (target.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(361)))
            //return SkillHitType.Immune;

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

        // An always_hit / heal kind lands without a roll; a healer's spell is not dodged. A caster that
        // is not a Unit has no accuracy to roll against and keeps the outcome it always had.
        if (Attacker == null)
            return CombatDiceRules.UnrollableSourceType(Template.DamageTypeId);
        if (!CombatDiceRules.RollsMiss(diceKind))
            return CombatDiceRules.HitTypeFor(Template.DamageTypeId);

        var hitChance = damageType switch
        {
            DamageType.Melee => AntiMissRules.HitChance(Attacker.MeleeAccuracy, Attacker.MeleeAntiMissMul),
            DamageType.Magic => AntiMissRules.HitChance(Attacker.SpellAccuracy, Attacker.SpellAntiMissMul),
            DamageType.Ranged => AntiMissRules.HitChance(Attacker.RangedAccuracy, Attacker.RangedAntiMissMul),
            _ => float.MaxValue
        };

        return Random.Shared.Next(0f, 100f) < hitChance
            ? CombatDiceRules.HitTypeFor(Template.DamageTypeId)
            : CombatDiceRules.MissTypeFor(Template.DamageTypeId);
    }

    public bool SkillMissed(uint objId)
    {
        if (HitTypes.TryGetValue(objId, out var hitType))
        {
            return SkillMissedFor(hitType);
        }
        Logger.Error($"Unit[{objId}] was not found in the CbtDiceRolls.");
        return true;
    }

    /// <summary>Whether a dice result means the cast did not land on that unit.</summary>
    /// <remarks>
    /// The spell variants are included: the list used to name only the melee and ranged families, so a
    /// magic cast that rolled SpellMiss or was resisted was still applied by
    /// <see cref="Effects.DamageEffect"/>.
    /// </remarks>
    public static bool SkillMissedFor(SkillHitType hitType) =>
        hitType is SkillHitType.MeleeDodge
            or SkillHitType.MeleeParry
            or SkillHitType.MeleeBlock
            or SkillHitType.MeleeMiss
            or SkillHitType.RangedDodge
            or SkillHitType.RangedParry
            or SkillHitType.RangedBlock
            or SkillHitType.RangedMiss
            or SkillHitType.SpellMiss
            or SkillHitType.SpellResist
            or SkillHitType.Immune;

    /// <summary>Whether any queued effect is a damage effect flagged <c>always_hit</c>.</summary>
    private bool HasAlwaysHitDamageEffect() =>
        Template.Effects.Any(effect => effect.AlwaysHit && effect.Template is DamageEffect);

    /// <summary>
    /// Whether the target carries one of the buff tags <c>skill_synergy_buff_tags</c> lists for this skill.
    /// </summary>
    private bool TargetHasSynergyTag(BaseUnit target)
    {
        var tags = Template.SynergyBuffTags;
        if (target == null || tags.Length == 0)
            return false;

        foreach (var tagId in tags)
        {
            if (tagId > 0 && target.Buffs.CheckBuffTag(tagId))
                return true;
        }

        return false;
    }

    /// <summary>Whether this cast deals damage at all, which is what makes it roll dice.</summary>
    private bool HasDamageEffect() =>
        Template.Effects.Any(effect => effect.Template is DamageEffect);

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
        if (unit == null || _bypassGcd || _plotOnlyFireCostsApplied)
            return;
        if (!Template.PlotOnly && !ForcePlotGraphOnly)
            return;
        _plotOnlyFireCostsApplied = true;
        ApplyGlobalCooldown(unit);
        // Skill cooldown is also applied in DoPlotEnd; applying early matches Cast() and blocks re-cast spam.
        ArmCooldowns(unit);
    }

    /// <summary>
    /// Arms the cast's cooldown on the skill id, on every cooldown tag the skill carries, and on the
    /// account when <c>account_cooldown</c> is set.
    /// </summary>
    /// <remarks>
    /// A <c>switch_to_skill_cooldown</c> variant (10534 빛과 어둠 → 36630/36631) takes over the running
    /// family cooldown instead of its own, so the player cannot use the parent and then reset the
    /// family timer by picking a variant with a shorter cooldown — 36632 연속 회복: 번개 declares 0 ms.
    ///
    /// A charge skill spends one charge here and arms nothing while the pool still has one, so the
    /// second use of a 2-charge skill is immediate; only the last charge spent starts the cooldown.
    /// The spend is tracked on the instance because the plot-only path arms at Use and again at plot
    /// end, and one cast must not cost two charges.
    /// </remarks>
    internal void ArmCooldowns(Unit unit)
    {
        if (unit == null)
            return;

        var duration = Template.CooldownTime > 0 ? (uint)Template.CooldownTime : 0u;
        if (Template.SwitchToSkillCooldown)
        {
            duration = SkillCooldownGateRules.SwitchToCooldownDuration(
                duration,
                unit.Cooldowns.GetRemaining(Template.Id, Template.CooldownTags));
        }

        if (Template.ChargeCount > 1)
        {
            if (_chargesAfterCast < 0)
            {
                _chargesAfterCast = unit.Cooldowns.ConsumeCharge(
                    Template.Id, Template.ChargeCount, Template.ChargeCooldownTime);
            }

            // Charges left: the skill is usable again right away, so neither its own cooldown nor its
            // cooldown tag is armed.
            if (_chargesAfterCast > 0)
                return;
        }

        unit.Cooldowns.AddCooldown(Template.Id, duration, Template.CooldownTags);

        if (Template.AccountCooldown && unit is Character character)
            AccountCooldowns.Arm(character.AccountId, Template.Id, duration);
    }

    /// <summary>
    /// ZoneAuthority WZSkillStarted once per Use. Instant skills must call this from Cast()
    /// before EndSkill zeroes TlId; cast-time/plot_only call it when the cast begins.
    /// </summary>
    private void RelayZoneSkillStartedIfNeeded(SkillCaster casterCaster, SkillCastTarget targetCaster, SkillObject skillObject)
    {
        if (_zoneSkillStartedRelayed || TlId == 0 || SuppressZoneSkillRelay)
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

        if (!SkillCastOverlapRules.ArmsSharedGlobalCooldown(Template.CastingTime, Template.CustomGcd, Template.DefaultGcd))
            return;

        // Length order of authority is custom_gcd → weapon_gcd_id → default_gcd: see SkillGcdRules.
        // weapon_gcd_id names a holdables row (15 한손창 1100 ms, 16 양손창 1200 ms, 17 양손지팡이 1300 ms
        // on 327 skills, none of which carry a custom_gcd), so those skills follow their weapon class
        // instead of the flat server default.
        var weaponGcdSpeed = Template.WeaponGcdId > 0
            ? ItemManager.Instance.GetHoldable((uint)Template.WeaponGcdId)?.Speed ?? 0
            : 0;
        var gcd = SkillGcdRules.ResolveSharedGcd(
            Template.CustomGcd, Template.DefaultGcd, weaponGcdSpeed, unit is Npc);
        if (gcd <= 0)
            return;
        var gcdMul = SkillGcdRules.SharedGcdMultiplier(
            Template.UseWeaponCooldownTime, unit.GlobalCooldownMul, unit.CastTimeMul);
        unit.GlobalCooldown = DateTime.UtcNow.AddMilliseconds(gcd * gcdMul);
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
