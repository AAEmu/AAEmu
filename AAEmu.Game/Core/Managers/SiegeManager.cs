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
            command.CommandText = """
                SELECT m.character_id, c.name
                FROM siege_raid_team_members m
                LEFT JOIN characters c ON c.id = m.character_id
                WHERE m.zone_id = @z
                ORDER BY m.registered_at, m.character_id
                """;
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
