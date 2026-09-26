using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Sieges;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Sieges;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Ticks the siege-cycle state machine (enum_siege_periods) for every claimed Dominion off the recurring
/// siege_zones/siege_plans schedule in SiegeGameData, gates DeclareDominion to its real declare-window, and owns
/// raid-team registration (the <c>siege_raid_team_members</c> table).
///
/// Score and settlement: the guard tower's magic power a side has purified, destroyed or kept is the siege score
/// (the three counters of <c>siege_scores</c>), each side's win point is a content_configs row, and the dominion
/// changes hands when the siege period ends with an attacker over its win point. Who produces the magic-power
/// score is the guard-tower runtime, which is not part of this slice - see Docs/W08A_SIEGE_SCORE.md.
///
/// Raid-commander election (SCElectSiegeRaidOwnerPacket) is NOT built - a separate voting subsystem similar to
/// Hero's, lower priority, not done here.
/// </summary>
public class SiegeManager(ITaskManager taskManager, IDominionManager dominionManager, ISiegeScoreStore scoreStore)
    : Singleton<SiegeManager>, ISiegeManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>GM testing override - see ISiegeManager.ToggleDeclareWindowOverride. Not persisted.</summary>
    private readonly HashSet<uint> _forcedOpenDeclareWindows = [];

    /// <summary>Kept in memory - the siege_offense_hq_user relation is tested once per unit per area-trigger pass.</summary>
    private readonly SiegeOffenseRosterCache _offenseRosters = new(ReadOffenseRosterFromDatabase);

    public void Load()
    {
        // Run once shortly after boot to correct any drift from server downtime, then every minute -
        // siege phase boundaries are minute-granular (siege_zones.start_*_min), a coarser interval could
        // miss a short phase entirely (e.g. the 1-hour siege window in the shipped data).
        taskManager.Schedule(new SiegeTickTask(), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1));
    }

    public SiegePeriod GetScheduledPeriod(uint zoneGroupId, DateTime atUtc)
    {
        var schedule = SiegeGameData.Instance.GetSiegeZoneSchedule(zoneGroupId);
        var weekStart = SiegeGameData.Instance.GetCurrentCycleWeekStart(zoneGroupId, atUtc);
        return SiegeScheduleRules.GetScheduledPeriod(schedule, weekStart, atUtc);
    }

    public bool ToggleDeclareWindowOverride(uint zoneGroupId)
    {
        var nowForced = !_forcedOpenDeclareWindows.Remove(zoneGroupId);
        if (nowForced)
            _forcedOpenDeclareWindows.Add(zoneGroupId);
        return nowForced;
    }

    public bool IsDeclareDominionWindowOpen(uint zoneGroupId, DateTime atUtc)
    {
        if (_forcedOpenDeclareWindows.Contains(zoneGroupId))
            return true;

        var schedule = SiegeGameData.Instance.GetSiegeZoneSchedule(zoneGroupId);
        var weekStart = SiegeGameData.Instance.GetCurrentCycleWeekStart(zoneGroupId, atUtc);
        return SiegeScheduleRules.IsDeclareWindowOpen(schedule, weekStart, atUtc);
    }

    public void Tick()
    {
        var now = DateTime.UtcNow;
        var changed = 0;
        var settled = 0;
        var failed = 0;
        var unsettleable = 0;
        foreach (var dominion in dominionManager.Dominions)
        {
            var period = GetScheduledPeriod(dominion.ZoneId, now);
            var previous = dominion.SiegeTimers.SiegePeriod;
            if (previous == (byte)period)
                continue;

            // A siege settles the moment its period ends, and that is the only moment it can: a cycle that
            // never reached the Siege period (a declare window that ran straight back to peace) has no
            // transition, so it never produces a winner either.
            if (previous == (byte)SiegePeriod.Siege && period != SiegePeriod.Siege)
            {
                switch (SettleZoneGroup(dominion.ZoneId, now))
                {
                    case SiegeSettlementResult.Settled:
                        settled++;
                        break;
                    case SiegeSettlementResult.Failed:
                        // The phase is left where it is on purpose. Writing the new period first would end the
                        // siege for good and leave the dominion unwon for ever, with nothing to notice the
                        // settlement never happened; this way the next tick tries again. Only a transient
                        // fault reaches here, so the retry is bounded by the fault clearing. The reason is
                        // already logged by the settle itself.
                        failed++;
                        continue;
                    case SiegeSettlementResult.Unsettleable:
                        // Content that cannot produce an outcome will not produce one on the next tick either,
                        // so holding the period would keep this zone group in Siege for ever and log every
                        // tick. The period advances and the dominion is left exactly as it stands; the fault
                        // is logged at Error by the settle itself.
                        unsettleable++;
                        break;
                    case SiegeSettlementResult.AlreadySettled:
                        settled++;
                        break;
                }
            }

            dominionManager.UpdateSiegePeriod(dominion.ZoneId, (byte)period);
            changed++;
        }

        if (changed > 0 || failed > 0 || unsettleable > 0)
            Logger.Info("SiegeManager.Tick: {0} dominion(s) changed siege period, {1} settled, " +
                "{2} deferred on a transient fault, {3} advanced past content that cannot settle",
                changed, settled, failed, unsettleable);
    }

    public void RegisterForRaidTeam(GameConnection connection, ushort zoneId, bool isOffense)
    {
        var character = connection?.ActiveChar;
        if (character == null)
            return;

        // Volunteering to fight a siege is a HeroVolunteer/ReadyToSiege-phase action, not something you can
        // sign up for mid-siege or during ordinary Peace.
        var period = GetScheduledPeriod(zoneId, DateTime.UtcNow);
        if (period is not (SiegePeriod.HeroVolunteer or SiegePeriod.ReadyToSiege))
        {
            // No dedicated "wrong raid-register period" error is shipped; reuse the existing timing message.
            character.SendErrorMessage(Models.Game.ErrorMessageType.SiegeDeclareBadPeriod);
            return;
        }

        using var connection2 = MySQL.CreateConnection();
        using var command = connection2.CreateCommand();
        command.CommandText = "REPLACE INTO siege_raid_team_members (zone_id, character_id, is_offense) VALUES (@z,@c,@o)";
        command.Parameters.AddWithValue("@z", zoneId);
        command.Parameters.AddWithValue("@c", character.Id);
        command.Parameters.AddWithValue("@o", isOffense);
        command.Prepare();
        command.ExecuteNonQuery();

        _offenseRosters.Invalidate(zoneId);

        // The client finds its dominion record by the zone group and then the team by the alliance the
        // character fights for, so both are sent as themselves: a score/member packet keyed on anything else
        // (a literal 0, the zone group in the team slot) is looked up, misses, and is dropped client-side.
        character.SendPacket(new SCSiegeMemberPacket(zoneId, (int)AllianceOfFaction(character), character.Id, true));
    }

    /// <summary>
    /// Answers the siege registration popup: who is registered in the character's current zone
    /// group and whether the requester is one of them. Ranking is the registration order, which is
    /// the only order the registration table carries.
    /// </summary>
    public void SendRaidTeamRegisterList(GameConnection connection)
    {
        var character = connection?.ActiveChar;
        if (character == null)
            return;

        var zoneGroupId = ZoneManager.Instance.GetZoneByKey(character.Transform.ZoneId)?.GroupId ?? 0;

        var rows = new List<SiegeRaidRegisterRow>();
        var registered = false;
        using (var sql = MySQL.CreateConnection())
        {
            using var command = sql.CreateCommand();
            command.CommandText = SiegeRaidTeamQueries.RegisterListSql;
            command.Parameters.AddWithValue("@z", zoneGroupId);
            command.Prepare();
            using var reader = command.ExecuteReader();
            uint ranking = 0;
            while (reader.Read())
            {
                var charId = reader.GetUInt32("character_id");
                ranking++;
                if (charId == character.Id)
                    registered = true;
                rows.Add(new SiegeRaidRegisterRow(ranking,
                    reader.IsDBNull(reader.GetOrdinal("name")) ? string.Empty : reader.GetString("name"),
                    charId));
            }
        }

        character.SendPacket(new SCSiegeRaidRegisterListPacket(registered, true, (ushort)zoneGroupId,
            [new SiegeRaidRegisterZone((int)zoneGroupId, rows)]));
    }

    /// <summary>
    /// One faction's raid team for a zone group's siege, as the member list shows it: the members that faction
    /// registered, with what each of them is and how well they are geared.
    /// </summary>
    /// <remarks>
    /// A member who is in the world is read live. A member who is not is read from the database: their heir
    /// level is their stored heir experience, and their gear score is their stored equipment scored the same way
    /// a live character's is — the loader is the one an inventory load uses, so an offline member is listed with
    /// the gear they logged out in rather than as a zero.
    /// </remarks>
    public List<SiegeRaidTeamMemberInfo> GetRaidTeamMembers(ushort zoneId, uint factionId)
    {
        var members = new List<SiegeRaidTeamMemberInfo>();

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SiegeRaidTeamQueries.MembersSql;
        command.Parameters.AddWithValue("@z", zoneId);
        command.Prepare();

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var characterId = reader.GetUInt32("id");
            if (AllianceOf(reader.GetUInt32("faction_id")) != factionId)
                continue;

            var name = reader.IsDBNull(reader.GetOrdinal("name")) ? string.Empty : reader.GetString("name");
            var level = (byte)Math.Clamp(reader.GetInt32("level"), 0, byte.MaxValue);
            var ability1 = (byte)Math.Clamp(reader.GetInt32("ability1"), 0, byte.MaxValue);
            var ability2 = (byte)Math.Clamp(reader.GetInt32("ability2"), 0, byte.MaxValue);
            var ability3 = (byte)Math.Clamp(reader.GetInt32("ability3"), 0, byte.MaxValue);

            var live = WorldManager.Instance.GetCharacterById(characterId);
            if (live != null)
            {
                members.Add(new SiegeRaidTeamMemberInfo(characterId, live.Name, (byte)live.Level,
                    live.HeirLevel, (byte)live.Ability1, (byte)live.Ability2, (byte)live.Ability3,
                    (uint)Math.Max(0, live.GearScore)));
                continue;
            }

            var heirExp = reader.GetInt64("heir_exp");
            members.Add(new SiegeRaidTeamMemberInfo(characterId, name, level,
                (byte)HeirGameData.Instance.GetLevelForExp(heirExp), ability1, ability2, ability3,
                OfflineGearScore(characterId)));
        }

        return members;
    }

    /// <summary>
    /// The gear score of a member who is not in the world, scored from their stored equipment.
    /// </summary>
    /// <remarks>
    /// Every persistent container is loaded at startup, so this is a lookup in that set - read-only, and it
    /// answers null for a character who has none. Not <c>GetItemContainerForCharacter</c>: that one builds an
    /// empty container with a fresh id and registers it for the next save, which then writes an empty
    /// <c>item_containers</c> row for that character. Not <c>ItemManager.LoadPlayerInventory</c> either: that
    /// one is obsolete and reads the in-memory item cache, which holds only loaded characters, so it answers an
    /// empty set for exactly the members this is for.
    /// Scored with the same per-piece calculation a live character's total uses, so a member's number does not
    /// change depending on whether they are online.
    /// </remarks>
    private static uint OfflineGearScore(uint characterId)
    {
        try
        {
            var equipment = ItemManager.Instance.FindItemContainerFor(characterId,
                Models.Game.Items.SlotType.Equipment, 0);
            if (equipment == null)
            {
                Logger.Warn("No equipment container for offline character {0} - gear score listed as 0", characterId);
                return 0;
            }

            var gains = SlotItemLevelGains(characterId);
            var parts = GearScoreCalculator.Sum(equipment.Items,
                slot => gains.GetValueOrDefault((byte)slot));
            return (uint)Math.Max(0, GearScoreCalculator.TruncatedTotal(parts));
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Could not score the gear of offline character {0} - listed as 0", characterId);
            return 0;
        }
    }

    /// <summary>
    /// Ladder gain per reinforced slot for a character who is not loaded. Empty when the table cannot be
    /// read, which leaves those pieces at their template level rather than failing the whole score.
    /// </summary>
    private static Dictionary<byte, float> SlotItemLevelGains(uint characterId)
    {
        var gains = new Dictionary<byte, float>();
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT `slot_type_id`, `level` FROM character_equip_slot_reinforces " +
                "WHERE `owner` = @owner AND `level` > 0";
            command.Parameters.AddWithValue("@owner", characterId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var slot = (byte)reader.GetInt32("slot_type_id");
                var level = (sbyte)reader.GetInt32("level");
                gains[slot] = CharacterEquipSlotReinforces.ItemLevelGainFor(slot, level);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Could not read slot reinforcement for offline character {0}", characterId);
        }

        return gains;
    }

    public void UnregisterFromRaidTeam(GameConnection connection, ushort zoneId)
    {
        var character = connection?.ActiveChar;
        if (character == null)
            return;

        using var conn = MySQL.CreateConnection();
        using var command = conn.CreateCommand();
        command.CommandText = "DELETE FROM siege_raid_team_members WHERE zone_id=@z AND character_id=@c";
        command.Parameters.AddWithValue("@z", zoneId);
        command.Parameters.AddWithValue("@c", character.Id);
        command.Prepare();
        command.ExecuteNonQuery();

        _offenseRosters.Invalidate(zoneId);

        character.SendPacket(
            new SCSiegeMemberPacket(zoneId, (int)AllianceOfFaction(character), character.Id, false));
    }

    public List<SiegeRaidTeam> GetRaidTeams(ushort zoneId)
    {
        var isWaitWar = GetScheduledPeriod(zoneId, DateTime.UtcNow) == SiegePeriod.ReadyToSiege;
        var teams = SiegeRaidTeamRules.Group(GetRaidTeamRoster(zoneId), GetDefenderFactionId(zoneId), isWaitWar);

        // The window has three frames (one defence, two offence). A roster with more factions than that has
        // nowhere to be drawn, so the extra teams are dropped - loudly, because a silent trim would look
        // exactly like a faction that never registered.
        if (teams.Count > SCAllSiegeRaidTeamInfoPacket.MaxTeams)
        {
            Logger.Warn(
                "Zone group {0} has {1} registered raid teams, more than the siege window's {2} frames; sending the first {2}",
                zoneId, teams.Count, SCAllSiegeRaidTeamInfoPacket.MaxTeams);
            teams = teams.Take(SCAllSiegeRaidTeamInfoPacket.MaxTeams).ToList();
        }

        return teams;
    }

    /// <summary>
    /// The faction that holds the ground being fought over - the team the window shows in its defence slot.
    /// Zero when the zone group has no live Dominion claim, in which case there is nothing defending it yet
    /// and no row claims to.
    /// </summary>
    private static uint GetDefenderFactionId(ushort zoneId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT faction_id FROM dominions WHERE zone_id=@z";
        command.Parameters.AddWithValue("@z", zoneId);
        command.Prepare();
        using var reader = command.ExecuteReader();
        return reader.Read() ? reader.GetUInt32(0) : 0u;
    }

    /// <summary>
    /// The characters registered for the zone group's raid team, each with the alliance it fights for - a team
    /// is the registrations of one alliance. Registrations come back in the order they were made, which is the
    /// order the window lists the teams in after the defence.
    /// </summary>
    private static List<SiegeRaidTeamMember> GetRaidTeamRoster(ushort zoneId)
    {
        var roster = new List<SiegeRaidTeamMember>();

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SiegeRaidTeamQueries.RosterSql;
        command.Parameters.AddWithValue("@z", zoneId);
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            roster.Add(new SiegeRaidTeamMember(reader.GetUInt32(0), AllianceOf(reader.GetUInt32(1))));

        return roster;
    }

    /// <summary>
    /// The top-level Nuia/Haranya/pirate alliance a character's own race-based faction belongs to (a faction
    /// with no mother is its own alliance) - the same resolution DominionManager.ResolveOwningFaction uses for
    /// a dominion's owning faction, so a team and the dominion it fights for are named by the same id.
    /// </summary>
    private static uint AllianceOf(uint factionId)
    {
        var faction = FactionManager.Instance.GetFaction((FactionsEnum)factionId);
        return faction != null && faction.MotherId != FactionsEnum.Invalid
            ? (uint)faction.MotherId
            : factionId;
    }

    public IReadOnlySet<uint> GetOffenseRaidTeam(ushort zoneId) => _offenseRosters.Get(zoneId);

    public void ForgetOffenseRaidTeams() => _offenseRosters.InvalidateAll();

    private static HashSet<uint> ReadOffenseRosterFromDatabase(ushort zoneId)
    {
        var roster = new HashSet<uint>();

        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SiegeRaidTeamQueries.OffenseRosterSql;
        command.Parameters.AddWithValue("@z", zoneId);
        command.Prepare();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            roster.Add(reader.GetUInt32(0));

        return roster;
    }

    /// <summary>The alliance a character fights for — the faction whose raid team their registrations join.</summary>
    public uint AllianceOfFaction(Character character) =>
        character?.Faction == null ? 0 : AllianceOf((uint)character.Faction.Id);

    /// <summary>
    /// Adds guard-tower magic power to one side of a zone group's siege score and pushes the new totals to the
    /// client.
    /// </summary>
    /// <remarks>
    /// The caller is the guard-tower runtime: the shipped siege guide makes the purified/destroyed magic power
    /// the score, and nothing else - a PvP kill is not a score event, which is why there is no kill hook here.
    /// The award is refused outside a siege of a zone group that has a <c>siege_zones</c> row, and the amount
    /// is whatever the caller measured, never a literal in this file.
    /// <para/>
    /// The client writes the three counters it is sent rather than adding to them, so this broadcasts the whole
    /// score read back from the row. A delta would leave every client showing the last award as the total.
    /// </remarks>
    /// <returns>The state that was stored, or null when the award was refused.</returns>
    public SiegeScoreState AwardScore(ushort zoneGroupId, SiegeScoreSide side, uint amount)
    {
        if (SiegeGameData.Instance.GetSiegeZoneSchedule(zoneGroupId) == null)
        {
            Logger.Warn("Siege score refused: zone group {0} has no siege_zones schedule", zoneGroupId);
            return null;
        }

        if (GetScheduledPeriod(zoneGroupId, DateTime.UtcNow) != SiegePeriod.Siege)
        {
            Logger.Warn("Siege score refused: zone group {0} is not in its siege period", zoneGroupId);
            return null;
        }

        if (amount == 0)
        {
            Logger.Warn("Siege score refused: {0} side of zone group {1} was awarded 0", side, zoneGroupId);
            return null;
        }

        var state = scoreStore.Add(zoneGroupId, side, amount);

        BroadcastScore(zoneGroupId, state);
        return state;
    }

    /// <summary>
    /// Settles the siege of one zone group: reads the score, decides the outcome from the content win points,
    /// records it with the score reset and the dominion change in one transaction, and then tells the clients.
    /// </summary>
    /// <returns>
    /// <see cref="SiegeSettlementResult.Failed"/> means nothing was written, so the caller must leave the
    /// siege period alone and try again - that is what keeps a failed settlement recoverable instead of lost.
    /// </returns>
    private SiegeSettlementResult SettleZoneGroup(ushort zoneGroupId, DateTime nowUtc)
    {
        var dominion = dominionManager.GetByZoneId(zoneGroupId);
        if (dominion == null)
        {
            Logger.Error("Zone group {0} left its siege period with no dominion to settle; it cannot settle again", zoneGroupId);
            return SiegeSettlementResult.Unsettleable;
        }

        var weekStart = SiegeGameData.Instance.GetCurrentCycleWeekStart(zoneGroupId, nowUtc);
        if (weekStart == null)
        {
            // Content carries no siege_plans cycle for this zone group. No later tick will find one, so this
            // is a permanent fault: the period advances rather than retrying against the same missing row.
            Logger.Error("Zone group {0} left its siege period with no siege_plans cycle; not settled and not retryable", zoneGroupId);
            return SiegeSettlementResult.Unsettleable;
        }

        var defenderFactionId = dominion.OwningFactionId != 0
            ? dominion.OwningFactionId
            : (uint)dominion.FactionId;

        var decision = ResolveOutcome(zoneGroupId, defenderFactionId);
        if (decision == null)
        {
            // The alliances a siege is fought between are content, so a content fault here is permanent too.
            return SiegeSettlementResult.Unsettleable;
        }

        var record = new SiegeSettlementRecord
        {
            ZoneGroupId = zoneGroupId,
            CycleWeekStart = weekStart.Value,
            SettledAtUtc = nowUtc,
            Score = scoreStore.Read(zoneGroupId),
            Outcome = decision.Value.Outcome,
            DefenderFactionId = decision.Value.DefenderFactionId,
            WinnerFactionId = decision.Value.WinnerFactionId,
            Reason = decision.Value.Reason,
        };

        SiegeSettlementRecord settled;
        try
        {
            // The outcome row, the zeroed counters and the dominion's new owner are written together, so the
            // database never holds a winner nobody was given. A duplicate means an earlier attempt (or an
            // earlier boot) already settled this cycle: its record comes back instead, and applying it again
            // is a no-op write, so the two halves of the work converge.
            settled = scoreStore.Settle(record);
        }
        catch (Exception ex)
        {
            // A store that threw is a transient fault: the database may be back next tick, so the period stays
            // put and the settlement is retried. This is the only failure that is meant to be retried.
            Logger.Error(ex, "Zone group {0} could not be settled for cycle {1:yyyy-MM-dd}; the siege period stays put",
                zoneGroupId, weekStart.Value);
            return SiegeSettlementResult.Failed;
        }

        var wasOnRecord = settled != record;
        Logger.Info("Zone group {0} siege settled: {1} - {2}", zoneGroupId, settled.Outcome, settled.Reason);

        // Only now, with the row written, is the in-memory dominion and the clients told. A World that dies
        // between the commit and here re-reads the same record on its next tick.
        dominionManager.ApplySettlement(zoneGroupId, settled);
        BroadcastScore(zoneGroupId, SiegeScoreState.Empty(zoneGroupId));

        return wasOnRecord ? SiegeSettlementResult.AlreadySettled : SiegeSettlementResult.Settled;
    }

    /// <summary>
    /// The outcome for a zone group, or null when the content cannot produce one (which is a failure to
    /// settle, not a reason to hand the dominion to somebody).
    /// </summary>
    private SiegeSettlementDecision? ResolveOutcome(ushort zoneGroupId, uint defenderFactionId)
    {
        try
        {
            return SiegeScoreRules.Resolve(scoreStore.Read(zoneGroupId), SiegeGameData.Instance.WinPoints,
                SiegeGameData.Instance.FactionRoles, defenderFactionId);
        }
        catch (InvalidOperationException ex)
        {
            // The alliances a siege is fought between are content; content that cannot name them cannot settle.
            Logger.Error(ex, "Zone group {0} cannot be settled: {1}", zoneGroupId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Sends the whole score of a zone group's siege. The first field is the zone group: the receiver looks its
    /// dominion record up by it, and a score sent for any other key is dropped.
    /// </summary>
    private static void BroadcastScore(ushort zoneGroupId, SiegeScoreState state) =>
        WorldManager.Instance.BroadcastPacketToServer(
            new SCSiegeScorePointPacket(zoneGroupId, state.OutlawPoint, state.DefensePoint, state.OffensePoint));

    public void ResetScore(ushort zoneGroupId)
    {
        scoreStore.Reset(zoneGroupId);
        BroadcastScore(zoneGroupId, SiegeScoreState.Empty(zoneGroupId));
    }
}

/// <summary>What settling one zone group's siege produced.</summary>
public enum SiegeSettlementResult
{
    /// <summary>Nothing was written. The caller must leave the siege period alone so the next tick retries.</summary>
    Failed = 0,

    /// <summary>This attempt recorded the outcome.</summary>
    Settled = 1,

    /// <summary>The cycle was already on record; that outcome was re-applied instead of a new one.</summary>
    AlreadySettled = 2,

    /// <summary>
    /// Nothing was written because the content cannot produce a settlement at all, and retrying will not
    /// change that. The caller must still advance the siege period: leaving it in place is what turns a
    /// permanent content fault into a zone group that never leaves its siege period and logs every tick
    /// for ever.
    /// </summary>
    Unsettleable = 3,
}
