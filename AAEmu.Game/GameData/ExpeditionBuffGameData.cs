using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// Guild prestige-shop buffs, loaded from <c>expedition_buffs</c> (one row per perk category) and
/// <c>expedition_buff_grades</c> (purchasable tiers within a category - cost in Contribution Points,
/// optional item cost, minimum guild level).
/// </summary>
[GameData]
public class ExpeditionBuffGameData : Singleton<ExpeditionBuffGameData>, IGameDataLoader
{
    private Dictionary<uint, ExpeditionBuffTemplate> _buffsById = [];
    private Dictionary<uint, List<ExpeditionBuffGrade>> _gradesByBuffId = [];

    public IEnumerable<ExpeditionBuffTemplate> Buffs => _buffsById.Values;

    public ExpeditionBuffTemplate GetBuff(uint buffId) => _buffsById.GetValueOrDefault(buffId);

    public IReadOnlyList<ExpeditionBuffGrade> GetGrades(uint buffId) =>
        _gradesByBuffId.TryGetValue(buffId, out var grades) ? grades : [];

    public ExpeditionBuffGrade GetGrade(uint buffId, byte grade) =>
        GetGrades(buffId).FirstOrDefault(g => g.Grade == grade);

    public byte GetMaxGrade(uint buffId) => GetGrades(buffId).Count == 0 ? (byte)0 : GetGrades(buffId).Max(g => g.Grade);

