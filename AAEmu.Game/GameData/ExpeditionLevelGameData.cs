using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// Guild (Expedition) level thresholds, loaded from <c>expedition_levels</c>.
/// </summary>
/// <remarks>
/// Row id doubles as the level number (contiguous from 1, total_exp 0). A level is reached
/// automatically once its total_exp threshold is met, unless its require_item_id is non-zero, in
/// which case exp keeps accumulating but the level itself waits for an explicit confirm
/// (CSExpeditionLevelUpPacket, consuming the item) - same two-tier shape as HeirGameData's
/// req_item_id gate, not independently wire-confirmed for Expedition but the column names mirror
/// heir_levels closely enough that this is the best-effort reading rather than a guess from nothing.
/// </remarks>
[GameData]
public class ExpeditionLevelGameData : Singleton<ExpeditionLevelGameData>, IGameDataLoader
{
    private Dictionary<uint, ExpeditionLevel> _levelsById = [];

    public uint MaxLevel { get; private set; }

    public ExpeditionLevel GetLevel(uint level) => _levelsById.GetValueOrDefault(level);

    /// <summary>
    /// Advances <paramref name="currentLevel"/> as far as <paramref name="totalExp"/> allows without
    /// requiring an item, stopping at the first level that needs an explicit level-up confirm.
    /// </summary>
    public uint GetAutoLevelForExp(uint currentLevel, long totalExp)
    {
        var level = currentLevel;
        while (true)
        {
            var next = GetLevel(level + 1);
            if (next == null || next.RequireItemId != 0 || totalExp < next.TotalExp)
                return level;
            level++;
        }
    }

    /// <summary>
    /// The next level, only when its exp threshold is already met and it requires an explicit
    /// confirm (i.e. a non-zero require_item_id).
    /// </summary>
    public bool TryGetLevelUpRequirement(uint currentLevel, long totalExp, out ExpeditionLevel requirement)
    {
        requirement = null;
        var next = GetLevel(currentLevel + 1);
        if (next == null || next.RequireItemId == 0 || totalExp < next.TotalExp)
            return false;

        requirement = next;
        return true;
    }

    public void Load(SqliteConnection connection)
    {
        _levelsById = [];

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM expedition_levels";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var level = new ExpeditionLevel
            {
                Id = reader.GetUInt32("id"),
                TotalExp = reader.GetInt64("total_exp", 0),
                DailyExp = reader.GetInt64("daily_exp", 0),
                MemberLimit = reader.GetInt32("member_limit", 0),
                SummonLimit = reader.GetInt32("summon_limit", 0),
                RequireItemId = reader.GetUInt32("require_item_id", 0),
                RequireItemAmount = reader.GetInt32("require_item_amount", 0),
                DailyContributionPoint = reader.GetInt32("daily_contribution_point", 0),
                PortalPointLimit = reader.GetInt32("portal_point_limit", 0)
            };
            _levelsById[level.Id] = level;
        }
    }

    public void PostLoad()
    {
        MaxLevel = _levelsById.Count == 0 ? 0 : _levelsById.Keys.Max();
    }
}
