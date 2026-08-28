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
    /// <summary>
    /// item_grade_buffs keyed by item, then grade, then the number of equipped pieces the row requires.
    /// </summary>
    /// <remarks>
    /// A single (item, grade) pair legitimately carries several rows, one per <c>num_pieces</c> tier, and
    /// they are not interchangeable: a mythic sail's 1-piece row grants the buff holding its
    /// <c>move_speed_mul</c>, while its 2-piece row grants a same-named buff with no modifiers at all.
    /// Collapsing them to one entry per grade therefore silently picked whichever row the reader saw last
    /// — for sails, the empty one, so a rigged hull sailed with none of its sail bonuses while the
    /// matching furl penalty still applied. That inverted the whole thing: furling a sail made the ship
    /// faster, because the penalty drove <c>move_speed_mul</c> negative and the simulation scales thrust
    /// headroom by the magnitude of the resulting top speed.
    /// </remarks>
    private Dictionary<uint, Dictionary<byte, List<GradeBuff>>> _itemGradeBuffs;

    private readonly record struct GradeBuff(int NumPieces, uint BuffId);

    /// <summary>
    /// The buff an equipped item grants at this grade, for a unit carrying
    /// <paramref name="equippedPieces"/> copies of it.
    /// </summary>
    /// <param name="equippedPieces">
    /// How many of this item the unit has equipped. Rows requiring more pieces than this do not apply;
    /// of those that do, the highest tier wins. Defaults to one, which is every slot-unique item.
    /// </param>
    public BuffTemplate GetItemBuff(uint itemId, byte gradeId, int equippedPieces = 1)
    {
        var buffId = GetItemBuffId(itemId, gradeId, equippedPieces);
        return buffId == 0 ? null : SkillManager.Instance.GetBuffTemplate(buffId);
    }

    /// <summary>Buff id for <see cref="GetItemBuff"/>, or zero when no row applies.</summary>
    public uint GetItemBuffId(uint itemId, byte gradeId, int equippedPieces = 1)
    {
        if (!_itemGradeBuffs.TryGetValue(itemId, out var grades) ||
            !grades.TryGetValue(gradeId, out var rows))
        {
            return 0;
        }

        var bestPieces = 0;
        var bestBuffId = 0u;
        foreach (var row in rows)
        {
            if (row.NumPieces > equippedPieces || row.NumPieces < bestPieces)
                continue;
            bestPieces = row.NumPieces;
            bestBuffId = row.BuffId;
        }

        return bestBuffId;
    }

    /// <summary>
    /// Every buff any tier of this (item, grade) can grant. Used when withdrawing an item's buff, where
    /// the tier that was applied may no longer be the one the current piece count would choose.
    /// </summary>
    public IEnumerable<uint> GetItemBuffIds(uint itemId, byte gradeId)
    {
        if (!_itemGradeBuffs.TryGetValue(itemId, out var grades) ||
            !grades.TryGetValue(gradeId, out var rows))
        {
            return [];
        }

        return rows.Select(row => row.BuffId).Where(id => id != 0).Distinct().ToList();
    }

    /// <summary>
    /// Every buff any grade of this item can grant. Used when a new grade is fitted so an older
    /// grade's independent buff can be taken off — those families do not replace each other.
    /// </summary>
    public IEnumerable<uint> GetItemBuffIds(uint itemId)
    {
        if (!_itemGradeBuffs.TryGetValue(itemId, out var grades))
            return [];

        return grades.Values
            .SelectMany(rows => rows.Select(row => row.BuffId))
            .Where(id => id != 0)
            .Distinct()
            .ToList();
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

                    // A missing or zero piece requirement means the row applies to a single equipped copy.
                    var numPieces = reader.IsDBNull("num_pieces") ? 1 : reader.GetInt32("num_pieces");
                    if (numPieces < 1)
                        numPieces = 1;

                    if (!_itemGradeBuffs.TryGetValue(itemId, out var grades))
                    {
                        grades = [];
                        _itemGradeBuffs.Add(itemId, grades);
                    }

                    if (!grades.TryGetValue(itemGrade, out var rows))
                    {
                        rows = [];
                        grades.Add(itemGrade, rows);
                    }

                    rows.Add(new GradeBuff(numPieces, buffId));
                }
            }
        }
    }

    public void PostLoad()
    {

    }
}
