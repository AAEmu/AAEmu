using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills
{
    public class BuffModifiers
    {
        private Dictionary<uint, List<BuffModifier>> _modifiersByBuffId;
        private Dictionary<uint, List<BuffModifier>> _modifiersByTagId;

        public BuffModifiers()
        {
            _modifiersByBuffId = new Dictionary<uint, List<BuffModifier>>();
            _modifiersByTagId = new Dictionary<uint, List<BuffModifier>>();
        }

        public double ApplyModifiers(BuffTemplate buff, BuffAttribute attribute, double baseValue)
        {
            double endValue = baseValue;

            var modifiers = GetModifiersForBuffIdWithAttribute(buff.Id, attribute).OrderBy(mod => mod.UnitModifierType).ToList();

            foreach (var tag in SkillManager.Instance.GetBuffTags(buff.Id))
            {
                modifiers.AddRange(GetModifiersForTagIdWithAttribute(tag, attribute));
            }

            foreach (var modifier in modifiers)
            {
                switch (modifier.UnitModifierType)
                {
                    case UnitModifierType.Percent:
                        endValue += (endValue * (modifier.Value / 100.0f));
                        break;
                    case UnitModifierType.Value:
                        endValue += modifier.Value;
                        break;
                }
            }

            return endValue;
        }

        public List<BuffModifier> GetModifiersForBuffIdWithAttribute(uint skillId, BuffAttribute attribute)
        {
            var modifiers = GetModifiersForBuffId(skillId);
            if (modifiers == null) return new List<BuffModifier>();
            return modifiers.Where(mod => mod.BuffAttribute == attribute).ToList();
        }

        public List<BuffModifier> GetModifiersForTagIdWithAttribute(uint tagId, BuffAttribute attribute)
        {
            var modifiers = GetModifiersForTagId(tagId);
            if (modifiers == null) return new List<BuffModifier>();
            return modifiers.Where(mod => mod.BuffAttribute == attribute).ToList();
        }

        public List<BuffModifier> GetModifiersForBuffId(uint skillId)
        {
            if (_modifiersByBuffId.ContainsKey(skillId))
                return _modifiersByBuffId[skillId];
            return null;
        }

        public List<BuffModifier> GetModifiersForTagId(uint tagId)
        {
            if (_modifiersByTagId.ContainsKey(tagId))
                return _modifiersByTagId[tagId];
            return null;
        }

        public void AddModifiers(uint ownerId)
        {
            var modifiers = BuffGameData.Instance.GetModifiersForBuff(ownerId);
            if (modifiers != null)
            {
                foreach (var modifier in modifiers)
                {
                    AddModifier(modifier);
                }
            }
        }

        public void RemoveModifiers(uint ownerId)
        {
            var modifiers = BuffGameData.Instance.GetModifiersForBuff(ownerId);
            foreach (var modifier in modifiers)
            {
                RemoveModifier(modifier);
            }
        }

        public void AddModifier(BuffModifier modifier)
        {
            if (modifier.BuffId > 0)
            {
                if (!_modifiersByBuffId.ContainsKey(modifier.BuffId))
                    _modifiersByBuffId.Add(modifier.BuffId, new List<BuffModifier>());
                _modifiersByBuffId[modifier.BuffId].Add(modifier);
            }

            if (modifier.TagId > 0)
            {
                if (!_modifiersByTagId.ContainsKey(modifier.TagId))
                    _modifiersByTagId.Add(modifier.TagId, new List<BuffModifier>());
                _modifiersByTagId[modifier.TagId].Add(modifier);
            }
        }

        public void RemoveModifier(BuffModifier modifier)
        {
            if (_modifiersByBuffId.ContainsKey(modifier.BuffId))
                _modifiersByBuffId[modifier.BuffId].Remove(modifier);

            if (_modifiersByTagId.ContainsKey(modifier.TagId))
                _modifiersByTagId[modifier.TagId].Remove(modifier);
        }
    }
}
