using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.CommonFarm;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class CommonFarmGameData : Singleton<CommonFarmGameData>, IGameDataLoader
{
    private Dictionary<uint, FarmGroup> _farmGroup;
    private Dictionary<uint, CommonFarm> _commonFarm;
    private Dictionary<uint, FarmGroupDoodads> _farmGroupDoodads;

    public void Load(SqliteConnection connection)
    {
        _farmGroup = [];
        _farmGroupDoodads = [];
        _commonFarm = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM common_farms";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new CommonFarm()
                {
                    Id = reader.GetUInt32("id"),
                    FarmGroupId = reader.GetUInt32("farm_group_id"),
                    GuardTime = reader.GetUInt32("guard_time")
                };

                _commonFarm.TryAdd(template.Id, template);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM farm_groups";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var template = new FarmGroup { Id = reader.GetUInt32("id"), Count = reader.GetUInt32("count") };

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

        
    }

    public uint GetFarmGroupMaxCount(FarmType farmType)
    {
        return _farmGroup.TryGetValue((uint)farmType, out var farm) ? farm.Count : 0;
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
