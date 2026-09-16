using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// The equip slot reinforcement ladders and everything hanging off them: what each level costs, which
/// materials feed it, the effects a level unlocks and the set and bundle bonuses the attribute totals
/// pay out. Progress per character lives on the character; this is the content it climbs.
/// </summary>
[GameData]
public class EquipSlotReinforceGameData : Singleton<EquipSlotReinforceGameData>, IGameDataLoader
{
    private readonly Dictionary<byte, List<EquipSlotReinforceStep>> _ladders = [];
    private readonly Dictionary<byte, EquipSlotReinforceAttribute> _attributeBySlot = [];
    private readonly Dictionary<(byte SlotTypeId, byte Level), List<EquipSlotReinforceMaterial>> _materials = [];
    private readonly Dictionary<uint, EquipSlotReinforceLevelEffect> _levelEffectsById = [];
    private readonly List<EquipSlotReinforceLevelEffect> _levelEffects = [];
    private readonly List<EquipSlotReinforceSetEffect> _setEffects = [];
    private readonly List<EquipSlotReinforceBundleEffect> _bundleEffects = [];

    public void Load(SqliteConnection connection)
    {
        _ladders.Clear();
        _attributeBySlot.Clear();
        _materials.Clear();
        _levelEffectsById.Clear();
        _levelEffects.Clear();
        _setEffects.Clear();
        _bundleEffects.Clear();

        LoadSteps(connection);
        LoadMaterials(connection);
        LoadLevelEffects(connection);
        LoadUnitModifiers(connection);
        LoadSetEffects(connection);
        LoadBundleEffects(connection);
    }

    public void PostLoad()
    {
    }

    private void LoadSteps(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, slot_type_id, level, need_exp, reinforce_attribute_id, gain_item_level, " +
            "level_up_item_id, level_up_item_count FROM equip_slot_reinforces";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);

        while (reader.Read())
        {
            var step = new EquipSlotReinforceStep
            {
                Id = reader.GetUInt32("id"),
                SlotTypeId = reader.GetByte("slot_type_id"),
                Level = reader.GetByte("level"),
                NeedExp = reader.GetInt32("need_exp"),
                Attribute = (EquipSlotReinforceAttribute)reader.GetByte("reinforce_attribute_id"),
                GainItemLevel = reader.GetFloat("gain_item_level"),
                LevelUpItemId = reader.GetUInt32("level_up_item_id"),
                LevelUpItemCount = reader.GetInt32("level_up_item_count")
            };

            if (!_ladders.TryGetValue(step.SlotTypeId, out var ladder))
            {
                ladder = [];
                _ladders[step.SlotTypeId] = ladder;
            }

            ladder.Add(step);

            // A slot's ladder belongs to one attribute; the totals that gate the set and bundle
            // effects are sums over the slots of an attribute.
            if (!_attributeBySlot.ContainsKey(step.SlotTypeId))
                _attributeBySlot[step.SlotTypeId] = step.Attribute;
        }

