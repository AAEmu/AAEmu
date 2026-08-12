using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Hero;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Hero;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Which phase of the hero season is running, and telling clients about it.
/// </summary>
/// <remarks>
/// A season runs four phases in order - leadership_ranking, hero_abstain, hero_voting, hero_period -
/// with their windows in hero_schedules. The client drives real behaviour off them: the rating gate
/// wants leadership_ranking live (.text 0x164f20), the ballot and the Hero Adjutant want hero_voting,
/// and the Current Heroes and Hero Missions tabs read hero_period for their "Active Period" headers.
///
/// Before this, HeroManager.Send simply declared leadership_ranking and hero_period active at all times.
/// That was enough to make rating work, and wrong for everything with a phase of its own.
///
/// The shipped windows are months long, so an override exists and is not a debug afterthought: without
/// it no part of an election can be exercised. The override wins over the schedule until it is cleared.
/// </remarks>
public class HeroElectionManager : Singleton<HeroElectionManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Set by the GM command; HeroPhase.None here means "follow the schedule".</summary>
    private HeroPhase? _override;

    /// <summary>Last phase announced, so a tick only broadcasts on a real transition.</summary>
    private HeroPhase _announced = HeroPhase.None;

    /// <summary>
    /// The season the last announcement belonged to.
    /// </summary>
    /// <remarks>
    /// Tracked alongside the phase because the phase alone cannot see a season change. Two seasons that
    /// tiled - the last one's hero_period running straight into the next one's leadership_ranking - would
    /// change phase and be caught, but a schedule where the same phase number spans the boundary would
    /// not, and the new season's entry work (the leadership roll, a fresh candidate field) would never
    /// run. The shipped data has gaps so this cannot happen today; it is one comparison to not depend on
    /// that.
    /// </remarks>
    private uint _announcedSeason;

    /// <summary>The season the phases belong to.</summary>
    public static uint Season => HeroSeason.CurrentId;

    /// <summary>Whether the phase is being forced rather than read from hero_schedules.</summary>
    public bool IsOverridden => _override.HasValue;

    /// <summary>The phase the server is currently running.</summary>
    public HeroPhase CurrentPhase =>
        _override ?? HeroSchedule.PhaseAt(Season, DateTime.UtcNow);

    /// <summary>The phase hero_schedules alone would put us in, ignoring any override.</summary>
    public static HeroPhase ScheduledPhase => HeroSchedule.PhaseAt(Season, DateTime.UtcNow);

    public void Initialize()
    {
        _announced = CurrentPhase;
        _announcedSeason = Season;
        Logger.Info("Hero season {0} is in phase {1}", _announcedSeason, _announced);

        // A minute is fine: the phases are days long, and the only thing that turns over faster is the
        // override, which announces itself immediately rather than waiting for a tick.
        TaskManager.Instance.Schedule(new HeroPhaseTickTask(), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    /// <summary>Re-reads the schedule and announces a change, if the season or phase moved on its own.</summary>
    public void Tick()
    {
        var phase = CurrentPhase;
        var season = Season;
        if (phase == _announced && season == _announcedSeason)
            return;

        var leaving = _announced;
        if (season != _announcedSeason)
            Logger.Info("Hero season {0} -> {1}", _announcedSeason, season);
        Logger.Info("Hero phase {0} -> {1}", leaving, phase);

        _announced = phase;
        _announcedSeason = season;
        OnPhaseEntered(phase);
        BroadcastAll(leaving);
    }

    /// <summary>
    /// Forces a phase, or clears the force with null so the schedule takes over again.
    /// </summary>
    public void SetOverride(HeroPhase? phase)
    {
        _override = phase;
        var leaving = _announced;
        var entered = CurrentPhase;
        _announced = entered;
        _announcedSeason = Season;

        Logger.Info("Hero phase override {0}; now in {1}",
            phase.HasValue ? phase.Value.ToString() : "cleared", entered);

        OnPhaseEntered(entered);
        BroadcastAll(leaving);
    }

    /// <summary>
    /// Work a phase does once, on the way in.
    /// </summary>
    /// <remarks>
    /// The snapshot is taken when hero_abstain opens, because withdrawals edit it and they cannot edit a
    /// list that does not exist yet. It was briefly folded into hero_voting, to avoid stepping through a
    /// phase that did nothing - that was only true while withdrawals were unimplemented, and while a
    /// duplicate announcement made the extra step annoying. Both reasons are gone.
    ///
    /// Entering hero_abstain always retakes it, so a fresh election starts a fresh field; entering
    /// hero_voting takes one only if nothing is on file, which keeps a straight jump to the ballot
    /// working without discarding withdrawals that have already been applied.
    /// </remarks>
    private void OnPhaseEntered(HeroPhase phase)
    {
        switch (phase)
        {
            // A new collection period starts here, so this is where last period's figures are retired.
            case HeroPhase.LeadershipRanking:
                RollPeriod(force: false);
                break;

            // The field is frozen when withdrawals open, which is what the "candidates announced in 10
            // minutes" message is telling players.
            case HeroPhase.HeroAbstain:
                FreezeCandidates();
                break;

            // Only if nothing is on file. Jumping straight to the ballot still produces something to
            // vote on, without discarding a list that withdrawals have already been applied to.
            case HeroPhase.HeroVoting when CountCandidates() == 0:
                Logger.Info("Hero election: entering the ballot with no candidates on file; freezing now");
                FreezeCandidates();
                break;

            // Entering the serving period is the end of the ballot, so it is where the count belongs.
            case HeroPhase.HeroPeriod:
                Logger.Info("Hero election: {0}", CountVotes().Replace('\n', ';'));
                break;
        }
    }

    /// <summary>
    /// The schedule state for one client, as SCHeroEventState wants it.
    /// </summary>
    /// <remarks>
    /// State is not just a flag the client stores - it decides whether the client ANNOUNCES. The apply
    /// loop at .text 0x10a3fa files each entry into heroManager + 12*event + 0x70 and then branches on
    /// the state: 2 means "just ended" and runs the announcements for that event (0x108700 - event 1
    /// raises HERO_SEASON_OFF, event 3 clears the voted flag and raises its own pair), while 1 is
    /// stored silently.
    ///
    /// So listing every inactive phase as 2 on every send, which is what this used to do, re-announced
    /// the end of leadership_ranking on every broadcast - the duplicate "Finished collecting Leadership
    /// information" on entering the ballot, and again on entering the serving period.
    ///
    /// There are three states, not two, and the third is what announces a beginning:
    ///
    ///   0  just started - 0x10a190, which announces per event: 1 raises 0x2bb, 3 raises
    ///      HERO_ELECTION_DAY_ALERT, and 4 installs the callback at 0x10d9a0 that fires
    ///      HERO_ELECTION_RESULT, or HERO_NOTI for a player who is in the new hero set
    ///   1  running - stored silently, which is what a client re-syncing wants
    ///   2  just ended - 0x108700, which raises HERO_SEASON_OFF for event 1 and clears the voted flag
    ///      for event 3
    ///
    /// So a transition sends the phase being left as 2 and the phase being entered as 0, and a client
    /// with no state yet gets the live phase as 1 - present, but with nothing announced at a login.
    /// Sending the entering phase as 1, which this used to do, is why the count seated heroes in silence.
    ///
    /// leadership_ranking and hero_period are sent as 1 even when they are not the current phase, and the
    /// two election phases are not. That split is forced by the client having no "present but idle"
    /// state: the lookup at 0x108010 answers only when the slot is filled AND its state is not 2
    /// (0x108046 is a literal cmp against 2), so a phase is either live or invisible.
    ///
    /// Two things need an answer for a phase that is not current, and both belong to the term rather
    /// than to an election:
    ///
    ///   the Mission Status tab, whose GetFactionScores binding (0x19db10) returns nil unless BOTH
    ///   hero_period and leadership_ranking resolve - checked at 0x19db7a and 0x19db95, before it reads
    ///   anything - which left the tab empty with a Lua error on the nil
    ///
    ///   its "Active Period" header, which reads the hero_period window through GetActivedHeroPeriod and
    ///   renders blank without it
    ///
    /// hero_abstain and hero_voting get the opposite treatment, because for them "live" is a claim the
    /// player can act on. Sending them outside their windows put the vote icon on screen and the "Vote
    /// Available" badge on the character sheet while the server was in no election phase at all, so the
    /// Voting Machine refused every click - the client offering a ballot the server would not honour.
    ///
    /// Sending only the live phase, which came before this, was wrong the other way: nothing showed it
    /// until a tab needed two phases at once, and it could not survive a transition either, since
    /// leadership_ranking goes out as 2 when it ends and 2 is the state the lookup refuses.
    ///
    /// One consequence worth stating: the peer-rating gate (0x164f20) also just needs the
    /// leadership_ranking slot to resolve, so the rating button appears outside that phase. The server
    /// has never gated rating on the phase, so this makes the client agree with what the server was
    /// already accepting rather than the reverse.
    /// </remarks>
    public IReadOnlyList<HeroEventStateEntry> BuildStates(HeroPhase? leaving = null)
    {
        var season = (int)Season;
        var current = CurrentPhase;
        var entries = new List<HeroEventStateEntry>(4);

        foreach (var phase in Enum.GetValues<HeroPhase>())
        {
            if (phase == HeroPhase.None)
                continue;

            // The phase just left announces its end, and the one just entered announces its start.
            if (leaving.HasValue && phase == leaving.Value && phase != current)
            {
                entries.Add(new HeroEventStateEntry((byte)phase, season, 2));
                continue;
            }

            if (phase == current)
            {
                entries.Add(new HeroEventStateEntry((byte)phase, season, (byte)(leaving.HasValue ? 0 : 1)));
                continue;
            }

            // Not running. Only the two term-long phases are still advertised; an election phase that is
            // not open must not look open.
            if (phase is HeroPhase.LeadershipRanking or HeroPhase.HeroPeriod)
                entries.Add(new HeroEventStateEntry((byte)phase, season, 1));
        }

        return entries;
    }

    /// <summary>
    /// Pushes the current schedule state, and the hero roster it gates, to everyone online.
    /// </summary>
    /// <remarks>
    /// Without clearAll: this is an update to a client that already has the schedule, so the entries
    /// overwrite in place. Resetting first would make every phase look newly entered, and the client
    /// announces phase changes - moving from hero_abstain to hero_voting replayed the end of
    /// leadership_ranking and showed its notification a second time.
    /// </remarks>
    private static void BroadcastAll(HeroPhase leaving)
    {
        foreach (var player in WorldManager.Instance.GetAllCharacters())
            HeroManager.Instance.Send(player, clearAll: false, leaving: leaving);
    }

    /// <summary>
    /// The candidates standing in a nation's election, as frozen when the ranking period closed.
    /// </summary>
    /// <remarks>
    /// Read back from hero_election_candidates, never recomputed. Leadership keeps accruing while the
    /// ballot is open, so a list derived per request would reorder itself between one player opening the
    /// window and the next, and a candidate could drop off the bottom after votes had been cast for
    /// them. The stored leadership figures are the snapshot's, not the character's current ones: the
    /// ballot shows what a candidate stood on.
    ///
    /// The guild is the exception, and is resolved fresh here. It is identity rather than qualification
    /// - the same category as the name, which the client itself resolves live through its name cache -
    /// so a candidate who changes expedition mid-election should be shown under the guild they are
    /// actually in. Freezing it would just publish a stale one.
    /// </remarks>
    public IReadOnlyList<HeroCandidateEntry> GetCandidates(uint nationId)
    {
        var candidates = new List<HeroCandidateEntry>();

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT c.`character_id`, c.`ranking`, c.`score`, c.`accum_point`, c.`reputation`, " +
                "       (SELECT COUNT(*) FROM `hero_election_votes` v " +
                "         WHERE v.`season` = c.`season` AND v.`candidate_id` = c.`character_id`) AS votes " +
                "FROM `hero_election_candidates` c " +
                "WHERE c.`season` = @season AND c.`faction_id` = @faction AND c.`abstained` = 0 " +
                "ORDER BY c.`ranking` ASC";
            command.Parameters.AddWithValue("@season", Season);
            command.Parameters.AddWithValue("@faction", nationId);
            command.Prepare();

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                candidates.Add(new HeroCandidateEntry(
                    Unk0: 0,
                    CharId: reader.GetUInt32(0),
                    FactionId: (int)nationId,
                    ExpeditionId: 0,
                    Ranking: reader.GetInt32(1),
                    Score: reader.GetInt32(2),
                    AccumPoint: reader.GetInt32(3),
                    VoteCount: reader.GetInt32(5),
                    Reputation: reader.GetInt32(4)));
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroElection: failed to read the candidate list for nation {0}", nationId);
            return candidates;
        }

        return ApplyCurrentExpeditions(candidates);
    }

    /// <summary>
    /// Fills in each candidate's guild as it stands right now.
    /// </summary>
    /// <remarks>
    /// One query for the whole ballot rather than one per row, with loaded characters overlaid on top so
    /// a guild joined this session shows without waiting for a save - the same treatment the leadership
    /// ladder gives its live figures.
    /// </remarks>
    private static List<HeroCandidateEntry> ApplyCurrentExpeditions(List<HeroCandidateEntry> candidates)
    {
        if (candidates.Count == 0)
            return candidates;

        var expeditions = new Dictionary<uint, int>();

        try
        {
            var ids = string.Join(",", candidates.Select(c => (uint)c.CharId));
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT `id`, `expedition_id` FROM `characters` WHERE `id` IN ({ids})";
            command.Prepare();

            using var reader = command.ExecuteReader();
            while (reader.Read())
                expeditions[reader.GetUInt32(0)] = reader.GetInt32(1);
        }
        catch (Exception ex)
        {
            // A missing guild column costs a blank line on the ballot, not the ballot.
            Logger.Error(ex, "HeroElection: failed to resolve candidate guilds");
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var id = (uint)candidates[i].CharId;
            var online = WorldManager.Instance.GetCharacterById(id);
            var expedition = online != null
                ? (int)(online.Expedition?.Id ?? 0)
                : expeditions.GetValueOrDefault(id);

            candidates[i] = candidates[i] with { ExpeditionId = expedition };
        }

        return candidates;
    }

    /// <summary>
    /// Takes the snapshot: freezes the top of each nation's ladder as that season's candidates.
    /// </summary>
    /// <remarks>
    /// The cut is hero_conditions.hero_candidate_scope - the same 16 the Candidates tab colours brown -
    /// so the ballot and the ladder cannot disagree about who is standing.
    ///
    /// Replaces any existing snapshot for the season. Retaking it is the right behaviour for a phase
    /// that is entered again, and it is what makes a test loop possible: earn leadership under
    /// leadership_ranking, enter hero_abstain to re-freeze, then vote.
    ///
    /// Vote counts are reset with the snapshot. A refreeze is a new election, and carrying votes cast
    /// against a previous field would attribute them to whoever now holds that ranking.
    /// </remarks>
    public int FreezeCandidates()
    {
        var scope = HeroConditions.Current.HeroCandidateScope;
        if (scope <= 0)
            return 0;

        var season = Season;
        var frozen = 0;

        try
        {
            using var connection = MySQL.CreateConnection();

            using (var clear = connection.CreateCommand())
            {
                clear.CommandText = "DELETE FROM `hero_election_candidates` WHERE `season` = @season";
                clear.Parameters.AddWithValue("@season", season);
                clear.Prepare();
                clear.ExecuteNonQuery();
            }

            foreach (var nation in Nations())
            {
                var ranking = HeroManager.Instance.GetRanking(nation);
                for (var i = 0; i < ranking.Count && i < scope; i++)
                {
                    var row = ranking[i];
                    using var insert = connection.CreateCommand();
                    // No guild here on purpose - it is resolved when the ballot is sent, so that changing
                    // expedition mid-election shows the current one. See GetCandidates.
                    insert.CommandText =
                        "INSERT INTO `hero_election_candidates` " +
                        "(`season`,`faction_id`,`character_id`,`ranking`,`score`,`accum_point`," +
                        " `reputation`,`abstained`,`vote_count`,`frozen_at`) " +
                        "VALUES (@season,@faction,@char,@rank,@score,@accum,@rep,0,0,@now)";
                    insert.Parameters.AddWithValue("@season", season);
                    insert.Parameters.AddWithValue("@faction", nation);
                    insert.Parameters.AddWithValue("@char", (uint)row.CharId);
                    insert.Parameters.AddWithValue("@rank", i + 1);
                    insert.Parameters.AddWithValue("@score", row.Score);
                    insert.Parameters.AddWithValue("@accum", row.Leadership);
                    insert.Parameters.AddWithValue("@rep", ReputationOf((uint)row.CharId));
                    insert.Parameters.AddWithValue("@now", DateTime.UtcNow);
                    insert.Prepare();
                    insert.ExecuteNonQuery();
                    frozen++;
                }
            }

            if (frozen == 0)
            {
                // Almost always the leadership roll rather than a fault. Entering leadership_ranking
                // moves everyone's current-period leadership into the historical column and clears it,
                // which is what starts a fresh ladder - and the ranking only counts characters holding
                // MORE than zero. In a real season the window is a month long and players earn it back
                // before the ballot; stepping through the phases in seconds skips that entirely, so the
                // freeze finds nobody. Say so, because "froze 0" on its own reads like a broken query.
                Logger.Warn(
                    "Hero election: froze 0 candidates for season {0} - no character in any nation holds " +
                    "current-period leadership. If the roll for this season has just run, leadership has " +
                    "been reset and has to be earned (or granted) again before there is a ballot.", season);
            }
            else
            {
                Logger.Info("Hero election: froze {0} candidates for season {1}", frozen, season);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroElection: failed to freeze the candidate list for season {0}", season);
        }

        return frozen;
    }

    /// <summary>
    /// How many heroes a nation elects, and so how many candidates one ballot may back.
    /// </summary>
    /// <remarks>
    /// From hero_rewards, which has one row per placing per nation - 6 for Nuia and Haranya, 3 for the
    /// Outlaws and for the independent-nation template. This was hardcoded as "6 for the two alliances,
    /// 3 otherwise", which the data agrees with, but only by luck: nation 166 exists and would have been
    /// right by accident, and a nation added later would not be.
    /// </remarks>
    public static int SeatsFor(uint nationId) => HeroRewards.SeatsFor(nationId);

    /// <summary>Why a ballot was refused.</summary>
    public enum VoteResult
    {
        Ok,
        NotVotingPhase,
        NoNation,
        AlreadyVoted,
        NothingSelected,
        TooManySelected,
        NotACandidate,
        ConditionsNotMet
    }

    /// <summary>
    /// Records one ballot.
    /// </summary>
    /// <remarks>
    /// Every pick is checked against the frozen candidate list for the voter's own nation, so a crafted
    /// packet cannot vote for someone in another nation, someone who withdrew, or an id that was never
    /// standing. The client will not offer any of those, which is exactly why the server has to.
    ///
    /// The whole ballot is accepted or rejected together: a partial one would let a client that sent six
    /// picks including a bad one quietly cast five.
    /// </remarks>
    public VoteResult Vote(Character voter, IReadOnlyCollection<ulong> candidateIds)
    {
        if (CurrentPhase != HeroPhase.HeroVoting)
            return VoteResult.NotVotingPhase;

        var nation = HeroManager.NationOf(voter);
        if (nation == 0)
            return VoteResult.NoNation;

        // LeadershipPeriodPoint, not the current total, because that is the figure the client checks.
        // X2Hero:IsVoter (.text 0x19c990 -> 0x108d80) reads ClientPlayer+0xef0 at 0x108e0c and compares
        // it against hero_conditions - and +0xef0 is periodLeadershipPoint, the COMPLETED period's
        // figure. Gating on the current total instead made the two disagree in both directions: a
        // character with leadership this period but none last was offered a ballot the server refused,
        // and one with last period's leadership but none this had the checkboxes hidden while the server
        // would have accepted the vote.
        //
        // It also means eligibility is earned in the period before it is spent, which is retail's rule
        // rather than an accident of this reading - the "vote available" badge sits on the character
        // sheet's Last Season Leadership row, not on the current one.
        var condition = HeroConditions.Current;
        if (voter.Level < condition.VotableLevel ||
            voter.LeadershipPeriodPoint < condition.VotableLeadershipPoint)
            return VoteResult.ConditionsNotMet;

        if (candidateIds.Count == 0)
            return VoteResult.NothingSelected;

        if (candidateIds.Count > SeatsFor(nation))
            return VoteResult.TooManySelected;

        var standing = GetCandidates(nation).Select(c => c.CharId).ToHashSet();
        if (candidateIds.Any(id => !standing.Contains(id)))
            return VoteResult.NotACandidate;

        if (HasVoted(voter.Id))
            return VoteResult.AlreadyVoted;

        try
        {
            using var connection = MySQL.CreateConnection();
            foreach (var candidateId in candidateIds)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText =
                    "INSERT INTO `hero_election_votes` (`season`,`voter_id`,`candidate_id`,`voted_at`) " +
                    "VALUES (@season,@voter,@candidate,@now)";
                insert.Parameters.AddWithValue("@season", Season);
                insert.Parameters.AddWithValue("@voter", voter.Id);
                insert.Parameters.AddWithValue("@candidate", (uint)candidateId);
                insert.Parameters.AddWithValue("@now", DateTime.UtcNow);
                insert.Prepare();
                insert.ExecuteNonQuery();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroElection: failed to record a ballot from {0}", voter.Name);
            return VoteResult.ConditionsNotMet;
        }

        Logger.Info("Hero election: {0} voted for {1} candidate(s)", voter.Name, candidateIds.Count);
        return VoteResult.Ok;
    }

    /// <summary>
    /// Counts the ballots and seats the winners.
    /// </summary>
    /// <remarks>
    /// Ordering is votes, then leadership, then a coin toss - the leadership figure being the frozen
    /// score the ladder ranked them on, not what they hold now, so the tiebreak matches the standings
    /// the ballot showed.
    ///
    /// A candidate with no votes cannot win. If nobody in a nation voted at all, that nation elects
    /// nobody and is left without heroes rather than falling back to seating the leadership leaders -
    /// an election with no ballots has no result, and inventing one would make voting optional.
    ///
    /// The outgoing roster is cleared first, so a hero who was not re-elected actually leaves.
    /// </remarks>
    public string CountVotes()
    {
        if (CountCandidates() == 0)
            return "No candidates are on file; nothing to count.";

        var lines = new List<string>();

        // Only nations that actually stood a ballot. Iterating every faction meant a ranking query and a
        // log line for each of the sixty-odd NPC and system factions, which buried the one line that
        // mattered.
        foreach (var nation in NationsWithCandidates())
        {
            var winners = RankBallots(nation);
            if (winners.Count == 0)
            {
                // Said out loud: a nation with no votes is skipped entirely - not cleared, not seated -
                // so any hero already serving stays, and the whole count can look like it did nothing.
                Logger.Info("Hero election: nation {0} had candidates but no votes; its heroes are unchanged", nation);
                continue;
            }

            var seats = Math.Min(SeatsFor(nation), winners.Count);
            HeroManager.Instance.ClearNation(nation);

            var seated = 0;
            for (var i = 0; i < seats; i++)
            {
                // A placing hero_rewards does not cover is a placing the nation does not have, so it is
                // left empty rather than seated at an invented grade.
                var grade = HeroRewards.GradeFor(nation, i + 1);
                if (grade == 0)
                {
                    Logger.Warn("Hero election: nation {0} has no hero_rewards row for rank {1}; not seated",
                        nation, i + 1);
                    continue;
                }

                HeroManager.Instance.Seat(winners[i].CharacterId, nation, grade);
                seated++;

                // The regalia - the cloak for their nation and grade, plus the office's consumables.
                // By mail, because a winner is usually offline when the count runs.
                Models.Game.Mails.MailForHeroElection.Send(winners[i].CharacterId, nation, i + 1);
            }

            if (seated == 0)
                continue;

            HeroManager.Instance.BroadcastNation(nation);

            var summary = string.Join(", ", winners.Take(seated).Select(w => $"{w.CharacterId}({w.Votes})"));
            Logger.Info("Hero election: nation {0} elected {1} hero(es): {2}", nation, seated, summary);
            lines.Add($"Nation {nation}: {seated} elected - {summary}");
        }

        if (lines.Count == 0)
            return "Nobody voted in any nation; no heroes were elected and the serving ones are unchanged.";

        return string.Join("\n", lines);
    }

    /// <summary>The nations that have a frozen candidate list this season.</summary>
    private IEnumerable<uint> NationsWithCandidates()
    {
        var nations = new List<uint>();

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT DISTINCT `faction_id` FROM `hero_election_candidates` WHERE `season` = @season";
            command.Parameters.AddWithValue("@season", Season);
            command.Prepare();

            using var reader = command.ExecuteReader();
            while (reader.Read())
                nations.Add(reader.GetUInt32(0));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroElection: failed to list the nations with candidates");
        }

        return nations;
    }

    private sealed record Ballot(uint CharacterId, int Votes, int Score);

    /// <summary>A nation's candidates that received at least one vote, best first.</summary>
    private List<Ballot> RankBallots(uint nationId)
    {
        var rows = new List<Ballot>();

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            // Counted from hero_election_votes, not from the vote_count column. That column was a
            // denormalised mirror kept in step by an UPDATE beside the ballot insert, and it silently
            // failed to increment at least once - a ballot was on file with the candidate still showing
            // zero, so the count found nobody and seated nobody. A cached tally that can disagree with
            // the ballots has no business deciding an election; the ballots are the record.
            command.CommandText =
                "SELECT c.`character_id`, COUNT(v.`voter_id`) AS votes, c.`score` " +
                "FROM `hero_election_candidates` c " +
                "JOIN `hero_election_votes` v " +
                "  ON v.`season` = c.`season` AND v.`candidate_id` = c.`character_id` " +
                "WHERE c.`season` = @season AND c.`faction_id` = @faction AND c.`abstained` = 0 " +
                "GROUP BY c.`character_id`, c.`score` " +
                "HAVING votes > 0";
            command.Parameters.AddWithValue("@season", Season);
            command.Parameters.AddWithValue("@faction", nationId);
            command.Prepare();

            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(new Ballot(reader.GetUInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroElection: failed to read ballots for nation {0}", nationId);
            return rows;
        }

        // Votes, then the leadership they stood on, then chance. The random key is drawn once per
        // candidate rather than compared pairwise, so the order it produces is a consistent one.
        return [.. rows
            .OrderByDescending(r => r.Votes)
            .ThenByDescending(r => r.Score)
            .ThenBy(_ => Random.Shared.Next())];
    }

    /// <summary>
    /// Retires the finished period's leadership and starts a fresh ladder.
    /// </summary>
    /// <remarks>
    /// Current-period leadership becomes the historical figure and the current one clears; the lifetime
    /// total is untouched, which is the whole reason it is kept separately. The character sheet's two
    /// rows then read as "nothing yet this period" over an unchanged lifetime, which is what a new
    /// season should look like.
    ///
    /// Done when leadership_ranking OPENS rather than when the count seats the heroes. The count is the
    /// end of the old cycle, but the figures stay meaningful through hero_period - the ladder that
    /// elected the serving heroes is still what players want to see while they serve. Clearing at the
    /// count would blank every leaderboard for the whole of the serving period.
    ///
    /// Guarded to once per season. "Entered leadership_ranking" is not "a new season began": stepping
    /// the phases with /herophase, or restarting the server inside the window, would otherwise wipe the
    /// ladder each time. force skips the guard, for a GM who means it.
    /// </remarks>
    public int RollPeriod(bool force)
    {
        var season = Season;

        try
        {
            using var connection = MySQL.CreateConnection();

            if (!force)
            {
                using var check = connection.CreateCommand();
                check.CommandText = "SELECT 1 FROM `hero_season_rolls` WHERE `season` = @s LIMIT 1";
                check.Parameters.AddWithValue("@s", season);
                check.Prepare();
                if (check.ExecuteScalar() != null)
                    return -1;
            }

            int touched;
            using (var roll = connection.CreateCommand())
            {
                roll.CommandText =
                    "UPDATE `characters` SET `leadership_period_point` = `leadership_point`, `leadership_point` = 0 " +
                    "WHERE `deleted` = 0 AND (`leadership_point` <> 0 OR `leadership_period_point` <> 0)";
                roll.Prepare();
                touched = roll.ExecuteNonQuery();
            }

            using (var mark = connection.CreateCommand())
            {
                mark.CommandText =
                    "REPLACE INTO `hero_season_rolls` (`season`,`rolled_at`,`characters_rolled`) VALUES (@s,@n,@c)";
                mark.Parameters.AddWithValue("@s", season);
                mark.Parameters.AddWithValue("@n", DateTime.UtcNow);
                mark.Parameters.AddWithValue("@c", touched);
                mark.Prepare();
                mark.ExecuteNonQuery();
            }

            // Loaded characters hold the authoritative figures and would write their old ones back on the
            // next save, undoing the roll for everyone who happened to be online when it ran.
            foreach (var player in WorldManager.Instance.GetAllCharacters())
            {
                player.SetLastSeasonLeadership(player.LeadershipPoint);
                player.SetLeadership(0);
                HeroManager.PublishLeadership(player);
            }

            Logger.Info("Hero season {0}: rolled the leadership period, {1} character(s) affected", season, touched);
            return touched;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroElection: failed to roll the leadership period for season {0}", season);
            return -2;
        }
    }

    /// <summary>Why a withdrawal was refused.</summary>
    public enum AbstainResult
    {
        Ok,
        NotAbstainPhase,
        NotACandidate,
        AlreadyAbstained,
        WouldLeaveTooFew
    }

    /// <summary>
    /// A candidate declining to stand.
    /// </summary>
    /// <remarks>
    /// Only during hero_abstain - the phase exists for exactly this, and GetAbstainPeriod is bound
    /// client-side to show its window.
    ///
    /// Refused when it would leave the nation with no more candidates than seats. The client already
    /// disables its own button on that rule (election.lua:258 greys it while activatedCount is at or
    /// below GetFactionHeroCount, with the hero_candidate_abstain_rejected tooltip), so the server is
    /// enforcing the same thing rather than inventing one: an election cannot be left unable to fill
    /// its seats.
    ///
    /// A withdrawn candidate is dropped from the ballot entirely. Retail keeps the row and blanks the
    /// name - GetCandidateList compares the row charId against the invalid-id sentinel at .text 0x19e765
    /// and pushes the literal "abstainer_player", which common.lua:26 turns into the greyed isAbstention
    /// row - so the rank stays visible and the person does not. Matching that needs the sentinel's
    /// value, which is a runtime-initialised global and not readable from the file, so for now they
    /// simply do not appear.
    /// </remarks>
    public AbstainResult Abstain(Character character)
    {
        if (CurrentPhase != HeroPhase.HeroAbstain)
            return AbstainResult.NotAbstainPhase;

        var nation = HeroManager.NationOf(character);
        var standing = GetCandidates(nation);

        if (standing.All(c => c.CharId != character.Id))
        {
            // Either never a candidate, or already withdrawn - GetCandidates excludes the withdrawn.
            return HasAbstained(character.Id) ? AbstainResult.AlreadyAbstained : AbstainResult.NotACandidate;
        }

        if (standing.Count <= SeatsFor(nation))
            return AbstainResult.WouldLeaveTooFew;

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE `hero_election_candidates` SET `abstained` = 1 " +
                "WHERE `season` = @season AND `character_id` = @char";
            command.Parameters.AddWithValue("@season", Season);
            command.Parameters.AddWithValue("@char", character.Id);
            command.Prepare();
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroElection: failed to record a withdrawal from {0}", character.Name);
            return AbstainResult.NotACandidate;
        }

        Logger.Info("Hero election: {0} withdrew from nation {1}'s ballot", character.Name, nation);
        return AbstainResult.Ok;
    }

    /// <summary>
    /// Withdraws a candidate by name, ignoring the rules a player's own withdrawal has to obey.
    /// </summary>
    /// <remarks>
    /// Exists because the honest path is hard to reach on a test server: a withdrawal is refused unless
    /// more candidates remain than there are seats, and a nation seats six, so exercising it properly
    /// needs seven characters holding leadership. This skips the seat-count guard and the phase check.
    ///
    /// Resolved by name against the frozen list, so it works for a candidate who is offline - which most
    /// of a real ballot will be.
    /// </remarks>
    public string ForceAbstain(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name a candidate to withdraw.";

        uint characterId;
        var online = WorldManager.Instance.GetCharacter(name);
        if (online != null)
        {
            characterId = online.Id;
            name = online.Name;
        }
        else
        {
            try
            {
                using var connection = MySQL.CreateConnection();
                using var lookup = connection.CreateCommand();
                lookup.CommandText = "SELECT `id`, `name` FROM `characters` WHERE `name` = @n AND `deleted` = 0 LIMIT 1";
                lookup.Parameters.AddWithValue("@n", name);
                lookup.Prepare();

                using var reader = lookup.ExecuteReader();
                if (!reader.Read())
                    return $"No character named '{name}'.";

                characterId = reader.GetUInt32(0);
                name = reader.GetString(1);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "HeroElection: failed to look up {0} for a forced withdrawal", name);
                return "Lookup failed; see the server log.";
            }
        }

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "UPDATE `hero_election_candidates` SET `abstained` = 1 " +
                "WHERE `season` = @season AND `character_id` = @char";
            command.Parameters.AddWithValue("@season", Season);
            command.Parameters.AddWithValue("@char", characterId);
            command.Prepare();

            if (command.ExecuteNonQuery() == 0)
                return $"{name} is not standing in this season's election.";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroElection: failed to force a withdrawal for {0}", name);
            return "The withdrawal failed; see the server log.";
        }

        // The ballot they are looking at just lost a row.
        if (online != null)
            SendBallot(online, openWindow: false);

        Logger.Info("Hero election: {0} was withdrawn by GM command", name);
        return $"{name} has been withdrawn from this season's ballot.";
    }

    /// <summary>Whether a character has withdrawn from this season's ballot.</summary>
    public bool HasAbstained(uint characterId)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT `abstained` FROM `hero_election_candidates` WHERE `season` = @s AND `character_id` = @c";
            command.Parameters.AddWithValue("@s", Season);
            command.Parameters.AddWithValue("@c", characterId);
            command.Prepare();
            var value = command.ExecuteScalar();
            return value != null && value != DBNull.Value && Convert.ToInt32(value) != 0;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroElection: failed to check whether {0} has withdrawn", characterId);
            return false;
        }
    }

    /// <summary>Human-readable refusal for a withdrawal.</summary>
    public static string Explain(AbstainResult result) => result switch
    {
        AbstainResult.NotAbstainPhase => "Candidates can only withdraw during the withdrawal period.",
        AbstainResult.NotACandidate => "You are not standing in this election.",
        AbstainResult.AlreadyAbstained => "You have already withdrawn.",
        AbstainResult.WouldLeaveTooFew => "Too few candidates remain for anyone else to withdraw.",
        _ => string.Empty
    };

    /// <summary>Whether a character has already submitted a ballot this season.</summary>
    public bool HasVoted(uint characterId)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT 1 FROM `hero_election_votes` WHERE `season` = @season AND `voter_id` = @voter LIMIT 1";
            command.Parameters.AddWithValue("@season", Season);
            command.Parameters.AddWithValue("@voter", characterId);
            command.Prepare();
            return command.ExecuteScalar() != null;
        }
        catch (Exception ex)
        {
            // Refuse rather than allow: a failed read must not turn into unlimited ballots.
            Logger.Error(ex, "HeroElection: failed to check whether {0} has voted", characterId);
            return true;
        }
    }

    /// <summary>Human-readable refusal.</summary>
    public static string Explain(VoteResult result) => result switch
    {
        VoteResult.NotVotingPhase => "The hero election is not open.",
        VoteResult.NoNation => "You have no nation to vote in.",
        VoteResult.AlreadyVoted => "You have already voted in this election.",
        VoteResult.NothingSelected => "Select at least one candidate.",
        VoteResult.TooManySelected => "You selected more candidates than your nation elects.",
        VoteResult.NotACandidate => "One of those characters is not standing in your nation's election.",
        VoteResult.ConditionsNotMet => "You do not meet the conditions to vote.",
        _ => string.Empty
    };

    /// <summary>How many candidates are on file for a season, across every nation.</summary>
    public int CountCandidates()
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM `hero_election_candidates` WHERE `season` = @season";
            command.Parameters.AddWithValue("@season", Season);
            command.Prepare();
            return Convert.ToInt32(command.ExecuteScalar());
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroElection: failed to count candidates");
            return 0;
        }
    }

    /// <summary>
    /// The nations an election runs in: the top-level factions.
    /// </summary>
    /// <remarks>
    /// Every faction resolves to a nation the same way the rest of the hero code does - MotherId when
    /// set, otherwise Id - so the nations are exactly the factions that are their own mother. Freezing
    /// against one with an empty ladder costs a query and stores nothing.
    /// </remarks>
    private static IEnumerable<uint> Nations() =>
        FactionManager.Instance.GetSystemFactions()
            .Where(f => f.MotherId == FactionsEnum.Invalid)
            .Select(f => (uint)f.Id)
            .Distinct();

    /// <summary>
    /// Sends the ballot to one client, opening the window with it.
    /// </summary>
    /// <remarks>
    /// The voted flag goes FIRST. X2Hero:IsAlreadyVoted reads the byte SCHeroVoting stores at
    /// heroManager + 0x11c, and election.lua reads it as the window builds - to grey the Vote button, to
    /// drop the checkboxes and to print "you have already voted" - so it has to be in the client before
    /// the window appears, not after.
    ///
    /// It is only sent here, and only when the player has actually voted. Sending it from the schedule
    /// broadcast opened the ballot for everyone online the moment hero_voting began: SCHeroVoting opens
    /// the window on its own, not only when bit 2 is set as the handler at .text 0x1085b2 implied. That
    /// makes it safe on a path where the ballot is being opened anyway, and unsafe anywhere else.
    ///
    /// Only one of the two may ask for the window. election.lua:303 TOGGLES when it is raised with no
    /// argument - "show = heroElectionWnd == nil and true or not heroElectionWnd:IsVisible()" - so a
    /// voter who had already voted got SCHeroVoting opening the ballot and showUI immediately shutting
    /// it again, which looked like a window flashing shut in milliseconds. When SCHeroVoting has already
    /// done the opening, the candidate list follows with showUI clear and only fills it.
    /// </remarks>
    public void SendBallot(Character character, bool openWindow)
    {
        var nation = HeroManager.NationOf(character);
        var voted = HasVoted(character.Id);

        if (voted)
            character.SendPacket(new SCHeroVotingPacket((int)Season, 1));

        character.SendPacket(new SCHeroCandidateListPacket(
            openWindow && !voted, (int)nation, (int)Season, GetCandidates(nation)));
    }

    /// <summary>Reputation for a candidate, live when they are online and from the database when not.</summary>
    private static int ReputationOf(uint characterId)
    {
        var online = WorldManager.Instance.GetCharacterById(characterId);
        if (online != null)
            return online.Reputation;

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT `reputation` FROM `characters` WHERE `id` = @id";
            command.Parameters.AddWithValue("@id", characterId);
            command.Prepare();

            var value = command.ExecuteScalar();
            if (value != null && value != DBNull.Value)
                return Convert.ToInt32(value);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "HeroElection: failed to read reputation for character {0}", characterId);
        }

        return 0;
    }

    /// <summary>A short description of where the season stands, for the GM command.</summary>
    public string Describe()
    {
        var season = Season;
        var lines = new List<string>
        {
            $"Season {season}, phase {CurrentPhase}" +
            (IsOverridden ? $"  (FORCED; schedule says {ScheduledPhase})" : "  (following the schedule)"),
            $"Candidates on file: {CountCandidates()}  (frozen when hero_voting opens)"
        };

        foreach (var window in HeroSchedule.ForSeason(season))
        {
            var state = window.Contains(DateTime.UtcNow) ? "now" : window.End <= DateTime.UtcNow ? "past" : "future";
            lines.Add($"  {window.Phase,-18} {window.Start:yyyy-MM-dd} -> {window.End:yyyy-MM-dd}  [{state}]");
        }

        if (lines.Count == 1)
            lines.Add("  hero_schedules has no windows for this season.");

        return string.Join("\n", lines);
    }
}
