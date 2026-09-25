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
    private Dictionary<uint, List<(UnitAttribute Attribute, UnitModifierType ModifierType, long Value)>> _bonusesByGradeId = [];
    private IReadOnlyList<string> _contentDiagnostics = [];

    public IEnumerable<ExpeditionBuffTemplate> Buffs => _buffsById.Values;
    public IReadOnlyList<string> ContentDiagnostics => _contentDiagnostics;

    public ExpeditionBuffTemplate GetBuff(uint buffId) => _buffsById.GetValueOrDefault(buffId);

    public IReadOnlyList<ExpeditionBuffGrade> GetGrades(uint buffId) =>
        _gradesByBuffId.TryGetValue(buffId, out var grades) ? grades : [];

    public ExpeditionBuffGrade GetGrade(uint buffId, byte grade) =>
        GetGrades(buffId).FirstOrDefault(g => g.Grade == grade);

    public byte GetMaxGrade(uint buffId) => GetGrades(buffId).Count == 0 ? (byte)0 : GetGrades(buffId).Max(g => g.Grade);

    public void SetForTest(ExpeditionBuffTemplate buff, params ExpeditionBuffGrade[] grades)
    {
        ArgumentNullException.ThrowIfNull(buff);
        _buffsById[buff.Id] = buff;
        _gradesByBuffId[buff.Id] = grades.OrderBy(grade => grade.Grade).ToList();
    }

    public void Load(SqliteConnection connection)
    {
        _buffsById = [];
        _gradesByBuffId = [];
        _bonusesByGradeId = [];
        _contentDiagnostics = [];

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
                    Housing = reader.GetBoolean("housing", false),
                    SummonLimit = reader.GetInt32("summon_limit", 0),
                    PortalPointLimit = reader.GetInt32("portal_point_limit", 0)
                };
                if (!_gradesByBuffId.TryGetValue(grade.ExpeditionBuffId, out var list))
                    _gradesByBuffId[grade.ExpeditionBuffId] = list = [];
                list.Add(grade);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT owner_id, unit_attribute_id, unit_modifier_type_id, value " +
                                  "FROM unit_modifiers WHERE owner_type = 'ExpeditionBuffGrade' AND enable = 't'";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var gradeId = reader.GetUInt32("owner_id");
                if (!_bonusesByGradeId.TryGetValue(gradeId, out var bonuses))
                    _bonusesByGradeId[gradeId] = bonuses = [];
                bonuses.Add(((UnitAttribute)reader.GetUInt32("unit_attribute_id"),
                    (UnitModifierType)reader.GetUInt32("unit_modifier_type_id"), reader.GetInt64("value")));
            }
        }
    }

    public void PostLoad()
    {
        var diagnostics = new List<string>();
        var gradeIds = new HashSet<uint>();
        var noItemId = 0u;

        foreach (var buff in _buffsById.Values.OrderBy(row => row.Id))
        {
            if (!_gradesByBuffId.TryGetValue(buff.Id, out var grades) || grades.Count == 0)
            {
                diagnostics.Add($"buff {buff.Id} has no grades");
                continue;
            }

            var expectedGrade = 1u;
            foreach (var grade in grades.OrderBy(row => row.Grade))
            {
                if (grade.Id == 0 || !gradeIds.Add(grade.Id))
                    diagnostics.Add($"buff {buff.Id} grade {grade.Grade} has a duplicate or empty grade id");
                if (grade.ExpeditionBuffId != buff.Id)
                    diagnostics.Add($"grade {grade.Id} points at buff {grade.ExpeditionBuffId}, not {buff.Id}");
                if (grade.Grade != expectedGrade)
                    diagnostics.Add($"buff {buff.Id} grade sequence has {grade.Grade}, expected {expectedGrade}");
                expectedGrade++;
                if (buff.ExpeditionLevelId == 0 || grade.ExpeditionLevelId == 0)
                    diagnostics.Add($"buff {buff.Id} grade {grade.Grade} has no expedition level requirement");
                if (grade.Contribution < 0)
                    diagnostics.Add($"grade {grade.Id} has a negative contribution cost");
                if (grade.Count < 0 || (grade.ItemId == noItemId && grade.Count != 0) ||
                    (grade.ItemId != noItemId && grade.Count <= 0))
                    diagnostics.Add($"grade {grade.Id} has an invalid item cost");
                if (grade.SummonLimit < 0 || grade.PortalPointLimit < 0)
                    diagnostics.Add($"grade {grade.Id} has a negative capacity bonus");
            }
        }

        foreach (var ownerId in _bonusesByGradeId.Keys)
        {
            if (!gradeIds.Contains(ownerId))
                diagnostics.Add($"unit modifier owner {ownerId} has no expedition buff grade");
        }

        _contentDiagnostics = diagnostics;
        if (diagnostics.Count > 0)
            throw new InvalidOperationException("Expedition buff content is inconsistent: " + string.Join("; ", diagnostics));
    }

    /// <summary>
    /// Classifies the only duration behavior that is authored for a purchased grade: a duration
    /// modifier row. The grade table itself has no expiry/TTL field, so no grade timer is inferred.
    /// </summary>
    internal GuildBuffDurationSemantics GetDurationSemantics(uint buffId, byte grade)
    {
        var gradeTemplate = GetGrade(buffId, grade);
        if (gradeTemplate == null)
            return GuildBuffDurationSemantics.UnknownGrade;

        return GuildBuffDurationRules.Classify(gradeTemplate, BuffGameData.Instance.GetGradeModifiers(gradeTemplate.Id));
    }

    /// <summary>
    /// Returns the authoritative <c>unit_modifiers</c> owned by a purchased expedition buff grade.
    /// </summary>
    public IEnumerable<(UnitAttribute Attribute, UnitModifierType ModifierType, long Value)> GetBonusEffects(uint buffId, byte grade)
    {
        var gradeId = GetGrade(buffId, grade)?.Id ?? 0;
        return _bonusesByGradeId.TryGetValue(gradeId, out var bonuses) ? bonuses : [];
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
    public int SummonLimit { get; set; }
    public int PortalPointLimit { get; set; }
}