        foreach (var ladder in _ladders.Values)
            ladder.Sort((left, right) => left.Level.CompareTo(right.Level));
    }

    private void LoadMaterials(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name, require_level, gain_exp, currency_id, currency_value, " +
            "need_material_item_set_id, slot_type_id FROM equip_slot_reinforce_materials";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);

        while (reader.Read())
        {
            var material = new EquipSlotReinforceMaterial
            {
                Id = reader.GetUInt32("id"),
                Name = reader.GetString("name", string.Empty),
                RequireLevel = reader.GetByte("require_level"),
                GainExp = reader.GetInt32("gain_exp"),
                CurrencyId = reader.GetUInt32("currency_id"),
                CurrencyValue = reader.GetInt32("currency_value"),
                NeedMaterialItemSetId = reader.GetUInt32("need_material_item_set_id"),
                SlotTypeId = reader.GetByte("slot_type_id")
            };

            var key = (material.SlotTypeId, material.RequireLevel);
            if (!_materials.TryGetValue(key, out var list))
            {
                list = [];
                _materials[key] = list;
            }

            list.Add(material);
        }
    }

    private void LoadLevelEffects(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, slot_type_id, trigger_level FROM equip_slot_reinforce_level_effects";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);

        while (reader.Read())
        {
            var effect = new EquipSlotReinforceLevelEffect
            {
                Id = reader.GetUInt32("id"),
                SlotTypeId = reader.GetByte("slot_type_id"),
                TriggerLevel = reader.GetByte("trigger_level")
            };

            _levelEffectsById[effect.Id] = effect;
            _levelEffects.Add(effect);
        }
    }

    private void LoadUnitModifiers(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, equip_slot_reinforce_level_effect_id, unit_attribute_id, " +
            "unit_modifier_type_id, value, weight FROM equip_slot_reinforce_unit_modifiers";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);

        while (reader.Read())
        {
            var modifier = new EquipSlotReinforceUnitModifier
            {
                Id = reader.GetUInt32("id"),
                LevelEffectId = reader.GetUInt32("equip_slot_reinforce_level_effect_id"),
                UnitAttributeId = reader.GetUInt32("unit_attribute_id"),
                UnitModifierTypeId = reader.GetUInt32("unit_modifier_type_id"),
                Value = reader.GetInt32("value"),
                Weight = reader.GetInt32("weight")
            };

            if (_levelEffectsById.TryGetValue(modifier.LevelEffectId, out var effect))
                effect.Modifiers.Add(modifier);
        }
    }

    private void LoadSetEffects(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, reinforce_attribute_id, require_reinforce_level, level, desc " +
            "FROM equip_slot_reinforce_set_effects";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);

        while (reader.Read())
        {
            _setEffects.Add(new EquipSlotReinforceSetEffect
            {
                Id = reader.GetUInt32("id"),
                Attribute = (EquipSlotReinforceAttribute)reader.GetByte("reinforce_attribute_id"),
                RequireReinforceLevel = reader.GetByte("require_reinforce_level"),
                Level = reader.GetByte("level"),
                Desc = reader.GetString("desc", string.Empty)
            });
        }
    }

    private void LoadBundleEffects(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, bundle_effect_level, require_offense_level, require_defense_level, " +
            "require_support_level, desc FROM equip_slot_reinforce_bundle_effects";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);

        while (reader.Read())
        {
            _bundleEffects.Add(new EquipSlotReinforceBundleEffect
            {
                Id = reader.GetUInt32("id"),
                BundleEffectLevel = reader.GetByte("bundle_effect_level"),
                RequireOffenseLevel = reader.GetByte("require_offense_level"),
                RequireDefenseLevel = reader.GetByte("require_defense_level"),
                RequireSupportLevel = reader.GetByte("require_support_level"),
                Desc = reader.GetString("desc", string.Empty)
            });
        }
    }

    /// <summary>Every slot type that has a ladder, which is the set the client is told about.</summary>
    public IReadOnlyCollection<byte> SlotTypeIds => _ladders.Keys;

    public IReadOnlyList<EquipSlotReinforceStep> Ladder(byte slotTypeId)
    {
        return _ladders.TryGetValue(slotTypeId, out var ladder) ? ladder : [];
    }

    public EquipSlotReinforceStep Step(byte slotTypeId, byte level)
    {
        if (!_ladders.TryGetValue(slotTypeId, out var ladder))
            return null;

        foreach (var step in ladder)
        {
            if (step.Level == level)
                return step;
        }

        return null;
    }

    public EquipSlotReinforceAttribute? AttributeOf(byte slotTypeId)
    {
        return _attributeBySlot.TryGetValue(slotTypeId, out var attribute) ? attribute : null;
    }

    public IReadOnlyList<EquipSlotReinforceMaterial> Materials(byte slotTypeId, byte level)
    {
        return _materials.TryGetValue((slotTypeId, level), out var list) ? list : [];
    }

    public IReadOnlyList<EquipSlotReinforceLevelEffect> LevelEffects => _levelEffects;

    public IReadOnlyList<EquipSlotReinforceSetEffect> SetEffects => _setEffects;

    public IReadOnlyList<EquipSlotReinforceBundleEffect> BundleEffects => _bundleEffects;
}
