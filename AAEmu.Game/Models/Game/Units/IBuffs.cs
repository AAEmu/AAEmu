using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Char;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Units;

public interface IBuffs
{
    void AddBuff(Buff buff, uint index = 0, int forcedDuration = 0);
    void AddBuff(uint buffId, BaseUnit caster);
    bool CheckBuff(uint id);
    bool CheckBuffImmune(BuffTemplate candidate, BaseUnit caster, Skill castingSkill = null);
    bool CheckBuffs(List<uint> ids);
    bool CheckBuffTag(uint tagId);
    bool CheckDamageImmune(DamageType damageType);
    bool CheckKnockbackImmune();
    bool CheckManaBurnImmune();
    uint GetMissingRequiredBuffTag(BuffTemplate candidate);
    void BroadcastBuffImmune(BaseUnit caster, CastAction castObj, SkillCaster casterObj);
    IEnumerable<Buff> GetAbsorptionEffects();
    IEnumerable<Buff> GetDamageReflectionEffects();
    IEnumerable<Buff> GetManaShieldEffects();
    void GetAllBuffs(List<Buff> goodBuffs, List<Buff> badBuffs, List<Buff> hiddenBuffs, bool includeAllPassives);
    int GetBuffCountById(uint buffId);
    IEnumerable<Buff> GetBuffsRequiring(uint buffId);
    Buff GetEffectByIndex(uint index);
    Buff GetEffectByTemplate(BuffTemplate template);
    Buff GetEffectFromBuffId(uint id);
    List<Buff> GetEffectsByType(Type effectType);
    bool HasEffectsMatchingCondition(Func<Buff, bool> predicate);
    void RemoveAllEffects();
    void RemoveBuff(uint buffId, bool notifyZone = true);
    void RemoveBuffs(BuffKind kind, int count, uint buffTagId = 0);
    void RemoveBuffs(uint buffTagId, int count);
    void RemoveEffect(Buff buff);
    void RemoveEffect(uint index, bool notifyZone = true);
    void RemoveEffect(uint templateId, uint skillId);
    void RemoveEffectsOnDeath();
    void RemoveStealth();
    void SetOwner(BaseUnit owner);
    void TriggerRemoveOn(BuffRemoveOn on, uint value = 0);
    void TimeoutBuffsFromSkill(uint skillId);
    // Buff Persistence
    void SaveActiveBuffs(MySqlConnection connection, MySqlTransaction transaction, uint characterId);
    void LoadActiveBuffs(Character character);
    void CancelAllEffectTasks();
}
