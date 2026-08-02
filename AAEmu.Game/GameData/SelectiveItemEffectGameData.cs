using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// Loads <c>selective_item_effects</c> / <c>selective_item_effect_elems</c> for item selection chests.
/// </summary>
[GameData]
public class SelectiveItemEffectGameData : Singleton<SelectiveItemEffectGameData>, IGameDataLoader
{
    private Dictionary<uint, SelectiveItemEffectTemplate> _bySkillId = new();
    private Dictionary<uint, SelectiveItemEffectTemplate> _byId = new();

    public SelectiveItemEffectTemplate GetBySkillId(uint skillId) =>
        _bySkillId.TryGetValue(skillId, out var t) ? t : null;

    public SelectiveItemEffectTemplate GetById(uint id) =>
        _byId.TryGetValue(id, out var t) ? t : null;

    public void Load(SqliteConnection connection)
    {
        _bySkillId = new Dictionary<uint, SelectiveItemEffectTemplate>();
        _byId = new Dictionary<uint, SelectiveItemEffectTemplate>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM selective_item_effects";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new SelectiveItemEffectTemplate
                {
                    Id = reader.GetUInt32("id"),
                    SkillId = reader.GetUInt32("skill_id"),
                    SelectCount = reader.GetInt32("select_count"),
                    ConsumeItemCount = reader.GetInt32("consume_item_count"),
                    IsMulti = reader.GetBoolean("is_multi", true)
                };
                _byId[template.Id] = template;
                if (template.SkillId != 0)
                    _bySkillId[template.SkillId] = template;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT * FROM selective_item_effect_elems ORDER BY selective_item_effect_id, id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var effectId = reader.GetUInt32("selective_item_effect_id");
                if (!_byId.TryGetValue(effectId, out var template))
                    continue;

                template.Elems.Add(new SelectiveItemEffectElem
                {
                    Id = reader.GetUInt32("id"),
                    SelectiveItemEffectId = effectId,
                    ItemId = reader.GetUInt32("item_id"),
                    GradeId = reader.GetByte("grade_id"),
                    Count = reader.GetInt32("count")
                });
            }
        }
    }

    public void PostLoad()
    {
    }
}
