 using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData
{
    [GameData]
    public class BuffGameData : Singleton<BuffGameData>, IGameDataLoader
    {
        private Dictionary<uint, List<BuffModifier>> _buffModifiers;
        
        public List<BuffModifier> GetModifiersForBuff(uint ownerId)
        {
            return _buffModifiers.ContainsKey(ownerId) ? _buffModifiers[ownerId] : new List<BuffModifier>();
        }
        
        public void Load(SqliteConnection connection)
        {
            _buffModifiers = new Dictionary<uint, List<BuffModifier>>();
            
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM buff_modifiers";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var template = new BuffModifier()
                        {
                            Id = reader.GetUInt32("id"),
                            OwnerId = reader.GetUInt32("owner_id"),
                            OwnerType = reader.GetString("owner_type"),
                            TagId = reader.GetUInt32("tag_id", 0),
                            BuffAttribute = (BuffAttribute)reader.GetUInt32("buff_attribute_id"),
                            UnitModifierType = (UnitModifierType)reader.GetUInt32("unit_modifier_type_id"),
                            Value = reader.GetInt32("value"),
                            BuffId = reader.GetUInt32("buff_id", 0),
                            Synergy = reader.GetBoolean("synergy"),
                        };

                        if (!_buffModifiers.ContainsKey(template.OwnerId))
                            _buffModifiers.Add(template.OwnerId, new List<BuffModifier>());
                        _buffModifiers[template.OwnerId].Add(template);
                    }
                }
            }
        }

        public void PostLoad()
        {
        }
    }
}
