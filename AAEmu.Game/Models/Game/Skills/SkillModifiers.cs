using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills
{
    public class SkillModifiers
    {
        private Dictionary<uint, List<SkillModifier>> _modifiersBySkillId;
        private Dictionary<uint, List<SkillModifier>> _modifiersByTagId;

        public SkillModifiers() {
            _modifiersBySkillId = new Dictionary<uint, List<SkillModifier>>();
            _modifiersByTagId = new Dictionary<uint, List<SkillModifier>>();
        }

        public double ApplyModifiers(Skill skill, SkillAttribute attribute, double baseValue) {
            double endValue = baseValue;

            List<SkillModifier> modifiers = GetModifiersForSkillIdWithAttribute(skill.Template.Id, attribute).OrderBy(mod => mod.UnitModifierType).ToList();

            foreach (var tag in SkillManager.Instance.GetSkillTags(skill.Template.Id)) {
                modifiers.AddRange(GetModifiersForTagIdWithAttribute(tag, attribute));
            }

            foreach (var modifier in modifiers) {
                switch (modifier.UnitModifierType) {
                    case UnitModifierType.Percent:
                        endValue += (endValue * (modifier.Value / 100));
                        break;
                    case UnitModifierType.Value:
                        endValue += modifier.Value;
                        break;
                }
            }

            return endValue;
        }

        public List<SkillModifier> GetModifiersForSkillIdWithAttribute(uint skillId, SkillAttribute attribute) {
            var modifiers = GetModifiersForSkillId(skillId);
            if (modifiers == null) return new List<SkillModifier>();
            return modifiers.Where(mod => mod.SkillAttribute == attribute).ToList();
        }

        public List<SkillModifier> GetModifiersForTagIdWithAttribute(uint tagId, SkillAttribute attribute) {
            var modifiers = GetModifiersForTagId(tagId);
            if (modifiers == null) return new List<SkillModifier>();
            return modifiers.Where(mod => mod.SkillAttribute == attribute).ToList();
        }

        public List<SkillModifier> GetModifiersForSkillId(uint skillId) {
            if (_modifiersBySkillId.ContainsKey(skillId))
                return _modifiersBySkillId[skillId];
            return null;
        }

        public List<SkillModifier> GetModifiersForTagId(uint tagId) {
            if (_modifiersByTagId.ContainsKey(tagId))
                return _modifiersByTagId[tagId];
            return null;
        }

        public void AddModifiers(uint ownerId) {
            var modifiers = SkillManager.Instance.GetModifiersByOwnerId(ownerId);
            if (modifiers != null) {
                foreach (var modifier in modifiers) {
                    AddModifier(modifier);
                }
            }
        }

        public void RemoveModifiers(uint ownerId) {
            var modifiers = SkillManager.Instance.GetModifiersByOwnerId(ownerId);
            foreach (var modifier in modifiers) {
                RemoveModifier(modifier);
            }
        }

        public void AddModifier(SkillModifier modifier) {
            if (modifier.SkillId > 0) {
                if (!_modifiersBySkillId.ContainsKey(modifier.SkillId))
                    _modifiersBySkillId.Add(modifier.SkillId, new List<SkillModifier>());
                _modifiersBySkillId[modifier.SkillId].Add(modifier);
            }

            if (modifier.TagId > 0) {
                if (!_modifiersByTagId.ContainsKey(modifier.TagId))
                    _modifiersByTagId.Add(modifier.TagId, new List<SkillModifier>());
                _modifiersByTagId[modifier.TagId].Add(modifier);
            } 
        } 

        public void RemoveModifier(SkillModifier modifier) {
            if (_modifiersBySkillId.ContainsKey(modifier.SkillId))
                _modifiersBySkillId[modifier.SkillId].Remove(modifier);

            if (_modifiersBySkillId.ContainsKey(modifier.TagId))
                _modifiersByTagId[modifier.TagId].Remove(modifier);
        }
    }
}