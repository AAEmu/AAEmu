using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Justice;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Units;

public class Buffs : IBuffs
{
    // ReSharper disable once InconsistentNaming
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private const int MinimumBuffDurationToSave = 60000;

    // [GEAR-SLOT-FIX] Slot reserved in Unit.Bonuses for gear bonuses (see Unit.UpdateGearBonuses,
    // which does `Bonuses[1] = []`). The buff allocator MUST skip this index, otherwise the first
    // passive buff applied at character load collides with gear and is wiped at login.
    // _nextIndex therefore starts at GearBonusesIndex + 1 and wraps back to it after uint.MaxValue.
    public const uint GearBonusesIndex = 1;
    private const uint FirstBuffIndex = GearBonusesIndex + 1;

    // Reserved fixed slot for guild prestige-shop buff bonuses (see Expedition.ApplyBuffBonuses).
    public const uint ExpeditionBonusesIndex = 0;

    // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
    private readonly object _lock = new();
    private uint _nextIndex;

    private WeakReference _owner;
    private readonly List<Buff> _effects;
    private readonly Dictionary<uint, BuffToleranceCounter> _toleranceCounters;

    public Buffs()
    {
        _nextIndex = FirstBuffIndex; // [GEAR-SLOT-FIX] skip GearBonusesIndex
        _effects = [];
        _toleranceCounters = [];
    }

    public Buffs(BaseUnit owner)
    {
        SetOwner(owner);
        _nextIndex = FirstBuffIndex; // [GEAR-SLOT-FIX] skip GearBonusesIndex
        _effects = [];
        _toleranceCounters = [];
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is refused by an immunity already active on this unit.
    /// </summary>
    /// <remarks>
    /// 10.0.2.13 removed the <c>buffs.immune_buff_tag_id</c> column this used to read, which made the
    /// old check silently false, but the rule did not go away — it moved to
    /// <c>tagged_immune_buffs</c>, which was then loaded nowhere. The owner's side of the lookup is the
    /// tag list of each active buff; the candidate's side is the tag list of the incoming buff. Both
    /// come from <see cref="SkillManager"/>, which already indexes <c>tagged_buffs</c>.
    /// </remarks>
    /// <param name="candidate">The buff about to be applied.</param>
    /// <param name="caster">The unit applying it, used by the <c>immune_except_creator</c> exception.</param>
    /// <param name="castingSkill">
    /// The skill applying it, used by the <c>immune_except_skill_tag_id</c> exception. Null for an
    /// application that is not a cast (a trigger or a combat buff), where that exception cannot apply.
    /// </param>
    public bool CheckBuffImmune(BuffTemplate candidate, BaseUnit caster, Skill castingSkill = null)
    {
        var owner = GetOwner();
        if (owner == null || candidate == null)
            return false;

        var candidateTags = SkillManager.Instance.GetBuffTags(candidate.Id);
        if (candidateTags.Count == 0)
            return false;

        // Create a copy of the list of effects to avoid changing the list while iterating
        Buff[] effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        var casterSkillTags = castingSkill?.Template != null
            ? SkillManager.Instance.GetSkillTags(castingSkill.Template.Id)
            : (IReadOnlyCollection<uint>)Array.Empty<uint>();

        // immune_except_creator_relation_check names one of enum_skill_target_relation's ids, so the
        // relation is resolved with the same helper the targeting code uses. Without a caster there is
        // no relation to test, and the check must not run: IsRelationValid dereferences its caster.
        Func<uint, bool> casterRelationMatches = caster == null
            ? _ => false
            : relationId => SkillTargetingUtil.IsRelationValid((SkillTargetRelation)relationId, caster, owner);

        return BuffImmunityRules.IsRefusedByTagImmunity(
            candidateTags,
            candidate.Id,
            effects,
            SkillManager.Instance.GetBuffImmunityTags,
            caster?.ObjId ?? 0,
            casterSkillTags,
            casterRelationMatches);
    }

    /// <summary>
    /// The first <c>tagged_require_buffs</c> tag this unit does not carry for <paramref name="candidate"/>,
    /// or 0 when every prerequisite is met. 4627 가벼운 발걸음 needs tag 831 무겁다, 21369 선장의 보호
    /// needs tag 3258 순항선, 20111 무적 비행 needs tag 2841 불사조 날틀.
    /// </summary>
    public uint GetMissingRequiredBuffTag(BuffTemplate candidate)
    {
        if (candidate == null)
            return 0;

        return BuffImmunityRules.FirstMissingRequiredTag(
            SkillManager.Instance.GetRequiredBuffTags(candidate.Id), CheckBuffTag);
    }

    /// <summary>
    /// Whether an active buff makes this unit immune to knockback and impulses
    /// (<c>buffs.knockback_immune</c>, 913 rows). Read exactly like <see cref="CheckDamageImmune"/>:
    /// from the flags of the buffs active on this unit.
    /// </summary>
    public bool CheckKnockbackImmune()
    {
        return HasEffectsMatchingCondition(buff => buff?.Template?.KnockbackImmune == true);
    }

    /// <summary>
    /// Whether an active buff makes this unit immune to mana burn
    /// (<c>buffs.mana_burn_immune</c>, 338 rows).
    /// </summary>
    public bool CheckManaBurnImmune()
    {
        return HasEffectsMatchingCondition(buff => buff?.Template?.ManaBurnImmune == true);
    }

    /// <summary>
    /// Tells the caster and the players around this unit that the candidate buff was refused because
    /// the unit is immune to it.
    /// </summary>
    /// <remarks>
    /// The 10.0.2.13 client has no error-message id for buff immunity — <c>enum_error_messages</c> has
    /// 1 244 names and not one of them mentions immunity (the nearest, 787 <c>BUFF_HIGHER</c>, is the
    /// stronger-buff case and already has its own <c>SkillResult.HigherBuff</c>). What the client does
    /// render is the immune hit result, which is what <c>DamageEffect</c> already broadcasts for
    /// <see cref="CheckDamageImmune"/>: <see cref="SkillHitType.Immune"/> (18, <c>immune</c> in
    /// <c>enum_skill_hit_type</c>) on a one-point <c>SCUnitDamagedPacket</c>.
    /// </remarks>
    public void BroadcastBuffImmune(BaseUnit caster, CastAction castObj, SkillCaster casterObj)
    {
        var owner = GetOwner();
        if (owner == null || castObj == null || casterObj == null)
            return;

        // Only a cast says so, and only once. A buff's own tick re-applies its effects with a CastBuff
        // action (BuffTemplate.DoTick / DoAreaTick, BuffTemplate.cs:432 and :475), so an immune unit inside
        // an aura or under a DoT sent one SCUnitDamagedPacket per tick to everyone nearby for the aura's
        // whole life; a buff trigger proc does the same on every proc.
        if (castObj is not CastSkill)
            return;

        owner.BroadcastPacket(
            // Damage 1, not 0: DamageEffect's CheckDamageImmune path sends the same hit type with 1
            // (DamageEffect.cs:114), and the two have to agree on what the client is shown.
            new SCUnitDamagedPacket(castObj, casterObj, caster?.ObjId ?? 0, owner.ObjId, 1, 0)
            {
                HitType = SkillHitType.Immune
            },
            false);
    }

    public bool CheckDamageImmune(DamageType damageType)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
        {
            if (effect == null)
                continue;
            var template = effect.Template;

            if (template == null)
                continue;

            switch (damageType)
            {
                case DamageType.Melee:
                    if (template.MeleeImmune) return true;
                    continue;
                case DamageType.Magic:
                    if (template.SpellImmune) return true;
                    continue;
                case DamageType.Ranged:
                    if (template.RangedImmune) return true;
                    continue;
                case DamageType.Siege:
                    if (template.SiegeImmune) return true;
                    continue;
                default:
                    continue;
            }
        }

        return false;
    }

