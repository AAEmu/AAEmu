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
/// raid-team registration + score counters (new `siege_raid_team_members`/`siege_scores` tables).
///
/// Raid-commander election (SCElectSiegeRaidOwnerPacket) is NOT built - a separate voting subsystem similar to
/// Hero's, lower priority, not done here.
///
/// Score tallying: a PvP kill in a zone group that is in the Siege period awards a point to the killer's
/// registered raid-team side. Unregistered killers score nothing.
/// </summary>
public class SiegeManager(ITaskManager taskManager, IDominionManager dominionManager) : Singleton<SiegeManager>, ISiegeManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>GM testing override - see ISiegeManager.ToggleDeclareWindowOverride. Not persisted.</summary>
    private readonly HashSet<uint> _forcedOpenDeclareWindows = [];

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
        foreach (var dominion in dominionManager.Dominions)
        {
            var period = GetScheduledPeriod(dominion.ZoneId, now);
            if (dominion.SiegeTimers.SiegePeriod == (byte)period)
                continue;

            dominionManager.UpdateSiegePeriod(dominion.ZoneId, (byte)period);
            changed++;
        }

        if (changed > 0)
            Logger.Info("SiegeManager.Tick: {0} dominion(s) changed siege period", changed);
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

        character.SendPacket(new SCSiegeMemberPacket(0, (int)zoneId, character.Id, true));
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

            double total = 0;
            foreach (var item in equipment.Items)
            {
                if (item != null)
                    total += GearScoreCalculator.EvaluateItem(item);
            }

            return (uint)Math.Max(0, (int)Math.Round(total));
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Could not score the gear of offline character {0} - listed as 0", characterId);
            return 0;
        }
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

        character.SendPacket(new SCSiegeMemberPacket(0, (int)zoneId, character.Id, false));
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

    /// <summary>The alliance a character fights for — the faction whose raid team their registrations join.</summary>
    public uint AllianceOfFaction(Character character) =>
        character?.Faction == null ? 0 : AllianceOf((uint)character.Faction.Id);

    public void AddScore(ushort zoneId, uint outlawDelta, uint defenseDelta, uint offenseDelta)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO siege_scores (zone_id, outlaw_point, defense_point, offense_point)
            VALUES (@z, @o, @d, @f)
            ON DUPLICATE KEY UPDATE
                outlaw_point = outlaw_point + @o,
                defense_point = defense_point + @d,
                offense_point = offense_point + @f
            """;
        command.Parameters.AddWithValue("@z", zoneId);
        command.Parameters.AddWithValue("@o", outlawDelta);
        command.Parameters.AddWithValue("@d", defenseDelta);
        command.Parameters.AddWithValue("@f", offenseDelta);
        command.Prepare();
        command.ExecuteNonQuery();

        WorldManager.Instance.BroadcastPacketToServer(new SCSiegeScorePointPacket(0, outlawDelta, defenseDelta, offenseDelta));
    }

    public void OnCharacterKilled(Character killer, Character victim)
    {
        var zone = ZoneManager.Instance.GetZoneByKey(victim.Transform.ZoneId);
        if (zone == null)
            return;

        var zoneId = (ushort)zone.GroupId;
        if (GetScheduledPeriod(zoneId, DateTime.UtcNow) != SiegePeriod.Siege)
            return;

        var isOffense = GetRaidTeamSide(zoneId, killer.Id);
        if (isOffense == null)
            return; // Killer isn't registered for this zone's raid team - no confirmed "outlaw" scoring rule to fall back on.

        AddScore(zoneId, 0, isOffense.Value ? 0u : 1u, isOffense.Value ? 1u : 0u);
    }

    private static bool? GetRaidTeamSide(ushort zoneId, uint characterId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT is_offense FROM siege_raid_team_members WHERE zone_id=@z AND character_id=@c";
        command.Parameters.AddWithValue("@z", zoneId);
        command.Parameters.AddWithValue("@c", characterId);
        command.Prepare();
        using var reader = command.ExecuteReader();
        return reader.Read() ? reader.GetBoolean(0) : null;
    }

    public void ResetScore(ushort zoneId)
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "REPLACE INTO siege_scores (zone_id, outlaw_point, defense_point, offense_point) VALUES (@z, 0, 0, 0)";
        command.Parameters.AddWithValue("@z", zoneId);
        command.Prepare();
        command.ExecuteNonQuery();
    }
}
