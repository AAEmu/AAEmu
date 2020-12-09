using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData
{
    [GameData]
    public class ItemGameData : Singleton<ItemGameData>, IGameDataLoader
    {
        private Dictionary<uint, Dictionary<byte, uint>> _itemGradeBuffs;
        
        public BuffTemplate GetItemBuff(uint itemId, byte gradeId)
        {
            if (_itemGradeBuffs.TryGetValue(itemId, out var itemGradeBuffs))
                if (itemGradeBuffs.TryGetValue(gradeId, out var buffId))
                    return SkillManager.Instance.GetBuffTemplate(buffId);
            return null;
        }
        
        public void Load(SqliteConnection connection)
        {
            _itemGradeBuffs = new Dictionary<uint, Dictionary<byte, uint>>();
            
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_grade_buffs";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var itemId = reader.GetUInt32("item_id");
                        var itemGrade = reader.GetByte("item_grade_id");
                        var buffId = reader.GetUInt32("buff_id");

                        if (!_itemGradeBuffs.ContainsKey(itemId))
                            _itemGradeBuffs.Add(itemId, new Dictionary<byte, uint>());

                        _itemGradeBuffs[itemId].Add(itemGrade, buffId);
                    }
                }
            }
        }

        public void PostLoad()
        {

        }
    }
}
