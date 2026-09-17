using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Rankings;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>One ranking board the table defines: what it is called and what it measures.</summary>
public class RankDefinition
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>The value kind behind the board, as <c>rank_details.actual_type</c> names it.</summary>
    public string DetailType { get; set; } = string.Empty;

    public string TabName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }

    /// <summary>The value kind behind the board (<c>enum_rank_kinds</c> id).</summary>
    public uint KindId { get; set; }

    /// <summary>Whether equal values share a place (<c>ranks.permit_tie</c>).</summary>
    public bool PermitTie { get; set; }

    /// <summary>The cycle the board starts over on, from <c>rank_resets</c>; 0 when it never does.</summary>
    public int ResetIntervalId { get; set; }

    /// <summary>The day the cycle turns on, as the client counts days.</summary>
    public int ResetDayOfWeekId { get; set; } = RankPeriods.NoDay;
}

/// <summary>One tier of a board: the places it covers, and what a holder in it is paid.</summary>
public class RankTier
{
    public uint Id { get; set; }
    public uint RankId { get; set; }

    /// <summary>Whether the tier covers places on this world only, or across the server.</summary>
    public bool IsLocal { get; set; }

    public int ScopeFrom { get; set; }
    public int ScopeTo { get; set; }

    /// <summary>The reward: an item at a grade, and/or a currency the game already has.</summary>
    public uint RewardItemId { get; set; }
    public int RewardItemCount { get; set; }
    public int RewardItemGradeId { get; set; }

    /// <summary>The currency paid, as <c>enum_currencies</c> names it; 0 when the tier pays none.</summary>
    public uint CurrencyId { get; set; }

    public int CurrencyAmount { get; set; }
}

/// <summary>One holder's place in a board and what it earns them.</summary>
public sealed record RankRewardGrant(
    RankHolderKind HolderKind,
    ulong HolderId,
    uint AccountId,
    byte WorldId,
    uint RankId,
    uint Position,
    uint TierId,
    uint ItemId,
    int ItemCount,
    int ItemGradeId,
    uint CurrencyId,
    int CurrencyAmount);

/// <summary>
/// What a board's tiers hand out, and which window they settle. Deciding a payout is kept apart from
/// paying it; the same window is what the window's own pre-season view shows.
/// </summary>
public static class RankPayouts
{
    /// <summary>The tier a place falls in, or null when the board gives that place no tier.</summary>
    public static RankTier TierFor(IEnumerable<RankTier> tiers, uint position, bool isLocal)
    {
        if (tiers == null)
            return null;

        return tiers.FirstOrDefault(tier => tier.IsLocal == isLocal
                                            && position >= tier.ScopeFrom
                                            && position <= tier.ScopeTo);
    }

    /// <summary>
    /// The window that ended when the given one opened, which is the one to pay out.
    /// </summary>
    /// <remarks>
    /// Asked as "the window one second before this one opened" rather than by subtracting the current
    /// window's length: a monthly window is a calendar month, and September's 30 days would put August's
    /// start on the 2nd.
    /// </remarks>
    public static RankPeriod Previous(int resetIntervalId, int resetDayOfWeekId, RankPeriod current)
    {
        return RankPeriods.For(current.StartUtc.AddSeconds(-1), resetIntervalId, resetDayOfWeekId);
    }

    /// <summary>
    /// The window a board's pre-season view shows: the one that closed most recently, which is the same
    /// window the payout settles. A board whose window never closes has no season behind it.
    /// </summary>
    public static RankPeriod? SeasonOf(RankDefinition board, RankPeriod current)
    {
        if (board == null || board.ResetIntervalId == 0)
            return null;

        return Previous(board.ResetIntervalId, board.ResetDayOfWeekId, current);
    }

    /// <summary>
    /// What every placed holder of a board's finished window is owed: the item its tier names at the grade
    /// the tier names, and the currency it names.
    /// </summary>
    public static List<RankRewardGrant> Plan(RankDefinition board, IReadOnlyList<RankTier> tiers,
        IReadOnlyList<RankPlace> standings)
    {
        var grants = new List<RankRewardGrant>();
        if (board == null || standings == null)
            return grants;

        foreach (var place in standings)
        {
            // The tiers of a place come in a local and a whole-server half, and the shipped local half
            // carries no reward, so the server half is the one that pays. One world is a whole server.
            var tier = TierFor(tiers, place.Position, isLocal: false);
            if (tier == null)
                continue;

            if (tier.RewardItemId == 0 && tier.CurrencyId == 0)
                continue;

            grants.Add(new RankRewardGrant(
                place.Score.HolderKind,
                place.Score.HolderId,
                place.Score.AccountId,
                place.Score.WorldId,
                board.Id,
                place.Position,
                tier.Id,
                tier.RewardItemId,
                tier.RewardItemCount,
                tier.RewardItemGradeId,
                tier.CurrencyId,
                tier.CurrencyAmount));
        }

        return grants;
    }
}

