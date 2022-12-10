using System;
using System.Collections.Generic;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Units
{
    public interface IBuffs
    {
        void AddBuff(Buff buff, uint index = 0, int forcedDuration = 0);
        void AddBuff(uint buffId, Unit caster);
        bool CheckBuff(uint id);
        bool CheckBuffImmune(uint buffId);
        bool CheckBuffs(List<uint> ids);
        bool CheckBuffTag(uint tagId);
        bool CheckDamageImmune(DamageType damageType);
        IEnumerable<Buff> GetAbsorptionEffects();
        void GetAllBuffs(List<Buff> goodBuffs, List<Buff> badBuffs, List<Buff> hiddenBuffs);
        int GetBuffCountById(uint buffId);
        IEnumerable<Buff> GetBuffsRequiring(uint buffId);
        Buff GetEffectByIndex(uint index);
        Buff GetEffectByTemplate(BuffTemplate template);
        Buff GetEffectFromBuffId(uint id);
        List<Buff> GetEffectsByType(Type effectType);
        bool HasEffectsMatchingCondition(Func<Buff, bool> predicate);
        void RemoveAllEffects();
        void RemoveBuff(uint buffId);
        void RemoveBuffs(BuffKind kind, int count, uint buffTagId = 0);
        void RemoveBuffs(uint buffTagId, int count);
        void RemoveEffect(Buff buff);
        void RemoveEffect(uint index);
        void RemoveEffect(uint templateId, uint skillId);
        void RemoveEffectsOnDeath();
        void RemoveStealth();
        void SetOwner(BaseUnit owner);
        void TriggerRemoveOn(BuffRemoveOn on, uint value = 0);
    }
}
