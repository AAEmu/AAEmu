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
    private Dictionary<uint, FarmGroupDoodads> _farmGroupDoodads;
    private Dictionary<uint, DoodadGroups> _doodadGroups;

    public void Load(SqliteConnection connection)
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

    /// <summary>
    /// Resolves the crop capacity configured for a farm group.
    /// </summary>
    /// <param name="farmType">The farm type, which is the farm group id.</param>
    /// <param name="maxCount">The configured capacity, or zero when the group has no row.</param>
    /// <returns>
    /// <c>false</c> when content has no <c>farm_groups</c> row for this farm type, meaning its size
    /// is unknown. The caller must decide what an unconfigured farm means and must not inherit a
    /// number: this accessor exists so a missing row cannot be read as a capacity of zero, which is
    /// a full farm rather than an absent one.
    /// </returns>
    public bool TryGetFarmGroupMaxCount(FarmType farmType, out uint maxCount)
    {
        if (_farmGroup.TryGetValue((uint)farmType, out var farm))
        {
            maxCount = farm.Count;
            return true;
        }

        maxCount = 0;
        return false;
    }

    /// <summary>
    /// Resolves how long a crop of this doodad group stays protected after it is planted.
    /// </summary>
    /// <param name="groupId">The doodad group the planted crop belongs to.</param>
    /// <param name="guardSeconds">The configured protection window, in seconds.</param>
    /// <returns>
    /// <c>false</c> when content has no <c>doodad_groups</c> row for this group, so the length of
    /// the protection window is unknown. The caller must not inherit a number: answering zero would
    /// retire a crop the moment it is planted, which is the opposite of a protection window.
    /// </returns>
    public bool TryGetDoodadGuardTime(uint groupId, out uint guardSeconds)
    {
        if (_doodadGroups.TryGetValue(groupId, out var group))
        {
            guardSeconds = group.GuardOnFieldTime;
            return true;
        }

        guardSeconds = 0;
        return false;
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
