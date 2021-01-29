 using System.Collections.Generic;
 using System.Linq;
 using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Skills;
 using AAEmu.Game.Models.Game.Skills.Buffs;
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
        private Dictionary<uint, BuffTolerance> _buffTolerances;
        private Dictionary<uint, BuffTolerance> _buffTolerancesById;
        
        public List<BuffModifier> GetModifiersForBuff(uint ownerId)
        {
            return _buffModifiers.ContainsKey(ownerId) ? _buffModifiers[ownerId] : new List<BuffModifier>();
        }

        public BuffTolerance GetBuffToleranceForBuffTag(uint buffTag)
        {
            return _buffTolerances.ContainsKey(buffTag) ? _buffTolerances[buffTag] : null;
        }
        
        public void Load(SqliteConnection connection)
        {
            _buffModifiers = new Dictionary<uint, List<BuffModifier>>();
            _buffTolerances = new Dictionary<uint, BuffTolerance>();
            _buffTolerancesById = new Dictionary<uint, BuffTolerance>();
            
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
                            Synergy = reader.GetBoolean("synergy", true),
                        };

                        if (!_buffModifiers.ContainsKey(template.OwnerId))
                            _buffModifiers.Add(template.OwnerId, new List<BuffModifier>());
                        _buffModifiers[template.OwnerId].Add(template);
                    }
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
                        var template = new BuffTolerance()
                        {
                            Id = reader.GetUInt32("id"),
                            BuffTagId = reader.GetUInt32("buff_tag_id"),
                            StepDuration = reader.GetUInt32("step_duration"),
                            FinalStepBuffId = reader.GetUInt32("final_step_buff_id"),
                            CharacterTimeReduction = reader.GetUInt32("character_time_reduction"),
                            Steps = new List<BuffToleranceStep>()
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
                        if (!_buffTolerancesById.ContainsKey(buffToleranceId))
                            continue;
                        var buffTolerance = _buffTolerancesById[buffToleranceId];
                        var template = new BuffToleranceStep()
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
    }
}
