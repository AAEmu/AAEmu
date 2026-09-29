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
    /// The farm group whose capacity governs a tab, whether or not the tab has a <c>farm_groups</c>
    /// row of its own.
    /// </summary>
    /// <param name="farmType">The tab a player is planting into.</param>
    /// <param name="owningGroupId">The group whose <c>farm_groups</c> row states the size.</param>
    /// <returns>
    /// <c>false</c> when no group governs the tab, in which case no size is enforced and the caller
    /// is expected to log it.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A tab with no row of its own is not necessarily a tab with no size. Content states a tab's size
    /// per farm group, and the shipped catalogue only writes rows for two of the four tabs - but the
    /// tabs it omits draw their crops from a group that does have one. Of the six crops content lists
    /// for Nursery, five are also listed for group 1, and all six for Ranch are, so both tabs are
    /// planted out of group 1's pool and a limit that ignored that would let a player exceed the one
    /// number content actually wrote down.
    /// </para>
    /// <para>
    /// The owner is derived from that membership rather than named in code, so a content patch that
    /// moves a crop changes the answer. A tab that carries no row and shares no crop with a group that
    /// has one resolves to no owner, which is the honest answer and leaves the loud log in place.
    /// </para>
    /// </remarks>
    public bool TryGetOwningFarmGroup(FarmType farmType, out uint owningGroupId)
    {
        // A row of its own always wins: content stated this tab's size directly.
        if (_farmGroup.ContainsKey((uint)farmType))
        {
            owningGroupId = (uint)farmType;
            return true;
        }

        var tabDoodads = new HashSet<uint>();
        foreach (var row in _farmGroupDoodads.Values)
        {
            if (row.FarmGroupId == farmType)
                tabDoodads.Add(row.DoodadId);
        }

        if (tabDoodads.Count == 0)
        {
            owningGroupId = 0;
            return false;
        }

        // Most shared crops wins, and the lowest group id breaks a tie, so the answer is stable
        // across restarts rather than depending on dictionary order.
        var bestGroup = 0u;
        var bestShared = 0;
        foreach (var groupId in _farmGroup.Keys)
        {
            var groupDoodads = new HashSet<uint>();
            foreach (var row in _farmGroupDoodads.Values)
            {
                if (row.FarmGroupId == (FarmType)groupId)
                    groupDoodads.Add(row.DoodadId);
            }

            var shared = tabDoodads.Count(groupDoodads.Contains);
            if (shared == 0 || shared < bestShared || (shared == bestShared && groupId >= bestGroup))
                continue;

            bestGroup = groupId;
            bestShared = shared;
        }

        if (bestGroup == 0)
        {
            owningGroupId = 0;
            return false;
        }

        owningGroupId = bestGroup;
        return true;
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
