using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Units;

public class Buffs : IBuffs
{
    private readonly object _lock = new();
    private uint _nextIndex;

    private WeakReference _owner;
    private readonly List<Buff> _effects;
    private readonly Dictionary<uint, BuffToleranceCounter> _toleranceCounters;

    public Buffs()
    {
        _nextIndex = 1;
        _effects = new List<Buff>();
        _toleranceCounters = new Dictionary<uint, BuffToleranceCounter>();
    }

    public Buffs(BaseUnit owner)
    {
        SetOwner(owner);
        _nextIndex = 1;
        _effects = new List<Buff>();
        _toleranceCounters = new Dictionary<uint, BuffToleranceCounter>();
    }

    public bool CheckBuffImmune(uint buffId)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects)
        {
            if (effect == null)
                continue;

            if (effect.Template.ImmuneBuffTagId == 0)
                continue;

            var buffs = SkillManager.Instance.GetBuffsByTagId(effect.Template.ImmuneBuffTagId);
            if (buffs != null && buffs.Contains(buffId))
                return true;
        }

        return false;
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
            var template = effect?.Template;

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
            if (effect?.Template?.BuffId > 0 && buffIdsSet.Contains(effect.Template.BuffId))
                return true;

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

        var count = 0;
        foreach (var effect in effects.ToList())
            if (effect.Template.BuffId == buffId)
                count++;
        return count;
    }

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
                    // Always include passives of Hidden Buffs, required by for example WorldBoss and Queen Bee captures
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
        if (buff?.Template is null)
        {
            return;
        }
        var finalToleranceBuffId = 0u;
        lock (_lock)
        {
            var owner = GetOwner();
            if (owner == null)
                return;

            buff.State = EffectState.Created;
            if (index == 0)
            {
                buff.Index = _nextIndex; // TODO need safe increment...

                if (++_nextIndex == uint.MaxValue)
                    _nextIndex = 1;
            }
            else
                buff.Index = index;

            var buffIds = SkillManager.Instance.GetBuffTags(buff.Template.Id);
            var buffTolerance = buffIds
                .Select(buffId => BuffGameData.Instance.GetBuffToleranceForBuffTag(buffId))
                .FirstOrDefault(t => t != null);
            if (buffTolerance != null && _toleranceCounters.ContainsKey(buffTolerance.Id) && !CheckBuff(buffTolerance.FinalStepBuffId))
            {
                var counter = _toleranceCounters[buffTolerance.Id];
                if (DateTime.UtcNow > counter.LastStep + TimeSpan.FromSeconds(buffTolerance.StepDuration))
                    counter.CurrentStep = buffTolerance.GetFirstStep();
                else
                {
                    var nextStep = buffTolerance.GetStepAfter(counter.CurrentStep);
                    if (nextStep.TimeReduction <= counter.CurrentStep.TimeReduction)
                    {
                        // Apply immune buff
                        finalToleranceBuffId = counter.Tolerance.FinalStepBuffId;
                        // reset to first
                        counter.CurrentStep = buffTolerance.GetFirstStep();
                    }
                    else
                        counter.CurrentStep = nextStep;
                }

                counter.LastStep = DateTime.UtcNow;
            }
            else if (buffTolerance != null)
            {
                var btc = new BuffToleranceCounter
                {
                    Tolerance = buffTolerance,
                    CurrentStep = buffTolerance.GetFirstStep(),
                    LastStep = DateTime.UtcNow
                };
                _toleranceCounters[buffTolerance.Id] = btc;
            }

            buff.Duration = buff.Template.GetDuration(buff.AbLevel);
            buff.Duration = (int)buff.Caster.BuffModifiersCache.ApplyModifiers(buff.Template, BuffAttribute.Duration, buff.Duration);
            buff.Duration = (int)buff.Owner.BuffModifiersCache.ApplyModifiers(buff.Template, BuffAttribute.InDuration, buff.Duration);

            if (buffTolerance != null)
            {
                var counter = _toleranceCounters[buffTolerance.Id];
                buff.Duration = (int)(buff.Duration * ((100 - counter.CurrentStep.TimeReduction) / 100.0));

                if (buff.Caster is Character && buff.Owner is Character)
                    buff.Duration = (int)(buff.Duration * ((100 - buffTolerance.CharacterTimeReduction) / 100.0));
            }

            if (forcedDuration != 0)
                buff.Duration = forcedDuration;

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
                        if (e != null && e.InUse && e.Template.BuffId == buff.Template.BuffId)
                            if (buff.GetTimeLeft() < e.GetTimeLeft())
                                return;
                            else
                                last = e;
                    break;
                case BuffStackRule.ChargeRefresh:
                    foreach (var e in new List<Buff>(_effects))
                        if (e != null && e.InUse && e.Template.BuffId == buff.Template.BuffId)
                            if (buff.Charge < e.Charge)
                                return;
                            else
                                last = e;
                    break;
                case BuffStackRule.Multiple:
                    if (buff.Template.MaxStack > 0 && GetBuffCountById(buff.Template.BuffId) < buff.Template.MaxStack)
                    {
                        buff.Stack = GetBuffCountById(buff.Template.BuffId) +1;
                    }
                    goto default;
                case BuffStackRule.ChargeExtend:
                case BuffStackRule.Extend:
                case BuffStackRule.Independent:
                default:
                    if (buff.Template.MaxStack > 0 && GetBuffCountById(buff.Template.BuffId) >= buff.Template.MaxStack)
                        foreach (var e in new List<Buff>(_effects))
                            if (e != null && e.InUse && e.Template.BuffId == buff.Template.BuffId)
                                if (e.GetTimeLeft() < buff.GetTimeLeft())
                                    last = e;
                    break;
            }
            last?.Exit(index > 0 && last.Template.Id == buff.Template.Id);

            _effects.Add(buff);
            buff.Triggers.SubscribeEvents();
            buff.Events.OnBuffStarted(buff, new OnBuffStartedArgs());

            if (buff.Template.BuffId > 0)
            {
                var bufft = SkillManager.Instance.GetBuffTemplate(buff.Template.BuffId);
                owner.SkillModifiersCache.AddModifiers(buff.Template.BuffId);
                owner.BuffModifiersCache.AddModifiers(buff.Template.BuffId);
                owner.CombatBuffs.AddCombatBuffs(buff.Template.BuffId);

                if (owner is Character character && character.IsRiding && (bufft.Stun || bufft.Sleep || bufft.Root))
                {
                    var mates = MateManager.Instance.GetActiveMates(character.ObjId);
                    if (mates != null)
                    {
                        foreach (var mate in mates.Where(mate => mate is { MateType: MateType.Ride }))
                            MateManager.Instance.UnMountMate(character.Connection, mate.TlId, AttachPointKind.Driver, AttachUnitReason.None);
                    }
                }

                if (bufft.Stun || bufft.Silence || bufft.Sleep)
                    owner.InterruptSkills();
            }

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
                if (owner is Npc { Ai: not null } npc)
                    npc.ClearAllAggro();

                if (owner is Unit unit)
                    unit.IsInBattle = false;
            }
        }

        if (finalToleranceBuffId > 0)
            AddBuff(new Buff(buff.Owner, buff.Caster, buff.SkillCaster, SkillManager.Instance.GetBuffTemplate(finalToleranceBuffId), buff.Skill, DateTime.UtcNow));
    }

    public void RemoveEffect(Buff buff)
    {
        lock (_lock)
        {
            var own = GetOwner();
            if (own == null)
                return;

            if (buff == null || _effects?.Contains(buff) != true)
                return;

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

        lock (_lock)
        {
            if (_effects != null)
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
    }

    public void RemoveEffect(uint index)
    {
        var own = GetOwner();
        if (own == null)
            return;

        lock (_lock)
        {
            if (_effects != null)
            {
                foreach (var e in _effects.ToList())
                {
                    if (e != null && e.Index == index)
                    {
                        e.Template.Dispel(e.Caster, e.Owner, e);
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
    }

    public void RemoveBuff(uint buffId)
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
                    e.Template.Dispel(e.Caster, e.Owner, e);
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
        {
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
    }

    public void RemoveBuffs(uint buffTagId, int count)
    {
        var own = GetOwner();
        if (own == null)
            return;

        if (_effects == null)
            return;

        // Create a copy of the list of effects to avoid changing the list while iterating
        var buffIds = SkillManager.Instance.GetBuffsByTagId(buffTagId);
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var e in effects.ToList())
        {
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

    public void TriggerRemoveOn(BuffRemoveOn on, uint value = 0)
    {
        // Create a copy of the list of effects to avoid changing the list while iterating
        IEnumerable<Buff> effects;
        lock (_lock)
        {
            effects = _effects.ToArray();
        }

        foreach (var effect in effects)
        {
            if (effect == null) { continue; }

            var template = effect.Template;

            if (template.RemoveOnAttackBuffTrigger && on == BuffRemoveOn.AttackBuffTrigger)
                effect.Exit();
            else if (template.RemoveOnAttackedBuffTrigger && on == BuffRemoveOn.AttackedBuffTrigger)
                effect.Exit();
            else if (template.RemoveOnAttackedEtc && on == BuffRemoveOn.AttackedEtc)
                effect.Exit();
            else if (template.RemoveOnAttackedEtcDot && on == BuffRemoveOn.AttackedEtcDot)
                effect.Exit();
            else if (template.RemoveOnAttackedSpellDot && on == BuffRemoveOn.AttackedSpellDot)
                effect.Exit();
            else if (template.RemoveOnAttackEtc && on == BuffRemoveOn.AttackEtc)
                effect.Exit();
            else if (template.RemoveOnAttackEtcDot && on == BuffRemoveOn.AttackEtcDot)
                effect.Exit();
            else if (template.RemoveOnAttackSpellDot && on == BuffRemoveOn.AttackSpellDot)
                effect.Exit();
            else if (template.RemoveOnAutoAttack && on == BuffRemoveOn.AutoAttack)
                effect.Exit();
            else if (template.RemoveOnDamageBuffTrigger && on == BuffRemoveOn.DamageBuffTrigger)
                effect.Exit();
            else if (template.RemoveOnDamagedBuffTrigger && on == BuffRemoveOn.DamagedBuffTrigger)
                effect.Exit();
            else if (template.RemoveOnDamagedEtc && on == BuffRemoveOn.DamagedEtc)
                effect.Exit();
            else if (template.RemoveOnDamagedEtcDot && on == BuffRemoveOn.DamagedEtcDot)
                effect.Exit();
            else if (template.RemoveOnDamagedSpellDot && on == BuffRemoveOn.DamagedSpellDot)
                effect.Exit();
            else if (template.RemoveOnDamageEtc && on == BuffRemoveOn.DamageEtc)
                effect.Exit();
            else if (template.RemoveOnDamageEtcDot && on == BuffRemoveOn.DamageEtcDot)
                effect.Exit();
            else if (template.RemoveOnDamageSpellDot && on == BuffRemoveOn.DamageSpellDot)
                effect.Exit();
            else if (template.RemoveOnDeath && on == BuffRemoveOn.Death)
                effect.Exit();
            else if (template.RemoveOnExempt && on == BuffRemoveOn.Exempt)
                effect.Exit();
            else if (template.RemoveOnInteraction && on == BuffRemoveOn.Interaction)
                effect.Exit();
            else if (template.RemoveOnLand && on == BuffRemoveOn.Land)
                effect.Exit();
            else if (template.RemoveOnMount && on == BuffRemoveOn.Mount)
                effect.Exit();
            else if (template.RemoveOnMove && on == BuffRemoveOn.Move)
                effect.Exit();
            else if (template.RemoveOnSourceDead && on == BuffRemoveOn.SourceDead && value == effect.Caster.ObjId)
                effect.Exit();//Need to investigate this one
            else if (template.RemoveOnStartSkill && on == BuffRemoveOn.StartSkill)
            {
                if (value == 0)
                    effect.Exit();
                else
                {
                    var tags = SkillManager.Instance.GetBuffTags(effect.Template.BuffId);
                    if (!tags.Contains(value))
                        effect.Exit();
                }
            }
            else if (template.RemoveOnUnmount && on == BuffRemoveOn.Unmount)
                effect.Exit();
            else if (template.RemoveOnUseSkill && on == BuffRemoveOn.UseSkill)
                effect.Exit();
            else
                continue;
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
            if (e != null && e.Template.Stealth)
                e.Exit();
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
}
