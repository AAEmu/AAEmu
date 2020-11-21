using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Units
{
    public class Effects
    {
        private readonly object _lock = new object();
        private uint _nextIndex;

        private WeakReference _owner;
        private readonly List<Effect> _effects;

        public Effects()
        {
            _nextIndex = 1;
            _effects = new List<Effect>();
        }

        public Effects(BaseUnit owner)
        {
            SetOwner(owner);
            _nextIndex = 1;
            _effects = new List<Effect>();
        }

        public bool CheckBuffImmune(uint buffId)
        {
            foreach (var effect in new List<Effect>(_effects))
            {
                if (effect == null)
                    continue;
                switch (effect.Template)
                {
                    case BuffTemplate template when template.ImmuneBuffTagId == 0:
                        return false;
                    case BuffTemplate template:
                    {
                        var buffs = SkillManager.Instance.GetBuffsByTagId(template.ImmuneBuffTagId);
                        return buffs != null && buffs.Contains(buffId);
                    }
                    case BuffEffect buffEffect when buffEffect.Buff.ImmuneBuffTagId == 0:
                        return false;
                    case BuffEffect buffEffect:
                    {
                        var buffs = SkillManager.Instance.GetBuffsByTagId(buffEffect.Buff.ImmuneBuffTagId);
                        return buffs != null && buffs.Contains(buffId);
                    }
                }
            }

            return false;
        }

        public List<Effect> GetEffectsByType(Type effectType)
        {
            var temp = new List<Effect>();
            foreach (var effect in new List<Effect>(_effects))
                if (effect.Template.GetType() == effectType)
                    temp.Add(effect);
            return temp;
        }

        public Effect GetEffectByIndex(uint index)
        {
            foreach (var effect in new List<Effect>(_effects))
                if (effect.Index == index)
                    return effect;
            return null;
        }

        public Effect GetEffectByTemplate(EffectTemplate template)
        {
            foreach (var effect in new List<Effect>(_effects))
                if (effect.Template == template)
                    return effect;
            return null;
        }

        public bool CheckBuff(uint id)
        {
            foreach (var effect in new List<Effect>(_effects))
                if (effect != null && effect.Template.BuffId > 0 && effect.Template.BuffId == id)
                    return true;
            return false;
        }
        
        public Effect GetEffectFromBuffId(uint id)
        {
            foreach (var effect in new List<Effect>(_effects))
                if (effect != null && effect.Template.BuffId > 0 && effect.Template.BuffId == id)
                    return effect;
            return null;
        }

        public bool CheckBuffs(List<uint> ids)
        {
            if (ids == null)
                return false;
            foreach (var effect in new List<Effect>(_effects))
                if (effect != null && effect.Template.BuffId > 0 && ids.Contains(effect.Template.BuffId))
                    return true;
            return false;
        }

        public int GetBuffCountById(uint buffId)
        {
            var count = 0;
            foreach (var effect in new List<Effect>(_effects))
                if (effect.Template.BuffId == buffId)
                    count++;
            return count;
        }

        public void GetAllBuffs(List<Effect> goodBuffs, List<Effect> badBuffs, List<Effect> hiddenBuffs)
        {
            foreach (var effect in new List<Effect>(_effects))
            {
                if (effect.Passive) continue;
                switch (effect.Template)
                {
                    case BuffTemplate template:
                        switch (template.Kind)
                        {
                            case BuffKind.Good:
                                goodBuffs.Add(effect);
                                break;
                            case BuffKind.Bad:
                                badBuffs.Add(effect);
                                break;
                            case BuffKind.Hidden:
                                hiddenBuffs.Add(effect);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        break;
                    case BuffEffect buffEffect:
                        switch (buffEffect.Buff.Kind)
                        {
                            case BuffKind.Good:
                                goodBuffs.Add(effect);
                                break;
                            case BuffKind.Bad:
                                badBuffs.Add(effect);
                                break;
                            case BuffKind.Hidden:
                                hiddenBuffs.Add(effect);
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        break;
                }
            }
        }

        public void AddEffect(Effect effect, uint index = 0)
        {
            lock (_lock)
            {
                var owner = GetOwner();
                if (owner == null)
                    return;

                effect.State = EffectState.Created;
                if (index == 0)
                {
                    effect.Index = _nextIndex; // TODO need safe increment...

                    if (++_nextIndex == uint.MaxValue)
                        _nextIndex = 1;
                }
                else
                {
                    effect.Index = index;
                }

                effect.Duration = effect.Template.GetDuration();
                if (effect.Duration > 0 && effect.StartTime == DateTime.MinValue)
                {
                    effect.StartTime = DateTime.Now;
                    effect.EndTime = effect.StartTime.AddMilliseconds(effect.Duration);
                }

                switch (effect.Template)
                {
                    case BuffTemplate buffTemplate:
                    {
                        Effect last = null;
                        if (buffTemplate.MaxStack > 0 && GetBuffCountById(effect.Template.BuffId) >= buffTemplate.MaxStack)
                            foreach (var e in new List<Effect>(_effects))
                                if (e != null && e.InUse && e.Template.BuffId == effect.Template.BuffId)
                                    if (e.GetTimeLeft() < effect.GetTimeLeft())
                                        last = e;
                        last?.Exit(index > 0 && last.Template.Id == effect.Template.Id);
                        break;
                    }
                    case BuffEffect buffEffect:
                    {
                        Effect last = null;
                        if (buffEffect.Buff.MaxStack > 0 && GetBuffCountById(effect.Template.BuffId) >= buffEffect.Buff.MaxStack)
                            foreach (var e in new List<Effect>(_effects))
                                if (e != null && e.InUse && e.Template.BuffId == effect.Template.BuffId)
                                    if (last == null || e.GetTimeLeft() < last.GetTimeLeft())
                                        last = e;
                        last?.Exit(index > 0 && last.Template.Id == effect.Template.Id);
                        break;
                    }
                }

                _effects.Add(effect);
                effect.Triggers.SubscribeEvents();
                effect.Events.OnBuffStarted(effect, new OnBuffStartedArgs());

                if (effect.Template.BuffId > 0)
                {
                    owner.Modifiers.AddModifiers(effect.Template.BuffId);
                    owner.CombatBuffs.AddCombatBuffs(effect.Template.BuffId);
                }
                
                if (effect.Duration > 0)
                    effect.SetInUse(true, false);
                else
                {
                    effect.InUse = true;
                    effect.State = EffectState.Acting;
                    effect.Template.Start(effect.Caster, owner, effect); // TODO поменять на target
                }
            }
        }

        public void RemoveEffect(Effect effect)
        {
            lock (_lock)
            {
                var own = GetOwner();
                if (own == null)
                    return;

                if (effect == null || _effects == null || !_effects.Contains(effect))
                    return;

                effect.SetInUse(false, false);
                _effects.Remove(effect);
                own.Modifiers.RemoveModifiers(effect.Template.BuffId);
                own.CombatBuffs.RemoveCombatBuff(effect.Template.BuffId);
                effect.Triggers.UnsubscribeEvents();
            }
        }

        public void RemoveEffect(uint templateId, uint skillId)
        {
            var own = GetOwner();
            if (own == null)
                return;

            if (_effects != null)
            {
                foreach (var e in new List<Effect>(_effects))
                {
                    if (e != null && e.Template.Id == templateId && e.Skill.Template.Id == skillId)
                    {
                        e.Template.Dispel(e.Caster, e.Owner, e);
                        _effects.Remove(e);
                        e.SetInUse(false, false);
                        own.Modifiers.RemoveModifiers(e.Template.BuffId);
                        own.CombatBuffs.RemoveCombatBuff(e.Template.BuffId);
                        e.Triggers.UnsubscribeEvents();
                    }
                }
            }
        }

        public void RemoveEffect(uint index)
        {
            var own = GetOwner();
            if (own == null)
                return;

            if (_effects != null)
            {
                foreach (var e in new List<Effect>(_effects))
                {
                    if (e != null && e.Index == index)
                    {
                        e.Template.Dispel(e.Caster, e.Owner, e);
                        _effects.Remove(e);
                        e.SetInUse(false, false);
                        own.Modifiers.RemoveModifiers(e.Template.BuffId);
                        own.CombatBuffs.RemoveCombatBuff(e.Template.BuffId);
                        e.Triggers.UnsubscribeEvents();
                        break;
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
            foreach (var e in new List<Effect>(_effects))
            {
                if (e != null && e.Template.BuffId == buffId)
                {
                    e.Template.Dispel(e.Caster, e.Owner, e);
                    _effects.Remove(e);
                    e.SetInUse(false, false);
                    own.Modifiers.RemoveModifiers(e.Template.BuffId);
                    own.CombatBuffs.RemoveCombatBuff(e.Template.BuffId);
                    e.Triggers.UnsubscribeEvents();
                    break;
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
            foreach (var e in new List<Effect>(_effects))
                if (e != null)
                {
                    BuffTemplate buffTemplate = null;
                    switch (e.Template)
                    {
                        case BuffEffect buffEffect:
                            buffTemplate = buffEffect.Buff;
                            break;
                        case BuffTemplate buffTempl:
                            buffTemplate = buffTempl;
                            break;
                    }

                    if (buffTemplate == null)
                        continue;

                    if (buffTagId == 0 && buffTemplate.System)
                        continue;
                    if (buffTagId == 0 && buffTemplate.Kind != kind)
                        continue;
                    if (buffTagId > 0 && !taggedBuffs.Contains(buffTemplate.Id))
                        continue;
                    
                    e.Exit();
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

            var buffIds = SkillManager.Instance.GetBuffsByTagId(buffTagId);
            foreach (var e in new List<Effect>(_effects))
                if (e != null)
                {
                    switch (e.Template)
                    {
                        case BuffTemplate template when !buffIds.Contains(template.Id):
                        case BuffEffect effect when !buffIds.Contains(effect.Buff.Id):
                            continue;
                    }

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

            foreach (var e in new List<Effect>(_effects))
                if (e != null /* && (e.Template.Skill == null || e.Template.Skill.Type != SkillTypes.Passive)*/)
                    e.Exit();
        }

        public void RemoveEffectsOnDeath()
        {
            var own = GetOwner();
            if (own == null)
                return;

            foreach (var e in new List<Effect>(_effects))
                if (e != null && (e.Template is BuffTemplate template && template.RemoveOnDeath ||
                                  e.Template is BuffEffect effect && effect.Buff.RemoveOnDeath))
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

            foreach (var e in new List<Effect>(_effects))
                if (e != null && (e.Template is BuffTemplate template && template.Stealth ||
                                  e.Template is BuffEffect effect && effect.Buff.Stealth))
                    e.Exit();
        }

        private BaseUnit GetOwner()
        {
            return _owner?.Target as BaseUnit;
        }

        public IEnumerable<Effect> GetAbsorptionEffects()
        {
            return _effects.Where(e =>
            {
                switch (e.Template)
                {
                    case BuffTemplate bt:
                        return bt.DamageAbsorptionTypeId > 0;
                    case BuffEffect be:
                        return be.Buff.DamageAbsorptionTypeId > 0;
                    default:
                        return false;
                }
            });
        }
    }
}
