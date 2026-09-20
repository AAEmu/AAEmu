using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Heroes;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Heroes;
using AAEmu.Game.Utils;

using MySql.Data.MySqlClient;

using WorldIntegration = AAEmu.Game.WorldIntegration;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Runs the monthly Hero election off <c>heros</c> / <c>hero_schedules</c> (HeroGameData): rolls leadership
/// at LeadershipRanking, freezes candidates at HeroAbstain, records ballots during HeroVoting, and seats
/// heroes plus pays <c>hero_rewards</c> at HeroPeriod. Also owns the serving Hero's Mobilization Order
/// and Dominion Point allowances.
/// </summary>
public class HeroManager(ITaskManager taskManager) : Singleton<HeroManager>, IHeroManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private static (DateTime Start, DateTime End) PhaseWindow(HeroCycle cycle, HeroPhase phase) =>
        cycle != null && cycle.Phases.TryGetValue(phase, out var window)
            ? window
            : (DateTime.UnixEpoch, DateTime.UnixEpoch);

    /// <summary>Last (season, phase) announced to online characters; lets Tick detect a real transition.</summary>
    private (uint Season, HeroPhase Phase) _lastBroadcast = (0, HeroPhase.None);

    /// <summary>GM phase override (/herophase). Null follows hero_schedules.</summary>
    private HeroPhase? _phaseOverride;

    /// <summary>
    /// One nation's in-flight Mobilization Order. A member may accept a given order once; a newer order
    /// from the same nation replaces it. The live call is memory-only; issue/accept clocks persist on
    /// the character so a World restart cannot reset the daily or hourly limits.
    /// </summary>
    private sealed class MobilizationOrder
    {
        public uint HeroId;
        public uint FlagObjId;
        public DateTime ExpiresAt;
        public float IssuerX;
        public float IssuerY;
        public float IssuerZ;
        public float IssuerYawRad;
        public HashSet<uint> AcceptedCharacterIds { get; } = [];
    }

    private readonly Dictionary<uint, MobilizationOrder> _activeMobilizationOrders = new();

    public void Load()
    {
        taskManager.Schedule(new HeroTickTask(), TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(1));
    }

    /// <summary>
    /// The schedule's cycle and phase for now, with the GM override applied. During a schedule gap an
    /// override still needs a cycle to key DB rows on, so the nearest cycle is used.
    /// </summary>
    private (HeroCycle Cycle, HeroPhase Phase) GetEffective(DateTime now)
    {
        var cycle = HeroGameData.Instance.GetCurrentCycle(now);
        if (cycle == null && _phaseOverride.HasValue)
            cycle = HeroGameData.Instance.GetNearestCycle(now);

        var phase = _phaseOverride ?? (cycle?.GetPhase(now) ?? HeroPhase.None);
        return (cycle, phase);
    }

    public void Tick()
    {
        var (cycle, phase) = GetEffective(DateTime.UtcNow);
        var seasonId = cycle?.Id ?? 0;

        if (phase != _lastBroadcast.Phase || seasonId != _lastBroadcast.Season)
        {
            var leaving = _lastBroadcast;
            _lastBroadcast = (seasonId, phase);
            BroadcastPhaseChange(leaving);
        }

        RunPhaseEntryWork(cycle, phase);
    }

    /// <summary>Idempotent per-phase work, shared by the tick and the GM override.</summary>
    private void RunPhaseEntryWork(HeroCycle cycle, HeroPhase phase)
    {
        if (cycle == null)
            return;

        switch (phase)
        {
            case HeroPhase.LeadershipRanking:
                EnsureLeadershipPeriodReset(cycle);
                break;
            case HeroPhase.HeroAbstain:
                EnsureCandidatesComputed(cycle);
                break;
            case HeroPhase.HeroPeriod:
                EnsureElectionFinalized(cycle);
                break;
        }
    }

    /// <summary>Forces the phase (/herophase) or clears the force with null; announces like a real transition.</summary>
    public void SetOverride(HeroPhase? phase)
    {
        _phaseOverride = phase;
        var (cycle, effective) = GetEffective(DateTime.UtcNow);
        var leaving = _lastBroadcast;
        _lastBroadcast = (cycle?.Id ?? 0, effective);
        RunPhaseEntryWork(cycle, effective);
        BroadcastPhaseChange(leaving);
    }

    public string Describe()
    {
        var (cycle, phase) = GetEffective(DateTime.UtcNow);
        var overrideText = _phaseOverride.HasValue ? " (GM override, /herophase auto to clear)" : "";

        if (cycle == null)
            return $"Phase: {phase}{overrideText}. No hero cycle covers now.";

        var windows = string.Join(", ", cycle.Phases.OrderBy(p => p.Key)
            .Select(p => $"{p.Key}={p.Value.Start:yyyy-MM-dd HH:mm}..{p.Value.End:yyyy-MM-dd HH:mm} UTC"));
        return $"Phase: {phase}{overrideText}. Cycle {cycle.Id} schedule: {windows}";
    }

    /// <summary>
    /// Start of a cycle's LeadershipRanking: the running period total becomes the frozen previous-period
    /// figure (the voter gate) and the running total restarts. Guarded by hero_period_resets so a restart
    /// mid-phase cannot roll twice.
    /// </summary>
    private static void EnsureLeadershipPeriodReset(HeroCycle cycle)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        var rolledOnline = new List<(Character Character, int Period, int Current)>();
        try
        {
            using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT COUNT(*) FROM hero_period_resets WHERE cycle_id=@c";
                check.Parameters.AddWithValue("@c", cycle.Id);
                check.Prepare();
                if (Convert.ToInt64(check.ExecuteScalar()) > 0)
                {
                    transaction.Rollback();
                    return;
                }
            }

            using (var roll = connection.CreateCommand())
            {
                roll.Transaction = transaction;
                roll.CommandText = "UPDATE characters SET leadership_period_point=leadership_point, leadership_point=0";
                roll.ExecuteNonQuery();
            }

            foreach (var character in WorldManager.Instance.GetAllCharacters())
            {
                rolledOnline.Add((character, character.LeadershipPeriodPoint, character.LeadershipPoint));
                var rolled = HeroElectionRules.RollLeadershipPeriod(character.LeadershipPeriodPoint, character.LeadershipPoint);
                character.LeadershipPeriodPoint = rolled.Period;
                character.LeadershipPoint = rolled.Current;
                character.Save(connection, transaction);
            }

            using (var mark = connection.CreateCommand())
            {
                mark.Transaction = transaction;
                mark.CommandText = "INSERT INTO hero_period_resets (cycle_id, reset_at) VALUES (@c,@t)";
                mark.Parameters.AddWithValue("@c", cycle.Id);
                mark.Parameters.AddWithValue("@t", DateTime.UtcNow);
                mark.Prepare();
                mark.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            foreach (var (character, period, current) in rolledOnline)
            {
                character.LeadershipPeriodPoint = period;
                character.LeadershipPoint = current;
            }

            Logger.Error(ex, "Hero cycle {0}: leadership period reset failed", cycle.Id);
            return;
        }

        foreach (var character in WorldManager.Instance.GetAllCharacters())
        {
            character.SendPacket(new SCCharacterGamePointsPacket(character));
            character.SendPacket(new SCHeroSeasonOffPacket(0, character.LeadershipPeriodPoint));
        }

        Logger.Info("Hero cycle {0}: rolled leadership_point -> leadership_period_point", cycle.Id);
    }

    private void BroadcastPhaseChange((uint Season, HeroPhase Phase) leaving)
    {
        foreach (var character in WorldManager.Instance.GetAllCharacters())
            PushHeroInfo(character, showUi: false, leaving);
    }

    /// <summary>
    /// Pushes the Hero panel data: phase state, per-faction candidate lists, the seated roster, and the
    /// character's own standing. <paramref name="showUi"/> opens the ballot window and must only be true from
    /// the client's own request (voting machine / list request); a background push with it set re-populates
    /// an open ballot and drops the player's ticked row.
    /// </summary>
    public void SendHeroInfo(Character character, bool showUi = false) => PushHeroInfo(character, showUi, leaving: null);

    private void PushHeroInfo(Character character, bool showUi, (uint Season, HeroPhase Phase)? leaving)
    {
        if (character?.Faction == null)
            return;

        using var connection = MySQL.CreateConnection();
        var (currentCycle, basePhase) = GetEffective(DateTime.UtcNow);
        var ownFactionId = ResolveNationFactionId(character);
        var factionIds = HeroGameData.Instance.FactionsWithRewards.ToList();
        var phaseByFaction = factionIds.ToDictionary(f => f, f => ComputeFactionPhase(f, currentCycle, basePhase, connection));

        character.SendPacket(new SCHeroEventStatePacket(false, HeroElectionRules.BuildEventStateEntries(phaseByFaction, leaving)));

        // The panel has a faction picker, so every faction's list is sent. The client keeps one candidate
        // cache and the last list received wins; the player's own faction goes last so voting resolves
        // against it.
        foreach (var factionId in factionIds.OrderBy(f => f == ownFactionId ? 1 : 0))
            SendHeroInfoForFaction(character, factionId, phaseByFaction[factionId].Phase, phaseByFaction[factionId].SeasonId, showUi, connection, sendScores: false);

        using (var findOwnCandidate = connection.CreateCommand())
        {
            var ownSeasonId = currentCycle?.Id ?? (phaseByFaction.TryGetValue(ownFactionId, out var ownPhase) ? ownPhase.SeasonId : 0);
            findOwnCandidate.CommandText = ownSeasonId == 0
                ? "SELECT votes FROM hero_candidates WHERE faction_id=@f AND character_id=@ch AND abstained=0 ORDER BY cycle_id DESC LIMIT 1"
                : "SELECT votes FROM hero_candidates WHERE faction_id=@f AND character_id=@ch AND cycle_id=@c AND abstained=0";
            findOwnCandidate.Parameters.AddWithValue("@f", ownFactionId);
            findOwnCandidate.Parameters.AddWithValue("@ch", character.Id);
            if (ownSeasonId != 0)
                findOwnCandidate.Parameters.AddWithValue("@c", ownSeasonId);
            findOwnCandidate.Prepare();
            var ownVotesResult = findOwnCandidate.ExecuteScalar();
            var ownVotes = ownVotesResult != null ? (int)Convert.ToInt64(ownVotesResult) : 0;
            character.SendPacket(new SCHeroSeasonInfoPacket((int)basePhase, character.LeadershipPoint, ownVotes));
        }

        // Game-point changes only push these on a new change; a login needs the baseline too.
        character.SendPacket(new SCCharacterGamePointsPacket(character));
        character.SendPacket(new SCHeroSeasonOffPacket(0, character.LeadershipPeriodPoint));

        SendDominionPointCount(character);
    }

    /// <summary>Per-faction request from the panel (score / ranking tab): the phase state for all factions plus that faction's lists.</summary>
    public void SendHeroInfoForRequestedFaction(Character character, uint requestedFactionId, bool showUi = false)
    {
        if (character?.Faction == null)
            return;

        using var connection = MySQL.CreateConnection();
        var (currentCycle, basePhase) = GetEffective(DateTime.UtcNow);
        var factionIds = HeroGameData.Instance.FactionsWithRewards.ToList();
        var phaseByFaction = factionIds.ToDictionary(f => f, f => ComputeFactionPhase(f, currentCycle, basePhase, connection));

        character.SendPacket(new SCHeroEventStatePacket(false, HeroElectionRules.BuildEventStateEntries(phaseByFaction, leaving: null)));

        if (phaseByFaction.TryGetValue(requestedFactionId, out var requested))
            SendHeroInfoForFaction(character, requestedFactionId, requested.Phase, requested.SeasonId, showUi, connection, sendScores: true);
    }

    /// <summary>
    /// hero_schedules has gaps between cycles. In a gap the phase is None, but a faction that has a seated
    /// Hero is still in that Hero's term, so its state stays HeroPeriod for the last finalized cycle.
    /// </summary>
    private static (HeroPhase Phase, uint SeasonId) ComputeFactionPhase(uint factionId, HeroCycle cycle, HeroPhase basePhase, MySqlConnection connection)
    {
        var phase = basePhase;
        var seasonId = cycle?.Id ?? 0;

        if (phase == HeroPhase.None)
        {
            using var checkElected = connection.CreateCommand();
            checkElected.CommandText = "SELECT cycle_id FROM hero_candidates WHERE faction_id=@f AND elected=1 ORDER BY cycle_id DESC LIMIT 1";
            checkElected.Parameters.AddWithValue("@f", factionId);
            checkElected.Prepare();
            var electedCycle = checkElected.ExecuteScalar();
            if (electedCycle != null)
            {
                phase = HeroPhase.HeroPeriod;
                seasonId = Convert.ToUInt32(electedCycle);
            }
        }

        return (phase, seasonId);
    }

    private void SendHeroInfoForFaction(Character character, uint factionId, HeroPhase phase, uint currentSeasonId, bool showUi, MySqlConnection connection, bool sendScores)
    {
        var candidates = new List<HeroCandidateEntry>();
        var rankings = new List<HeroRankingEntry>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = """
                SELECT hc.cycle_id, hc.character_id, hc.leadership_point_at_ranking, hc.votes, c.expedition_id,
                       c.leadership_point, c.accumulated_leadership_point
                FROM hero_candidates hc
                JOIN characters c ON c.id = hc.character_id
                WHERE hc.faction_id=@f AND hc.abstained=0 AND hc.cycle_id=@s
                ORDER BY hc.votes DESC, hc.leadership_point_at_ranking DESC
                """;
            select.Parameters.AddWithValue("@f", factionId);
            select.Parameters.AddWithValue("@s", currentSeasonId);
            select.Prepare();
            using var reader = select.ExecuteReader();
            var ranking = 0;
            while (reader.Read())
            {
                ranking++;
                var seasonId = (uint)reader.GetInt32(0);
                var characterId = (uint)reader.GetInt32(1);
                var leadershipPoint = reader.GetInt32(2);
                var votes = (int)reader.GetInt64(3);
                var expeditionId = (uint)reader.GetInt32(4);
                var liveLeadershipPoint = reader.GetInt32(5);
                var liveAccumulatedLeadershipPoint = reader.GetInt32(6);

                // The ballot treats a zero score as an empty slot, so a standing candidate is floored at 1.
                // Client GetCandidateList / GetHeroList bind packet.score → Lua "score" and
                // packet.accumPoint → Lua "leadership" (the panel titles those 리더십 / 누적 리더십).
                // GetMyScore is a different pair: SeasonInfo.score = votes, SeasonInfo.leadership = current.
                candidates.Add(new HeroCandidateEntry(seasonId, characterId, factionId, expeditionId, ranking, Math.Max(liveLeadershipPoint, 1), liveAccumulatedLeadershipPoint, votes, 0));
                rankings.Add(new HeroRankingEntry(characterId, leadershipPoint, votes, expeditionId));
            }
        }

        Logger.Debug("SendHeroInfo({0}): faction={1} phase={2} candidates={3}", character.Name, factionId, phase, candidates.Count);

        // The "already voted" flag only arrives through the voting packet; send it before the list and
        // keep the ballot closed once a vote is on record.
        var isOwnFaction = factionId == ResolveNationFactionId(character);
        if (isOwnFaction && currentSeasonId != 0 && HasVoted(connection, currentSeasonId, character.Id))
        {
            character.SendPacket(new SCHeroVotingPacket((int)currentSeasonId, 1));
            showUi = false;
        }

        character.SendPacket(new SCHeroCandidateListPacket(showUi, (int)factionId, (int)currentSeasonId, candidates));

        // Scores only on the score tab's own request; the panel refresh does not need them.
        if (sendScores)
        {
            var scores = candidates.Select(c => new HeroScoreEntry((ulong)c.CharacterId, c.Score, c.Score, 0)).ToList();
            character.SendPacket(new SCHeroAllScorePacket((int)factionId, scores));
        }

        // The ranking tab waits on this reply; an empty list still has to answer.
        character.SendPacket(new SCHeroRankingListPacket(factionId, character.LeadershipPoint, 0, rankings));

        var heroes = LoadSeatedHeroes(connection, factionId);
        if (heroes.Count > 0)
        {
            character.SendPacket(new SCHeroListPacket(heroes));
            // The list does not fill the client's per-character hero map; each entry is also sent as an update.
            foreach (var hero in heroes)
                character.SendPacket(new SCHeroInfoUpdatedPacket(hero));

            if (heroes.Any(h => h.CharacterId == character.Id))
                SendMobilizationOrderCount(character, MobilizationOrderAction.None);
        }
    }

    /// <summary>Every seat of the most recently finalized cycle for a faction, ranked.</summary>
    private static List<HeroListEntry> LoadSeatedHeroes(MySqlConnection connection, uint factionId)
    {
        var heroes = new List<HeroListEntry>();
        using var findCycle = connection.CreateCommand();
        findCycle.CommandText = "SELECT cycle_id FROM hero_candidates WHERE faction_id=@f AND elected=1 ORDER BY cycle_id DESC LIMIT 1";
        findCycle.Parameters.AddWithValue("@f", factionId);
        findCycle.Prepare();
        var latestFinalizedCycle = findCycle.ExecuteScalar();
        if (latestFinalizedCycle == null)
            return heroes;

        using var selectHeroes = connection.CreateCommand();
        selectHeroes.CommandText = """
            SELECT hc.character_id, c.expedition_id, c.leadership_point, c.accumulated_leadership_point
            FROM hero_candidates hc
            JOIN characters c ON c.id = hc.character_id
            WHERE hc.cycle_id=@c AND hc.faction_id=@f AND hc.abstained=0 AND hc.elected=1
            ORDER BY hc.votes DESC, hc.leadership_point_at_ranking DESC
            """;
        selectHeroes.Parameters.AddWithValue("@c", latestFinalizedCycle);
        selectHeroes.Parameters.AddWithValue("@f", factionId);
        selectHeroes.Prepare();
        using var reader = selectHeroes.ExecuteReader();
        var ranking = 0;
        var seasonId = Convert.ToUInt32(latestFinalizedCycle);
        while (reader.Read())
        {
            ranking++;
            var characterId = (uint)reader.GetInt32(0);
            var expeditionId = (uint)reader.GetInt32(1);
            var liveLeadershipPoint = reader.GetInt32(2);
            var liveAccumulatedLeadershipPoint = reader.GetInt32(3);
            var heroGrade = (byte)(HeroGameData.Instance.GetReward(factionId, ranking)?.HeroGradeId ?? 0);
            heroes.Add(new HeroListEntry(seasonId, characterId, factionId, expeditionId, ranking, Math.Max(liveLeadershipPoint, 1), liveAccumulatedLeadershipPoint, heroGrade));
        }

        return heroes;
    }

    /// <summary>The character's seat ranking in the latest finalized cycle of their nation, or 0.</summary>
    private static int SeatRankingOf(Character character)
    {
        if (character?.Faction == null)
            return 0;

        var factionId = ResolveNationFactionId(character);
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id FROM hero_candidates
            WHERE faction_id=@f AND elected=1
              AND cycle_id = (SELECT MAX(cycle_id) FROM hero_candidates WHERE faction_id=@f AND elected=1)
            ORDER BY votes DESC, leadership_point_at_ranking DESC
            """;
        command.Parameters.AddWithValue("@f", factionId);
        command.Prepare();
        using var reader = command.ExecuteReader();
        var ranking = 0;
        while (reader.Read())
        {
            ranking++;
            if ((uint)reader.GetInt32(0) == character.Id)
                return ranking;
        }

        return 0;
    }

    /// <summary>Whether this character holds a seat in their nation's latest finalized cycle.</summary>
    public bool IsCurrentHero(Character character) => SeatRankingOf(character) > 0;

    public uint TopSeatedCharacterId(uint nationFactionId)
    {
        if (nationFactionId == 0)
            return 0;

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT character_id FROM hero_candidates
            WHERE faction_id=@f AND elected=1
              AND cycle_id = (SELECT MAX(cycle_id) FROM hero_candidates WHERE faction_id=@f AND elected=1)
            ORDER BY votes DESC, leadership_point_at_ranking DESC
            LIMIT 1
            """;
        command.Parameters.AddWithValue("@f", nationFactionId);
        command.Prepare();
        var result = command.ExecuteScalar();
        return result == null || result == DBNull.Value ? 0 : Convert.ToUInt32(result);
    }

    /// <summary>Resolves a character to the nation faction (the mother of their race faction), which is the key hero data uses.</summary>
    private static uint ResolveNationFactionId(Character character) => (uint)DominionManager.ResolveOwningFaction(character);

    /// <summary>Race faction ids under a nation plus the nation id itself; characters.faction_id is race-level.</summary>
    private static List<uint> RaceFactionIdsUnderNation(uint nationFactionId)
    {
        var ids = FactionManager.Instance.GetSystemFactions()
            .Where(f => (uint)f.MotherId == nationFactionId)
            .Select(f => (uint)f.Id)
            .ToList();
        ids.Add(nationFactionId);
        return ids;
    }

    /// <summary>Whether this character is a standing candidate in the most recently computed cycle for their nation.</summary>
    public bool IsCandidate(Character character)
    {
        if (character?.Faction == null)
            return false;

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM hero_candidates
            WHERE faction_id=@f AND character_id=@ch AND abstained=0
              AND cycle_id = (SELECT MAX(cycle_id) FROM hero_candidates WHERE faction_id=@f)
            """;
        command.Parameters.AddWithValue("@f", ResolveNationFactionId(character));
        command.Parameters.AddWithValue("@ch", character.Id);
        command.Prepare();
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    /// <summary>This character's hero_grades tier, or 0 if not seated.</summary>
    public int GradeOf(Character character)
    {
        var ranking = SeatRankingOf(character);
        if (ranking == 0)
            return 0;
        return (int)(HeroGameData.Instance.GetReward(ResolveNationFactionId(character), ranking)?.HeroGradeId ?? 0);
    }

    // ---- Hero activity bonus ------------------------------------------------------------------------

    /// <summary>A Hero-board today-quest step completed: counts it for the term and re-checks the bonus.</summary>
    public void OnHeroBoardQuestCompleted(Character character, uint todayQuestStepId)
    {
        if (GradeOf(character) == 0)
            return;

        using var connection = MySQL.CreateConnection();
        using (var upsert = connection.CreateCommand())
        {
            upsert.CommandText = """
                INSERT INTO character_hero_bonus_progress (character_id, today_quest_step_id, `count`)
                VALUES (@ch, @step, 1)
                ON DUPLICATE KEY UPDATE `count` = `count` + 1
                """;
            upsert.Parameters.AddWithValue("@ch", character.Id);
            upsert.Parameters.AddWithValue("@step", todayQuestStepId);
            upsert.Prepare();
            upsert.ExecuteNonQuery();
        }

        TryGrantHeroBonus(character, connection);
    }

    /// <summary>
    /// Pays the grade's <c>hero_bonuses</c> tier once per term when every condition holds: term leadership,
    /// Mobilization Orders issued, and each <c>hero_bonus_today_assignments</c> step count.
    /// </summary>
    public void TryGrantHeroBonus(Character character, MySqlConnection connection)
    {
        var grade = (uint)GradeOf(character);
        if (grade == 0)
            return;

        var bonus = HeroGameData.Instance.GetBonusForGrade(grade);
        if (bonus == null)
            return;

        var (cycle, _) = GetEffective(DateTime.UtcNow);
        var cycleId = cycle?.Id ?? 0;
        if (cycleId == 0)
            return;

        using (var claimed = connection.CreateCommand())
        {
            claimed.CommandText = "SELECT COUNT(*) FROM hero_bonus_claims WHERE character_id=@ch AND cycle_id=@c";
            claimed.Parameters.AddWithValue("@ch", character.Id);
            claimed.Parameters.AddWithValue("@c", cycleId);
            claimed.Prepare();
            if (Convert.ToInt64(claimed.ExecuteScalar()) > 0)
                return;
        }

        var progress = new Dictionary<uint, int>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT today_quest_step_id, `count` FROM character_hero_bonus_progress WHERE character_id=@ch";
            select.Parameters.AddWithValue("@ch", character.Id);
            select.Prepare();
            using var reader = select.ExecuteReader();
            while (reader.Read())
                progress[(uint)reader.GetInt32(0)] = reader.GetInt32(1);
        }

        var assignments = HeroGameData.Instance.GetBonusAssignments(bonus.Id);
        if (!HeroElectionRules.MeetsBonusConditions(character.LeadershipPoint, character.MobilizationOrderTotalCount, bonus, assignments, progress))
            return;

        using (var claim = connection.CreateCommand())
        {
            claim.CommandText = "INSERT INTO hero_bonus_claims (character_id, cycle_id, claimed_at) VALUES (@ch, @c, @t)";
            claim.Parameters.AddWithValue("@ch", character.Id);
            claim.Parameters.AddWithValue("@c", cycleId);
            claim.Parameters.AddWithValue("@t", DateTime.UtcNow);
            claim.Prepare();
            claim.ExecuteNonQuery();
        }

        if (bonus.ItemId > 0 && bonus.ItemCount > 0)
        {
            var condition = HeroGameData.Instance.GetCondition(cycle.HeroConditionId);
            var activity = PhaseWindow(cycle, HeroPhase.HeroPeriod);
            var mail = new BaseMail
            {
                MailType = MailType.HeroElectionItem,
                Title = HeroMailWire.LocaleTitle,
                ReceiverName = character.Name
            };
            mail.Header.SenderName = HeroMailWire.BonusSender;
            mail.Header.ReceiverId = character.Id;
            mail.Header.Status = MailStatus.Unread;
            mail.Body.Text = HeroMailWire.FormatPeriodBody(
                condition?.HeroBonusMailBody ?? string.Empty, activity.Start, activity.End);
            mail.Body.RecvDate = DateTime.UtcNow;

            var item = ItemManager.Instance.Create(bonus.ItemId, bonus.ItemCount, bonus.ItemGradeId, true);
            if (item != null)
                mail.Body.Attachments.Add(item);

            mail.Send();
        }

        Logger.Info("Hero bonus paid: {0} cycle={1} tier={2}", character.Name, cycleId, bonus.Id);
    }

    // ---- Dominion Points -----------------------------------------------------------------------------

    /// <summary>Weekly Dominion Point allowance (hero_rewards.dominion_point_weekly_count) for this character's seat, or 0.</summary>
    public int DominionPointWeeklyMax(Character character)
    {
        var ranking = SeatRankingOf(character);
        if (ranking == 0)
            return 0;
        return HeroGameData.Instance.GetReward(ResolveNationFactionId(character), ranking)?.DominionPointWeeklyCount ?? 0;
    }

    /// <summary>Give timestamps for this character in the current ISO week, newest first.</summary>
    private static List<DateTime> LoadWeekGives(MySqlConnection connection, uint characterId, DateTime utcNow)
    {
        var gives = new List<DateTime>();
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT given_at FROM hero_dominion_point_gives WHERE character_id=@ch AND given_at >= @from ORDER BY given_at DESC";
        select.Parameters.AddWithValue("@ch", characterId);
        select.Parameters.AddWithValue("@from", utcNow.AddDays(-7));
        select.Prepare();
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            var at = ServerCalendar.AsUtc(reader.GetDateTime(0));
            if (HeroElectionRules.IsSameIsoWeek(at, utcNow))
                gives.Add(at);
        }

        return gives;
    }

    public (uint daily, uint dailyMax, uint weekly, uint weeklyMax, uint remainSec) GetDominionPointCount(Character character)
    {
        var now = DateTime.UtcNow;
        using var connection = MySQL.CreateConnection();
        var gives = LoadWeekGives(connection, character.Id, now);
        var state = HeroElectionRules.DominionPointState(gives, now, HeroContentConfig.DominionPointDailyMax, HeroContentConfig.DominionPointCooldown);
        return (state.Daily, HeroContentConfig.DominionPointDailyMax, state.Weekly, (uint)DominionPointWeeklyMax(character), state.RemainSeconds);
    }

    public void SendDominionPointCount(Character character)
    {
        var (daily, dailyMax, weekly, weeklyMax, remainSec) = GetDominionPointCount(character);
        character.SendPacket(new SCHeroDominionPointCountPacket(daily, dailyMax, weekly, weeklyMax, remainSec));
    }

    public enum DominionPointGiveResult
    {
        Success,
        NotHero,
        NoSuchDominion,
        WrongFaction,
        DailyLimitReached,
        OnCooldown,
        WeeklyCapReached
    }

    /// <summary>
    /// A seated Hero distributes Dominion Points (content_configs.hero_dominion_point) to a territory their
    /// nation owns, within the daily limit, cooldown and weekly cap. The territory-side spending of the
    /// points is not modelled yet; the give is logged.
    /// </summary>
    public DominionPointGiveResult GiveDominionPoint(Character character, ushort zoneId)
    {
        if (character?.Faction == null || !IsCurrentHero(character))
            return DominionPointGiveResult.NotHero;

        var dominion = GuildDominionManager.Instance.GetByZoneId(zoneId) ?? DominionManager.Instance.GetByZoneId(zoneId);
        if (dominion == null)
            return DominionPointGiveResult.NoSuchDominion;

        if (dominion.OwningFactionId != ResolveNationFactionId(character))
            return DominionPointGiveResult.WrongFaction;

        var now = DateTime.UtcNow;
        using var connection = MySQL.CreateConnection();
        var gives = LoadWeekGives(connection, character.Id, now);
        var state = HeroElectionRules.DominionPointState(gives, now, HeroContentConfig.DominionPointDailyMax, HeroContentConfig.DominionPointCooldown);
        var weeklyMax = DominionPointWeeklyMax(character);

        if (weeklyMax <= 0 || state.Weekly >= weeklyMax)
            return DominionPointGiveResult.WeeklyCapReached;
        if (state.Daily >= HeroContentConfig.DominionPointDailyMax)
            return DominionPointGiveResult.DailyLimitReached;
        if (state.RemainSeconds > 0)
            return DominionPointGiveResult.OnCooldown;

        var points = HeroContentConfig.DominionPointValue;
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO hero_dominion_point_gives (character_id, zone_group_id, points, given_at) VALUES (@ch, @z, @p, @t)";
            insert.Parameters.AddWithValue("@ch", character.Id);
            insert.Parameters.AddWithValue("@z", zoneId);
            insert.Parameters.AddWithValue("@p", points);
            insert.Parameters.AddWithValue("@t", now);
            insert.Prepare();
            insert.ExecuteNonQuery();
        }

        SendDominionPointCount(character);
        character.SendPacket(new SCHeroGiveDominionPointPacket(0, character.Name, 0, (uint)Math.Max(points, 0), true));
        Logger.Info("Dominion point: {0} gave {1} to zone group {2}", character.Name, points, zoneId);

        return DominionPointGiveResult.Success;
    }

    // ---- Mobilization Order -------------------------------------------------------------------------

    /// <summary>
    /// Each nation's rally flag is the doodad whose template carries that nation's <c>faction_id</c> and an
    /// <c>DoodadFuncIssuanceOfMobilizationOrderUiOpen</c> func. Its spawn gives both the dialog's zone group
    /// and the rally point members are sent to.
    /// </summary>
    private static Doodad FindMobilizationFlag(uint nationFactionId)
    {
        foreach (var world in WorldManager.Instance.GetWorlds())
        {
            foreach (var doodad in WorldManager.Instance.GetAllDoodadsFromWorld(world.Id))
            {
                if (doodad.Template == null || (uint)doodad.Template.FactionId != nationFactionId)
                    continue;
                if (IsMobilizationFlagTemplate(doodad.Template.Id))
                    return doodad;
            }
        }

        return null;
    }

    private static bool IsMobilizationFlagTemplate(uint templateId)
    {
        var template = DoodadManager.Instance.GetTemplate(templateId);
        if (template == null)
            return false;

        foreach (var group in template.FuncGroups)
        {
            foreach (var func in DoodadManager.Instance.GetFuncsForGroup(group.Id))
            {
                if (func.FuncType == nameof(DoodadFuncIssuanceOfMobilizationOrderUiOpen))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Zone group of a nation's rally flag (what the order dialog is keyed on), or 0 when none is spawned.</summary>
    public uint ResolveMobilizationOrderZoneGroupId(Character character)
    {
        var flag = FindMobilizationFlag(ResolveNationFactionId(character));
        return flag == null ? 0 : ZoneGroupOf(flag);
    }

    private static uint ZoneGroupOf(Doodad doodad) =>
        ZoneManager.Instance.GetZoneByKey(doodad.Transform.ZoneId)?.GroupId ?? 0;

    /// <summary>
    /// The accept-popup checkbox "do not receive today". <paramref name="mute"/> stamps now (or epoch
    /// when cleared). <paramref name="persist"/> writes immediately so a World kill cannot restore the
    /// popup; the live checkbox always asks to persist.
    /// </summary>
    public void SetMobilizationOrderNotRecv(Character character, bool mute, bool persist)
    {
        if (character == null)
            return;

        character.LastMobilizationNotRecvTime = HeroElectionRules.MobilizationOrderNotRecvStamp(mute, DateTime.UtcNow);
        if (persist)
            PersistMobilizationClocks(character);
        Logger.Info("Mobilization order not-recv mute={0} persist={1} for {2} (stamp {3:o})",
            mute, persist, character.Name, character.LastMobilizationNotRecvTime);
    }

    /// <summary>Pushes the Hero's own order counters so the client's dialog gate has the flag's zone group and today's count.</summary>
    public void SendMobilizationOrderCount(Character character, MobilizationOrderAction action)
    {
        character.SendPacket(new SCHeroMobilizationOrderUpdatedPacket(
            (byte)action,
            ResolveMobilizationOrderZoneGroupId(character),
            character.Id,
            (uint)character.MobilizationOrderTodayCount,
            (uint)character.MobilizationOrderTotalCount));
    }

    /// <summary>
    /// A seated Hero issues a Mobilization Order from a rally flag. Members of the nation get the accept
    /// popup unless they muted it for the UTC day or already accepted this UTC hour; accepting teleports
    /// them to the flag's authored stand (<see cref="AcceptMobilizationOrder"/>).
    /// </summary>
    public bool IssueMobilizationOrder(Character character, uint flagObjId)
    {
        if (character?.Faction == null)
            return false;

        if (!IsCurrentHero(character))
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        var nationFactionId = ResolveNationFactionId(character);
        var flag = character.ParentWorld?.GetDoodad(flagObjId);
        if (flag == null || (uint)flag.Template.FactionId != nationFactionId || !IsMobilizationFlagTemplate(flag.TemplateId))
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        var now = DateTime.UtcNow;
        if (!HeroElectionRules.IsSameUtcDay(character.LastMobilizationOrderTime, now))
            character.MobilizationOrderTodayCount = 0;

        var dailyMax = HeroContentConfig.MobilizationDailyMax;
        if (!HeroElectionRules.CanIssueMobilizationOrder(character.MobilizationOrderTodayCount, dailyMax))
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        character.MobilizationOrderTodayCount++;
        character.MobilizationOrderTotalCount++;
        character.LastMobilizationOrderTime = now;
        PersistMobilizationClocks(character);

        var issuer = character.Transform.World;
        _activeMobilizationOrders[nationFactionId] = new MobilizationOrder
        {
            HeroId = character.Id,
            FlagObjId = flag.ObjId,
            ExpiresAt = now + HeroContentConfig.MobilizationAcceptWindow,
            IssuerX = issuer.Position.X,
            IssuerY = issuer.Position.Y,
            IssuerZ = issuer.Position.Z,
            IssuerYawRad = issuer.Rotation.Z
        };

        var zoneGroupId = ZoneGroupOf(flag);
        var scOrder = new SCFactionMobilizationOrderPacket((ushort)zoneGroupId, character.Id, character.Name);
        var scUpdated = new SCHeroMobilizationOrderUpdatedPacket(
            (byte)MobilizationOrderAction.Issued, zoneGroupId, character.Id,
            (uint)character.MobilizationOrderTodayCount, (uint)character.MobilizationOrderTotalCount);
        foreach (var member in WorldManager.Instance.GetAllCharacters()
                     .Where(c => c.Faction != null && ResolveNationFactionId(c) == nationFactionId))
        {
            if (HeroElectionRules.ShouldOfferMobilizationOrder(
                    member.LastMobilizationNotRecvTime, member.LastMobilizationAcceptTime, now))
                member.SendPacket(scOrder);
            member.SendPacket(scUpdated);
        }

        character.SendPacket(new SCFactionMobilizationOrderSuccessPacket());
        using (var bonusConnection = MySQL.CreateConnection())
            TryGrantHeroBonus(character, bonusConnection);
        Logger.Info("Mobilization order issued by {0} for faction {1} at flag {2}: today {3}/{4}",
            character.Name, nationFactionId, flag.TemplateId, character.MobilizationOrderTodayCount, dailyMax);
        return true;
    }

    /// <summary>
    /// A nation member accepts the current order: teleported to the flag's stand pad and mailed the rally
    /// item (content_configs.mobilization_order_give_item). Rejects a stale or superseded order, a member
    /// below the accept level / leadership, a second accept in the same UTC hour, and a repeat accept of
    /// the same live order.
    /// </summary>
    public bool AcceptMobilizationOrder(Character character, ulong heroId)
    {
        if (character?.Faction == null)
            return false;

        var nationFactionId = ResolveNationFactionId(character);
        if (!_activeMobilizationOrders.TryGetValue(nationFactionId, out var order) || order.HeroId != heroId)
        {
            Logger.Warn(
                "Mobilization accept refused for {0}: no live order matching hero {1} (faction {2}, liveHero={3})",
                character.Name, heroId, nationFactionId,
                _activeMobilizationOrders.TryGetValue(nationFactionId, out var live) ? live.HeroId.ToString() : "none");
            return false;
        }

        var now = DateTime.UtcNow;
        if (!HeroElectionRules.CanAcceptMobilizationOrder(now, order.ExpiresAt, character.Level, character.LeadershipPoint,
                HeroContentConfig.MobilizationAcceptLevel, HeroContentConfig.MobilizationAcceptLeadership))
        {
            Logger.Warn(
                "Mobilization accept refused for {0}: window or threshold (level {1}, leadership {2}, expires {3:o})",
                character.Name, character.Level, character.LeadershipPoint, order.ExpiresAt);
            return false;
        }

        if (order.AcceptedCharacterIds.Contains(character.Id))
        {
            Logger.Warn("Mobilization accept refused for {0}: already rallied this order", character.Name);
            return false;
        }

        if (!HeroElectionRules.CanAcceptMobilizationAgainThisHour(character.LastMobilizationAcceptTime, now))
        {
            Logger.Warn(
                "Mobilization accept refused for {0}: already accepted this UTC hour (last {1:o})",
                character.Name, character.LastMobilizationAcceptTime);
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        var flag = FindDoodadAcrossWorlds(order.FlagObjId);
        if (!HeroElectionRules.CanTransferToMobilizationFlag(flag != null, flag?.ParentWorld != null))
        {
            Logger.Warn("Mobilization order for faction {0}: rally flag {1} is no longer spawned", nationFactionId, order.FlagObjId);
            return false;
        }

        if (!TryTransferToRallyStand(character, flag, order))
        {
            Logger.Warn("Mobilization order for faction {0}: could not transfer {1} to flag {2}", nationFactionId, character.Name, order.FlagObjId);
            return false;
        }

        if (!SendMobilizationGiveItem(character))
        {
            Logger.Warn("Mobilization order for faction {0}: rally item mail failed for {1}", nationFactionId, character.Name);
            return false;
        }

        order.AcceptedCharacterIds.Add(character.Id);
        character.LastMobilizationAcceptTime = now;
        PersistMobilizationClocks(character);
        Logger.Info("Mobilization order: {0} rallied to faction {1}'s stand", character.Name, nationFactionId);
        return true;
    }

    private static bool TryTransferToRallyStand(Character character, Doodad flag, MobilizationOrder order)
    {
        var destination = flag?.Transform;
        if (destination == null || flag.ParentWorld == null)
            return false;

        var x = destination.World.Position.X;
        var y = destination.World.Position.Y;
        var z = destination.World.Position.Z;
        var yaw = destination.World.Rotation.Z;
        var zoneId = destination.ZoneId;

        if (TryResolveRallyStand(flag, out var stand))
        {
            x = stand.X;
            y = stand.Y;
            z = stand.Z;
            yaw = stand.YawRad;
            if (stand.ZoneId != 0)
                zoneId = stand.ZoneId;
        }
        else if (ReturnTeleportRules.HasValidDestination(order.IssuerX, order.IssuerY, order.IssuerZ))
        {
            x = order.IssuerX;
            y = order.IssuerY;
            z = order.IssuerZ;
            yaw = order.IssuerYawRad;
        }

        if (!ReturnTeleportRules.HasValidDestination(x, y, z))
            return false;

        if (!TeleportLandingRules.CanLandInZone(
                WorldIntegration.ZoneAuthority, WorldIntegration.IsZoneLoaded, zoneId))
        {
            Logger.Warn(
                "Mobilization order: refusing rally for {0} — stand zone {1} cannot be landed",
                character.Name, zoneId);
            return false;
        }

        var destInstanceId = HeroElectionRules.LandingInstanceId(
            character.Transform.InstanceId,
            flag.ParentWorld.Id,
            ReferenceEquals(character.ParentWorld, flag.ParentWorld));
        var destWorldId = flag.ParentWorld.Template?.Id ?? destination.WorldId;
        var stayInZone = HeroElectionRules.StaysInZone(
            character.Transform.ZoneId, zoneId, destInstanceId, character.Transform.InstanceId);
        SkillTeleportLanding.Apply(
            character, destWorldId, zoneId, destInstanceId, x, y, z, yaw,
            TeleportReason.MobilizationOrder, stayInZone);
        return true;
    }

    private static bool TryResolveRallyStand(Doodad flag, out HeroElectionRules.RallyStand stand)
    {
        stand = default;
        var milestone = flag.Template?.MilestoneId ?? 0;
        if (milestone == 0)
            return false;

        var pads = new List<HeroElectionRules.RallyStand>();
        foreach (var id in HeroGameData.Instance.GetReturnPointIdsForMilestone(milestone))
        {
            var portal = PortalManager.Instance.GetReturnPoint(id);
            if (portal == null || !ReturnTeleportRules.HasValidDestination(portal.X, portal.Y, portal.Z))
                continue;
            pads.Add(new HeroElectionRules.RallyStand(portal.X, portal.Y, portal.Z, portal.Yaw.DegToRad(), portal.ZoneId));
        }

        return HeroElectionRules.TryPickRallyStand(
            flag.Transform.World.Position.X,
            flag.Transform.World.Position.Y,
            pads,
            out stand);
    }

    private static void PersistMobilizationClocks(Character character)
    {
        if (character == null)
            return;

        try
        {
            SaveManager.Instance.ExecuteOperation((connection, transaction) =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE characters SET
                        mobilization_order_today_count=@today,
                        mobilization_order_total_count=@total,
                        last_mobilization_order_time=@issued,
                        last_mobilization_accept_time=@accepted,
                        last_mobilization_not_recv_time=@notRecv
                    WHERE id=@id
                    """;
                command.Parameters.AddWithValue("@today", character.MobilizationOrderTodayCount);
                command.Parameters.AddWithValue("@total", character.MobilizationOrderTotalCount);
                command.Parameters.AddWithValue("@issued", PersistUtc(character.LastMobilizationOrderTime));
                command.Parameters.AddWithValue("@accepted", PersistUtc(character.LastMobilizationAcceptTime));
                command.Parameters.AddWithValue("@notRecv", PersistUtc(character.LastMobilizationNotRecvTime));
                command.Parameters.AddWithValue("@id", character.Id);
                command.Prepare();
                return command.ExecuteNonQuery();
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Mobilization clocks failed to persist for {0}", character.Name);
        }
    }

    private static DateTime PersistUtc(DateTime value)
    {
        var utc = ServerCalendar.AsUtc(value);
        return utc <= DateTime.UnixEpoch ? DateTime.UnixEpoch : utc;
    }

    private static bool SendMobilizationGiveItem(Character character)
    {
        var itemId = HeroContentConfig.MobilizationGiveItemId;
        if (itemId == 0)
            return true;

        var item = ItemManager.Instance.Create(itemId, 1, 0, true);
        if (item == null)
            return false;

        var mail = new BaseMail
        {
            MailType = MailType.MobilizationGiveItem,
            Title = HeroMailWire.LocaleTitle,
            ReceiverName = character.Name
        };
        mail.Header.SenderName = HeroMailWire.MobilizationSender;
        mail.Header.ReceiverId = character.Id;
        mail.Header.Status = MailStatus.Unread;
        mail.Body.Text = HeroMailWire.LocaleBody;
        mail.Body.RecvDate = DateTime.UtcNow;
        mail.Body.Attachments.Add(item);
        return mail.Send();
    }

    // ---- Ballot / candidates / finalize --------------------------------------------------------------

    /// <summary>
    /// Records a ballot: the whole selection is accepted or rejected together, and a voter has one ballot
    /// per cycle.
    /// </summary>
    public void Vote(GameConnection connection, IReadOnlyCollection<ulong> candidateCharacterIds)
    {
        var voter = connection?.ActiveChar;
        if (voter == null)
            return;

        var (cycle, phase) = GetEffective(DateTime.UtcNow);
        if (cycle == null || phase != HeroPhase.HeroVoting)
        {
            Logger.Info("Vote({0}): rejected, cycle={1} phase={2}", voter.Name, cycle?.Id, phase);
            return;
        }

        var factionId = ResolveNationFactionId(voter);
        var seats = HeroGameData.Instance.SeatsFor(factionId);
        var condition = HeroGameData.Instance.GetCondition(cycle.HeroConditionId);
        var picks = candidateCharacterIds?.Count ?? 0;
        if (!HeroElectionRules.CanCastBallot(picks, seats, voter.Level, voter.LeadershipPeriodPoint,
                condition?.VotableLevel ?? 0, condition?.VotableLeadershipPoint ?? 0))
        {
            Logger.Info("Vote({0}): rejected, picks={1} seats={2} level={3} periodLeadership={4}",
                voter.Name, picks, seats, voter.Level, voter.LeadershipPeriodPoint);
            voter.SendErrorMessage(ErrorMessageType.NoPerm);
            return;
        }

        using var connection2 = MySQL.CreateConnection();
        if (HasVoted(connection2, cycle.Id, voter.Id))
        {
            Logger.Info("Vote({0}): rejected, already voted in cycle {1}", voter.Name, cycle.Id);
            return;
        }

        var candidateIds = candidateCharacterIds.Select(id => (uint)id).Distinct().ToList();
        foreach (var candidateId in candidateIds)
        {
            using var checkCandidate = connection2.CreateCommand();
            checkCandidate.CommandText = "SELECT COUNT(*) FROM hero_candidates WHERE cycle_id=@c AND faction_id=@f AND character_id=@ch AND abstained=0";
            checkCandidate.Parameters.AddWithValue("@c", cycle.Id);
            checkCandidate.Parameters.AddWithValue("@f", factionId);
            checkCandidate.Parameters.AddWithValue("@ch", candidateId);
            checkCandidate.Prepare();
            if (Convert.ToInt64(checkCandidate.ExecuteScalar()) == 0)
            {
                Logger.Info("Vote({0}): rejected, {1} is not a standing candidate in cycle {2}", voter.Name, candidateId, cycle.Id);
                return;
            }
        }

        using (var ballot = connection2.BeginTransaction())
        {
            try
            {
                foreach (var candidateId in candidateIds)
                {
                    using var insertVote = connection2.CreateCommand();
                    insertVote.Transaction = ballot;
                    insertVote.CommandText = "INSERT INTO hero_votes (cycle_id, faction_id, voter_character_id, candidate_character_id) VALUES (@c,@f,@v,@ch)";
                    insertVote.Parameters.AddWithValue("@c", cycle.Id);
                    insertVote.Parameters.AddWithValue("@f", factionId);
                    insertVote.Parameters.AddWithValue("@v", voter.Id);
                    insertVote.Parameters.AddWithValue("@ch", candidateId);
                    insertVote.Prepare();
                    insertVote.ExecuteNonQuery();
                }

                RecountVotes(connection2, cycle.Id, factionId, ballot);
                ballot.Commit();
            }
            catch (Exception ex)
            {
                ballot.Rollback();
                Logger.Error(ex, "Vote({0}): ballot persist failed for cycle {1}", voter.Name, cycle.Id);
                return;
            }
        }

        Logger.Info("Vote({0}): recorded [{1}] cycle={2} faction={3}", voter.Name, string.Join(",", candidateIds), cycle.Id, factionId);

        voter.SendPacket(new SCHeroVotingPacket((int)cycle.Id, 1));
    }

    public void Abstain(GameConnection connection)
    {
        var character = connection?.ActiveChar;
        if (character == null)
            return;

        var (cycle, phase) = GetEffective(DateTime.UtcNow);
        if (cycle == null || phase != HeroPhase.HeroAbstain)
            return;

        SetAbstained(character, cycle.Id, true);
    }

    public void DropoutComeback(GameConnection connection)
    {
        var character = connection?.ActiveChar;
        if (character == null)
            return;

        var (cycle, phase) = GetEffective(DateTime.UtcNow);
        if (cycle == null || phase != HeroPhase.HeroAbstain)
            return;

        SetAbstained(character, cycle.Id, false);
    }

    /// <summary>
    /// Freezes the candidate list for a cycle: per nation, the top <c>hero_candidate_scope</c> characters
    /// meeting the candidacy thresholds. Idempotent per faction so a later tick can finish a cycle that
    /// stopped after the first nation's rows were written.
    /// </summary>
    private void EnsureCandidatesComputed(HeroCycle cycle)
    {
        var condition = HeroGameData.Instance.GetCondition(cycle.HeroConditionId);
        if (condition == null)
            return;

        using var connection = MySQL.CreateConnection();
        foreach (var factionId in HeroGameData.Instance.FactionsWithRewards)
        {
            if (CountCandidates(connection, cycle.Id, factionId) > 0)
            {
                SendPendingCandidateMails(connection, cycle, factionId, condition);
                continue;
            }

            var persisted = new List<(uint characterId, int points)>();
            using (var select = connection.CreateCommand())
            {
                var raceFactionIds = RaceFactionIdsUnderNation(factionId);
                var placeholders = string.Join(",", raceFactionIds.Select((_, i) => "@f" + i));
                select.CommandText = $"SELECT id, leadership_point FROM characters WHERE faction_id IN ({placeholders}) AND level>=@lvl AND leadership_point>=@pt ORDER BY leadership_point DESC LIMIT @scope";
                for (var i = 0; i < raceFactionIds.Count; i++)
                    select.Parameters.AddWithValue("@f" + i, raceFactionIds[i]);
                select.Parameters.AddWithValue("@lvl", condition.HeroCandidateMinLevel);
                select.Parameters.AddWithValue("@pt", condition.HeroCandidateMinPoint);
                select.Parameters.AddWithValue("@scope", Math.Max(condition.HeroCandidateScope, 1));
                select.Prepare();
                using var reader = select.ExecuteReader();
                while (reader.Read())
                    persisted.Add(((uint)reader.GetInt32(0), reader.GetInt32(1)));
            }

            var live = WorldManager.Instance.GetAllCharacters()
                .Where(ch => ch.Faction != null && ResolveNationFactionId(ch) == factionId)
                .Select(ch => (ch.Id, (int)ch.Level, ch.LeadershipPoint));
            var candidates = HeroElectionRules.MergeAndRankCandidates(
                persisted, live, condition.HeroCandidateMinLevel, condition.HeroCandidateMinPoint,
                condition.HeroCandidateScope);

            using (var persist = connection.BeginTransaction())
            {
                try
                {
                    foreach (var (characterId, points) in candidates)
                    {
                        using var insert = connection.CreateCommand();
                        insert.Transaction = persist;
                        insert.CommandText = "INSERT INTO hero_candidates (cycle_id, faction_id, character_id, leadership_point_at_ranking, candidate_mail_sent, reward_mail_sent) VALUES (@c,@f,@ch,@p,0,0)";
                        insert.Parameters.AddWithValue("@c", cycle.Id);
                        insert.Parameters.AddWithValue("@f", factionId);
                        insert.Parameters.AddWithValue("@ch", characterId);
                        insert.Parameters.AddWithValue("@p", points);
                        insert.Prepare();
                        insert.ExecuteNonQuery();
                    }

                    persist.Commit();
                }
                catch (Exception ex)
                {
                    persist.Rollback();
                    Logger.Error(ex, "Hero cycle {0} faction {1}: candidate persist failed", cycle.Id, factionId);
                    continue;
                }
            }

            foreach (var (characterId, _) in candidates)
                TrySendCandidateMail(connection, cycle.Id, factionId, characterId, condition, cycle);

            Logger.Info("Hero cycle {0} faction {1}: {2} candidates", cycle.Id, factionId, candidates.Count);
        }
    }

    /// <summary>
    /// Seats the heroes for a cycle: candidates ranked by votes take a seat while a <c>hero_rewards</c> row
    /// exists for their rank, each paid their rank's item set by mail. Idempotent per faction so a later
    /// tick can finish a cycle that stopped after the first nation was seated.
    /// </summary>
    private void EnsureElectionFinalized(HeroCycle cycle)
    {
        using var connection = MySQL.CreateConnection();
        var condition = HeroGameData.Instance.GetCondition(cycle.HeroConditionId);

        foreach (var factionId in HeroGameData.Instance.FactionsWithRewards)
        {
            if (CountElected(connection, cycle.Id, factionId) > 0)
            {
                SendPendingRewardMails(connection, cycle, factionId, condition);
                continue;
            }

            var ranked = new List<uint>();
            using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT character_id FROM hero_candidates WHERE cycle_id=@c AND faction_id=@f AND abstained=0 ORDER BY votes DESC, leadership_point_at_ranking DESC";
                select.Parameters.AddWithValue("@c", cycle.Id);
                select.Parameters.AddWithValue("@f", factionId);
                select.Prepare();
                using var reader = select.ExecuteReader();
                while (reader.Read())
                    ranked.Add((uint)reader.GetInt32(0));
            }

            var seats = HeroGameData.Instance.SeatsFor(factionId);
            if (ranked.Count == 0 || seats <= 0)
                continue;

            var seatedIds = new List<uint>();
            var pendingRewards = new List<(uint CharacterId, HeroReward Reward)>();
            using (var persist = connection.BeginTransaction())
            {
                try
                {
                    for (var i = 0; i < ranked.Count; i++)
                    {
                        var characterId = ranked[i];
                        var ranking = i + 1;
                        var seated = HeroElectionRules.HoldsSeat(ranking, seats);

                        using (var update = connection.CreateCommand())
                        {
                            update.Transaction = persist;
                            update.CommandText = "UPDATE hero_candidates SET elected=@e WHERE cycle_id=@c AND faction_id=@f AND character_id=@ch";
                            update.Parameters.AddWithValue("@e", seated);
                            update.Parameters.AddWithValue("@c", cycle.Id);
                            update.Parameters.AddWithValue("@f", factionId);
                            update.Parameters.AddWithValue("@ch", characterId);
                            update.Prepare();
                            update.ExecuteNonQuery();
                        }

                        if (!seated)
                            continue;

                        ResetTermCounters(connection, characterId, persist);
                        seatedIds.Add(characterId);
                        var reward = HeroGameData.Instance.GetReward(factionId, ranking);
                        if (reward != null)
                            pendingRewards.Add((characterId, reward));
                    }

                    persist.Commit();
                }
                catch (Exception ex)
                {
                    persist.Rollback();
                    Logger.Error(ex, "Hero cycle {0} faction {1}: finalize persist failed", cycle.Id, factionId);
                    continue;
                }
            }

            foreach (var characterId in seatedIds)
                ApplyTermCountersOnline(characterId);
            foreach (var (characterId, reward) in pendingRewards)
                TrySendRewardMail(connection, cycle.Id, factionId, characterId, reward, condition, cycle);

            Logger.Info("Hero cycle {0} faction {1}: seated {2} of {3} ranked candidates", cycle.Id, factionId, Math.Min(seats, ranked.Count), ranked.Count);
        }
    }

    /// <summary>A new term starts with fresh Mobilization Order and Hero-board bonus counters.</summary>
    private static void ResetTermCounters(MySqlConnection connection, uint characterId, MySqlTransaction transaction)
    {
        using (var reset = connection.CreateCommand())
        {
            reset.Transaction = transaction;
            reset.CommandText = "UPDATE characters SET mobilization_order_today_count=0, mobilization_order_total_count=0 WHERE id=@ch";
            reset.Parameters.AddWithValue("@ch", characterId);
            reset.Prepare();
            reset.ExecuteNonQuery();
        }

        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM character_hero_bonus_progress WHERE character_id=@ch";
            clear.Parameters.AddWithValue("@ch", characterId);
            clear.Prepare();
            clear.ExecuteNonQuery();
        }
    }

    private static void ApplyTermCountersOnline(uint characterId)
    {
        var online = WorldManager.Instance.GetCharacterById(characterId);
        if (online == null)
            return;
        online.MobilizationOrderTodayCount = 0;
        online.MobilizationOrderTotalCount = 0;
    }

    private static bool HasVoted(MySqlConnection connection, uint cycleId, uint characterId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM hero_votes WHERE cycle_id=@c AND voter_character_id=@v";
        command.Parameters.AddWithValue("@c", cycleId);
        command.Parameters.AddWithValue("@v", characterId);
        command.Prepare();
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static void RecountVotes(MySqlConnection connection, uint cycleId, uint factionId, MySqlTransaction transaction)
    {
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE hero_candidates hc
            SET votes = (SELECT COUNT(*) FROM hero_votes hv WHERE hv.cycle_id = hc.cycle_id AND hv.faction_id = hc.faction_id AND hv.candidate_character_id = hc.character_id)
            WHERE hc.cycle_id = @c AND hc.faction_id = @f
            """;
        update.Parameters.AddWithValue("@c", cycleId);
        update.Parameters.AddWithValue("@f", factionId);
        update.Prepare();
        update.ExecuteNonQuery();
    }

    private static long CountCandidates(MySqlConnection connection, uint cycleId, uint factionId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM hero_candidates WHERE cycle_id=@c AND faction_id=@f";
        command.Parameters.AddWithValue("@c", cycleId);
        command.Parameters.AddWithValue("@f", factionId);
        command.Prepare();
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static long CountElected(MySqlConnection connection, uint cycleId, uint factionId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM hero_candidates WHERE cycle_id=@c AND faction_id=@f AND elected=1";
        command.Parameters.AddWithValue("@c", cycleId);
        command.Parameters.AddWithValue("@f", factionId);
        command.Prepare();
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static Doodad FindDoodadAcrossWorlds(uint objId)
    {
        foreach (var world in WorldManager.Instance.GetWorlds() ?? [])
        {
            var doodad = world.GetDoodad(objId);
            if (doodad != null)
                return doodad;
        }

        return null;
    }

    private static void SetAbstained(Character character, uint cycleId, bool abstained)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE hero_candidates SET abstained=@a WHERE cycle_id=@c AND faction_id=@f AND character_id=@ch";
        command.Parameters.AddWithValue("@a", abstained);
        command.Parameters.AddWithValue("@c", cycleId);
        command.Parameters.AddWithValue("@f", ResolveNationFactionId(character));
        command.Parameters.AddWithValue("@ch", character.Id);
        command.Prepare();
        command.ExecuteNonQuery();
    }

    private static void SendPendingCandidateMails(MySqlConnection connection, HeroCycle cycle, uint factionId, HeroCondition condition)
    {
        var rows = new List<(uint CharacterId, bool MailSent)>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT character_id, candidate_mail_sent FROM hero_candidates WHERE cycle_id=@c AND faction_id=@f";
            select.Parameters.AddWithValue("@c", cycle.Id);
            select.Parameters.AddWithValue("@f", factionId);
            select.Prepare();
            using var reader = select.ExecuteReader();
            while (reader.Read())
                rows.Add(((uint)reader.GetInt32(0), reader.GetBoolean(1)));
        }

        foreach (var characterId in HeroElectionRules.PendingMailCharacterIds(rows))
            TrySendCandidateMail(connection, cycle.Id, factionId, characterId, condition, cycle);
    }

    private static void SendPendingRewardMails(MySqlConnection connection, HeroCycle cycle, uint factionId, HeroCondition condition)
    {
        var ranked = new List<(uint CharacterId, bool MailSent)>();
        using (var select = connection.CreateCommand())
        {
            select.CommandText = "SELECT character_id, reward_mail_sent FROM hero_candidates WHERE cycle_id=@c AND faction_id=@f AND elected=1 ORDER BY votes DESC, leadership_point_at_ranking DESC";
            select.Parameters.AddWithValue("@c", cycle.Id);
            select.Parameters.AddWithValue("@f", factionId);
            select.Prepare();
            using var reader = select.ExecuteReader();
            while (reader.Read())
                ranked.Add(((uint)reader.GetInt32(0), reader.GetBoolean(1)));
        }

        for (var i = 0; i < ranked.Count; i++)
        {
            if (ranked[i].MailSent)
                continue;
            var reward = HeroGameData.Instance.GetReward(factionId, i + 1);
            if (reward == null)
                continue;
            TrySendRewardMail(connection, cycle.Id, factionId, ranked[i].CharacterId, reward, condition, cycle);
        }
    }

    private static void TrySendCandidateMail(MySqlConnection connection, uint cycleId, uint factionId, uint characterId, HeroCondition condition, HeroCycle cycle)
    {
        var mail = BuildCandidateMail(characterId, condition, cycle);
        if (mail == null)
            return;
        DeliverElectionMail(connection, cycleId, factionId, characterId, candidateMail: true, mail);
    }

    private static void TrySendRewardMail(MySqlConnection connection, uint cycleId, uint factionId, uint characterId, HeroReward reward, HeroCondition condition, HeroCycle cycle)
    {
        var mail = BuildRewardMail(characterId, reward, condition, cycle);
        if (mail == null)
            return;
        DeliverElectionMail(connection, cycleId, factionId, characterId, candidateMail: false, mail);
    }

    /// <summary>
    /// Claim marker and mail (plus attachment items) commit together. A stop before commit
    /// leaves sent=0 so the next tick can retry; a commit cannot mark sent without the mail row.
    /// </summary>
    private static void DeliverElectionMail(MySqlConnection connection, uint cycleId, uint factionId, uint characterId, bool candidateMail, BaseMail mail)
    {
        using var persist = MailManager.Instance.DeferPersist();
        using var transaction = connection.BeginTransaction();
        try
        {
            if (!TryClaimMailSent(connection, transaction, cycleId, factionId, characterId, candidateMail))
            {
                transaction.Rollback();
                return;
            }

            if (!MailManager.Instance.TryDeliverOn(mail, connection, transaction))
            {
                transaction.Rollback();
                return;
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            MailManager.Instance.DiscardUnpersisted(mail);
            Logger.Error(ex, "Hero {0} mail for {1} failed", candidateMail ? "candidate" : "reward", characterId);
            return;
        }

        MailManager.Instance.PublishDelivered(mail);
    }

    private static bool TryClaimMailSent(MySqlConnection connection, MySqlTransaction transaction, uint cycleId, uint factionId, uint characterId, bool candidateMail)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = candidateMail
            ? "UPDATE hero_candidates SET candidate_mail_sent=1 WHERE cycle_id=@c AND faction_id=@f AND character_id=@ch AND candidate_mail_sent=0"
            : "UPDATE hero_candidates SET reward_mail_sent=1 WHERE cycle_id=@c AND faction_id=@f AND character_id=@ch AND reward_mail_sent=0";
        command.Parameters.AddWithValue("@c", cycleId);
        command.Parameters.AddWithValue("@f", factionId);
        command.Parameters.AddWithValue("@ch", characterId);
        command.Prepare();
        return command.ExecuteNonQuery() == 1;
    }

    private static BaseMail BuildCandidateMail(uint characterId, HeroCondition condition, HeroCycle cycle)
    {
        var name = NameManager.Instance.GetCharacterName(characterId);
        if (name == null)
            return null;

        var abstain = PhaseWindow(cycle, HeroPhase.HeroAbstain);
        var mail = new BaseMail
        {
            MailType = MailType.HeroCandidateAlarm,
            Title = HeroMailWire.LocaleTitle,
            ReceiverName = name
        };
        mail.Header.SenderName = HeroMailWire.CandidateSender;
        mail.Header.ReceiverId = characterId;
        mail.Header.Status = MailStatus.Unread;
        mail.Body.Text = HeroMailWire.FormatPeriodBody(condition.CandidateMailBody, abstain.Start, abstain.End);
        mail.Body.RecvDate = DateTime.UtcNow;
        return mail;
    }

    private static BaseMail BuildRewardMail(uint characterId, HeroReward reward, HeroCondition condition, HeroCycle cycle)
    {
        var name = NameManager.Instance.GetCharacterName(characterId);
        if (name == null)
            return null;

        var activity = PhaseWindow(cycle, HeroPhase.HeroPeriod);
        var mail = new BaseMail
        {
            MailType = MailType.HeroElectionItem,
            Title = HeroMailWire.LocaleTitle,
            ReceiverName = name
        };
        mail.Header.SenderName = HeroMailWire.ElectionSender;
        mail.Header.ReceiverId = characterId;
        mail.Header.Status = MailStatus.Unread;
        mail.Body.Text = HeroMailWire.FormatPeriodBody(
            condition?.ElectionMailBody ?? string.Empty, activity.Start, activity.End);
        mail.Body.RecvDate = DateTime.UtcNow;

        var itemSet = ItemManager.Instance.GetItemSet(reward.ItemSetId);
        if (itemSet != null)
        {
            foreach (var setItem in itemSet.Items.Values)
            {
                var item = ItemManager.Instance.Create(setItem.ItemId, setItem.Count, 0, true);
                if (item != null)
                    mail.Body.Attachments.Add(item);
            }
        }

        return mail;
    }
}
