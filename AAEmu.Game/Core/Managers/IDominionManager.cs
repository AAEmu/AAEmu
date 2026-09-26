using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Sieges;

namespace AAEmu.Game.Core.Managers;

public interface IDominionManager : ILoadable
{
    IEnumerable<DominionData> Dominions { get; }
    DominionData GetByZoneId(ushort zoneId);

    /// <summary>The claimed dominion whose territory (TerritoryData.RadiusDominion around its X/Y) contains this world position, or null.</summary>
    DominionData GetDominionAtPosition(ushort zoneId, float x, float y);

    /// <summary>
    /// Claims <paramref name="zoneId"/> for <paramref name="expeditionId"/> on behalf of <paramref name="lodestone"/>,
    /// persists it and broadcasts the result. Returns null (and sends <see cref="Models.Game.ErrorMessageType.DominionAlreadyDedclared"/>
    /// to <paramref name="declarer"/>) if the zone group is already claimed.
    /// </summary>
    DominionData Declare(ushort zoneId, uint expeditionId, House lodestone, Models.Game.Char.Character declarer);

    /// <summary>
    /// Hero/faction-claim path for the 4 siege_zones territories (zone groups 33/34/43/44) - claims for
    /// <paramref name="owningFactionId"/> (the raw FactionsEnum id, 148 Nuia / 149 Haranya) directly, NOT tied
    /// to any guild. Caller (DeclareDominion.cs) is responsible for checking the declarer is their faction's
    /// currently-elected Hero first. Same already-claimed/error-message behavior as <see cref="Declare"/>.
    /// </summary>
    DominionData DeclareForFaction(ushort zoneId, uint owningFactionId, House lodestone, Models.Game.Char.Character declarer);

    /// <summary>Changes the local dominion tax rate; only the owning Expedition (or, for a faction-owned zone, that faction's current Hero) may call this.</summary>
    void UpdateTaxRate(GameConnection connection, ushort zoneId, int taxRate);

    /// <summary>Sends every currently-claimed dominion to <paramref name="connection"/> (zone-enter / on-request push).</summary>
    void SendAllDominionsTo(GameConnection connection);

    /// <summary>System-driven (not player-driven) siege-phase update; used by SiegeManager's schedule tick.</summary>
    void UpdateSiegePeriod(ushort zoneId, byte period);

    /// <summary>
    /// Applies a settled siege to a claimed zone group: an alliance that broke through takes the claim (and
    /// with it the reign the guild had), and the siege's end and the new reign start are stamped either way.
    /// The dominions row is already written - the settlement wrote it in the same transaction as the outcome -
    /// so this mirrors the stored decision in memory and tells the clients and the zone. Re-applying a record
    /// is a no-op, which is what lets a retry after a restart finish what an interrupted attempt started.
    /// </summary>
    void ApplySettlement(ushort zoneId, SiegeSettlementRecord record);

    /// <summary>Weekly: mails out each dominion's current tax pool to its owning Expedition's leader and resets it. Called by DominionTaxPayoutTask; also callable directly for tests/GM use.</summary>
    void PayoutTax();

    /// <summary>Re-sends WZDominionData to Zone for an already-claimed zone group. Returns false if not claimed or the House/its zone can't be resolved.</summary>
    bool ResyncZone(ushort zoneId);

    /// <summary>Re-announce claims and the territory agent when a zone (re)loads.</summary>
    void RelayAllToZone(uint rawZoneId);

    /// <summary>GM diagnostic: resend a zeroed WZ claim body with an optional extra pad.</summary>
    bool ResyncZoneWithZeroedTestData(ushort zoneId, int diagnosticPaddingBytes = 0);

    /// <summary>
    /// GM/testing tool: reverses everything Declare() does for a claimed zone group - deletes its dominions
    /// row, resets the lodestone House back to its pre-claim buried state (owner cleared, build step reset to
    /// 0, attached doodads cleaned up), removes the initial-claim and any guard-tower-step buffs from the
    /// House, despawns the Territory Agent NPC, and broadcasts the cleared state to already-online clients.
    /// Returns false if the zone group isn't currently claimed. Does not touch the leftover native "Guard
    /// Tower Summon Point" doodad markers (never AAEmu-tracked) - the same lodestone House can be re-claimed
    /// afterward via the normal Declare() flow or <see cref="ClaimTerritory"/>.
    /// </summary>
    bool UnclaimTerritory(ushort zoneId);

    /// <summary>
    /// GM/testing convenience: claims <paramref name="zoneId"/> for <paramref name="expedition"/> without a
    /// live skill-cast. Finds the seeded lodestone House in that zone group and calls <see cref="Declare"/>.
    /// <paramref name="declarer"/> is optional (used for House.OwnerId/faction resolution, same as a real
    /// declare) - pass null to fall back to the boot-time expedition-owner faction resolution.
    /// </summary>
    DominionData ClaimTerritory(ushort zoneId, Models.Game.Expeditions.Expedition expedition, Models.Game.Char.Character declarer);

    /// <summary>
    /// GM/testing convenience, faction path: claims <paramref name="zoneId"/> for <paramref name="factionId"/>
    /// directly, bypassing the normal Hero-eligibility check - see <see cref="DeclareForFaction"/>.
    /// </summary>
    DominionData ClaimTerritoryForFaction(ushort zoneId, Models.StaticValues.FactionsEnum factionId, Models.Game.Char.Character declarer);
}
