using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

public class BuffModifiers
{
    private readonly Dictionary<uint, List<BuffModifier>> _modifiersByBuffId = [];
    private readonly Dictionary<uint, List<BuffModifier>> _modifiersByTagId = [];
    // The rows the unit's equipped items put here, kept so a gear change can take exactly them back out.
    private readonly List<BuffModifier> _itemModifiers = [];
    private readonly List<BuffModifier> _expeditionModifiers = [];

    public double ApplyModifiers(BuffTemplate buff, BuffAttribute attribute, double baseValue)
    {
        var endValue = baseValue;

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
                    endValue += endValue * (modifier.Value / 100.0f);
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
        if (modifiers == null) return [];
        return modifiers.Where(mod => mod.BuffAttribute == attribute).ToList();
    }

    public List<BuffModifier> GetModifiersForTagIdWithAttribute(uint tagId, BuffAttribute attribute)
    {
        var modifiers = GetModifiersForTagId(tagId);
        if (modifiers == null) return [];
        return modifiers.Where(mod => mod.BuffAttribute == attribute).ToList();
    }

    public List<BuffModifier> GetModifiersForBuffId(uint skillId)
    {
        if (_modifiersByBuffId.TryGetValue(skillId, out var id))
            return id;
        return null;
    }

    public List<BuffModifier> GetModifiersForTagId(uint tagId)
    {
        if (_modifiersByTagId.TryGetValue(tagId, out var id))
            return id;
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

    /// <summary>
    /// Registers the buff_modifiers rows the item whose template id this is grants (owner_type='Item').
    /// Called from the gear walk for every equipped item and gem, mirroring the unit_modifiers walk.
    /// </summary>
    public void AddItemModifiers(uint itemTemplateId)
    {
        foreach (var modifier in BuffGameData.Instance.GetItemModifiers(itemTemplateId))
        {
            _itemModifiers.Add(modifier);
            AddModifier(modifier);
        }
    }

    /// <summary>Takes back every item-owned modifier. The gear walk calls this before re-adding.</summary>
    public void RemoveItemModifiers()
    {
        foreach (var modifier in _itemModifiers)
            RemoveModifier(modifier);
        _itemModifiers.Clear();
    }

    public void ReplaceExpeditionModifiers(IEnumerable<uint> gradeIds)
    {
        foreach (var modifier in _expeditionModifiers)
            RemoveModifier(modifier);
        _expeditionModifiers.Clear();

        foreach (var gradeId in gradeIds)
        foreach (var modifier in BuffGameData.Instance.GetGradeModifiers(gradeId))
        {
            _expeditionModifiers.Add(modifier);
            AddModifier(modifier);
        }
    }

    public void AddModifier(BuffModifier modifier)
    {
        if (modifier.BuffId > 0)
        {
            if (!_modifiersByBuffId.ContainsKey(modifier.BuffId))
                _modifiersByBuffId.Add(modifier.BuffId, []);
            _modifiersByBuffId[modifier.BuffId].Add(modifier);
        }

        if (modifier.TagId > 0)
        {
            if (!_modifiersByTagId.ContainsKey(modifier.TagId))
                _modifiersByTagId.Add(modifier.TagId, []);
            _modifiersByTagId[modifier.TagId].Add(modifier);
        }
    }

    public void RemoveModifier(BuffModifier modifier)
    {
        if (_modifiersByBuffId.TryGetValue(modifier.BuffId, out var buffsById))
            buffsById.Remove(modifier);

        if (_modifiersByTagId.TryGetValue(modifier.TagId, out var buffsByTag))
            buffsByTag.Remove(modifier);
    }
}
