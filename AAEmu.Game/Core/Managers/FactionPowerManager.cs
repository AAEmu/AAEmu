using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Weekly faction activity ("faction power") of Nuia, Haranya and the Outlaws, earned and weighted as
/// faction_power_records and member_weights say: sieges, faction competitions, quests of the listed groups,
/// raid bosses (allied factions taking part share in it) and exiles taken in. Part of last week's siege and
/// raid points carries over (faction_power_siege_maintain / _raid_maintain percent). Last week's activity
/// decides the superior and inferior factions; members of an inferior faction carry faction_inferior_buff,
/// and the exile system reads the same standings.
/// </summary>
public class FactionPowerManager : Singleton<FactionPowerManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static ContentConfigGameData Config => ContentConfigGameData.Instance;
    private static int SiegeMaintainPercent => Config.GetInt("faction_power_siege_maintain", 50);
    private static int RaidMaintainPercent => Config.GetInt("faction_power_raid_maintain", 70);
    private static int RelationFactor => Config.GetInt("power_relation_factor", 110);
    private static int Qualifier => Config.GetInt("power_qualifier", 1000);
    private static uint InferiorBuffId => (uint)Config.GetInt("faction_inferior_buff", 25861);
    private static FactionPowerGameData Data => FactionPowerGameData.Instance;

    private readonly object _sync = new();
    private readonly Dictionary<uint, FactionPowerScore> _current = [];
    private readonly Dictionary<uint, FactionPowerScore> _previous = [];
    private readonly Dictionary<uint, FactionPowerStanding> _overrides = [];
    private readonly Dictionary<(uint Week, uint FactionId), FactionPowerScore> _dirty = [];
    private readonly List<SiegeAward> _pendingSiegeAwards = [];
    private readonly List<(uint FactionId, ushort ZoneGroupId, DateTime CycleWeekStart)> _unscoredSiegeWins = [];
    private bool _reclassified;
    private HashSet<uint> _superiors = [];
    private HashSet<uint> _inferiors = [];
    private uint _week;
    private int _started;
    private int _subscribed;

    /// <summary>Raised after the standings were published (week rollover or a GM change).</summary>
    public event Action StandingsChanged;

    public bool IsStarted => Volatile.Read(ref _started) != 0;

    public uint Week { get { lock (_sync) return _week; } }

    public IReadOnlyCollection<uint> Superiors { get { lock (_sync) return _superiors.ToList(); } }

    public IReadOnlyCollection<uint> Inferiors { get { lock (_sync) return _inferiors.ToList(); } }

    public IReadOnlyDictionary<uint, FactionPowerStanding> Overrides { get { lock (_sync) return new Dictionary<uint, FactionPowerStanding>(_overrides); } }

    public bool IsSuperior(uint factionId) { lock (_sync) return _superiors.Contains(factionId); }

    public bool IsInferior(uint factionId) { lock (_sync) return _inferiors.Contains(factionId); }

    /// <summary>The root factions that earn activity: the system diplomacy factions.</summary>
    public static IReadOnlyCollection<uint> PowerFactions()
    {
        return FactionManager.Instance.GetSystemFactions()
            .Where(f => f.DiplomacyTarget)
            .Select(f => (uint)f.Id)
            .Distinct()
            .ToList();
    }

    /// <summary>The root (mother chain) faction of a character's current faction.</summary>
    public static uint RootFactionOf(Character character)
    {
        if (character?.Faction == null)
            return 0;
        var root = character.Faction;
        for (var depth = 0; depth < 8 && root.MotherId != 0; depth++)
        {
            var mother = FactionManager.Instance.GetFaction(root.MotherId);
            if (mother == null)
                break;
            root = mother;
        }

        return (uint)root.Id;
    }

    /// <summary>The power faction a character's activity counts for, or 0.</summary>
    public static uint PowerFactionOf(Character character)
    {
        var rootId = RootFactionOf(character);
        return PowerFactions().Contains(rootId) ? rootId : 0;
    }

    /// <summary>
    /// Starts scoring once the main world exists. Nothing is scored or saved before this and last week are
    /// loaded; a load that fails is retried every minute rather than starting empty over the stored scores.
    /// </summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _subscribed, 1) != 0)
            return;

        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMinutes(1));
        var mainWorld = WorldManager.Instance.GetWorld(WorldManager.DefaultInstanceId);
        if (mainWorld != null)
            mainWorld.Events.OnUnitKilled += OnUnitKilled;
        TryLoad();
    }

    private bool TryLoad()
    {
        lock (_sync)
        {
            if (IsStarted)
                return true;
            try
            {
                var week = FactionPowerRules.WeekNumber(DateTime.UtcNow);
                var previous = LoadWeek(week - 1);
                var current = LoadWeek(week);
                var overrides = LoadOverrides();

                _week = week;
                foreach (var score in previous)
                    _previous[score.FactionId] = score;
                foreach (var score in current)
                    _current[score.FactionId] = score;
                foreach (var (factionId, standing) in overrides)
                    _overrides[factionId] = standing;
                ClassifyNoLock();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "Faction power: could not load the weekly scores, retrying in a minute");
                return false;
            }

            Volatile.Write(ref _started, 1);
            Logger.Info("Faction power: week {0}, superior [{1}], inferior [{2}]",
                _week, string.Join(",", _superiors), string.Join(",", _inferiors));
        }

        return true;
    }

    /// <summary>This week's row for a faction, created with last week's carry-over on first use.</summary>
    private FactionPowerScore RowNoLock(uint factionId)
    {
        if (_current.TryGetValue(factionId, out var score))
            return score;

        score = FactionPowerRules.NewWeekRow(_week, factionId, _previous.GetValueOrDefault(factionId),
            SiegeMaintainPercent, RaidMaintainPercent);
        _current[factionId] = score;
        _dirty[(score.WeekNum, factionId)] = score;
        return score;
    }

    private void Add(uint factionId, Action<FactionPowerScore> change)
    {
        if (factionId == 0 || !IsStarted || !PowerFactions().Contains(factionId))
            return;
        lock (_sync)
        {
            var row = RowNoLock(factionId);
            change(row);
            _dirty[(row.WeekNum, factionId)] = row;
        }
    }

    private static uint Round(double points) => (uint)Math.Max(0, Math.Round(points));

    private sealed record SiegeAward(uint FactionId, ushort ZoneGroupId, DateTime CycleWeekStart, uint Points, uint Week);

    /// <summary>
    /// Scores a siege win once per settled cycle: the award marker and the new points are committed together,
    /// so a replayed settlement does not score twice. An award that cannot be saved is retried on the next tick.
    /// A win settled before the scores are loaded (a siege that ended while the server was down) is scored
    /// once they are, for the week it was fought in.
    /// </summary>
    public void AddSiegeWin(uint factionId, ushort zoneGroupId, DateTime cycleWeekStart)
    {
        if (!IsStarted)
        {
            lock (_sync)
                _unscoredSiegeWins.Add((factionId, zoneGroupId, cycleWeekStart));
            return;
        }

        if (!PowerFactions().Contains(factionId))
            return;
        var record = Data.GetRecord(FactionPowerRecordType.Siege);
        var members = Math.Max(1, SiegeManager.Instance.GetRaidTeamMembers(zoneGroupId, factionId)?.Count ?? 0);
        var points = Round(Data.Points(record, members));
        var siegeEnd = SiegeGameData.Instance.GetSiegeZoneSchedule(zoneGroupId)?.SiegeEnd(cycleWeekStart);
        var award = new SiegeAward(factionId, zoneGroupId, cycleWeekStart, points,
            FactionPowerRules.SiegeAwardWeek(siegeEnd, DateTime.UtcNow));

        lock (_sync)
        {
            if (!TryRecordSiegeAwardNoLock(award))
                _pendingSiegeAwards.Add(award);
        }
    }

    private void ScoreUnscoredSiegeWins()
    {
        List<(uint FactionId, ushort ZoneGroupId, DateTime CycleWeekStart)> wins;
        lock (_sync)
        {
            wins = _unscoredSiegeWins.ToList();
            _unscoredSiegeWins.Clear();
        }

        foreach (var (factionId, zoneGroupId, cycleWeekStart) in wins)
            AddSiegeWin(factionId, zoneGroupId, cycleWeekStart);
    }

    /// <summary>
    /// Saves the award marker and adds the points to the week the siege was won in, which a retry after the
    /// weekly rollover keeps: last week's row, this week's carried siege points and the standings follow it.
    /// </summary>
    /// <returns>False when the award could not be saved and should be tried again.</returns>
    private bool TryRecordSiegeAwardNoLock(SiegeAward award)
    {
        var (defer, won, carried) = FactionPowerRules.PlanSiegeAward(award.Week, award.Points, _week,
            _previous.GetValueOrDefault(award.FactionId), RowNoLock(award.FactionId), SiegeMaintainPercent);
        if (defer)
            return false;

        try
        {
            using var connection = MySQL.CreateConnection();
            using var transaction = connection.BeginTransaction();
            using (var marker = connection.CreateCommand())
            {
                marker.Transaction = transaction;
                marker.CommandText = """
                    INSERT IGNORE INTO faction_power_siege_awards (zone_group_id, cycle_week_start, faction_id, week_num, points)
                    VALUES (@zone, @cycle, @faction, @week, @points)
                    """;
                marker.Parameters.AddWithValue("@zone", award.ZoneGroupId);
                marker.Parameters.AddWithValue("@cycle", award.CycleWeekStart);
                marker.Parameters.AddWithValue("@faction", award.FactionId);
                marker.Parameters.AddWithValue("@week", award.Week);
                marker.Parameters.AddWithValue("@points", award.Points);
                if (marker.ExecuteNonQuery() == 0)
                    return true;
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                if (won != null)
                {
                    WriteRow(command, won);
                }
                else
                {
                    command.CommandText = """
                        INSERT INTO faction_power_scores (week_num, faction_id, siege_point) VALUES (@week, @faction, @points)
                        ON DUPLICATE KEY UPDATE siege_point = siege_point + @points
                        """;
                    command.Parameters.AddWithValue("@week", award.Week);
                    command.Parameters.AddWithValue("@faction", award.FactionId);
                    command.Parameters.AddWithValue("@points", award.Points);
                    command.ExecuteNonQuery();
                }
            }

            if (carried != null)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                WriteRow(command, carried);
            }

            transaction.Commit();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Faction power: siege win of faction {0} in zone group {1} not saved, retrying on the next tick",
                award.FactionId, award.ZoneGroupId);
            return false;
        }

        if (award.Week == _week)
        {
            var row = RowNoLock(award.FactionId);
            row.SiegePoint += award.Points;
            _dirty[(row.WeekNum, award.FactionId)] = row;
        }
        else if (carried != null)
        {
            if (!_previous.TryGetValue(award.FactionId, out var last))
                _previous[award.FactionId] = last = new FactionPowerScore { WeekNum = award.Week, FactionId = award.FactionId };
            last.SiegePoint += award.Points;
            _dirty[(last.WeekNum, award.FactionId)] = last;
            RowNoLock(award.FactionId).SiegeMaintainPoint = carried.SiegeMaintainPoint;

            ClassifyNoLock();
            _reclassified = true;
        }

        return true;
    }

    public void AddCompetitionResult(uint competitionId, uint winnerFactionId)
    {
        var record = Data.GetRecord(FactionPowerRecordType.Competition, competitionId);
        if (record != null)
            Add(winnerFactionId, s => s.CompetitionPoint += Round(Data.Points(record, 1)));
    }

    public void AddQuest(Character character, uint questId)
    {
        if (!IsStarted)
            return;
        foreach (var record in Data.GetRecords(FactionPowerRecordType.Quest))
        {
            if (record.TargetType == "QuestContextGroup" && QuestManager.Instance.CheckContextGroup(record.TargetId, questId))
            {
                var points = (float)Data.Points(record, 1);
                Add(PowerFactionOf(character), s => s.QuestPoint += points);
                return;
            }
        }
    }

    public void AddExile(uint toFactionId)
    {
        var record = Data.GetRecord(FactionPowerRecordType.FactionChange);
        if (record != null)
            Add(toFactionId, s => s.ImmigratePoint += Round(Data.Points(record, 1)));
    }

    /// <summary>A listed raid boss died: the killer's faction scores it, allied factions that took part share in it.</summary>
    private void OnUnitKilled(object sender, OnUnitKilledArgs args)
    {
        if (args.Victim is not Npc boss)
            return;
        var record = Data.GetRecord(FactionPowerRecordType.Raid, boss.TemplateId);
        if (record == null)
            return;

        var killer = args.Killer as Character
                     ?? (args.Killer is Mate mate ? WorldManager.Instance.GetCharacterByObjId(mate.OwnerObjId) : null)
                     ?? (args.Killer as Slave)?.Summoner as Character;
        var killerFaction = PowerFactionOf(killer);
        if (killerFaction == 0)
            return;

        var participants = boss.AggroTable.Keys
            .Select(objId => WorldManager.Instance.GetCharacterByObjId(objId))
            .Append(killer)
            .Where(c => c != null)
            .Distinct()
            .GroupBy(PowerFactionOf)
            .Where(g => g.Key != 0)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (factionId, count) in participants)
        {
            var points = Round(Data.Points(record, count));
            if (factionId == killerFaction)
                Add(factionId, s => s.RaidPoint += points);
            else if (AreAllied(factionId, killerFaction))
                Add(factionId, s => s.RaidJointPoint += points);
        }

        Logger.Info("Faction power: raid boss {0} killed by {1}, participants {2}", boss.TemplateId, killer.Name,
            string.Join(", ", participants.Select(p => $"{p.Key}x{p.Value}")));
    }

    private static bool AreAllied(uint first, uint second)
    {
        var now = DateTime.UtcNow;
        return FactionDiplomacyManager.Instance.Agreements.Any(a =>
            a.Involves(first) && a.Involves(second) && !FactionDiplomacyRules.IsExpired(a, now));
    }

    /// <summary>GM change of one counter by name; set replaces the value instead of adding to it.</summary>
    public bool ChangePoints(uint factionId, string field, int amount, bool set)
    {
        static uint Apply(uint value, int amount, bool set) => set ? (uint)Math.Max(0, amount) : (uint)Math.Max(0, value + (long)amount);
        Action<FactionPowerScore> change = field.ToLowerInvariant() switch
        {
            "siege" => s => s.SiegePoint = Apply(s.SiegePoint, amount, set),
            "siegemaintain" => s => s.SiegeMaintainPoint = Apply(s.SiegeMaintainPoint, amount, set),
            "competition" => s => s.CompetitionPoint = Apply(s.CompetitionPoint, amount, set),
            "quest" => s => s.QuestPoint = set ? Math.Max(0, amount) : Math.Max(0, s.QuestPoint + amount),
            "raid" => s => s.RaidPoint = Apply(s.RaidPoint, amount, set),
            "raidmaintain" => s => s.RaidMaintainPoint = Apply(s.RaidMaintainPoint, amount, set),
            "raidjoint" => s => s.RaidJointPoint = Apply(s.RaidJointPoint, amount, set),
            "exile" or "immigrate" => s => s.ImmigratePoint = Apply(s.ImmigratePoint, amount, set),
            _ => null
        };
        if (change == null || !PowerFactions().Contains(factionId))
            return false;
        Add(factionId, change);
        return true;
    }

    public void SetOverride(uint factionId, FactionPowerStanding standing)
    {
        if (!IsStarted)
            return;
        lock (_sync)
        {
            if (standing == FactionPowerStanding.Automatic)
                _overrides.Remove(factionId);
            else
                _overrides[factionId] = standing;
            SaveOverrides();
            ClassifyNoLock();
        }

        Broadcast();
    }

    /// <summary>This week's scores for every power faction.</summary>
    public List<FactionPowerScore> CurrentScores()
    {
        lock (_sync)
            return PowerFactions().Select(f => RowNoLock(f).Clone()).ToList();
    }

    public List<FactionPowerScore> PreviousScores()
    {
        lock (_sync)
        {
            return PowerFactions().Select(f => _previous.TryGetValue(f, out var s)
                ? s.Clone()
                : new FactionPowerScore { WeekNum = _week - 1, FactionId = f }).ToList();
        }
    }

    public SCFactionPowerScorePacket BuildPacket()
    {
        var scores = CurrentScores();
        lock (_sync)
        {
            return new SCFactionPowerScorePacket(_superiors.ToList(), _inferiors.ToList(), _week, scores, []);
        }
    }

    public void OnLogin(Character character)
    {
        if (character == null || !IsStarted)
            return;
        character.SendPacket(BuildPacket());
        ApplyInferiorBuff(character);
    }

    public void Broadcast()
    {
        if (!IsStarted)
            return;
        WorldManager.Instance.BroadcastPacketToServer(BuildPacket());
        foreach (var character in WorldManager.Instance.GetAllCharacters())
            ApplyInferiorBuff(character);
        StandingsChanged?.Invoke();
    }

    public void ApplyInferiorBuff(Character character)
    {
        var buffId = InferiorBuffId;
        if (buffId == 0 || character == null)
            return;
        var inferior = IsInferior(PowerFactionOf(character));
        var has = character.Buffs.CheckBuff(buffId);
        if (inferior && !has)
            character.Buffs.AddBuff(buffId, character);
        else if (!inferior && has)
            character.Buffs.RemoveBuff(buffId);
    }

    /// <summary>Re-ranks from this week's points instead of last week's, for testing.</summary>
    public void RankFromCurrentWeek()
    {
        lock (_sync)
            ClassifyNoLock(PowerFactions().ToDictionary(f => f, f => FactionPowerRules.Total(RowNoLock(f))));
        Broadcast();
    }

    private void ClassifyNoLock(IReadOnlyDictionary<uint, int> totals = null)
    {
        totals ??= PowerFactions().ToDictionary(f => f, f => FactionPowerRules.Total(_previous.GetValueOrDefault(f)));
        var (superiors, inferiors) = FactionPowerRules.Classify(totals, Qualifier, RelationFactor);
        FactionPowerRules.ApplyOverrides(superiors, inferiors, _overrides);
        (_superiors, _inferiors) = (superiors, inferiors);
    }

    private void Tick(TimeSpan delta)
    {
        var loaded = false;
        if (!IsStarted)
        {
            if (!TryLoad())
                return;
            loaded = true;
        }

        ScoreUnscoredSiegeWins();
        var week = FactionPowerRules.WeekNumber(DateTime.UtcNow);
        var rolledOver = false;
        bool changed;
        bool reclassified;
        lock (_sync)
        {
            if (week != _week)
            {
                foreach (var factionId in PowerFactions())
                    RowNoLock(factionId);
                SaveNoLock();
                _previous.Clear();
                foreach (var (factionId, score) in _current)
                    _previous[factionId] = score;
                _current.Clear();
                _week = week;
                ClassifyNoLock();
                rolledOver = true;
            }

            if (_pendingSiegeAwards.Count > 0)
                _pendingSiegeAwards.RemoveAll(TryRecordSiegeAwardNoLock);
            changed = _dirty.Count > 0;
            SaveNoLock();

            reclassified = _reclassified;
            _reclassified = false;
        }

        if (!rolledOver && !reclassified && !loaded)
        {
            if (changed)
                WorldManager.Instance.BroadcastPacketToServer(BuildPacket());
            return;
        }

        if (rolledOver)
        {
            Logger.Info("Faction power: week {0} started, superior [{1}], inferior [{2}]",
                week, string.Join(",", Superiors), string.Join(",", Inferiors));
        }

        Broadcast();
    }

    /// <summary>Writes the changed scores now, e.g. before the server stops.</summary>
    public void Save()
    {
        if (!TryLoad())
        {
            lock (_sync)
                LogLostSiegeAwardsNoLock();
            return;
        }

        ScoreUnscoredSiegeWins();
        lock (_sync)
        {
            _pendingSiegeAwards.RemoveAll(TryRecordSiegeAwardNoLock);
            LogLostSiegeAwardsNoLock();
            SaveNoLock();
        }
    }

    private void LogLostSiegeAwardsNoLock()
    {
        foreach (var (factionId, zoneGroupId, cycleWeekStart) in _unscoredSiegeWins)
        {
            Logger.Error("Faction power: siege win of faction {0} in zone group {1} (cycle {2:yyyy-MM-dd}) was not scored",
                factionId, zoneGroupId, cycleWeekStart);
        }

        foreach (var award in _pendingSiegeAwards)
        {
            Logger.Error("Faction power: siege win of faction {0} in zone group {1} (cycle {2:yyyy-MM-dd}, week {3}, {4} points) was not saved",
                award.FactionId, award.ZoneGroupId, award.CycleWeekStart, award.Week, award.Points);
        }
    }

    /// <summary>Writes every changed row, of this week or an earlier one; rows that fail stay queued for the next tick.</summary>
    private void SaveNoLock()
    {
        if (_dirty.Count == 0)
            return;
        try
        {
            using var connection = MySQL.CreateConnection();
            foreach (var (key, s) in _dirty.ToList())
            {
                using var command = connection.CreateCommand();
                WriteRow(command, s);
                _dirty.Remove(key);
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Faction power: saving week {0} failed, will retry", _week);
        }
    }

    private static void WriteRow(MySqlCommand command, FactionPowerScore s)
    {
        command.CommandText = """
            REPLACE INTO faction_power_scores (week_num, faction_id, siege_point, siege_maintain_point,
                competition_point, quest_point, raid_point, raid_maintain_point, raid_joint_point, immigrate_point)
            VALUES (@week, @faction, @siege, @maintain, @competition, @quest, @raid, @raidMaintain, @raidJoint, @immigrate)
            """;
        command.Parameters.AddWithValue("@week", s.WeekNum);
        command.Parameters.AddWithValue("@faction", s.FactionId);
        command.Parameters.AddWithValue("@siege", s.SiegePoint);
        command.Parameters.AddWithValue("@maintain", s.SiegeMaintainPoint);
        command.Parameters.AddWithValue("@competition", s.CompetitionPoint);
        command.Parameters.AddWithValue("@quest", s.QuestPoint);
        command.Parameters.AddWithValue("@raid", s.RaidPoint);
        command.Parameters.AddWithValue("@raidMaintain", s.RaidMaintainPoint);
        command.Parameters.AddWithValue("@raidJoint", s.RaidJointPoint);
        command.Parameters.AddWithValue("@immigrate", s.ImmigratePoint);
        command.ExecuteNonQuery();
    }

    private static Dictionary<uint, FactionPowerStanding> LoadOverrides()
    {
        var overrides = new Dictionary<uint, FactionPowerStanding>();
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT faction_id, status FROM faction_power_overrides";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            overrides[reader.GetUInt32("faction_id")] = (FactionPowerStanding)reader.GetByte("status");
        return overrides;
    }

    private void SaveOverrides()
    {
        using var connection = MySQL.CreateConnection();
        using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "DELETE FROM faction_power_overrides";
            clear.ExecuteNonQuery();
        }

        foreach (var (factionId, standing) in _overrides)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO faction_power_overrides (faction_id, status) VALUES (@f, @s)";
            command.Parameters.AddWithValue("@f", factionId);
            command.Parameters.AddWithValue("@s", (byte)standing);
            command.ExecuteNonQuery();
        }
    }

    private static List<FactionPowerScore> LoadWeek(uint week)
    {
        var scores = new List<FactionPowerScore>();
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM faction_power_scores WHERE week_num = @week";
        command.Parameters.AddWithValue("@week", week);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            scores.Add(new FactionPowerScore
            {
                WeekNum = reader.GetUInt32("week_num"),
                FactionId = reader.GetUInt32("faction_id"),
                SiegePoint = reader.GetUInt32("siege_point"),
                SiegeMaintainPoint = reader.GetUInt32("siege_maintain_point"),
                CompetitionPoint = reader.GetUInt32("competition_point"),
                QuestPoint = reader.GetFloat("quest_point"),
                RaidPoint = reader.GetUInt32("raid_point"),
                RaidMaintainPoint = reader.GetUInt32("raid_maintain_point"),
                RaidJointPoint = reader.GetUInt32("raid_joint_point"),
                ImmigratePoint = reader.GetUInt32("immigrate_point")
            });
        }

        return scores;
    }
}