    public void Load(SqliteConnection connection)
    {
        _buffsById = [];
        _gradesByBuffId = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM expedition_buffs";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var buff = new ExpeditionBuffTemplate
                {
                    Id = reader.GetUInt32("id"),
                    Name = reader.GetString("name"),
                    DisplayOrder = reader.GetInt32("display_order", 1),
                    ExpeditionLevelId = reader.GetUInt32("expedition_level_id", 0),
                    Active = reader.GetBoolean("active")
                };
                _buffsById[buff.Id] = buff;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM expedition_buff_grades ORDER BY expedition_buff_id, grade";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var grade = new ExpeditionBuffGrade
                {
                    Id = reader.GetUInt32("id"),
                    ExpeditionBuffId = reader.GetUInt32("expedition_buff_id"),
                    Grade = (byte)reader.GetInt32("grade", 1),
                    Description = reader.GetString("desc"),
                    Contribution = reader.GetInt32("contribution", 0),
                    ItemId = reader.GetUInt32("item_id", 0),
                    Count = reader.GetInt32("count", 0),
                    ExpeditionLevelId = reader.GetUInt32("expedition_level_id", 0),
                    Housing = reader.GetBoolean("housing", false)
                };
                if (!_gradesByBuffId.TryGetValue(grade.ExpeditionBuffId, out var list))
                    _gradesByBuffId[grade.ExpeditionBuffId] = list = [];
                list.Add(grade);
            }
        }
    }

    public void PostLoad()
    {
    }

    /// <summary>
    /// Maps a purchased grade to the actual stat bonus every online guild member should receive.
    /// expedition_buff_grades has no buff/skill-id column to key off - these formulas are transcribed
    /// from each grade row's own "desc" text.
    /// TODO: buffs 1-4 and 14, and part of 8/9, are intentionally unimplemented - no existing
    /// UnitAttribute models them without misapplying the bonus to the wrong mechanic (e.g. buff 1's
    /// "PVE damage" would need to be all-damage, wrongly buffing PVP too).
    /// </summary>
    public static IEnumerable<(UnitAttribute Attribute, UnitModifierType ModifierType, long Value)> GetBonusEffects(uint buffId, byte grade)
    {
        switch (buffId)
        {
            case 5: // 신체 강화: 힘/지능/민첩/정신/체력 + (2 + 6*grade) - matches +8/14/20/26/32/38 for grade 1-6
                var stat = 2 + 6L * grade;
                yield return (UnitAttribute.Str, UnitModifierType.Value, stat);
                yield return (UnitAttribute.Dex, UnitModifierType.Value, stat);
                yield return (UnitAttribute.Sta, UnitModifierType.Value, stat);
                yield return (UnitAttribute.Int, UnitModifierType.Value, stat);
                yield return (UnitAttribute.Spi, UnitModifierType.Value, stat);
                break;
            case 6: // 단단한 근육: 물리/마법 방어도 + (90 + 120*grade) - matches +210/330/450/570/690/810
                var defense = 90 + 120L * grade;
                yield return (UnitAttribute.Armor, UnitModifierType.Value, defense);
                yield return (UnitAttribute.MagicResist, UnitModifierType.Value, defense);
                break;
            case 7: // 건강한 신체: 생명력/활력 + (180*grade) - matches +180..1440 across grade 1-8; applied to
                     // both HP and MP as the closest resource-pool pair (no separate "Vitality" attribute exists)
                var vitality = 180L * grade;
                yield return (UnitAttribute.MaxHealth, UnitModifierType.Value, vitality);
                yield return (UnitAttribute.MaxMana, UnitModifierType.Value, vitality);
                break;
            case 8: // 봄날의 산책: only the 이동속도 (move speed) half is modeled, +(0.5*grade)% - matches
                     // 1/1.5/2/2.5/3/3.5/4%; rounded to the nearest whole percent since Bonus.Value is long
                yield return (UnitAttribute.MoveSpeedMul, UnitModifierType.Percent, (long)Math.Round(0.5 * grade, MidpointRounding.AwayFromZero));
                break;
            case 9: // 자유 운동: only the 수영속도 (swim speed) half is modeled - grade-indexed since it isn't
                     // linear (4/6/6/8/8% for grade 1-5); glide half has no attribute
                long[] swim = [0, 4, 6, 6, 8, 8];
                if (grade < swim.Length)
                    yield return (UnitAttribute.SwimSpeedMul, UnitModifierType.Percent, swim[grade]);
                break;
            case 10: // 명예로운 생활: 명예 점수 획득률 + (2 + grade)% - matches +3/4/5/6/7/8%, applied across
                     // every honor-gain source since there's no single generic HonorPointGainMul
                var honor = 2L + grade;
                yield return (UnitAttribute.HonorPointGainBattleFieldMul, UnitModifierType.Percent, honor);
                yield return (UnitAttribute.HonorPointGainNpcKillMul, UnitModifierType.Percent, honor);
                yield return (UnitAttribute.HonorPointGainTrialMul, UnitModifierType.Percent, honor);
                yield return (UnitAttribute.HonorPointGainWarMul, UnitModifierType.Percent, honor);
                yield return (UnitAttribute.HonorPointGainQuestMul, UnitModifierType.Percent, honor);
                break;
            case 11: // 보물 탐욕: 전리품 획득 확률 + (1 + grade)% - matches +2/3/4/5/6/7/8%
                yield return (UnitAttribute.DropRateMul, UnitModifierType.Percent, 1L + grade);
                break;
            case 12: // 앞서나가는 자: 경험치 획득률 + (2*grade)% - matches +2/4/6/8/10/12/14/16%
                yield return (UnitAttribute.ExpMul, UnitModifierType.Percent, 2L * grade);
                break;
            case 13: // 슬기로운 생활: 생활 점수 획득률 + (2 + grade)% - matches +3/4/5/6/7/8%
                yield return (UnitAttribute.LivingPointGainMul, UnitModifierType.Percent, 2L + grade);
                break;
        }
    }
}

public class ExpeditionBuffTemplate
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Minimum guild level for this perk category to even appear (its grade 1 may still separately require a higher level).</summary>
    public uint ExpeditionLevelId { get; set; }
    public bool Active { get; set; }
}

public class ExpeditionBuffGrade
{
    public uint Id { get; set; }
    public uint ExpeditionBuffId { get; set; }
    public byte Grade { get; set; }
    public string Description { get; set; }

    /// <summary>Contribution Point cost, paid by the purchasing character (same model as the existing Guild Contribution Shop - CSBuyItemsPacket/MerchantPackKind.ItemPoint).</summary>
    public int Contribution { get; set; }
    public uint ItemId { get; set; }
    public int Count { get; set; }

    /// <summary>Minimum guild level required to purchase this specific grade.</summary>
    public uint ExpeditionLevelId { get; set; }

    /// <summary>True when this grade requires the guild to already have its Guild Residence placed.
    /// See ExpeditionManager.TryPurchaseBuffGrade.</summary>
    public bool Housing { get; set; }
}
