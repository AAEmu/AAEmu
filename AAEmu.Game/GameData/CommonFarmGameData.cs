using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.CommonFarm;
using AAEmu.Game.Models.CommonFarm.Static;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class CommonFarmGameData : Singleton<CommonFarmGameData>, IGameDataLoader
{
    public uint GuardTime = 86400000; // 24 часа

    private Dictionary<uint, FarmGroup> _farmGroup;
    private Dictionary<uint, FarmGroupDoodads> _farmGroupDoodads;

    public void Load(SqliteConnection connection)
    {
        _farmGroup = new Dictionary<uint, FarmGroup>();
        _farmGroupDoodads = new Dictionary<uint, FarmGroupDoodads>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM farm_groups";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new FarmGroup
                    {
                        Id = reader.GetUInt32("id"),
                        Count = reader.GetUInt32("count"),

                    };

                    _farmGroup.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM farm_group_doodads";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var template = new FarmGroupDoodads()
                    {
                        Id = reader.GetUInt32("id"),
                        FarmGroupId = reader.GetUInt32("farm_group_id"),
                        DoodadId = reader.GetUInt32("doodad_id"),
                        ItemId = reader.GetUInt32("item_id")
                    };

                    _farmGroupDoodads.Add(template.Id, template);
                }
            }
        }
    }

    public uint GetFarmGroupMaxCount(FarmType farmType)
    {
        if (_farmGroup.TryGetValue((uint)farmType, out var farm))
        {
            return farm.Count;
        }

        return 0;
    }

    public List<uint> GetAllowedDoodads(FarmType farmType)
    {
        var doodads = new List<uint>();

        foreach (var item in _farmGroupDoodads)
        {
            if (item.Value.FarmGroupId == (uint)farmType)
            {
                doodads.Add(item.Value.DoodadId);
            }
        }

        return doodads;
    }

    public void PostLoad()
    {
    }
}
