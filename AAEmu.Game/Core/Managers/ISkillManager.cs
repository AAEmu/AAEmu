using System;
using System.Collections.Generic;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Core.Managers
{
    public interface ISkillManager
    {
        event EventHandler OnSkillsLoaded;

        List<uint> GetBuffsByTagId(uint tagId);
        List<uint> GetBuffTags(uint buffId);
        BuffTemplate GetBuffTemplate(uint id);
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
        void Load();
        ushort NextId();
        void ReleaseId(ushort id);
    }
}