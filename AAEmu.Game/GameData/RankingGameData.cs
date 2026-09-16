using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
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

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, RankDefinition> _ranks = [];
    private readonly Dictionary<uint, string> _detailTypes = [];
    private readonly Dictionary<uint, RankGate> _gates = [];

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
                "SELECT id, name, rank_detail_id, tab_name, display_order FROM ranks";
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
                    DisplayOrder = reader.GetInt32("display_order", 0)
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

        Logger.Info("Rankings: {0} boards loaded, {1} of them gear score, {2} of them gated",
            _ranks.Count,
            _ranks.Values.Count(rank => rank.DetailType == GearScoreDetailType),
            _gates.Count);
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

    /// <summary>The boards whose value kind is the one asked for, in the table's display order.</summary>
    public List<RankDefinition> BoardsMeasuring(string detailType)
    {
        return _ranks.Values
            .Where(rank => string.Equals(rank.DetailType, detailType, StringComparison.Ordinal))
            .OrderBy(rank => rank.DisplayOrder)
            .ToList();
    }
}
