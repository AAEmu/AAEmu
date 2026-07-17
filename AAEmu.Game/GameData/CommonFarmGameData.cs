using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.CommonFarm;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// Loads common farm group definitions and allowed doodad groups.
/// </summary>
[GameData]
public class CommonFarmGameData : Singleton<CommonFarmGameData>, IGameDataLoader
{
    private Dictionary<uint, FarmGroup> _farmGroup;
    private Dictionary<uint, FarmGroupDoodads> _farmGroupDoodads;
    private Dictionary<uint, DoodadGroups> _doodadGroups;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _farmGroup = [];
        _farmGroupDoodads = [];
        _doodadGroups = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM farm_groups";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new FarmGroup
                {
                    Id = reader.GetUInt32("id"),
                    Count = reader.GetUInt32("count"),
                    Description = reader.GetString("description"),
                    Name = reader.GetString("name")
                };

                _farmGroup.TryAdd(template.Id, template);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM farm_group_doodads";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new FarmGroupDoodads
                {
                    Id = reader.GetUInt32("id"),
                    FarmGroupId = (FarmType)reader.GetUInt32("farm_group_id"),
                    DoodadId = reader.GetUInt32("doodad_id"),
                    ItemId = reader.GetUInt32("item_id")
                };

                _farmGroupDoodads.TryAdd(template.Id, template);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM doodad_groups";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new DoodadGroups
                {
                    Id = reader.GetUInt32("id"),
                    GuardOnFieldTime = reader.GetUInt32("guard_on_field_time"),
                    IsExport = reader.GetBoolean("is_export"),
                    RemovedByHouse = reader.GetBoolean("removed_by_house")
                };

                _doodadGroups.TryAdd(template.Id, template);
            }
        }
    }

    public uint GetFarmGroupMaxCount(FarmType farmType)
    {
        return _farmGroup.TryGetValue((uint)farmType, out var farm) ? farm.Count : 0;
    }

    public uint GetDoodadGuardTime(uint groupId)
    {
        return _doodadGroups.TryGetValue(groupId, out var farm) ? farm.GuardOnFieldTime : 0;
    }

    public List<uint> GetAllowedDoodads(FarmType farmType)
    {
        return (from item in _farmGroupDoodads
                where item.Value.FarmGroupId == farmType
                select item.Value.DoodadId).ToList();
    }

    public void PostLoad()
    {
    }
}
