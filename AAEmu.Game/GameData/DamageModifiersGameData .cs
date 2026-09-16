using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public class DamageModifierGameData : Singleton<DamageModifierGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, List<BonusTemplate>> __damageModifiers;
    private Dictionary<uint, List<BonusTemplate>> __healModifiers;

    public List<BonusTemplate> GetModifiersForBuff(uint ownerId)
    {
        return __damageModifiers.TryGetValue(ownerId, out var modifier) ? modifier : [];
    }

    public void Load(SqliteConnection connection)
    {
        __damageModifiers = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM unit_modifiers WHERE owner_type = 'DamageEffect'";
            command.Prepare();
            var attributeIds = new List<long>();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var ownerId = reader.GetUInt32("owner_id");
                    var attributeId = reader.GetUInt32("unit_attribute_id");
                    attributeIds.Add(attributeId);
                    var template = new BonusTemplate
                    {
                        Attribute = (UnitAttribute)attributeId,
                        ModifierType = (UnitModifierType)reader.GetUInt32("unit_modifier_type_id"),
                        Value = reader.GetInt64("value"),
                        LinearLevelBonus = reader.GetInt32("linear_level_bonus")
                    };

                    if (!__damageModifiers.ContainsKey(ownerId))
                        __damageModifiers.Add(ownerId, []);
                    __damageModifiers[ownerId].Add(template);
                }
            }

            var unknownIds = UnitAttributeLoadRules.UnknownIds(attributeIds);
            if (unknownIds.Count > 0)
                Logger.Warn(UnitAttributeLoadRules.Warning("unit_modifiers (owner_type='DamageEffect')", unknownIds));
        }

        // 160 rows, 158 of them attribute 185 heal_critical_mul = -2000: the heal effects authored never to
        // crit. Attached to the HealEffect templates in PostLoad, the same shape as the damage rows above.
        __healModifiers = [];
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM unit_modifiers WHERE owner_type = 'HealEffect'";
            command.Prepare();
            var attributeIds = new List<long>();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var ownerId = reader.GetUInt32("owner_id");
                    var attributeId = reader.GetUInt32("unit_attribute_id");
                    attributeIds.Add(attributeId);
                    var template = new BonusTemplate
                    {
                        Attribute = (UnitAttribute)attributeId,
                        ModifierType = (UnitModifierType)reader.GetUInt32("unit_modifier_type_id"),
                        Value = reader.GetInt64("value"),
                        LinearLevelBonus = reader.GetInt32("linear_level_bonus")
                    };

                    if (!__healModifiers.ContainsKey(ownerId))
                        __healModifiers.Add(ownerId, []);
                    __healModifiers[ownerId].Add(template);
                }
            }

            var unknownIds = UnitAttributeLoadRules.UnknownIds(attributeIds);
            if (unknownIds.Count > 0)
                Logger.Warn(UnitAttributeLoadRules.Warning("unit_modifiers (owner_type='HealEffect')", unknownIds));
        }
    }

    public void PostLoad()
    {
        foreach (var mod in __damageModifiers)
        {
            var de = SkillManager.Instance.GetEffectTemplate(mod.Key, "DamageEffect") as DamageEffect;
            if (de != null)
            {
                de.Bonuses = mod.Value;
            }
        }

        foreach (var mod in __healModifiers)
        {
            var he = SkillManager.Instance.GetEffectTemplate(mod.Key, "HealEffect") as HealEffect;
            if (he != null)
            {
                he.Bonuses = mod.Value;
            }
        }
    }
}
