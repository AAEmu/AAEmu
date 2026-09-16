using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public class BuffGameData : Singleton<BuffGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, List<BuffModifier>> _buffModifiers;
    private Dictionary<uint, List<BuffModifier>> _itemModifiers;
    private Dictionary<uint, List<BuffModifier>> _gradeModifiers;
    private Dictionary<uint, BuffTolerance> _buffTolerances;
    private Dictionary<uint, BuffTolerance> _buffTolerancesById;

    /// <summary>
    /// The modifiers the buff whose id this is grants to other buffs (owner_type='Buff'). Rows owned by an
    /// item or an expedition buff grade are not part of this table; see <see cref="ModifierOwnerRules"/>.
    /// </summary>
    public List<BuffModifier> GetModifiersForBuff(uint ownerId)
    {
        return _buffModifiers.TryGetValue(ownerId, out var modifier) ? modifier : [];
    }

    /// <summary>The modifiers an equipped item grants, keyed by the item template id in its owner_id.</summary>
    public List<BuffModifier> GetItemModifiers(uint itemTemplateId)
    {
        return _itemModifiers.TryGetValue(itemTemplateId, out var modifier) ? modifier : [];
    }

    /// <summary>The modifiers an expedition buff grade grants, keyed by expedition_buff_grades.id.</summary>
    public List<BuffModifier> GetGradeModifiers(uint gradeId)
    {
        return _gradeModifiers.TryGetValue(gradeId, out var modifier) ? modifier : [];
    }

    public BuffTolerance GetBuffToleranceForBuffTag(uint buffTag)
    {
        return _buffTolerances.TryGetValue(buffTag, out var tolerance) ? tolerance : null;
    }

    public void Load(SqliteConnection connection)
    {
        _buffModifiers = [];
        _itemModifiers = [];
        _gradeModifiers = [];
        _buffTolerances = [];
        _buffTolerancesById = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM buff_modifiers";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                // 921 Buff, 115 Item and 21 ExpeditionBuffGrade rows in 10.0.2.13. Only the Buff rows are
                // granted by the buff the owner_id names; the other two are filed under the owner they
                // really have, so an item id that coincides with a buff id no longer applies to that buff.
                var unknownOwners = new List<string>();
                while (reader.Read())
                {
                    var template = new BuffModifier
                    {
                        Id = reader.GetUInt32("id"),
                        OwnerId = reader.GetUInt32("owner_id"),
                        OwnerType = reader.GetString("owner_type"),
                        TagId = reader.GetUInt32("tag_id", 0),
                        BuffAttribute = (BuffAttribute)reader.GetUInt32("buff_attribute_id"),
                        UnitModifierType = (UnitModifierType)reader.GetUInt32("unit_modifier_type_id"),
                        Value = reader.GetInt32("value"),
                        BuffId = reader.GetUInt32("buff_id", 0),
                        Synergy = reader.GetBoolean("synergy", true),
                    };

                    switch (ModifierOwnerRules.Classify(template.OwnerType))
                    {
                        case ModifierOwner.Buff:
                            Add(_buffModifiers, template.OwnerId, template);
                            break;
                        case ModifierOwner.Item:
                            Add(_itemModifiers, template.OwnerId, template);
                            break;
                        case ModifierOwner.ExpeditionBuffGrade:
                            Add(_gradeModifiers, template.OwnerId, template);
                            break;
                        default:
                            unknownOwners.Add(template.OwnerType);
                            break;
                    }
                }

                if (unknownOwners.Count > 0)
                    Logger.Warn("10.0.2.13: {0} buff_modifiers rows carry an owner_type this server does not " +
                                "file ({1}) and are inert",
                        unknownOwners.Count, string.Join(", ", unknownOwners.Distinct()));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM buff_tolerances";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new BuffTolerance
                    {
                        Id = reader.GetUInt32("id"),
                        BuffTagId = reader.GetUInt32("buff_tag_id"),
                        StepDuration = reader.GetUInt32("step_duration"),
                        FinalStepBuffId = reader.GetUInt32("final_step_buff_id"),
                        CharacterTimeReduction = reader.GetUInt32("character_time_reduction"),
                        Steps = []
                    };

                    _buffTolerances.Add(template.BuffTagId, template);
                    _buffTolerancesById.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM buff_tolerance_steps";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var buffToleranceId = reader.GetUInt32("buff_tolerance_id");
                    if (!_buffTolerancesById.TryGetValue(buffToleranceId, out var buffTolerance)) // potential bug?
                        continue;
                    var template = new BuffToleranceStep
                    {
                        Id = reader.GetUInt32("id"),
                        BuffTolerance = buffTolerance,
                        HitChance = reader.GetUInt32("hit_chance"),
                        TimeReduction = reader.GetUInt32("time_reduction")
                    };

                    buffTolerance.Steps.Add(template);
                }
            }
        }
    }

    public void PostLoad()
    {
        foreach (var buffToleranceId in _buffTolerances.Keys)
        {
            _buffTolerances[buffToleranceId].Steps = _buffTolerances[buffToleranceId].Steps.OrderBy(st => st.Id).ToList();
        }
    }

    private static void Add(Dictionary<uint, List<BuffModifier>> table, uint ownerId, BuffModifier modifier)
    {
        if (!table.TryGetValue(ownerId, out var list))
        {
            list = [];
            table.Add(ownerId, list);
        }

        list.Add(modifier);
    }
}
