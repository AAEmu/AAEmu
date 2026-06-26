using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

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
        _itemGradeBuffs = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_grade_buffs";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    // 10.0.2.13: item_id, item_grade_id and buff_id are nullable; skip incomplete rows
                    if (reader.IsDBNull("item_id") || reader.IsDBNull("item_grade_id") || reader.IsDBNull("buff_id"))
                        continue;

                    var itemId = reader.GetUInt32("item_id");
                    var itemGrade = reader.GetByte("item_grade_id");
                    var buffId = reader.GetUInt32("buff_id");

                    if (!_itemGradeBuffs.ContainsKey(itemId))
                        _itemGradeBuffs.Add(itemId, []);

                    // 10.0.2.13: duplicate (item_id, item_grade_id) pairs exist; overwrite instead of Add to avoid duplicate-key exception
                    _itemGradeBuffs[itemId][itemGrade] = buffId;
                }
            }
        }
    }

    public void PostLoad()
    {

    }
}
