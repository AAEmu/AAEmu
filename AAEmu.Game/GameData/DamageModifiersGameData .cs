 using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData
{
    [GameData]
    public class DamageModifierGameData : Singleton<DamageModifierGameData>, IGameDataLoader
    {
        private Dictionary<uint, List<BonusTemplate>> __damageModifiers;
        
        public List<BonusTemplate> GetModifiersForBuff(uint ownerId)
        {
            return __damageModifiers.ContainsKey(ownerId) ? __damageModifiers[ownerId] : new List<BonusTemplate>();
        }
        
        public void Load(SqliteConnection connection)
        {
            __damageModifiers = new Dictionary<uint, List<BonusTemplate>>();
            
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM unit_modifiers WHERE owner_type = 'DamageEffect'";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var ownerId = reader.GetUInt32("owner_id");
                        var template = new BonusTemplate()
                        {
                            Attribute = (UnitAttribute)reader.GetUInt32("unit_attribute_id"),
                            ModifierType = (UnitModifierType)reader.GetUInt32("unit_modifier_type_id"),
                            Value = reader.GetInt32("value"),
                            LinearLevelBonus = reader.GetInt32("linear_level_bonus")
                        };

                        if (!__damageModifiers.ContainsKey(ownerId))
                            __damageModifiers.Add(ownerId, new List<BonusTemplate>());
                        __damageModifiers[ownerId].Add(template);
                    }
                }
            }
        }

        public void PostLoad()
        {
            foreach(var mod in __damageModifiers)
            {
                var de = SkillManager.Instance.GetEffectTemplate(mod.Key, "DamageEffect") as DamageEffect;
                if (de != null)
                {
                    de.Bonuses = mod.Value;
                }
            }
        }
    }
}
