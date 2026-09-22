using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Sieges;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Managers;

public interface ISiegeManager : ILoadable
{
    /// <summary>The zone group's calendar-driven phase at <paramref name="atUtc"/> — never <see cref="SiegePeriod.NoDominion"/>, that's an ownership fact, not a calendar one.</summary>
    SiegePeriod GetScheduledPeriod(uint zoneGroupId, DateTime atUtc);

    /// <summary>Whether <c>DeclareDominion</c> should be allowed to succeed for this zone group right now.</summary>
    bool IsDeclareDominionWindowOpen(uint zoneGroupId, DateTime atUtc);

    /// <summary>
    /// GM testing aid: toggles an in-memory override that forces <see cref="IsDeclareDominionWindowOpen"/> to
    /// return true for this zone group regardless of the real siege_plans/siege_zones schedule. Does not touch
    /// the schedule data or any other phase check (GetScheduledPeriod is unaffected) - purely unblocks the
    /// declare-window gate for testing. Not persisted; resets on World restart.
    /// </summary>
    /// <returns>The override's new state (true = forced open).</returns>
    bool ToggleDeclareWindowOverride(uint zoneGroupId);

    /// <summary>Recomputes and persists SiegePeriod for every currently-claimed dominion. Called on a schedule; also callable directly for tests/GM use.</summary>
    void Tick();

    /// <summary>Registers the caller for a zone group's raid team (offense or defense side); only during HeroVolunteer/ReadyToSiege.</summary>
    void RegisterForRaidTeam(GameConnection connection, ushort zoneId, bool isOffense);

    /// <summary>Removes the caller from a zone group's raid team roster.</summary>
    void UnregisterFromRaidTeam(GameConnection connection, ushort zoneId);

    /// <summary>Answers the registration popup with the characters registered in the caller's current zone group.</summary>
    void SendRaidTeamRegisterList(GameConnection connection);

    /// <summary>
    /// The raid teams the siege window lists for a zone group's siege: one per faction that has registrations,
    /// the defending faction first and numbered from one. Empty when nobody has registered, which is what the
    /// window shows as a vacant team.
    /// </summary>
    List<SiegeRaidTeam> GetRaidTeams(ushort zoneId);

    /// <summary>
    /// The characters registered on the offense side of a zone group's raid team (siege_raid_team_members
    /// with is_offense set, live characters only). Read by the siege_offense_hq_user target relation, which
    /// runs it once per unit per area-trigger pass, so the roster is held in memory between registrations.
    /// </summary>
    IReadOnlySet<uint> GetOffenseRaidTeam(ushort zoneId);

    /// <summary>
    /// Drops every cached offense roster, for a change to siege_raid_team_members that is not one zone group's
    /// registration. The rosters rebuild on their next read.
    /// </summary>
    void ForgetOffenseRaidTeams();

    /// <summary>Adds to a zone group's running siege score counters and broadcasts the update. No confirmed automatic trigger yet - see SiegeManager's doc comment.</summary>
    void AddScore(ushort zoneId, uint outlawDelta, uint defenseDelta, uint offenseDelta);

    /// <summary>Zeroes a zone group's score counters (start of a new siege cycle).</summary>
    void ResetScore(ushort zoneId);

    /// <summary>
    /// Automatic siege-score hook: called on every PvP kill (see Unit.DoDie). Awards a point to the killer's
    /// registered raid-team side (siege_raid_team_members) if the victim died in a zone group currently in the
    /// Siege period and the killer is registered for that zone's raid team. No-op otherwise (unregistered kills
    /// award nothing - "outlaw" scoring semantics are unconfirmed, see SiegeManager's doc comment).
    /// </summary>
    void OnCharacterKilled(Character killer, Character victim);
}
