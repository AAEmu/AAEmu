using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Core.Managers;

public interface ISkillManager : ILoadable
{
    event EventHandler OnSkillsLoaded;

    List<uint> GetBuffsByTagId(uint tagId);
    List<uint> GetBuffTags(uint buffId);
    BuffTemplate GetBuffTemplate(uint id);
    int ResolveDynamicBonusValue(DynamicBonusTemplate template, uint abLevel);
    // Time-based variant for LinearFunc dynamic_unit_modifiers that evolve over the buff duration
    // (e.g. buffs 2504 and 114). elapsedMs and durationMs are in milliseconds.
    int ResolveDynamicBonusValueTime(DynamicBonusTemplate template, long elapsedMs, long durationMs);
    List<BuffTriggerTemplate> GetBuffTriggerTemplates(uint buffId);
    List<CombatBuffTemplate> GetCombatBuffs(uint reqBuffId);
    List<DefaultSkill> GetDefaultSkills();
    EffectTemplate GetEffectTemplate(uint id);
    EffectTemplate GetEffectTemplate(uint id, string type);
    List<SkillModifier> GetModifiersByOwnerId(uint id);
    PassiveBuffTemplate GetPassiveBuffTemplate(uint id);
    List<SkillProduct> GetSkillProductsBySkillId(uint id);
    List<SkillReagent> GetSkillReagentsBySkillId(uint id);
    List<uint> GetSkillsByTag(uint tagId);
    List<uint> GetSkillTags(uint skillId);
    SkillTemplate GetSkillTemplate(uint id);
    List<SkillTemplate> GetStartAbilitySkills(AbilityType ability);
    bool IsCommonSkill(uint id);
    bool IsDefaultSkill(uint id);
    // ushort NextId();
    // void ReleaseId(ushort id);
}