/// <summary>What a board counts, and the floor a value has to reach to be counted at all.</summary>
public class RankGate
{
    public uint RankId { get; set; }

    /// <summary>The score a character has to reach for a score board, from <c>gear_rank_details</c>.</summary>
    public int MinScore { get; set; }

    /// <summary>The item level an item has to reach for an item board, from <c>item_rank_details</c>.</summary>
    public int MinLevel { get; set; }

    /// <summary>The item grade an item has to reach for an item board.</summary>
    public int MinGrade { get; set; }
}

/// <summary>
/// The ranking boards this server ships. Each row names a value kind through <c>rank_details</c>, which is
/// what decides whether the World can fill a board from live state or has to leave it alone.
/// </summary>
[GameData]
public class RankingGameData : Singleton<RankingGameData>, IGameDataLoader
{
    /// <summary>The value kind whose board holds a character's gear score.</summary>
    public const string GearScoreDetailType = "GearRankDetail";

    /// <summary>The value kind whose boards measure one equipped weapon each.</summary>
    public const string ItemDetailType = "ItemRankDetail";

    /// <summary>The value kinds whose boards hold expeditions rather than characters.</summary>
    public const uint ExpeditionGearScoreKind = 12;
    public const uint ExpeditionBattleRecordKind = 13;
    public const uint ExpeditionInstanceRatingKind = 16;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, RankDefinition> _ranks = [];
    private readonly Dictionary<uint, string> _detailTypes = [];
    private readonly Dictionary<uint, RankGate> _gates = [];
    private readonly Dictionary<uint, (int Kind, int Method)> _gamePointCounters = [];
    private readonly Dictionary<uint, List<RankTier>> _tiersByRank = [];

