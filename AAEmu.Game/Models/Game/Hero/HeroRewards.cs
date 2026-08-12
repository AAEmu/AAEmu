using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Models.Game.Hero;

/// <summary>One hero_rewards row: what a given placing in a given nation's election is worth.</summary>
/// <param name="Ranking">Placing in the election, 1 being the winner.</param>
/// <param name="Nation">top_faction_id - the nation whose election this placing belongs to.</param>
/// <param name="Grade">hero_grades row the placing is seated at.</param>
/// <param name="ItemSetId">Reward item set granted for the term.</param>
/// <param name="DominionPointWeekly">Dominion points the seat earns per week.</param>
/// <param name="DominionTax">Whether the seat collects dominion tax. Only the winner does.</param>
/// <param name="DefaultRezDistrictBinds">Resurrection district binds the seat may set.</param>
/// <param name="InferiorRezDistrictBinds">The same for inferior districts.</param>
public readonly record struct HeroReward(
    int Ranking,
    uint Nation,
    byte Grade,
    uint ItemSetId,
    int DominionPointWeekly,
    bool DominionTax,
    int DefaultRezDistrictBinds,
    int InferiorRezDistrictBinds);

/// <summary>
/// hero_rewards, cached: how many heroes a nation seats, at what grades, for what.
/// </summary>
/// <remarks>
/// This table is the authority on both figures the election needs, and both were hardcoded before it was
/// read. It has one row per placing per nation, so the seat count is simply how many rows a nation has
/// and the grade is the row's own:
///
///   148 Nuia and 149 Haranya   ranks 1-6, grades 4,3,3,2,2,2
///   114 Outlaw                 ranks 1-3, grades 4,3,3
///   166 Independent            ranks 1-3, grades 4,3,3
///
/// The shipped data agrees with what was inferred from the client's hero_election_rule text and the
/// six-slot pyramid, which is reassuring but not a reason to keep guessing: 166 exists and was only
/// right by accident, and a nation added later would not be.
/// </remarks>
public static class HeroRewards
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private static readonly Lock Gate = new();
    private static Dictionary<(uint Nation, int Ranking), HeroReward> _byPlacing;
    private static Dictionary<uint, int> _seatsByNation;

    /// <summary>
    /// What a nation seats when the table says nothing about it.
    /// </summary>
    /// <remarks>
    /// Three, the smaller of the two shipped sizes. An unknown nation seating too few costs its players
    /// some seats; seating too many would seat heroes at grades no reward row covers.
    /// </remarks>
    public const int DefaultSeats = 3;

    /// <summary>How many heroes a nation elects.</summary>
    public static int SeatsFor(uint nation)
    {
        EnsureLoaded();
        return _seatsByNation.TryGetValue(nation, out var seats) ? seats : DefaultSeats;
    }

    /// <summary>The reward row for a placing, if the data has one.</summary>
    public static HeroReward? For(uint nation, int ranking)
    {
        EnsureLoaded();
        return _byPlacing.TryGetValue((nation, ranking), out var reward) ? reward : null;
    }

    /// <summary>
    /// The grade a placing is seated at, or 0 when the data does not cover it.
    /// </summary>
    /// <remarks>
    /// 0 rather than a guess: a placing with no reward row is a placing the nation does not have, and the
    /// caller should not seat it at all.
    /// </remarks>
    public static byte GradeFor(uint nation, int ranking) => For(nation, ranking)?.Grade ?? 0;

    private static void EnsureLoaded()
    {
        if (_byPlacing != null)
            return;

        lock (Gate)
        {
            if (_byPlacing != null)
                return;

            var byPlacing = new Dictionary<(uint, int), HeroReward>();
            var seats = new Dictionary<uint, int>();

            try
            {
                using var connection = SQLite.CreateConnection();
                using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT ranking, top_faction_id, hero_grade_id, item_set_id, dominion_point_weekly_count, " +
                    "dominion_tax, default_rez_district_bind_count, inferior_rez_district_bind_count " +
                    "FROM hero_rewards";
                command.Prepare();

                // SQLiteWrapperReader, not a raw reader: SQLite stores booleans as the strings 't' and
                // 'f', and Convert.ToBoolean("t") throws. That threw here, the catch below cached an
                // empty table, and every placing then resolved to grade 0 - so an election counted its
                // ballots correctly and seated nobody, reporting only "no hero_rewards row for rank 1".
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var reward = new HeroReward(
                        reader.GetInt32("ranking"),
                        reader.GetUInt32("top_faction_id"),
                        reader.GetByte("hero_grade_id"),
                        reader.GetUInt32("item_set_id"),
                        reader.GetInt32("dominion_point_weekly_count"),
                        reader.GetBoolean("dominion_tax"),
                        reader.GetInt32("default_rez_district_bind_count"),
                        reader.GetInt32("inferior_rez_district_bind_count"));

                    byPlacing[(reward.Nation, reward.Ranking)] = reward;
                    seats[reward.Nation] = Math.Max(seats.GetValueOrDefault(reward.Nation), reward.Ranking);
                }

                Logger.Info("Loaded {0} hero reward placings across {1} nation(s)", byPlacing.Count, seats.Count);
            }
            catch (Exception ex)
            {
                // Cached empty: SeatsFor and GradeFor both degrade to something safe, and retrying per
                // election would not fix a missing game database.
                Logger.Error(ex, "HeroRewards: failed to read hero_rewards");
            }

            _seatsByNation = seats;
            _byPlacing = byPlacing;
        }
    }
}
