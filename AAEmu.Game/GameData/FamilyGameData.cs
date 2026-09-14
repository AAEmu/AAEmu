using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Families;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>Family levels, member-cap expansions, and roles from the client content database.</summary>
[GameData]
public class FamilyGameData : Singleton<FamilyGameData>, IGameDataLoader
{
    private Dictionary<uint, FamilyLevel> _levels = [];
    private Dictionary<int, FamilyMemberLimit> _memberLimits = [];
    private Dictionary<uint, FamilyRole> _roles = [];

    public uint MaxLevel { get; private set; }
    public int MaxMemberLimit { get; private set; }

    public FamilyLevel GetLevel(uint level) => _levels.GetValueOrDefault(level);
    public FamilyRole GetRole(uint roleId) => _roles.GetValueOrDefault(roleId);
    public FamilyMemberLimit GetMemberLimit(int count) => _memberLimits.GetValueOrDefault(count);
    public FamilyMemberLimit GetNextMemberLimit(int currentLimit) =>
        _memberLimits.Values.Where(x => x.Count > currentLimit).MinBy(x => x.Count);
    public int GetMemberLimit(uint increasedMemberCount) => increasedMemberCount == 0
        ? FamilyContentConfig.MaximumCount
        : _memberLimits.Values.OrderBy(x => x.Count).ElementAtOrDefault(
            checked((int)Math.Min(increasedMemberCount, int.MaxValue)) - 1)?.Count ?? MaxMemberLimit;

    /// <summary>Returns the highest level whose EXP requirement is met; leveling is an explicit skill action.</summary>
    public uint GetEligibleLevelForExp(uint exp)
    {
        var result = _levels.Values
            .Where(x => x.Exp <= exp)
            .MaxBy(x => x.Exp);
        return result?.Level ?? 0;
    }

    public void Load(SqliteConnection connection)
    {
        _levels = [];
        _memberLimits = [];
        _roles = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM family_levels";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var level = new FamilyLevel
                {
                    Id = reader.GetUInt32("id"),
                    Level = reader.GetUInt32("level", 0),
                    GradeName = reader.GetString("grade_name", string.Empty),
                    Exp = reader.GetUInt32("exp", 0),
                    BuffId = reader.GetUInt32("buff_id", 0)
                };
                _levels[level.Level] = level;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM family_member_limits";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var limit = new FamilyMemberLimit
                {
                    Id = reader.GetUInt32("id"),
                    Count = reader.GetInt32("count", 0),
                    ItemId = reader.GetUInt32("item_id", 0),
                    ItemCount = reader.GetInt32("item_count", 0)
                };
                _memberLimits[limit.Count] = limit;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM family_roles";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var role = new FamilyRole
                {
                    Id = reader.GetUInt32("id"),
                    IconId = reader.GetString("icon_id", string.Empty),
                    RoleName = reader.GetString("role_name", string.Empty),
                    RoleCount = reader.GetInt32("role_count", 0)
                };
                _roles[role.Id] = role;
            }
        }

    }

    public void PostLoad()
    {
        MaxLevel = _levels.Count == 0 ? 0 : _levels.Keys.Max();
        MaxMemberLimit = _memberLimits.Count == 0 ? FamilyContentConfig.MaximumCount : _memberLimits.Keys.Max();
    }
}