    public List<Buff> GetEffectsByType(Type effectType)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        var temp = new List<Buff>();
        foreach (var effect in effects.ToList())
            if (effect.Template.GetType() == effectType)
                temp.Add(effect);
        return temp;
    }

    public Buff GetEffectByIndex(uint index)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
            if (effect.Index == index)
                return effect;
        return null;
    }

    public Buff GetEffectByTemplate(BuffTemplate template)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
            if (effect.Template == template)
                return effect;
        return null;
    }

    public bool CheckBuff(uint id)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
            if (effect != null && effect.Template.BuffId > 0 && effect.Template.BuffId == id)
                return true;
        return false;
    }

    public bool CheckBuffTag(uint tagId)
    {
        var buffs = SkillManager.Instance.GetBuffsByTagId(tagId);
        if (buffs == null)
            return false;

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
            if (effect != null && buffs.Contains(effect.Template.BuffId))
                return true;
        return false;
    }

    public Buff GetEffectFromBuffId(uint id)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
            if (effect != null && effect.Template.BuffId > 0 && effect.Template.BuffId == id)
                return effect;
        return null;
    }

    public IEnumerable<Buff> GetBuffsRequiring(uint buffId)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        return effects.Where(b => b.Template.RequireBuffId == buffId);
    }

    public bool CheckBuffs(List<uint> ids)
    {
        if (ids is not { Count: not 0 })
            return false;

        var buffIdsSet = new HashSet<uint>(ids);

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects)
        {
            if (effect?.Template?.BuffId > 0 && buffIdsSet.Contains(effect.Template.BuffId))
                return true;
        }

        return false;
    }
    public int GetBuffCountById(uint buffId)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        // Stacks, not instances. A multiple-stack family is one instance carrying a count, so summing
        // instances would report 1 for a full 60-stack member and undo what the count is read for.
        // This is the family total; whether a wire record wants it or the instance's own count is
        // BuffStackRules.WireStack's decision.
        var count = 0;
        foreach (var effect in effects.ToList())
            if (effect.Template.BuffId == buffId)
                count += Math.Max(1, effect.Stack);
        return count;
    }

    /// <summary>The live instance of a buff family, which is the one that carries its stack count.</summary>
    private Buff FindLiveInstance(uint buffId)
    {
        // The caller holds _lock.
        foreach (var effect in _effects)
            if (effect is { InUse: true } && effect.Template.BuffId == buffId)
                return effect;

        return null;
    }

    /// <summary>The live instance <paramref name="casterKey"/> holds of a per-caster buff family.</summary>
    private Buff FindLiveInstance(uint buffId, uint casterKey)
    {
        // The caller holds _lock.
        foreach (var effect in _effects)
            if (effect is { InUse: true } && effect.Template.BuffId == buffId
                && CasterKeyOf(effect) == casterKey)
                return effect;

        return null;
    }

    /// <summary>Every live instance <paramref name="casterKey"/> holds of a buff family.</summary>
    private List<Buff> LiveInstancesOf(uint buffId, uint casterKey)
    {
        // The caller holds _lock.
        var instances = new List<Buff>();
        foreach (var effect in _effects)
            if (effect is { InUse: true } && effect.Template.BuffId == buffId
                && CasterKeyOf(effect) == casterKey)
                instances.Add(effect);

        return instances;
    }

    /// <summary>
    /// Which caster an instance belongs to, for the rules that keep one instance per caster.
    /// </summary>
    /// <remarks>
    /// The casting unit identifies a player or NPC cast. A source that is not a unit — a doodad, an item
    /// or a mount — still names itself in the skill caster, and the sail-wind family (20860 해풍 응용,
    /// rule 4) arrives that way. An unknown source collapses to zero so every application of it shares
    /// one instance instead of one per arrival, which is the behaviour those families had before the
    /// rule was keyed on a caster at all.
    /// </remarks>
    private static uint CasterKeyOf(Buff buff) =>
        buff?.Caster?.ObjId ?? buff?.SkillCaster?.ObjId ?? 0;

    public void GetAllBuffs(List<Buff> goodBuffs, List<Buff> badBuffs, List<Buff> hiddenBuffs, bool includeAllPassives)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var buff in effects.ToList())
        {
            switch (buff.Template.Kind)
            {
                case BuffKind.Good:
                    if (buff.Passive && !includeAllPassives)
                        continue;
                    goodBuffs.Add(buff);
                    break;
                case BuffKind.Bad:
                    if (buff.Passive && !includeAllPassives)
                        continue;
                    badBuffs.Add(buff);
                    break;
                case BuffKind.Hidden:
                    hiddenBuffs.Add(buff);
                    break;
                default:
                    throw new NotSupportedException(nameof(buff.Template.Kind));
            }
        }
    }

    public void AddBuff(uint buffId, BaseUnit caster)
    {
        var buff = SkillManager.Instance.GetBuffTemplate(buffId);
        var casterObj = new SkillCasterUnit(caster.ObjId);
        AddBuff(new Buff(GetOwner(), caster, casterObj, buff, null, DateTime.UtcNow));
    }

    public void AddBuff(Buff buff, uint index = 0, int forcedDuration = 0)
    {
        Buff transformFrom = null;
        var transformBuffId = 0u;
        // Rule 7's family is several instances; its transform takes all of them, where the
        // single-instance rules have only the one to drop.
        List<Buff> transformFamily = null;
        lock (_lock)
        {
            var owner = GetOwner();
            if (owner == null)
                return;

            buff.State = EffectState.Created;
            if (index == 0)
            {
                buff.Index = AllocateIndex();
            }
            else
            {
                buff.Index = index;
            }

            var buffIds = SkillManager.Instance.GetBuffTags(buff.Template.Id);
            var buffTolerance = buffIds
                .Select(buffId => BuffGameData.Instance.GetBuffToleranceForBuffTag(buffId))
                .FirstOrDefault(t => t != null);

            var toleranceNow = DateTime.UtcNow;
            BuffToleranceCounter toleranceCounter = null;
            if (buffTolerance != null)
            {
                _toleranceCounters.TryGetValue(buffTolerance.Id, out toleranceCounter);

                var toleranceDecision = BuffToleranceRules.Decide(
                    buffTolerance,
                    toleranceCounter,
                    CheckBuff(buffTolerance.FinalStepBuffId),
                    toleranceNow);

                // Already immune to this family: the CC does not land and the counter must not move, or
                // the immunity would end with the ladder part-way down. Bailing out here also keeps the
                // rest of the cast alive — the exception the old create-branch threw on this path
                // escaped the effect loop, dropping every remaining effect and EndSkill with it.
                if (toleranceDecision.Outcome == BuffToleranceOutcome.Immune)
                    return;

                if (toleranceDecision.Outcome == BuffToleranceOutcome.Untracked)
                {
                    toleranceCounter = null;
                }
                else
                {
                    if (toleranceCounter == null)
                    {
                        toleranceCounter = new BuffToleranceCounter { Tolerance = buffTolerance };
                        _toleranceCounters.Add(buffTolerance.Id, toleranceCounter);
                    }

                    toleranceCounter.CurrentStep = toleranceDecision.Step;
                    toleranceCounter.LastStep = toleranceNow;
                }

                // This application is the ladder's immunity step: it is refused and the family's
                // final-step buff goes on in its place. Applying it from here keeps the counter restart
                // and the immunity in one atomic step; AddBuff re-enters this instance's lock on this
                // same thread, and no final_step_buff_id carries a tag that resolves to a tolerance, so
                // this cannot nest any further.
                if (toleranceDecision.Outcome == BuffToleranceOutcome.ImmunityApplied)
                {
                    var immunityTemplate = SkillManager.Instance.GetBuffTemplate(buffTolerance.FinalStepBuffId);
                    if (immunityTemplate != null)
                    {
                        AddBuff(new Buff(buff.Owner, buff.Caster, buff.SkillCaster, immunityTemplate,
                            buff.Skill, DateTime.UtcNow));
                    }

                    return;
                }
            }

            // A buff that lands can break others. buff_breakers(buff_id, buff_tag_id) says a buff carrying
            // buff_tag_id removes buff_id, and this is the point in the order where that happens:
            //   1. the immunity check and the require-tag check refuse the application before AddBuff is
            //      reached at all (BuffEffect.Apply, BuffTemplate.Apply) — a refused buff breaks nothing;
            //   2. the tolerance gate just above drops the CC ladder's immune step and its transform;
            //   3. HERE — the application is accepted, and only the instances already live are removed,
            //      so the arriving buff can never break itself (the 29 rows that name their own buff id
            //      clear the previous instance of a re-grant family instead);
            //   4. the stack rule below then decides what the arrival does to its own family.
            RemoveBuffsBrokenBy(buff);

            buff.Duration = buff.Template.GetDuration(buff.AbLevel);
            if (forcedDuration != 0)
                buff.Duration = forcedDuration;
            if (buff.Caster != null)
            {
                buff.Duration = (int)buff.Caster.BuffModifiersCache.ApplyModifiers(buff.Template, BuffAttribute.Duration, buff.Duration);
            }
            buff.Duration = (int)buff.Owner.BuffModifiersCache.ApplyModifiers(buff.Template, BuffAttribute.InDuration, buff.Duration);

            if (toleranceCounter != null)
            {
                buff.Duration = (int)(buff.Duration * ((100 - toleranceCounter.CurrentStep.TimeReduction) / 100.0));

                if (buff.Caster is Character && buff.Owner is Character)
                    buff.Duration = (int)(buff.Duration * ((100 - buffTolerance.CharacterTimeReduction) / 100.0));
            }

            if (buff.Duration > 0 && buff.StartTime == DateTime.MinValue)
            {
                buff.StartTime = DateTime.UtcNow;
                buff.EndTime = buff.StartTime.AddMilliseconds(buff.Duration);
            }

            Buff last = null;
            switch (buff.Template.StackRule)
            {
                case BuffStackRule.Refresh:
                    foreach (var e in new List<Buff>(_effects))
                        if (e is { InUse: true } && e.Template.BuffId == buff.Template.BuffId)
                        {
                            if (!BuffStackRules.ShouldOverwriteOnRefresh(buff.Duration, e.Duration))
                                return;
                            if (buff.GetTimeLeft() < e.GetTimeLeft())
                                return;
                            last = e;
                        }
                    break;
                case BuffStackRule.ChargeRefresh:
                    foreach (var e in new List<Buff>(_effects))
                        if (e is { InUse: true } && e.Template.BuffId == buff.Template.BuffId)
                            if (buff.Charge < e.Charge)
                                return;
                            else
                                last = e;
                    break;
                case BuffStackRule.Extend:
                    {
                        // Extend ADDS the incoming duration to what is left of the live instance; refresh
                        // replaces it. One instance for the whole family, like Refresh: the 95 shipped
                        // rows are single-source consumables or world effects (4841 연료 주입, 5209 맑은
                        // 정신, 2287 강력한 화염), where a second caster's application is the same effect
                        // continuing rather than a second effect to hold alongside.
                        var live = FindLiveInstance(buff.Template.BuffId);
                        if (live != null)
                        {
                            // A permanent family (duration 0) has nothing to lengthen, and replacing it
                            // would schedule a dispel from GetTimeLeft() == -1, which reads as "already
                            // due". Same guard as Refresh, and it is what keeps 26136 칼리디스 공격력 강화
                            // (duration 0) in place.
                            if (!BuffStackRules.ShouldOverwriteOnRefresh(buff.Duration, live.Duration))
                                return;
                            last = live;
                        }

                        break;
                    }
                case BuffStackRule.ChargeExtend:
                    {
                        // ChargeExtend SUMS the incoming charge into the live instance, held at
                        // max_charge; the timer is left alone, which is what separates it from
                        // ChargeRefresh above (that one only replaces on a higher charge, it never adds).
                        // 864 근성 (max_charge 5,000) and 22574 보호막 (20,000) are that family.
                        var live = FindLiveInstance(buff.Template.BuffId);
                        if (live != null)
                        {
                            // The incoming instance has not been through BuffTemplate.Start yet, so its
                            // charge is still zero and has to be rolled the way Start would have.
                            var incomingCharge = buff.Charge != 0
                                ? buff.Charge
                                : Random.Shared.Next(buff.Template.InitMinCharge, buff.Template.InitMaxCharge);
                            live.AddCharge(BuffStackRules.SummedCharge(
                                live.Charge, incomingCharge, buff.Template.MaxCharge));
                            return;
                        }

                        break;
                    }
                case BuffStackRule.Independent:
                    {
                        // Independent: ONE instance per caster, so two casters' copies of the same buff
                        // coexist instead of the second collapsing onto the first. It never accumulates —
                        // 9,527 of its 9,942 rows author max_stack 1 — so the caster's own re-application
                        // refreshes its instance rather than growing a count; the counting rule is
                        // Multiple below.
                        var live = FindLiveInstance(buff.Template.BuffId, CasterKeyOf(buff));
                        if (live != null)
                        {
                            if (!BuffStackRules.ShouldOverwriteOnRefresh(buff.Duration, live.Duration))
                                return;
                            last = live;
                        }

                        break;
                    }
                case BuffStackRule.Multiple:
                case BuffStackRule.MultipleDecreaseOne:
                default:
                    {
                        // A multiple-stack family is ONE instance carrying a count, not one instance per
                        // application. The client draws an icon per instance and takes the number on it
                        // from the stack field, so an instance per application paints a grid of identical
                        // icons that all read the same total — a two-sail hull showed roughly sixty of
                        // them. Growing the live instance keeps the total effect the same (the bonus is
                        // scaled by the count) while leaving one icon per family, and the ceiling simply
                        // stops it.
                        //
                        // Rule 4 (Multiple) keys that instance on the caster, so a second caster holds its
                        // own: an expiry then takes one instance rather than the whole family. Rule 7
                        // (MultipleDecreaseOne) is the one rule whose applications stay separate instances,
                        // each with its own timer, so they fall off one at a time.
                        //
                        // A rule id this build does not know keeps the family-wide instance it had before
                        // the rules were split out, whatever the caster: nothing new can multiply instances.
                        var casterScoped = BuffStackRules.IsCasterScoped(buff.Template.StackRule);
                        var casterKey = casterScoped ? CasterKeyOf(buff) : 0;

                        if (BuffStackRules.IsInstancePerApplication(buff.Template.StackRule))
                        {
                            // Rule 7 is caster-scoped, so the ceiling counts this caster's instances and
                            // an expiry takes one of them.
                            var instances = LiveInstancesOf(buff.Template.BuffId, casterKey);
                            if (!BuffStackRules.CanAddInstance(instances.Count, buff.Template.MaxStack))
                            {
                                // The family is full. Nine of its 28 rows name a transform (28644 동상 →
                                // 28645 동결 at 10, 32704 생산력 → 32705 at 2), and that consumes the whole
                                // family rather than one member, so the instances are collected here and
                                // dropped together in the tail.
                                if (BuffStackRules.ShouldTransform(
                                        instances.Count, buff.Template.MaxStack, buff.Template.TransformBuffId))
                                {
                                    transformFrom = instances[0];
                                    transformBuffId = buff.Template.TransformBuffId;
                                    transformFamily = instances;
                                    break;
                                }

                                // No transform: the application goes to the instance nearest to expiring —
                                // the stack that is about to fall off — instead of adding a member the
                                // family has no room for. A permanent family has no such instance to
                                // replace, so it simply absorbs the application.
                                var soonest = instances.MinBy(e => e.GetTimeLeft());
                                if (soonest == null ||
                                    !BuffStackRules.ShouldOverwriteOnRefresh(buff.Duration, soonest.Duration))
                                    return;
                                last = soonest;
                            }

                            break;
                        }

                        var live = casterScoped
                            ? FindLiveInstance(buff.Template.BuffId, casterKey)
                            : FindLiveInstance(buff.Template.BuffId);
                        if (live != null)
                        {
                            var grew = live.TryGrowStack(buff.Template.MaxStack);
                            if (BuffStackRules.ShouldTransform(
                                    live.Stack, live.Template.MaxStack, live.Template.TransformBuffId))
                            {
                                transformFrom = live;
                                transformBuffId = live.Template.TransformBuffId;
                                break;
                            }

                            if (grew)
                                return;

                            // At the ceiling. A permanent family has no timer to refresh, so the extra
                            // application is simply absorbed. It must not go through OverwriteWith: that
                            // re-runs SetInUse, which schedules a dispel using the buff's remaining time —
                            // and for a permanent buff that reads as -1, i.e. a delay in the past, so the
                            // buff is dropped the moment it fills. A hull's sails did exactly that, losing
                            // all sixty wind stacks the instant they topped out and rebuilding from one,
                            // which also took the hull's speed back down with them.
                            if (buff.Duration <= 0)
                                return;

                            // A timed family does refresh the member already there rather than adding to it,
                            // so it cannot creep past max_stack.
                            last = live;
                        }

                        break;
                    }
            }
            if (transformBuffId == 0 && last != null)
            {
                // Announce the instance that survives, not the one being discarded. An index is
                // allocated for every arrival before the stack rule decides its fate, so a displacement
                // used to be published under the arrival's brand-new index while the live instance kept
                // its own — leaving observers holding an index this unit does not have, and never
                // retiring it. A ceiling-bound family therefore looked capped here and unbounded to
                // anything downstream (a 60-stack family reached 114 published indices).
                buff.Index = last.Index;
                last.OverwriteWith(buff);
            }
            else if (transformBuffId == 0)
            {
                _effects.Add(buff);
                buff.Triggers.SubscribeEvents();
                buff.SyncSourceDeathSubscription();
                buff.Events.OnBuffStarted(buff, new OnBuffStartedArgs());

                if (buff.Template.BuffId > 0)
                {
                    var buffTemplate = SkillManager.Instance.GetBuffTemplate(buff.Template.BuffId);
                    owner.SkillModifiersCache.AddModifiers(buff.Template.BuffId);
                    owner.BuffModifiersCache.AddModifiers(buff.Template.BuffId);
                    owner.CombatBuffs.AddCombatBuffs(buff.Template.BuffId);

                    if (owner is Character { IsRiding: true } character && (buffTemplate.Stun || buffTemplate.Sleep || buffTemplate.Root))
                    {
                        var mateList = character.ParentWorld.MateManager.GetActiveMates(character.Id);
                        foreach (var mate in mateList)
                        {
                            // TODO: handle passengers
                            character.ParentWorld.MateManager.UnMountMate(character, mate.TlId, AttachPointKind.Driver, AttachUnitReason.None);
                        }
                    }

                    if (buffTemplate.Stun || buffTemplate.Silence || buffTemplate.Sleep)
                        owner.InterruptSkills();
                }

                //if (buff.Duration > 0)
                if (buff.Duration > 0 || buff.Template.TickEffects.Count > 0)
                    buff.SetInUse(true, false);
                else
                {
                    buff.InUse = true;
                    buff.State = EffectState.Acting;
                    buff.Template.Start(buff.Caster, owner, buff); // TODO поменять на target
                }

                // If Owner has buffs that prevent it from doing combat, then remove the aggro for it
                if (buffIds.Contains((uint)TagsEnum.NoFight) || buffIds.Contains((uint)TagsEnum.Returning))
                {
                    // Unit entered a "safe zone"
                    if (owner is Unit unit)
                    {
                        unit.ClearAllAggro();
                        unit.IsInBattle = false;
                    }
                }
            }
        }
        if (transformBuffId > 0 && transformFrom != null)
        {
            if (transformFamily != null)
            {
                // Rule 7: every instance of the family goes, not just the one RemoveBuff would take.
                foreach (var instance in transformFamily)
                    RemoveEffect(instance);
            }
            else
            {
                RemoveBuff(transformFrom.Template.BuffId);
            }

            var nextTemplate = SkillManager.Instance.GetBuffTemplate(transformBuffId);
            if (nextTemplate != null)
            {
                AddBuff(new Buff(
                    transformFrom.Owner,
                    transformFrom.Caster,
                    transformFrom.SkillCaster,
                    nextTemplate,
                    transformFrom.Skill,
                    DateTime.UtcNow));
            }
        }

        if (buff.Template.BuffId == SportFishCombat.LineBrokenBuffId && GetOwner() is Npc lineFish)
            SportFishCombat.OnLineDropped(lineFish);

        // An arrest-state buff on a player starts the justice flow (escort to court, then the
        // imprison-or-trial offer). Guarded by the buff's own shipped length.
        if (GetOwner() is Character arrested && ArrestRules.IsArrestStateBuff(buff.Template.BuffId))
            JusticeManager.Instance.OnArrestStateApplied(arrested);
    }

    private uint AllocateIndex()
    {
        // The caller holds _lock. Do not reuse an index after a long-running server wraps,
        // and never allocate the Unit.Bonuses slot reserved for gear effects.
        while (true)
        {
            var candidate = _nextIndex;
            _nextIndex = candidate == uint.MaxValue ? FirstBuffIndex : candidate + 1;

            if (candidate != GearBonusesIndex && _effects.All(effect => effect?.Index != candidate))
                return candidate;
        }
    }

    /// <summary>
    /// Ends the live buffs that <paramref name="arriving"/> breaks (<c>buff_breakers</c>).
    /// </summary>
    /// <remarks>
    /// The removal is by buff id and takes every live instance of it, whatever caster holds it. The rows
    /// name a buff, not a caster's copy of it, and the caster of the arriving buff is usually not the
    /// caster of the victims — a stun from an enemy ends the song the target is performing — so scoping
    /// the removal to the arriving buff's caster would leave exactly the buffs the table exists to end.
    /// The arriving instance itself is not in <c>_effects</c> yet (see the call site), which is what keeps
    /// a self-referential row from removing the buff that just landed.
    /// </remarks>
    private void RemoveBuffsBrokenBy(Buff arriving)
    {
        // The caller holds _lock.
        var tags = SkillManager.Instance.GetBuffTags(arriving?.Template?.Id ?? 0);
        if (tags.Count == 0)
            return;

        var victimIds = new HashSet<uint>();
        foreach (var tag in tags)
            foreach (var brokenId in SkillManager.Instance.GetBuffsBrokenByTag(tag))
                victimIds.Add(brokenId);

        if (victimIds.Count == 0)
            return;

        // One pass over the live list, so every instance of a victim family goes rather than the first
        // one found, and the arriving instance is skipped for the reason above.
        List<Buff> victims = null;
        foreach (var live in _effects)
        {
            if (live is not { InUse: true } || ReferenceEquals(live, arriving))
                continue;
            if (!victimIds.Contains(live.Template.BuffId))
                continue;

            victims ??= [];
            victims.Add(live);
        }

        if (victims == null)
            return;

        foreach (var victim in victims)
        {
            Logger.Debug("Buff {0} breaks buff {1}", arriving.Template.BuffId, victim.Template.BuffId);
            victim.Exit();
        }
    }

    public void RemoveEffect(Buff buff)
    {
        var own = GetOwner();
        if (own == null)
            return;

        if (buff == null || _effects?.Contains(buff) != true)
            return;

        if (_effects == null)
            return;

        lock (_lock)
        {
            buff.SetInUse(false, false);
            _effects.Remove(buff);
            own.SkillModifiersCache.RemoveModifiers(buff.Template.BuffId);
            own.BuffModifiersCache.RemoveModifiers(buff.Template.BuffId);
            own.CombatBuffs.RemoveCombatBuff(buff.Template.BuffId);
            //effect.Triggers.UnsubscribeEvents();

            if (buff.Template.Gliding)
                TriggerRemoveOn(BuffRemoveOn.Land);
        }
    }

    public void RemoveEffect(uint templateId, uint skillId)
    {
        var own = GetOwner();
        if (own == null)
            return;

        if (_effects == null)
            return;

        lock (_lock)
        {
            foreach (var e in _effects.ToList())
            {
                if (e != null && e.Template.Id == templateId && e.Skill.Template.Id == skillId)
                {
                    e.Template.Dispel(e.Caster, e.Owner, e);
                    _effects.Remove(e);
                    e.SetInUse(false, false);
                    own.SkillModifiersCache.RemoveModifiers(e.Template.BuffId);
                    own.BuffModifiersCache.RemoveModifiers(e.Template.BuffId);
                    own.CombatBuffs.RemoveCombatBuff(e.Template.BuffId);
                    //e.Triggers.UnsubscribeEvents();
                }
            }
        }
    }

    public void RemoveEffect(uint index, bool notifyZone = true)
    {
        var own = GetOwner();
        if (own == null)
            return;

        if (_effects == null)
            return;

        lock (_lock)
        {
            foreach (var e in _effects.ToList())
            {
                if (e?.Index == index)
                {
                    e.Template.Dispel(e.Caster, e.Owner, e, notifyZone: notifyZone);
                    _effects.Remove(e);
                    e.SetInUse(false, false);
                    own.SkillModifiersCache.RemoveModifiers(e.Template.BuffId);
                    own.BuffModifiersCache.RemoveModifiers(e.Template.BuffId);
                    own.CombatBuffs.RemoveCombatBuff(e.Template.BuffId);
                    //e.Triggers.UnsubscribeEvents();
                    break;
                }
            }
        }
    }

    public void RemoveBuff(uint buffId, bool notifyZone = true)
    {
        var own = GetOwner();
        if (own == null)
            return;

        if (_effects == null)
            return;

        lock (_lock)
        {
            foreach (var e in _effects.ToList())
            {
                if (e != null && e.Template.BuffId == buffId)
                {
                    e.Template.Dispel(e.Caster, e.Owner, e, notifyZone: notifyZone);
                    _effects.Remove(e);
                    e.SetInUse(false, false);
                    own.SkillModifiersCache.RemoveModifiers(e.Template.BuffId);
                    own.BuffModifiersCache.RemoveModifiers(e.Template.BuffId);
                    own.CombatBuffs.RemoveCombatBuff(e.Template.BuffId);
                    //e.Triggers.UnsubscribeEvents();
                    // WZBuffRemoved is sent from BuffTemplate.Dispel with buff.Index (not template id).
                    break;
                }
            }
        }
    }

    public void RemoveBuffs(BuffKind kind, int count, uint buffTagId = 0)
    {
        var own = GetOwner();
        if (own == null)
            return;

        var taggedBuffs = SkillManager.Instance.GetBuffsByTagId(buffTagId);

        if (_effects == null)
            return;

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var buff in effects.ToList())
            if (buff != null)
            {
                var buffTemplate = buff.Template;

                if (buffTemplate == null)
                    continue;

                if (buffTagId == 0 && buffTemplate.System)
                    continue;
                if (buffTagId == 0 && buffTemplate.Kind != kind)
                    continue;
                if (buffTagId > 0 && !taggedBuffs.Contains(buffTemplate.Id))
                    continue;

                buff.Exit();
                count--;
                if (count == 0)
                    return;
            }
    }

    public void RemoveBuffs(uint buffTagId, int count)
    {
        var own = GetOwner();
        if (own == null)
            return;

        if (_effects == null)
            return;

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        var buffIds = SkillManager.Instance.GetBuffsByTagId(buffTagId);
        foreach (var e in effects.ToList())
            if (e != null)
            {
                if (!buffIds.Contains(e.Template.BuffId))
                    continue;

                e.Exit();
                count--;
                if (count == 0)
                    return;
            }
    }

    public void RemoveAllEffects()
    {
        var own = GetOwner();
        if (own == null)
            return;

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var e in effects.ToList())
            if (e != null /* && (e.Template.Skill == null || e.Template.Skill.Type != SkillTypes.Passive)*/)
                e.Exit();
    }

    /// <summary>
    /// Ends every active buff applied by <paramref name="skillId"/> through its natural-timeout path
    /// (triggers fire, buff removed). Used when a seat is left: rides such as the house floor mover are
    /// driven by the seat buff's Timeout trigger (skills.id 40228 '층간 이동' applies it and its trigger
    /// casts the ride skill), and the ride happens on leaving the seat, not after the buff's full
    /// duration. Other buff removals keep the natural-expiry rule.
    /// </summary>
    public void TimeoutBuffsFromSkill(uint skillId)
    {
        if (skillId == 0)
            return;

        List<Buff> snapshot;
        lock (_lock)
        {
            snapshot = _effects.ToList();
        }

        foreach (var buff in snapshot)
        {
            if (buff.State == EffectState.Finished || buff.Skill?.Id != skillId)
                continue;

            Logger.Debug("Timing out buff {0} on seat release (skill {1})",
                buff.Template?.Id ?? 0, skillId);
            buff.TimeOut();
        }
    }

    /// <summary>
    /// Ends every live instance whose template removes on <paramref name="on"/>.
    /// </summary>
    /// <remarks>
    /// Each instance is decided on its own, which is what keeps a per-caster family honest: two casters
    /// hold two instances and an event that names one caster (<see cref="BuffRemoveOn.SourceDead"/>, whose
    /// value is the dead unit's object id) ends that caster's instance only. The decisions themselves live
    /// in <see cref="BuffRemoveOnRules.Matches"/>. <paramref name="value"/> carries the event's payload:
    /// the dead unit for <c>SourceDead</c>, the started skill's tag for <c>StartSkill</c>, the
    /// <c>enum_attach_point</c> seat for <c>Unmount</c>, and the changed <c>enum_equip_slot</c> for
    /// <c>ChangeEquipments</c>.
    /// </remarks>
    public void TriggerRemoveOn(BuffRemoveOn on, uint value = 0)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects.ToList())
        {
            var template = effect?.Template;
            if (template == null)
                continue;

            // A source that is not a unit (a doodad, an item, a mount's own cast) has no object id to be
            // named by, so it collapses to 0 the same way the caster key does in the stack rules.
            var casterObjId = effect.Caster?.ObjId ?? 0;
            Func<uint, bool> carriesTag = on == BuffRemoveOn.StartSkill
                ? tag => SkillManager.Instance.GetBuffTags(template.BuffId).Contains(tag)
                : null;

            if (BuffRemoveOnRules.Matches(on, template, value, casterObjId, carriesTag))
                effect.Exit();
        }
    }

    public void RemoveEffectsOnDeath()
    {
        var own = GetOwner();
        if (own == null)
            return;

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var e in effects.ToList())
            if (e != null && e.Template.RemoveOnDeath)
                e.Exit();

        // remove_on_source_dead (749 buffs) is the other half of death: the buffs this unit applied end
        // with it. On its own list that is the ones it applied to itself, and this pass is what covers a
        // death that never subscribed (a buff restored from the database, or a non-unit source that
        // collapsed to caster 0). The ones it applied to other units are ended by the subscription each
        // instance takes on its caster's OnDeath — see Buff.SyncSourceDeathSubscription.
        TriggerRemoveOn(BuffRemoveOn.SourceDead, own.ObjId);
    }

    public void SetOwner(BaseUnit owner)
    {
        _owner = owner == null ? null : new WeakReference(owner);
    }

    public void RemoveStealth()
    {
        var own = GetOwner();
        if (own == null)
            return;

        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var e in effects.ToList())
        {
            if (e == null || !e.Template.Stealth)
                continue;

            // Before Exit(): exiting unsubscribes this buff's triggers, and a `remove_stealth` row is
            // one of them.
            e.Events.OnStealthRemoved(e, new OnStealthRemovedArgs());
            e.Exit();
        }
    }

    private BaseUnit GetOwner()
    {
        return _owner?.Target as BaseUnit;
    }

    public IEnumerable<Buff> GetAbsorptionEffects()
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        return effects.Where(e => e.Template.DamageAbsorptionTypeId > 0);
    }

    public bool HasEffectsMatchingCondition(Func<Buff, bool> predicate)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        return effects.Any(predicate);
    }

    #region Buff Persistence
    /// <summary>
    /// Determines whether a buff should be saved to the database on logout.
    /// </summary>
    private static bool ShouldPersistBuff(Buff buff)
    {
        if (buff == null)
            return false;

        // Only save buffs with SaveRuleId > 0
        if (buff.Template.SaveRuleId == BuffSaveRuleType.DontSave)
            return false;

        // Passive buffs are restored via the skill system
        if (buff.Passive)
            return false;

        // Permanent buffs (Duration=0) are race/template buffs
        if (buff.Duration <= 0)
            return false;

        // Don't save already expired buffs
        if (buff.GetTimeLeft() <= 0)
            return false;

        // Don't save buffs in Finishing/Finished state
        if (buff.State == EffectState.Finishing || buff.State == EffectState.Finished)
            return false;

        // --- SaveRule differentiation ---

        // Rule 2: Premium/Crafting (Cash-Shop, Mastery, Proficiency) → always save
        // Rule 3: Special (Inn/Sleep, Battlefield, Cosmetics) → always save
        if (buff.Template.SaveRuleId >= BuffSaveRuleType.CharacterPersistent)
            return true;

        // Rule 1: Standard buffs — only save beneficial buffs with ≥60s duration.
        // Filters out short combat debuffs (stuns, bleeds, knockdowns).
        if (buff.Template.SaveRuleId == BuffSaveRuleType.Normal)
        {
            // Don't save debuffs (combat effects should not persist through logout)
            if (buff.Template.Kind == BuffKind.Bad)
                return false;

            // Very short buffs (< 60s) are combat abilities, not consumables
            if (buff.Duration < MinimumBuffDurationToSave)
                return false;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Saves all persistable active buffs to the database.
    /// Called during Character.Save().
    /// </summary>
    public void SaveActiveBuffs(MySqlConnection connection, MySqlTransaction transaction, uint characterId)
    {
        try
        {
            // Delete previously saved buffs
            using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.Connection = connection;
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = "DELETE FROM `character_active_buffs` WHERE `character_id` = @characterId";
                deleteCmd.Parameters.AddWithValue("@characterId", characterId);
                deleteCmd.ExecuteNonQuery();
            }

            // Snapshot active buffs
            Buff[] effects;
            lock (_lock)
            {
                effects = _effects.ToArray();
            }

            var savedCount = 0;
            foreach (var buff in effects)
            {
                if (!ShouldPersistBuff(buff))
                    continue;

                var timeLeft = (int)buff.GetTimeLeft();
                if (timeLeft <= 0)
                    continue;

                using var cmd = connection.CreateCommand();
                cmd.Connection = connection;
                cmd.Transaction = transaction;
                cmd.CommandText =
                    "INSERT INTO `character_active_buffs` " +
                    "(`character_id`, `buff_id`, `caster_id`, `skill_id`, `ab_level`, " +
                    " `duration`, `time_left`, `charge`, `stack_count`, `real_time`, `saved_at`) " +
                    "VALUES (@characterId, @buffId, @casterId, @skillId, @abLevel, " +
                    "        @duration, @timeLeft, @charge, @stackCount, @realTime, @savedAt) " +
                    "ON DUPLICATE KEY UPDATE " +
                    "`caster_id`=@casterId, `skill_id`=@skillId, `ab_level`=@abLevel, " +
                    "`duration`=@duration, `time_left`=@timeLeft, `charge`=@charge, " +
                    "`stack_count`=@stackCount, `real_time`=@realTime, `saved_at`=@savedAt";

                cmd.Parameters.AddWithValue("@characterId", characterId);
                cmd.Parameters.AddWithValue("@buffId", buff.Template.Id);
                cmd.Parameters.AddWithValue("@casterId", buff.Caster?.Id ?? characterId);
                cmd.Parameters.AddWithValue("@skillId", buff.Skill?.Template?.Id ?? 0u);
                cmd.Parameters.AddWithValue("@abLevel", buff.AbLevel);
                cmd.Parameters.AddWithValue("@duration", buff.Duration);
                cmd.Parameters.AddWithValue("@timeLeft", timeLeft);
                cmd.Parameters.AddWithValue("@charge", buff.Charge);
                cmd.Parameters.AddWithValue("@stackCount", 1);
                cmd.Parameters.AddWithValue("@realTime", buff.Template.RealTime ? 1 : 0);
                cmd.Parameters.AddWithValue("@savedAt", DateTime.UtcNow);
                cmd.ExecuteNonQuery();
                savedCount++;
            }

            if (savedCount > 0)
                Logger.Info($"Saved {savedCount} active buff(s) for character {characterId}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to save active buffs for character {characterId}");
        }
    }

    /// <summary>
    /// Loads saved buffs from the database and reapplies them to the character.
    /// RealTime buffs: offline time is subtracted.
    /// GameTime buffs: timer was paused, full remaining time is restored.
    /// </summary>
    public void LoadActiveBuffs(Character character)
    {
        try
        {
            var restoredCount = 0;

            using var connection = MySQL.CreateConnection();
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT * FROM `character_active_buffs` WHERE `character_id` = @characterId";
                cmd.Parameters.AddWithValue("@characterId", character.Id);

                using var reader = cmd.ExecuteReader();
                var buffRows = new List<(uint buffId, uint casterId, uint skillId,
                    uint abLevel, int duration, int timeLeft, int charge,
                    bool realTime, DateTime savedAt)>();

                while (reader.Read())
                {
                    buffRows.Add((
                        buffId:   reader.GetUInt32("buff_id"),
                        casterId: reader.GetUInt32("caster_id"),
                        skillId:  reader.GetUInt32("skill_id"),
                        abLevel:  reader.GetUInt32("ab_level"),
                        duration: reader.GetInt32("duration"),
                        timeLeft: reader.GetInt32("time_left"),
                        charge:   reader.GetInt32("charge"),
                        realTime: reader.GetBoolean("real_time"),
                        savedAt:  reader.GetDateTime("saved_at")
                    ));
                }

                reader.Close();

                foreach (var row in buffRows)
                {
                    var buffTemplate = SkillManager.Instance.GetBuffTemplate(row.buffId);
                    if (buffTemplate == null)
                    {
                        Logger.Warn($"LoadActiveBuffs: BuffTemplate {row.buffId} not found, skipping");
                        continue;
                    }

                    // Calculate remaining time
                    int remainingMs;
                    if (row.realTime)
                    {
                        // RealTime: subtract offline time
                        var offlineMs = (int)(DateTime.UtcNow - row.savedAt).TotalMilliseconds;
                        remainingMs = row.timeLeft - offlineMs;

                        if (remainingMs <= 0)
                        {
                            Logger.Debug($"LoadActiveBuffs: RealTime buff {row.buffId} expired offline");
                            continue;
                        }
                    }
                    else
                    {
                        // GameTime: timer was paused → restore full remaining time
                        remainingMs = row.timeLeft;
                    }

                    if (remainingMs <= 0)
                        continue;

                    // Check if buff is already active (e.g. from passive skills)
                    if (CheckBuff(row.buffId))
                    {
                        Logger.Debug($"LoadActiveBuffs: Buff {row.buffId} already active, skipping");
                        continue;
                    }

                    // Create and apply the buff
                    var casterObj = new SkillCasterUnit(character.ObjId);
                    var buff = new Buff(character, character, casterObj,
                        buffTemplate, null, DateTime.UtcNow)
                    {
                        AbLevel  = row.abLevel,
                        Duration = remainingMs,
                        Charge   = row.charge > 0 ? row.charge : buffTemplate.InitMinCharge
                    };

                    AddBuff(buff, forcedDuration: remainingMs);
                    restoredCount++;

                    Logger.Debug($"LoadActiveBuffs: Restored buff {row.buffId} ({remainingMs}ms) for char {character.Id}");
                }
            }

            // Delete saved buffs — they are now active again
            using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.CommandText =
                    "DELETE FROM `character_active_buffs` WHERE `character_id` = @characterId";
                deleteCmd.Parameters.AddWithValue("@characterId", character.Id);
                deleteCmd.ExecuteNonQuery();
            }

            if (restoredCount > 0)
                Logger.Info($"Restored {restoredCount} buff(s) for character {character.Id} ({character.Name})");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to load active buffs for character {character.Id}");
        }
    }

    /// <summary>
    /// Cancels all running buff timers (DispelTasks) without triggering the normal
    /// dispel/exit flow. Called during logout.
    /// </summary>
    public void CancelAllEffectTasks()
    {
        Buff[] effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var buff in effects)
        {
            if (buff == null)
                continue;

            try
            {
                TaskManager.Instance.RemoveTasks(task =>
                {
                    if (task is AAEmu.Game.Models.Tasks.Skills.DispelTask dt
                        && dt.Effect.Target is Buff b)
                        return b == buff;
                    return false;
                });
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Error cancelling tasks for buff {buff.Template?.Id}");
            }
        }
    }
    #endregion
}
