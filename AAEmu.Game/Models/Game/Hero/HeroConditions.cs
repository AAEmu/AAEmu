using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Models.Game.Hero;

/// <summary>One hero_conditions row: the rules a hero season runs under.</summary>
/// <param name="VotableLeadershipPoint">Leadership needed to rate a peer or vote in the election.</param>
/// <param name="VotableLevel">
/// Level needed to take part. It gates BOTH sides of a peer rating: the client loads it once
/// (.text 0x164ff4) and compares it against the target first, then against the rater, so there is no
/// separate target threshold to read.
/// </param>
/// <param name="HeroCandidateScope">
/// How many of the top ranks count as candidates. 16 in shipped data, which is what makes the first 16
/// rows of the Candidates tab render brown instead of grey (hero_rank.lua:32).
/// </param>
/// <param name="LeadershipRankingScope">How many ranks the leaderboard is meant to show. 50 shipped.</param>
/// <param name="CandidateMinPoint">Leadership needed to stand as a candidate.</param>
/// <param name="CandidateMinLevel">Level needed to stand as a candidate.</param>
public readonly record struct HeroCondition(
    int VotableLeadershipPoint,
    int VotableLevel,
    int HeroCandidateScope,
    int LeadershipRankingScope,
    int CandidateMinPoint,
    int CandidateMinLevel);

/// <summary>
/// The hero_conditions rules for a season, cached.
/// </summary>
/// <remarks>
/// A season does not hold its own rules: heros.hero_condition_id points at the row, and the client
/// resolves it exactly that way before every gated action (.text 0x98b160 takes the season, reads the
/// condition id off it and looks the row up). Resolving it the same way here keeps the two agreeing when
/// a season ships different requirements - every shipped season currently points at condition 1, so a
/// hardcoded 55/500 happened to be right, but only by coincidence.
///
/// Both tables are static content and tiny - 121 seasons, one condition row - so the whole thing is read
/// once on first use and kept. Nothing here is worth a query per rating attempt.
/// </remarks>
public static class HeroConditions
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private static readonly Lock Gate = new();
    private static Dictionary<uint, HeroCondition> _byId;
    private static Dictionary<uint, uint> _conditionBySeason;

    /// <summary>
    /// What to fall back on when the tables cannot be read.
    /// </summary>
    /// <remarks>
    /// These are the shipped values, so a data failure degrades to the rules the client is enforcing on
    /// its side anyway. Refusing to gate at all would be worse: the client would show a rating button
    /// the server then rejected, or the reverse.
    /// </remarks>
    public static readonly HeroCondition Default = new(
        VotableLeadershipPoint: 500,
        VotableLevel: 55,
        HeroCandidateScope: 16,
        LeadershipRankingScope: 50,
        CandidateMinPoint: 0,
        CandidateMinLevel: 0);

    /// <summary>The rules of the season the server is currently advertising.</summary>
    public static HeroCondition Current => For(HeroSeason.CurrentId);

    /// <summary>The rules a given season runs under, or <see cref="Default"/> if it resolves to nothing.</summary>
    public static HeroCondition For(uint seasonId)
    {
        EnsureLoaded();

        if (_conditionBySeason.TryGetValue(seasonId, out var conditionId) &&
            _byId.TryGetValue(conditionId, out var condition))
            return condition;

        return Default;
    }

    private static void EnsureLoaded()
    {
        if (_byId != null)
            return;

        lock (Gate)
        {
            if (_byId != null)
                return;

            var byId = new Dictionary<uint, HeroCondition>();
            var bySeason = new Dictionary<uint, uint>();

            try
            {
                using var connection = SQLite.CreateConnection();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        "SELECT id, votable_leadership_point, votable_level, hero_candidate_scope, " +
                        "leadership_ranking_scope, hero_candidate_min_point, hero_candidate_min_level " +
                        "FROM hero_conditions";
                    command.Prepare();

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        byId[Convert.ToUInt32(reader["id"])] = new HeroCondition(
                            Convert.ToInt32(reader["votable_leadership_point"]),
                            Convert.ToInt32(reader["votable_level"]),
                            Convert.ToInt32(reader["hero_candidate_scope"]),
                            Convert.ToInt32(reader["leadership_ranking_scope"]),
                            Convert.ToInt32(reader["hero_candidate_min_point"]),
                            Convert.ToInt32(reader["hero_candidate_min_level"]));
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT id, hero_condition_id FROM heros";
                    command.Prepare();

                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                        bySeason[Convert.ToUInt32(reader["id"])] = Convert.ToUInt32(reader["hero_condition_id"]);
                }

                Logger.Info("Loaded {0} hero conditions across {1} seasons", byId.Count, bySeason.Count);
            }
            catch (Exception ex)
            {
                // Cache the empty result anyway: a failure here is a missing or unreadable game database,
                // which retrying on every rating attempt will not fix, and For() already degrades to the
                // shipped values.
                Logger.Error(ex, "HeroConditions: failed to read hero_conditions/heros, using defaults");
            }

            _conditionBySeason = bySeason;
            _byId = byId;
        }
    }
}