    public void Load(SqliteConnection connection)
    {
        _ranks.Clear();
        _detailTypes.Clear();
        _gates.Clear();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, actual_type FROM rank_details";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
                _detailTypes[reader.GetUInt32("id")] = reader.GetString("actual_type") ?? string.Empty;
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT r.id, r.name, r.rank_detail_id, r.rank_kind_id, r.tab_name, r.display_order, r.permit_tie, " +
                "rs.reset_interval_id, rs.day_of_week_id " +
                "FROM ranks r LEFT JOIN rank_resets rs ON rs.id = r.rank_reset_id";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var detailId = reader.IsDBNull("rank_detail_id") ? 0u : reader.GetUInt32("rank_detail_id");
                var rank = new RankDefinition
                {
                    Id = reader.GetUInt32("id"),
                    Name = reader.GetString("name", string.Empty),
                    DetailType = detailId != 0 && _detailTypes.TryGetValue(detailId, out var type) ? type : string.Empty,
                    TabName = reader.GetString("tab_name", string.Empty),
                    DisplayOrder = reader.GetInt32("display_order", 0),
                    KindId = reader.GetUInt32("rank_kind_id"),
                    PermitTie = reader.GetBoolean("permit_tie"),
                    ResetIntervalId = reader.IsDBNull("reset_interval_id") ? 0 : reader.GetInt32("reset_interval_id"),
                    ResetDayOfWeekId = reader.IsDBNull("day_of_week_id") ? RankPeriods.NoDay : reader.GetInt32("day_of_week_id")
                };

                _ranks[rank.Id] = rank;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, min_score FROM gear_rank_details";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var rankId = reader.GetUInt32("id");
                Gate(rankId).MinScore = reader.GetInt32("min_score", 0);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, min_level, min_grade FROM item_rank_details";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var rankId = reader.GetUInt32("id");
                var gate = Gate(rankId);
                gate.MinLevel = reader.GetInt32("min_level", 0);
                gate.MinGrade = reader.GetInt32("min_grade", 0);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, game_point_kind, game_point_method FROM game_point_rank_details";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                _gamePointCounters[reader.GetUInt32("id")] =
                    (reader.GetInt32("game_point_kind", 0), reader.GetInt32("game_point_method", 0));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT id, rank_id, is_local, scope_from, scope_to, reward_item_id, reward_item_count, " +
                "reward_item_grade_id, currency_id, currency_amount FROM rank_tiers";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var tier = new RankTier
                {
                    Id = reader.GetUInt32("id"),
                    RankId = reader.GetUInt32("rank_id"),
                    IsLocal = reader.GetBoolean("is_local"),
                    ScopeFrom = reader.GetInt32("scope_from", 0),
                    ScopeTo = reader.GetInt32("scope_to", 0),
                    RewardItemId = reader.GetUInt32("reward_item_id", 0),
                    RewardItemCount = reader.GetInt32("reward_item_count", 0),
                    RewardItemGradeId = reader.GetInt32("reward_item_grade_id", 0),
                    CurrencyId = reader.GetUInt32("currency_id", 0),
                    CurrencyAmount = reader.GetInt32("currency_amount", 0)
                };

                if (!_tiersByRank.TryGetValue(tier.RankId, out var tiers))
                {
                    tiers = [];
                    _tiersByRank[tier.RankId] = tiers;
                }

                tiers.Add(tier);
            }
        }

        Logger.Info("Rankings: {0} boards loaded, {1} of them gear score, {2} of them gated, {3} counting a period total, {4} tiers over {5} boards",
            _ranks.Count,
            _ranks.Values.Count(rank => rank.DetailType == GearScoreDetailType),
            _gates.Count,
            _gamePointCounters.Count,
            _tiersByRank.Values.Sum(tiers => tiers.Count),
            _tiersByRank.Count);
    }

    private RankGate Gate(uint rankId)
    {
        if (!_gates.TryGetValue(rankId, out var gate))
        {
            gate = new RankGate { RankId = rankId };
            _gates[rankId] = gate;
        }

        return gate;
    }

    public void PostLoad()
    {
    }

    public IReadOnlyCollection<RankDefinition> Ranks => _ranks.Values;

    /// <summary>One board by the id the client asks for, or null when no board carries that id.</summary>
    public RankDefinition GetBoard(uint id)
    {
        return _ranks.TryGetValue(id, out var rank) ? rank : null;
    }

    /// <summary>The floor a board counts from. A board the tables give no gate is open.</summary>
    public RankGate GateFor(uint rankId)
    {
        return _gates.TryGetValue(rankId, out var gate) ? gate : new RankGate { RankId = rankId };
    }

    /// <summary>The window a board's values are counted in at the given moment.</summary>
    public RankPeriod PeriodFor(RankDefinition board, DateTime momentUtc)
    {
        // A board with no cycle in the table never starts over: its window is open-ended, which is how the
        // window displays it ("Period: On-going").
        return board == null || board.ResetIntervalId == 0
            ? new RankPeriod(DateTime.UnixEpoch, DateTime.MaxValue)
            : RankPeriods.For(momentUtc, board.ResetIntervalId, board.ResetDayOfWeekId);
    }

    /// <summary>Who a board's lines belong to: the character, or the expedition they are in.</summary>
    public RankHolderKind HolderKindOf(RankDefinition board)
    {
        return board?.KindId switch
        {
            ExpeditionGearScoreKind or ExpeditionBattleRecordKind or ExpeditionInstanceRatingKind => RankHolderKind.Expedition,
            _ => RankHolderKind.Character
        };
    }

    /// <summary>
    /// The running total a board ranks, as <c>game_point_rank_details</c> names it, or null when the board
    /// does not rank one (a board over a value the World reads directly).
    /// </summary>
    public (int Kind, int Method)? GamePointCounterOf(RankDefinition board)
    {
        return board != null && _gamePointCounters.TryGetValue(board.Id, out var counter) ? counter : null;
    }

    /// <summary>The tiers of a board, which are the places it pays for. Empty when it pays none.</summary>
    public IReadOnlyList<RankTier> TiersFor(uint rankId)
    {
        return _tiersByRank.TryGetValue(rankId, out var tiers) ? tiers : [];
    }

    /// <summary>The boards whose value kind is the one asked for, in the table's display order.</summary>
    public List<RankDefinition> BoardsMeasuring(string detailType)
    {
        return _ranks.Values
            .Where(rank => string.Equals(rank.DetailType, detailType, StringComparison.Ordinal))
            .OrderBy(rank => rank.DisplayOrder)
            .ToList();
    }
}
