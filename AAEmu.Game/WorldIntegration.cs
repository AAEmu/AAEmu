using System.Collections.Concurrent;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game;

public sealed record WorldZoneConnectionSnapshot(
    uint SessionId,
    uint ZoneId,
    uint InstanceId,
    string State,
    string Ip,
    int UnitCount);

public sealed record WorldNpcSpawnerEventRequest(
    uint CreatorObjId,
    BaseUnitType CreatorType,
    ulong CreatorCharacterId,
    long CreatorValue,
    uint CreatorTemplateId,
    ulong CreatorOwnerId,
    byte CreatorFlag,
    uint SpawnerId,
    NpcSpawnerEvent Event,
    NpcSpawnerEventType Type,
    float LifeTime,
    bool DespawnOnCreatorDeath,
    bool UseSummonerAggroTarget);

public sealed record WorldNpcSpawnRequest(uint ZoneId, uint ObjId, byte[] Body);

public sealed record WorldNpcAggroRequest(
    uint SkillTargetObjId,
    uint SourceObjId,
    uint UnitInChargeObjId,
    uint Aggro,
    bool Hostile,
    CastAction CastAction);

/// <summary>
/// Commercial World hooks. When <see cref="ZoneAuthority"/> is true, the native zone is
/// the only sim authority (NPCs, movement, combat). Game is lobby + CS/SC glue only.
/// Standalone Game.exe: ZoneAuthority false — all hooks no-op.
/// </summary>
public static class WorldIntegration
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly ConcurrentQueue<PendingZoneNpc> PendingZoneNpcs = new();
    private static readonly object PendingNpcHandoffsLock = new();
    private static readonly Dictionary<uint, PendingNpcHandoff> PendingNpcHandoffs = [];

    private sealed class PendingNpcHandoff(uint readyBuffTemplateId, Action completion)
    {
        public uint ReadyBuffTemplateId { get; } = readyBuffTemplateId;
        public Action Completion { get; } = completion;
        public bool PlotReady { get; set; }
        public bool ZoneReady { get; set; }
    }

    private readonly record struct PendingZoneNpc(
        uint ZoneId, uint BcId, uint TemplateId, float X, float Y, float Z, float ZRot, float Scale);

    private readonly record struct WzNpcSpawnMetadata(
        uint SpawnerId,
        byte MemberIndex,
        byte PartIndex,
        ushort TableIndex,
        NpcSpawnReasonType Reason,
        CastAction SpawnAction,
        float SpawningEffectTime,
        uint GroupType,
        uint GroupId,
        byte GroupMemberIndex);

    // A World-authored summon is not a member of a native Zone spawner or group. These zero
    // identities are required protocol values, not placeholder content ids.
    private static readonly WzNpcSpawnMetadata WorldAuthoredNpcSpawn = new(
        0,
        0,
        0,
        0,
        NpcSpawnReasonType.Default,
        null,
        0f,
        0,
        0,
        0);

    // Native empty UnitState id-block (type Character): charId 0 and v 0.
    private const ulong NoCreatorCharacterId = 0UL;
    private const long NoCreatorValue = 0L;

    /// <summary>Set by AAEmu.World — zone is sim authority, not local Game world.</summary>
    public static bool ZoneAuthority { get; set; }

    /// <summary>Place player in zone via WZUnitState. Args: bcId, body. False = refuse enter.</summary>
    public static Func<uint, byte[], bool> TryEnterZone { get; set; }

    /// <summary>
    /// Push an arbitrary WZUnitState (0x007) body to a specific zone key — e.g. house units before WZHouseState.
    /// </summary>
    public static Action<uint, byte[]> RelayUnitStateToZone { get; set; }

    /// <summary>Relay CS movement to zone as WZUnitMovement. Args: bcId, type+move body (no outer bc).</summary>
    public static Action<uint, byte[]> RelayMoveToZone { get; set; }

    /// <summary>Relay skill-controller creation state to Zone.</summary>
    public static Action<uint, byte, bool> RelayCreateSkillControllerToZone { get; set; }

    /// <summary>
    /// Relay authoritative skill-controller length and torn/cutout state to Zone.
    /// </summary>
    public static Action<uint, byte, float, bool, bool> RelaySkillControllerStateToZone { get; set; }

    /// <summary>
    /// Relay player cast to zone as WZSkillStarted (0x02B). True if a ZoneLoaded connection accepted it.
    /// Args: skillId, tl, caster, target, ct, skillObject. Under ZoneAuthority, Game must not Skill.Use.
    /// </summary>
    public static Func<uint, ushort, SkillCaster, SkillCastTarget, uint, SkillObject, bool> RelaySkillStartedToZone { get; set; }

    /// <summary>Relay WZSkillFired (0x02C). True if accepted.</summary>
    public static Func<uint, ushort, SkillCaster, SkillCastTarget, SkillObject, bool> RelaySkillFiredToZone { get; set; }

    /// <summary>
    /// Relay WZSkillEnded (0x02D). Native body is timeline u16 followed by SkillCaster.
    /// True when the owning ZoneLoaded connection accepted the completion.
    /// </summary>
    public static Func<ushort, SkillCaster, bool> RelaySkillEndedToZone { get; set; }

    /// <summary>
    /// Relay WZUnitDamaged (0x030) after World authors SCUnitDamaged.
    /// Args: skillId, tl, caster, target, damage, absorbed, casterBc, targetBc.
    /// HARD-BLOCKED until full UnitDamaged body RE — use <see cref="RelayUnitPointsToZone"/> instead.
    /// </summary>
    public static Action<uint, ushort, SkillCaster, SkillCastTarget, int, int, uint, uint> RelayUnitDamagedToZone { get; set; }

    /// <summary>
    /// Relay WZUnitPoints (0x020) — sync Zone HP/MP to World display values (precise ×100).
    /// Args: objId, hp, mp. Safe alternative to WZUnitDamaged for post-hit Zone HP sync.
    /// </summary>
    public static Action<uint, int, int> RelayUnitPointsToZone { get; set; }

    /// <summary>Relay WZUnitDeath (0x021) — Zone marks unit dead (corpse / spawner liveCount).</summary>
    public static Action<uint> RelayUnitDeathToZone { get; set; }

    /// <summary>Relay WZNpcStartDespawn (0x004) — Zone AI GO_TO_DESPAWN after corpse timeout.</summary>
    public static Action<uint> RelayNpcStartDespawnToZone { get; set; }

    /// <summary>
    /// Relay WZNpcSpawnerEvent (0x070). Zone owns the spawner population, lifetime, and
    /// summoner-aggro behavior; World supplies the skill-authored event and creator identity.
    /// </summary>
    public static Func<WorldNpcSpawnerEventRequest, bool> RelayNpcSpawnerEventToZone { get; set; }

    /// <summary>Relay a World-created NPC to its simulation authority as WZNpcState (0x002).</summary>
    public static Func<WorldNpcSpawnRequest, bool> RelayNpcSpawnToZone { get; set; }

    /// <summary>Publish scripted aggro to the Zone AI as WZUpdateAggro (0x044).</summary>
    public static Action<WorldNpcAggroRequest> RelayNpcAggroToZone { get; set; }

    /// <summary>
    /// Relay WZAggroReset (0x045). Args: affected unit, damage selector, heal selector,
    /// direct/script selector, and the signed value assigned to each selected component.
    /// </summary>
    public static Action<uint, int, int, int, int> RelayAggroResetToZone { get; set; }

    /// <summary>
    /// table into the second unit.
    /// </summary>
    public static Action<uint, uint> RelayAggroCopyToZone { get; set; }

    /// <summary>
    /// Relay WZFakeDeath (0x046). The native packet carries only the affected unit's bc; special
    /// descriptor values are not part of the Zone-authoritative state transition.
    /// </summary>
    public static Action<uint> RelayFakeDeathToZone { get; set; }

    /// <summary>Relay WZUnitRemoved (0x008) — force Zone drop unit (frees spawner liveCount for respawn).</summary>
    public static Action<uint> RelayUnitRemovedToZone { get; set; }

    /// <summary>
    /// Relay WZUnitRemoved (0x008) to one named zone key instead of the unit's current one.
    /// Args: zoneId, bcId. Needed when a unit has already moved on (or been re-announced elsewhere)
    /// and the dedicate still holding it can no longer be found from its transform.
    /// </summary>
    public static Action<uint, uint> RelayUnitRemovedToZoneId { get; set; }

    /// <summary>Relay WZPlotEvent (0x03A) so Zone runs plot effects locally.</summary>
    public static Action<ushort, uint, uint, PlotObject, PlotObject, ulong, uint, uint, uint, bool, bool, uint[]> RelayPlotEventToZone { get; set; }

    /// <summary>
    /// Relay WZGmCommand (0x04F) from real client CSGmCommand / X2Gm.
    /// Args: unitId, cmd, params. True if Zone accepted.
    /// </summary>
    public static Func<uint, ushort, string, bool> RelayGmCommandToZone { get; set; }

    /// <summary>WZCreateDoodad — Zone physics ownership for a World-authored doodad.</summary>
    public static Action<object> RelayCreateDoodadToZone { get; set; }

    /// <summary>
    /// Zone just reached ZoneLoaded — flush World-authored doodads for this zone key.
    /// </summary>
    public static Action<uint> NotifyZoneReadyForDoodads { get; set; }

    /// <summary>Replay World-owned gimmicks for the Zone that just loaded or reconnected.</summary>
    public static Action<uint> NotifyZoneReadyForGimmicks { get; set; }

    /// <summary>
    /// Character crossed zone keys under ZoneAuthority: hand off presence (old→remove, new→UnitState).
    /// Args: bcId, oldZoneId, newZoneId, unitStateBody.
    /// </summary>
    public static Func<uint, uint, uint, byte[], bool> RelayCharacterZoneHandoff { get; set; }

    /// <summary>Replay World-owned housing for the Zone that just loaded or reconnected.</summary>
    public static Action<uint> NotifyZoneReadyForHousing { get; set; }

    /// <summary>
    /// True when a dedicate is ZoneLoaded for this zone key. Used to warn on create/login
    /// when race starters (Nuian 179, Firran 184, …) have no matching process.
    /// </summary>
    public static Func<uint, bool> IsZoneLoaded { get; set; }

    /// <summary>
    /// Read-only snapshots of the native Zone connections currently registered by World.
    /// Supplied by AAEmu.World so the shared Game Web API does not depend on the World executable.
    /// </summary>
    public static Func<IReadOnlyList<WorldZoneConnectionSnapshot>> GetZoneConnectionStatus { get; set; }

    /// <summary>Requests every loaded Zone to move its authoritative day-cycle clock.</summary>
    public static Action<float> RelayTimeOfDayToZones { get; set; }

    /// <summary>
    /// Zone day-cycle report. Args: zoneId, time, speed, start, end, isDetailed.
    /// </summary>
    public static Action<uint, float, float, float, float, bool> OnZoneTimeOfDay { get; set; }

    /// <summary>WZRemoveDoodad.</summary>
    public static Action<uint> RelayRemoveDoodadToZone { get; set; }

    /// <summary>WZDoodadChangePhase.</summary>
    public static Action<uint, uint, int> RelayDoodadPhaseToZone { get; set; }

    /// <summary>WZUnitEquipmentChanged body after bc (num + slots + flags).</summary>
    public static Action<uint, byte[]> RelayEquipmentChangedToZone { get; set; }

    /// <summary>WZBuffCreated opaque body (target unit ObjId for zone routing).</summary>
    public static Action<uint, byte[]> RelayBuffCreatedToZone { get; set; }

    /// <summary>WZBuffRemoved.</summary>
    public static Action<uint, uint> RelayBuffRemovedToZone { get; set; }

    /// <summary>WZInteractNPC / End.</summary>
    public static Action<uint, uint, bool> RelayInteractNpcToZone { get; set; }

    /// <summary>WZUnitAttached / Detached.</summary>
    public static Action<uint, uint, byte, bool> RelayUnitAttachToZone { get; set; }

    /// <summary>
    /// WZImpulseUnit. Args: target bc, caster, then vel / angvel / impulse / angImpulse as the
    /// twelve floats impulse_effects authors, already in world space.
    /// </summary>
    public static Action<uint, SkillCaster, float[], float[], float[], float[]> RelayImpulseToZone { get; set; }

    /// <summary>
    /// WZUnitHealed (0x031). Args: castAction, caster, targetId, healType, healHitType, value,
    /// unitInCharge (healer bc for Zone threat), critical.
    /// </summary>
    public static Action<CastAction, SkillCaster, uint, HealType, HealHitType, long, uint, bool>
        RelayUnitHealedToZone { get; set; }

    /// <summary>WZKnockBackUnit (0x033). Args: unit bc, world pos xyz.</summary>
    public static Action<uint, float, float, float> RelayKnockBackToZone { get; set; }

    /// <summary>
    /// WZBlinkUnit (0x032). Args: unit bc, baseUnit bc, move3D, world x/y/z
    /// (x/y as ConvertLongX/Y ulong wire).
    /// </summary>
    public static Action<uint, uint, bool, float, float, float> RelayBlinkToZone { get; set; }

    /// <summary>WZCombatEngaged / Cleared (0x025 / 0x026).</summary>
    public static Action<uint> RelayCombatEngagedToZone { get; set; }
    public static Action<uint> RelayCombatClearedToZone { get; set; }

    /// <summary>WZTargetChanged (0x02A). Args: unit, target (0 = clear), forceByWorld.</summary>
    public static Action<uint, uint, bool> RelayTargetChangedToZone { get; set; }

    /// <summary>WZForceAttackSet (0x024).</summary>
    public static Action<uint, bool> RelayForceAttackToZone { get; set; }

    /// <summary>
    /// WZUnitResurrection (0x022). Args: unit bc, world x/y/z, zRot.
    /// </summary>
    public static Action<uint, float, float, float, float> RelayUnitResurrectionToZone { get; set; }

    /// <summary>WZSkillStopped (0x02E). Args: unit bc, skillId.</summary>
    public static Action<uint, int> RelaySkillStoppedToZone { get; set; }

    /// <summary>WZCastingStopped (0x02F). Args: unit bc, tl, reason type, duration ms.</summary>
    public static Action<uint, short, int, int> RelayCastingStoppedToZone { get; set; }

    /// <summary>WZUnitFactionChanged (0x019). Args: unit, oldFaction, newFaction, temp.</summary>
    public static Action<uint, int, int, bool> RelayUnitFactionChangedToZone { get; set; }

    /// <summary>WZEscapeSlave (0x04C). Args: slave bc, world x/y/z, rot.</summary>
    public static Action<uint, float, float, float, float> RelayEscapeSlaveToZone { get; set; }

    /// <summary>WZShipControlChange (0x04B). Args: slave bc, hasDriverControl.</summary>
    public static Action<uint, bool> RelayShipControlChangeToZone { get; set; }

    /// <summary>
    /// Quest AI WZ family. Args: npc bc, player/target bc, then optional pathName/pathType/commandSetId
    /// selected by <paramref name="kind"/> (Attack=0 FollowUnit=1 FollowPath=2 RunCommand=3).
    /// </summary>
    public static Action<int, uint, uint, string, byte, int> RelayQuestNpcAiToZone { get; set; }

    /// <summary>WZBuffUpdated (0x040). Args: unit, buffIndex, stack, charged, elapsedMs, reason.</summary>
    public static Action<uint, int, uint, uint, int, byte> RelayBuffUpdatedToZone { get; set; }

    /// <summary>WZRequestCombatUnits (0x071). Args: requester, aroundUnit.</summary>
    public static Action<uint, uint> RelayRequestCombatUnitsToZone { get; set; }

    /// <summary>WZDropBackpack (0x07F) opaque-ish. Args: character bc, itemId, doodadTpl, zoneId, x,y,z.</summary>
    public static Action<uint, ulong, uint, uint, float, float, float> RelayDropBackpackToZone { get; set; }

    /// <summary>
    /// WZUnitBondToDoodad / Unbond. When <paramref name="bond"/> is true, <paramref name="bonding"/>
    /// must be non-null (full SerializeBonding + root bc). Unbond ignores bonding.
    /// </summary>
    public static Action<uint, BondDoodad, bool> RelayBondDoodadToZone { get; set; }

    /// <summary>Atomically route WZUnitState followed by WZHouseState to the requested zone key.</summary>
    public static Action<uint, byte[], byte[]> RelayHouseStateToZone { get; set; }

    /// <summary>WZHouseBuildProgress / Done (zone key, then housing timeline id).</summary>
    public static Action<uint, ushort, uint, int, int> RelayHouseBuildProgressToZone { get; set; }
    public static Action<uint, ushort> RelayHouseBuildDoneToZone { get; set; }

    /// <summary>WZGimmickCreated / Removed / Grasped.</summary>
    public static Action<GimmickSpawnData, int> RelayGimmickCreatedToZone { get; set; }
    public static Action<uint> RelayGimmickRemovedToZone { get; set; }
    public static Action<uint, uint, uint, bool> RelayGimmickGraspedToZone { get; set; }

    /// <summary>World authority hooks for static gimmicks discovered and simulated by Zone.</summary>
    public static Func<Character, uint, bool> TryInteractZoneGimmick { get; set; }
    public static Action<Character> ReleaseZoneGimmickGrasps { get; set; }

    public static Action<uint, string> RelayZoneCommand { get; set; }

    /// <summary>CS raycast request routed by unit objId; native payload starts at persistent playerId.</summary>
    public static Action<uint, ulong, ulong, ulong, float, float, float, float, uint, bool, bool>
        RelayZoneRayCasting { get; set; }

    /// <summary>Native ZW raycast result: persistent playerId, request id, position and text.</summary>
    public static Action<ulong, uint, ulong, ulong, float, string> OnZoneRayCastingResult { get; set; }

    /// <summary>WZSlaveMasterChanged. Args: slave bc, master persistent id, master world id.</summary>
    public static Action<uint, long, byte> RelaySlaveMasterChangedToZone { get; set; }

    /// <summary>WZSiegeState opaque.</summary>
    public static Action<byte[]> RelaySiegeStateToZone { get; set; }

    /// <summary>WZCheckMole* opaque.</summary>
    public static Action<bool, byte[]> RelayMoleCheckToZone { get; set; }

    public static Action<uint, uint, int, int> OnZoneEnterArea { get; set; }

    /// <summary>Zone reported quest_area/district leave.</summary>
    public static Action<uint, uint, int, int> OnZoneLeaveArea { get; set; }

    /// <summary>Zone removed a house (ZWRemoveHouse).</summary>
    public static Action<ushort> OnZoneRemoveHouse { get; set; }

    /// <summary>True when this world spawned and drives the gimmick with the given object id.</summary>
    public static Func<uint, bool> IsWorldOwnedGimmick { get; set; }

    /// <summary>
    /// Zone found a level static gimmick and wants World to spawn it.
    /// Carries the complete GimmickSpawnData record from Zone.
    /// </summary>
    public static Action<uint, GimmickSpawnData> OnZoneRequestStaticGimmick { get; set; }

    /// <summary>Zone backpack drop notify (opaque body).</summary>
    public static Action<byte[]> OnZoneBackpackDropped { get; set; }

    /// <summary>Broadcast a fully-built SC GamePacket to in-world clients (same gates as BroadcastSc).</summary>
    public static void BroadcastPacket(GamePacket packet)
    {
        if (!ZoneAuthority || packet == null)
            return;

        ForEachReadyConnection((con, _) =>
        {
            try
            {
                con.SendPacket(packet);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "BroadcastPacket 0x{0:X3} failed for connection {1}", packet.TypeId, con.Id);
            }
        });
    }

    /// <summary>Send an SC packet only to clients that received at least one referenced unit.</summary>
    public static void BroadcastPacketToUnitViewers(GamePacket packet, params uint[] unitIds)
    {
        if (!ZoneAuthority || packet == null || unitIds == null || unitIds.Length == 0)
            return;

        ForEachReadyConnection((con, ch) =>
        {
            if (!unitIds.Any(unitId => IsStreamedUnitForClient(ch, unitId)))
                return;
            try
            {
                con.SendPacket(packet);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Unit-viewer packet 0x{0:X3} failed for connection {1}", packet.TypeId, con.Id);
            }
        });
    }

    public static Action<uint> OnPlayerLeave { get; set; }

    /// <summary>Zone spawned an NPC — mirror into Game for SCUnitState. Args: zoneId, bcId, tpl, x,y,z,zRot,scale (zone-local xy). True if mirrored (or already present).</summary>
    public static Func<uint, uint, uint, float, float, float, float, float, bool> OnZoneNpcSpawn { get; set; }

    /// <summary>Zone removed an NPC — drop Game mirror + SCUnitsRemoved.</summary>
    public static Action<uint> OnZoneNpcRemove { get; set; }

    /// <summary>Fired once MainWorld exists — World remirrors any zone units that arrived early.</summary>
    public static Action OnMainWorldReady { get; set; }

    /// <summary>
    /// Fired after a World instance leaves the manager. Consumers must discard state keyed by the
    /// recycled instance id at this boundary.
    /// </summary>
    public static Action<uint> OnWorldInstanceRemoved { get; set; }

    /// <summary>
    /// Drive a <c>tower_defs</c> timed world event by hand. Args: action ("start"/"end"/"wave"),
    /// towerDefId, step (wave only). Returns a human-readable result for the GM output.
    /// The scheduler lives in World because only it holds the Zone connections.
    /// </summary>
    public static Func<string, uint, uint, string> TriggerTowerDef { get; set; }

    /// <summary>Schedule overview for the <c>towerdef list</c> action, one line per event.</summary>
    public static Func<IEnumerable<string>> DescribeTowerDefs { get; set; }

    /// <summary>
    /// Broadcast a finished SC body (opcode + body only) to in-world clients only.
    /// Must NOT target lobby/select/loading connections — ActiveChar is set on SelectCharacter,
    /// and premature SCUnitMovements (zone flood) hard-closes the client before enter finishes.
    /// Gate matches mirror interest: NotifyInGameCompleted (+ optional grace).
    /// </summary>
    public static void BroadcastSc(ushort scOpcode, byte[] body)
    {
        if (!ZoneAuthority || body == null)
            return;

        ForEachReadyConnection((con, _) =>
        {
            try
            {
                con.SendPacket(new SCOpaquePacket(scOpcode, body));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "BroadcastSc 0x{0:X3} failed for connection {1}", scOpcode, con.Id);
            }
        });
    }

    /// <summary>Run an action for each client whose Zone mirror stream is ready.</summary>
    public static void ForEachReadyConnection(Action<GameConnection, Character> action)
    {
        if (!ZoneAuthority || action == null)
            return;

        var now = Environment.TickCount64;
        foreach (var con in GameConnectionTable.Instance.GetConnections())
        {
            var ch = con?.ActiveChar;
            if (ch == null || !IsMirrorStreamReady(ch, now))
                continue;
            action(con, ch);
        }
    }

    /// <summary>True when this specific client has received the referenced unit's SCUnitState.</summary>
    public static bool IsStreamedUnitForClient(Character character, uint bcId)
    {
        if (!ZoneAuthority || character == null || bcId == 0)
            return false;
        if (!IsMirrorStreamReady(character, Environment.TickCount64))
            return false;
        if (character.ObjId == bcId)
            return true;

        var unit = character.ParentWorld?.GetBaseUnit(bcId);
        if (unit is Npc { IsZoneMirror: true })
            return character.MirrorNpcStatesSentIds.ContainsKey(bcId);

        // Players, mounts, and vehicles use the normal Game region stream rather than
        // MirrorNpcStatesSentIds. Region-neighbor visibility matches GameObject.BroadcastPacket.
        return unit is { IsVisible: true } && character.UnitIsVisible(unit);
    }

    /// <summary>
    /// True if any in-world client already received SCUnitState for this bcId (or it is that
    /// client's player). Mirrored NPCs use their explicit per-client stream set because Zone can
    /// hold more mirrors than the client limit; ordinary units use normal Game region visibility.
    /// </summary>
    public static bool IsStreamedUnitForAnyClient(uint bcId)
    {
        if (!ZoneAuthority || bcId == 0)
            return false;

        foreach (var con in GameConnectionTable.Instance.GetConnections())
        {
            var ch = con?.ActiveChar;
            if (IsStreamedUnitForClient(ch, bcId))
                return true;
        }

        return false;
    }

    private static bool IsMirrorStreamReady(Character character, long now) =>
        character.MirrorNpcStreamReady && now >= character.MirrorNpcStreamNotBeforeTick;

    /// <summary>
    /// Zone TCP lost. Return only clients whose Transform.ZoneId matches
    /// <paramref name="zoneId"/> to character select. Sibling zones remain available.
    /// When zoneId is 0 (unknown), recover all in-world clients.
    /// </summary>
    public static void NotifyZoneLost(string reason, uint zoneId = 0)
    {
        if (!ZoneAuthority)
            return;

        if (zoneId == 0)
        {
            Logger.Error("Zone lost ({0}) — returning all in-world clients to character select (zoneId unknown)", reason);
            ReturnInWorldClientsToCharacterSelect(0, reason);
            return;
        }

        Logger.Error(
            "Zone lost ({0}) zoneId={1} — returning clients in that zone to character select",
            reason, zoneId);
        ReturnInWorldClientsToCharacterSelect(zoneId, reason);
    }

    public static byte[] BuildWzUnitStateBody(Character character)
        => BuildWzUnitStateBody((Unit)character);

    public static byte[] BuildWzUnitStateBody(Unit unit)
    {
        var stream = new PacketStream();
        new SCUnitStatePacket(unit).WriteWzBody(stream);
        return stream.GetBytes();
    }

    public static bool PublishNpcSpawnerEvent(
        BaseUnit creator,
        uint spawnerId,
        NpcSpawnerEvent spawnerEvent,
        float lifeTime = 0f,
        bool despawnOnCreatorDeath = false,
        bool useSummonerAggroTarget = false,
        NpcSpawnerEventType type = NpcSpawnerEventType.Default)
    {
        if (!ZoneAuthority || spawnerId == 0 || creator == null)
            return false;

        var creatorType = creator switch
        {
            Character => BaseUnitType.Character,
            Npc => BaseUnitType.Npc,
            _ => BaseUnitType.Invalid
        };
        if (creatorType == BaseUnitType.Invalid)
        {
            Logger.Warn(
                "PublishNpcSpawnerEvent: creator type {0} has no verified packet identity block",
                creator.GetType().Name);
            return false;
        }

        var characterId = creator is Character character ? character.Id : 0UL;
        var ownerId = creator is Npc npc ? npc.OwnerId : 0UL;
        var creatorFlag = creator is Npc creatorNpc ? creatorNpc.UnitStateFlag : (byte)0;

        var request = new WorldNpcSpawnerEventRequest(
            creator.ObjId,
            creatorType,
            characterId,
            0L, // Character id-block field "v" is required and has no runtime model; UnitState also writes zero.
            creator.TemplateId,
            ownerId,
            creatorFlag,
            spawnerId,
            spawnerEvent,
            type,
            lifeTime,
            despawnOnCreatorDeath,
            useSummonerAggroTarget);

        return RelayNpcSpawnerEventToZone?.Invoke(request) == true;
    }

    /// <summary>
    /// Hands a World-created NPC to the Zone. The Game object remains a display/persistence mirror;
    /// movement, combat, lifetime, and AI belong exclusively to the native Zone from this point.
    /// </summary>
    public static bool PublishNpcSpawn(
        Npc npc,
        float lifeTime = 0f,
        bool despawnOnCreatorDeath = false,
        bool useSummonerAggroTarget = false,
        BaseUnit creator = null,
        NpcSpawnReasonType reason = NpcSpawnReasonType.Default,
        CastAction spawnAction = null)
    {
        if (!ZoneAuthority || npc?.Transform == null || npc.ObjId == 0)
            return false;

        var zoneId = npc.Transform.ZoneId;
        if (zoneId == 0)
        {
            var world = npc.ParentWorld ?? WorldManager.Instance.MainWorld;
            var position = npc.Transform.World.Position;
            zoneId = world?.Template == null
                ? 0
                : WorldManager.Instance.GetZoneId(world.Template, position.X, position.Y);
            if (zoneId != 0)
                npc.Transform.ZoneId = zoneId;
        }

        if (zoneId == 0)
        {
            Logger.Warn("PublishNpcSpawn: no zone owns npc {0} (template {1})", npc.ObjId, npc.TemplateId);
            return false;
        }

        npc.IsZoneMirror = true;
        var body = BuildWzNpcStateBody(
            npc,
            WorldAuthoredNpcSpawn with { Reason = reason, SpawnAction = spawnAction },
            creator,
            lifeTime,
            despawnOnCreatorDeath,
            useSummonerAggroTarget);
        return body is { Length: > 0 }
               && RelayNpcSpawnToZone?.Invoke(new WorldNpcSpawnRequest(zoneId, npc.ObjId, body)) == true;
    }

    public static void PublishNpcDespawn(BaseUnit npc)
    {
        if (ZoneAuthority && npc?.ObjId > 0)
            RelayNpcStartDespawnToZone?.Invoke(npc.ObjId);
    }

    public static void RegisterNpcHandoff(uint objId, uint readyBuffTemplateId, Action completion)
    {
        if (objId == 0 || readyBuffTemplateId == 0 || completion == null)
            return;

        lock (PendingNpcHandoffsLock)
            PendingNpcHandoffs[objId] = new PendingNpcHandoff(readyBuffTemplateId, completion);
    }

    public static void MarkNpcHandoffPlotReady(uint objId) =>
        UpdatePendingNpcHandoff(objId, plotReady: true, removedBuffTemplateId: 0);

    public static void ObserveZoneBuffRemoved(uint objId, uint buffTemplateId) =>
        UpdatePendingNpcHandoff(objId, plotReady: false, removedBuffTemplateId: buffTemplateId);

    public static void CancelNpcHandoff(uint objId)
    {
        if (objId == 0)
            return;

        lock (PendingNpcHandoffsLock)
            PendingNpcHandoffs.Remove(objId);
    }

    private static void UpdatePendingNpcHandoff(uint objId, bool plotReady, uint removedBuffTemplateId)
    {
        Action completion = null;
        lock (PendingNpcHandoffsLock)
        {
            if (!PendingNpcHandoffs.TryGetValue(objId, out var pending))
                return;

            if (plotReady)
                pending.PlotReady = true;
            if (removedBuffTemplateId == pending.ReadyBuffTemplateId)
                pending.ZoneReady = true;

            if (pending.PlotReady && pending.ZoneReady)
            {
                PendingNpcHandoffs.Remove(objId);
                completion = pending.Completion;
            }
        }

        if (completion == null)
            return;

        try
        {
            completion();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Pending NPC handoff failed obj={0}", objId);
        }
    }

    /// <summary>
    /// Retires a World-side NPC mirror. Zone-owned NPCs remain mirrored and keep their broadcast
    /// id until ZWRemoveNpc confirms retirement; failed pre-handoff spawns delete and release now.
    /// </summary>
    public static void DeleteNpcMirror(Npc npc, bool notifyZone)
    {
        if (npc == null || npc.ObjId == 0)
            return;

        CancelNpcHandoff(npc.ObjId);

        if (notifyZone && ZoneAuthority && RelayNpcStartDespawnToZone != null)
        {
            // Keep the mirror and its id reserved until ZWRemoveNpc confirms the authority has
            // retired the unit. Releasing earlier could recycle the bc while Zone still owns it.
            PublishNpcDespawn(npc);
            return;
        }

        var objId = npc.ObjId;
        npc.Delete();
        ObjectIdManager.Instance.ReleaseId(objId);
    }

    public static void PublishAggro(
        BaseUnit unit,
        BaseUnit target,
        uint aggro,
        CastAction castAction,
        bool hostile = true)
    {
        if (!ZoneAuthority || unit?.ObjId == 0 || target?.ObjId == 0 || castAction == null)
            return;

        RelayNpcAggroToZone?.Invoke(new WorldNpcAggroRequest(
            unit.ObjId,
            target.ObjId,
            target.ObjId,
            aggro,
            hostile,
            castAction));
    }

    /// <summary>WZUnitState body for a housing unit (prereq for WZHouseState).</summary>
    public static byte[] BuildWzUnitStateBody(House house)
    {
        if (house == null)
            return null;
        var stream = new PacketStream();
        new SCUnitStatePacket(house).WriteWzBody(stream);
        return stream.GetBytes();
    }

    /// <summary>
    /// Zone only creates local NPCs (and their AI) after receiving this; ZWSpawnNpc alone
    /// is a request, not a Create. Without this reply GetAIObject stays null → no ZWUnitMovements.
    /// </summary>
    public static byte[] BuildWzNpcStateBody(
        uint bcId,
        uint spawnerId,
        byte memberIdx,
        byte partIdx,
        ushort tableIdx,
        uint groupType,
        uint groupId,
        byte groupMemberIdx)
    {
        if (!ZoneAuthority || bcId == 0)
            return null;

        if (FindUnitAcrossWorlds(bcId) is not Npc npc)
            return null;

        return BuildWzNpcStateBody(
            npc,
            new WzNpcSpawnMetadata(
                spawnerId,
                memberIdx,
                partIdx,
                tableIdx,
                NpcSpawnReasonType.Default,
                null,
                0f,
                groupType,
                groupId,
                groupMemberIdx),
            null,
            0f,
            false,
            false);
    }

    private static byte[] BuildWzNpcStateBody(
        Npc npc,
        BaseUnit creator,
        float lifeTime,
        bool despawnOnCreatorDeath,
        bool useSummonerAggroTarget)
        => BuildWzNpcStateBody(
            npc, WorldAuthoredNpcSpawn, creator, lifeTime,
            despawnOnCreatorDeath, useSummonerAggroTarget);

    private static byte[] BuildWzNpcStateBody(
        Npc npc,
        WzNpcSpawnMetadata metadata,
        BaseUnit creator,
        float lifeTime,
        bool despawnOnCreatorDeath,
        bool useSummonerAggroTarget)
    {
        var stream = new PacketStream();

        // Spawn meta header (packet+16..)
        stream.Write(metadata.SpawnerId);  // sid u32
        stream.Write(metadata.MemberIndex); // mIdx u8
        stream.Write(metadata.PartIndex);   // pIdx u8
        stream.Write(metadata.TableIndex);  // tIdx u16

        // Only union cases whose fields are verified here are emitted; ordinary spawner and GM
        // spawns use the native empty Character sentinel.
        if (creator is Character character)
        {
            stream.Write((byte)BaseUnitType.Character);
            stream.Write((ulong)character.Id);
            stream.Write(NoCreatorValue); // required Character union field "v"; no runtime model
        }
        else if (creator is Npc creatorNpc)
        {
            stream.Write((byte)BaseUnitType.Npc);
            stream.WriteBc(creatorNpc.ObjId);
            stream.Write(creatorNpc.TemplateId);
            stream.Write((ulong)creatorNpc.OwnerId);
            stream.Write(creatorNpc.UnitStateFlag);
        }
        else if (creator == null)
        {
            stream.Write((byte)BaseUnitType.Character);
            stream.Write(NoCreatorCharacterId);
            stream.Write(NoCreatorValue);
        }
        else
        {
            Logger.Warn(
                "BuildWzNpcStateBody: creator type {0} has no verified native identity union",
                creator.GetType().Name);
            return null;
        }

        stream.Write(despawnOnCreatorDeath);
        stream.Write(useSummonerAggroTarget);
        stream.Write(lifeTime);

        stream.Write((sbyte)metadata.Reason);
        switch (metadata.Reason)
        {
            case NpcSpawnReasonType.Default:
                break;
            case NpcSpawnReasonType.Fishing when metadata.SpawnAction != null:
                stream.Write(metadata.SpawnAction);
                break;
            default:
                Logger.Warn(
                    "BuildWzNpcStateBody: spawn reason {0} is missing its verified payload",
                    metadata.Reason);
                return null;
        }

        // UnitState_Serialize + buffs (no WZUnitState action tail)
        // Live unit state is World-space. Only static spawner geometry is Zone-local; converting
        // this position would desynchronize the dedicate's unit and its movement stream.
        new SCUnitStatePacket(npc).WriteWzUnitStateAndBuffs(stream);

        stream.Write(metadata.SpawningEffectTime);

        stream.Write(metadata.GroupType);
        stream.Write(metadata.GroupId);
        stream.Write(metadata.GroupMemberIndex);

        return stream.GetBytes();
    }

    /// <param name="onlyZoneId">0 = all in-world clients; otherwise only that Transform.ZoneId.</param>
    /// <param name="reason">Failure description written to the recovery log.</param>
    public static void ReturnInWorldClientsToCharacterSelect(uint onlyZoneId, string reason)
    {
        foreach (var con in GameConnectionTable.Instance.GetConnections())
        {
            if (con?.ActiveChar == null || con.State != GameState.World)
                continue;
            if (onlyZoneId != 0 && con.ActiveChar.Transform?.ZoneId != onlyZoneId)
                continue;
            try
            {
                if (!EnterWorldManager.Instance.ReturnToCharacterSelect(con, reason))
                {
                    Logger.Warn(
                        "Character-select recovery was unavailable for connection {0}; closing the session",
                        con.Id);
                    con.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Character-select recovery failed for connection {0}", con.Id);
                con.Shutdown();
            }
        }
    }

    /// <summary>
    /// WorldInstance that owns a zone. Mirrors and doodad pushes must target it rather than
    /// MainWorld: zone 260 (arche_mall_world) mirrored into main_world, so its NPCs landed
    /// nowhere near the player's own instance and region interest streamed no SCUnitState —
    /// Mirage Isle looked completely empty.
    /// </summary>
    public static Models.Game.World.WorldInstance ResolveWorldForZone(uint zoneId)
    {
        var main = WorldManager.Instance.MainWorld;
        if (zoneId == 0)
            return main;

        var template = WorldManager.Instance.GetWorldTemplateByZoneKey(zoneId);
        if (template == null)
            return main;
        if (main?.Template?.Id == template.Id)
            return main;

        return Array.Find(WorldManager.Instance.GetWorlds(), w => w.Template?.Id == template.Id);
    }

    /// <summary>
    /// Locate a mirror by bcId across every instance. UnitRegistry allocates bcIds process-wide,
    /// so a single id is unambiguous, but it may live in any world a dedicate has loaded.
    /// </summary>
    public static BaseUnit FindUnitAcrossWorlds(uint bcId)
    {
        foreach (var world in WorldManager.Instance.GetWorlds())
        {
            var unit = world?.GetBaseUnit(bcId);
            if (unit != null)
                return unit;
        }

        return null;
    }

    /// <summary>
    /// Create a display-only Game NPC so clients get SCUnitState via region interest.
    /// AI is frozen — zone owns sim. XY from ZWSpawnNpc are zone-local; convert via zone origin.
    /// </summary>
    public static bool MirrorZoneNpcSpawn(uint zoneId, uint bcId, uint templateId, float x, float y, float z, float zRot, float scale)
    {
        if (!ZoneAuthority || bcId == 0 || templateId == 0)
            return false;

        try
        {
            var world = ResolveWorldForZone(zoneId);
            if (world == null)
            {
                // Before CreateStaticInstances there is no instance to mirror into; queue and
                // let NotifyMainWorldReady replay. Afterwards a miss means the zone's world was
                // never instanced, and queueing would grow without bound.
                if (WorldManager.Instance.MainWorld == null)
                {
                    PendingZoneNpcs.Enqueue(new PendingZoneNpc(zoneId, bcId, templateId, x, y, z, zRot, scale));
                    if (PendingZoneNpcs.Count <= 3 || PendingZoneNpcs.Count % 100 == 0)
                        Logger.Warn("MirrorZoneNpcSpawn: no world instance yet — queued bc={0} tpl={1} (pending={2})", bcId, templateId, PendingZoneNpcs.Count);
                    return false;
                }

                Logger.Warn(
                    "MirrorZoneNpcSpawn: no world instance owns zoneId={0} — dropping bc={1} tpl={2}",
                    zoneId, bcId, templateId);
                return false;
            }

            // Idempotent remirror (same bc). Multi-zone MUST NOT share bcIds — UnitRegistry
            // allocates process-wide; a hit here is the same NPC re-announced, not a sibling zone.
            if (world.GetNpc(bcId) != null || world.GetBaseUnit(bcId) != null)
                return true;

            var npc = NpcManager.Instance.Create(world, bcId, templateId);
            if (npc == null)
            {
                Logger.Debug("MirrorZoneNpcSpawn: unknown template {0} (bc={1})", templateId, bcId);
                return false;
            }

            npc.IsZoneMirror = true;

            var worldPos = ZoneManager.Instance.ConvertToWorldCoordinates(zoneId, new System.Numerics.Vector3(x, y, z));
            npc.Transform.ZoneId = zoneId;
            npc.Transform.Local.SetPosition(worldPos.X, worldPos.Y, worldPos.Z, 0f, 0f, zRot);
            npc.Spawn();

            if ((bcId - 0x00F00000) <= 5 || (bcId - 0x00F00000) % 100 == 0 || bcId <= 5 || bcId % 100 == 0)
            {
                Logger.Info(
                    "Mirrored zone NPC bc={0} tpl={1} zone={2} local=({3:F1},{4:F1},{5:F1}) world=({6:F1},{7:F1},{8:F1})",
                    bcId, templateId, zoneId, x, y, z, worldPos.X, worldPos.Y, worldPos.Z);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "MirrorZoneNpcSpawn failed bc={0} tpl={1}", bcId, templateId);
            return false;
        }
    }

    /// <summary>Flush NPCs that arrived before MainWorld existed. Call after CreateStaticInstances.</summary>
    public static void NotifyMainWorldReady()
    {
        if (!ZoneAuthority)
            return;

        var flushed = 0;
        var failed = 0;
        while (PendingZoneNpcs.TryDequeue(out var p))
        {
            if (MirrorZoneNpcSpawn(p.ZoneId, p.BcId, p.TemplateId, p.X, p.Y, p.Z, p.ZRot, p.Scale))
                flushed++;
            else
                failed++;
        }

        Logger.Info("MainWorld ready — flushed pending zone NPC mirrors ok={0} fail={1}", flushed, failed);
        try
        {
            OnMainWorldReady?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "OnMainWorldReady failed");
        }
    }

    public static void MirrorZoneNpcRemove(uint bcId)
    {
        CancelNpcHandoff(bcId);
        if (!ZoneAuthority || bcId == 0)
            return;

        try
        {
            var unit = FindUnitAcrossWorlds(bcId);
            if (unit == null)
                return;

            // Delete -> Hide -> Region.RemoveFromCharacters sends SCUnitsRemoved only to
            // clients that could see this mirror and releases their mirror stream slots.
            unit.Delete();
            Logger.Debug("Removed mirrored zone NPC bc={0}", bcId);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "MirrorZoneNpcRemove failed bc={0}", bcId);
        }
    }

}
