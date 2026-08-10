using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Hero;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Hero;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>Why a rating was refused. Maps to the client's own reputation_* error strings.</summary>
public enum ReputationVoteResult
{
    Ok,
    IsMe,
    NotSameFaction,
    NotSameTeam,
    VoterLevelLow,
    VoterLeadershipLow,
    TargetLevelLow,
    AlreadyRatedToday
}

/// <summary>
/// Peer ratings - the input side of Leadership.
/// </summary>
/// <remarks>
/// The client states the rules in its own help text: "Rate a player's contributions to a party or raid.
/// Target a player in your party or raid to rate. Requirements to Rate: Must be Lv$1+ with $2+
/// Leadership." and "You can only rate a character once per day."
///
/// A rating moves the TARGET's reputation only. The rater gains nothing at the time. Leadership is paid
/// out later, at each Hero Qualification Evaluation, by ranking reputation and applying the
/// reputation_rewards percentile table - see Evaluate.
/// </remarks>
public class ReputationManager : Singleton<ReputationManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>reputation_rewards, ascending by percent - best band first.</summary>
    private ReputationReward[] _rewards = [];

    /// <summary>reputation_resets.time: hours between evaluations. 12 in shipped data.</summary>
    private int _resetHours = 12;

    /// <summary>
    /// Requirements to rate, from the current season's hero_conditions row.
    /// </summary>
    /// <remarks>
    /// Read through HeroConditions, which caches the table, rather than held as constants: the client
    /// resolves these per season through heros.hero_condition_id, so a season shipping different rules
    /// would leave a hardcoded pair silently disagreeing with the client on both sides of the same check.
    ///
    /// One level covers rater and target alike. The client loads votable_level once and compares it
    /// against the target first and the rater second (.text 0x164ff4), so the two thresholds the
    /// reputation_*_level_low strings imply are in fact one value.
    /// </remarks>
    public static int RaterMinLevel => HeroConditions.Current.VotableLevel;
    public static int RaterMinLeadership => HeroConditions.Current.VotableLeadershipPoint;
    public static int TargetMinLevel => HeroConditions.Current.VotableLevel;

    /// <summary>
    /// Applies a rating, or explains why it was refused.
    /// </summary>
    /// <remarks>
    /// Every check here has a matching string shipped in the client (reputation_is_me,
    /// reputation_not_same_faction, reputation_not_same_team, reputation_owner_level_low,
    /// reputation_owner_leadership_low, reputation_target_level_low), which is what pins the rule set
    /// down rather than leaving it to guesswork.
    ///
    /// The daily limit is per rater/target PAIR: you may rate many people in a day, each of them once.
    /// </remarks>
    public ReputationVoteResult Vote(Character voter, Character target, int amount)
    {
        if (voter == null || target == null)
            return ReputationVoteResult.IsMe;

        if (voter.Id == target.Id)
            return ReputationVoteResult.IsMe;

        if (NationOf(voter) != NationOf(target) || NationOf(voter) == 0)
            return ReputationVoteResult.NotSameFaction;

        if (!TeamManager.Instance.AreTeamMembers(voter.Id, target.Id))
            return ReputationVoteResult.NotSameTeam;

        if (voter.Level < RaterMinLevel)
            return ReputationVoteResult.VoterLevelLow;

        if (voter.LeadershipPoint < RaterMinLeadership)
            return ReputationVoteResult.VoterLeadershipLow;

        if (target.Level < TargetMinLevel)
            return ReputationVoteResult.TargetLevelLow;

        if (HasRatedToday(voter.Id, target.Id))
            return ReputationVoteResult.AlreadyRatedToday;

        // The client only ever sends +1 or -1 (VoteReputation(1) / VoteReputation(-1)); clamping rather
        // than trusting the wire keeps a crafted packet from handing out arbitrary standing.
        var delta = Math.Sign(amount);
        if (delta == 0)
            return ReputationVoteResult.Ok;

        target.Reputation = Math.Max(0, target.Reputation + delta);
        RecordVote(voter.Id, target.Id);

        // Tell the target their standing moved. weeklyReset is false: this is an ordinary rating, not the
        // periodic wipe that flag exists to announce.
        target.SendPacket(new SCReputationChangedPacket(target.Reputation, false));

        Logger.Debug("{0} rated {1} {2:+#;-#}, reputation now {3}", voter.Name, target.Name, delta, target.Reputation);
        return ReputationVoteResult.Ok;
    }

    /// <summary>Reads the reward ladder and the evaluation interval from game data.</summary>
    public void Load()
    {
        using var connection = SQLite.CreateConnection();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT percent, leadership_point FROM reputation_rewards ORDER BY percent";
            command.Prepare();

            var rewards = new List<ReputationReward>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rewards.Add(new ReputationReward(Convert.ToDouble(reader["percent"]), Convert.ToInt32(reader["leadership_point"])));

            _rewards = [.. rewards];
        }

        using (var command = connection.CreateCommand())
        {
            // One row in shipped data. MIN keeps a multi-row table from picking an arbitrary interval.
            command.CommandText = "SELECT MIN(time) FROM reputation_resets";
            command.Prepare();

            var value = command.ExecuteScalar();
            if (value != null && value != DBNull.Value)
                _resetHours = Math.Clamp(Convert.ToInt32(value), 1, 24);
        }

        Logger.Info("Loaded {0} reputation reward bands, evaluating every {1}h", _rewards.Length, _resetHours);
    }

    /// <summary>Puts the evaluation on the clock.</summary>
    /// <remarks>
    /// Driven by reputation_resets rather than hardcoded, so the shipped 12 gives the 12AM/12PM the
    /// client's own rule text promises. The cron runs on server time; the vote limit in HasRatedToday is
    /// UTC-dated, so the two only line up when the server runs UTC. Worth revisiting together if the
    /// server ever grows a configured timezone - both belong on the same clock.
    /// </remarks>
    public void Initialize()
    {
        TaskManager.Instance.CronSchedule(new ReputationEvaluationTask(), $"0 0 */{_resetHours} * * *");
    }

    /// <summary>
    /// A Hero Qualification Evaluation: converts the reputation earned this period into leadership.
    /// </summary>
    /// <remarks>
    /// Each nation is ranked on its own, because the reward table is a share of a field rather than a
    /// fixed threshold and the nations are separate contests.
    ///
    /// Only characters who were actually rated take part. Ranking every character in the nation would
    /// pad the field with people nobody rated, pushing real contributors down the percentiles while
    /// handing the padding 1 leadership each from the bottom bands - which pays out for not playing.
    ///
    /// Ties share a band, using the average of the positions they span. Reputation is a small integer,
    /// so ties are the norm rather than the exception: on a quiet period most of the field sits on 1 or
    /// 2. Awarding the tie group its best position would hand everyone the top band whenever the whole
    /// field is level, and its worst would pay nobody; the midpoint is the only choice that degrades
    /// sensibly at both ends.
    ///
    /// Everyone's reputation is cleared afterwards, paid or not - the standing describes one period.
    /// </remarks>
    /// <returns>A short summary, for the GM command that triggers this by hand.</returns>
    public string Evaluate()
    {
        if (_rewards.Length == 0)
        {
            Logger.Warn("Reputation evaluation skipped: no reward bands loaded");
            return "No reputation reward bands are loaded; nothing to pay out.";
        }

        var rated = LoadRated();
        if (rated.Count == 0)
        {
            Logger.Info("Reputation evaluation: nobody was rated this period");
            return "Nobody was rated this period.";
        }

        var awards = new Dictionary<uint, int>();
        foreach (var nation in rated.GroupBy(r => r.Nation).Where(g => g.Key != 0))
        {
            // Descending, so position 1 is the most-rated character in the nation.
            var field = nation.OrderByDescending(r => r.Reputation).ToList();

            for (var i = 0; i < field.Count;)
            {
                var j = i;
                while (j + 1 < field.Count && field[j + 1].Reputation == field[i].Reputation)
                    j++;

                // Positions are 1-based; the tie group spans i+1 .. j+1 and shares their midpoint.
                var rank = (i + 1 + j + 1) / 2.0;
                var award = AwardFor(rank, field.Count);
                if (award > 0)
                {
                    for (var k = i; k <= j; k++)
                        awards[field[k].Id] = award;
                }

                i = j + 1;
            }
        }

        Pay(awards);
        ClearStandings(rated);

        var paid = awards.Count;
        var total = awards.Values.Sum();
        Logger.Info("Reputation evaluation: {0} rated, {1} paid, {2} leadership awarded", rated.Count, paid, total);
        return $"Evaluated {rated.Count} rated characters: {paid} paid, {total} leadership awarded, standings reset.";
    }

    /// <summary>
    /// The leadership for a position in a field of a given size, or 0 below the last band.
    /// </summary>
    /// <remarks>
    /// A band covers the first ceil(percent * count) positions rather than everyone whose rank/count
    /// falls under percent. The two agree once a field is large, and differ where it matters: on a field
    /// of one, rank/count is 1.0, so the only rated character in a nation would rank in the bottom
    /// percentile of themselves and be paid nothing. Rounding the band's size up instead keeps "the top
    /// 3%" meaning at least one person, which is both the ordinary reading and the only one that behaves
    /// on the small fields a private server actually has.
    /// </remarks>
    private int AwardFor(double rank, int count)
    {
        foreach (var band in _rewards)
        {
            if (rank <= Math.Ceiling(band.Percent * count))
                return band.LeadershipPoint;
        }

        return 0;
    }

    private sealed record RatedCharacter(uint Id, uint Nation, int Reputation);

    /// <summary>
    /// Every character carrying reputation, with their nation resolved.
    /// </summary>
    /// <remarks>
    /// Read straight from the database rather than from loaded characters: almost everyone who was rated
    /// this period is offline by the time an evaluation runs, and an evaluation that only saw whoever
    /// happened to be logged in would rank a different field every time.
    /// </remarks>
    private static List<RatedCharacter> LoadRated()
    {
        var result = new List<RatedCharacter>();

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT `id`, `faction_id`, `reputation` FROM `characters` WHERE `reputation` > 0 AND `deleted` = 0";
        command.Prepare();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var faction = FactionManager.Instance.GetFaction((FactionsEnum)reader.GetUInt32(1));
            var nation = faction == null
                ? 0u
                : (uint)(faction.MotherId != FactionsEnum.Invalid ? faction.MotherId : faction.Id);

            result.Add(new RatedCharacter(reader.GetUInt32(0), nation, reader.GetInt32(2)));
        }

        return result;
    }

    /// <summary>Awards leadership, in memory for anyone online and in the database for everyone else.</summary>
    private static void Pay(Dictionary<uint, int> awards)
    {
        if (awards.Count == 0)
            return;

        using var connection = MySQL.CreateConnection();
        foreach (var (characterId, amount) in awards)
        {
            var online = WorldManager.Instance.GetCharacterById(characterId);
            if (online != null)
            {
                online.AddLeadership(amount);
                HeroManager.PublishLeadership(online);
                online.SendMessage($"Your reputation this period earned you {amount} leadership.");
                continue;
            }

            // Mirrors AddLeadership's rule that the lifetime total follows awards. The daily counter is
            // left alone: it exists to cap what a character can earn while playing, and this payout is
            // not something they earn by being logged in.
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE `characters` SET `leadership_point` = LEAST(`leadership_point` + @amount, 2147483647), " +
                "`accumulated_leadership_point` = LEAST(`accumulated_leadership_point` + @amount, 2147483647) " +
                "WHERE `id` = @id";
            command.Parameters.AddWithValue("@amount", amount);
            command.Parameters.AddWithValue("@id", characterId);
            command.Prepare();
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Wipes the period's standings and tells anyone online that it was a reset, not a rating.</summary>
    private static void ClearStandings(List<RatedCharacter> rated)
    {
        using (var connection = MySQL.CreateConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE `characters` SET `reputation` = 0 WHERE `reputation` > 0";
            command.Prepare();
            command.ExecuteNonQuery();
        }

        foreach (var entry in rated)
        {
            var online = WorldManager.Instance.GetCharacterById(entry.Id);
            if (online == null)
                continue;

            online.Reputation = 0;
            online.SendPacket(new SCReputationChangedPacket(0, true));
        }
    }

    /// <summary>Human-readable refusal, mirroring the client's own wording.</summary>
    public static string Explain(ReputationVoteResult result) => result switch
    {
        ReputationVoteResult.IsMe => "You cannot rate yourself.",
        ReputationVoteResult.NotSameFaction => "You can only rate someone of your own nation.",
        ReputationVoteResult.NotSameTeam => "You can only rate someone in your party or raid.",
        ReputationVoteResult.VoterLevelLow => $"You must be level {RaterMinLevel} or above to rate.",
        ReputationVoteResult.VoterLeadershipLow => $"You need {RaterMinLeadership} leadership to rate.",
        ReputationVoteResult.TargetLevelLow => $"That character must be level {TargetMinLevel} or above.",
        ReputationVoteResult.AlreadyRatedToday => "You have already rated that character today.",
        _ => string.Empty
    };

    private static uint NationOf(Character character)
    {
        var faction = character.Faction;
        if (faction == null)
            return 0;

        return (uint)(faction.MotherId != FactionsEnum.Invalid ? faction.MotherId : faction.Id);
    }

    /// <remarks>
    /// Compares UTC dates rather than subtracting 24 hours, so the limit lines up with a calendar day
    /// the way the client's "once per day" wording reads, instead of drifting later each time.
    /// </remarks>
    private static bool HasRatedToday(uint voterId, uint targetId)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT `voted_at` FROM `character_reputation_votes` WHERE `voter_id` = @v AND `target_id` = @t";
            command.Parameters.AddWithValue("@v", voterId);
            command.Parameters.AddWithValue("@t", targetId);
            command.Prepare();

            using var reader = command.ExecuteReader();
            if (reader.Read())
                return reader.GetDateTime(0).Date == DateTime.UtcNow.Date;
        }
        catch (Exception ex)
        {
            // Refuse rather than allow: a failed read must not turn into unlimited ratings.
            Logger.Error(ex, "Reputation: failed to read the vote record for {0} -> {1}", voterId, targetId);
            return true;
        }

        return false;
    }

    private static void RecordVote(uint voterId, uint targetId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            "REPLACE INTO `character_reputation_votes` (`voter_id`,`target_id`,`voted_at`) VALUES (@v,@t,@d)";
        command.Parameters.AddWithValue("@v", voterId);
        command.Parameters.AddWithValue("@t", targetId);
        command.Parameters.AddWithValue("@d", DateTime.UtcNow);
        command.Prepare();
        command.ExecuteNonQuery();
    }
}
